import AppKit
import Foundation

// MARK: - Preview

extension WorkspaceController {
    func updatePreviewPanel(item: RynatFileItem) {
        previewImageView.contentTintColor = item.isDirectory ? RynatUI.folder.withAlphaComponent(0.82) : RynatUI.muted
        previewImageView.image = NSImage(systemSymbolName: RynatFileIcon.symbolName(for: item), accessibilityDescription: item.typeLabel)
        previewTitleField.stringValue = item.name
        previewTitleField.toolTip = item.name

        if item.isDirectory {
            previewMetaField.stringValue = directoryDetailLabel(for: item)
            playButton.isEnabled = false
            return
        }

        do {
            let plan = try previewPlan(for: item)
            let playback = plan.playback?.kind ?? "无播放"
            previewMetaField.stringValue = "\(contentTypeLabel(plan.contentType)) · \(plan.thumbnail?.kind ?? "无缩略图") · \(playback)"
            playButton.isEnabled = plan.contentType == "video"
            renderLocalThumbnailIfAvailable(for: item, plan: plan)
        } catch {
            previewImageView.image = NSImage(systemSymbolName: "doc", accessibilityDescription: "文件")
            previewMetaField.stringValue = "预览失败"
            playButton.isEnabled = false
            appendLog("Preview plan failed: \(error.localizedDescription)")
        }
    }

    func renderLocalThumbnailIfAvailable(for item: RynatFileItem, plan: PreviewPlan) {
        if let localURL = item.localPreviewURL {
            renderThumbnail(for: item, localURL: localURL, plan: plan)
            return
        }

        guard let location = session?.location(for: item), plan.thumbnail != nil else {
            previewImageView.image = NSImage(systemSymbolName: previewSymbol(for: plan.contentType), accessibilityDescription: "预览")
            previewImageView.contentTintColor = plan.contentType == "unsupported" ? .tertiaryLabelColor : RynatUI.accent
            return
        }

        previewMetaField.stringValue = "正在缓存预览..."
        cacheRemotePreviewFile(
            item: item,
            share: location.share,
            remotePath: location.remotePath,
            plan: plan
        )
    }

    func cacheRemotePreviewFile(item: RynatFileItem, share: String, remotePath: String, plan: PreviewPlan) {
        let destination = previewCacheURL(for: item, share: share, remotePath: remotePath, plan: plan)
        if FileManager.default.fileExists(atPath: destination.path) {
            renderThumbnail(for: item, localURL: destination, plan: plan)
            return
        }

        guard let connectionID = session?.connectionID else {
            previewMetaField.stringValue = "未连接"
            return
        }
        let maxBytes = previewCacheLimitBytes(for: plan)
        previewCoordinator.cache(
            kind: .preview,
            itemPath: item.path,
            share: share,
            remotePath: remotePath,
            localURL: destination,
            maxBytes: maxBytes,
            connectionID: connectionID,
            serverProfileID: session?.server.id
        ) { [weak self] result in
            guard let self, self.currentItem().path == item.path else {
                return
            }
            switch result {
            case .success:
                self.renderThumbnail(for: item, localURL: destination, plan: plan)
            case .failure(let error):
                self.previewImageView.image = NSImage(systemSymbolName: self.previewSymbol(for: plan.contentType), accessibilityDescription: "预览")
                self.previewImageView.contentTintColor = RynatUI.accent
                self.appendLog("Preview cache failed: \(error.localizedDescription)")
                self.previewMetaField.stringValue = "预览失败"
            }
        }
    }

    func cacheVideoForPlayback(item: RynatFileItem, plan: PreviewPlan) {
        guard let location = session?.location(for: item) else {
            setActivityMessage("无法确定文件位置")
            setStatus("播放失败")
            return
        }
        let destination = previewCacheURL(for: item, share: location.share, remotePath: location.remotePath, plan: plan)
        if FileManager.default.fileExists(atPath: destination.path) {
            NSWorkspace.shared.open(destination)
            setStatus("已打开播放器")
            return
        }

        setActivityMessage("正在准备播放\n\(item.name)")
        setStatus("正在缓存视频")
        guard let connectionID = session?.connectionID else {
            setStatus("未连接")
            return
        }
        previewCoordinator.cache(
            kind: .playback,
            itemPath: item.path,
            share: location.share,
            remotePath: location.remotePath,
            localURL: destination,
            connectionID: connectionID,
            serverProfileID: session?.server.id
        ) { [weak self] result in
            guard let self else {
                return
            }
            switch result {
            case .success:
                NSWorkspace.shared.open(destination)
                self.setStatus("已打开播放器")
            case .failure(let error):
                self.appendLog("Video cache failed: \(error.localizedDescription)")
                self.setActivityMessage("播放失败，请重试")
                self.setStatus("播放失败")
            }
        }
    }

    func renderThumbnail(for item: RynatFileItem, localURL: URL, plan: PreviewPlan) {
        previewMetaField.stringValue = "正在生成缩略图..."
        previewService.renderThumbnail(for: localURL, plan: plan) { [weak self] result in
            DispatchQueue.main.async {
                guard let self, self.currentItem().path == item.path else {
                    return
                }
                switch result {
                case .success(let rendered):
                    self.previewImageView.image = rendered.image
                    self.previewImageView.contentTintColor = nil
                    self.previewMetaField.stringValue = "\(self.contentTypeLabel(plan.contentType)) · \(rendered.width)x\(rendered.height)"
                case .failure(let error):
                    self.previewImageView.image = NSImage(systemSymbolName: self.previewSymbol(for: plan.contentType), accessibilityDescription: "预览")
                    self.previewImageView.contentTintColor = RynatUI.accent
                    self.appendLog("Thumbnail render failed: \(error.localizedDescription)")
                    self.previewMetaField.stringValue = "缩略图生成失败"
                }
            }
        }
    }

    func previewCacheURL(for item: RynatFileItem, share: String, remotePath: String, plan: PreviewPlan) -> URL {
        prunePreviewCacheIfNeeded()
        let ext = URL(fileURLWithPath: item.name).pathExtension
        let filename = ext.isEmpty ? plan.cacheKey : "\(plan.cacheKey).\(ext)"
        return previewCacheRoot()
            .appendingPathComponent(share, isDirectory: true)
            .appendingPathComponent(filename)
    }

    func previewCacheLimitBytes(for plan: PreviewPlan) -> UInt64? {
        switch plan.contentType {
        case "video":
            return 24 * 1024 * 1024
        case "image":
            return 32 * 1024 * 1024
        case "pdf":
            return 16 * 1024 * 1024
        default:
            return nil
        }
    }

    func previewCacheRoot() -> URL {
        FileManager.default
            .homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Caches/RYNATClient/Previews", isDirectory: true)
    }

    func prunePreviewCacheIfNeeded(maxBytes: Int64 = 2 * 1024 * 1024 * 1024) {
        let defaultsKey = "previewCache.lastPrune"
        let now = Date()
        let lastPrune = UserDefaults.standard.object(forKey: defaultsKey) as? Date
        if let lastPrune, now.timeIntervalSince(lastPrune) < 6 * 60 * 60 {
            return
        }
        UserDefaults.standard.set(now, forKey: defaultsKey)
        let root = previewCacheRoot()
        DispatchQueue.global(qos: .utility).async {
            pruneCacheDirectory(root, maxBytes: maxBytes)
        }
    }
}

private func pruneCacheDirectory(_ root: URL, maxBytes: Int64) {
    let manager = FileManager.default
    let keys: [URLResourceKey] = [
        .isRegularFileKey,
        .fileSizeKey,
        .contentAccessDateKey,
        .contentModificationDateKey,
    ]
    guard let enumerator = manager.enumerator(
        at: root,
        includingPropertiesForKeys: keys,
        options: [.skipsHiddenFiles]
    ) else {
        return
    }

    var files: [(url: URL, size: Int64, date: Date)] = []
    var totalBytes: Int64 = 0
    for case let fileURL as URL in enumerator {
        guard let values = try? fileURL.resourceValues(forKeys: Set(keys)),
              values.isRegularFile == true else {
            continue
        }
        let size = Int64(values.fileSize ?? 0)
        let date = values.contentAccessDate ?? values.contentModificationDate ?? .distantPast
        totalBytes += size
        files.append((fileURL, size, date))
    }

    guard totalBytes > maxBytes else {
        return
    }
    for file in files.sorted(by: { $0.date < $1.date }) {
        try? manager.removeItem(at: file.url)
        totalBytes -= file.size
        if totalBytes <= maxBytes {
            break
        }
    }
}
