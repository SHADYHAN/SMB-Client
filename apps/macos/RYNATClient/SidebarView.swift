import AppKit

protocol SidebarViewDelegate: AnyObject {
    func sidebarView(_ view: SidebarView, didSelect tab: RynatSidebarTab)
    func sidebarView(_ view: SidebarView, didSelectPath path: String)
    func sidebarView(_ view: SidebarView, didSelectFavorite link: QuickLink)
    func sidebarView(_ view: SidebarView, didRequestExpandPath path: String)
    func sidebarViewDidRequestAddFavorite(_ view: SidebarView)
    func sidebarView(_ view: SidebarView, didRemoveFavorite id: String)
}

private final class SidebarTabButton: NSControl {
    private let titleField = NSTextField(labelWithString: "")

    var isActiveTab = false {
        didSet { updateAppearance() }
    }

    init(title: String, target: AnyObject?, action: Selector?) {
        super.init(frame: .zero)
        self.target = target
        self.action = action
        translatesAutoresizingMaskIntoConstraints = false
        wantsLayer = true

        titleField.stringValue = title
        titleField.alignment = .center
        titleField.font = .systemFont(ofSize: 14, weight: .medium)
        titleField.translatesAutoresizingMaskIntoConstraints = false
        addSubview(titleField)
        NSLayoutConstraint.activate([
            titleField.leadingAnchor.constraint(equalTo: leadingAnchor),
            titleField.trailingAnchor.constraint(equalTo: trailingAnchor),
            titleField.centerYAnchor.constraint(equalTo: centerYAnchor),
        ])
        updateAppearance()
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    private func updateAppearance() {
        titleField.textColor = isActiveTab ? RynatUI.ink : RynatUI.muted
        titleField.font = .systemFont(ofSize: 14, weight: isActiveTab ? .semibold : .medium)
        layer?.cornerRadius = 6
        layer?.cornerCurve = .continuous
        layer?.backgroundColor = isActiveTab ? RynatUI.selectionFill.cgColor : NSColor.clear.cgColor
        layer?.borderWidth = 0
        layer?.borderColor = nil
    }

    override func mouseDown(with event: NSEvent) {
        guard isEnabled else { return }
        sendAction(action, to: target)
    }
}

private final class SidebarOutlineView: NSOutlineView {
    var contextMenuBuilder: ((Int) -> NSMenu?)?

    override func menu(for event: NSEvent) -> NSMenu? {
        let point = convert(event.locationInWindow, from: nil)
        return contextMenuBuilder?(row(at: point))
    }
}

/// 侧栏：共享/收藏分段 + 真 NSOutlineView 目录树。
/// 见 docs/ui-redesign.md §3.3、§6.2。砍掉「最近」tab。
final class SidebarView: NSView, NSOutlineViewDataSource, NSOutlineViewDelegate {
    static let defaultWidth: CGFloat = 220
    static let minimumWidth: CGFloat = 180
    static let maximumWidth: CGFloat = 320

    weak var delegate: SidebarViewDelegate?

    private var session: RynatWorkspaceSession?
    private var activeTab: RynatSidebarTab = .shares
    private var isSyncingSelection = false
    private var selectedFavoriteID: String?

    private lazy var shareTabButton = SidebarTabButton(title: "共享", target: self, action: #selector(shareTabClicked))
    private lazy var favoritesTabButton = SidebarTabButton(title: "收藏", target: self, action: #selector(favoritesTabClicked))
    private let tabBar = NSView()
    private let tabGroupBackground = NSView()
    private var tabBarHeightConstraint: NSLayoutConstraint?
    private let outline = SidebarOutlineView()
    private let scroll = NSScrollView()

    /// 共享 tab 用 RynatFileItem 作为 outline item；收藏 tab 用 FavoriteRow。
    private var shareRoots: [RynatFileItem] = []

    private struct FavoriteRow {
        let id: String
        let title: String
        let subtitle: String
        let link: QuickLink
    }
    private var favoriteRows: [FavoriteRow] = []

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        build()
    }

    required init?(coder: NSCoder) {
        super.init(coder: coder)
        build()
    }

    private func build() {
        translatesAutoresizingMaskIntoConstraints = false
        wantsLayer = true
        layer?.backgroundColor = RynatUI.sidebar.cgColor
        let preferredWidth = widthAnchor.constraint(equalToConstant: Self.defaultWidth)
        preferredWidth.priority = .defaultLow
        preferredWidth.isActive = true
        widthAnchor.constraint(greaterThanOrEqualToConstant: Self.minimumWidth).isActive = true
        widthAnchor.constraint(lessThanOrEqualToConstant: Self.maximumWidth).isActive = true
        setContentHuggingPriority(.defaultHigh, for: .horizontal)
        setContentCompressionResistancePriority(.defaultHigh, for: .horizontal)

        buildTabBar()
        addSubview(tabBar)
        addSubview(scroll)
        NSLayoutConstraint.activate([
            tabBar.leadingAnchor.constraint(equalTo: leadingAnchor),
            tabBar.trailingAnchor.constraint(equalTo: trailingAnchor),
            tabBar.topAnchor.constraint(equalTo: topAnchor),
            scroll.leadingAnchor.constraint(equalTo: leadingAnchor),
            scroll.trailingAnchor.constraint(equalTo: trailingAnchor),
            scroll.topAnchor.constraint(equalTo: tabBar.bottomAnchor),
            scroll.bottomAnchor.constraint(equalTo: bottomAnchor),
        ])
        tabBarHeightConstraint = tabBar.heightAnchor.constraint(equalToConstant: 36)
        tabBarHeightConstraint?.isActive = true

        configureOutline()
    }

    private func buildTabBar() {
        tabBar.translatesAutoresizingMaskIntoConstraints = false
        tabBar.wantsLayer = true
        tabBar.layer?.backgroundColor = RynatUI.sidebarTint.cgColor

        tabGroupBackground.translatesAutoresizingMaskIntoConstraints = false
        tabGroupBackground.wantsLayer = true
        tabGroupBackground.layer?.backgroundColor = RynatUI.hoverFill.cgColor
        tabGroupBackground.layer?.cornerRadius = 7
        tabGroupBackground.layer?.cornerCurve = .continuous

        tabBar.addSubview(tabGroupBackground)
        tabBar.addSubview(shareTabButton)
        tabBar.addSubview(favoritesTabButton)
        NSLayoutConstraint.activate([
            tabGroupBackground.leadingAnchor.constraint(equalTo: tabBar.leadingAnchor, constant: 8),
            tabGroupBackground.trailingAnchor.constraint(equalTo: tabBar.trailingAnchor, constant: -8),
            tabGroupBackground.topAnchor.constraint(equalTo: tabBar.topAnchor, constant: 5),
            tabGroupBackground.bottomAnchor.constraint(equalTo: tabBar.bottomAnchor, constant: -5),

            shareTabButton.leadingAnchor.constraint(equalTo: tabGroupBackground.leadingAnchor),
            shareTabButton.trailingAnchor.constraint(equalTo: tabGroupBackground.centerXAnchor),
            shareTabButton.topAnchor.constraint(equalTo: tabGroupBackground.topAnchor),
            shareTabButton.bottomAnchor.constraint(equalTo: tabGroupBackground.bottomAnchor),

            favoritesTabButton.leadingAnchor.constraint(equalTo: shareTabButton.trailingAnchor),
            favoritesTabButton.trailingAnchor.constraint(equalTo: tabGroupBackground.trailingAnchor),
            favoritesTabButton.topAnchor.constraint(equalTo: shareTabButton.topAnchor),
            favoritesTabButton.bottomAnchor.constraint(equalTo: shareTabButton.bottomAnchor),
        ])
        updateTabButtons()
    }

    func setTabBarHeight(_ height: CGFloat) {
        guard height.isFinite, height > 0 else {
            return
        }
        let roundedHeight = round(height)
        guard abs((tabBarHeightConstraint?.constant ?? 0) - roundedHeight) > 0.5 else {
            return
        }
        tabBarHeightConstraint?.constant = roundedHeight
        needsLayout = true
    }

    private func configureOutline() {
        let column = NSTableColumn(identifier: NSUserInterfaceItemIdentifier("sidebar"))
        column.width = Self.defaultWidth
        column.minWidth = Self.minimumWidth
        column.maxWidth = Self.maximumWidth
        column.resizingMask = .autoresizingMask
        outline.addTableColumn(column)
        outline.outlineTableColumn = column
        outline.headerView = nil
        outline.dataSource = self
        outline.delegate = self
        outline.selectionHighlightStyle = .regular
        outline.rowHeight = 30
        outline.indentationPerLevel = 14
        outline.backgroundColor = .clear
        outline.floatsGroupRows = false
        outline.allowsEmptySelection = true
        outline.doubleAction = #selector(rowDoubleClicked)
        outline.target = self
        outline.contextMenuBuilder = { [weak self] row in
            self?.contextMenu(forRow: row)
        }

        scroll.documentView = outline
        scroll.hasVerticalScroller = true
        scroll.hasHorizontalScroller = false
        scroll.borderType = .noBorder
        scroll.drawsBackground = false
        scroll.translatesAutoresizingMaskIntoConstraints = false
        scroll.contentView.backgroundColor = .clear
    }

    func update(session: RynatWorkspaceSession, activeTab: RynatSidebarTab) {
        self.session = session
        self.activeTab = activeTab
        updateTabButtons()
        reloadContent()
    }

    private func reloadContent() {
        guard let session else { return }
        switch activeTab {
        case .shares:
            shareRoots = session.rootItems.filter(\.isDirectory)
        case .favorites:
            favoriteRows = session.favorites.map { link in
                let displayPath = Self.displayPath(for: link)
                return FavoriteRow(
                    id: link.id,
                    title: Self.title(for: link),
                    subtitle: displayPath,
                    link: link
                )
            }
            if let selectedFavoriteID, !favoriteRows.contains(where: { $0.id == selectedFavoriteID }) {
                self.selectedFavoriteID = nil
            }
        }
        outline.reloadData()
        restoreSelection()
    }

    private func restoreSelection() {
        switch activeTab {
        case .shares:
            ensureCurrentPathExpandedAndSelected()
        case .favorites:
            ensureSelectedFavorite()
        }
    }

    private func ensureCurrentPathExpandedAndSelected() {
        guard let session else { return }
        let current = session.currentPath
        // 展开当前路径的所有祖先
        if activeTab == .shares, current != "/" {
            var parts = current.split(separator: "/").map(String.init)
            while parts.count > 1 {
                parts.removeLast()
                let ancestor = "/" + parts.joined(separator: "/")
                if let item = session.findItem(path: ancestor) {
                    outline.expandItem(item)
                }
            }
        }
        // 选中当前目录
        if let item = session.findItem(path: current) {
            let row = outline.row(forItem: item)
            if row >= 0, outline.selectedRow != row {
                isSyncingSelection = true
                defer { isSyncingSelection = false }
                outline.selectRowIndexes(IndexSet(integer: row), byExtendingSelection: false)
            }
        }
    }

    private func ensureSelectedFavorite() {
        guard let selectedFavoriteID,
              let index = favoriteRows.firstIndex(where: { $0.id == selectedFavoriteID })
        else {
            return
        }
        let row = index
        guard row >= 0, outline.selectedRow != row else {
            return
        }
        isSyncingSelection = true
        defer { isSyncingSelection = false }
        outline.selectRowIndexes(IndexSet(integer: row), byExtendingSelection: false)
    }

    private func updateTabButtons() {
        shareTabButton.isActiveTab = activeTab == .shares
        favoritesTabButton.isActiveTab = activeTab == .favorites
    }

    @objc
    private func shareTabClicked() {
        selectTab(.shares)
    }

    @objc
    private func favoritesTabClicked() {
        selectTab(.favorites)
    }

    private func selectTab(_ tab: RynatSidebarTab) {
        guard activeTab != tab else { return }
        activeTab = tab
        updateTabButtons()
        delegate?.sidebarView(self, didSelect: activeTab)
        reloadContent()
    }

    @objc
    private func rowDoubleClicked() {
        let row = outline.clickedRow
        guard row >= 0 else { return }
        if let item = outline.item(atRow: row) as? RynatFileItem {
            selectedFavoriteID = nil
            selectRowWithoutOpening(row)
            delegate?.sidebarView(self, didSelectPath: item.path)
            toggleExpansion(for: item)
        } else if let fav = favoriteItem(atRow: row) {
            selectedFavoriteID = fav.id
            selectRowWithoutOpening(row)
            delegate?.sidebarView(self, didSelectFavorite: fav.link)
        }
    }

    private func selectRowWithoutOpening(_ row: Int) {
        guard outline.selectedRow != row else { return }
        isSyncingSelection = true
        defer { isSyncingSelection = false }
        outline.selectRowIndexes(IndexSet(integer: row), byExtendingSelection: false)
    }

    private func toggleExpansion(for item: RynatFileItem) {
        guard activeTab == .shares else { return }
        if outline.isItemExpanded(item) {
            outline.collapseItem(item)
        } else {
            outline.expandItem(item)
        }
    }

    // MARK: - NSOutlineViewDataSource

    func outlineView(_ outlineView: NSOutlineView, numberOfChildrenOfItem item: Any?) -> Int {
        guard activeTab == .shares else { return favoriteRows.count }
        if item == nil {
            return shareRoots.count
        }
        if let dir = item as? RynatFileItem, let session {
            let path = session.normalizedDisplayPath(dir.path)
            return (session.cachedItems(forDisplayPath: path) ?? []).filter(\.isDirectory).count
        }
        return 0
    }

    func outlineView(_ outlineView: NSOutlineView, child index: Int, ofItem item: Any?) -> Any {
        guard activeTab == .shares else { return favoriteRows[index] }
        if item == nil {
            return shareRoots[index]
        }
        if let dir = item as? RynatFileItem, let session {
            let path = session.normalizedDisplayPath(dir.path)
            let children = (session.cachedItems(forDisplayPath: path) ?? []).filter(\.isDirectory)
            return children[index]
        }
        return shareRoots[index]
    }

    func outlineView(_ outlineView: NSOutlineView, isItemExpandable item: Any) -> Bool {
        if activeTab != .shares { return false }
        guard item is RynatFileItem else { return false }
        // 目录均可展开；未加载时展开会触发 didRequestExpandPath 加载子目录。
        return true
    }

    // MARK: - NSOutlineViewDelegate

    func outlineView(_ outlineView: NSOutlineView, viewFor tableColumn: NSTableColumn?, item: Any) -> NSView? {
        let cell = NSTableCellView()
        let icon = NSImageView()
        icon.translatesAutoresizingMaskIntoConstraints = false
        icon.symbolConfiguration = NSImage.SymbolConfiguration(pointSize: 14, weight: .regular)

        let title = NSTextField(labelWithString: "")
        title.translatesAutoresizingMaskIntoConstraints = false
        title.font = .systemFont(ofSize: 13, weight: .regular)
        title.lineBreakMode = .byTruncatingMiddle
        title.maximumNumberOfLines = 1
        title.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)

        cell.addSubview(icon)
        cell.addSubview(title)
        NSLayoutConstraint.activate([
            icon.leadingAnchor.constraint(equalTo: cell.leadingAnchor, constant: 4),
            icon.centerYAnchor.constraint(equalTo: cell.centerYAnchor),
            icon.widthAnchor.constraint(equalToConstant: 18),
            icon.heightAnchor.constraint(equalToConstant: 18),
            title.leadingAnchor.constraint(equalTo: icon.trailingAnchor, constant: 8),
            title.trailingAnchor.constraint(equalTo: cell.trailingAnchor, constant: -4),
            title.centerYAnchor.constraint(equalTo: cell.centerYAnchor),
        ])

        if let dir = item as? RynatFileItem {
            icon.image = NSImage(systemSymbolName: "folder", accessibilityDescription: nil)
            icon.contentTintColor = RynatUI.folder.withAlphaComponent(0.82)
            title.stringValue = dir.name
            title.font = .systemFont(ofSize: 13, weight: .medium)
            title.textColor = RynatUI.sidebarText
        } else if let fav = item as? FavoriteRow {
            icon.image = NSImage(systemSymbolName: "star.fill", accessibilityDescription: nil)
            icon.contentTintColor = RynatUI.gold.withAlphaComponent(0.90)
            title.stringValue = fav.title
            title.toolTip = fav.subtitle
            title.textColor = RynatUI.sidebarText
        }
        return cell
    }

    func outlineView(_ outlineView: NSOutlineView, rowViewForItem item: Any) -> NSTableRowView? {
        RynatSidebarRowView()
    }

    func outlineView(_ outlineView: NSOutlineView, shouldExpandItem item: Any) -> Bool {
        if let dir = item as? RynatFileItem {
            delegate?.sidebarView(self, didRequestExpandPath: dir.path)
        }
        return true
    }

    func outlineViewSelectionDidChange(_ notification: Notification) {
        guard !isSyncingSelection else {
            return
        }
        let row = outline.selectedRow
        guard row >= 0 else { return }
        if let item = outline.item(atRow: row) as? RynatFileItem {
            selectedFavoriteID = nil
            delegate?.sidebarView(self, didSelectPath: item.path)
        } else if let fav = favoriteItem(atRow: row) {
            selectedFavoriteID = fav.id
            delegate?.sidebarView(self, didSelectFavorite: fav.link)
        }
    }

    private func favoriteItem(atRow row: Int) -> FavoriteRow? {
        guard activeTab == .favorites, favoriteRows.indices.contains(row) else { return nil }
        return favoriteRows[row]
    }

    private func favoriteItem(id: String) -> FavoriteRow? {
        favoriteRows.first { $0.id == id }
    }

    private func contextMenu(forRow row: Int) -> NSMenu? {
        guard let fav = favoriteItem(atRow: row) else {
            return nil
        }
        let menu = NSMenu(title: fav.title)
        let openItem = NSMenuItem(title: "打开收藏", action: #selector(openFavoriteFromMenu(_:)), keyEquivalent: "")
        openItem.target = self
        openItem.representedObject = fav.id
        menu.addItem(openItem)
        menu.addItem(.separator())
        let removeItem = NSMenuItem(title: "取消收藏", action: #selector(removeFavoriteFromMenu(_:)), keyEquivalent: "")
        removeItem.target = self
        removeItem.representedObject = fav.id
        menu.addItem(removeItem)
        return menu
    }

    @objc
    private func openFavoriteFromMenu(_ sender: NSMenuItem) {
        guard let id = sender.representedObject as? String, let fav = favoriteItem(id: id) else {
            return
        }
        selectedFavoriteID = id
        delegate?.sidebarView(self, didSelectFavorite: fav.link)
    }

    @objc
    private func removeFavoriteFromMenu(_ sender: NSMenuItem) {
        guard let id = sender.representedObject as? String else {
            return
        }
        if selectedFavoriteID == id {
            selectedFavoriteID = nil
        }
        delegate?.sidebarView(self, didRemoveFavorite: id)
    }

    private static func title(for link: QuickLink) -> String {
        if let name = link.target.name?.trimmingCharacters(in: .whitespacesAndNewlines), !name.isEmpty {
            return name
        }
        let trimmedPath = link.target.path.trimmingCharacters(in: CharacterSet(charactersIn: "/"))
        if let last = trimmedPath.split(separator: "/").last {
            return String(last)
        }
        return link.target.share
    }

    private static func displayPath(for link: QuickLink) -> String {
        let trimmedPath = link.target.path.trimmingCharacters(in: CharacterSet(charactersIn: "/"))
        if trimmedPath.isEmpty {
            return "/\(link.target.share)"
        }
        return "/\(link.target.share)/\(trimmedPath)"
    }

    /// 收藏 tab 右键删除（option+点击）。
    func handleOptionClickOnRow(_ row: Int) {
        if let fav = favoriteItem(atRow: row) {
            delegate?.sidebarView(self, didRemoveFavorite: fav.id)
        }
    }
}
