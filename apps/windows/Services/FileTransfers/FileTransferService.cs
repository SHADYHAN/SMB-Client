using System.IO;
using System.Security.Cryptography;
using System.Text;
using Rynat.Client;
using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.Infrastructure;

namespace Rynat.WindowsClient.Services.FileTransfers;

public sealed class FileTransferService : IFileTransferService
{
    private readonly RynatCoreBridge _bridge;

    public FileTransferService(RynatCoreBridge bridge)
    {
        _bridge = bridge;
    }

    public Task<DragFilePayloadResult> CreateDragDownloadPayloadAsync(
        ServerSession session,
        IReadOnlyList<RemoteFileItem> items,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult(CreateDragDownloadPayload(session, items, cancellationToken));
    }

    private DragFilePayloadResult CreateDragDownloadPayload(
        ServerSession session,
        IReadOnlyList<RemoteFileItem> items,
        CancellationToken cancellationToken
    )
    {
        if (items.Count == 0)
        {
            return Failure("请先选择文件。", "download.no_selection");
        }

        if (items.Any(item => item.IsDirectory))
        {
            return Failure("暂不支持拖出文件夹。", "download.directory_not_supported");
        }

        var files = new List<DragFilePayload>(items.Count);
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var localPath = DragCachePath(session, item);
            files.Add(new DragFilePayload(
                SafeFileName(item.Name),
                item.Size,
                item.ModifiedAt,
                () => new LazyRemoteDownloadStream(
                    _bridge,
                    session,
                    item,
                    localPath
                )
            ));
        }

        return new DragFilePayloadResult(true, "可以拖出。", files);
    }

    private static DragFilePayloadResult Failure(string summary, string errorCode) =>
        new(false, summary, Array.Empty<DragFilePayload>(), errorCode);

    private sealed class LazyRemoteDownloadStream : Stream
    {
        private readonly RynatCoreBridge _bridge;
        private readonly ServerSession _session;
        private readonly RemoteFileItem _item;
        private readonly string _localPath;
        private FileStream? _inner;

        public LazyRemoteDownloadStream(
            RynatCoreBridge bridge,
            ServerSession session,
            RemoteFileItem item,
            string localPath
        )
        {
            _bridge = bridge;
            _session = session;
            _item = item;
            _localPath = localPath;
        }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => CheckedLength(_item.Size);

        public override long Position
        {
            get => _inner?.Position ?? 0;
            set => EnsureInnerStream().Position = value;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            EnsureInnerStream().Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) =>
            EnsureInnerStream().Read(buffer);

        public override long Seek(long offset, SeekOrigin origin) =>
            EnsureInnerStream().Seek(offset, origin);

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner?.Dispose();
            }

            base.Dispose(disposing);
        }

        private FileStream EnsureInnerStream()
        {
            if (_inner is not null)
            {
                return _inner;
            }

            if (!IsCompleteLocalFile(_localPath, _item.Size))
            {
                DownloadFile(_bridge, _session, _item, _localPath);
            }

            _inner = File.Open(_localPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return _inner;
        }
    }

    private static void DownloadFile(
        RynatCoreBridge bridge,
        ServerSession session,
        RemoteFileItem item,
        string localPath
    )
    {
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        var partialPath = localPath + ".part";
        DeleteIfExists(partialPath);

        try
        {
            var cached = bridge.SmbCacheFile(new SmbCacheFileRequest(
                item.Share,
                item.Path,
                partialPath,
                null,
                session.ConnectionId,
                OperationId("drag-download")
            ));
            ReplaceWithCompletedFile(cached.LocalPath, localPath);
        }
        catch (Exception ex) when (BridgeExceptionClassifier.IsBridgeFailure(ex) || ex is IOException or UnauthorizedAccessException)
        {
            DeleteIfExists(partialPath);
            throw;
        }
    }

    private static string DragCachePath(ServerSession session, RemoteFileItem item)
    {
        var cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Rynat",
            "DragCache",
            SafeFileName(session.ConnectionId),
            StableKey(session.Host, item.Share, item.Path, item.Size.ToString(), item.ModifiedAt?.ToUnixTimeSeconds().ToString() ?? "")
        );

        return Path.Combine(cacheDirectory, SafeFileName(item.Name));
    }

    private static bool IsCompleteLocalFile(string localPath, ulong expectedSize)
    {
        try
        {
            if (!File.Exists(localPath))
            {
                return false;
            }

            var info = new FileInfo(localPath);
            return unchecked((ulong)info.Length) >= expectedSize;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void ReplaceWithCompletedFile(string partialPath, string completedPath)
    {
        if (File.Exists(completedPath))
        {
            File.Delete(completedPath);
        }

        File.Move(partialPath, completedPath);
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string OperationId(string prefix) =>
        prefix + "-" + Guid.NewGuid().ToString("N");

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = string.Concat(value.Select(ch => invalid.Contains(ch) ? '_' : ch));
        return string.IsNullOrWhiteSpace(safe) ? "download" : safe;
    }

    private static string StableKey(params string[] parts)
    {
        var input = string.Join("\n", parts);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..16];
    }

    private static long CheckedLength(ulong size)
    {
        if (size > long.MaxValue)
        {
            throw new IOException("文件过大。");
        }

        return unchecked((long)size);
    }
}
