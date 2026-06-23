using System.IO;
using Rynat.Client;
using Rynat.WindowsClient.Domain;

namespace Rynat.WindowsClient.Services.Bootstrap;

public sealed class BootstrapService : IBootstrapService
{
    private readonly RynatCoreBridge _bridge;

    public BootstrapService(RynatCoreBridge bridge)
    {
        _bridge = bridge;
    }

    public Task<BootstrapState> LoadAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _bridge.OpenStore(new OpenStoreRequest(ResolveStorePath()));
            var snapshot = _bridge.AppBootstrap();

            return new BootstrapState(
                snapshot.ServerProfiles.Select(profile => MapProfile(profile, CredentialFor(snapshot, profile.Id))).ToArray(),
                snapshot.ActiveServer is null ? null : MapProfile(snapshot.ActiveServer, snapshot.ActiveCredential),
                snapshot.ActiveCredential?.Username,
                snapshot.ActiveCredential?.RememberPassword ?? false,
                snapshot.ActiveCredential?.AutoLogin ?? false
            );
        }, cancellationToken);
    }

    private static StoredServerCredential? CredentialFor(AppBootstrapState snapshot, string profileId)
    {
        return snapshot.ActiveCredential?.ServerProfileId == profileId
            ? snapshot.ActiveCredential
            : null;
    }

    private static ServerProfile MapProfile(
        StoredServerProfile profile,
        StoredServerCredential? credential
    )
    {
        return new ServerProfile(
            profile.Id,
            profile.DisplayName,
            profile.Endpoint.Port is null
                ? profile.Endpoint.Host
                : $"{profile.Endpoint.Host}:{profile.Endpoint.Port}",
            profile.Username ?? credential?.Username,
            credential is not null,
            credential?.AutoLogin ?? false
        );
    }

    private static string ResolveStorePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directory = Path.Combine(appData, "Rynat");
        System.IO.Directory.CreateDirectory(directory);
        return Path.Combine(directory, "rynat.sqlite");
    }
}
