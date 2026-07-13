using System.Text.RegularExpressions;
using Jellyfin.Plugin.WatchDB.Configuration;

namespace Jellyfin.Plugin.WatchDB.Services;

/// <summary>
/// Validates filesystem and network-related configuration before a task can touch media files.
/// </summary>
public static partial class OrganizerConfigurationValidator
{
    /// <summary>
    /// Validates the configuration and resolves every configured directory to an absolute path.
    /// </summary>
    public static bool TryValidate(PluginConfiguration configuration, out ValidatedOrganizerConfiguration? validated, out string error)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (string.IsNullOrWhiteSpace(configuration.TmdbReadAccessTokenEnvironmentVariable)
            || !EnvironmentVariableRegex().IsMatch(configuration.TmdbReadAccessTokenEnvironmentVariable))
        {
            validated = null;
            error = "The TMDb environment variable name has an invalid format.";
            return false;
        }

        var tmdbToken = Environment.GetEnvironmentVariable(configuration.TmdbReadAccessTokenEnvironmentVariable)?.Trim();
        tmdbToken = string.IsNullOrWhiteSpace(tmdbToken) ? configuration.TmdbReadAccessToken.Trim() : tmdbToken;
        if (string.IsNullOrWhiteSpace(tmdbToken) || !TmdbTokenRegex().IsMatch(tmdbToken))
        {
            validated = null;
            error = "The TMDb Read Access Token is absent or has an invalid format.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(configuration.PreferredLanguage) || !LanguageRegex().IsMatch(configuration.PreferredLanguage))
        {
            validated = null;
            error = "The TMDb language must use an IETF language tag such as fr-FR or en-US.";
            return false;
        }

        try
        {
            var sources = configuration.SourceDirectories
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (sources.Length == 0)
            {
                validated = null;
                error = "At least one source directory is required.";
                return false;
            }

            if (sources.Any(source => !Directory.Exists(source)))
            {
                validated = null;
                error = "Every configured source directory must exist and be mounted before WatchDB runs.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(configuration.DestinationDirectory))
            {
                validated = null;
                error = "A destination directory is required.";
                return false;
            }

            var destination = Path.GetFullPath(configuration.DestinationDirectory);
            if (Directory.Exists(destination) && IsReparsePoint(destination))
            {
                validated = null;
                error = "The destination directory must not itself be a symbolic link or reparse point.";
                return false;
            }

            if (sources.Any(source => IsWithin(destination, source) || IsWithin(source, destination)))
            {
                validated = null;
                error = "Source and destination directories must not overlap.";
                return false;
            }

            validated = new ValidatedOrganizerConfiguration(sources, destination, tmdbToken);
            error = string.Empty;
            return true;
        }
        catch (ArgumentException)
        {
            validated = null;
            error = "One configured path is not valid for this server.";
            return false;
        }
        catch (NotSupportedException)
        {
            validated = null;
            error = "One configured path uses an unsupported format.";
            return false;
        }
    }

    /// <summary>
    /// Returns whether a path is in or equal to a configured root.
    /// </summary>
    public static bool IsWithin(string path, string root)
    {
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns whether a file-system entry is a symbolic link or another reparse point.
    /// </summary>
    public static bool IsReparsePoint(string path)
    {
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    [GeneratedRegex("^[A-Za-z0-9._~-]{20,1024}$")]
    private static partial Regex TmdbTokenRegex();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,127}$")]
    private static partial Regex EnvironmentVariableRegex();

    [GeneratedRegex("^[a-z]{2,3}(?:-[A-Z]{2})?$")]
    private static partial Regex LanguageRegex();
}

/// <summary>
/// Holds the canonical paths derived from a valid organizer configuration.
/// </summary>
public sealed record ValidatedOrganizerConfiguration(string[] SourceDirectories, string DestinationDirectory, string TmdbReadAccessToken);
