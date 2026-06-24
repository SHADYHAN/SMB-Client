using Rynat.WindowsClient.Domain;

namespace Rynat.WindowsClient.Platform.Dialogs;

public interface IServerSettingsDialogService
{
    Task<ServerSettingsDialogResult?> ShowAsync(
        IReadOnlyList<ServerProfile> profiles,
        ServerProfile? activeProfile
    );
}
