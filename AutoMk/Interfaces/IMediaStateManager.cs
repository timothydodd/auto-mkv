using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMk.Models;

namespace AutoMk.Interfaces;

public interface IMediaStateManager
{
    Task<SeriesState> GetOrCreateSeriesStateAsync(string seriesTitle);
    DiscInfo GetNextDiscInfo(SeriesState seriesState, string discName, int trackCount, ParsedDiscInfo? parsedInfo = null, bool useAutoIncrement = false, List<AkTitle>? rippedTracks = null);
    Task UpdateSeriesStateAsync(SeriesState seriesState, DiscInfo discInfo, int actualEpisodeCount, List<AkTitle>? rippedTracks = null, bool wasAutoIncrement = false);
    Task SaveSeriesStateAsync(SeriesState seriesState);

    // Manual identification cache methods
    Task<ManualIdentification?> GetManualIdentificationAsync(string discName);
    Task SaveManualIdentificationAsync(string discName, MediaIdentity mediaIdentity);
    
    // State checking methods
    Task<SeriesState?> GetExistingSeriesStateAsync(string seriesTitle);
}
