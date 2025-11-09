using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using AutoMk.Interfaces;
using AutoMk.Models;

namespace AutoMk.Services;

public class OmdbClient : IOmdbClient
{
    private readonly HttpClient _client;
    private readonly OmdbSettings _omdbSettings;

    readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
    public OmdbClient(HttpClient client, OmdbSettings omdbSettings)
    {
        _client = client;
        _omdbSettings = omdbSettings;
    }
    public async Task<OmdbMovieResponse?> GetMovie(string title, int? year)
    {
        //encode title
        title = System.Net.WebUtility.UrlEncode(title);

        var url = $"{_omdbSettings.BaseUrl}?apikey={_omdbSettings.ApiKey}&t={title}";
        if (year.HasValue && year > 0)
        {
            url += $"&y={year}";
        }
        var response = await _client.GetAsync(url);
        try
        {
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<OmdbMovieResponse>(content, _jsonOptions);
        }
        catch (Exception)
        {
            // Log exception if needed
        }
        return null;
    }
    public async Task<OmdbSeriesResponse?> GetSeries(string title)
    {
        //encode title
        title = System.Net.WebUtility.UrlEncode(title);

        var url = $"{_omdbSettings.BaseUrl}?apikey={_omdbSettings.ApiKey}&t={title}&type=series";

        var response = await _client.GetAsync(url);
        try
        {
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<OmdbSeriesResponse>(content, _jsonOptions);
        }
        catch (Exception)
        {
            // Log exception if needed
        }
        return null;
    }

    public async Task<OmdbMovieResponse?> GetMediaByImdbId(string imdbId)
    {
        // IMDB IDs don't need URL encoding but we'll clean it up
        imdbId = imdbId.Trim();

        var url = $"{_omdbSettings.BaseUrl}?apikey={_omdbSettings.ApiKey}&i={imdbId}";

        var response = await _client.GetAsync(url);
        try
        {
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<OmdbMovieResponse>(content, _jsonOptions);
        }
        catch (Exception)
        {
            // Log exception if needed
        }
        return null;
    }

    public async Task<OmdbSeasonResponse?> GetSeasonInfo(string seriesName, int season)
    {
        //encode title
        seriesName = System.Net.WebUtility.UrlEncode(seriesName);

        var url = $"{_omdbSettings.BaseUrl}?apikey={_omdbSettings.ApiKey}&t={seriesName}&Season={season}";

        var response = await _client.GetAsync(url);
        try
        {
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<OmdbSeasonResponse>(content, _jsonOptions);
        }
        catch (Exception)
        {
            // Log exception if needed
        }
        return null;
    }

    public async Task<OmdbSearchResult[]?> SearchMovie(string title, int? year)
    {
        title = System.Net.WebUtility.UrlEncode(title.Trim());

        var url = $"{_omdbSettings.BaseUrl}?apikey={_omdbSettings.ApiKey}&s={title}&type=movie";
        if (year.HasValue && year > 0)
        {
            url += $"&y={year}";
        }
        var response = await _client.GetAsync(url);
        try
        {
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();

            var r = JsonSerializer.Deserialize<OmdbSearchResponse>(content, _jsonOptions);
            if (r != null && r.Search?.Count > 0)
            {
                return r.Search.ToArray();
            }

        }
        catch (Exception)
        {


        }
        return null;
    }

    public async Task GetPoster(string url, Stream output)
    {
        await (await _client.GetStreamAsync(url)).CopyToAsync(output);
    }

}
