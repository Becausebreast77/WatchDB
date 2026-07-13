using Jellyfin.Plugin.WatchDB.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
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
    private readonly IProviderManager _providerManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrganizeOrphanEpisodesTask"/> class.
    /// </summary>
    public OrganizeOrphanEpisodesTask(ILogger<OrganizeOrphanEpisodesTask> logger, ILibraryManager libraryManager, IProviderManager providerManager)
    {
        _logger = logger;
        _libraryManager = libraryManager;
        _providerManager = providerManager;
    }

    /// <inheritdoc />
    public string Name => "WatchDB — Réparer les séries fragmentées";

    /// <inheritdoc />
    public string Key => "WatchDBOrganizeOrphanEpisodes";

    /// <inheritdoc />
    public string Description => "Regroupe automatiquement les cartes de séries que Jellyfin a déjà confirmées comme identiques via TMDb.";

    /// <inheritdoc />
    public string Category => "WatchDB";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            progress.Report(100);
            return;
        }

        var organizer = new LibrarySeriesMerger(_libraryManager, _providerManager, _logger);
        var summary = await organizer.RunAsync(progress, cancellationToken).ConfigureAwait(false);
        WatchDbLog.LibraryMergeSummary(
            _logger,
            summary.IdentifiedSeries,
            summary.DuplicateGroups,
            summary.SeriesCardsMerged,
            summary.EpisodesMerged,
            summary.OrphanEpisodesFound,
            summary.OrphanEpisodesParsed,
            summary.OrphanEpisodesReattached);
    }
}
