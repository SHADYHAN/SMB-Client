import AppKit

protocol LoginViewControllerDelegate: AnyObject {
    func loginViewController(_ controller: LoginViewController, didLoginWith server: RynatServerProfile)
    func loginViewController(_ controller: LoginViewController, didUpdateBootstrap bootstrap: AppBootstrapState)
}

final class LoginViewController: NSViewController {
    weak var delegate: LoginViewControllerDelegate?

    private static let storedPasswordPlaceholder = "********"
    fileprivate static let cardWidth: CGFloat = 376
    fileprivate static let formWidth: CGFloat = 304
    static var minimumContentSize: NSSize {
        NSSize(width: max(cardWidth + 264, 640), height: 520)
    }
    static var defaultContentSize: NSSize {
        NSSize(width: minimumContentSize.width + 80, height: minimumContentSize.height + 54)
    }

    private let core = RynatCore()
    private var bootstrap: AppBootstrapState
    private var storedProfiles: [StoredServerProfile]
    private var activeCredential: StoredServerCredential?
    private var selectedServer: RynatServerProfile
    private let usernameField = NSTextField()
    private let passwordField = NSSecureTextField()
    private let rememberButton = NSButton(checkboxWithTitle: "记住密码", target: nil, action: nil)
    private let autoLoginButton = NSButton(checkboxWithTitle: "自动登录", target: nil, action: nil)
    private let connectButton = LoginPrimaryButton(title: "登录")
    private let settingsButton = NSButton()
    private let statusField = NSTextField(labelWithString: "")
    private let statusIcon = NSImageView()

    init(bootstrap: AppBootstrapState) {
        self.bootstrap = bootstrap
        self.storedProfiles = bootstrap.serverProfiles
        self.activeCredential = bootstrap.activeCredential
        let activeProfile = bootstrap.activeServer ?? bootstrap.serverProfiles.first
        if let activeProfile {
            self.selectedServer = RynatServerProfile.fromStored(
                activeProfile,
                credential: bootstrap.activeCredential
            )
        } else {
            self.selectedServer = RynatServerProfile(
                id: "",
                connectionID: UUID().uuidString,
                name: "共享网盘",
                host: "192.168.102.136",
                protocolLabel: "SMB3 自动",
                accountName: "",
                rememberPassword: false,
                autoLogin: false,
                shares: []
            )
        }
        super.init(nibName: nil, bundle: nil)
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    override func loadView() {
        let root = LoginBackgroundView()
        root.autoresizingMask = [.width, .height]

        let card = LoginGlassCardView()

        let stack = NSStackView()
        stack.orientation = .vertical
        stack.alignment = .centerX
        stack.spacing = 10
        stack.translatesAutoresizingMaskIntoConstraints = false

        configureField(usernameField, placeholder: "用户名")
        configureField(passwordField, placeholder: "密码")
        configureSettingsButton()

        let usernameInput = LoginFieldView(field: usernameField, symbolName: "person")
        let passwordInput = LoginFieldView(field: passwordField, symbolName: "lock")

        rememberButton.state = selectedServer.rememberPassword ? .on : .off
        rememberButton.font = .systemFont(ofSize: 12.5, weight: .regular)
        rememberButton.controlSize = .small
        autoLoginButton.state = selectedServer.autoLogin ? .on : .off
        autoLoginButton.font = .systemFont(ofSize: 12.5, weight: .regular)
        autoLoginButton.controlSize = .small
        autoLoginButton.target = self
        autoLoginButton.action = #selector(autoLoginToggled)

        connectButton.keyEquivalent = "\r"
        connectButton.target = self
        connectButton.action = #selector(connect)
        connectButton.widthAnchor.constraint(equalToConstant: Self.formWidth).isActive = true

        statusField.font = .systemFont(ofSize: 12, weight: .regular)
        statusField.textColor = .systemRed
        statusField.alignment = .left
        statusField.maximumNumberOfLines = 2
        statusField.isHidden = true
        statusIcon.symbolConfiguration = NSImage.SymbolConfiguration(pointSize: 13, weight: .regular)
        statusIcon.contentTintColor = .systemRed
        statusIcon.isHidden = true

        let header = headerView()
        stack.addArrangedSubview(header)
        stack.setCustomSpacing(22, after: header)
        stack.addArrangedSubview(usernameInput)
        stack.addArrangedSubview(passwordInput)
        stack.setCustomSpacing(12, after: passwordInput)
        let options = optionsRow()
        stack.addArrangedSubview(options)
        stack.setCustomSpacing(16, after: options)
        stack.addArrangedSubview(connectButton)
        stack.addArrangedSubview(statusRow())

        card.translatesAutoresizingMaskIntoConstraints = false
        stack.translatesAutoresizingMaskIntoConstraints = false
        card.addSubview(stack)
        card.addSubview(settingsButton)
        root.addSubview(card)

        let cardWidth = card.widthAnchor.constraint(equalToConstant: Self.cardWidth)
        cardWidth.priority = .defaultHigh

        NSLayoutConstraint.activate([
            card.centerXAnchor.constraint(equalTo: root.centerXAnchor),
            card.centerYAnchor.constraint(equalTo: root.centerYAnchor, constant: -2),
            cardWidth,
            card.widthAnchor.constraint(lessThanOrEqualTo: root.widthAnchor, constant: -48),
            card.topAnchor.constraint(greaterThanOrEqualTo: root.topAnchor, constant: 24),
            card.bottomAnchor.constraint(lessThanOrEqualTo: root.bottomAnchor, constant: -24),

            stack.leadingAnchor.constraint(equalTo: card.leadingAnchor, constant: 36),
            stack.trailingAnchor.constraint(equalTo: card.trailingAnchor, constant: -36),
            stack.topAnchor.constraint(equalTo: card.topAnchor, constant: 32),
            stack.bottomAnchor.constraint(equalTo: card.bottomAnchor, constant: -26),

            settingsButton.topAnchor.constraint(equalTo: card.topAnchor, constant: 16),
            settingsButton.trailingAnchor.constraint(equalTo: card.trailingAnchor, constant: -16),
            settingsButton.widthAnchor.constraint(equalToConstant: 28),
            settingsButton.heightAnchor.constraint(equalToConstant: 28),
        ])

        view = root
        fillServer(selectedServer)
    }

    func updateBootstrap(_ bootstrap: AppBootstrapState) {
        self.bootstrap = bootstrap
        self.storedProfiles = bootstrap.serverProfiles
        self.activeCredential = bootstrap.activeCredential
        if let activeProfile = bootstrap.activeServer ?? bootstrap.serverProfiles.first {
            selectedServer = RynatServerProfile.fromStored(
                activeProfile,
                credential: bootstrap.activeCredential
            )
        } else {
            selectedServer = Self.defaultServerProfile()
        }
        if isViewLoaded {
            fillServer(selectedServer)
        }
    }

    override func viewDidAppear() {
        super.viewDidAppear()
        DispatchQueue.main.async { [weak self] in
            self?.focusInitialLoginControl()
        }
    }

    private func headerView() -> NSView {
        let stack = NSStackView()
        stack.orientation = .vertical
        stack.alignment = .centerX
        stack.spacing = 7
        stack.translatesAutoresizingMaskIntoConstraints = false

        let mark = LoginLogoImageView()
        mark.widthAnchor.constraint(equalToConstant: 54).isActive = true
        mark.heightAnchor.constraint(equalToConstant: 58).isActive = true

        let title = NSTextField(labelWithString: "RYNAT 共享网盘")
        title.font = .systemFont(ofSize: 19, weight: .semibold)
        title.textColor = RynatUI.ink
        title.alignment = .center

        stack.addArrangedSubview(mark)
        stack.addArrangedSubview(title)
        return stack
    }

    private func optionsRow() -> NSView {
        let row = NSStackView()
        row.orientation = .horizontal
        row.alignment = .centerY
        row.spacing = 18
        row.translatesAutoresizingMaskIntoConstraints = false
        row.addArrangedSubview(rememberButton)
        row.addArrangedSubview(RynatUI.spacer())
        row.addArrangedSubview(autoLoginButton)
        row.widthAnchor.constraint(equalToConstant: Self.formWidth).isActive = true
        return row
    }

    private func statusRow() -> NSView {
        let row = NSStackView()
        row.orientation = .horizontal
        row.spacing = 6
        row.alignment = .centerY
        row.translatesAutoresizingMaskIntoConstraints = false
        row.addArrangedSubview(statusIcon)
        row.addArrangedSubview(statusField)
        row.widthAnchor.constraint(equalToConstant: Self.formWidth).isActive = true
        return row
    }

    private func configureField(_ field: NSTextField, placeholder: String) {
        field.placeholderAttributedString = NSAttributedString(
            string: placeholder,
            attributes: [
                .font: NSFont.systemFont(ofSize: 14, weight: .regular),
                .foregroundColor: RynatUI.faint,
            ]
        )
        field.font = .systemFont(ofSize: 14, weight: .regular)
        field.textColor = RynatUI.ink
        field.controlSize = .regular
        field.isBordered = false
        field.drawsBackground = false
        field.focusRingType = .none
        field.lineBreakMode = .byTruncatingTail
        field.maximumNumberOfLines = 1
        field.cell?.usesSingleLineMode = true
        field.cell?.wraps = false
        field.cell?.isScrollable = true
    }

    private func configureSettingsButton() {
        settingsButton.translatesAutoresizingMaskIntoConstraints = false
        settingsButton.isBordered = false
        settingsButton.bezelStyle = .regularSquare
        settingsButton.image = NSImage(systemSymbolName: "gearshape", accessibilityDescription: "服务器设置")
        settingsButton.imagePosition = .imageOnly
        settingsButton.contentTintColor = RynatUI.muted
        settingsButton.toolTip = "服务器设置"
        settingsButton.target = self
        settingsButton.action = #selector(showServerSettings)
        settingsButton.setButtonType(.momentaryPushIn)
    }

    private func fillServer(_ server: RynatServerProfile?) {
        guard let server else {
            return
        }
        selectedServer = server
        usernameField.stringValue = server.accountName
        passwordField.stringValue = activeCredential?.serverProfileID == server.id
            ? Self.storedPasswordPlaceholder
            : ""
        rememberButton.state = server.rememberPassword ? .on : .off
        autoLoginButton.state = server.autoLogin ? .on : .off
    }

    private static func defaultServerProfile() -> RynatServerProfile {
        RynatServerProfile(
            id: "",
            connectionID: UUID().uuidString,
            name: "共享网盘",
            host: "192.168.102.136",
            protocolLabel: "SMB3 自动",
            accountName: "",
            rememberPassword: false,
            autoLogin: false,
            shares: []
        )
    }

    private func focusInitialLoginControl() {
        if usernameField.stringValue.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            view.window?.makeFirstResponder(usernameField)
        } else if passwordField.stringValue.isEmpty {
            view.window?.makeFirstResponder(passwordField)
        } else {
            view.window?.makeFirstResponder(nil)
        }
    }

    @objc
    private func autoLoginToggled() {
        // 自动登录隐含记住密码。
        if autoLoginButton.state == .on {
            rememberButton.state = .on
        }
    }

    @objc
    private func showServerSettings() {
        guard let action = ServerSettingsDialog.request(bootstrap: bootstrap) else {
            return
        }

        do {
            let state: AppBootstrapState
            switch action {
            case .save(let draft):
                let existingProfile = draft.profileID.flatMap { id in
                    bootstrap.serverProfiles.first(where: { $0.id == id })
                }
                _ = try core.saveServerProfile(
                    id: draft.profileID,
                    displayName: draft.displayName,
                    host: draft.host,
                    username: existingProfile?.username,
                    setActive: draft.setActive
                )
                state = try core.appBootstrap()
            case .delete(let id):
                state = try core.deleteServerProfile(id: id)
            }
            updateBootstrap(state)
            delegate?.loginViewController(self, didUpdateBootstrap: state)
            showInfoStatus("服务器设置已保存")
        } catch {
            showStatus("保存失败，请重试")
        }
    }

    @objc
    private func connect() {
        let username = usernameField.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !username.isEmpty else {
            showStatus("请输入用户名")
            return
        }
        let hasStoredCredential = activeCredential?.serverProfileID == selectedServer.id
            && activeCredential?.username == username
            && selectedServer.rememberPassword
        let typedPassword = passwordField.stringValue
        let shouldUseStoredCredential = hasStoredCredential
            && typedPassword == Self.storedPasswordPlaceholder
        guard shouldUseStoredCredential
            || (!typedPassword.isEmpty && typedPassword != Self.storedPasswordPlaceholder)
        else {
            showStatus("请输入密码")
            return
        }

        connectButton.isEnabled = false
        hideStatus()

        let host = selectedServer.host.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !host.isEmpty else {
            showStatus("服务器地址无效")
            connectButton.isEnabled = true
            return
        }
        let serverName = selectedServer.name
        let password = typedPassword
        let shouldRememberPassword = rememberButton.state == .on
        let shouldAutoLogin = autoLoginButton.state == .on
        let selectedProfileID = selectedServer.id.isEmpty ? nil : selectedServer.id
        let connectionID = selectedProfileID ?? selectedServer.connectionID
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            let backgroundCore = RynatCore()
            do {
                let result: SmbConnectResult
                if shouldUseStoredCredential, let selectedProfileID {
                    result = try backgroundCore.smbConnectStoredCredential(
                        serverProfileID: selectedProfileID,
                        connectionID: connectionID
                    )
                } else {
                    result = try backgroundCore.smbConnect(
                        host: host,
                        username: username,
                        password: password,
                        connectionID: connectionID
                    )
                }
                let storedProfile = try backgroundCore.saveServerProfile(
                    id: selectedProfileID,
                    displayName: serverName,
                    host: result.host,
                    username: username,
                    setActive: true
                )
                    if shouldRememberPassword {
                        if !shouldUseStoredCredential {
                            _ = try backgroundCore.saveServerCredential(
                                serverProfileID: storedProfile.id,
                                username: username,
                            password: password,
                            rememberPassword: true,
                                autoLogin: shouldAutoLogin
                            )
                        } else {
                            _ = try backgroundCore.updateServerCredentialOptions(
                                serverProfileID: storedProfile.id,
                                rememberPassword: true,
                                autoLogin: shouldAutoLogin
                            )
                        }
                    } else {
                        try? backgroundCore.deleteServerCredential(serverProfileID: storedProfile.id)
                    }
                let matched = RynatServerProfile(
                    id: storedProfile.id,
                    connectionID: result.connectionID,
                    name: serverName,
                    host: result.host,
                    protocolLabel: result.dialectLabel,
                    accountName: username,
                    rememberPassword: shouldRememberPassword,
                    autoLogin: shouldAutoLogin,
                    shares: result.shares.map { RynatShare(name: $0.name, comment: $0.comment) }
                )
                DispatchQueue.main.async {
                    guard let self else {
                        return
                    }
                    self.selectedServer = matched
                    if let index = self.storedProfiles.firstIndex(where: { $0.id == storedProfile.id }) {
                        self.storedProfiles[index] = storedProfile
                    } else {
                        self.storedProfiles.append(storedProfile)
                    }
                    if shouldRememberPassword {
                        self.activeCredential = StoredServerCredential(
                            serverProfileID: storedProfile.id,
                            username: username,
                            rememberPassword: true,
                            autoLogin: shouldAutoLogin,
                            updatedAt: ""
                        )
                    } else {
                        self.activeCredential = nil
                    }
                    self.connectButton.isEnabled = true
                    self.delegate?.loginViewController(self, didLoginWith: matched)
                }
            } catch {
                DispatchQueue.main.async {
                    self?.showStatus(self?.simpleLoginErrorMessage(for: error) ?? "连接失败，请重试")
                    self?.connectButton.isEnabled = true
                }
            }
        }
    }

    private func simpleLoginErrorMessage(for error: Error) -> String {
        guard let coreError = error as? RynatCoreError else {
            return "连接失败，请重试"
        }
        switch coreError.errorCode {
        case "auth":
            return "账号或密码错误"
        case "reconnectable":
            return "连接失败，请重试"
        default:
            return "登录失败，请重试"
        }
    }

    private func showStatus(_ message: String) {
        statusField.stringValue = message
        statusField.textColor = .systemRed
        statusIcon.image = NSImage(systemSymbolName: "exclamationmark.triangle", accessibilityDescription: "错误")
        statusIcon.contentTintColor = .systemRed
        statusField.isHidden = false
        statusIcon.isHidden = false
    }

    private func showInfoStatus(_ message: String) {
        statusField.stringValue = message
        statusField.textColor = RynatBrand.secondary
        statusIcon.image = NSImage(systemSymbolName: "checkmark.circle", accessibilityDescription: "成功")
        statusIcon.contentTintColor = RynatBrand.secondary
        statusField.isHidden = false
        statusIcon.isHidden = false
    }

    private func hideStatus() {
        statusField.isHidden = true
        statusIcon.isHidden = true
    }
}

private final class LoginBackgroundView: NSView {
    override var isOpaque: Bool {
        true
    }

    override func draw(_ dirtyRect: NSRect) {
        let rect = bounds
        NSColor(srgbRed: 0.93, green: 0.96, blue: 0.99, alpha: 1).setFill()
        rect.fill()

        NSGradient(
            starting: NSColor(srgbRed: 0.96, green: 0.98, blue: 1.00, alpha: 1),
            ending: NSColor(srgbRed: 0.84, green: 0.91, blue: 0.99, alpha: 1)
        )?.draw(in: rect, angle: 92)

        drawSoftGlow(
            in: NSRect(x: rect.minX - rect.width * 0.18, y: rect.maxY - rect.height * 0.58, width: rect.width * 0.64, height: rect.height * 0.78),
            colors: [
                NSColor(srgbRed: 0.20, green: 0.46, blue: 0.95, alpha: 0.16),
                NSColor(srgbRed: 0.20, green: 0.46, blue: 0.95, alpha: 0.00),
            ]
        )
        drawSoftGlow(
            in: NSRect(x: rect.maxX - rect.width * 0.28, y: rect.minY - rect.height * 0.18, width: rect.width * 0.50, height: rect.height * 0.70),
            colors: [
                NSColor(srgbRed: 0.16, green: 0.66, blue: 0.48, alpha: 0.11),
                NSColor(srgbRed: 0.16, green: 0.66, blue: 0.48, alpha: 0.00),
            ]
        )

        drawCorporateScene(in: rect)
        drawNetworkLines(in: rect)
        drawVignette(in: rect)
    }

    private func drawSoftGlow(in rect: NSRect, colors: [NSColor]) {
        let path = NSBezierPath(ovalIn: rect)
        NSGradient(colors: colors)?.draw(in: path, angle: 0)
    }

    private func drawCorporateScene(in rect: NSRect) {
        let baseY = rect.midY - 118
        let sceneWidth = min(430, rect.width * 0.50)
        let sceneX = rect.maxX - sceneWidth - max(34, rect.width * 0.06)
        let buildingRect = NSRect(x: sceneX, y: baseY, width: sceneWidth, height: 270)

        let backPanel = NSBezierPath(roundedRect: buildingRect, xRadius: 24, yRadius: 24)
        NSColor.white.withAlphaComponent(0.22).setFill()
        backPanel.fill()

        NSColor.white.withAlphaComponent(0.34).setStroke()
        backPanel.lineWidth = 1
        backPanel.stroke()

        let columnCount = 5
        let gap: CGFloat = 12
        let columnWidth = (buildingRect.width - gap * CGFloat(columnCount + 1)) / CGFloat(columnCount)
        for index in 0..<columnCount {
            let height = CGFloat([142, 198, 230, 176, 208][index])
            let x = buildingRect.minX + gap + CGFloat(index) * (columnWidth + gap)
            let columnRect = NSRect(x: x, y: buildingRect.minY + 22, width: columnWidth, height: height)
            let column = NSBezierPath(roundedRect: columnRect, xRadius: 14, yRadius: 14)
            let alpha = 0.20 + CGFloat(index % 2) * 0.05
            NSColor.white.withAlphaComponent(alpha).setFill()
            column.fill()

            NSColor(srgbRed: 0.33, green: 0.51, blue: 0.78, alpha: 0.15).setStroke()
            column.lineWidth = 1
            column.stroke()

            drawWindowStripes(in: columnRect)
        }

        let storageRect = NSRect(x: buildingRect.minX + 58, y: buildingRect.minY + 42, width: buildingRect.width - 116, height: 68)
        let storage = NSBezierPath(roundedRect: storageRect, xRadius: 18, yRadius: 18)
        NSColor.white.withAlphaComponent(0.30).setFill()
        storage.fill()
        NSColor(srgbRed: 0.40, green: 0.58, blue: 0.86, alpha: 0.12).setStroke()
        storage.lineWidth = 1
        storage.stroke()

        for index in 0..<3 {
            let tray = NSRect(x: storageRect.minX + 18, y: storageRect.maxY - 18 - CGFloat(index) * 18, width: storageRect.width - 36, height: 5)
            let trayPath = NSBezierPath(roundedRect: tray, xRadius: 2.5, yRadius: 2.5)
            NSColor.white.withAlphaComponent(0.42).setFill()
            trayPath.fill()
        }

        let dotRect = NSRect(x: storageRect.maxX - 36, y: storageRect.midY - 4, width: 8, height: 8)
        NSColor(srgbRed: 0.24, green: 0.71, blue: 0.48, alpha: 0.72).setFill()
        NSBezierPath(ovalIn: dotRect).fill()
    }

    private func drawWindowStripes(in rect: NSRect) {
        NSColor.white.withAlphaComponent(0.26).setStroke()
        let rows = 5
        for row in 0..<rows {
            let y = rect.maxY - 24 - CGFloat(row) * 28
            guard y > rect.minY + 18 else { continue }
            let path = NSBezierPath()
            path.lineWidth = 1
            path.move(to: NSPoint(x: rect.minX + 12, y: y))
            path.line(to: NSPoint(x: rect.maxX - 12, y: y))
            path.stroke()
        }
    }

    private func drawNetworkLines(in rect: NSRect) {
        let points = [
            NSPoint(x: rect.minX + rect.width * 0.17, y: rect.minY + rect.height * 0.35),
            NSPoint(x: rect.minX + rect.width * 0.27, y: rect.minY + rect.height * 0.50),
            NSPoint(x: rect.minX + rect.width * 0.20, y: rect.minY + rect.height * 0.66),
            NSPoint(x: rect.maxX - rect.width * 0.24, y: rect.minY + rect.height * 0.67),
            NSPoint(x: rect.maxX - rect.width * 0.16, y: rect.minY + rect.height * 0.43),
        ]

        let line = NSBezierPath()
        line.lineWidth = 1.2
        line.move(to: points[0])
        for point in points.dropFirst() {
            line.line(to: point)
        }
        NSColor(srgbRed: 0.12, green: 0.33, blue: 0.72, alpha: 0.12).setStroke()
        line.stroke()

        for (index, point) in points.enumerated() {
            let size: CGFloat = index == 1 ? 7 : 5
            let dot = NSRect(x: point.x - size / 2, y: point.y - size / 2, width: size, height: size)
            NSColor.white.withAlphaComponent(0.62).setFill()
            NSBezierPath(ovalIn: dot.insetBy(dx: -3, dy: -3)).fill()
            (index == 4 ? RynatBrand.secondary : RynatBrand.accent).withAlphaComponent(0.62).setFill()
            NSBezierPath(ovalIn: dot).fill()
        }
    }

    private func drawVignette(in rect: NSRect) {
        let path = NSBezierPath(rect: rect)
        NSGradient(colors: [
            NSColor.white.withAlphaComponent(0.00),
            NSColor.white.withAlphaComponent(0.32),
        ])?.draw(in: path, angle: 270)
    }
}

private final class LoginGlassCardView: NSView {
    private let effectView = NSVisualEffectView()
    private let fillView = NSView()

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        translatesAutoresizingMaskIntoConstraints = false
        wantsLayer = true
        layer?.cornerRadius = 18
        layer?.cornerCurve = .continuous
        layer?.shadowColor = NSColor.black.cgColor
        layer?.shadowOpacity = 0.14
        layer?.shadowRadius = 22
        layer?.shadowOffset = NSSize(width: 0, height: -8)

        effectView.translatesAutoresizingMaskIntoConstraints = false
        effectView.blendingMode = .withinWindow
        effectView.material = .popover
        effectView.state = .active
        effectView.alphaValue = 0.76
        effectView.wantsLayer = true
        effectView.layer?.cornerRadius = 18
        effectView.layer?.cornerCurve = .continuous
        effectView.layer?.masksToBounds = true
        effectView.layer?.borderWidth = 1
        effectView.layer?.borderColor = NSColor.white.withAlphaComponent(0.28).cgColor

        fillView.translatesAutoresizingMaskIntoConstraints = false
        fillView.wantsLayer = true
        fillView.layer?.cornerRadius = 18
        fillView.layer?.cornerCurve = .continuous
        fillView.layer?.backgroundColor = NSColor.clear.cgColor

        addSubview(effectView)
        addSubview(fillView)

        NSLayoutConstraint.activate([
            effectView.leadingAnchor.constraint(equalTo: leadingAnchor),
            effectView.trailingAnchor.constraint(equalTo: trailingAnchor),
            effectView.topAnchor.constraint(equalTo: topAnchor),
            effectView.bottomAnchor.constraint(equalTo: bottomAnchor),
            fillView.leadingAnchor.constraint(equalTo: leadingAnchor),
            fillView.trailingAnchor.constraint(equalTo: trailingAnchor),
            fillView.topAnchor.constraint(equalTo: topAnchor),
            fillView.bottomAnchor.constraint(equalTo: bottomAnchor),
        ])
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }
}

private final class LoginFieldView: NSView {
    private let field: NSTextField
    private let iconView = NSImageView()
    private var isEditing = false {
        didSet { updateAppearance() }
    }

    init(field: NSTextField, symbolName: String) {
        self.field = field
        super.init(frame: .zero)
        translatesAutoresizingMaskIntoConstraints = false
        wantsLayer = true
        layer?.cornerRadius = 13
        layer?.cornerCurve = .continuous

        iconView.image = NSImage(systemSymbolName: symbolName, accessibilityDescription: nil)
        iconView.symbolConfiguration = NSImage.SymbolConfiguration(pointSize: 14, weight: .medium)
        iconView.imageScaling = .scaleProportionallyDown
        iconView.contentTintColor = RynatUI.faint
        iconView.translatesAutoresizingMaskIntoConstraints = false

        field.translatesAutoresizingMaskIntoConstraints = false

        addSubview(iconView)
        addSubview(field)

        NSLayoutConstraint.activate([
            widthAnchor.constraint(equalToConstant: LoginViewController.formWidth),
            heightAnchor.constraint(equalToConstant: 44),

            iconView.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 15),
            iconView.centerYAnchor.constraint(equalTo: centerYAnchor),
            iconView.widthAnchor.constraint(equalToConstant: 16),
            iconView.heightAnchor.constraint(equalToConstant: 16),

            field.leadingAnchor.constraint(equalTo: iconView.trailingAnchor, constant: 11),
            field.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -15),
            field.centerYAnchor.constraint(equalTo: centerYAnchor),
            field.heightAnchor.constraint(equalToConstant: 22),
        ])

        NotificationCenter.default.addObserver(
            self,
            selector: #selector(fieldDidBeginEditing),
            name: NSText.didBeginEditingNotification,
            object: field
        )
        NotificationCenter.default.addObserver(
            self,
            selector: #selector(fieldDidEndEditing),
            name: NSText.didEndEditingNotification,
            object: field
        )
        updateAppearance()
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    deinit {
        NotificationCenter.default.removeObserver(self)
    }

    override func mouseDown(with event: NSEvent) {
        window?.makeFirstResponder(field)
    }

    @objc
    private func fieldDidBeginEditing() {
        isEditing = true
    }

    @objc
    private func fieldDidEndEditing() {
        isEditing = false
    }

    private func updateAppearance() {
        let background = isEditing
            ? NSColor.white.withAlphaComponent(0.86)
            : NSColor.white.withAlphaComponent(0.64)
        let border = isEditing
            ? RynatBrand.accent.withAlphaComponent(0.62)
            : NSColor.white.withAlphaComponent(0.58)

        layer?.backgroundColor = background.cgColor
        layer?.borderWidth = 1
        layer?.borderColor = border.cgColor
        iconView.contentTintColor = isEditing ? RynatBrand.accent : RynatUI.faint
    }
}

private final class LoginPrimaryButton: NSButton {
    private var trackingAreaRef: NSTrackingArea?
    private var hovered = false {
        didSet { needsDisplay = true }
    }
    private var pressed = false {
        didSet { needsDisplay = true }
    }

    init(title: String) {
        super.init(frame: .zero)
        self.title = title
        translatesAutoresizingMaskIntoConstraints = false
        isBordered = false
        bezelStyle = .regularSquare
        focusRingType = .default
        heightAnchor.constraint(equalToConstant: 44).isActive = true
        setButtonType(.momentaryPushIn)
        font = .systemFont(ofSize: 14, weight: .semibold)
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    override var isEnabled: Bool {
        didSet { needsDisplay = true }
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
        super.mouseDown(with: event)
        pressed = false
    }

    override func draw(_ dirtyRect: NSRect) {
        let rect = bounds.insetBy(dx: 0.5, dy: 0.5)
        let path = NSBezierPath(roundedRect: rect, xRadius: 13, yRadius: 13)

        let base = pressed
            ? RynatBrand.accent.blended(withFraction: 0.18, of: .black) ?? RynatBrand.accent
            : hovered
                ? RynatBrand.accent.blended(withFraction: 0.10, of: .white) ?? RynatBrand.accent
                : RynatBrand.accent
        base.withAlphaComponent(isEnabled ? 1 : 0.42).setFill()
        path.fill()

        if hovered && isEnabled {
            NSColor.white.withAlphaComponent(0.16).setFill()
            NSBezierPath(roundedRect: rect.insetBy(dx: 1, dy: 1), xRadius: 12, yRadius: 12).fill()
        }

        let titleText = title as NSString
        let attributes: [NSAttributedString.Key: Any] = [
            .font: NSFont.systemFont(ofSize: 14, weight: .semibold),
            .foregroundColor: NSColor.white.withAlphaComponent(isEnabled ? 1 : 0.72),
        ]
        let size = titleText.size(withAttributes: attributes)
        titleText.draw(
            at: NSPoint(x: rect.midX - size.width / 2, y: rect.midY - size.height / 2 - 0.5),
            withAttributes: attributes
        )
    }

    override func drawFocusRingMask() {
        NSBezierPath(roundedRect: bounds.insetBy(dx: 1, dy: 1), xRadius: 13, yRadius: 13).fill()
    }
}

private final class LoginLogoImageView: NSImageView {
    init() {
        super.init(frame: .zero)
        translatesAutoresizingMaskIntoConstraints = false
        image = NSImage(named: "RYNATLogo")
        imageScaling = .scaleProportionallyUpOrDown
        setAccessibilityLabel("RYNAT")
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }
}
