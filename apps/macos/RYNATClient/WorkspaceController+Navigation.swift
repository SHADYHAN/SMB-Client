import AppKit
import Foundation

// MARK: - Navigation

extension WorkspaceController {
    func navigateToDirectory(_ path: String, selectPath: String? = nil) {
        guard let session else {
            return
        }
        let normalizedPath = session.normalizedDisplayPath(path)
        let wasLoadingTarget = directoryLoader.isLoading(normalizedPath)
        session.navigate(to: normalizedPath)
        directoryLoadGeneration += 1
        if wasLoadingTarget {
            directoryLoader.cancel(displayPath: normalizedPath)
        }
        if path == "/" {
            visibleItems = session.currentDirectoryItems
            selectedItem = visibleItems.first
            searchField.stringValue = ""
            reloadFileList()
            refreshSidebar()
            updateSelectionState()
            return
        }

        if !session.hasLoadedDirectory(session.currentPath), let location = session.activeLocation() {
            if !directoryLoader.isLoading(session.currentPath) {
                visibleItems = []
                selectedItem = nil
                reloadFileList()
            }
            updateSelectionState()
            refreshSidebar()
            loadRemoteDirectory(
                share: location.share,
                remotePath: location.remotePath,
                displayPath: session.currentPath,
                selectPath: selectPath
            )
            return
        }

        visibleItems = session.currentDirectoryItems
        if let selectPath, let item = session.findItem(path: selectPath) {
            selectedItem = item
        } else {
            selectedItem = visibleItems.first
        }
        searchField.stringValue = ""
        reloadFileList()
        refreshSidebar()
        updateSelectionState()
    }

    func loadRemoteDirectory(share: String, remotePath: String, displayPath: String, selectPath: String? = nil) {
        let normalizedDisplayPath = session?.normalizedDisplayPath(displayPath) ?? displayPath
        guard let connectionID = session?.connectionID else {
            setStatus("未连接")
            return
        }
        let request = DirectoryLoadRequest(
            displayPath: normalizedDisplayPath,
            share: share,
            remotePath: remotePath,
            connectionID: connectionID,
            generation: directoryLoadGeneration
        )
        guard let token = directoryLoader.begin(request) else {
            return
        }
        setStatus("正在读取 \(share)\(remotePath == "/" ? "" : remotePath)...")
        directoryLoader.load(
            request: request,
            mapItem: { item, share in WorkspaceController.fileItem(from: item, share: share) }
        ) { [weak self] result in
            guard let self, self.directoryLoader.complete(displayPath: normalizedDisplayPath, token: token) else {
                return
            }
            switch result {
            case .success(let load):
                self.session?.cacheItems(load.items, forDisplayPath: load.request.displayPath)
                guard self.isCurrentVisibleDirectoryLoad(load.request) else {
                    return
                }
                self.applyLoadedDirectory(load.items, displayPath: load.request.displayPath, selectPath: selectPath, refreshSidebarAfterCache: false)
                self.setStatus("已读取 \(load.items.count) 项")
            case .failure(let error):
                guard self.isCurrentVisibleDirectoryLoad(request) else {
                    return
                }
                self.visibleItems = []
                self.selectedItem = nil
                self.reloadFileList()
                self.updateSelectionState()
                self.appendLog("Load directory failed: share=\(share), path=\(remotePath), error=\(error.localizedDescription)")
                self.setActivityMessage("读取目录失败，请重试")
                self.setStatus("读取目录失败")
            }
        }
    }

    func loadSidebarDirectory(_ path: String) {
        guard let session else {
            return
        }
        let normalized = session.normalizedDisplayPath(path)
        guard normalized != "/", !session.hasLoadedDirectory(normalized), !directoryLoader.isLoading(normalized),
              let location = session.location(forDisplayPath: normalized) else {
            return
        }
        let request = DirectoryLoadRequest(
            displayPath: normalized,
            share: location.share,
            remotePath: location.remotePath,
            connectionID: session.connectionID,
            generation: directoryLoadGeneration
        )
        guard let token = directoryLoader.begin(request) else {
            return
        }
        refreshSidebar()
        directoryLoader.load(
            request: request,
            mapItem: { item, share in WorkspaceController.fileItem(from: item, share: share) }
        ) { [weak self] result in
            guard let self, self.directoryLoader.complete(displayPath: normalized, token: token) else {
                return
            }
            switch result {
            case .success(let load):
                self.session?.cacheItems(load.items, forDisplayPath: load.request.displayPath)
                if self.isCurrentVisibleDirectoryLoad(load.request) {
                    self.visibleItems = load.items
                    self.selectedItem = self.itemToSelect(in: load.items, selectPath: nil)
                    self.reloadFileList()
                    self.updateSelectionState()
                }
                self.refreshSidebar()
                self.setStatus("已读取 \(load.items.count) 项")
            case .failure(let error):
                self.refreshSidebar()
                self.appendLog("Load sidebar directory failed: path=\(normalized), error=\(error.localizedDescription)")
                if self.directoryLoadGeneration == request.generation {
                    self.setActivityMessage("读取目录失败，请重试")
                    self.setStatus("读取目录失败")
                }
            }
        }
    }

    func isCurrentVisibleDirectoryLoad(_ request: DirectoryLoadRequest) -> Bool {
        session?.connectionID == request.connectionID &&
            session?.normalizedDisplayPath(session?.currentPath ?? "") == request.displayPath
    }

    func applyLoadedDirectory(_ items: [RynatFileItem], displayPath: String, selectPath: String?, refreshSidebarAfterCache: Bool = true) {
        session?.cacheItems(items, forDisplayPath: displayPath)
        visibleItems = items
        selectedItem = itemToSelect(in: items, selectPath: selectPath)
        searchField.stringValue = ""
        reloadFileList()
        if refreshSidebarAfterCache {
            refreshSidebar()
        }
        updateSelectionState()
    }

    func itemToSelect(in items: [RynatFileItem], selectPath: String?) -> RynatFileItem? {
        if let selectPath, let item = items.first(where: { $0.path == selectPath }) {
            return item
        }
        return items.first
    }

    func reloadCurrentRemoteDirectory(keepSelection: Bool = true) {
        guard let session else {
            return
        }
        let displayPath = session.currentPath
        let selectPath = keepSelection ? selectedItem?.path : nil
        if displayPath == "/" {
            visibleItems = session.currentDirectoryItems
            selectedItem = keepSelection ? selectedItem : visibleItems.first
            reloadFileList(keepSelection: keepSelection)
            refreshSidebar()
            updateSelectionState()
            setStatus("已刷新")
            return
        }
        guard let location = session.activeLocation() else {
            setStatus("无法确定当前目录")
            return
        }
        directoryLoadGeneration += 1
        session.invalidateDirectory(displayPath)
        directoryLoader.cancel(displayPath: displayPath)
        visibleItems = []
        selectedItem = nil
        reloadFileList()
        updateSelectionState()
        loadRemoteDirectory(
            share: location.share,
            remotePath: location.remotePath,
            displayPath: displayPath,
            selectPath: selectPath
        )
    }

    func shareRootItems(for server: RynatServerProfile) -> [RynatFileItem] {
        server.shares.map { share in
            RynatFileItem(
                name: share.name,
                path: "/\(share.name)",
                shareName: share.name,
                remotePath: "/",
                kind: .dir,
                sizeBytes: nil,
                modifiedAt: nil
            )
        }
    }

    static func fileItem(from item: SmbFileItem, share: String) -> RynatFileItem {
        RynatFileItem(
            name: item.name,
            path: Self.displayPathForRemote(share: share, remotePath: item.path),
            shareName: share,
            remotePath: item.path,
            kind: item.isDir ? .dir : .file,
            sizeBytes: item.isDir ? nil : item.size.map { Int64(clamping: $0) },
            modifiedAt: item.modifiedTime.map { Date(timeIntervalSince1970: TimeInterval($0)) }
        )
    }

    func applySearch() {
        guard let session else {
            return
        }
        visibleItems = session.search(searchField.stringValue, global: true)
        selectedItem = visibleItems.first
        reloadFileList()
        updateSelectionState()
    }

    func reloadFileList(keepSelection: Bool = false) {
        let priorPath = keepSelection ? selectedItem?.path : nil
        if let priorPath, let matched = visibleItems.first(where: { $0.path == priorPath }) {
            selectedItem = matched
        }
        fileListController.reload(items: visibleItems, selectedPath: selectedItem?.path)
        updateSummary()
    }

    func refreshSidebar() {
        guard let session else {
            return
        }
        sidebarView.update(session: session, activeTab: activeSidebarTab)
    }

    func updateSummary() {
        let dirs = visibleItems.filter(\.isDirectory).count
        let files = visibleItems.count - dirs
        let totalBytes = visibleItems.reduce(Int64(0)) { $0 + ($1.isDirectory ? 0 : ($1.sizeBytes ?? 0)) }
        fileSummaryField.stringValue = "\(dirs) 个文件夹 · \(files) 个文件 · \(ByteCountFormatter.string(fromByteCount: totalBytes, countStyle: .file))"
    }

    func directoryDetailLabel(for item: RynatFileItem) -> String {
        guard item.isDirectory else {
            return item.typeLabel
        }
        guard let session else {
            return "文件夹"
        }
        let path = session.normalizedDisplayPath(item.path)
        guard session.hasLoadedDirectory(path) else {
            return "文件夹"
        }
        let count = session.cachedItems(forDisplayPath: path)?.count ?? 0
        return "\(count) 项 · 文件夹"
    }

    func updateSelectionState() {
        let item = currentItem()
        let currentPath = session?.currentPath ?? "/"

        breadcrumbField.stringValue = breadcrumbText()
        breadcrumbField.toolTip = breadcrumbField.stringValue
        // 状态栏右侧原始路径（NAS 内部路径），点击复制。
        let rawPath: String
        if let location = session?.location(for: item) {
            rawPath = "/\(location.share)\(location.remotePath == "/" ? "" : location.remotePath)"
        } else {
            rawPath = currentPath == "/" ? "" : currentPath
        }
        statusPathField.stringValue = rawPath
        statusPathField.toolTip = rawPath
        fileKindField.stringValue = item.isDirectory ? directoryDetailLabel(for: item) : item.typeLabel
        fileKindField.toolTip = fileKindField.stringValue
        filePathField.stringValue = item.path
        filePathField.toolTip = item.path
        fileSizeField.stringValue = item.sizeLabel
        fileSizeField.toolTip = item.sizeLabel
        fileModifiedField.stringValue = item.modifiedLabel
        fileModifiedField.toolTip = item.modifiedLabel

        if let location = session?.location(for: item) {
            generatedLinkField.stringValue = "\(location.share)\(location.remotePath == "/" ? "" : location.remotePath)"
            generatedLinkField.toolTip = "可通过复制链接生成快速访问地址"
        } else {
            generatedLinkField.stringValue = "无法生成链接"
            generatedLinkField.toolTip = nil
        }
        updatePreviewPanel(item: item)
    }
}
