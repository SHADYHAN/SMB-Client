import AppKit

enum ThemeManager {
    private static let key = "rynat.themeMode"

    /// 固定使用系统默认外观；清理旧版本保存过的手动主题。
    static func restoreDefaultAppearance() {
        UserDefaults.standard.removeObject(forKey: key)
        NSApp.appearance = nil
    }
}
