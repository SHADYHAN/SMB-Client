using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Rynat.WindowsTray.Services;

internal sealed class WindowsSmbSessionService
{
    private const int NoError = 0;
    private const int ErrorMoreData = 234;
    private const int ErrorNoMoreItems = 259;
    private const int ResourceConnected = 1;
    private const int ResourceTypeAny = 0;
    private const int ResourceTypeDisk = 1;
    private const int ConnectTemporary = 0x0000_0004;
    private const int ErrorInvalidPassword = 86;
    private const int ErrorSessionCredentialConflict = 1219;
    private const int ErrorNotConnected = 2250;

    private string? _connectedHost;

    public string? ConnectedHost => _connectedHost;

    public async Task ConnectAsync(string host, string username, string password)
    {
        var normalizedHost = NormalizeHost(host);
        var normalizedUsername = username.Trim();

        if (string.IsNullOrWhiteSpace(normalizedUsername))
        {
            throw new InvalidOperationException("请输入用户名。");
        }

        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException("请输入密码。");
        }

        await Task.Run(() => Connect(normalizedHost, normalizedUsername, password));
    }

    public void DisconnectCurrent()
    {
        if (string.IsNullOrWhiteSpace(_connectedHost))
        {
            return;
        }

        DisconnectHost(_connectedHost);
        _connectedHost = null;
    }

    public static string NormalizeHost(string host)
    {
        var value = host.Trim();
        if (value.StartsWith("smb://", StringComparison.OrdinalIgnoreCase))
        {
            value = value[6..];
        }

        value = value.Trim().Trim('\\', '/').Replace('/', '\\');
        var slashIndex = value.IndexOf('\\');
        if (slashIndex >= 0)
        {
            value = value[..slashIndex];
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("请输入服务器地址。");
        }

        return value;
    }

    private void Connect(string host, string username, string password)
    {
        if (!string.IsNullOrWhiteSpace(_connectedHost)
            && !string.Equals(_connectedHost, host, StringComparison.OrdinalIgnoreCase))
        {
            DisconnectHost(_connectedHost);
            _connectedHost = null;
        }

        var remoteName = IpcRemoteName(host);
        var result = AddConnection(remoteName, username, password);
        if (result == ErrorSessionCredentialConflict)
        {
            DisconnectHost(host);
            result = AddConnection(remoteName, username, password);
        }

        if (result != NoError)
        {
            throw new InvalidOperationException(FriendlyMessage(result));
        }

        _connectedHost = host;
    }

    private static int AddConnection(string remoteName, string username, string password)
    {
        var resource = new NetResource
        {
            Scope = 0,
            Type = ResourceTypeDisk,
            DisplayType = 0,
            Usage = 0,
            LocalName = null,
            RemoteName = remoteName,
            Comment = null,
            Provider = null
        };

        return WNetAddConnection2(ref resource, password, username, ConnectTemporary);
    }

    private static void DisconnectHost(string host)
    {
        foreach (var remoteName in ConnectedRemoteNamesForHost(host).ToArray())
        {
            CancelConnection(remoteName);
        }

        CancelConnection(IpcRemoteName(host));
        CancelConnection($@"\\{host}");
    }

    private static void CancelConnection(string remoteName)
    {
        var result = WNetCancelConnection2(remoteName, 0, true);
        if (result is NoError or ErrorNotConnected)
        {
            return;
        }
    }

    private static string IpcRemoteName(string host) => $@"\\{host}\IPC$";

    private static IEnumerable<string> ConnectedRemoteNamesForHost(string host)
    {
        var result = WNetOpenEnum(ResourceConnected, ResourceTypeAny, 0, IntPtr.Zero, out var enumHandle);
        if (result != NoError)
        {
            yield break;
        }

        try
        {
            var bufferSize = 16 * 1024;
            var buffer = Marshal.AllocHGlobal(bufferSize);
            try
            {
                while (true)
                {
                    var count = -1;
                    var size = bufferSize;
                    result = WNetEnumResource(enumHandle, ref count, buffer, ref size);
                    if (result == ErrorNoMoreItems)
                    {
                        break;
                    }

                    if (result == ErrorMoreData)
                    {
                        Marshal.FreeHGlobal(buffer);
                        bufferSize = size;
                        buffer = Marshal.AllocHGlobal(bufferSize);
                        continue;
                    }

                    if (result != NoError)
                    {
                        break;
                    }

                    var structSize = Marshal.SizeOf<NetResource>();
                    for (var index = 0; index < count; index++)
                    {
                        var itemPtr = IntPtr.Add(buffer, index * structSize);
                        var resource = Marshal.PtrToStructure<NetResource>(itemPtr);
                        var remoteName = resource.RemoteName;
                        if (IsRemoteNameForHost(remoteName, host))
                        {
                            yield return remoteName;
                        }
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            WNetCloseEnum(enumHandle);
        }
    }

    private static bool IsRemoteNameForHost(string? remoteName, string host)
    {
        if (string.IsNullOrWhiteSpace(remoteName))
        {
            return false;
        }

        var serverRoot = $@"\\{host}";
        return remoteName.Equals(serverRoot, StringComparison.OrdinalIgnoreCase)
            || remoteName.StartsWith(serverRoot + @"\", StringComparison.OrdinalIgnoreCase);
    }

    private static string FriendlyMessage(int errorCode)
    {
        return errorCode switch
        {
            ErrorInvalidPassword => "用户名或密码错误，请检查后重试。",
            ErrorSessionCredentialConflict => "Windows 已经用其他账号连接了这个服务器，请先关闭相关 Explorer 窗口或断开旧连接后重试。",
            _ => $"SMB 登录失败：{new Win32Exception(errorCode).Message}（错误码 {errorCode}）"
        };
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NetResource
    {
        public int Scope;
        public int Type;
        public int DisplayType;
        public int Usage;
        public string? LocalName;
        public string RemoteName;
        public string? Comment;
        public string? Provider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int WNetAddConnection2(
        ref NetResource netResource,
        string? password,
        string? username,
        int flags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int WNetCancelConnection2(
        string name,
        int flags,
        [MarshalAs(UnmanagedType.Bool)] bool force);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int WNetOpenEnum(
        int scope,
        int type,
        int usage,
        IntPtr netResource,
        out IntPtr enumHandle);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int WNetEnumResource(
        IntPtr enumHandle,
        ref int count,
        IntPtr buffer,
        ref int bufferSize);

    [DllImport("mpr.dll", SetLastError = false)]
    private static extern int WNetCloseEnum(IntPtr enumHandle);
}
