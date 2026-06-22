import Foundation

struct RynatShare {
    let name: String
    let comment: String
}

struct RynatServerProfile {
    let id: String
    let connectionID: String
    let name: String
    let host: String
    let protocolLabel: String
    let accountName: String
    let rememberPassword: Bool
    let autoLogin: Bool
    let shares: [RynatShare]

    var share: String {
        shares.first?.name ?? ""
    }

    static func fromStored(
        _ profile: StoredServerProfile,
        credential: StoredServerCredential? = nil,
        shares: [RynatShare] = [],
        protocolLabel: String = "SMB3 自动"
    ) -> RynatServerProfile {
        RynatServerProfile(
            id: profile.id,
            connectionID: profile.id.isEmpty ? UUID().uuidString : profile.id,
            name: profile.displayName,
            host: profile.linkHost,
            protocolLabel: protocolLabel,
            accountName: credential?.username ?? profile.username ?? "",
            rememberPassword: credential?.rememberPassword ?? false,
            autoLogin: credential?.autoLogin ?? false,
            shares: shares
        )
    }
}

final class RynatFileItem: NSObject {
    let name: String
    let path: String
    let shareName: String?
    let remotePath: String
    let kind: RynatLinkKind
    let sizeBytes: Int64?
    let modifiedAt: Date?
    let localPreviewURL: URL?

    init(
        name: String,
        path: String,
        shareName: String? = nil,
        remotePath: String? = nil,
        kind: RynatLinkKind,
        sizeBytes: Int64?,
        modifiedAt: Date?,
        localPreviewURL: URL? = nil
    ) {
        self.name = name
        self.path = path
        self.shareName = shareName
        self.remotePath = remotePath ?? path
        self.kind = kind
        self.sizeBytes = sizeBytes
        self.modifiedAt = modifiedAt
        self.localPreviewURL = localPreviewURL
        super.init()
    }

    var isDirectory: Bool {
        kind == .dir
    }

    var fileExtension: String {
        URL(fileURLWithPath: name).pathExtension.lowercased()
    }

    var typeLabel: String {
        if isDirectory {
            return "文件夹"
        }

        switch fileExtension {
        case "mp4", "mov", "m4v", "mkv", "avi", "webm":
            return "视频"
        case "jpg", "jpeg", "png", "gif", "webp", "heic", "heif", "avif":
            return "图片"
        case "pdf":
            return "PDF"
        case "doc", "docx":
            return "Word"
        case "xls", "xlsx":
            return "表格"
        default:
            return fileExtension.isEmpty ? "文件" : fileExtension.uppercased()
        }
    }

    var sizeLabel: String {
        guard let sizeBytes else {
            return isDirectory ? "--" : "未知"
        }
        return RynatFileItem.byteFormatter.string(fromByteCount: sizeBytes)
    }

    var modifiedLabel: String {
        guard let modifiedAt else {
            return "--"
        }
        return RynatFileItem.dateFormatter.string(from: modifiedAt)
    }

    func matchesSearch(_ query: String) -> Bool {
        guard !query.isEmpty else {
            return true
        }

        let haystack = "\(name) \(path) \(typeLabel)".lowercased()
        return haystack.contains(query)
    }

    private static let byteFormatter: ByteCountFormatter = {
        let formatter = ByteCountFormatter()
        formatter.allowedUnits = [.useKB, .useMB, .useGB, .useTB]
        formatter.countStyle = .file
        return formatter
    }()

    private static let dateFormatter: DateFormatter = {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "zh_CN")
        formatter.dateFormat = "MM-dd HH:mm"
        return formatter
    }()
}

func parentPath(for remotePath: String) -> String {
    guard remotePath != "/" else {
        return "/"
    }

    let parts = remotePath.split(separator: "/")
    if parts.count <= 1 {
        return "/"
    }
    return "/" + parts.dropLast().joined(separator: "/")
}

enum RynatSidebarTab: String, CaseIterable {
    case shares = "共享"
    case favorites = "收藏"
}

final class RynatWorkspaceSession {
    private(set) var server: RynatServerProfile
    private(set) var share: String
    private(set) var currentPath: String
    private(set) var rootItems: [RynatFileItem]
    private var directoryCache: [String: [RynatFileItem]] = [:]
    private var directoryCacheOrder: [String] = []
    private var loadedDirectories: Set<String> = []
    private(set) var favorites: [QuickLink] = []
    private let maxCachedDirectories = 200

    init(server: RynatServerProfile, rootItems: [RynatFileItem]) {
        self.server = server
        self.share = server.share
        self.currentPath = "/"
        self.rootItems = rootItems
    }

    var connectionID: String {
        server.connectionID
    }

    var currentDirectoryItems: [RynatFileItem] {
        if currentPath == "/" {
            return rootItems
        }
        return directoryCache[currentPath] ?? []
    }

    var breadcrumbSegments: [String] {
        currentPath.split(separator: "/").map(String.init)
    }

    func switchServer(_ server: RynatServerProfile, rootItems: [RynatFileItem]) {
        self.server = server
        self.share = server.share
        self.currentPath = "/"
        self.rootItems = rootItems
        self.directoryCache = [:]
        self.directoryCacheOrder = []
        self.loadedDirectories = []
        self.favorites = []
    }

    func navigate(to path: String) {
        currentPath = normalizedDirectoryPath(path)
        if let location = location(forDisplayPath: currentPath) {
            share = location.share
        }
    }

    func cacheItems(_ items: [RynatFileItem], forDisplayPath path: String) {
        let normalized = normalizedDirectoryPath(path)
        directoryCache[normalized] = items
        loadedDirectories.insert(normalized)
        markDirectoryCacheUsed(normalized)
        trimDirectoryCacheIfNeeded()
    }

    func invalidateDirectory(_ path: String) {
        let normalized = normalizedDirectoryPath(path)
        directoryCache.removeValue(forKey: normalized)
        loadedDirectories.remove(normalized)
        directoryCacheOrder.removeAll { $0 == normalized }
    }

    func cachedItems(forDisplayPath path: String) -> [RynatFileItem]? {
        directoryCache[normalizedDirectoryPath(path)]
    }

    func normalizedDisplayPath(_ path: String) -> String {
        normalizedDirectoryPath(path)
    }

    func hasLoadedDirectory(_ path: String) -> Bool {
        loadedDirectories.contains(normalizedDirectoryPath(path))
    }

    func activeLocation() -> (share: String, remotePath: String)? {
        location(forDisplayPath: currentPath)
    }

    func location(for item: RynatFileItem) -> (share: String, remotePath: String)? {
        if let shareName = item.shareName {
            return (shareName, item.remotePath)
        }
        return location(forDisplayPath: item.path)
    }

    func location(forDisplayPath path: String) -> (share: String, remotePath: String)? {
        let normalized = normalizedDirectoryPath(path)
        guard normalized != "/" else {
            return nil
        }
        let parts = normalized.split(separator: "/").map(String.init)
        guard let share = parts.first, !share.isEmpty else {
            return nil
        }
        if parts.count == 1 {
            return (share, "/")
        }
        return (share, "/" + parts.dropFirst().joined(separator: "/"))
    }

    func addFavorite(_ link: QuickLink) {
        favorites.removeAll { $0.target.serverHost == link.target.serverHost && $0.target.share == link.target.share && $0.target.path == link.target.path }
        favorites.insert(link, at: 0)
    }

    func setFavorites(_ links: [QuickLink]) {
        favorites = links.filter { link in
            link.target.serverHost.caseInsensitiveCompare(server.host) == .orderedSame
        }
    }

    func removeFavorite(id: String) {
        favorites.removeAll { $0.id == id }
    }

    func findItem(path: String) -> RynatFileItem? {
        if let item = rootItems.first(where: { $0.path == path }) {
            return item
        }
        for items in directoryCache.values {
            if let item = items.first(where: { $0.path == path }) {
                return item
            }
        }
        return nil
    }

    func search(_ query: String, global: Bool) -> [RynatFileItem] {
        let normalizedQuery = query.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        guard !normalizedQuery.isEmpty else {
            return currentDirectoryItems
        }
        let scope = global ? searchScopeFromLoadedDirectories() : currentDirectoryItems
        return scope.filter { $0.matchesSearch(normalizedQuery) }
    }

    private func searchScopeFromLoadedDirectories() -> [RynatFileItem] {
        var seenPaths: Set<String> = []
        var result: [RynatFileItem] = []

        func appendUnique(_ item: RynatFileItem) {
            guard !seenPaths.contains(item.path) else {
                return
            }
            seenPaths.insert(item.path)
            result.append(item)
        }

        for item in rootItems {
            appendUnique(item)
        }
        for key in directoryCache.keys.sorted() {
            for item in directoryCache[key] ?? [] {
                appendUnique(item)
            }
        }
        return result
    }

    private func markDirectoryCacheUsed(_ path: String) {
        directoryCacheOrder.removeAll { $0 == path }
        directoryCacheOrder.append(path)
    }

    private func trimDirectoryCacheIfNeeded() {
        while directoryCache.count > maxCachedDirectories, let oldest = directoryCacheOrder.first {
            directoryCacheOrder.removeFirst()
            if oldest == currentPath {
                directoryCacheOrder.append(oldest)
                if directoryCacheOrder.count <= 1 {
                    break
                }
                continue
            }
            directoryCache.removeValue(forKey: oldest)
            loadedDirectories.remove(oldest)
        }
    }

    private func normalizedDirectoryPath(_ path: String) -> String {
        let trimmed = path.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty, trimmed != "/" else {
            return "/"
        }
        return trimmed.hasPrefix("/") ? trimmed : "/\(trimmed)"
    }
}
