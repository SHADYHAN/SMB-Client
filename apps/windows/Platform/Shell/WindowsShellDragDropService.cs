using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.Services.FileTransfers;

namespace Rynat.WindowsClient.Platform.Shell;

public sealed class WindowsShellDragDropService : IWindowsShellDragDropService
{
    private const string FileGroupDescriptorW = "FileGroupDescriptorW";
    private const string FileContents = "FileContents";
    private const uint FdAttributes = 0x00000004;
    private const uint FdWriteTime = 0x00000020;
    private const uint FdFileSize = 0x00000040;
    private const uint FileAttributeNormal = 0x00000080;

    public bool CanStartDrag(IReadOnlyList<RemoteFileItem> selection)
    {
        return selection.Count == 1 && selection.All(item => !item.IsDirectory);
    }

    public bool StartDrag(object dragSource, IReadOnlyList<DragFilePayload> files)
    {
        if (dragSource is not DependencyObject dependencyObject || files.Count == 0)
        {
            return false;
        }

        using var payload = VirtualFileDragPayload.Create(files[0]);
        var effect = DragDrop.DoDragDrop(dependencyObject, payload.DataObject, DragDropEffects.Copy);
        return effect == DragDropEffects.Copy;
    }

    private sealed class VirtualFileDragPayload : IDisposable
    {
        private readonly List<IDisposable> _disposables = new();

        private VirtualFileDragPayload(DataObject dataObject)
        {
            DataObject = dataObject;
        }

        public DataObject DataObject { get; }

        public static VirtualFileDragPayload Create(DragFilePayload file)
        {
            var dataObject = new DataObject();
            var descriptor = BuildFileGroupDescriptor(file);
            var content = file.OpenReadStream();

            dataObject.SetData(FileGroupDescriptorW, descriptor, false);
            dataObject.SetData(FileContents, content, false);

            var payload = new VirtualFileDragPayload(dataObject);
            payload._disposables.Add(descriptor);
            payload._disposables.Add(content);
            return payload;
        }

        public void Dispose()
        {
            foreach (var disposable in _disposables)
            {
                disposable.Dispose();
            }
        }
    }

    private static MemoryStream BuildFileGroupDescriptor(DragFilePayload file)
    {
        var descriptor = new FileDescriptorW
        {
            DwFlags = FdAttributes | FdWriteTime | FdFileSize,
            DwFileAttributes = FileAttributeNormal,
            FtLastWriteTime = file.ModifiedAt is null ? default : ToFileTime(file.ModifiedAt.Value),
            NFileSizeHigh = (uint)(file.Size >> 32),
            NFileSizeLow = (uint)(file.Size & 0xffffffff),
            CFileName = TruncateFileName(file.FileName)
        };

        var descriptorSize = Marshal.SizeOf<FileDescriptorW>();
        var buffer = new byte[sizeof(uint) + descriptorSize];
        BitConverter.GetBytes(1u).CopyTo(buffer, 0);

        var pointer = Marshal.AllocHGlobal(descriptorSize);
        try
        {
            Marshal.StructureToPtr(descriptor, pointer, false);
            Marshal.Copy(pointer, buffer, sizeof(uint), descriptorSize);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }

        return new MemoryStream(buffer);
    }

    private static FileTime ToFileTime(DateTimeOffset timestamp)
    {
        var fileTime = timestamp.ToFileTime();
        return new FileTime
        {
            DwLowDateTime = (uint)(fileTime & 0xffffffff),
            DwHighDateTime = (uint)(fileTime >> 32)
        };
    }

    private static string TruncateFileName(string fileName)
    {
        return fileName.Length <= 259 ? fileName : fileName[..259];
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileDescriptorW
    {
        public uint DwFlags;
        public Guid Clsid;
        public SizeL Size;
        public PointL Point;
        public uint DwFileAttributes;
        public FileTime FtCreationTime;
        public FileTime FtLastAccessTime;
        public FileTime FtLastWriteTime;
        public uint NFileSizeHigh;
        public uint NFileSizeLow;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string CFileName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SizeL
    {
        public int Cx;
        public int Cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointL
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint DwLowDateTime;
        public uint DwHighDateTime;
    }
}
