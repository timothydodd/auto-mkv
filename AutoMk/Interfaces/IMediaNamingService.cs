namespace AutoMk.Interfaces;

public interface IMediaNamingService
{
    string GenerateMovieFileName(string title, string? year, string extension);
    string GenerateEpisodeFileName(string seriesTitle, int season, int episode, string? episodeTitle, string extension);
    string GetMovieDirectory(string basePath, string title, string? year);
    string GetSeriesDirectory(string basePath, string seriesTitle);
    string GetSeasonDirectory(int season);
}