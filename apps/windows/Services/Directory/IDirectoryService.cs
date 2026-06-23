using Rynat.WindowsClient.Domain;

namespace Rynat.WindowsClient.Services.Directory;

public interface IDirectoryService
{
    Task<RemoteDirectory> ListAsync(
        ServerSession session,
        string share,
        string path,
        CancellationToken cancellationToken = default
    );
}
