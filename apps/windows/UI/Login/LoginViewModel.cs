using System.Windows.Input;
using Rynat.WindowsClient.UI.Infrastructure;

namespace Rynat.WindowsClient.UI.Login;

public sealed class LoginViewModel : ObservableObject
{
    private string _serverHost = "192.168.102.136";
    private string _username = string.Empty;
    private string _password = string.Empty;
    private string _message = "请输入账号密码登录共享网盘";
    private bool _isBusy;

    public string ServerHost
    {
        get => _serverHost;
        set => SetProperty(ref _serverHost, value);
    }

    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    public string Message
    {
        get => _message;
        set => SetProperty(ref _message, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public ICommand LoginCommand { get; set; } = new RelayCommand(() => { });
}
