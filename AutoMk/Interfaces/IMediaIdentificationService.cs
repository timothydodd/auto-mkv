using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMk.Models;

namespace AutoMk.Interfaces;

public interface IMediaIdentificationService
{
    Task<bool> ProcessRippedMediaAsync(string outputPath, string discName, List<AkTitle> rippedTitles);
    Task<bool> ProcessRippedMediaAsync(string outputPath, string discName, List<AkTitle> rippedTitles, PreIdentifiedMedia? preIdentifiedMedia);
    Task<PreIdentifiedMedia?> PreIdentifyMediaAsync(string discName, List<AkTitle> titlesToRip);
}