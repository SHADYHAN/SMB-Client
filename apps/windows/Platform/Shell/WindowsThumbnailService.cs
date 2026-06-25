using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Rynat.WindowsClient.Services.Preview;

namespace Rynat.WindowsClient.Platform.Shell;

public sealed class WindowsThumbnailService : IThumbnailService
{
    private const int SOk = 0;
    private const ShellItemImageFactoryFlags ThumbnailOnly = ShellItemImageFactoryFlags.ThumbnailOnly;

    public bool TryCreateThumbnail(string sourcePath, string destinationPath, int maxEdgePx)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)
            || string.IsNullOrWhiteSpace(destinationPath)
            || maxEdgePx <= 0
            || !File.Exists(sourcePath))
        {
            return false;
        }

        var factoryId = typeof(IShellItemImageFactory).GUID;
        var createResult = SHCreateItemFromParsingName(
            sourcePath,
            IntPtr.Zero,
            ref factoryId,
            out var factory
        );
        if (createResult != SOk || factory is null)
        {
            return false;
        }

        var bitmapHandle = IntPtr.Zero;
        try
        {
            var imageResult = factory.GetImage(
                new NativeSize(maxEdgePx, maxEdgePx),
                ThumbnailOnly,
                out bitmapHandle
            );
            if (imageResult != SOk || bitmapHandle == IntPtr.Zero)
            {
                return false;
            }

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmapHandle,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions()
            );
            source.Freeze();

            var parent = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = File.Create(destinationPath);
            encoder.Save(stream);
            return true;
        }
        finally
        {
            if (bitmapHandle != IntPtr.Zero)
            {
                DeleteObject(bitmapHandle);
            }

            Marshal.ReleaseComObject(factory);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? ppv
    );

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(
            NativeSize size,
            ShellItemImageFactoryFlags flags,
            out IntPtr phbm
        );
    }

    [Flags]
    private enum ShellItemImageFactoryFlags : uint
    {
        ThumbnailOnly = 0x00000008
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeSize
    {
        public NativeSize(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public readonly int Width;

        public readonly int Height;
    }
}
