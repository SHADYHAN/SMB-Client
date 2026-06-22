import AppKit
import Foundation

// MARK: - Helpers

extension WorkspaceController {
    func selectedItems() -> [RynatFileItem] {
        let rows = fileListController.selectedItems()
        if !rows.isEmpty {
            return rows
        }
        let item = currentItem()
        return item.path == "/" ? [] : [item]
    }

    func makeClipboard(mode: FileClipboardMode) -> FileClipboard? {
        let entries = selectedItems().compactMap { item -> FileClipboardEntry? in
            guard !isShareRootItem(item), let location = session?.location(for: item) else {
                return nil
            }
            return FileClipboardEntry(
                name: item.name,
                share: location.share,
                remotePath: location.remotePath,
                displayPath: item.path,
                isDirectory: item.isDirectory
            )
        }
        guard !entries.isEmpty else {
            return nil
        }
        return FileClipboard(mode: mode, entries: entries)
    }

    func currentServer() -> RynatServerProfile {
        if let server = session?.server {
            return server
        }
        return RynatServerProfile(
            id: "",
            connectionID: UUID().uuidString,
            name: "",
            host: "",
            protocolLabel: "SMB3 自动",
            accountName: "",
            rememberPassword: false,
            autoLogin: false,
            shares: []
        )
    }

    func currentItem() -> RynatFileItem {
        selectedItem ?? visibleItems.first ?? RynatFileItem(name: "共享", path: session?.currentPath ?? "/", kind: .dir, sizeBytes: nil, modifiedAt: nil)
    }

    func currentServerLine() -> String {
        let server = currentServer()
        if let location = session?.activeLocation() {
            return "\(server.host) / \(location.share)"
        }
        return server.host
    }

    func displayDirectoryPath() -> String {
        session?.currentPath ?? "/"
    }

    func appendPathComponent(directory: String, fileName: String) -> String {
        directory == "/" ? "/\(fileName)" : "\(directory)/\(fileName)"
    }

    func remoteParentPath(for remotePath: String) -> String {
        RemotePath.parent(remotePath)
    }

    func isShareRootItem(_ item: RynatFileItem) -> Bool {
        guard item.isDirectory, let location = session?.location(for: item) else {
            return false
        }
        return location.remotePath == "/"
    }

    func breadcrumbText() -> String {
        let path = session?.currentPath ?? "/"
        if path == "/" {
            return "全部共享"
        }
        return path.split(separator: "/").joined(separator: " / ")
    }

    func selectedItemNames() -> String {
        let rows = fileListController.selectedItems()
        let names = rows.isEmpty ? [currentItem().name] : rows.map(\.name)
        return names.joined(separator: "、")
    }

    func setActivityMessage(_ message: String) {
        if let headline = message.split(separator: "\n", omittingEmptySubsequences: true).first {
            setStatus(String(headline))
        }
    }

    func showMainWindow() {
        if window == nil {
            buildWindow()
        }
        window?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    func setStatus(_ message: String) {
        statusField.stringValue = message
    }

    func appendLog(_ message: String) {
        let stamp = ISO8601DateFormatter().string(from: Date())
        let line = "[\(stamp)] \(message)\n\n"
        let url = logURL
        logQueue.async {
            do {
                try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
                if FileManager.default.fileExists(atPath: url.path),
                   let handle = try? FileHandle(forWritingTo: url) {
                    defer {
                        try? handle.close()
                    }
                    try handle.seekToEnd()
                    try handle.write(contentsOf: Data(line.utf8))
                } else {
                    try line.write(to: url, atomically: true, encoding: .utf8)
                }
            } catch {
                NSLog("RYNATClient log write failed: \(error.localizedDescription)")
            }
        }
    }
}
