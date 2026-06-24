using System.Collections.ObjectModel;
using System.Windows.Input;
using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.UI.Infrastructure;

namespace Rynat.WindowsClient.UI.Login;

public sealed class LoginViewModel : ObservableObject
{
    private ICommand _loginCommand = new RelayCommand(() => { });
    private ICommand _serverSettingsCommand = new RelayCommand(() => { });
    private ServerProfile? _selectedProfile;
    private string _serverHost = "192.168.102.136";
    private string _username = string.Empty;
    private string _password = string.Empty;
    private string _message = "请输入账号密码登录共享网盘";
    private bool _rememberPassword = true;
    private bool _autoLogin;
    private bool _isBusy;

    public ObservableCollection<ServerProfile> ServerProfiles { get; } = new();

    public ServerProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!SetProperty(ref _selectedProfile, value))
            {
                return;
            }

            if (value is not null)
            {
                ServerHost = value.Host;
                Username = value.Username ?? Username;
                RememberPassword = value.HasStoredCredential;
                AutoLogin = value.AutoLogin;
            }

            RefreshLoginCommand();
        }
    }

    public string ServerHost
    {
        get => _serverHost;
        set
        {
            if (SetProperty(ref _serverHost, value))
            {
                RefreshLoginCommand();
            }
        }
    }

    public string Username
    {
        get => _username;
        set
        {
            if (SetProperty(ref _username, value))
            {
                RefreshLoginCommand();
            }
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            if (SetProperty(ref _password, value))
            {
                RefreshLoginCommand();
            }
        }
    }

    public string Message
    {
        get => _message;
        set => SetProperty(ref _message, value);
    }

    public bool RememberPassword
    {
        get => _rememberPassword;
        set
        {
            if (SetProperty(ref _rememberPassword, value) && !value)
            {
                AutoLogin = false;
            }
        }
    }

    public bool AutoLogin
    {
        get => _autoLogin;
        set => SetProperty(ref _autoLogin, value && RememberPassword);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshLoginCommand();
            }
        }
    }

    public ICommand LoginCommand
    {
        get => _loginCommand;
        set
        {
            if (SetProperty(ref _loginCommand, value))
            {
                RefreshLoginCommand();
            }
        }
    }

    public ICommand ServerSettingsCommand
    {
        get => _serverSettingsCommand;
        set => SetProperty(ref _serverSettingsCommand, value);
    }

    public void LoadServerProfiles(
        IReadOnlyList<ServerProfile> profiles,
        ServerProfile? activeProfile,
        string? activeUsername,
        bool rememberPassword,
        bool autoLogin
    )
    {
        ServerProfiles.Clear();
        foreach (var profile in profiles)
        {
            ServerProfiles.Add(profile);
        }

        SelectedProfile = activeProfile is null
            ? ServerProfiles.FirstOrDefault()
            : ServerProfiles.FirstOrDefault(profile => profile.Id == activeProfile.Id) ?? activeProfile;

        if (SelectedProfile is not null)
        {
            ServerHost = SelectedProfile.Host;
            Username = activeUsername ?? SelectedProfile.Username ?? Username;
        }

        RememberPassword = rememberPassword || SelectedProfile?.HasStoredCredential == true;
        AutoLogin = autoLogin || SelectedProfile?.AutoLogin == true;
    }

    public void UpsertProfile(ServerProfile profile)
    {
        var existing = ServerProfiles.FirstOrDefault(item => item.Id == profile.Id);
        if (existing is not null)
        {
            var index = ServerProfiles.IndexOf(existing);
            ServerProfiles[index] = profile;
        }
        else
        {
            ServerProfiles.Add(profile);
        }

        SelectedProfile = profile;
    }

    public void ReplaceServerProfiles(IReadOnlyList<ServerProfile> profiles, ServerProfile? activeProfile)
    {
        ServerProfiles.Clear();
        foreach (var profile in profiles)
        {
            ServerProfiles.Add(profile);
        }

        SelectedProfile = activeProfile is null
            ? ServerProfiles.FirstOrDefault()
            : ServerProfiles.FirstOrDefault(profile => profile.Id == activeProfile.Id) ?? ServerProfiles.FirstOrDefault();
    }

    private void RefreshLoginCommand()
    {
        if (_loginCommand is AsyncRelayCommand asyncCommand)
        {
            asyncCommand.RaiseCanExecuteChanged();
        }
        else if (_loginCommand is RelayCommand relayCommand)
        {
            relayCommand.RaiseCanExecuteChanged();
        }
    }
}
