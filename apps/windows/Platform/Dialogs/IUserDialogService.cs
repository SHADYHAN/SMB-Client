namespace Rynat.WindowsClient.Platform.Dialogs;

public interface IUserDialogService
{
    string? PromptText(string title, string label, string initialValue = "");

    bool Confirm(string title, string message);

    bool ConfirmOverwrite(IReadOnlyList<string> names);
}
