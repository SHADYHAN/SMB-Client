using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.UI.Infrastructure;

namespace Rynat.WindowsClient.UI.Servers;

public sealed class ServerSettingsItemViewModel : ObservableObject
{
    private string _displayName;
    private string _host;
    private bool _isActive;

    public ServerSettingsItemViewModel(ServerProfile profile, bool isActive)
    {
        Profile = profile;
        _displayName = profile.DisplayName;
        _host = profile.Host;
        _isActive = isActive;
    }

    public ServerProfile Profile { get; private set; }

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public string Host
    {
        get => _host;
        set => SetProperty(ref _host, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public void UpdateProfile(ServerProfile profile, bool isActive)
    {
        Profile = profile;
        DisplayName = profile.DisplayName;
        Host = profile.Host;
        IsActive = isActive;
    }
}
