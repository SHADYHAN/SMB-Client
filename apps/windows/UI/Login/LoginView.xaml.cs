using System.Windows.Controls;
using System.Windows.Input;

namespace Rynat.WindowsClient.UI.Login;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
    }

    private void PasswordInput_OnPasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
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
}
