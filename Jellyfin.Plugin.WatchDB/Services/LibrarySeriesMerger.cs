using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WatchDB.Services;

/// <summary>
/// Reattaches episodes from duplicate Jellyfin series cards and repairs orphan episodes
/// from their release filenames.
/// </summary>
public sealed class LibrarySeriesMerger
{
    private static readonly SemaphoreSlim RunGate = new(1, 1);
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger _logger;
    private readonly IProviderManager _providerManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibrarySeriesMerger"/> class.
    /// </summary>
    public LibrarySeriesMerger(ILibraryManager libraryManager, IProviderManager providerManager, ILogger logger)
    {
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _logger = logger;
    }

    /// <summary>
    /// Groups duplicate series cards that Jellyfin's metadata provider has already confirmed as the same TMDb series
    /// and repairs invalid season links when the raw filename unambiguously identifies an existing series.
    /// </summary>
    public async Task<LibraryMergeSummary> RunAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (!await RunGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            WatchDbLog.OrganizationAlreadyRunning(_logger);
            return LibraryMergeSummary.AlreadyRunning;
        }

        try
        {
            var allSeries = _libraryManager.RootFolder
                .GetRecursiveChildren()
                .OfType<Series>()
                .Where(IsInTvLibrary)
                .ToArray();
            var allEpisodes = _libraryManager.RootFolder
                .GetRecursiveChildren()
                .OfType<Episode>()
                .ToArray();

            foreach (var seriesWithoutId in allSeries.Where(item => GetTmdbId(item) is null))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await TryIdentifySeriesAsync(seriesWithoutId, cancellationToken).ConfigureAwait(false);
            }

            var series = allSeries
                .Select(item => new IdentifiedSeries(item, GetTmdbId(item)))
                .Where(item => item.TmdbId is not null)
                .ToArray();

            var duplicates = series
                .GroupBy(item => item.TmdbId!, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .ToArray();
            var summary = new LibraryMergeSummary(series.Length, duplicates.Length);

            for (var index = 0; index < duplicates.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress.Report((double)index / Math.Max(duplicates.Length, 1) * 100);

                var group = duplicates[index];
                var ordered = group
                    .OrderByDescending(item => item.Series.GetRecursiveChildren().OfType<Episode>().Count())
                    .ThenBy(item => item.Series.Name, StringComparer.Ordinal)
                    .ToArray();
                var primary = ordered[0].Series;
                var seasons = primary.GetRecursiveChildren()
                    .OfType<Season>()
                    .Where(season => season.IndexNumber.HasValue)
                    .GroupBy(season => season.IndexNumber!.Value)
                    .ToDictionary(grouping => grouping.Key, grouping => grouping.First());

                foreach (var duplicate in ordered.Skip(1))
                {
                    var duplicateEpisodes = allEpisodes
                        .Where(episode => episode.SeriesId == duplicate.Series.Id)
                        .ToArray();
                    foreach (var episode in duplicateEpisodes)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        episode.SeriesId = primary.Id;
                        episode.SeriesName = primary.Name;
                        episode.SeriesPresentationUniqueKey = primary.PresentationUniqueKey;

                        if (episode.ParentIndexNumber.HasValue && seasons.TryGetValue(episode.ParentIndexNumber.Value, out var season))
                        {
                            episode.SeasonId = season.Id;
                            episode.SeasonName = season.Name;
                            await _libraryManager.UpdateItemAsync(episode, season, ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            await _libraryManager.UpdateItemAsync(episode, primary, ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
                        }

                        summary.EpisodesMerged++;
                    }

                    if (duplicateEpisodes.Length > 0)
                    {
                        summary.SeriesCardsMerged++;
                        WatchDbLog.MergedDuplicateSeries(_logger, duplicate.Series.Name, primary.Name, group.Key);
                    }
                }
            }

            await RepairOrphanEpisodesAsync(allSeries, allEpisodes, summary, cancellationToken).ConfigureAwait(false);

            progress.Report(100);
            return summary;
        }
        finally
        {
            RunGate.Release();
        }
    }

    private bool IsInTvLibrary(Series series)
    {
        return string.Equals(_libraryManager.GetInheritedContentType(series)?.ToString(), "tvshows", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetTmdbId(Series series)
    {
        return series.ProviderIds.TryGetValue("Tmdb", out var tmdbId) && !string.IsNullOrWhiteSpace(tmdbId)
            ? tmdbId
            : null;
    }

    private async Task RepairOrphanEpisodesAsync(
        IReadOnlyCollection<Series> allSeries,
        IReadOnlyCollection<Episode> allEpisodes,
        LibraryMergeSummary summary,
        CancellationToken cancellationToken)
    {
        var seasonIds = allSeries
            .SelectMany(series => series.GetRecursiveChildren().OfType<Season>())
            .Select(season => season.Id)
            .ToHashSet();
        var lookupCache = new Dictionary<string, Series?>(StringComparer.Ordinal);

        foreach (var episode in allEpisodes.Where(episode => IsOrphanedSeason(episode, seasonIds)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            summary.OrphanEpisodesFound++;

            if (!TryParseEpisodeFilename(episode, out var parsed))
            {
                WatchDbLog.OrphanEpisodeIgnored(_logger, episode.Name, episode.Path ?? string.Empty);
                continue;
            }

            summary.OrphanEpisodesParsed++;
            var target = await ResolveCanonicalSeriesAsync(parsed, allSeries, lookupCache, cancellationToken).ConfigureAwait(false);
            if (target is null)
            {
                WatchDbLog.OrphanEpisodeTargetNotFound(_logger, episode.Name, parsed.SeriesTitle, parsed.SeasonNumber, parsed.EpisodeNumber);
                continue;
            }

            var season = target
                .GetRecursiveChildren()
                .OfType<Season>()
                .FirstOrDefault(item => item.IndexNumber == parsed.SeasonNumber);
            if (season is null)
            {
                // A Season item is owned by Jellyfin's library scanner. Creating one here would leave the
                // scanner and metadata provider with conflicting ownership, so wait for its normal scan.
                WatchDbLog.OrphanEpisodeSeasonNotFound(_logger, episode.Name, target.Name, parsed.SeasonNumber);
                continue;
            }

            episode.SeriesId = target.Id;
            episode.SeriesName = target.Name;
            episode.SeriesPresentationUniqueKey = target.PresentationUniqueKey;
            episode.SeasonId = season.Id;
            episode.SeasonName = season.Name;
            episode.ParentIndexNumber = parsed.SeasonNumber;
            episode.IndexNumber = parsed.EpisodeNumber;
            await _libraryManager.UpdateItemAsync(episode, season, ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);

            summary.EpisodesMerged++;
            summary.OrphanEpisodesReattached++;
            WatchDbLog.OrphanEpisodeReattached(_logger, episode.Name, parsed.SeriesTitle, parsed.SeasonNumber, parsed.EpisodeNumber, target.Name);
        }
    }

    private static bool IsOrphanedSeason(Episode episode, HashSet<Guid> seasonIds)
    {
        return episode.SeasonId == Guid.Empty || !seasonIds.Contains(episode.SeasonId);
    }

    private static bool TryParseEpisodeFilename(Episode episode, out ParsedEpisode parsed)
    {
        parsed = null!;
        if (string.IsNullOrWhiteSpace(episode.Path))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(episode.Path);
        return !string.IsNullOrWhiteSpace(directory)
            && EpisodeFilenameParser.TryParse(episode.Path, directory, 0, out var parsedEpisode)
            && (parsed = parsedEpisode!) is not null;
    }

    private async Task<Series?> ResolveCanonicalSeriesAsync(
        ParsedEpisode parsed,
        IReadOnlyCollection<Series> allSeries,
        Dictionary<string, Series?> lookupCache,
        CancellationToken cancellationToken)
    {
        var normalizedTitle = EpisodeFilenameParser.NormalizeTitle(parsed.SeriesTitle);
        if (lookupCache.TryGetValue(normalizedTitle, out var cached))
        {
            return cached;
        }

        var localMatches = allSeries
            .Where(series => string.Equals(
                EpisodeFilenameParser.NormalizeTitle(series.Name),
                normalizedTitle,
                StringComparison.Ordinal))
            .ToArray();
        if (localMatches.Length == 1)
        {
            lookupCache[normalizedTitle] = localMatches[0];
            return localMatches[0];
        }

        var query = new RemoteSearchQuery<SeriesInfo>
        {
            SearchInfo = new SeriesInfo
            {
                Name = parsed.SeriesTitle,
                Year = parsed.Year,
            },
        };
        var tmdbIds = (await _providerManager
            .GetRemoteSearchResults<Series, SeriesInfo>(query, cancellationToken)
            .ConfigureAwait(false))
            .Where(candidate => candidate.ProviderIds.TryGetValue("Tmdb", out _))
            .Where(candidate => string.Equals(
                EpisodeFilenameParser.NormalizeTitle(candidate.Name),
                normalizedTitle,
                StringComparison.Ordinal))
            .Select(candidate => candidate.ProviderIds["Tmdb"])
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToArray();

        var target = tmdbIds.Length == 1
            ? allSeries.SingleOrDefault(series => string.Equals(GetTmdbId(series), tmdbIds[0], StringComparison.Ordinal))
            : null;
        lookupCache[normalizedTitle] = target;
        return target;
    }

    private async Task TryIdentifySeriesAsync(Series series, CancellationToken cancellationToken)
    {
        var searchTitle = GetSearchTitle(series);
        if (string.IsNullOrWhiteSpace(searchTitle))
        {
            return;
        }

        var query = new RemoteSearchQuery<SeriesInfo>
        {
            SearchInfo = new SeriesInfo
            {
                Name = searchTitle,
            },
        };
        var candidates = (await _providerManager
            .GetRemoteSearchResults<Series, SeriesInfo>(query, cancellationToken)
            .ConfigureAwait(false))
            .Where(candidate => candidate.ProviderIds.TryGetValue("Tmdb", out _))
            .Where(candidate => string.Equals(
                EpisodeFilenameParser.NormalizeTitle(candidate.Name),
                EpisodeFilenameParser.NormalizeTitle(searchTitle),
                StringComparison.Ordinal))
            .GroupBy(candidate => candidate.ProviderIds["Tmdb"], StringComparer.Ordinal)
            .Select(group => group.First())
            .Take(2)
            .ToArray();

        if (candidates.Length != 1 || !candidates[0].ProviderIds.TryGetValue("Tmdb", out var tmdbId))
        {
            return;
        }

        series.ProviderIds["Tmdb"] = tmdbId;
        await _providerManager.SaveMetadataAsync(series, ItemUpdateType.MetadataEdit).ConfigureAwait(false);
        WatchDbLog.IdentifiedSeries(_logger, series.Name, tmdbId);
    }

    private static string GetSearchTitle(Series series)
    {
        if (!string.IsNullOrWhiteSpace(series.Path))
        {
            var parent = Path.GetDirectoryName(series.Path);
            if (!string.IsNullOrWhiteSpace(parent)
                && EpisodeFilenameParser.TryParse(series.Path, parent, 0, out var parsed)
                && parsed is not null)
            {
                return parsed.SeriesTitle;
            }
        }

        return series.Name;
    }

    private sealed record IdentifiedSeries(Series Series, string? TmdbId);
}

/// <summary>
/// Summarizes a logical merge pass.
/// </summary>
public sealed class LibraryMergeSummary
{
    /// <summary>Initializes a new instance of the <see cref="LibraryMergeSummary"/> class.</summary>
    public LibraryMergeSummary(int identifiedSeries, int duplicateGroups)
    {
        IdentifiedSeries = identifiedSeries;
        DuplicateGroups = duplicateGroups;
    }

    /// <summary>Gets a summary returned when another pass is in progress.</summary>
    public static LibraryMergeSummary AlreadyRunning { get; } = new(0, 0);

    /// <summary>Gets the number of identified series considered.</summary>
    public int IdentifiedSeries { get; }

    /// <summary>Gets the number of duplicate TMDb groups found.</summary>
    public int DuplicateGroups { get; }

    /// <summary>Gets or sets the number of duplicate series cards merged.</summary>
    public int SeriesCardsMerged { get; set; }

    /// <summary>Gets or sets the number of episodes reattached.</summary>
    public int EpisodesMerged { get; set; }

    /// <summary>Gets or sets the number of episodes whose SeasonId does not point to a known season.</summary>
    public int OrphanEpisodesFound { get; set; }

    /// <summary>Gets or sets the number of orphan episodes for which a release filename was parsed.</summary>
    public int OrphanEpisodesParsed { get; set; }

    /// <summary>Gets or sets the number of orphan episodes reattached through a parsed release filename.</summary>
    public int OrphanEpisodesReattached { get; set; }
}
