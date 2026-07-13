using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.WatchDB.Configuration;

namespace Jellyfin.Plugin.WatchDB.Services;

/// <summary>
/// Extracts a series title and an episode number from release-like filenames.
/// </summary>
public sealed partial class EpisodeFilenameParser
{
    private const int MaxSeasonNumber = 99;
    private const int MaxEpisodeNumber = 999;

    /// <summary>
    /// Attempts to parse a media file name, falling back to its release-folder names.
    /// </summary>
    public static bool TryParse(string filePath, string sourceRoot, int parentFolderDepth, out ParsedEpisode? parsedEpisode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);

        if (TryParseText(Path.GetFileNameWithoutExtension(filePath), filePath, out parsedEpisode))
        {
            return true;
        }

        var root = Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var directory = Directory.GetParent(filePath);
        for (var depth = 0; directory is not null && depth < Math.Clamp(parentFolderDepth, 0, 5); depth++)
        {
            var candidate = directory.Name;
            if (TryParseText(candidate, filePath, out parsedEpisode))
            {
                return true;
            }

            if (string.Equals(directory.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), root, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            directory = directory.Parent;
        }

        parsedEpisode = null;
        return false;
    }

    /// <summary>
    /// Normalizes a title for exact and token-based matching.
    /// </summary>
    public static string NormalizeTitle(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (character == '&')
            {
                builder.Append(" and ");
                continue;
            }

            if (character is '\'' or '’')
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }

        return WhitespaceRegex().Replace(builder.ToString(), " ").Trim();
    }

    private static bool TryParseText(string rawText, string filePath, out ParsedEpisode? parsedEpisode)
    {
        var match = EpisodeRegex().Match(rawText);
        if (!match.Success)
        {
            parsedEpisode = null;
            return false;
        }

        var title = CleanTitle(match.Groups["title"].Value);
        if (title.Length == 0
            || !int.TryParse(match.Groups["season"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seasonNumber)
            || !int.TryParse(match.Groups["episode"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var episodeNumber)
            || seasonNumber is < 0 or > MaxSeasonNumber
            || episodeNumber is < 0 or > MaxEpisodeNumber)
        {
            parsedEpisode = null;
            return false;
        }

        var yearMatch = YearRegex().Match(title);
        int? year = null;
        if (yearMatch.Success)
        {
            year = int.Parse(yearMatch.Value, CultureInfo.InvariantCulture);
            title = CleanTitle(YearRegex().Replace(title, " ", 1));
        }

        parsedEpisode = new ParsedEpisode(filePath, title, seasonNumber, episodeNumber, year);
        return true;
    }

    private static string CleanTitle(string title)
    {
        return WhitespaceRegex()
            .Replace(title.Replace('.', ' ').Replace('_', ' '), " ")
            .Trim(' ', '-', '–', '—');
    }

    [GeneratedRegex(@"^(?<title>.+?)(?:[\s._\-]+)?[sS](?<season>\d{1,2})\s*[eE](?<episode>\d{1,3})(?:\b|[\s._\-])")]
    private static partial Regex EpisodeRegex();

    [GeneratedRegex(@"\b(?:19|20)\d{2}\b")]
    private static partial Regex YearRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}

/// <summary>
/// Represents reliable information parsed from a release name.
/// </summary>
public sealed record ParsedEpisode(string FilePath, string SeriesTitle, int SeasonNumber, int EpisodeNumber, int? Year);
