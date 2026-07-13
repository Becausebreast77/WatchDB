using Jellyfin.Plugin.WatchDB.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WatchDB.Tasks;

/// <summary>
/// Jellyfin dashboard task that finds and groups orphan episodes.
/// </summary>
public sealed class OrganizeOrphanEpisodesTask : IScheduledTask
{
    private readonly ILogger<OrganizeOrphanEpisodesTask> _logger;
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrganizeOrphanEpisodesTask"/> class.
    /// </summary>
    public OrganizeOrphanEpisodesTask(ILogger<OrganizeOrphanEpisodesTask> logger, ILibraryManager libraryManager)
    {
        _logger = logger;
        _libraryManager = libraryManager;
    }

    /// <inheritdoc />
    public string Name => "WatchDB — Organiser les épisodes orphelins";

    /// <inheritdoc />
    public string Key => "WatchDBOrganizeOrphanEpisodes";

    /// <inheritdoc />
    public string Description => "Associe prudemment les épisodes isolés à TMDb et crée une arborescence Jellyfin par liens symboliques.";

    /// <inheritdoc />
    public string Category => "WatchDB";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var plugin = Plugin.Instance;
        if (plugin is null || !plugin.Configuration.Enabled)
        {
            WatchDbLog.Disabled(_logger);
            progress.Report(100);
            return;
        }

        var organizer = new OrphanEpisodeOrganizer(_logger);
        var summary = await organizer.RunAsync(plugin.Configuration, progress, cancellationToken).ConfigureAwait(false);
        if (summary.Linked > 0)
        {
            _libraryManager.QueueLibraryScan();
        }

        WatchDbLog.ManualSummary(
            _logger,
            summary.Scanned,
            summary.Confirmed,
            summary.Linked,
            summary.Simulated,
            summary.AmbiguousOrUnmatched,
            summary.Unparsed,
            summary.AlreadyPresent,
            summary.Errors);
    }
}
