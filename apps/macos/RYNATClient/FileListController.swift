import AppKit

enum RynatFileIcon {
    static func symbolName(for item: RynatFileItem) -> String {
        if item.isDirectory {
            return "folder.fill"
        }
        switch item.fileExtension {
        case "mp4", "mov", "m4v", "mkv", "avi", "webm":
            return "play.rectangle"
        case "jpg", "jpeg", "png", "gif", "webp", "heic", "heif", "avif":
            return "photo"
        case "pdf":
            return "doc.richtext"
        case "xls", "xlsx":
            return "tablecells"
        case "doc", "docx":
            return "doc.text"
        default:
            return "doc"
        }
    }
}

final class RynatFileTableView: NSTableView {
    var contextMenuBuilder: ((Int) -> NSMenu?)?
    var onLayoutChanged: (() -> Void)?

    override func menu(for event: NSEvent) -> NSMenu? {
        let point = convert(event.locationInWindow, from: nil)
        let clickedRow = row(at: point)
        guard clickedRow >= 0 else {
            return contextMenuBuilder?(-1)
        }
        if !selectedRowIndexes.contains(clickedRow) {
            selectRowIndexes(IndexSet(integer: clickedRow), byExtendingSelection: false)
        }
        return contextMenuBuilder?(clickedRow)
    }

    override func layout() {
        super.layout()
        onLayoutChanged?()
    }
}

private final class RynatTableHeaderView: NSTableHeaderView {
    override func draw(_ dirtyRect: NSRect) {
        RynatUI.surface.setFill()
        dirtyRect.fill()

        guard let tableView else {
            return
        }
        for columnIndex in 0..<tableView.numberOfColumns {
            let rect = headerRect(ofColumn: columnIndex)
            guard rect.intersects(dirtyRect) else {
                continue
            }
            let column = tableView.tableColumns[columnIndex]
            column.headerCell.draw(withFrame: rect, in: self)
        }
    }
}

private final class RynatTableHeaderCell: NSTableHeaderCell {
    override func draw(withFrame cellFrame: NSRect, in controlView: NSView) {
        RynatUI.surface.setFill()
        cellFrame.fill()

        let paragraph = NSMutableParagraphStyle()
        paragraph.alignment = alignment
        paragraph.lineBreakMode = .byTruncatingTail
        let attributes: [NSAttributedString.Key: Any] = [
            .font: NSFont.systemFont(ofSize: 13, weight: .medium),
            .foregroundColor: RynatUI.muted,
            .paragraphStyle: paragraph,
        ]
        let insetX: CGFloat = alignment == .right ? 14 : 12
        let textRect = cellFrame.insetBy(dx: insetX, dy: 0)
        let size = (stringValue as NSString).size(withAttributes: attributes)
        let drawRect = NSRect(
            x: textRect.minX,
            y: cellFrame.midY - ceil(size.height) / 2,
            width: textRect.width,
            height: ceil(size.height) + 2
        )
        (stringValue as NSString).draw(in: drawRect, withAttributes: attributes)
    }
}

final class FileListController: NSObject, NSTableViewDataSource, NSTableViewDelegate {
    private(set) var items: [RynatFileItem] = []
    private weak var tableView: RynatFileTableView?
    private var isAdjustingColumnWidths = false
    var onSelectionChanged: ((RynatFileItem?) -> Void)?
    var onContextMenu: ((Int) -> NSMenu?)?
    var onOpen: (() -> Void)?

    func attach(to tableView: RynatFileTableView) {
        self.tableView = tableView
        configure(tableView)
    }

    func reload(items: [RynatFileItem], selectedPath: String?) {
        self.items = items
        tableView?.reloadData()
        adjustColumnWidths()
        if let selectedPath, let index = items.firstIndex(where: { $0.path == selectedPath }) {
            tableView?.selectRowIndexes(IndexSet(integer: index), byExtendingSelection: false)
            tableView?.scrollRowToVisible(index)
        } else if let first = items.first {
            tableView?.selectRowIndexes(IndexSet(integer: 0), byExtendingSelection: false)
            onSelectionChanged?(first)
        } else {
            tableView?.deselectAll(nil)
            onSelectionChanged?(nil)
        }
    }

    func selectedItems() -> [RynatFileItem] {
        guard let tableView else {
            return []
        }
        return tableView.selectedRowIndexes.compactMap { row in
            guard row >= 0, row < items.count else {
                return nil
            }
            return items[row]
        }
    }

    func item(at row: Int) -> RynatFileItem? {
        guard row >= 0, row < items.count else {
            return nil
        }
        return items[row]
    }

    private func configure(_ tableView: RynatFileTableView) {
        tableView.dataSource = self
        tableView.delegate = self
        tableView.headerView = RynatTableHeaderView()
        tableView.headerView?.frame.size.height = 36
        tableView.rowHeight = 36
        tableView.selectionHighlightStyle = .regular
        tableView.usesAlternatingRowBackgroundColors = false
        tableView.backgroundColor = RynatUI.surface
        tableView.gridStyleMask = []
        tableView.allowsMultipleSelection = true
        tableView.allowsColumnResizing = true
        tableView.columnAutoresizingStyle = .noColumnAutoresizing
        tableView.autoresizingMask = [.width, .height]
        tableView.target = self
        tableView.doubleAction = #selector(openSelectedItem)
        tableView.contextMenuBuilder = { [weak self] row in
            self?.onContextMenu?(row)
        }
        tableView.onLayoutChanged = { [weak self] in
            self?.adjustColumnWidths()
        }

        for tableColumn in tableView.tableColumns {
            tableView.removeTableColumn(tableColumn)
        }
        for spec in [
            ("name", "名称", 300.0, 160.0),
            ("kind", "类型", 84.0, 64.0),
            ("size", "大小", 88.0, 72.0),
            ("modified", "修改时间", 168.0, 152.0),
        ] {
            let column = NSTableColumn(identifier: NSUserInterfaceItemIdentifier(spec.0))
            column.title = spec.1
            column.width = spec.2
            column.minWidth = spec.3
            column.resizingMask = [.userResizingMask]
            let headerCell = RynatTableHeaderCell(textCell: spec.1)
            headerCell.alignment = spec.0 == "size" ? .right : .left
            column.headerCell = headerCell
            tableView.addTableColumn(column)
        }
        adjustColumnWidths()
    }

    private func adjustColumnWidths() {
        guard let tableView, !isAdjustingColumnWidths else {
            return
        }
        let visibleWidth = tableView.enclosingScrollView?.contentView.bounds.width ?? tableView.bounds.width
        guard visibleWidth > 0 else {
            return
        }

        isAdjustingColumnWidths = true
        defer { isAdjustingColumnWidths = false }

        let availableWidth = max(360, floor(visibleWidth - 2))
        if abs(tableView.frame.width - availableWidth) > 0.5 {
            var frame = tableView.frame
            frame.size.width = availableWidth
            tableView.frame = frame
        }

        let kindWidth = adaptiveWidth(for: availableWidth, compact: 72, regular: 82, expanded: 92)
        let sizeWidth = adaptiveWidth(for: availableWidth, compact: 76, regular: 86, expanded: 96)
        let modifiedWidth = adaptiveWidth(for: availableWidth, compact: 152, regular: 164, expanded: 176)
        let reservedWidth = kindWidth + sizeWidth + modifiedWidth
        let nameWidth = max(160, availableWidth - reservedWidth)
        let totalWidth = nameWidth + reservedWidth
        let overflow = max(0, totalWidth - availableWidth)

        setColumnWidth(nameWidth - overflow, identifier: "name", in: tableView)
        setColumnWidth(kindWidth, identifier: "kind", in: tableView)
        setColumnWidth(sizeWidth, identifier: "size", in: tableView)
        setColumnWidth(modifiedWidth, identifier: "modified", in: tableView)
    }

    private func adaptiveWidth(for availableWidth: CGFloat, compact: CGFloat, regular: CGFloat, expanded: CGFloat) -> CGFloat {
        if availableWidth >= 760 {
            return expanded
        }
        if availableWidth >= 560 {
            return regular
        }
        return compact
    }

    private func setColumnWidth(_ width: CGFloat, identifier: String, in tableView: NSTableView) {
        guard let column = tableView.tableColumn(withIdentifier: NSUserInterfaceItemIdentifier(identifier)) else {
            return
        }
        let target = max(48, floor(width))
        column.minWidth = min(column.minWidth, target)
        if abs(column.width - target) > 0.5 {
            column.width = target
        }
    }

    func numberOfRows(in tableView: NSTableView) -> Int {
        items.count
    }

    func tableView(_ tableView: NSTableView, rowViewForRow row: Int) -> NSTableRowView? {
        RynatTableRowView()
    }

    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        guard row >= 0, row < items.count else {
            return nil
        }
        let item = items[row]
        switch tableColumn?.identifier.rawValue ?? "name" {
        case "name":
            return nameCell(for: item)
        case "kind":
            return textCell(item.typeLabel, color: RynatUI.muted)
        case "size":
            return textCell(item.sizeLabel, color: RynatUI.muted, alignment: .right)
        case "modified":
            return textCell(item.modifiedLabel, color: RynatUI.muted, trailingInset: 22)
        default:
            return textCell(item.name)
        }
    }

    func tableViewSelectionDidChange(_ notification: Notification) {
        guard let tableView else {
            onSelectionChanged?(nil)
            return
        }
        let row = tableView.selectedRow
        onSelectionChanged?(item(at: row))
    }

    @objc
    private func openSelectedItem() {
        onOpen?()
    }

    private func nameCell(for item: RynatFileItem) -> NSView {
        let cell = NSTableCellView()
        let icon = NSImageView()
        icon.translatesAutoresizingMaskIntoConstraints = false
        icon.symbolConfiguration = NSImage.SymbolConfiguration(pointSize: 15, weight: .regular)
        icon.contentTintColor = item.isDirectory ? RynatUI.folder.withAlphaComponent(0.82) : RynatUI.muted
        icon.image = NSImage(systemSymbolName: RynatFileIcon.symbolName(for: item), accessibilityDescription: item.typeLabel)

        let field = NSTextField(labelWithString: item.name)
        field.translatesAutoresizingMaskIntoConstraints = false
        field.font = .systemFont(ofSize: 13, weight: item.isDirectory ? .semibold : .regular)
        field.textColor = RynatUI.ink
        field.lineBreakMode = .byTruncatingMiddle
        field.toolTip = item.name

        cell.addSubview(icon)
        cell.addSubview(field)
        NSLayoutConstraint.activate([
            icon.leadingAnchor.constraint(equalTo: cell.leadingAnchor, constant: 16),
            icon.centerYAnchor.constraint(equalTo: cell.centerYAnchor),
            icon.widthAnchor.constraint(equalToConstant: 18),
            icon.heightAnchor.constraint(equalToConstant: 18),
            field.leadingAnchor.constraint(equalTo: icon.trailingAnchor, constant: 10),
            field.trailingAnchor.constraint(equalTo: cell.trailingAnchor, constant: -8),
            field.centerYAnchor.constraint(equalTo: cell.centerYAnchor),
        ])
        return cell
    }

    private func textCell(_ text: String, color: NSColor = RynatUI.ink, alignment: NSTextAlignment = .left, trailingInset: CGFloat = 8) -> NSView {
        let cell = NSTableCellView()
        let field = NSTextField(labelWithString: text)
        field.translatesAutoresizingMaskIntoConstraints = false
        field.font = .systemFont(ofSize: 12)
        field.textColor = color
        field.alignment = alignment
        field.lineBreakMode = .byTruncatingTail
        field.toolTip = text
        cell.addSubview(field)
        NSLayoutConstraint.activate([
            field.leadingAnchor.constraint(equalTo: cell.leadingAnchor, constant: 8),
            field.trailingAnchor.constraint(equalTo: cell.trailingAnchor, constant: -trailingInset),
            field.centerYAnchor.constraint(equalTo: cell.centerYAnchor),
        ])
        return cell
    }
}
