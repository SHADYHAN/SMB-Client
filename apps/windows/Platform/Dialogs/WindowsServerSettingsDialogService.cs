using System.Windows;
using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.Services.Profiles;
using Rynat.WindowsClient.UI.Servers;

namespace Rynat.WindowsClient.Platform.Dialogs;

public sealed class WindowsServerSettingsDialogService : IServerSettingsDialogService
{
    private readonly IServerProfileService _serverProfileService;

    public WindowsServerSettingsDialogService(IServerProfileService serverProfileService)
    {
        _serverProfileService = serverProfileService;
    }

    public Task<ServerSettingsDialogResult?> ShowAsync(
        IReadOnlyList<ServerProfile> profiles,
        ServerProfile? activeProfile
    )
    {
        var viewModel = new ServerSettingsViewModel(_serverProfileService, profiles, activeProfile);
        var window = new ServerSettingsWindow
        {
            Owner = Application.Current.MainWindow,
            DataContext = viewModel
        };

        var accepted = window.ShowDialog() == true;
        var result = accepted
            ? new ServerSettingsDialogResult(viewModel.CurrentProfiles, viewModel.ActiveProfile)
            : null;
        return Task.FromResult(result);
    }
}
