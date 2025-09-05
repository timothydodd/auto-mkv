using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMk.Models;

namespace AutoMk.Interfaces;

public interface IMakeMkvService
{
    Task<bool> GetDiscInfoAsync(AkDriveInfo drive);
    List<AkTitle> FilterTitlesBySize(AkDriveInfo drive, double minSizeGB = 3.0, double maxSizeGB = 12.0);
    List<AkTitle> FilterTitlesBySizeForTvSeries(AkDriveInfo drive, double minSizeGB = 3.0);
    Task<bool> RipTitlesAsync(AkDriveInfo drive, List<AkTitle> titles, string outputPath, bool skipIfExists = true);
    Task<bool> RipDiscWithFilterAsync(AkDriveInfo drive, string outputPath, double minSizeGB = 3.0, double maxSizeGB = 12.0);
    Task<List<AkDriveInfo>> GetAvailableDrivesAsync();
}
