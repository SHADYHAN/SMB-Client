import AppKit

/// 品牌配色：保留登录页与品牌视觉使用，主界面另走 RynatUI 语义色。
/// 见 docs/ui-redesign.md §4.1.1。仅用于 logo、侧栏头部小标、空状态图标、主按钮点缀；
/// 大面积背景与文本走 RynatUI 的系统语义色。
enum RynatBrand {
    static let accent = dynamicColor(light: NSColor(srgbRed: 0x2E/255, green: 0x6B/255, blue: 0xE6/255, alpha: 1),
                                     dark: NSColor(srgbRed: 0x6B/255, green: 0x9C/255, blue: 0xF5/255, alpha: 1))
    static let secondary = dynamicColor(light: NSColor(srgbRed: 0x36/255, green: 0xA8/255, blue: 0x78/255, alpha: 1),
                                        dark: NSColor(srgbRed: 0x62/255, green: 0xCE/255, blue: 0x9E/255, alpha: 1))
    static let tagImage = dynamicColor(light: NSColor(srgbRed: 0x58/255, green: 0x46/255, blue: 0x9B/255, alpha: 1),
                                       dark: NSColor(srgbRed: 0xA4/255, green: 0x94/255, blue: 0xDB/255, alpha: 1))
    static let tagVideo = dynamicColor(light: NSColor(srgbRed: 0xD8/255, green: 0xB2/255, blue: 0x14/255, alpha: 1),
                                       dark: NSColor(srgbRed: 0xF0/255, green: 0xD6/255, blue: 0x64/255, alpha: 1))

    private static func dynamicColor(light: NSColor, dark: NSColor) -> NSColor {
        NSColor(name: nil) { appearance in
            if appearance.bestMatch(from: [.darkAqua, .vibrantDark, .accessibilityHighContrastDarkAqua, .accessibilityHighContrastVibrantDark]) != nil {
                return dark
            }
            return light
        }
    }
}

enum RynatUI {
    // 主界面保持白色底，只用轻分隔和低饱和状态色建立层次。
    static let canvas = dynamicColor(
        light: NSColor.white,
        dark: NSColor.windowBackgroundColor
    )
    static let sidebar = dynamicColor(
        light: NSColor.white,
        dark: NSColor.controlBackgroundColor
    )
    static let sidebarTint = dynamicColor(
        light: NSColor.white,
        dark: NSColor.controlBackgroundColor
    )
    static let chrome = dynamicColor(
        light: NSColor.white,
        dark: NSColor.windowBackgroundColor
    )
    static let workspace = canvas
    static let surface = dynamicColor(
        light: NSColor(srgbRed: 0xFF/255, green: 0xFF/255, blue: 0xFF/255, alpha: 1),
        dark: NSColor.textBackgroundColor
    )
    static let panel = dynamicColor(
        light: NSColor.white,
        dark: NSColor.controlBackgroundColor
    )
    static let elevated = dynamicColor(
        light: NSColor.white,
        dark: NSColor.controlBackgroundColor
    )
    static let previewSurface = dynamicColor(
        light: NSColor.white,
        dark: NSColor.underPageBackgroundColor
    )
    static let line = dynamicColor(
        light: NSColor(srgbRed: 0xDC/255, green: 0xE2/255, blue: 0xEA/255, alpha: 1),
        dark: NSColor.separatorColor
    )
    static let lineSoft = dynamicColor(
        light: NSColor(srgbRed: 0xEC/255, green: 0xF0/255, blue: 0xF5/255, alpha: 1),
        dark: NSColor.separatorColor
    )
    static let ink = NSColor.labelColor
    static let muted = NSColor.secondaryLabelColor
    static let faint = NSColor.tertiaryLabelColor
    static let sidebarText = NSColor.labelColor
    static let sidebarMuted = NSColor.secondaryLabelColor
    static let accent = dynamicColor(
        light: NSColor(srgbRed: 0x2F/255, green: 0x6F/255, blue: 0xD8/255, alpha: 1),
        dark: NSColor(srgbRed: 0x86/255, green: 0xB0/255, blue: 0xF2/255, alpha: 1)
    )
    static let accentHover = dynamicColor(
        light: NSColor(srgbRed: 0x25/255, green: 0x61/255, blue: 0xC4/255, alpha: 1),
        dark: NSColor(srgbRed: 0x9A/255, green: 0xBE/255, blue: 0xF7/255, alpha: 1)
    )
    static let selectionFill = dynamicColor(
        light: NSColor(srgbRed: 0xEC/255, green: 0xF5/255, blue: 0xFF/255, alpha: 1),
        dark: NSColor.selectedContentBackgroundColor.withAlphaComponent(0.28)
    )
    static let selectionFillEmphasized = dynamicColor(
        light: NSColor(srgbRed: 0xD9/255, green: 0xEA/255, blue: 0xFF/255, alpha: 1),
        dark: NSColor.selectedContentBackgroundColor.withAlphaComponent(0.34)
    )
    static let hoverFill = dynamicColor(
        light: NSColor(srgbRed: 0xF7/255, green: 0xFB/255, blue: 0xFF/255, alpha: 1),
        dark: NSColor.selectedContentBackgroundColor.withAlphaComponent(0.18)
    )
    static let accentSoft = selectionFill
    static let accentMist = hoverFill
    static let glass = NSColor.white
    static let glassStrong = NSColor.white
    static let folder = dynamicColor(
        light: NSColor(srgbRed: 0x36/255, green: 0xA8/255, blue: 0x78/255, alpha: 1),
        dark: NSColor(srgbRed: 0x72/255, green: 0xD0/255, blue: 0xA6/255, alpha: 1)
    )
    static let purple = dynamicColor(
        light: NSColor(srgbRed: 0x58/255, green: 0x46/255, blue: 0x9B/255, alpha: 1),
        dark: NSColor(srgbRed: 0xA4/255, green: 0x94/255, blue: 0xDB/255, alpha: 1)
    )
    static let gold = dynamicColor(
        light: NSColor(srgbRed: 0xD8/255, green: 0xB2/255, blue: 0x14/255, alpha: 1),
        dark: NSColor(srgbRed: 0xF0/255, green: 0xD6/255, blue: 0x64/255, alpha: 1)
    )
    static let success = folder
    static let warning = gold

    private static func dynamicColor(light: NSColor, dark: NSColor) -> NSColor {
        NSColor(name: nil) { appearance in
            if appearance.bestMatch(from: [.darkAqua, .vibrantDark, .accessibilityHighContrastDarkAqua, .accessibilityHighContrastVibrantDark]) != nil {
                return dark
            }
            return light
        }
    }

    static func title(_ text: String, size: CGFloat = 13, weight: NSFont.Weight = .semibold) -> NSTextField {
        let field = NSTextField(labelWithString: text)
        field.font = .systemFont(ofSize: size, weight: weight)
        field.textColor = ink
        field.lineBreakMode = .byTruncatingTail
        return field
    }

    static func label(_ text: String, size: CGFloat = 12) -> NSTextField {
        let field = NSTextField(labelWithString: text)
        field.font = .systemFont(ofSize: size, weight: .regular)
        field.textColor = muted
        field.lineBreakMode = .byTruncatingTail
        return field
    }

    static func monoLabel(_ text: String, size: CGFloat = 11) -> NSTextField {
        let field = NSTextField(labelWithString: text)
        field.font = .monospacedSystemFont(ofSize: size, weight: .regular)
        field.textColor = muted
        field.lineBreakMode = .byTruncatingMiddle
        return field
    }

    static func symbolButton(_ symbolName: String, accessibilityLabel: String, target: AnyObject?, action: Selector?) -> RynatButton {
        let button = RynatButton(title: "", symbolName: symbolName, style: .ghost)
        button.target = target
        button.action = action
        button.toolTip = accessibilityLabel
        button.accessibilityLabel = accessibilityLabel
        return button
    }

    static func commandButton(_ title: String, symbolName: String, accessibilityLabel: String, target: AnyObject?, action: Selector?) -> RynatButton {
        let button = RynatButton(title: title, symbolName: symbolName, style: .secondary)
        button.target = target
        button.action = action
        button.toolTip = accessibilityLabel
        button.accessibilityLabel = accessibilityLabel
        return button
    }

    static func separator() -> NSView {
        let view = NSView()
        view.translatesAutoresizingMaskIntoConstraints = false
        view.wantsLayer = true
        view.layer?.backgroundColor = lineSoft.cgColor
        view.heightAnchor.constraint(equalToConstant: 1).isActive = true
        return view
    }

    static func verticalSeparator() -> NSView {
        let view = NSView()
        view.translatesAutoresizingMaskIntoConstraints = false
        view.wantsLayer = true
        view.layer?.backgroundColor = lineSoft.cgColor
        view.widthAnchor.constraint(equalToConstant: 1).isActive = true
        return view
    }

    static func spacer() -> NSView {
        let view = NSView()
        view.setContentHuggingPriority(.defaultLow, for: .horizontal)
        view.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        return view
    }

    static func pill(_ text: String, color: NSColor) -> NSTextField {
        let field = NSTextField(labelWithString: text)
        field.font = .systemFont(ofSize: 11, weight: .medium)
        field.textColor = color
        field.alignment = .center
        field.wantsLayer = true
        field.layer?.cornerRadius = 7
        field.layer?.backgroundColor = color.withAlphaComponent(0.10).cgColor
        field.translatesAutoresizingMaskIntoConstraints = false
        field.heightAnchor.constraint(equalToConstant: 20).isActive = true
        field.widthAnchor.constraint(greaterThanOrEqualToConstant: 48).isActive = true
        return field
    }

    static func wrap(_ child: NSView, insets: NSEdgeInsets) -> NSView {
        let container = NSView()
        container.translatesAutoresizingMaskIntoConstraints = false
        child.translatesAutoresizingMaskIntoConstraints = false
        container.addSubview(child)
        NSLayoutConstraint.activate([
            child.leadingAnchor.constraint(equalTo: container.leadingAnchor, constant: insets.left),
            child.trailingAnchor.constraint(equalTo: container.trailingAnchor, constant: -insets.right),
            child.topAnchor.constraint(equalTo: container.topAnchor, constant: insets.top),
            child.bottomAnchor.constraint(equalTo: container.bottomAnchor, constant: -insets.bottom),
        ])
        return container
    }

    static func roundedBox(background: NSColor, radius: CGFloat = 10, border: NSColor? = nil) -> NSView {
        let view = NSView()
        view.translatesAutoresizingMaskIntoConstraints = false
        view.wantsLayer = true
        view.layer?.cornerRadius = radius
        view.layer?.backgroundColor = background.cgColor
        if let border {
            view.layer?.borderWidth = 1
            view.layer?.borderColor = border.cgColor
        }
        return view
    }
}

final class RynatSplitView: NSSplitView {
    override var dividerThickness: CGFloat {
        8
    }

    override func drawDivider(in rect: NSRect) {
        RynatUI.canvas.setFill()
        rect.fill()
    }
}

final class RynatPopupButton: NSPopUpButton {
    convenience init() {
        self.init(frame: .zero, pullsDown: false)
    }

    override init(frame buttonFrame: NSRect, pullsDown flag: Bool) {
        super.init(frame: buttonFrame, pullsDown: flag)
        configure()
    }

    required init?(coder: NSCoder) {
        super.init(coder: coder)
        configure()
    }

    private func configure() {
        translatesAutoresizingMaskIntoConstraints = false
        isBordered = false
        bezelStyle = .regularSquare
        controlSize = .regular
        font = .systemFont(ofSize: 12.5, weight: .medium)
        contentTintColor = RynatUI.ink
        wantsLayer = true
        layer?.cornerRadius = 8
        layer?.cornerCurve = .continuous
        layer?.backgroundColor = RynatUI.hoverFill.cgColor
        heightAnchor.constraint(equalToConstant: 30).isActive = true
        widthAnchor.constraint(greaterThanOrEqualToConstant: 92).isActive = true
    }
}

final class RynatButton: NSControl {
    enum Style {
        case primary
        case secondary
        case ghost
        case subtle
    }

    private let backgroundView = NSView()
    private let imageView = NSImageView()
    private let titleField = NSTextField(labelWithString: "")
    private let stack = NSStackView()
    private let style: Style
    private var trackingAreaRef: NSTrackingArea?
    private var hovered = false {
        didSet { updateAppearance() }
    }
    private var pressed = false {
        didSet { updateAppearance() }
    }

    var accessibilityLabel: String? {
        didSet {
            setAccessibilityLabel(accessibilityLabel)
        }
    }

    func setSymbolName(_ symbolName: String, accessibilityDescription: String? = nil) {
        imageView.image = NSImage(systemSymbolName: symbolName, accessibilityDescription: accessibilityDescription)
    }

    init(title: String, symbolName: String?, style: Style = .secondary) {
        self.style = style
        super.init(frame: .zero)
        translatesAutoresizingMaskIntoConstraints = false
        wantsLayer = true
        focusRingType = .default
        setAccessibilityElement(true)
        setAccessibilityRole(.button)
        setAccessibilityLabel(title.isEmpty ? nil : title)
        let defaultHeight = heightAnchor.constraint(equalToConstant: 34)
        defaultHeight.priority = .defaultHigh
        defaultHeight.isActive = true

        backgroundView.translatesAutoresizingMaskIntoConstraints = false
        backgroundView.wantsLayer = true
        backgroundView.layer?.cornerRadius = 9
        backgroundView.layer?.cornerCurve = .continuous
        addSubview(backgroundView)

        stack.orientation = .horizontal
        stack.alignment = .centerY
        stack.spacing = title.isEmpty ? 0 : 7
        stack.translatesAutoresizingMaskIntoConstraints = false
        stack.setContentHuggingPriority(.required, for: .horizontal)
        stack.setContentCompressionResistancePriority(.required, for: .horizontal)

        if let symbolName {
            imageView.image = NSImage(systemSymbolName: symbolName, accessibilityDescription: title)
            imageView.symbolConfiguration = NSImage.SymbolConfiguration(pointSize: 13.5, weight: .medium)
            imageView.imageScaling = .scaleProportionallyDown
            imageView.translatesAutoresizingMaskIntoConstraints = false
            imageView.widthAnchor.constraint(equalToConstant: 16).isActive = true
            imageView.heightAnchor.constraint(equalToConstant: 16).isActive = true
            stack.addArrangedSubview(imageView)
        }

        if !title.isEmpty {
            titleField.stringValue = title
            titleField.font = .systemFont(ofSize: 12.5, weight: .medium)
            titleField.lineBreakMode = .byTruncatingTail
            titleField.setContentCompressionResistancePriority(.required, for: .horizontal)
            stack.addArrangedSubview(titleField)
        }

        addSubview(stack)
        var constraints = [
            backgroundView.leadingAnchor.constraint(equalTo: leadingAnchor),
            backgroundView.trailingAnchor.constraint(equalTo: trailingAnchor),
            backgroundView.topAnchor.constraint(equalTo: topAnchor),
            backgroundView.bottomAnchor.constraint(equalTo: bottomAnchor),
            stack.centerXAnchor.constraint(equalTo: centerXAnchor),
            stack.centerYAnchor.constraint(equalTo: centerYAnchor),
        ]

        if title.isEmpty {
            constraints.append(contentsOf: [
                stack.widthAnchor.constraint(equalToConstant: 16),
                stack.heightAnchor.constraint(equalToConstant: 16),
            ])
        } else {
            constraints.append(contentsOf: [
                stack.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 13),
                stack.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -13),
            ])
        }
        NSLayoutConstraint.activate(constraints)

        if title.isEmpty {
            let defaultWidth = widthAnchor.constraint(equalToConstant: 34)
            defaultWidth.priority = .defaultHigh
            defaultWidth.isActive = true
        }
        updateAppearance()
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    override var isEnabled: Bool {
        didSet {
            updateAppearance()
        }
    }

    override var acceptsFirstResponder: Bool {
        isEnabled
    }

    override func becomeFirstResponder() -> Bool {
        let accepted = super.becomeFirstResponder()
        needsDisplay = true
        return accepted
    }

    override func resignFirstResponder() -> Bool {
        let accepted = super.resignFirstResponder()
        needsDisplay = true
        return accepted
    }

    override func drawFocusRingMask() {
        NSBezierPath(roundedRect: bounds.insetBy(dx: 1, dy: 1), xRadius: 9, yRadius: 9).fill()
    }

    override func keyDown(with event: NSEvent) {
        guard isEnabled else {
            return
        }
        if event.keyCode == 36 || event.keyCode == 49 {
            _ = sendAction(action, to: target)
        } else {
            super.keyDown(with: event)
        }
    }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let trackingAreaRef {
            removeTrackingArea(trackingAreaRef)
        }
        let area = NSTrackingArea(
            rect: bounds,
            options: [.mouseEnteredAndExited, .activeInKeyWindow, .inVisibleRect],
            owner: self,
            userInfo: nil
        )
        addTrackingArea(area)
        trackingAreaRef = area
    }

    override func mouseEntered(with event: NSEvent) {
        hovered = true
    }

    override func mouseExited(with event: NSEvent) {
        hovered = false
        pressed = false
    }

    override func mouseDown(with event: NSEvent) {
        guard isEnabled else { return }
        pressed = true
        window?.trackEvents(matching: [.leftMouseUp, .leftMouseDragged], timeout: NSEvent.foreverDuration, mode: .eventTracking) { [weak self] nextEvent, stop in
            guard let self, let nextEvent else {
                stop.pointee = true
                return
            }
            let point = self.convert(nextEvent.locationInWindow, from: nil)
            if nextEvent.type == .leftMouseDragged {
                self.hovered = self.bounds.contains(point)
            } else if nextEvent.type == .leftMouseUp {
                self.pressed = false
                if self.bounds.contains(point) {
                    _ = self.sendAction(self.action, to: self.target)
                }
                stop.pointee = true
            }
        }
    }

    override func accessibilityPerformPress() -> Bool {
        guard isEnabled else {
            return false
        }
        return sendAction(action, to: target)
    }

    private func updateAppearance() {
        let alpha: CGFloat = isEnabled ? 1 : 0.38
        let colors = colorsForCurrentState()
        backgroundView.layer?.backgroundColor = colors.background.withAlphaComponent(colors.background.alphaComponent * alpha).cgColor
        backgroundView.layer?.borderWidth = colors.border == nil ? 0 : 1
        backgroundView.layer?.borderColor = colors.border?.cgColor
        titleField.textColor = colors.foreground.withAlphaComponent(alpha)
        imageView.contentTintColor = colors.foreground.withAlphaComponent(alpha)
    }

    private func colorsForCurrentState() -> (background: NSColor, foreground: NSColor, border: NSColor?) {
        switch style {
        case .primary:
            let background = pressed ? RynatUI.accent.blended(withFraction: 0.16, of: .black) ?? RynatUI.accent :
                hovered ? RynatUI.accent.blended(withFraction: 0.10, of: .white) ?? RynatUI.accent : RynatUI.accent
            return (background, .white, nil)
        case .secondary:
            let background = pressed ? RynatUI.selectionFillEmphasized :
                hovered ? RynatUI.hoverFill : RynatUI.glassStrong
            return (background, RynatUI.ink, RynatUI.lineSoft)
        case .ghost:
            let background = pressed ? RynatUI.selectionFillEmphasized :
                hovered ? RynatUI.hoverFill : .clear
            return (background, RynatUI.muted, nil)
        case .subtle:
            let background = pressed ? RynatUI.selectionFillEmphasized :
                hovered ? RynatUI.selectionFill : RynatUI.hoverFill
            return (background, RynatUI.accent, nil)
        }
    }
}
