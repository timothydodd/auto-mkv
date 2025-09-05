using System.Threading;
using System.Threading.Tasks;

namespace AutoMk.Interfaces;

public interface IFileTransferClient
{
    Task<bool> SendFileAsync(string relativePath, string filePath, CancellationToken cancellationToken = default);
    Task<bool?> SendFileInBackground(string relativePath, string filePath);
}