using Jellyfin.Plugin.WatchDB.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WatchDB.Services;

/// <summary>
/// Matches orphan files to TMDb and creates a safe, Jellyfin-compatible link tree.
/// </summary>
public sealed class OrphanEpisodeOrganizer
{
    private static readonly SemaphoreSlim RunGate = new(1, 1);
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".avi", ".m4v", ".mkv", ".mov", ".mp4", ".mpeg", ".mpg", ".ts", ".webm", ".wmv",
    };

    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrphanEpisodeOrganizer"/> class.
    /// </summary>
    public OrphanEpisodeOrganizer(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Executes a conservative organization pass.
    /// </summary>
    public async Task<OrganizerSummary> RunAsync(PluginConfiguration configuration, IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (!await RunGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            WatchDbLog.OrganizationAlreadyRunning(_logger);
            return OrganizerSummary.AlreadyRunning;
        }

        try
        {
            return await RunCoreAsync(configuration, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            RunGate.Release();
        }
    }

    private async Task<OrganizerSummary> RunCoreAsync(PluginConfiguration configuration, IProgress<double> progress, CancellationToken cancellationToken)
    {

        if (!OrganizerConfigurationValidator.TryValidate(configuration, out var validated, out var validationError) || validated is null)
        {
            WatchDbLog.ConfigurationInvalid(_logger, validationError!);
            return OrganizerSummary.InvalidConfiguration;
        }

        var sourceDirectories = validated.SourceDirectories;
        var destinationRoot = validated.DestinationDirectory;
        if (configuration.Mode == OrganizerMode.CreateSymbolicLinks)
        {
            EnsureSafeDirectory(destinationRoot);
        }

        using var tmdb = new TmdbClient(validated.TmdbReadAccessToken);
        var maxFiles = Math.Clamp(configuration.MaxFilesPerRun, 1, 5000);
        var files = EnumerateVideoFiles(sourceDirectories, destinationRoot, configuration.ScanRecursively, maxFiles);
        var summary = new OrganizerSummary(files.Length);

        for (var index = 0; index < files.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = files[index];
            progress.Report((double)index / Math.Max(files.Length, 1) * 100);

            try
            {
                if (!File.Exists(source))
                {
                    summary.Errors++;
                    WatchDbLog.SourceFileDisappeared(_logger, source);
                    continue;
                }

                var sourceRoot = sourceDirectories.First(root => OrganizerConfigurationValidator.IsWithin(source, root));
                if (!EpisodeFilenameParser.TryParse(source, sourceRoot, configuration.ParentFolderDepth, out var parsed) || parsed is null)
                {
                    summary.Unparsed++;
                    WatchDbLog.IgnoredUnparsed(_logger, source);
                    continue;
                }

                var match = await FindMatchAsync(tmdb, parsed, configuration, cancellationToken).ConfigureAwait(false);
                if (match is null)
                {
                    summary.AmbiguousOrUnmatched++;
                    WatchDbLog.Unmatched(_logger, source, parsed.SeriesTitle, parsed.SeasonNumber, parsed.EpisodeNumber);
                    continue;
                }

                summary.Confirmed++;
                var targetPath = BuildTargetPath(destinationRoot, parsed, match.Value.Series);
                if (configuration.Mode == OrganizerMode.DryRun)
                {
                    summary.Simulated++;
                    WatchDbLog.WouldLink(_logger, source, targetPath, match.Value.Series.Name, match.Value.Score);
                    continue;
                }

                EnsureSafeDirectory(Path.GetDirectoryName(targetPath)!);
                if (PathEntryExists(targetPath))
                {
                    summary.AlreadyPresent++;
                    WatchDbLog.TargetAlreadyPresent(_logger, targetPath);
                    continue;
                }

                File.CreateSymbolicLink(targetPath, source);
                summary.Linked++;
                WatchDbLog.Linked(_logger, source, targetPath, match.Value.Series.Name, match.Value.Score);
            }
            catch (HttpRequestException exception)
            {
                summary.Errors++;
                WatchDbLog.TmdbUnavailable(_logger, exception, source);
            }
            catch (IOException exception)
            {
                summary.Errors++;
                WatchDbLog.FileSystemFailure(_logger, exception, source);
            }
            catch (UnauthorizedAccessException exception)
            {
                summary.Errors++;
                WatchDbLog.AccessDenied(_logger, exception, source);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                summary.Errors++;
                WatchDbLog.TmdbTimeout(_logger, source);
            }
            catch (System.Text.Json.JsonException exception)
            {
                summary.Errors++;
                WatchDbLog.InvalidTmdbResponse(_logger, exception, source);
            }
        }

        progress.Report(100);
        return summary;
    }

    private static async Task<ConfirmedMatch?> FindMatchAsync(TmdbClient tmdb, ParsedEpisode parsed, PluginConfiguration configuration, CancellationToken cancellationToken)
    {
        var candidates = await tmdb
            .SearchSeriesAsync(parsed.SeriesTitle, configuration.PreferredLanguage, configuration.CandidateLimit, cancellationToken)
            .ConfigureAwait(false);

        var validated = new List<ConfirmedMatch>();
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var episode = await tmdb
                .GetEpisodeAsync(candidate.Id, parsed.SeasonNumber, parsed.EpisodeNumber, configuration.PreferredLanguage, cancellationToken)
                .ConfigureAwait(false);
            if (episode is null)
            {
                continue;
            }

            var score = Score(parsed, candidate) + 30;
            validated.Add(new ConfirmedMatch(candidate, score));
        }

        var ordered = validated.OrderByDescending(match => match.Score).ToArray();
        if (ordered.Length == 0 || ordered[0].Score < Math.Clamp(configuration.MinimumConfidence, 0, 140))
        {
            return null;
        }

        if (ordered.Length > 1 && ordered[0].Score - ordered[1].Score < Math.Clamp(configuration.MinimumScoreGap, 0, 40))
        {
            return null;
        }

        return ordered[0];
    }

    private static int Score(ParsedEpisode parsed, TmdbSeries series)
    {
        var fileTitle = EpisodeFilenameParser.NormalizeTitle(parsed.SeriesTitle);
        var localizedTitle = EpisodeFilenameParser.NormalizeTitle(series.Name);
        var originalTitle = EpisodeFilenameParser.NormalizeTitle(series.OriginalName);
        var titleScore = Math.Max(ScoreTitle(fileTitle, localizedTitle), ScoreTitle(fileTitle, originalTitle));

        if (parsed.Year.HasValue && parsed.Year == series.Year)
        {
            titleScore += 10;
        }

        return titleScore;
    }

    private static int ScoreTitle(string source, string candidate)
    {
        if (string.Equals(source, candidate, StringComparison.Ordinal))
        {
            return 100;
        }

        var sourceTokens = source.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var candidateTokens = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        if (sourceTokens.Count == 0 || candidateTokens.Count == 0)
        {
            return 0;
        }

        var overlap = sourceTokens.Intersect(candidateTokens, StringComparer.Ordinal).Count();
        return (int)Math.Round(85d * overlap / sourceTokens.Union(candidateTokens, StringComparer.Ordinal).Count(), MidpointRounding.AwayFromZero);
    }

    private static string BuildTargetPath(string destinationRoot, ParsedEpisode parsed, TmdbSeries series)
    {
        var displayTitle = string.IsNullOrWhiteSpace(series.Name) ? series.OriginalName : series.Name;
        var yearSuffix = series.Year.HasValue ? $" ({series.Year.Value})" : string.Empty;
        var seriesDirectory = SanitizePathSegment($"{displayTitle}{yearSuffix} [tmdbid-{series.Id}]");
        var seasonDirectory = $"Season {parsed.SeasonNumber:D2}";
        var fileName = SanitizeFileName(Path.GetFileName(parsed.FilePath));

        return Path.Combine(destinationRoot, seriesDirectory, seasonDirectory, fileName);
    }

    private static string SanitizePathSegment(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalidCharacters.Contains(character) ? ' ' : character).ToArray());
        sanitized = string.Join(' ', sanitized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return sanitized.Trim(' ', '.');
    }

    private static string SanitizeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalidCharacters.Contains(character) || char.IsControl(character) ? '_' : character).ToArray());
        sanitized = sanitized.Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            throw new IOException("The source file has no safe destination file name.");
        }

        return sanitized;
    }

    private static string[] EnumerateVideoFiles(IEnumerable<string> sourceDirectories, string destinationRoot, bool recursive, int maximum)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(maximum);
        foreach (var source in sourceDirectories)
        {
            if (result.Count >= maximum)
            {
                break;
            }

            if (!Directory.Exists(source))
            {
                continue;
            }

            var pendingDirectories = new Stack<string>();
            pendingDirectories.Push(source);
            while (pendingDirectories.Count > 0 && result.Count < maximum)
            {
                var directory = pendingDirectories.Pop();
                IEnumerable<string> entries;
                try
                {
                    entries = Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly).ToArray();
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    if (result.Count >= maximum)
                    {
                        break;
                    }

                    FileAttributes attributes;
                    try
                    {
                        attributes = File.GetAttributes(entry);
                    }
                    catch (IOException)
                    {
                        continue;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        continue;
                    }

                    if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.System) != 0)
                    {
                        continue;
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (recursive)
                        {
                            pendingDirectories.Push(entry);
                        }

                        continue;
                    }

                    var fullPath = Path.GetFullPath(entry);
                    if (!OrganizerConfigurationValidator.IsWithin(fullPath, destinationRoot)
                        && VideoExtensions.Contains(Path.GetExtension(fullPath))
                        && seen.Add(fullPath))
                    {
                        result.Add(fullPath);
                    }
                }
            }
        }

        return result.ToArray();
    }

    private static void EnsureSafeDirectory(string directory)
    {
        var fullPath = Path.GetFullPath(directory);
        var existing = fullPath;
        while (!Directory.Exists(existing))
        {
            if (File.Exists(existing))
            {
                throw new IOException($"A file blocks the required destination directory {fullPath}.");
            }

            var parent = Directory.GetParent(existing);
            if (parent is null)
            {
                throw new IOException($"Cannot locate a safe parent directory for {fullPath}.");
            }

            existing = parent.FullName;
        }

        if (OrganizerConfigurationValidator.IsReparsePoint(existing))
        {
            throw new IOException($"WatchDB refuses to create output beneath the symbolic link or reparse point {existing}.");
        }

        Directory.CreateDirectory(fullPath);
        if (OrganizerConfigurationValidator.IsReparsePoint(fullPath))
        {
            throw new IOException($"WatchDB refuses to use a destination symbolic link or reparse point {fullPath}.");
        }
    }

    private static bool PathEntryExists(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private readonly record struct ConfirmedMatch(TmdbSeries Series, int Score);
}

/// <summary>
/// Summarizes one organization pass.
/// </summary>
public sealed class OrganizerSummary
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OrganizerSummary"/> class.
    /// </summary>
    public OrganizerSummary(int scanned) => Scanned = scanned;

    /// <summary>
    /// Gets a summary returned for missing mandatory settings.
    /// </summary>
    public static OrganizerSummary InvalidConfiguration { get; } = new(0);

    /// <summary>
    /// Gets a summary returned when a previous automatic or manual pass is still active.
    /// </summary>
    public static OrganizerSummary AlreadyRunning { get; } = new(0);

    /// <summary>Gets the number of video files considered.</summary>
    public int Scanned { get; }

    /// <summary>Gets or sets the number of files without an episode pattern.</summary>
    public int Unparsed { get; set; }

    /// <summary>Gets or sets the number of files that could not be safely matched.</summary>
    public int AmbiguousOrUnmatched { get; set; }

    /// <summary>Gets or sets the number of matches confirmed by TMDb.</summary>
    public int Confirmed { get; set; }

    /// <summary>Gets or sets the number of simulated links.</summary>
    public int Simulated { get; set; }

    /// <summary>Gets or sets the number of links created.</summary>
    public int Linked { get; set; }

    /// <summary>Gets or sets the number of existing target paths retained untouched.</summary>
    public int AlreadyPresent { get; set; }

    /// <summary>Gets or sets the number of errors.</summary>
    public int Errors { get; set; }
}
