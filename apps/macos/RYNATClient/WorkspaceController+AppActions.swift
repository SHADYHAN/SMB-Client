import AppKit
import Foundation

// MARK: - App Actions

extension WorkspaceController {
    /// 由 AppDelegate 的 URL 事件处理器与重开回调调用。
    func handleOpenURL(_ rawURL: String) {
        let summary = activateExternalLink(rawURL)
        showMainWindow()
        setActivityMessage(summary)
        appendLog(summary)
    }

    func openActivityLog() {
        do {
            try FileManager.default.createDirectory(at: logURL.deletingLastPathComponent(), withIntermediateDirectories: true)
            if !FileManager.default.fileExists(atPath: logURL.path) {
                try "".write(to: logURL, atomically: true, encoding: .utf8)
            }
            NSWorkspace.shared.open(logURL)
        } catch {
            appendLog("Open activity log failed: \(error.localizedDescription)")
            setStatus("打开日志失败")
        }
    }

    func showDiagnosticsPanel() {
        showDiagnostics()
    }

    func showServerSettingsPanel() {
        presentServerSettings()
    }
}
