using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;

namespace Rynat.WindowsClient.UI.Main;

public sealed class WindowsServerSession
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<DirectoryItemViewModel>> _directoryCache = new();

    public WindowsServerSession(
        string connectionId,
        string host,
        string dialectLabel,
        ServerProfileListItem profile,
        IReadOnlyList<ShareListItem> shares
    )
    {
        ConnectionId = connectionId;
        Host = host;
        DialectLabel = dialectLabel;
        Profile = profile;
        Shares = shares;
        CurrentDisplayPath = "/";
    }

    public string ConnectionId { get; }

    public string Host { get; }

    public string DialectLabel { get; }

    public ServerProfileListItem Profile { get; }

    public IReadOnlyList<ShareListItem> Shares { get; }

    public string CurrentDisplayPath { get; private set; }

    public string? ActiveShareName { get; private set; }

    public IReadOnlyList<DirectoryItemViewModel> CurrentItems =>
        _directoryCache.TryGetValue(CurrentDisplayPath, out var items)
            ? items
            : [];

    public bool HasCached(string displayPath) => _directoryCache.ContainsKey(NormalizeDisplayPath(displayPath));

    public IReadOnlyList<DirectoryItemViewModel> CachedItemsFor(string displayPath) =>
        _directoryCache.TryGetValue(NormalizeDisplayPath(displayPath), out var items)
            ? items
            : [];

    public void NavigateTo(string displayPath)
    {
        CurrentDisplayPath = NormalizeDisplayPath(displayPath);
        ActiveShareName = ResolveLocation(CurrentDisplayPath)?.ShareName;
    }

    public void CacheDirectory(string displayPath, IReadOnlyList<DirectoryItemViewModel> items)
    {
        var normalized = NormalizeDisplayPath(displayPath);
        _directoryCache[normalized] = items;
    }

    public void InvalidateDirectory(string displayPath)
    {
        _directoryCache.TryRemove(NormalizeDisplayPath(displayPath), out _);
    }

    public DirectoryLocation? ResolveCurrentLocation()
    {
        return ResolveLocation(CurrentDisplayPath);
    }

    public DirectoryLocation? ResolveLocation(string displayPath)
    {
        var normalized = NormalizeDisplayPath(displayPath);
        if (normalized == "/")
        {
            return null;
        }

        var parts = normalized
            .Split('/', System.StringSplitOptions.RemoveEmptyEntries)
            .ToArray();

        if (parts.Length == 0)
        {
            return null;
        }

        var shareName = parts[0];
        var remotePath = parts.Length == 1
            ? "/"
            : "/" + string.Join("/", parts.Skip(1));

        return new DirectoryLocation(shareName, remotePath);
    }

    public static string NormalizeDisplayPath(string path)
    {
        var trimmed = (path ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed == "/")
        {
            return "/";
        }

        var normalized = trimmed.StartsWith('/') ? trimmed : "/" + trimmed;
        return normalized.TrimEnd('/');
    }

    public static string GetParentDisplayPath(string path)
    {
        var normalized = NormalizeDisplayPath(path);
        if (normalized == "/")
        {
            return "/";
        }

        var lastSlashIndex = normalized.LastIndexOf('/');
        if (lastSlashIndex <= 0)
        {
            return "/";
        }

        return normalized[..lastSlashIndex];
    }
}

public sealed record DirectoryLocation(
    string ShareName,
    string RemotePath
);
