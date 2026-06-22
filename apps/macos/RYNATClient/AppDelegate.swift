import AppKit
import Foundation

final class AppDelegate: NSObject, NSApplicationDelegate {
    private let workspace = WorkspaceController()

    func applicationDidFinishLaunching(_ notification: Notification) {
        ThemeManager.restoreDefaultAppearance()
        installApplicationMenu()

        // 注册 rynat:// scheme 的 Apple Event 处理器。
        NSAppleEventManager.shared().setEventHandler(
            self,
            andSelector: #selector(handleGetURLEvent(_:withReplyEvent:)),
            forEventClass: AEEventClass(kInternetEventClass),
            andEventID: AEEventID(kAEGetURL)
        )

        workspace.setup()
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        false
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        workspace.showMainWindow()
        return true
    }

    @objc
    private func handleGetURLEvent(_ event: NSAppleEventDescriptor, withReplyEvent replyEvent: NSAppleEventDescriptor) {
        guard let rawURL = event.paramDescriptor(forKeyword: keyDirectObject)?.stringValue else {
            return
        }
        workspace.handleOpenURL(rawURL)
    }

    private func installApplicationMenu() {
        let mainMenu = NSMenu()
        let appItem = NSMenuItem()
        mainMenu.addItem(appItem)

        let appMenu = NSMenu(title: "RYNAT 共享网盘")
        appItem.submenu = appMenu
        appMenu.addItem(withTitle: "关于 RYNAT 共享网盘", action: #selector(NSApplication.orderFrontStandardAboutPanel(_:)), keyEquivalent: "")
        appMenu.addItem(.separator())

        let settingsItem = NSMenuItem(title: "服务器设置...", action: #selector(showServerSettings), keyEquivalent: ",")
        settingsItem.target = self
        appMenu.addItem(settingsItem)

        let logItem = NSMenuItem(title: "打开活动日志", action: #selector(openActivityLog), keyEquivalent: "")
        logItem.target = self
        appMenu.addItem(logItem)

        let diagnosticsItem = NSMenuItem(title: "诊断信息", action: #selector(showDiagnostics), keyEquivalent: "")
        diagnosticsItem.target = self
        appMenu.addItem(diagnosticsItem)

        appMenu.addItem(.separator())
        appMenu.addItem(withTitle: "隐藏 RYNAT 共享网盘", action: #selector(NSApplication.hide(_:)), keyEquivalent: "h")
        appMenu.addItem(withTitle: "退出 RYNAT 共享网盘", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")

        let editItem = NSMenuItem()
        mainMenu.addItem(editItem)
        let editMenu = NSMenu(title: "编辑")
        editItem.submenu = editMenu
        editMenu.addItem(withTitle: "撤销", action: Selector(("undo:")), keyEquivalent: "z")
        editMenu.addItem(withTitle: "重做", action: Selector(("redo:")), keyEquivalent: "Z")
        editMenu.addItem(.separator())
        editMenu.addItem(withTitle: "剪切", action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        editMenu.addItem(withTitle: "复制", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        editMenu.addItem(withTitle: "粘贴", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        editMenu.addItem(withTitle: "全选", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")

        let windowItem = NSMenuItem()
        mainMenu.addItem(windowItem)
        let windowMenu = NSMenu(title: "窗口")
        windowItem.submenu = windowMenu
        windowMenu.addItem(withTitle: "最小化", action: #selector(NSWindow.miniaturize(_:)), keyEquivalent: "m")
        windowMenu.addItem(withTitle: "缩放", action: #selector(NSWindow.performZoom(_:)), keyEquivalent: "")

        NSApp.mainMenu = mainMenu
    }

    @objc
    private func showServerSettings() {
        workspace.showServerSettingsPanel()
    }

    @objc
    private func openActivityLog() {
        workspace.openActivityLog()
    }

    @objc
    private func showDiagnostics() {
        workspace.showDiagnosticsPanel()
    }
}
