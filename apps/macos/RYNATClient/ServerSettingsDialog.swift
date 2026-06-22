import AppKit

struct ServerSettingsDraft {
    let profileID: String?
    let displayName: String
    let host: String
    let setActive: Bool
}

enum ServerSettingsAction {
    case save(ServerSettingsDraft)
    case delete(String)
}

enum ServerSettingsDialog {
    static func request(
        bootstrap: AppBootstrapState?,
        fallbackName: String = "共享网盘",
        fallbackHost: String = "192.168.102.136"
    ) -> ServerSettingsAction? {
        var forcedAction: ServerSettingsAction?
        var runningAlert: NSAlert?
        let form = ServerManagerView(
            profiles: bootstrap?.serverProfiles ?? [],
            activeProfileID: bootstrap?.activeServer?.id,
            fallbackName: fallbackName,
            fallbackHost: fallbackHost,
            onDeleteRequested: { action in
                forcedAction = action
                runningAlert?.window.orderOut(nil)
                NSApp.stopModal(withCode: .abort)
            }
        )

        while true {
            let alert = NSAlert()
            alert.messageText = "服务器管理"
            alert.alertStyle = .informational
            alert.accessoryView = form
            alert.addButton(withTitle: "保存")
            alert.addButton(withTitle: "取消")
            runningAlert = alert

            let response = alert.runModal()
            if let forcedAction {
                return forcedAction
            }
            guard response == .alertFirstButtonReturn else {
                return nil
            }

            guard let action = form.pendingAction() else {
                showValidationMessage("请输入地址")
                continue
            }
            return action
        }
    }

    private static func showValidationMessage(_ message: String) {
        let alert = NSAlert()
        alert.messageText = message
        alert.alertStyle = .warning
        alert.addButton(withTitle: "确定")
        alert.runModal()
    }
}

private final class ServerManagerView: NSView, NSTableViewDataSource, NSTableViewDelegate, NSTextFieldDelegate {
    private struct ServerRow {
        var id: String?
        var displayName: String
        var host: String

        var isNew: Bool {
            id == nil
        }
    }

    private var rows: [ServerRow]
    private let activeProfileID: String?
    private let fallbackName: String
    private let fallbackHost: String
    private let tableView = NSTableView()
    private let nameField = NSTextField()
    private let hostField = NSTextField()
    private let removeButton = NSButton(title: "-", target: nil, action: nil)
    private let onDeleteRequested: (ServerSettingsAction) -> Void
    private var selectedIndex: Int?
    private var isCreatingNewProfile = false
    private var requestedDeleteID: String?

    init(
        profiles: [StoredServerProfile],
        activeProfileID: String?,
        fallbackName: String,
        fallbackHost: String,
        onDeleteRequested: @escaping (ServerSettingsAction) -> Void
    ) {
        self.rows = profiles.map { ServerRow(id: $0.id, displayName: $0.displayName, host: $0.linkHost) }
        self.activeProfileID = activeProfileID
        self.fallbackName = fallbackName
        self.fallbackHost = fallbackHost
        self.onDeleteRequested = onDeleteRequested
        self.selectedIndex = profiles.firstIndex { $0.id == activeProfileID } ?? profiles.indices.first
        self.isCreatingNewProfile = profiles.isEmpty
        super.init(frame: NSRect(x: 0, y: 0, width: 560, height: 258))
        buildView()
        applyInitialSelection()
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    func numberOfRows(in tableView: NSTableView) -> Int {
        rows.count
    }

    func tableView(_ tableView: NSTableView, rowViewForRow row: Int) -> NSTableRowView? {
        ServerManagerRowView()
    }

    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        guard rows.indices.contains(row) else {
            return nil
        }
        let item = rows[row]
        let cell = NSTableCellView()
        let isPendingDefault = row == selectedIndex
        let icon = NSImageView()
        icon.translatesAutoresizingMaskIntoConstraints = false
        icon.image = isPendingDefault ? NSImage(systemSymbolName: "checkmark.circle.fill", accessibilityDescription: "默认服务器") : nil
        icon.symbolConfiguration = NSImage.SymbolConfiguration(pointSize: 14, weight: .medium)
        icon.contentTintColor = RynatUI.accent
        icon.widthAnchor.constraint(equalToConstant: 18).isActive = true
        icon.heightAnchor.constraint(equalToConstant: 18).isActive = true

        let title = NSTextField(labelWithString: item.displayName.isEmpty ? fallbackName : item.displayName)
        title.font = .systemFont(ofSize: 13, weight: isPendingDefault ? .semibold : .regular)
        title.textColor = RynatUI.ink
        title.lineBreakMode = .byTruncatingTail

        let subtitleText = item.host.isEmpty ? "待填写地址" : item.host
        let subtitle = NSTextField(labelWithString: subtitleText)
        subtitle.font = .systemFont(ofSize: 11)
        subtitle.textColor = item.host.isEmpty ? RynatUI.faint : RynatUI.muted
        subtitle.lineBreakMode = .byTruncatingMiddle

        let textStack = NSStackView()
        textStack.orientation = .vertical
        textStack.alignment = .leading
        textStack.spacing = 2
        textStack.addArrangedSubview(title)
        textStack.addArrangedSubview(subtitle)

        let rowStack = NSStackView()
        rowStack.orientation = .horizontal
        rowStack.alignment = .centerY
        rowStack.spacing = 7
        rowStack.translatesAutoresizingMaskIntoConstraints = false
        rowStack.addArrangedSubview(icon)
        rowStack.addArrangedSubview(textStack)
        cell.addSubview(rowStack)

        NSLayoutConstraint.activate([
            rowStack.leadingAnchor.constraint(equalTo: cell.leadingAnchor, constant: 14),
            rowStack.trailingAnchor.constraint(equalTo: cell.trailingAnchor, constant: -16),
            rowStack.centerYAnchor.constraint(equalTo: cell.centerYAnchor),
        ])
        return cell
    }

    func tableViewSelectionDidChange(_ notification: Notification) {
        let row = tableView.selectedRow
        guard rows.indices.contains(row) else {
            return
        }
        let previousIndex = selectedIndex
        selectedIndex = row
        fill(row: rows[row])
        var reloadRows = IndexSet(integer: row)
        if let previousIndex, rows.indices.contains(previousIndex) {
            reloadRows.insert(previousIndex)
        }
        tableView.reloadData(forRowIndexes: reloadRows, columnIndexes: IndexSet(integer: 0))
    }

    func controlTextDidChange(_ obj: Notification) {
        guard let selectedIndex, rows.indices.contains(selectedIndex) else {
            return
        }
        if obj.object as? NSTextField === nameField {
            rows[selectedIndex].displayName = nameField.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        } else if obj.object as? NSTextField === hostField {
            rows[selectedIndex].host = hostField.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        }
        tableView.reloadData(forRowIndexes: IndexSet(integer: selectedIndex), columnIndexes: IndexSet(integer: 0))
    }

    func pendingAction() -> ServerSettingsAction? {
        if let requestedDeleteID {
            return .delete(requestedDeleteID)
        }

        let name = nameField.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        let host = hostField.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !host.isEmpty else {
            return nil
        }

        let selectedRow = selectedIndex.flatMap { rows[safe: $0] }
        let profileID = isCreatingNewProfile ? nil : selectedRow?.id?.trimmingCharacters(in: .whitespacesAndNewlines)
        let shouldSetActive = profileID == nil || profileID == activeProfileID
        return .save(
            ServerSettingsDraft(
                profileID: profileID?.isEmpty == false ? profileID : nil,
                displayName: name.isEmpty ? fallbackName : name,
                host: host,
                setActive: shouldSetActive
            )
        )
    }

    private func buildView() {
        let split = NSStackView()
        split.orientation = .horizontal
        split.alignment = .top
        split.spacing = 18
        split.translatesAutoresizingMaskIntoConstraints = false
        addSubview(split)

        split.addArrangedSubview(leftPane())
        split.addArrangedSubview(rightPane())

        NSLayoutConstraint.activate([
            split.leadingAnchor.constraint(equalTo: leadingAnchor),
            split.trailingAnchor.constraint(equalTo: trailingAnchor),
            split.topAnchor.constraint(equalTo: topAnchor),
            split.bottomAnchor.constraint(equalTo: bottomAnchor),
        ])
    }

    private func leftPane() -> NSView {
        tableView.headerView = nil
        tableView.rowHeight = 48
        tableView.delegate = self
        tableView.dataSource = self
        tableView.selectionHighlightStyle = .regular
        tableView.backgroundColor = .clear
        tableView.intercellSpacing = .zero
        tableView.usesAlternatingRowBackgroundColors = false
        let column = NSTableColumn(identifier: NSUserInterfaceItemIdentifier("server"))
        column.width = 196
        tableView.addTableColumn(column)

        let scroll = NSScrollView()
        scroll.documentView = tableView
        scroll.hasVerticalScroller = true
        scroll.autohidesScrollers = true
        scroll.scrollerStyle = .overlay
        scroll.borderType = .noBorder
        scroll.drawsBackground = false
        scroll.backgroundColor = .clear
        scroll.contentView.drawsBackground = false
        scroll.contentView.backgroundColor = .clear
        scroll.translatesAutoresizingMaskIntoConstraints = false

        let listContainer = ServerManagerListContainerView()
        listContainer.translatesAutoresizingMaskIntoConstraints = false
        listContainer.addSubview(scroll)
        NSLayoutConstraint.activate([
            scroll.leadingAnchor.constraint(equalTo: listContainer.leadingAnchor),
            scroll.trailingAnchor.constraint(equalTo: listContainer.trailingAnchor),
            scroll.topAnchor.constraint(equalTo: listContainer.topAnchor),
            scroll.bottomAnchor.constraint(equalTo: listContainer.bottomAnchor),
            listContainer.widthAnchor.constraint(equalToConstant: 198),
            listContainer.heightAnchor.constraint(equalToConstant: 218),
        ])

        let addButton = NSButton(title: "+", target: self, action: #selector(createNewProfile))
        addButton.bezelStyle = .rounded
        addButton.widthAnchor.constraint(equalToConstant: 32).isActive = true

        removeButton.target = self
        removeButton.action = #selector(confirmDeleteProfile)
        removeButton.bezelStyle = .rounded
        removeButton.widthAnchor.constraint(equalToConstant: 32).isActive = true

        let buttonRow = NSStackView()
        buttonRow.orientation = .horizontal
        buttonRow.spacing = 6
        buttonRow.addArrangedSubview(addButton)
        buttonRow.addArrangedSubview(removeButton)
        buttonRow.addArrangedSubview(NSView())

        let stack = NSStackView()
        stack.orientation = .vertical
        stack.spacing = 8
        stack.addArrangedSubview(listContainer)
        stack.addArrangedSubview(buttonRow)
        stack.widthAnchor.constraint(equalToConstant: 198).isActive = true
        return stack
    }

    private func rightPane() -> NSView {
        let title = NSTextField(labelWithString: "服务器信息")
        title.font = .systemFont(ofSize: 14, weight: .semibold)
        title.textColor = RynatUI.ink

        configureField(nameField, placeholder: fallbackName)
        configureField(hostField, placeholder: fallbackHost)

        let stack = NSStackView()
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 12
        stack.addArrangedSubview(title)
        stack.addArrangedSubview(formRow(title: "名称", field: nameField))
        stack.addArrangedSubview(formRow(title: "地址", field: hostField))
        stack.widthAnchor.constraint(equalToConstant: 344).isActive = true
        return stack
    }

    private func configureField(_ field: NSTextField, placeholder: String) {
        field.placeholderString = placeholder
        field.font = .systemFont(ofSize: 13)
        field.lineBreakMode = .byTruncatingMiddle
        field.delegate = self
        field.widthAnchor.constraint(equalToConstant: 284).isActive = true
    }

    private func formRow(title: String, field: NSTextField) -> NSView {
        let label = NSTextField(labelWithString: title)
        label.font = .systemFont(ofSize: 12, weight: .medium)
        label.textColor = RynatUI.muted
        label.alignment = .right
        label.widthAnchor.constraint(equalToConstant: 40).isActive = true

        let row = NSStackView()
        row.orientation = .horizontal
        row.alignment = .centerY
        row.spacing = 10
        row.addArrangedSubview(label)
        row.addArrangedSubview(field)
        return row
    }

    private func applyInitialSelection() {
        tableView.reloadData()
        if let selectedIndex, rows.indices.contains(selectedIndex) {
            tableView.selectRowIndexes(IndexSet(integer: selectedIndex), byExtendingSelection: false)
            fill(row: rows[selectedIndex])
        } else {
            selectNewProfile()
        }
    }

    private func fill(row: ServerRow) {
        requestedDeleteID = nil
        isCreatingNewProfile = row.isNew
        nameField.stringValue = row.displayName
        hostField.stringValue = row.host
        removeButton.isEnabled = !row.isNew && rows.count > 1
    }

    @objc
    private func createNewProfile() {
        selectNewProfile()
    }

    private func selectNewProfile() {
        requestedDeleteID = nil
        isCreatingNewProfile = true
        let newRow = ServerRow(id: nil, displayName: "新服务器", host: "")
        rows.append(newRow)
        let index = rows.count - 1
        selectedIndex = index
        tableView.reloadData()
        tableView.selectRowIndexes(IndexSet(integer: index), byExtendingSelection: false)
        nameField.stringValue = newRow.displayName
        hostField.stringValue = newRow.host
        removeButton.isEnabled = false
    }

    @objc
    private func confirmDeleteProfile() {
        guard let selectedIndex,
              rows.indices.contains(selectedIndex),
              rows.count > 1,
              let profileID = rows[selectedIndex].id
        else {
            return
        }
        let profile = rows[selectedIndex]
        let alert = NSAlert()
        alert.messageText = "移除服务器？"
        alert.informativeText = "将移除“\(profile.displayName)”及其已保存凭据。分享链接不会删除。"
        alert.alertStyle = .warning
        alert.addButton(withTitle: "移除")
        alert.addButton(withTitle: "取消")
        guard alert.runModal() == .alertFirstButtonReturn else {
            return
        }
        requestedDeleteID = profileID
        onDeleteRequested(.delete(profileID))
    }
}

private extension Array {
    subscript(safe index: Int) -> Element? {
        indices.contains(index) ? self[index] : nil
    }
}

private final class ServerManagerListContainerView: NSView {
    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        layer?.cornerRadius = 12
        layer?.cornerCurve = .continuous
        layer?.masksToBounds = true
        layer?.backgroundColor = NSColor.controlBackgroundColor.withAlphaComponent(0.72).cgColor
        layer?.borderColor = RynatUI.lineSoft.cgColor
        layer?.borderWidth = 1
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    override func viewDidChangeEffectiveAppearance() {
        super.viewDidChangeEffectiveAppearance()
        layer?.backgroundColor = NSColor.controlBackgroundColor.withAlphaComponent(0.72).cgColor
        layer?.borderColor = RynatUI.lineSoft.cgColor
    }
}

private final class ServerManagerRowView: NSTableRowView {
    override var interiorBackgroundStyle: NSView.BackgroundStyle {
        .normal
    }

    override func drawSelection(in dirtyRect: NSRect) {
        guard selectionHighlightStyle != .none else {
            return
        }

        let visibleInSuperview = superview?.visibleRect ?? bounds
        let visibleBounds = bounds.intersection(convert(visibleInSuperview, from: superview))
        let selectionRect = visibleBounds.insetBy(dx: 8, dy: 4)
        guard selectionRect.width > 0, selectionRect.height > 0 else {
            return
        }

        RynatUI.selectionFill.setFill()
        NSBezierPath(roundedRect: selectionRect, xRadius: 10, yRadius: 10).fill()
    }
}
