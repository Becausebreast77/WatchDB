using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WatchDB.Services;

/// <summary>
/// Reattaches episodes from duplicate Jellyfin series cards which have the same TMDb id.
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
    /// Groups duplicate series cards that Jellyfin's metadata provider has already confirmed as the same TMDb series.
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
}
