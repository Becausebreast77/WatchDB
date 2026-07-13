using Jellyfin.Plugin.WatchDB.Services;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WatchDB.Tasks;

/// <summary>
/// Runs WatchDB after Jellyfin finishes one of its normal library scans.
/// </summary>
public sealed class OrganizeAfterLibraryScanTask : ILibraryPostScanTask
{
    private readonly ILogger<OrganizeAfterLibraryScanTask> _logger;
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrganizeAfterLibraryScanTask"/> class.
    /// </summary>
    public OrganizeAfterLibraryScanTask(ILogger<OrganizeAfterLibraryScanTask> logger, ILibraryManager libraryManager)
    {
        _logger = logger;
        _libraryManager = libraryManager;
    }

    /// <inheritdoc />
    public async Task Run(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var plugin = Plugin.Instance;
        if (plugin is null || !plugin.Configuration.Enabled || !plugin.Configuration.OrganizeAfterLibraryScan)
        {
            progress.Report(100);
            return;
        }

        WatchDbLog.AutomaticPostScanStarted(_logger);
        var organizer = new OrphanEpisodeOrganizer(_logger);
        var summary = await organizer.RunAsync(plugin.Configuration, progress, cancellationToken).ConfigureAwait(false);
        if (summary.Linked > 0)
        {
            _libraryManager.QueueLibraryScan();
        }

        WatchDbLog.AutomaticSummary(
            _logger,
            summary.Scanned,
            summary.Confirmed,
            summary.Linked,
            summary.Simulated,
            summary.AmbiguousOrUnmatched,
            summary.Errors);
    }
}
