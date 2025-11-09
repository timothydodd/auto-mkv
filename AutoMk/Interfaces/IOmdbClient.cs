using System.IO;
using System.Threading.Tasks;
using AutoMk.Models;

namespace AutoMk.Interfaces;

public interface IOmdbClient
{
    Task<OmdbMovieResponse?> GetMovie(string title, int? year);
    Task<OmdbSeriesResponse?> GetSeries(string title);
    Task<OmdbMovieResponse?> GetMediaByImdbId(string imdbId);

    Task<OmdbSeasonResponse?> GetSeasonInfo(string seriesName, int season);
    Task<OmdbSearchResult[]?> SearchMovie(string title, int? year);
    Task GetPoster(string url, Stream output);

}
