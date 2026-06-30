using System.Threading;
using System.Windows.Forms;

namespace Rynat.WindowsTray.Services;

internal static class ClipboardService
{
    public static Task SetTextAsync(string text)
    {
        var completion = new TaskCompletionSource<object?>();
        var thread = new Thread(() =>
        {
            try
            {
                Clipboard.SetText(text);
                completion.SetResult(null);
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
