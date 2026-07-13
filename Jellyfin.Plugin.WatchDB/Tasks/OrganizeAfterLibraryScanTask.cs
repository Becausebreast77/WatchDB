using Jellyfin.Plugin.WatchDB.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WatchDB.Tasks;

/// <summary>
/// Runs WatchDB after Jellyfin finishes one of its normal library scans.
/// </summary>
public sealed class OrganizeAfterLibraryScanTask : ILibraryPostScanTask
{
    private readonly ILogger<OrganizeAfterLibraryScanTask> _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrganizeAfterLibraryScanTask"/> class.
    /// </summary>
    public OrganizeAfterLibraryScanTask(ILogger<OrganizeAfterLibraryScanTask> logger, ILibraryManager libraryManager, IProviderManager providerManager)
    {
        _logger = logger;
        _libraryManager = libraryManager;
        _providerManager = providerManager;
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
        var organizer = new LibrarySeriesMerger(_libraryManager, _providerManager, _logger);
        var summary = await organizer.RunAsync(progress, cancellationToken).ConfigureAwait(false);
        WatchDbLog.LibraryMergeSummary(
            _logger,
            summary.IdentifiedSeries,
            summary.DuplicateGroups,
            summary.SeriesCardsMerged,
            summary.EpisodesMerged);
    }
}
