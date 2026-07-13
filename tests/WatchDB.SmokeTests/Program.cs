using Jellyfin.Plugin.WatchDB.Services;
using Jellyfin.Plugin.WatchDB.Configuration;

AssertEpisode(
    "/inbox/Rooster - S01E01 - Épisode 1 2160p.DV.HDR.x265-Amen.mkv",
    "/inbox",
    0,
    "Rooster",
    1,
    1);

AssertEpisode(
    "/inbox/Georgie.and.Mandys.First.Marriage.S02E09.1080p.x265-ELiTE.mkv",
    "/inbox",
    0,
    "Georgie and Mandys First Marriage",
    2,
    9);

AssertEpisode(
    "/inbox/Georgie.and.Mandys.First.Marriage.S02E10.1080p.x265-ELiTE/release.mkv",
    "/inbox",
    1,
    "Georgie and Mandys First Marriage",
    2,
    10);

AssertEpisode(
    "/media/Series/Cape.Fear.S01E01.MULTi.1080p.WEB.H264-TyHD.mkv",
    "/media/Series",
    0,
    "Cape Fear",
    1,
    1);

Assert(
    EpisodeFilenameParser.NormalizeTitle("Georgie.and.Mandys.First.Marriage")
    == EpisodeFilenameParser.NormalizeTitle("Georgie & Mandy's First Marriage"),
    "English punctuation variants should normalize to the same series title.");

var testRoot = Path.Combine(Path.GetTempPath(), $"watchdb-smoke-{Guid.NewGuid():N}");
var sourceDirectory = Path.Combine(testRoot, "source");
var destinationDirectory = Path.Combine(testRoot, "destination");
Directory.CreateDirectory(sourceDirectory);
try
{
    var validConfiguration = new PluginConfiguration
    {
        SourceDirectories = sourceDirectory,
        DestinationDirectory = destinationDirectory,
        TmdbReadAccessToken = new string('a', 24),
    };
    Assert(
        OrganizerConfigurationValidator.TryValidate(validConfiguration, out var validated, out _)
        && validated is not null,
        "Separate, absolute source and destination directories should be accepted.");

    validConfiguration.DestinationDirectory = Path.Combine(sourceDirectory, "output");
    Assert(
        !OrganizerConfigurationValidator.TryValidate(validConfiguration, out _, out _),
        "A destination nested inside a source must be rejected.");
}
finally
{
    Directory.Delete(testRoot, recursive: true);
}

Console.WriteLine("WatchDB smoke tests passed.");

static void AssertEpisode(string path, string root, int parentFolderDepth, string title, int season, int episode)
{
    if (!EpisodeFilenameParser.TryParse(path, root, parentFolderDepth, out var parsed) || parsed is null)
    {
        throw new InvalidOperationException($"Expected a parsed episode for {path}.");
    }

    Assert(parsed.SeriesTitle == title, $"Expected title {title}; got {parsed.SeriesTitle}.");
    Assert(parsed.SeasonNumber == season, $"Expected season {season}; got {parsed.SeasonNumber}.");
    Assert(parsed.EpisodeNumber == episode, $"Expected episode {episode}; got {parsed.EpisodeNumber}.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
