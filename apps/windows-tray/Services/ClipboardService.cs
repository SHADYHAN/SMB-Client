using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Rynat.WindowsTray.Services;

internal static class ClipboardService
{
    private const int MaxAttempts = 8;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(85);

    public static Task SetTextAsync(string text)
    {
        var completion = new TaskCompletionSource<object?>();
        var thread = new Thread(() =>
        {
            try
            {
                SetTextWithRetry(text);
                completion.SetResult(null);
            }
            catch (Exception ex)
            {
                completion.SetException(new InvalidOperationException("剪贴板正被其他程序占用，请稍后重试。", ex));
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static void SetTextWithRetry(string text)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, copy: true);
                return;
            }
            catch (Exception ex) when (ex is ExternalException or ThreadStateException)
            {
                lastError = ex;
                Thread.Sleep(RetryDelay);
            }
        }

        throw lastError ?? new ExternalException("clipboard unavailable");
    }
}
