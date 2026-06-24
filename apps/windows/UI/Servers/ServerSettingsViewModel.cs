using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.Services.Profiles;
using Rynat.WindowsClient.UI.Infrastructure;

namespace Rynat.WindowsClient.UI.Servers;

public sealed class ServerSettingsViewModel : ObservableObject
{
    private readonly IServerProfileService _serverProfileService;
    private ServerSettingsItemViewModel? _selectedServer;
    private string _message = string.Empty;
    private bool _isBusy;

    public ServerSettingsViewModel(
        IServerProfileService serverProfileService,
        IReadOnlyList<ServerProfile> profiles,
        ServerProfile? activeProfile
    )
    {
        _serverProfileService = serverProfileService;
        foreach (var profile in profiles)
        {
            Servers.Add(new ServerSettingsItemViewModel(profile, profile.Id == activeProfile?.Id));
        }

        SelectedServer = Servers.FirstOrDefault(item => item.IsActive) ?? Servers.FirstOrDefault();
        AddCommand = new RelayCommand(AddServer);
        RemoveCommand = new AsyncRelayCommand(RemoveSelectedAsync, () => SelectedServer is not null && Servers.Count > 1 && !IsBusy);
        SaveCommand = new AsyncRelayCommand(SaveSelectedAsync, () => SelectedServer is not null && !IsBusy);
        SetActiveCommand = new AsyncRelayCommand(SetSelectedActiveAsync, () => SelectedServer is not null && !IsBusy);
        CloseCommand = new RelayCommand(CloseWindow);
    }

    public ObservableCollection<ServerSettingsItemViewModel> Servers { get; } = new();

    public ICommand AddCommand { get; }

    public ICommand RemoveCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand SetActiveCommand { get; }

    public ICommand CloseCommand { get; }

    public ServerSettingsItemViewModel? SelectedServer
    {
        get => _selectedServer;
        set
        {
            if (SetProperty(ref _selectedServer, value))
            {
                RefreshCommands();
            }
        }
    }

    public string Message
    {
        get => _message;
        set => SetProperty(ref _message, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshCommands();
            }
        }
    }

    public IReadOnlyList<ServerProfile> CurrentProfiles => Servers
        .Select(item => item.Profile)
        .Where(profile => !string.IsNullOrWhiteSpace(profile.Id))
        .ToArray();

    public ServerProfile? ActiveProfile => Servers
        .FirstOrDefault(item => item.IsActive && !string.IsNullOrWhiteSpace(item.Profile.Id))
        ?.Profile;

    private void AddServer()
    {
        var profile = new ServerProfile(string.Empty, "新服务器", "", null);
        var item = new ServerSettingsItemViewModel(profile, false);
        Servers.Add(item);
        SelectedServer = item;
        Message = string.Empty;
    }

    private async Task SaveSelectedAsync()
    {
        if (SelectedServer is not { } item)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _serverProfileService.SaveServerAsync(
                string.IsNullOrWhiteSpace(item.Profile.Id) ? null : item.Profile,
                item.DisplayName,
                item.Host,
                item.IsActive
            );
            ApplyResult(result);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RemoveSelectedAsync()
    {
        if (SelectedServer is not { } item)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(item.Profile.Id))
        {
            Servers.Remove(item);
            SelectedServer = Servers.FirstOrDefault();
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _serverProfileService.DeleteServerAsync(item.Profile);
            ApplyResult(result);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SetSelectedActiveAsync()
    {
        if (SelectedServer is not { } item)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(item.Profile.Id))
        {
            item.IsActive = true;
            foreach (var other in Servers.Where(server => !ReferenceEquals(server, item)))
            {
                other.IsActive = false;
            }
            Message = "保存后作为默认服务器。";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _serverProfileService.SetActiveAsync(item.Profile);
            ApplyResult(result);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyResult(ServerProfileListResult result)
    {
        Message = result.Summary;
        if (!result.Succeeded)
        {
            return;
        }

        Servers.Clear();
        foreach (var profile in result.Profiles)
        {
            Servers.Add(new ServerSettingsItemViewModel(profile, profile.Id == result.ActiveProfile?.Id));
        }

        SelectedServer = result.SavedProfile is not null
            ? Servers.FirstOrDefault(item => item.Profile.Id == result.SavedProfile.Id)
            : Servers.FirstOrDefault(item => item.IsActive) ?? Servers.FirstOrDefault();
    }

    private static void CloseWindow(object? parameter)
    {
        if (parameter is Window window)
        {
            window.DialogResult = true;
        }
    }

    private void RefreshCommands()
    {
        if (RemoveCommand is AsyncRelayCommand removeCommand)
        {
            removeCommand.RaiseCanExecuteChanged();
        }
        if (SaveCommand is AsyncRelayCommand saveCommand)
        {
            saveCommand.RaiseCanExecuteChanged();
        }
        if (SetActiveCommand is AsyncRelayCommand activeCommand)
        {
            activeCommand.RaiseCanExecuteChanged();
        }
    }
}
