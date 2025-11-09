using System.Threading.Tasks;

namespace AutoMk.Interfaces;

/// <summary>
/// Service for discovering existing MKV files and organizing them without ripping
/// </summary>
public interface IDiscoverAndNameService
{
    /// <summary>
    /// Runs the discover and name workflow: finds MKV files, prompts for identification, and moves them
    /// </summary>
    /// <returns>Task representing the async operation</returns>
    Task RunDiscoverAndNameWorkflowAsync();
}
