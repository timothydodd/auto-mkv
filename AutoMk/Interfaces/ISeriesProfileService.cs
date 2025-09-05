using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMk.Models;

namespace AutoMk.Interfaces;

public interface ISeriesProfileService
{
    Task<SeriesProfile?> GetProfileAsync(string seriesTitle);
    Task<SeriesProfile> CreateOrUpdateProfileAsync(SeriesProfile profile);
    Task<List<SeriesProfile>> GetAllProfilesAsync();
    Task DeleteProfileAsync(string seriesTitle);
    SeriesProfile CreateDefaultProfile(string seriesTitle);
}