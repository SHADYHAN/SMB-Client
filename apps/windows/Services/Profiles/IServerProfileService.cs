using Rynat.WindowsClient.Domain;

namespace Rynat.WindowsClient.Services.Profiles;

public interface IServerProfileService
{
    Task<ServerProfileSaveResult> SaveLoginAsync(
        ServerProfile? existingProfile,
        string host,
        string username,
        string password,
        bool rememberPassword,
        bool autoLogin,
        CancellationToken cancellationToken = default
    );
}
