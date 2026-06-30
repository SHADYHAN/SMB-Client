using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Rynat.WindowsTray.Services;

internal sealed class ExplorerService
{
    private const int SW_SHOW = 5;
    private const int SW_RESTORE = 9;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int ActivationAttempts = 12;
    private static readonly TimeSpan ActivationDelay = TimeSpan.FromMilliseconds(120);

    public void OpenDirectory(string uncPath)
    {
        var directory = NormalizePath(uncPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        if (TryReuseExistingDirectoryWindow(directory))
        {
            return;
        }

        StartExplorer(QuoteArgument(directory));
        TryActivateDirectoryWithRetry(directory);
    }

    public void OpenTarget(string path, string kind)
    {
        var normalizedPath = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return;
        }

        if (string.Equals(kind, "file", StringComparison.OrdinalIgnoreCase))
        {
            var parentDirectory = NormalizePath(Path.GetDirectoryName(normalizedPath) ?? string.Empty);
            StartExplorer($"/select,{QuoteArgument(normalizedPath)}");
            TryActivateDirectoryWithRetry(parentDirectory);
            return;
        }

        OpenDirectory(normalizedPath);
    }

    public bool TryGetForegroundSelection(out ExplorerSelection selection)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return TryGetForegroundSelectionCore(out selection);
        }

        var found = false;
        var result = new ExplorerSelection();
        var thread = new Thread(() => found = TryGetForegroundSelectionCore(out result));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        selection = result;
        return found;
    }

    private static void StartExplorer(string arguments)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = arguments,
            UseShellExecute = true
        });
    }

    private static bool TryActivateDirectoryWithRetry(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        for (var attempt = 0; attempt < ActivationAttempts; attempt++)
        {
            Thread.Sleep(ActivationDelay);
            if (TryActivateExactDirectory(directory))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReuseExistingDirectoryWindow(string directory)
    {
        if (!TryFindReusableExplorerWindow(directory, out var windowHandle, out var navigated))
        {
            return false;
        }

        BringWindowToForeground(windowHandle);
        if (navigated)
        {
            TryActivateWindowAtDirectoryWithRetry(windowHandle, directory);
            BringWindowToForeground(windowHandle);
        }

        return true;
    }

    private static bool TryActivateExactDirectory(string directory)
    {
        if (!TryFindExplorerWindow(directory, out var windowHandle))
        {
            return false;
        }

        BringWindowToForeground(windowHandle);
        return true;
    }

    private static bool TryActivateWindowAtDirectoryWithRetry(IntPtr windowHandle, string directory)
    {
        for (var attempt = 0; attempt < ActivationAttempts; attempt++)
        {
            Thread.Sleep(ActivationDelay);
            if (TryGetExplorerWindowPath(windowHandle, out var windowPath)
                && PathsEqual(NormalizePathForComparison(directory), NormalizePathForComparison(windowPath)))
            {
                BringWindowToForeground(windowHandle);
                return true;
            }
        }

        return false;
    }

    private static bool TryGetExplorerWindowPath(IntPtr windowHandle, out string path)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return TryGetExplorerWindowPathCore(windowHandle, out path);
        }

        var found = false;
        var result = string.Empty;
        var thread = new Thread(() => found = TryGetExplorerWindowPathCore(windowHandle, out result));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        path = result;
        return found;
    }

    private static bool TryGetExplorerWindowPathCore(IntPtr windowHandle, out string path)
    {
        path = string.Empty;
        object? shell = null;
        object? windows = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return false;
            }

            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return false;
            }

            windows = shellType.InvokeMember(
                "Windows",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: null,
                culture: CultureInfo.InvariantCulture);

            foreach (var window in EnumerateComCollection(windows))
            {
                if (!IsExplorerWindow(window))
                {
                    ReleaseComObject(window);
                    continue;
                }

                var matchedWindowHandle = GetComIntPtr(window, "HWND");
                if (matchedWindowHandle != windowHandle)
                {
                    ReleaseComObject(window);
                    continue;
                }

                var locationUrl = GetComString(window, "LocationURL");
                ReleaseComObject(window);
                return TryConvertLocationUrlToPath(locationUrl, out path);
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            ReleaseComObject(windows);
            ReleaseComObject(shell);
        }

        return false;
    }

    private static bool TryFindReusableExplorerWindow(string directory, out IntPtr windowHandle, out bool navigated)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return TryFindReusableExplorerWindowCore(directory, out windowHandle, out navigated);
        }

        var found = false;
        var handle = IntPtr.Zero;
        var didNavigate = false;
        var thread = new Thread(() => found = TryFindReusableExplorerWindowCore(directory, out handle, out didNavigate));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        windowHandle = handle;
        navigated = didNavigate;
        return found;
    }

    private static bool TryFindReusableExplorerWindowCore(string directory, out IntPtr windowHandle, out bool navigated)
    {
        windowHandle = IntPtr.Zero;
        navigated = false;
        var targetPath = NormalizePathForComparison(directory);
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return false;
        }

        object? shell = null;
        object? windows = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return false;
            }

            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return false;
            }

            windows = shellType.InvokeMember(
                "Windows",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: null,
                culture: CultureInfo.InvariantCulture);

            foreach (var window in EnumerateComCollection(windows))
            {
                if (!IsExplorerWindow(window))
                {
                    ReleaseComObject(window);
                    continue;
                }

                var locationUrl = GetComString(window, "LocationURL");
                if (!TryConvertLocationUrlToPath(locationUrl, out var locationPath))
                {
                    ReleaseComObject(window);
                    continue;
                }

                var normalizedLocationPath = NormalizePathForComparison(locationPath);
                if (!ShouldReuseWindowForDirectory(targetPath, normalizedLocationPath))
                {
                    ReleaseComObject(window);
                    continue;
                }

                var matchedWindowHandle = GetComIntPtr(window, "HWND");
                if (matchedWindowHandle == IntPtr.Zero)
                {
                    ReleaseComObject(window);
                    continue;
                }

                if (!PathsEqual(targetPath, normalizedLocationPath))
                {
                    if (!TryNavigateExplorerWindow(window, targetPath))
                    {
                        ReleaseComObject(window);
                        continue;
                    }

                    navigated = true;
                }

                ReleaseComObject(window);
                windowHandle = matchedWindowHandle;
                return true;
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            ReleaseComObject(windows);
            ReleaseComObject(shell);
        }

        return false;
    }

    private static bool TryFindExplorerWindow(string directory, out IntPtr windowHandle)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return TryFindExplorerWindowCore(directory, out windowHandle);
        }

        var found = false;
        var handle = IntPtr.Zero;
        var thread = new Thread(() => found = TryFindExplorerWindowCore(directory, out handle));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        windowHandle = handle;
        return found;
    }

    private static bool TryFindExplorerWindowCore(string directory, out IntPtr windowHandle)
    {
        windowHandle = IntPtr.Zero;
        var targetPath = NormalizePathForComparison(directory);
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return false;
        }

        object? shell = null;
        object? windows = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return false;
            }

            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return false;
            }

            windows = shellType.InvokeMember(
                "Windows",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: null,
                culture: CultureInfo.InvariantCulture);

            foreach (var window in EnumerateComCollection(windows))
            {
                if (!IsExplorerWindow(window))
                {
                    ReleaseComObject(window);
                    continue;
                }

                var locationUrl = GetComString(window, "LocationURL");
                if (TryConvertLocationUrlToPath(locationUrl, out var locationPath)
                    && PathsEqual(targetPath, NormalizePathForComparison(locationPath)))
                {
                    var matchedWindowHandle = GetComIntPtr(window, "HWND");
                    ReleaseComObject(window);
                    if (matchedWindowHandle != IntPtr.Zero)
                    {
                        windowHandle = matchedWindowHandle;
                        return true;
                    }

                    continue;
                }

                ReleaseComObject(window);
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            ReleaseComObject(windows);
            ReleaseComObject(shell);
        }

        return false;
    }

    private static bool TryGetForegroundSelectionCore(out ExplorerSelection selection)
    {
        selection = new ExplorerSelection();
        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
        {
            return false;
        }

        object? shell = null;
        object? windows = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return false;
            }

            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return false;
            }

            windows = shellType.InvokeMember(
                "Windows",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: null,
                culture: CultureInfo.InvariantCulture);

            foreach (var window in EnumerateComCollection(windows))
            {
                if (!IsExplorerWindow(window))
                {
                    ReleaseComObject(window);
                    continue;
                }

                var matchedWindowHandle = GetComIntPtr(window, "HWND");
                if (matchedWindowHandle != foregroundWindow)
                {
                    ReleaseComObject(window);
                    continue;
                }

                var selectedPath = GetFirstSelectedPath(window);
                if (string.IsNullOrWhiteSpace(selectedPath))
                {
                    ReleaseComObject(window);
                    return false;
                }

                selection = new ExplorerSelection
                {
                    Path = NormalizePath(selectedPath),
                    Kind = Directory.Exists(selectedPath) ? "dir" : "file"
                };
                ReleaseComObject(window);
                return !string.IsNullOrWhiteSpace(selection.Path);
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            ReleaseComObject(windows);
            ReleaseComObject(shell);
        }

        return false;
    }

    private static string GetFirstSelectedPath(object window)
    {
        object? document = null;
        object? selectedItems = null;
        object? selectedItem = null;
        try
        {
            document = GetComProperty(window, "Document");
            if (document is null)
            {
                return string.Empty;
            }

            selectedItems = InvokeComMethod(document, "SelectedItems");
            if (selectedItems is null)
            {
                return string.Empty;
            }

            if (GetComInt32(selectedItems, "Count") <= 0)
            {
                return string.Empty;
            }

            selectedItem = InvokeComMethod(selectedItems, "Item", 0);
            return selectedItem is null ? string.Empty : GetComString(selectedItem, "Path");
        }
        catch
        {
            return string.Empty;
        }
        finally
        {
            ReleaseComObject(selectedItem);
            ReleaseComObject(selectedItems);
            ReleaseComObject(document);
        }
    }

    private static IEnumerable<object> EnumerateComCollection(object? collection)
    {
        if (collection is null)
        {
            yield break;
        }

        if (collection is System.Collections.IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (item is not null)
                {
                    yield return item;
                }
            }

            yield break;
        }

        var count = GetComInt32(collection, "Count");
        for (var index = 0; index < count; index++)
        {
            var item = InvokeComMethod(collection, "Item", index);
            if (item is not null)
            {
                yield return item;
            }
        }
    }

    private static bool IsExplorerWindow(object window)
    {
        var fullName = GetComString(window, "FullName");
        return fullName.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryNavigateExplorerWindow(object window, string directory)
    {
        try
        {
            InvokeComMethod(window, "Navigate2", directory);
            return true;
        }
        catch
        {
            try
            {
                InvokeComMethod(window, "Navigate", directory);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private static bool TryConvertLocationUrlToPath(string locationUrl, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(locationUrl)
            || !Uri.TryCreate(locationUrl, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        path = NormalizePath(uri.LocalPath);
        return !string.IsNullOrWhiteSpace(path);
    }

    private static object? InvokeComMethod(object comObject, string memberName, params object[] args)
    {
        return comObject.GetType().InvokeMember(
            memberName,
            BindingFlags.InvokeMethod,
            binder: null,
            target: comObject,
            args: args,
            culture: CultureInfo.InvariantCulture);
    }

    private static object? GetComProperty(object comObject, string propertyName)
    {
        return comObject.GetType().InvokeMember(
            propertyName,
            BindingFlags.GetProperty,
            binder: null,
            target: comObject,
            args: null,
            culture: CultureInfo.InvariantCulture);
    }

    private static string GetComString(object comObject, string propertyName)
    {
        return Convert.ToString(GetComProperty(comObject, propertyName), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static int GetComInt32(object comObject, string propertyName)
    {
        return Convert.ToInt32(GetComProperty(comObject, propertyName), CultureInfo.InvariantCulture);
    }

    private static IntPtr GetComIntPtr(object comObject, string propertyName)
    {
        var value = GetComProperty(comObject, propertyName);
        return value switch
        {
            int intValue => new IntPtr(intValue),
            long longValue => new IntPtr(longValue),
            _ => IntPtr.Zero
        };
    }

    private static string NormalizePath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Trim().Replace('/', '\\');
    }

    private static string NormalizePathForComparison(string? path)
    {
        var normalized = NormalizePath(path);
        while (normalized.Length > 1 && normalized.EndsWith('\\'))
        {
            if (normalized.Length == 3 && normalized[1] == ':' && normalized[2] == '\\')
            {
                break;
            }

            normalized = normalized[..^1];
        }

        return normalized;
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldReuseWindowForDirectory(string targetPath, string windowPath)
    {
        if (PathsEqual(targetPath, windowPath))
        {
            return true;
        }

        var targetShareRoot = GetUncShareRoot(targetPath);
        if (string.IsNullOrWhiteSpace(targetShareRoot))
        {
            return false;
        }

        var windowShareRoot = GetUncShareRoot(windowPath);
        return PathsEqual(targetShareRoot, windowShareRoot);
    }

    private static string GetUncShareRoot(string path)
    {
        var normalized = NormalizePathForComparison(path);
        if (!normalized.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var parts = normalized[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return normalized;
        }

        return $@"\\{parts[0]}\{parts[1]}";
    }

    private static string QuoteArgument(string value)
    {
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static void BringWindowToForeground(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        if (IsIconic(windowHandle))
        {
            ShowWindow(windowHandle, SW_RESTORE);
        }
        else
        {
            ShowWindow(windowHandle, SW_SHOW);
        }

        var currentThread = GetCurrentThreadId();
        var targetThread = GetWindowThreadProcessId(windowHandle, out _);
        var foregroundWindow = GetForegroundWindow();
        var foregroundThread = foregroundWindow == IntPtr.Zero
            ? 0
            : GetWindowThreadProcessId(foregroundWindow, out _);

        var attachedTarget = false;
        var attachedForeground = false;
        try
        {
            if (targetThread != 0 && targetThread != currentThread)
            {
                attachedTarget = AttachThreadInput(currentThread, targetThread, true);
            }

            if (foregroundThread != 0 && foregroundThread != currentThread && foregroundThread != targetThread)
            {
                attachedForeground = AttachThreadInput(currentThread, foregroundThread, true);
            }

            BringWindowToTop(windowHandle);
            SetForegroundWindow(windowHandle);
            SetWindowPos(windowHandle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            SetWindowPos(windowHandle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            SetForegroundWindow(windowHandle);
        }
        finally
        {
            if (attachedForeground)
            {
                AttachThreadInput(currentThread, foregroundThread, false);
            }

            if (attachedTarget)
            {
                AttachThreadInput(currentThread, targetThread, false);
            }
        }
    }

    private static void ReleaseComObject(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.FinalReleaseComObject(comObject);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
}

internal sealed class ExplorerSelection
{
    public string Path { get; init; } = string.Empty;

    public string Kind { get; init; } = "file";
}
