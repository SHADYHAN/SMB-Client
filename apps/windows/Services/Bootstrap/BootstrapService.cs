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
                snapshot.ServerProfiles.Select(MapProfile).ToArray(),
                snapshot.ActiveServer is null ? null : MapProfile(snapshot.ActiveServer),
                snapshot.ActiveCredential?.Username
            );
        }, cancellationToken);
    }

    private static ServerProfile MapProfile(StoredServerProfile profile)
    {
        return new ServerProfile(
            profile.Id,
            profile.DisplayName,
            profile.Endpoint.Port is null
                ? profile.Endpoint.Host
                : $"{profile.Endpoint.Host}:{profile.Endpoint.Port}",
            profile.Username
        );
    }

    private static string ResolveStorePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directory = Path.Combine(appData, "Rynat");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "rynat.sqlite");
    }
}
