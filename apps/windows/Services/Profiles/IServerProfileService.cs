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

    Task<ServerProfileSaveResult> SaveLoginProfileAsync(
        ServerProfile? existingProfile,
        string host,
        string username,
        CancellationToken cancellationToken = default
    );

    Task<ServerProfileSaveResult> UpdateCredentialOptionsAsync(
        ServerProfile profile,
        bool rememberPassword,
        bool autoLogin,
        CancellationToken cancellationToken = default
    );

    Task<ServerProfileListResult> SaveServerAsync(
        ServerProfile? existingProfile,
        string displayName,
        string host,
        bool setActive,
        CancellationToken cancellationToken = default
    );

    Task<ServerProfileListResult> DeleteServerAsync(
        ServerProfile profile,
        CancellationToken cancellationToken = default
    );

    Task<ServerProfileListResult> SetActiveAsync(
        ServerProfile profile,
        CancellationToken cancellationToken = default
    );
}
