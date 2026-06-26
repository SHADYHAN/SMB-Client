using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;

namespace Rynat.WindowsClient.UI.Login;

public partial class LoginView : UserControl
{
    private LoginViewModel? _viewModel;
    private bool _syncingPassword;

    public LoginView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void PasswordInput_OnPasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_syncingPassword)
        {
            return;
        }

        if (DataContext is LoginViewModel viewModel)
        {
            viewModel.Password = PasswordInput.Password;
        }
    }

    private void LoginInput_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not LoginViewModel viewModel)
        {
            return;
        }

        if (viewModel.LoginCommand.CanExecute(null))
        {
            viewModel.LoginCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        }

        _viewModel = e.NewValue as LoginViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
            SyncPasswordBox(_viewModel.Password);
        }
        else
        {
            SyncPasswordBox(string.Empty);
        }
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LoginViewModel.Password) && sender is LoginViewModel viewModel)
        {
            SyncPasswordBox(viewModel.Password);
        }
    }

    private void SyncPasswordBox(string password)
    {
        if (PasswordInput.Password == password)
        {
            return;
        }

        _syncingPassword = true;
        try
        {
            PasswordInput.Password = password;
        }
        finally
        {
            _syncingPassword = false;
        }
    }
}
