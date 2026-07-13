using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.WatchDB.Services;

/// <summary>
/// Small, focused TMDb client used only to validate series and episode matches.
/// </summary>
public sealed class TmdbClient : IDisposable
{
    private const long MaxResponseBytes = 2 * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, IReadOnlyList<TmdbSeries>> _searchCache = new(StringComparer.Ordinal);
    private readonly Dictionary<(int SeriesId, int SeasonNumber, int EpisodeNumber, string Language), TmdbEpisode?> _episodeCache = [];
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbClient"/> class.
    /// </summary>
    public TmdbClient(string readAccessToken)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://api.themoviedb.org/3/", UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(20),
        };
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", readAccessToken);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WatchDB-Smart-Organizer/0.1");
    }

    /// <summary>
    /// Searches TV series by name.
    /// </summary>
    public async Task<IReadOnlyList<TmdbSeries>> SearchSeriesAsync(string query, string language, int limit, CancellationToken cancellationToken)
    {
        var cacheKey = $"{query}\n{language}\n{limit}";
        if (_searchCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var uri = $"search/tv?query={Uri.EscapeDataString(query)}&language={Uri.EscapeDataString(language)}&include_adult=false";
        using var response = await _httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        EnsureSafeResponseSize(response);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var payload = await JsonSerializer.DeserializeAsync<TmdbSearchResponse>(stream, _jsonOptions, cancellationToken).ConfigureAwait(false);
        var results = (payload?.Results ?? [])
            .Where(result => result.Id > 0)
            .Take(Math.Clamp(limit, 1, 10))
            .Select(result => new TmdbSeries(result.Id, result.Name ?? string.Empty, result.OriginalName ?? string.Empty, result.FirstAirDate))
            .ToArray();
        _searchCache[cacheKey] = results;
        return results;
    }

    /// <summary>
    /// Checks that a concrete season and episode exists for a series.
    /// </summary>
    public async Task<TmdbEpisode?> GetEpisodeAsync(int seriesId, int seasonNumber, int episodeNumber, string language, CancellationToken cancellationToken)
    {
        var cacheKey = (seriesId, seasonNumber, episodeNumber, language);
        if (_episodeCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var uri = $"tv/{seriesId}/season/{seasonNumber}/episode/{episodeNumber}?language={Uri.EscapeDataString(language)}";
        using var response = await _httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _episodeCache[cacheKey] = null;
            return null;
        }

        EnsureSafeResponseSize(response);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var payload = await JsonSerializer.DeserializeAsync<TmdbEpisodeResponse>(stream, _jsonOptions, cancellationToken).ConfigureAwait(false);
        var result = payload is null ? null : new TmdbEpisode(payload.Name ?? string.Empty);
        _episodeCache[cacheKey] = result;
        return result;
    }

    /// <inheritdoc />
    public void Dispose() => _httpClient.Dispose();

    private static void EnsureSafeResponseSize(HttpResponseMessage response)
    {
        if (response.Content.Headers.ContentLength is > MaxResponseBytes)
        {
            throw new InvalidDataException("TMDb returned a response larger than WatchDB accepts.");
        }
    }

    private sealed class TmdbSearchResponse
    {
        [JsonPropertyName("results")]
        public List<TmdbSearchResult> Results { get; set; } = [];
    }

    private sealed class TmdbSearchResult
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("original_name")]
        public string? OriginalName { get; set; }

        [JsonPropertyName("first_air_date")]
        public string? FirstAirDate { get; set; }
    }

    private sealed class TmdbEpisodeResponse
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}

/// <summary>
/// A TV series returned by TMDb.
/// </summary>
public sealed record TmdbSeries(int Id, string Name, string OriginalName, string? FirstAirDate)
{
    /// <summary>
    /// Gets the first air year when TMDb supplied one.
    /// </summary>
    public int? Year => FirstAirDate is { Length: >= 4 } && int.TryParse(FirstAirDate[..4], out var year) ? year : null;
}

/// <summary>
/// A validated episode returned by TMDb.
/// </summary>
public sealed record TmdbEpisode(string Name);
