using System.Threading.Tasks;

namespace AutoMk.Interfaces;

public interface IMediaMoverService
{
    Task FindFiles(string outputPath);
}