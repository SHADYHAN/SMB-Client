using System.Windows;
using System.Windows.Controls;

namespace Rynat.WindowsClient.Platform.Dialogs;

public sealed class WindowsUserDialogService : IUserDialogService
{
    public string? PromptText(string title, string label, string initialValue = "")
    {
        var input = new TextBox
        {
            MinWidth = 280,
            Text = initialValue,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = label });
        panel.Children.Add(input);

        var window = new Window
        {
            Title = title,
            Content = new Border
            {
                Padding = new Thickness(18),
                Child = panel
            },
            Width = 360,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current.MainWindow
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var ok = new Button { Content = "确定", MinWidth = 72, IsDefault = true };
        var cancel = new Button { Content = "取消", MinWidth = 72, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        ok.Click += (_, _) => window.DialogResult = true;
        cancel.Click += (_, _) => window.DialogResult = false;
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        window.Loaded += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };

        return window.ShowDialog() == true ? input.Text : null;
    }

    public bool Confirm(string title, string message)
    {
        return MessageBox.Show(
            Application.Current.MainWindow,
            message,
            title,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question
        ) == MessageBoxResult.OK;
    }
}
