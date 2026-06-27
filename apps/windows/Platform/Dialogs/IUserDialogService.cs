namespace Rynat.WindowsClient.Platform.Dialogs;

public interface IUserDialogService
{
    string? PromptText(string title, string label, string initialValue = "");

    string? PickSaveFilePath(string title, string suggestedFileName);

    string? PickFolderPath(string title);

    bool Confirm(string title, string message);

    bool ConfirmOverwrite(IReadOnlyList<string> names);
}
