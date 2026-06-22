import AppKit

final class BrowserRootContainerView: NSView {
    private let headerView: NSView
    private let bodyView: NSView
    private let statusView: NSView
    private let headerHeight: CGFloat
    private let statusHeight: CGFloat

    init(headerView: NSView, bodyView: NSView, statusView: NSView, headerHeight: CGFloat = 44, statusHeight: CGFloat = 30) {
        self.headerView = headerView
        self.bodyView = bodyView
        self.statusView = statusView
        self.headerHeight = headerHeight
        self.statusHeight = statusHeight
        super.init(frame: .zero)
        autoresizesSubviews = true
        [headerView, bodyView, statusView].forEach {
            $0.translatesAutoresizingMaskIntoConstraints = true
            addSubview($0)
        }
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    override var intrinsicContentSize: NSSize {
        NSSize(width: NSView.noIntrinsicMetric, height: NSView.noIntrinsicMetric)
    }

    override var fittingSize: NSSize {
        bounds.size
    }

    override func setFrameSize(_ newSize: NSSize) {
        super.setFrameSize(newSize)
        tileSubviews()
    }

    override func layout() {
        super.layout()
        tileSubviews()
    }

    private func tileSubviews() {
        let width = bounds.width
        let height = bounds.height
        let bodyHeight = max(0, height - headerHeight - statusHeight)
        statusView.frame = NSRect(x: 0, y: 0, width: width, height: statusHeight)
        bodyView.frame = NSRect(x: 0, y: statusHeight, width: width, height: bodyHeight)
        headerView.frame = NSRect(x: 0, y: statusHeight + bodyHeight, width: width, height: headerHeight)
        headerView.needsLayout = true
        bodyView.needsLayout = true
        statusView.needsLayout = true
    }
}

final class DropContainerView: NSView {
    var onFilesDropped: (([URL]) -> Void)?

    override var intrinsicContentSize: NSSize {
        NSSize(width: NSView.noIntrinsicMetric, height: NSView.noIntrinsicMetric)
    }

    override var fittingSize: NSSize {
        bounds.size
    }

    override func setFrameSize(_ newSize: NSSize) {
        super.setFrameSize(newSize)
        fillRootSubview()
    }

    override func layout() {
        super.layout()
        fillRootSubview()
    }

    private func fillRootSubview() {
        subviews.forEach { $0.frame = bounds }
    }

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        registerForDraggedTypes([.fileURL])
    }

    required init?(coder: NSCoder) {
        super.init(coder: coder)
        registerForDraggedTypes([.fileURL])
    }

    override func draggingEntered(_ sender: NSDraggingInfo) -> NSDragOperation {
        sender.draggingPasteboard.canReadObject(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) ? .copy : []
    }

    override func draggingUpdated(_ sender: NSDraggingInfo) -> NSDragOperation {
        draggingEntered(sender)
    }

    override func performDragOperation(_ sender: NSDraggingInfo) -> Bool {
        guard let urls = sender.draggingPasteboard.readObjects(
            forClasses: [NSURL.self],
            options: [.urlReadingFileURLsOnly: true]
        ) as? [URL], !urls.isEmpty else {
            return false
        }
        onFilesDropped?(urls)
        return true
    }
}
