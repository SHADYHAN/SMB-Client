using Rynat.WindowsClient.Domain;

namespace Rynat.WindowsClient.Platform.Dialogs;

public sealed record ServerSettingsDialogResult(
    IReadOnlyList<ServerProfile> Profiles,
    ServerProfile? ActiveProfile
);
