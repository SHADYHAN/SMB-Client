using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Rynat.WindowsTray.Services;

internal sealed class GlobalHotkeyService : NativeWindow, IDisposable
{
    private const int HotkeyId = 0x524c;
    private const int WmHotkey = 0x0312;
    private const uint ModShift = 0x0004;
    private const uint ModControl = 0x0002;
    private const uint VkL = 0x4c;
    private bool _registered;

    public event EventHandler? Pressed;

    public bool IsRegistered => _registered;

    public bool Register()
    {
        if (_registered)
        {
            return true;
        }

        if (Handle == IntPtr.Zero)
        {
            CreateHandle(new CreateParams());
        }

        _registered = RegisterHotKey(Handle, HotkeyId, ModControl | ModShift, VkL);
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered)
        {
            return;
        }

        UnregisterHotKey(Handle, HotkeyId);
        _registered = false;
    }

    public void Dispose()
    {
        Unregister();
        if (Handle != IntPtr.Zero)
        {
            DestroyHandle();
        }

        GC.SuppressFinalize(this);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmHotkey && message.WParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.WndProc(ref message);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
