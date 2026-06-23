using Rynat.WindowsClient.Domain;

namespace Rynat.WindowsClient.Services.Bootstrap;

public interface IBootstrapService
{
    Task<BootstrapState> LoadAsync(CancellationToken cancellationToken = default);
}

public sealed record BootstrapState(
    IReadOnlyList<ServerProfile> ServerProfiles,
    ServerProfile? ActiveServer,
    string? ActiveUsername
);
