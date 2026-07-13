using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WatchDB.Services;

/// <summary>
/// High-performance, structured log messages used by WatchDB.
/// </summary>
internal static partial class WatchDbLog
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "WatchDB skipped this request because another organization pass is already running.")]
    public static partial void OrganizationAlreadyRunning(ILogger logger);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning, Message = "WatchDB configuration is invalid: {ValidationError}")]
    public static partial void ConfigurationInvalid(ILogger logger, string validationError);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "WatchDB skipped {File} because it disappeared during the scan.")]
    public static partial void SourceFileDisappeared(ILogger logger, string file);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = "WatchDB ignored {File}: no SxxExx pattern was found in the file or its release folder.")]
    public static partial void IgnoredUnparsed(ILogger logger, string file);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Warning, Message = "WatchDB left {File} unchanged: no unambiguous TMDb match for {Title} S{Season:D2}E{Episode:D2}.")]
    public static partial void Unmatched(ILogger logger, string file, string title, int season, int episode);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Information, Message = "WatchDB would link {File} to {Target} ({Series}, score {Score}).")]
    public static partial void WouldLink(ILogger logger, string file, string target, string series, int score);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Information, Message = "WatchDB did not replace existing link or file {Target}.")]
    public static partial void TargetAlreadyPresent(ILogger logger, string target);

    [LoggerMessage(EventId = 1007, Level = LogLevel.Information, Message = "WatchDB linked {File} to {Target} ({Series}, score {Score}).")]
    public static partial void Linked(ILogger logger, string file, string target, string series, int score);

    [LoggerMessage(EventId = 1008, Level = LogLevel.Error, Message = "WatchDB could not reach TMDb while processing {File}.")]
    public static partial void TmdbUnavailable(ILogger logger, Exception exception, string file);

    [LoggerMessage(EventId = 1009, Level = LogLevel.Error, Message = "WatchDB could not access the filesystem while processing {File}.")]
    public static partial void FileSystemFailure(ILogger logger, Exception exception, string file);

    [LoggerMessage(EventId = 1010, Level = LogLevel.Error, Message = "WatchDB has insufficient access to process {File}.")]
    public static partial void AccessDenied(ILogger logger, Exception exception, string file);

    [LoggerMessage(EventId = 1011, Level = LogLevel.Error, Message = "WatchDB timed out while processing {File}.")]
    public static partial void TmdbTimeout(ILogger logger, string file);

    [LoggerMessage(EventId = 1012, Level = LogLevel.Error, Message = "WatchDB received an invalid TMDb response while processing {File}.")]
    public static partial void InvalidTmdbResponse(ILogger logger, Exception exception, string file);

    [LoggerMessage(EventId = 1013, Level = LogLevel.Information, Message = "WatchDB is disabled; no files were processed.")]
    public static partial void Disabled(ILogger logger);

    [LoggerMessage(EventId = 1014, Level = LogLevel.Information, Message = "WatchDB started its automatic post-scan organization pass.")]
    public static partial void AutomaticPostScanStarted(ILogger logger);

    [LoggerMessage(EventId = 1015, Level = LogLevel.Information, Message = "WatchDB completed: {Scanned} scanned, {Confirmed} confirmed, {Linked} linked, {Simulated} simulated, {Unmatched} unmatched, {Unparsed} unparsed, {Existing} existing, {Errors} errors.")]
    public static partial void ManualSummary(ILogger logger, int scanned, int confirmed, int linked, int simulated, int unmatched, int unparsed, int existing, int errors);

    [LoggerMessage(EventId = 1016, Level = LogLevel.Information, Message = "WatchDB post-scan pass completed: {Scanned} scanned, {Confirmed} confirmed, {Linked} linked, {Simulated} simulated, {Unmatched} unmatched, {Errors} errors.")]
    public static partial void AutomaticSummary(ILogger logger, int scanned, int confirmed, int linked, int simulated, int unmatched, int errors);
}
