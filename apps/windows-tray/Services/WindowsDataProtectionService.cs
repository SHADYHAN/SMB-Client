using System.Runtime.InteropServices;

namespace Rynat.WindowsTray.Services;

internal static class WindowsDataProtectionService
{
    public static byte[] Protect(byte[] plainText, byte[] entropy)
    {
        if (!OperatingSystem.IsWindows())
        {
            return plainText;
        }

        return ProtectOrUnprotect(plainText, entropy, protect: true);
    }

    public static byte[] Unprotect(byte[] protectedData, byte[] entropy)
    {
        if (!OperatingSystem.IsWindows())
        {
            return protectedData;
        }

        return ProtectOrUnprotect(protectedData, entropy, protect: false);
    }

    private static byte[] ProtectOrUnprotect(byte[] input, byte[] entropy, bool protect)
    {
        var inputBlob = DataBlob.FromBytes(input);
        var entropyBlob = DataBlob.FromBytes(entropy);
        var outputBlob = new DataBlob();

        try
        {
            var ok = protect
                ? CryptProtectData(ref inputBlob, null, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, 0, ref outputBlob)
                : CryptUnprotectData(ref inputBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, 0, ref outputBlob);

            if (!ok)
            {
                Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
            }

            return outputBlob.ToBytes();
        }
        finally
        {
            inputBlob.FreeHGlobal();
            entropyBlob.FreeHGlobal();
            outputBlob.FreeLocal();
        }
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? dataDescription,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        ref DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr dataDescription,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        ref DataBlob dataOut);

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Count;

        public IntPtr Data;

        public static DataBlob FromBytes(byte[] bytes)
        {
            if (bytes.Length == 0)
            {
                return new DataBlob();
            }

            var data = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, data, bytes.Length);
            return new DataBlob
            {
                Count = bytes.Length,
                Data = data
            };
        }

        public readonly byte[] ToBytes()
        {
            if (Count <= 0 || Data == IntPtr.Zero)
            {
                return Array.Empty<byte>();
            }

            var bytes = new byte[Count];
            Marshal.Copy(Data, bytes, 0, Count);
            return bytes;
        }

        public void FreeHGlobal()
        {
            if (Data == IntPtr.Zero)
            {
                return;
            }

            Marshal.FreeHGlobal(Data);
            Data = IntPtr.Zero;
            Count = 0;
        }

        public void FreeLocal()
        {
            if (Data == IntPtr.Zero)
            {
                return;
            }

            WindowsDataProtectionService.LocalFree(Data);
            Data = IntPtr.Zero;
            Count = 0;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr handle);
}
