using System.Windows.Input;
using Rynat.WindowsClient.UI.Infrastructure;

namespace Rynat.WindowsClient.UI.Login;

public sealed class LoginViewModel : ObservableObject
{
    private ICommand _loginCommand = new RelayCommand(() => { });
    private string _serverHost = "192.168.102.136";
    private string _username = string.Empty;
    private string _password = string.Empty;
    private string _message = "请输入账号密码登录共享网盘";
    private bool _isBusy;

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
