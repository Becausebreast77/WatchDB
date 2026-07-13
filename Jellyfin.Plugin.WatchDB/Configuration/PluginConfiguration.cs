using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.WatchDB.Configuration;

/// <summary>
/// Specifies how WatchDB handles a confirmed match.
/// </summary>
public enum OrganizerMode
{
    /// <summary>
    /// Do not touch the filesystem; only write a detailed task log.
    /// </summary>
    DryRun,

    /// <summary>
    /// Create a canonical Jellyfin tree with symbolic links to the original files.
    /// </summary>
    CreateSymbolicLinks,
}

/// <summary>
/// Persistent plugin settings.
/// </summary>
public sealed class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether the scheduled task may process files.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether WatchDB runs after Jellyfin completes a library scan.
    /// </summary>
    public bool OrganizeAfterLibraryScan { get; set; } = true;

    /// <summary>
    /// Gets or sets a newline-separated list of folders which may contain orphan episodes.
    /// </summary>
    public string SourceDirectories { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the root of the clean Jellyfin-visible library.
    /// </summary>
    public string DestinationDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the TMDb API Read Access Token (v4 bearer token).
    /// </summary>
    public string TmdbReadAccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the environment variable which takes precedence over the stored TMDb token.
    /// </summary>
    public string TmdbReadAccessTokenEnvironmentVariable { get; set; } = "WATCHDB_TMDB_TOKEN";

    /// <summary>
    /// Gets or sets the preferred language sent to TMDb.
    /// </summary>
    public string PreferredLanguage { get; set; } = "fr-FR";

    /// <summary>
    /// Gets or sets a value indicating whether WatchDB may look inside release folders.
    /// </summary>
    public bool ScanRecursively { get; set; } = true;

    /// <summary>
    /// Gets or sets the number of parent folders whose name can be parsed as a fallback.
    /// </summary>
    public int ParentFolderDepth { get; set; } = 2;

    /// <summary>
    /// Gets or sets the maximum number of TMDb series candidates evaluated for one file.
    /// </summary>
    public int CandidateLimit { get; set; } = 5;

    /// <summary>
    /// Gets or sets the largest number of source files accepted in a single task run.
    /// </summary>
    public int MaxFilesPerRun { get; set; } = 500;

    /// <summary>
    /// Gets or sets the minimum score needed before a match can be handled automatically.
    /// </summary>
    public int MinimumConfidence { get; set; } = 115;

    /// <summary>
    /// Gets or sets the score gap required between the best and second best candidates.
    /// </summary>
    public int MinimumScoreGap { get; set; } = 10;

    /// <summary>
    /// Gets or sets how a confirmed match is applied.
    /// </summary>
    public OrganizerMode Mode { get; set; } = OrganizerMode.DryRun;
}
