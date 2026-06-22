import AppKit
import Foundation

// MARK: - Layout

extension WorkspaceController {
    func buildWindow() {
        let newWindow = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 1220, height: 760),
            styleMask: [.titled, .closable, .miniaturizable, .resizable],
            backing: .buffered,
            defer: false
        )
        newWindow.title = "RYNAT 共享网盘"
        newWindow.titlebarAppearsTransparent = false
        newWindow.toolbarStyle = .automatic
        newWindow.contentMinSize = NSSize(width: 820, height: 560)
        newWindow.minSize = frameSize(forContentSize: newWindow.contentMinSize, in: newWindow)
        newWindow.contentMaxSize = NSSize(width: 10000, height: 10000)
        newWindow.maxSize = frameSize(forContentSize: newWindow.contentMaxSize, in: newWindow)
        newWindow.collectionBehavior = [.managed, .fullScreenPrimary]
        newWindow.showsResizeIndicator = true
        newWindow.isReleasedWhenClosed = false
        newWindow.center()
        newWindow.delegate = self
        window = newWindow

        if bootstrapState?.activeCredential?.autoLogin == true, let profile = bootstrapState?.activeServer {
            showAutoLoginPlaceholder(host: profile.endpoint.host)
        } else {
            showLogin()
        }
        window?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    /// 自动登录时的占位视图，避免登录页闪现。
    func showAutoLoginPlaceholder(host: String) {
        let container = NSView()
        container.wantsLayer = true
        container.layer?.backgroundColor = NSColor.windowBackgroundColor.cgColor

        let label = NSTextField(labelWithString: "正在连接 \(host)...")
        label.font = .systemFont(ofSize: 14, weight: .regular)
        label.textColor = RynatUI.muted
        label.alignment = .center

        let progress = NSProgressIndicator()
        progress.style = .spinning
        progress.controlSize = .regular
        progress.startAnimation(nil)

        let stack = NSStackView()
        stack.orientation = .vertical
        stack.spacing = 12
        stack.alignment = .centerX
        stack.addArrangedSubview(progress)
        stack.addArrangedSubview(label)
        stack.translatesAutoresizingMaskIntoConstraints = false
        container.addSubview(stack)
        NSLayoutConstraint.activate([
            stack.centerXAnchor.constraint(equalTo: container.centerXAnchor),
            stack.centerYAnchor.constraint(equalTo: container.centerYAnchor),
        ])

        window?.titlebarAppearsTransparent = false
        window?.titleVisibility = .visible
        window?.toolbarStyle = .automatic
        window?.styleMask.remove(.fullSizeContentView)
        installContentView(container, preferredSize: NSSize(width: 380, height: 240), minimumSize: NSSize(width: 360, height: 240))
    }

    func showLogin() {
        let controller = LoginViewController(bootstrap: bootstrapState ?? fallbackBootstrapState())
        controller.delegate = self
        loginController = controller
        window?.titlebarAppearsTransparent = false
        window?.titleVisibility = .visible
        window?.toolbarStyle = .automatic
        window?.styleMask.remove(.fullSizeContentView)
        let preferredSize = LoginViewController.defaultContentSize
        let minimumSize = LoginViewController.minimumContentSize
        let loginView = controller.view
        loginView.frame = NSRect(x: 0, y: 0, width: preferredSize.width, height: preferredSize.height)
        loginView.autoresizingMask = [.width, .height]
        let shouldResizeWindow = shouldResizeWindowForLogin(preferredSize: preferredSize)
        installContentController(
            controller,
            preferredSize: preferredSize,
            minimumSize: minimumSize,
            resizeWindow: shouldResizeWindow
        )
        mainSplitView = nil
        inspectorPane = nil
        previewToggleButton = nil
        isPreviewVisible = true
        browserRootView = nil
        browserBodyView = nil
        statusField.stringValue = "未连接"
    }

    func loadBootstrapState() -> AppBootstrapState {
        do {
            let state = try core.openStore(path: appStoreURL().path)
            appendLog("Opened app store at \(appStoreURL().path)")
            return state
        } catch {
            appendLog("Open app store failed: \(error.localizedDescription)")
            return fallbackBootstrapState()
        }
    }

    func appStoreURL() -> URL {
        let root = FileManager.default
            .homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Application Support/RYNATClient", isDirectory: true)
        return root.appendingPathComponent("rynat.sqlite")
    }

    func fallbackBootstrapState() -> AppBootstrapState {
        let profile = StoredServerProfile(
            id: "",
            displayName: "共享网盘",
            endpoint: StoredServerEndpoint(host: "192.168.102.136", port: nil),
            username: nil,
            authMode: "username_password",
            dialectPreference: "smb3_preferred",
            createdAt: "",
            updatedAt: ""
        )
        return AppBootstrapState(
            serverProfiles: [profile],
            activeServer: profile,
            activeCredential: nil
        )
    }

    func loginViewController(_ controller: LoginViewController, didLoginWith server: RynatServerProfile) {
        finishLogin(with: server)
    }

    func loginViewController(_ controller: LoginViewController, didUpdateBootstrap bootstrap: AppBootstrapState) {
        bootstrapState = bootstrap
    }

    func showBrowser() {
        configureControls()
        loadWorkspaceSplitWidths()

        let minimum = NSSize(width: 1088, height: 520)
        let desired = preferredWindowContentSize(
            desired: NSSize(width: 1000, height: 680),
            minimum: minimum
        )

        let content = DropContainerView()
        content.frame = NSRect(x: 0, y: 0, width: desired.width, height: desired.height)
        content.autoresizingMask = [.width, .height]
        content.wantsLayer = true
        content.layer?.backgroundColor = RynatUI.canvas.cgColor
        content.onFilesDropped = { [weak self] urls in
            self?.handleDroppedFiles(urls)
        }

        let body = NSView()
        body.translatesAutoresizingMaskIntoConstraints = true
        body.wantsLayer = true
        body.layer?.backgroundColor = RynatUI.workspace.cgColor
        body.setContentHuggingPriority(.defaultLow, for: .horizontal)
        body.setContentHuggingPriority(.defaultLow, for: .vertical)
        body.setContentCompressionResistancePriority(.defaultLow, for: .vertical)
        body.heightAnchor.constraint(greaterThanOrEqualToConstant: 446).isActive = true
        browserBodyView = body

        let splitView = RynatSplitView()
        splitView.isVertical = true
        splitView.dividerStyle = .thin
        splitView.delegate = self
        hasAppliedWorkspaceSplitLayout = false
        splitView.translatesAutoresizingMaskIntoConstraints = false
        splitView.setContentHuggingPriority(.defaultLow, for: .horizontal)
        splitView.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        splitView.addArrangedSubview(sidebarView)
        splitView.addArrangedSubview(fileWorkspaceView())
        let inspector = inspectorView()
        inspectorPane = inspector
        if isPreviewVisible {
            splitView.addArrangedSubview(inspector)
        }
        splitView.setHoldingPriority(.required, forSubviewAt: 0)
        splitView.setHoldingPriority(.defaultLow, forSubviewAt: 1)
        if isPreviewVisible {
            splitView.setHoldingPriority(.required, forSubviewAt: 2)
        }
        mainSplitView = splitView

        sidebarView.delegate = self
        body.addSubview(splitView)

        let header = appHeaderView()
        let statusBar = statusBarView()
        let root = BrowserRootContainerView(headerView: header, bodyView: body, statusView: statusBar)
        root.frame = content.bounds
        root.autoresizingMask = [.width, .height]
        root.translatesAutoresizingMaskIntoConstraints = true
        root.setContentHuggingPriority(.defaultLow, for: .vertical)
        root.setContentCompressionResistancePriority(.defaultLow, for: .vertical)
        browserRootView = root
        NSLayoutConstraint.activate([
            splitView.leadingAnchor.constraint(equalTo: body.leadingAnchor),
            splitView.trailingAnchor.constraint(equalTo: body.trailingAnchor),
            splitView.topAnchor.constraint(equalTo: body.topAnchor),
            splitView.bottomAnchor.constraint(equalTo: body.bottomAnchor),
        ])

        content.addSubview(root)

        window?.titlebarAppearsTransparent = false
        window?.titleVisibility = .visible
        window?.toolbarStyle = .automatic
        window?.styleMask.remove(.fullSizeContentView)
        window?.styleMask.insert(.resizable)
        installContentView(content, preferredSize: desired, minimumSize: minimum)
        applyWorkspaceSplitLayout()
        syncSidebarHeaderHeightToFileTable()

        refreshSidebar()
        reloadFileList()
        updateSelectionState()
        DispatchQueue.main.async { [weak self] in
            self?.syncSidebarHeaderHeightToFileTable()
        }
    }

    func installContentView(_ content: NSView, preferredSize: NSSize, minimumSize: NSSize, resizeWindow: Bool = true) {
        guard let window else {
            return
        }
        window.styleMask.insert(.resizable)
        window.contentMinSize = minimumSize
        window.minSize = frameSize(forContentSize: minimumSize, in: window)
        window.contentMaxSize = NSSize(width: 10000, height: 10000)
        window.maxSize = frameSize(forContentSize: window.contentMaxSize, in: window)
        window.contentResizeIncrements = NSSize(width: 1, height: 1)
        window.contentViewController = nil
        let host = WorkspaceContentHostView(frame: NSRect(origin: .zero, size: preferredSize))
        host.translatesAutoresizingMaskIntoConstraints = true
        host.autoresizingMask = [.width, .height]
        host.autoresizesSubviews = true
        host.wantsLayer = true
        host.layer?.backgroundColor = NSColor.windowBackgroundColor.cgColor
        content.frame = host.bounds
        content.autoresizingMask = [.width, .height]
        content.translatesAutoresizingMaskIntoConstraints = true
        host.addSubview(content)
        window.contentView = host
        hasInstalledContentView = true
        if resizeWindow {
            resizeWindowContent(to: preferredSize)
        }
    }

    func installContentController(_ controller: NSViewController, preferredSize: NSSize, minimumSize: NSSize, resizeWindow: Bool = true) {
        installContentView(controller.view, preferredSize: preferredSize, minimumSize: minimumSize, resizeWindow: resizeWindow)
    }

    func preferredWindowContentSize(desired: NSSize, minimum: NSSize) -> NSSize {
        guard let screen = window?.screen ?? NSScreen.main else {
            return desired
        }
        let available = screen.visibleFrame.insetBy(dx: 24, dy: 24).size
        return NSSize(
            width: max(minimum.width, min(desired.width, available.width)),
            height: max(minimum.height, min(desired.height, available.height))
        )
    }

    func frameSize(forContentSize contentSize: NSSize, in window: NSWindow) -> NSSize {
        window.frameRect(forContentRect: NSRect(origin: .zero, size: contentSize)).size
    }

    func resizeWindowContent(to size: NSSize) {
        guard let window else {
            return
        }
        window.setContentSize(size)
        window.center()
    }

    func shouldResizeWindowForLogin(preferredSize: NSSize) -> Bool {
        guard hasInstalledContentView, let window else {
            return true
        }
        let currentSize = window.contentLayoutRect.size
        return currentSize.width < preferredSize.width || currentSize.height < preferredSize.height
    }

    func configureControls() {
        guard !didConfigureControls else {
            return
        }
        didConfigureControls = true

        searchField.placeholderString = "搜索已加载目录"
        searchField.delegate = self
        searchField.controlSize = .large
        searchField.isEnabled = true
        searchField.isEditable = true
        searchField.sendsSearchStringImmediately = true
        searchField.translatesAutoresizingMaskIntoConstraints = false
        searchWidthConstraint = searchField.widthAnchor.constraint(equalToConstant: 236)
        searchWidthConstraint?.priority = .defaultHigh
        searchWidthConstraint?.isActive = true
        NSLayoutConstraint.activate([
            searchField.heightAnchor.constraint(equalToConstant: 32),
        ])

        breadcrumbField.font = .systemFont(ofSize: 17, weight: .semibold)
        breadcrumbField.textColor = RynatUI.ink
        breadcrumbField.lineBreakMode = .byTruncatingMiddle

        fileSummaryField.font = .systemFont(ofSize: 12)
        fileSummaryField.textColor = RynatUI.muted

        generatedLinkField.isSelectable = true
        generatedLinkField.maximumNumberOfLines = 3
        generatedLinkField.lineBreakMode = .byTruncatingMiddle
        generatedLinkField.font = .systemFont(ofSize: 12, weight: .regular)
        generatedLinkField.textColor = RynatUI.muted
        generatedLinkField.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)

        configureFileList()
        configurePreviewImageView()
        configureCommandButtons()
        configureUploadProgress()
    }

    func appHeaderView() -> NSView {
        let container = NSView()
        container.translatesAutoresizingMaskIntoConstraints = false
        container.heightAnchor.constraint(equalToConstant: 44).isActive = true
        container.wantsLayer = true
        container.layer?.backgroundColor = RynatUI.chrome.cgColor

        let stack = NSStackView()
        stack.orientation = .horizontal
        stack.alignment = .centerY
        stack.spacing = 8
        stack.edgeInsets = NSEdgeInsets(top: 0, left: 14, bottom: 0, right: 14)
        stack.translatesAutoresizingMaskIntoConstraints = false

        let backButton = RynatUI.symbolButton("chevron.left", accessibilityLabel: "返回上级", target: self, action: #selector(goUpDirectory))
        let homeButton = RynatUI.symbolButton("house", accessibilityLabel: "共享根目录", target: self, action: #selector(goShareRoot))

        let breadcrumbWrap = NSStackView()
        breadcrumbWrap.orientation = .horizontal
        breadcrumbWrap.alignment = .centerY
        breadcrumbWrap.addArrangedSubview(breadcrumbField)
        breadcrumbWrap.translatesAutoresizingMaskIntoConstraints = false
        breadcrumbWrap.setContentHuggingPriority(.defaultLow, for: .horizontal)

        let refreshButton = RynatUI.symbolButton("arrow.clockwise", accessibilityLabel: "刷新", target: self, action: #selector(refreshCurrentView))
        let previewButton = RynatUI.symbolButton(isPreviewVisible ? "sidebar.right" : "sidebar.trailing", accessibilityLabel: isPreviewVisible ? "关闭预览面板" : "打开预览面板", target: self, action: #selector(togglePreviewPanel))
        previewToggleButton = previewButton
        let utilityGroup = NSStackView()
        utilityGroup.orientation = .horizontal
        utilityGroup.alignment = .centerY
        utilityGroup.spacing = 4
        utilityGroup.translatesAutoresizingMaskIntoConstraints = false
        utilityGroup.addArrangedSubview(refreshButton)
        utilityGroup.addArrangedSubview(previewButton)

        stack.addArrangedSubview(backButton)
        stack.addArrangedSubview(homeButton)
        stack.addArrangedSubview(breadcrumbWrap)
        stack.addArrangedSubview(RynatUI.spacer())
        stack.addArrangedSubview(searchField)
        stack.addArrangedSubview(utilityGroup)
        stack.addArrangedSubview(userMenuButton())
        container.addSubview(stack)

        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: container.leadingAnchor),
            stack.trailingAnchor.constraint(equalTo: container.trailingAnchor),
            stack.topAnchor.constraint(equalTo: container.topAnchor),
            stack.bottomAnchor.constraint(equalTo: container.bottomAnchor),
        ])
        return container
    }

    func userMenuButton() -> NSPopUpButton {
        let popup = RynatPopupButton()
        popup.addItems(withTitles: [session?.server.accountName ?? "用户", "服务器设置...", "退出登录"])
        popup.target = self
        popup.action = #selector(userMenuChanged(_:))
        return popup
    }

    func fileWorkspaceView() -> NSView {
        let container = NSView()
        container.translatesAutoresizingMaskIntoConstraints = false
        container.wantsLayer = true
        container.layer?.backgroundColor = RynatUI.surface.cgColor
        container.widthAnchor.constraint(greaterThanOrEqualToConstant: fileWorkspaceMinimumWidth).isActive = true
        container.setContentHuggingPriority(.defaultLow, for: .horizontal)
        container.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)

        let listScroll = tableScrollView(fileTable)
        listScrollView = listScroll

        container.addSubview(listScroll)

        NSLayoutConstraint.activate([
            listScroll.leadingAnchor.constraint(equalTo: container.leadingAnchor),
            listScroll.trailingAnchor.constraint(equalTo: container.trailingAnchor),
            listScroll.topAnchor.constraint(equalTo: container.topAnchor),
            listScroll.bottomAnchor.constraint(equalTo: container.bottomAnchor),
        ])
        return container
    }

    func inspectorView() -> NSView {
        let container = NSView()
        container.translatesAutoresizingMaskIntoConstraints = false
        container.wantsLayer = true
        container.layer?.backgroundColor = RynatUI.panel.cgColor
        container.layer?.borderWidth = 0
        let preferredWidth = container.widthAnchor.constraint(equalToConstant: previewPaneWidth)
        preferredWidth.priority = .defaultLow
        preferredWidth.isActive = true
        container.widthAnchor.constraint(greaterThanOrEqualToConstant: previewPaneMinimumWidth).isActive = true
        container.widthAnchor.constraint(lessThanOrEqualToConstant: previewPaneMaximumWidth).isActive = true
        container.setContentHuggingPriority(.defaultHigh, for: .horizontal)
        container.setContentCompressionResistancePriority(.defaultHigh, for: .horizontal)

        let scroll = NSScrollView()
        scroll.translatesAutoresizingMaskIntoConstraints = false
        scroll.hasVerticalScroller = true
        scroll.hasHorizontalScroller = false
        scroll.borderType = .noBorder
        scroll.drawsBackground = false

        let document = NSView()
        document.translatesAutoresizingMaskIntoConstraints = false
        let documentHeightConstraint = document.heightAnchor.constraint(greaterThanOrEqualTo: scroll.contentView.heightAnchor)
        documentHeightConstraint.priority = .defaultHigh

        let stack = NSStackView()
        stack.orientation = .vertical
        stack.spacing = 0
        stack.edgeInsets = NSEdgeInsets(top: 0, left: 0, bottom: 0, right: 0)
        stack.translatesAutoresizingMaskIntoConstraints = false
        stack.addArrangedSubview(inspectorHeaderView())
        stack.addArrangedSubview(inspectorContentView())
        stack.addArrangedSubview(previewFooterView())
        document.addSubview(stack)
        scroll.documentView = document
        container.addSubview(scroll)

        NSLayoutConstraint.activate([
            scroll.leadingAnchor.constraint(equalTo: container.leadingAnchor),
            scroll.trailingAnchor.constraint(equalTo: container.trailingAnchor),
            scroll.topAnchor.constraint(equalTo: container.topAnchor),
            scroll.bottomAnchor.constraint(equalTo: container.bottomAnchor),
            document.widthAnchor.constraint(equalTo: scroll.contentView.widthAnchor),
            documentHeightConstraint,
            stack.leadingAnchor.constraint(equalTo: document.leadingAnchor),
            stack.trailingAnchor.constraint(equalTo: document.trailingAnchor),
            stack.topAnchor.constraint(equalTo: document.topAnchor),
            stack.bottomAnchor.constraint(equalTo: document.bottomAnchor),
        ])
        return container
    }

    func inspectorHeaderView() -> NSView {
        let container = NSView()
        container.translatesAutoresizingMaskIntoConstraints = false
        container.heightAnchor.constraint(equalToConstant: 58).isActive = true
        container.wantsLayer = true
        container.layer?.backgroundColor = RynatUI.glassStrong.cgColor

        let stack = NSStackView()
        stack.orientation = .horizontal
        stack.alignment = .centerY
        stack.spacing = 8
        stack.edgeInsets = NSEdgeInsets(top: 0, left: 16, bottom: 0, right: 16)
        stack.translatesAutoresizingMaskIntoConstraints = false
        stack.addArrangedSubview(RynatUI.title("预览", size: 16))
        stack.addArrangedSubview(RynatUI.spacer())
        stack.addArrangedSubview(fileSummaryField)
        container.addSubview(stack)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: container.leadingAnchor),
            stack.trailingAnchor.constraint(equalTo: container.trailingAnchor),
            stack.topAnchor.constraint(equalTo: container.topAnchor),
            stack.bottomAnchor.constraint(equalTo: container.bottomAnchor),
        ])
        return container
    }

    func inspectorContentView() -> NSView {
        let container = NSView()
        container.translatesAutoresizingMaskIntoConstraints = false
        container.wantsLayer = true
        container.layer?.backgroundColor = RynatUI.panel.cgColor

        let stack = NSStackView()
        stack.orientation = .vertical
        stack.spacing = 14
        stack.edgeInsets = NSEdgeInsets(top: 14, left: 14, bottom: 14, right: 14)
        stack.translatesAutoresizingMaskIntoConstraints = false
        stack.addArrangedSubview(previewStageView())
        stack.addArrangedSubview(infoSummaryView())
        container.addSubview(stack)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: container.leadingAnchor),
            stack.trailingAnchor.constraint(equalTo: container.trailingAnchor),
            stack.topAnchor.constraint(equalTo: container.topAnchor),
            stack.bottomAnchor.constraint(equalTo: container.bottomAnchor),
        ])
        return container
    }

    func previewStageView() -> NSView {
        let previewStage = NSView()
        previewStage.translatesAutoresizingMaskIntoConstraints = false
        previewStage.wantsLayer = true
        previewStage.layer?.backgroundColor = NSColor.clear.cgColor

        let proportionalHeight = previewStage.heightAnchor.constraint(equalTo: previewStage.widthAnchor, multiplier: 0.62)
        proportionalHeight.priority = .defaultHigh
        proportionalHeight.isActive = true
        previewStage.heightAnchor.constraint(greaterThanOrEqualToConstant: 172).isActive = true
        previewStage.heightAnchor.constraint(lessThanOrEqualToConstant: 300).isActive = true

        previewStage.addSubview(previewImageView)

        NSLayoutConstraint.activate([
            previewImageView.leadingAnchor.constraint(equalTo: previewStage.leadingAnchor, constant: 10),
            previewImageView.trailingAnchor.constraint(equalTo: previewStage.trailingAnchor, constant: -10),
            previewImageView.topAnchor.constraint(equalTo: previewStage.topAnchor),
            previewImageView.bottomAnchor.constraint(equalTo: previewStage.bottomAnchor),
        ])
        return previewStage
    }

    /// 单行次要信息：文件名 + 类型·大小·修改。
    func infoSummaryView() -> NSView {
        let stack = NSStackView()
        stack.orientation = .vertical
        stack.alignment = .centerX
        stack.spacing = 3
        stack.translatesAutoresizingMaskIntoConstraints = false
        stack.addArrangedSubview(previewTitleField)
        stack.addArrangedSubview(previewMetaField)
        return stack
    }

    func actionRowView() -> NSView {
        let stack = NSStackView()
        stack.orientation = .horizontal
        stack.spacing = 8
        stack.alignment = .centerY
        // 1 主（复制链接）+ 1 次（播放/打开，随类型显隐）
        stack.addArrangedSubview(copyLinkButton)
        stack.addArrangedSubview(playButton)
        stack.addArrangedSubview(openButton)
        return stack
    }

    func previewFooterView() -> NSView {
        let container = NSView()
        container.translatesAutoresizingMaskIntoConstraints = false
        container.heightAnchor.constraint(equalToConstant: 54).isActive = true
        container.wantsLayer = true
        container.layer?.backgroundColor = RynatUI.glassStrong.cgColor

        let stack = actionRowView()
        stack.translatesAutoresizingMaskIntoConstraints = false
        container.addSubview(stack)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(greaterThanOrEqualTo: container.leadingAnchor, constant: 14),
            stack.trailingAnchor.constraint(equalTo: container.trailingAnchor, constant: -14),
            stack.centerYAnchor.constraint(equalTo: container.centerYAnchor),
        ])
        return container
    }

    func statusBarView() -> NSView {
        let container = NSView()
        container.translatesAutoresizingMaskIntoConstraints = false
        container.heightAnchor.constraint(equalToConstant: 30).isActive = true
        container.wantsLayer = true
        container.layer?.backgroundColor = RynatUI.chrome.cgColor

        let stack = NSStackView()
        stack.orientation = .horizontal
        stack.alignment = .centerY
        stack.spacing = 10
        stack.edgeInsets = NSEdgeInsets(top: 4, left: 16, bottom: 4, right: 16)
        stack.translatesAutoresizingMaskIntoConstraints = false

        statusField.font = .systemFont(ofSize: 11.5, weight: .regular)
        statusField.textColor = RynatUI.muted
        statusPathField.font = .monospacedSystemFont(ofSize: 10.5, weight: .regular)
        statusPathField.textColor = RynatUI.faint
        statusPathField.lineBreakMode = .byTruncatingMiddle
        statusPathField.toolTip = nil
        uploadProgress.isHidden = true
        cancelTaskButton.isHidden = true
        cancelTaskButton.target = self
        cancelTaskButton.action = #selector(cancelActiveTask)

        stack.addArrangedSubview(statusField)
        stack.addArrangedSubview(RynatUI.spacer())
        stack.addArrangedSubview(statusPathField)
        stack.addArrangedSubview(uploadProgress)
        stack.addArrangedSubview(cancelTaskButton)
        container.addSubview(stack)

        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: container.leadingAnchor),
            stack.trailingAnchor.constraint(equalTo: container.trailingAnchor),
            stack.topAnchor.constraint(equalTo: container.topAnchor),
            stack.bottomAnchor.constraint(equalTo: container.bottomAnchor),
        ])
        return container
    }

    func configureFileList() {
        fileListController.attach(to: fileTable)
        syncSidebarHeaderHeightToFileTable()
        fileListController.onContextMenu = { [weak self] row in
            self?.contextMenu(forRow: row)
        }
        fileListController.onOpen = { [weak self] in
            self?.openSelectedItem()
        }
        fileListController.onSelectionChanged = { [weak self] item in
            guard let self, let item else {
                return
            }
            self.selectedItem = item
            self.updateSelectionState()
        }
    }

    func syncSidebarHeaderHeightToFileTable() {
        fileTable.layoutSubtreeIfNeeded()
        let headerHeight = fileTable.headerView?.bounds.height ?? 0
        guard headerHeight > 0 else {
            return
        }
        sidebarView.setTabBarHeight(headerHeight)
    }

    /// 行内复制链接：用 buildLink（不写 quick_links），复制 httpURL 到剪贴板。
    func copyLink(for item: RynatFileItem) {
        let server = currentServer()
        guard let location = session?.location(for: item) else {
            setStatus("生成链接失败")
            return
        }
        do {
            let link = try core.buildLink(serverHost: server.host, share: location.share, path: location.remotePath, kind: item.kind)
            NSPasteboard.general.clearContents()
            NSPasteboard.general.setString(link.httpURL, forType: .string)
            setStatus("已复制链接")
            if let window = window {
                ToastPresenter.shared.show(message: "已复制链接", in: window)
            }
        } catch {
            appendLog("Inline copy link failed: \(error.localizedDescription)")
            setStatus("生成链接失败")
        }
    }

    func configurePreviewImageView() {
        previewImageView.symbolConfiguration = NSImage.SymbolConfiguration(pointSize: 58, weight: .regular)
        previewImageView.contentTintColor = RynatUI.accent
        previewImageView.imageScaling = .scaleProportionallyUpOrDown
        previewImageView.translatesAutoresizingMaskIntoConstraints = false

        previewTitleField.font = .systemFont(ofSize: 15.5, weight: .semibold)
        previewTitleField.alignment = .center
        previewTitleField.lineBreakMode = .byTruncatingMiddle
        previewTitleField.maximumNumberOfLines = 2
        previewTitleWidthConstraint = previewTitleField.widthAnchor.constraint(lessThanOrEqualToConstant: 286)
        previewTitleWidthConstraint?.isActive = true

        previewMetaField.font = .systemFont(ofSize: 12, weight: .regular)
        previewMetaField.textColor = RynatUI.muted
        previewMetaField.alignment = .center
        previewMetaField.maximumNumberOfLines = 2
    }

    func configureCommandButtons() {
        configureButton(copyLinkButton, title: "复制链接", symbolName: "link", action: #selector(copyGeneratedLink))
        configureButton(playButton, title: "播放", symbolName: "play.fill", action: #selector(playSelectedItem))
        configureButton(openButton, title: "打开", symbolName: "arrow.up.right.square", action: #selector(openSelectedItem))
    }

    func configureButton(_ button: RynatButton, title: String, symbolName: String, action: Selector) {
        button.target = self
        button.action = action
        button.setContentCompressionResistancePriority(.required, for: .horizontal)
    }

    func configureUploadProgress() {
        uploadProgress.style = .bar
        uploadProgress.controlSize = .small
        uploadProgress.isIndeterminate = true
        uploadProgressWidthConstraint = uploadProgress.widthAnchor.constraint(equalToConstant: 120)
        uploadProgressWidthConstraint?.isActive = true
    }

    func tableScrollView(_ table: NSTableView) -> NSScrollView {
        let scroll = NSScrollView()
        scroll.documentView = table
        scroll.hasVerticalScroller = true
        scroll.hasHorizontalScroller = false
        scroll.borderType = .noBorder
        scroll.drawsBackground = true
        scroll.backgroundColor = RynatUI.surface
        scroll.contentView.backgroundColor = RynatUI.surface
        scroll.translatesAutoresizingMaskIntoConstraints = false
        return scroll
    }
}
