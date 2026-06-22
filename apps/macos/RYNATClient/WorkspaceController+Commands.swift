import AppKit
import Foundation

// MARK: - Commands

extension WorkspaceController {
    @objc
    func refreshCurrentView() {
        reloadCurrentRemoteDirectory(keepSelection: true)
        setActivityMessage("正在刷新当前目录\n\(currentServerLine())\n路径：\(displayDirectoryPath())")
    }

    @objc
    func showUploadPicker() {
        let panel = NSOpenPanel()
        panel.canChooseFiles = true
        panel.canChooseDirectories = false
        panel.allowsMultipleSelection = true
        panel.message = "选择要上传到当前目录的文件"
        if panel.runModal() == .OK {
            handleDroppedFiles(panel.urls)
        }
    }

    @objc
    func focusPreviewPanel() {
        if !isPreviewVisible {
            togglePreviewPanel()
        } else {
            setStatus("右侧预览面板已打开")
        }
    }

    @objc
    func togglePreviewPanel() {
        setPreviewPanelVisible(!isPreviewVisible)
        let message = isPreviewVisible ? "已打开预览" : "已关闭预览"
        setStatus(message)
    }

    func setPreviewPanelVisible(_ visible: Bool) {
        guard visible != isPreviewVisible else {
            updatePreviewToggleControl()
            return
        }
        isPreviewVisible = visible

        guard let splitView = mainSplitView else {
            updatePreviewToggleControl()
            return
        }

        if visible {
            let inspector = inspectorView()
            inspectorPane = inspector
            splitView.addArrangedSubview(inspector)
            splitView.setHoldingPriority(.required, forSubviewAt: splitView.arrangedSubviews.count - 1)
            updatePreviewPanel(item: currentItem())
        } else if let inspector = inspectorPane, inspector.superview != nil {
            captureWorkspaceSplitWidths()
            splitView.removeArrangedSubview(inspector)
            inspector.removeFromSuperview()
            inspectorPane = nil
        }

        if splitView.arrangedSubviews.count > 0 {
            splitView.setHoldingPriority(.required, forSubviewAt: 0)
        }
        if splitView.arrangedSubviews.count > 1 {
            splitView.setHoldingPriority(.defaultLow, forSubviewAt: 1)
        }
        if splitView.arrangedSubviews.count > 2 {
            splitView.setHoldingPriority(.required, forSubviewAt: 2)
        }
        updatePreviewToggleControl()
        applyWorkspaceSplitLayout()
        window?.contentView?.needsLayout = true
        window?.contentView?.layoutSubtreeIfNeeded()
    }

    func loadWorkspaceSplitWidths() {
        let defaults = UserDefaults.standard
        let savedSidebar = defaults.double(forKey: sidebarWidthDefaultsKey)
        if savedSidebar >= Double(SidebarView.defaultWidth) {
            sidebarPaneWidth = clamp(CGFloat(savedSidebar), min: SidebarView.minimumWidth, max: SidebarView.maximumWidth)
        } else {
            sidebarPaneWidth = SidebarView.defaultWidth
        }
        let savedPreview = defaults.double(forKey: previewWidthDefaultsKey)
        if savedPreview >= Double(previewPaneMinimumWidth) {
            previewPaneWidth = clamp(CGFloat(savedPreview), min: previewPaneMinimumWidth, max: previewPaneMaximumWidth)
        } else {
            previewPaneWidth = 340
        }
    }

    func captureWorkspaceSplitWidths() {
        guard let splitView = mainSplitView, splitView.arrangedSubviews.count >= 2 else {
            return
        }
        let nextSidebarWidth = clamp(splitView.arrangedSubviews[0].frame.width, min: SidebarView.minimumWidth, max: SidebarView.maximumWidth)
        if nextSidebarWidth.isFinite, nextSidebarWidth > 0 {
            sidebarPaneWidth = nextSidebarWidth
            UserDefaults.standard.set(Double(nextSidebarWidth), forKey: sidebarWidthDefaultsKey)
        }
        guard isPreviewVisible, splitView.arrangedSubviews.count >= 3 else {
            return
        }
        let nextPreviewWidth = clamp(splitView.arrangedSubviews[2].frame.width, min: previewPaneMinimumWidth, max: previewPaneMaximumWidth)
        if nextPreviewWidth.isFinite, nextPreviewWidth > 0 {
            previewPaneWidth = nextPreviewWidth
            UserDefaults.standard.set(Double(nextPreviewWidth), forKey: previewWidthDefaultsKey)
        }
    }

    func applyWorkspaceSplitLayout() {
        guard let splitView = mainSplitView, splitView.arrangedSubviews.count >= 2 else {
            return
        }
        guard !isApplyingWorkspaceSplitLayout else {
            return
        }
        splitView.layoutSubtreeIfNeeded()
        let splitWidth = splitView.bounds.width
        guard splitWidth > 0 else {
            DispatchQueue.main.async { [weak self] in
                self?.applyWorkspaceSplitLayout()
            }
            return
        }
        isApplyingWorkspaceSplitLayout = true
        defer { isApplyingWorkspaceSplitLayout = false }

        let divider = splitView.dividerThickness
        let hasPreview = isPreviewVisible && splitView.arrangedSubviews.count >= 3
        let dividerCount = CGFloat(splitView.arrangedSubviews.count - 1)
        let desiredPreviewWidth = hasPreview ? previewPaneWidth : 0
        let availableForSidebar = splitWidth - fileWorkspaceMinimumWidth - desiredPreviewWidth - divider * dividerCount
        let sidebarMax = Swift.max(SidebarView.minimumWidth, Swift.min(SidebarView.maximumWidth, availableForSidebar))
        let sidebarWidth = clamp(sidebarPaneWidth, min: SidebarView.minimumWidth, max: sidebarMax)
        splitView.setPosition(sidebarWidth, ofDividerAt: 0)

        guard hasPreview else {
            splitView.needsLayout = true
            splitView.layoutSubtreeIfNeeded()
            return
        }
        splitView.layoutSubtreeIfNeeded()
        let availableForPreview = splitWidth - sidebarWidth - fileWorkspaceMinimumWidth - divider * 2
        let previewMax = Swift.max(previewPaneMinimumWidth, Swift.min(previewPaneMaximumWidth, availableForPreview))
        let previewWidth = clamp(previewPaneWidth, min: previewPaneMinimumWidth, max: previewMax)
        let secondDividerPosition = splitWidth - previewWidth - divider
        splitView.setPosition(max(sidebarWidth + divider + fileWorkspaceMinimumWidth, secondDividerPosition), ofDividerAt: 1)
        splitView.needsLayout = true
        splitView.layoutSubtreeIfNeeded()
        hasAppliedWorkspaceSplitLayout = true
    }

    func clamp(_ value: CGFloat, min minimum: CGFloat, max maximum: CGFloat) -> CGFloat {
        Swift.max(minimum, Swift.min(value, maximum))
    }

    func updatePreviewToggleControl() {
        let symbol = isPreviewVisible ? "sidebar.right" : "sidebar.trailing"
        let label = isPreviewVisible ? "关闭预览面板" : "打开预览面板"
        previewToggleButton?.setSymbolName(symbol, accessibilityDescription: label)
        previewToggleButton?.toolTip = label
        previewToggleButton?.accessibilityLabel = label
    }

    @objc
    func goShareRoot() {
        navigateToDirectory("/")
    }

    @objc
    func goUpDirectory() {
        navigateToDirectory(parentPath(for: session?.currentPath ?? "/"))
    }

    @objc
    func createFolder() {
        guard let location = session?.activeLocation() else {
            setStatus("请先进入一个共享文件夹")
            return
        }
        promptForName(title: "新建文件夹", message: "在当前目录创建文件夹", defaultValue: "新建文件夹") { [weak self] name in
            guard let self else {
                return
            }
            let remotePath = self.appendPathComponent(directory: location.remotePath, fileName: name)
            self.performRemoteWrite(title: "正在创建文件夹", total: 1) { task, context, progress in
                _ = try self.runSmbWithReconnect(context: context) { core, connectionID in
                    try core.smbCreateDirectory(
                        share: location.share,
                        path: remotePath,
                        connectionID: connectionID,
                        operationID: task.operationID
                    )
                }
                progress(1, 1)
                return "已创建文件夹\n\(location.share)\(remotePath == "/" ? "" : remotePath)"
            }
        }
    }

    @objc
    func copyGeneratedLink() {
        guard let link = builtQuickLink() else {
            setActivityMessage("生成链接失败：请检查服务器、共享名和路径。")
            setStatus("生成链接失败")
            return
        }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(link.httpURL, forType: .string)
        setActivityMessage("""
        已复制固定快速访问链接
        \(link.httpURL)

        deep_link 仅供本机中转页内部唤醒：
        \(link.deepLinkURL)
        """)
        setStatus("已复制链接")
    }

    @objc
    func playSelectedItem() {
        let item = currentItem()
        guard item.kind == .file else {
            setActivityMessage("目录不能播放")
            setStatus("目录不可播放")
            return
        }
        do {
            let plan = try previewPlan(for: item)
            guard plan.contentType == "video" else {
                setActivityMessage("当前文件不是视频，无法播放。")
                setStatus("非视频文件")
                return
            }
            if let localPreviewURL = item.localPreviewURL {
                NSWorkspace.shared.open(localPreviewURL)
                setActivityMessage("已调用系统播放器\n\(localPreviewURL.path)")
                setStatus("已打开播放器")
                return
            }
            cacheVideoForPlayback(item: item, plan: plan)
        } catch {
            appendLog("Play failed: \(error.localizedDescription)")
            setActivityMessage("播放失败，请重试")
            setStatus("播放失败")
        }
    }

    @objc
    func openSelectedItem() {
        let item = currentItem()
        if item.isDirectory {
            navigateToDirectory(item.path)
            return
        }
        if let localPreviewURL = item.localPreviewURL {
            NSWorkspace.shared.activateFileViewerSelecting([localPreviewURL])
            setStatus("已在 Finder 中显示")
            return
        }
        do {
            let plan = try previewPlan(for: item)
            cacheVideoForPlayback(item: item, plan: plan)
        } catch {
            appendLog("Open failed: \(error.localizedDescription)")
            setActivityMessage("打开失败，请重试")
            setStatus("打开失败")
        }
    }

    @objc
    func cutSelectedItem() {
        guard let clipboard = makeClipboard(mode: .cut) else {
            setStatus("没有可剪切的项目")
            return
        }
        fileClipboard = clipboard
        setActivityMessage("已剪切\n\(clipboard.description)\n粘贴时将使用 SMB 服务端移动。")
        setStatus("已剪切")
    }

    @objc
    func copySelectedItem() {
        guard let clipboard = makeClipboard(mode: .copy) else {
            setStatus("没有可复制的项目")
            return
        }
        fileClipboard = clipboard
        setActivityMessage("已复制\n\(clipboard.description)\n粘贴时将优先使用 NAS 内部复制。")
        setStatus("已复制")
    }

    @objc
    func pasteClipboardItems() {
        guard let clipboard = fileClipboard, !clipboard.entries.isEmpty else {
            setStatus("剪贴板为空")
            return
        }
        guard let target = session?.activeLocation() else {
            setStatus("请先进入一个共享文件夹")
            return
        }
        clipboard.entries.forEach { entry in
            session?.invalidateDirectory(parentPath(for: entry.displayPath))
        }

        performRemoteWrite(title: clipboard.mode == .cut ? "正在移动" : "正在复制", total: clipboard.entries.count) { task, context, progress in
            let operation = RemoteOperationContext(
                listDirectory: { share, path in
                    try self.runSmbWithReconnect(context: context) { core, connectionID in
                        try core.smbListDirectory(
                            share: share,
                            path: path,
                            connectionID: connectionID,
                            operationID: task.operationID
                        )
                    }
                },
                log: { [weak self] message in self?.appendLog(message) }
            )
            var processed = 0
            var skipped = 0
            var total = clipboard.entries.count
            let markProcessed = {
                processed += 1
                progress(processed, total)
            }
            let addTotal = { (count: Int) in
                total += count
                progress(processed, total)
            }
            for entry in clipboard.entries {
                if task.isCancelled {
                    break
                }
                let targetPath = self.appendPathComponent(directory: target.remotePath, fileName: entry.name)
                switch clipboard.mode {
                case .cut:
                    guard entry.share == target.share else {
                        throw RynatCoreError.bridgeError("跨共享移动暂不支持，请使用复制。", code: "unsupported")
                    }
                    guard try self.resolveConflictIfNeeded(
                        share: target.share,
                        path: targetPath,
                        name: entry.name,
                        isDirectory: entry.isDirectory,
                        task: task,
                        operation: operation
                    ) == .replace else {
                        skipped += 1
                        processed += 1
                        progress(processed, clipboard.entries.count)
                        continue
                    }
                    if let existing = try operation.existingItem(share: target.share, path: targetPath) {
                        addTotal(1)
                        try self.deleteRemotePathRecursively(
                            share: target.share,
                            path: targetPath,
                            isDirectory: existing.isDirectory,
                            task: task,
                            context: context,
                            operation: operation,
                            onProcessed: markProcessed,
                            onDiscovered: addTotal
                        )
                    }
                    _ = try self.runSmbWithReconnect(context: context) { core, connectionID in
                        try core.smbRename(
                            share: entry.share,
                            fromPath: entry.remotePath,
                            toPath: targetPath,
                            connectionID: connectionID,
                            operationID: task.operationID
                        )
                    }
                    operation.markCreated(share: target.share, path: targetPath, isDirectory: entry.isDirectory)
                    markProcessed()
                case .copy:
                    let summary = try self.copyEntryRecursively(
                        entry,
                        targetShare: target.share,
                        targetPath: targetPath,
                        task: task,
                        context: context,
                        operation: operation,
                        onProcessed: markProcessed,
                        onDiscovered: addTotal
                    )
                    skipped += summary.skipped
                }
            }
            if clipboard.mode == .cut {
                DispatchQueue.main.async { [weak self] in
                    self?.fileClipboard = nil
                }
            }
            return skipped > 0 ? "已完成，跳过 \(skipped) 项" : "已完成"
        }
    }

    @objc
    func renameSelectedItem() {
        let item = currentItem()
        guard let location = session?.location(for: item), item.path != "/", !isShareRootItem(item) else {
            setStatus("请选择要重命名的项目")
            return
        }
        promptForName(title: "重命名", message: item.name, defaultValue: item.name) { [weak self] newName in
            guard let self else {
                return
            }
            let targetPath = self.appendPathComponent(directory: self.remoteParentPath(for: location.remotePath), fileName: newName)
            self.performRemoteWrite(title: "正在重命名", total: 1) { task, context, progress in
                _ = try self.runSmbWithReconnect(context: context) { core, connectionID in
                    try core.smbRename(
                        share: location.share,
                        fromPath: location.remotePath,
                        toPath: targetPath,
                        connectionID: connectionID,
                        operationID: task.operationID
                    )
                }
                progress(1, 1)
                return "已重命名\n\(item.name) -> \(newName)"
            }
        }
    }

    @objc
    func deleteSelectedItem() {
        let items = selectedItems()
        guard !items.isEmpty else {
            setStatus("没有可删除的项目")
            return
        }
        let alert = NSAlert()
        alert.messageText = "确认删除 \(items.count) 个项目？"
        alert.informativeText = selectedItemNames()
        alert.alertStyle = .warning
        alert.addButton(withTitle: "删除")
        alert.addButton(withTitle: "取消")
        guard alert.runModal() == .alertFirstButtonReturn else {
            return
        }

        let targets = items.compactMap { item -> (item: RynatFileItem, share: String, remotePath: String)? in
            guard !isShareRootItem(item), let location = session?.location(for: item) else {
                return nil
            }
            return (item, location.share, location.remotePath)
        }
        guard !targets.isEmpty else {
            setStatus("共享根目录不能删除")
            return
        }
        performRemoteWrite(title: "正在删除", total: targets.count) { task, context, progress in
            for (index, target) in targets.enumerated() {
                if task.isCancelled {
                    break
                }
                _ = try self.runSmbWithReconnect(context: context) { core, connectionID in
                    try core.smbDelete(
                        share: target.share,
                        path: target.remotePath,
                        isDir: target.item.isDirectory,
                        connectionID: connectionID,
                        operationID: task.operationID
                    )
                }
                progress(index + 1, targets.count)
            }
            return "已删除\n\(targets.map { $0.item.name }.joined(separator: "、"))"
        }
    }

    @objc
    func copySelectedPath() {
        let item = currentItem()
        let path: String
        if let location = session?.location(for: item) {
            path = "/\(location.share)\(location.remotePath == "/" ? "" : location.remotePath)"
        } else {
            path = currentServer().host
        }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(path, forType: .string)
        setStatus("路径已复制")
    }

    func contextMenu(forRow row: Int) -> NSMenu? {
        guard let item = fileListController.item(at: row) else {
            return backgroundMenu()
        }
        selectedItem = item
        updateSelectionState()

        let menu = NSMenu(title: item.name)
        menu.addItem(NSMenuItem(title: item.isDirectory ? "打开文件夹" : "打开文件", action: #selector(openSelectedItem), keyEquivalent: ""))
        if !isShareRootItem(item) {
            menu.addItem(.separator())
            menu.addItem(NSMenuItem(title: "剪切", action: #selector(cutSelectedItem), keyEquivalent: "x"))
            menu.addItem(NSMenuItem(title: "复制", action: #selector(copySelectedItem), keyEquivalent: "c"))
            menu.addItem(NSMenuItem(title: "重命名", action: #selector(renameSelectedItem), keyEquivalent: ""))
            menu.addItem(NSMenuItem(title: "删除", action: #selector(deleteSelectedItem), keyEquivalent: ""))
        }
        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: "生成分享链接", action: #selector(copyGeneratedLink), keyEquivalent: ""))
        menu.addItem(NSMenuItem(title: "添加到收藏", action: #selector(addCurrentFavorite), keyEquivalent: ""))
        menu.addItem(NSMenuItem(title: "复制路径", action: #selector(copySelectedPath), keyEquivalent: ""))
        if !item.isDirectory {
            menu.addItem(NSMenuItem(title: "播放/预览", action: #selector(playSelectedItem), keyEquivalent: ""))
        }
        menu.items.forEach { $0.target = self }
        return menu
    }

    func backgroundMenu() -> NSMenu {
        let menu = NSMenu(title: "目录")
        menu.addItem(NSMenuItem(title: "新建文件夹", action: #selector(createFolder), keyEquivalent: ""))
        menu.addItem(NSMenuItem(title: "上传文件", action: #selector(showUploadPicker), keyEquivalent: ""))
        if fileClipboard != nil {
            menu.addItem(.separator())
            let count = fileClipboard?.entries.count ?? 0
            let mode = fileClipboard?.mode == .cut ? "移动" : "复制"
            menu.addItem(NSMenuItem(title: "粘贴 \(count) 项（\(mode)）", action: #selector(pasteClipboardItems), keyEquivalent: "v"))
        }
        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: "刷新", action: #selector(refreshCurrentView), keyEquivalent: "r"))
        menu.items.forEach { $0.target = self }
        return menu
    }

    @objc
    func addCurrentFavorite() {
        guard let link = generatedQuickLink() else {
            setStatus("收藏失败")
            return
        }
        session?.addFavorite(link)
        refreshSidebar()
        setStatus("已收藏 \(currentItem().name)")
    }

    func refreshFavoritesFromStore() {
        do {
            session?.setFavorites(try core.listQuickLinks())
        } catch {
            appendLog("Load favorites failed: \(error.localizedDescription)")
        }
    }

    func openFavorite(_ link: QuickLink) {
        guard canOpenFavorite(link) else {
            setStatus("收藏属于其他服务器")
            return
        }
        switch link.target.kind {
        case .file:
            let parentRemotePath = parentPath(for: link.target.path)
            let directoryPath = Self.displayPathForRemote(share: link.target.share, remotePath: parentRemotePath)
            let selectedPath = Self.displayPathForRemote(share: link.target.share, remotePath: link.target.path)
            navigateToDirectory(directoryPath, selectPath: selectedPath)
        case .dir, .unknown:
            let displayPath = Self.displayPathForRemote(share: link.target.share, remotePath: link.target.path)
            navigateToDirectory(displayPath)
        }
        activeSidebarTab = .favorites
        refreshSidebar()
        setStatus("已打开收藏")
    }

    func canOpenFavorite(_ link: QuickLink) -> Bool {
        guard let session else {
            return false
        }
        let favoriteHost = normalizedServerHostForCompare(link.target.serverHost)
        let sessionHost = normalizedServerHostForCompare(session.server.host)
        return favoriteHost == sessionHost
    }

    func normalizedServerHostForCompare(_ host: String) -> String {
        var normalized = host.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        if normalized.hasPrefix("smb://") {
            normalized.removeFirst("smb://".count)
        }
        normalized = normalized.trimmingCharacters(in: CharacterSet(charactersIn: "/"))
        if normalized.hasSuffix(":445") {
            normalized.removeLast(":445".count)
        }
        return normalized
    }

    func promptForName(
        title: String,
        message: String,
        defaultValue: String,
        completion: @escaping (String) -> Void
    ) {
        let alert = NSAlert()
        alert.messageText = title
        alert.informativeText = message
        alert.addButton(withTitle: "确认")
        alert.addButton(withTitle: "取消")

        let field = NSTextField(string: defaultValue)
        field.frame = NSRect(x: 0, y: 0, width: 280, height: 28)
        field.bezelStyle = .roundedBezel
        alert.accessoryView = field

        guard alert.runModal() == .alertFirstButtonReturn else {
            return
        }
        let value = field.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        guard isValidFileName(value) else {
            setStatus("名称不能为空，且不能包含 / 或 :")
            return
        }
        completion(value)
    }

    func isValidFileName(_ name: String) -> Bool {
        !name.isEmpty && !name.contains("/") && !name.contains("\\") && !name.contains(":")
    }
}
