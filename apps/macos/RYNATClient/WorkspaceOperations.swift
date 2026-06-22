import Foundation

enum RemotePath {
    static func parent(_ path: String) -> String {
        let normalized = normalizedDirectory(path)
        guard normalized != "/", !normalized.isEmpty else {
            return "/"
        }
        let parts = normalized.split(separator: "/").map(String.init)
        guard parts.count > 1 else {
            return "/"
        }
        return "/" + parts.dropLast().joined(separator: "/")
    }

    static func fileName(_ path: String) -> String {
        let normalized = normalizedDirectory(path)
        return normalized == "/" ? "" : normalized.split(separator: "/").last.map(String.init) ?? ""
    }

    static func normalizedDirectory(_ path: String) -> String {
        let trimmed = path.trimmingCharacters(in: .whitespacesAndNewlines).replacingOccurrences(of: "\\", with: "/")
        guard !trimmed.isEmpty, trimmed != "/" else {
            return "/"
        }
        var normalized = trimmed.hasPrefix("/") ? trimmed : "/\(trimmed)"
        while normalized.contains("//") {
            normalized = normalized.replacingOccurrences(of: "//", with: "/")
        }
        if normalized.count > 1 && normalized.hasSuffix("/") {
            normalized.removeLast()
        }
        return normalized
    }
}

enum FileClipboardMode {
    case copy
    case cut
}

struct FileClipboardEntry {
    let name: String
    let share: String
    let remotePath: String
    let displayPath: String
    let isDirectory: Bool
}

struct FileClipboard {
    let mode: FileClipboardMode
    let entries: [FileClipboardEntry]

    var description: String {
        let names = entries.map(\.name).joined(separator: "、")
        return "\(mode == .cut ? "剪切" : "复制")：\(names)"
    }
}

enum ConflictDecision {
    case replace
    case skip
}

struct ExistingRemoteItem {
    let name: String
    let isDirectory: Bool
}

final class OperationTask {
    enum State {
        case running
        case completed
        case cancelled
        case failed
    }

    let id = UUID()
    let operationID = UUID().uuidString
    let title: String
    var completed: Int
    var total: Int
    var state: State
    var isCancelled: Bool

    init(title: String, total: Int = 0) {
        self.title = title
        self.completed = 0
        self.total = total
        self.state = .running
        self.isCancelled = false
    }
}

struct RemoteWriteSessionContext {
    let serverProfileID: String
    let connectionID: String
    let generation: Int
}

struct RemoteWriteRequest {
    let title: String
    let total: Int
    let context: RemoteWriteSessionContext
    let operation: (OperationTask, RemoteWriteSessionContext, @escaping (Int, Int) -> Void) throws -> String
}

struct DirectoryLoadRequest {
    let displayPath: String
    let share: String
    let remotePath: String
    let connectionID: String
    let generation: Int
}

struct DirectoryLoadResult {
    let request: DirectoryLoadRequest
    let items: [RynatFileItem]
}

final class DirectoryLoader {
    private var loadingPaths: Set<String> = []
    private var tokens: [String: UUID] = [:]

    func isLoading(_ displayPath: String) -> Bool {
        loadingPaths.contains(displayPath)
    }

    func begin(_ request: DirectoryLoadRequest) -> UUID? {
        guard !loadingPaths.contains(request.displayPath) else {
            return nil
        }
        let token = UUID()
        loadingPaths.insert(request.displayPath)
        tokens[request.displayPath] = token
        return token
    }

    func complete(displayPath: String, token: UUID) -> Bool {
        guard tokens[displayPath] == token else {
            return false
        }
        loadingPaths.remove(displayPath)
        tokens.removeValue(forKey: displayPath)
        return true
    }

    func cancel(displayPath: String) {
        loadingPaths.remove(displayPath)
        tokens.removeValue(forKey: displayPath)
    }

    func clear() {
        loadingPaths.removeAll()
        tokens.removeAll()
    }

    func load(
        request: DirectoryLoadRequest,
        mapItem: @escaping (SmbFileItem, String) -> RynatFileItem,
        completion: @escaping (Result<DirectoryLoadResult, Error>) -> Void
    ) {
        DispatchQueue.global(qos: .userInitiated).async {
            do {
                let remoteItems = try RynatCore().smbListDirectory(
                    share: request.share,
                    path: request.remotePath,
                    connectionID: request.connectionID
                )
                let items = remoteItems.map { mapItem($0, request.share) }
                DispatchQueue.main.async {
                    completion(.success(DirectoryLoadResult(request: request, items: items)))
                }
            } catch {
                DispatchQueue.main.async {
                    completion(.failure(error))
                }
            }
        }
    }
}

final class RemoteOperationContext {
    private let listDirectory: (String, String) throws -> [SmbFileItem]
    private let log: (String) -> Void
    private var directoryItems: [String: [String: ExistingRemoteItem]] = [:]

    init(
        listDirectory: @escaping (String, String) throws -> [SmbFileItem],
        log: @escaping (String) -> Void
    ) {
        self.listDirectory = listDirectory
        self.log = log
    }

    func itemExists(share: String, path: String) throws -> Bool {
        try existingItem(share: share, path: path) != nil
    }

    func existingItem(share: String, path: String) throws -> ExistingRemoteItem? {
        let parent = RemotePath.parent(path)
        let name = RemotePath.fileName(path)
        let key = "\(share)\u{1f}\(parent)"
        if directoryItems[key] == nil {
            do {
                let items = try listDirectory(share, parent)
                directoryItems[key] = Dictionary(
                    uniqueKeysWithValues: items.map { item in
                        (item.name, ExistingRemoteItem(name: item.name, isDirectory: item.isDir))
                    }
                )
            } catch {
                log("Conflict cache load failed: share=\(share), parent=\(parent), error=\(error.localizedDescription)")
                throw RynatCoreError.bridgeError("无法确认是否存在同名项目", code: "conflict_check_failed")
            }
        }
        return directoryItems[key]?[name]
    }

    func markCreated(share: String, path: String, isDirectory: Bool) {
        updateName(share: share, path: path, isDirectory: isDirectory, insert: true)
    }

    func markDeleted(share: String, path: String) {
        updateName(share: share, path: path, insert: false)
    }

    func invalidate(share: String, directory: String) {
        let key = "\(share)\u{1f}\(RemotePath.normalizedDirectory(directory))"
        directoryItems.removeValue(forKey: key)
    }

    private func updateName(share: String, path: String, isDirectory: Bool = false, insert: Bool) {
        let parent = RemotePath.parent(path)
        let name = RemotePath.fileName(path)
        let key = "\(share)\u{1f}\(parent)"
        guard !name.isEmpty, directoryItems[key] != nil else {
            return
        }
        if insert {
            directoryItems[key]?[name] = ExistingRemoteItem(name: name, isDirectory: isDirectory)
        } else {
            directoryItems[key]?.removeValue(forKey: name)
        }
    }
}
