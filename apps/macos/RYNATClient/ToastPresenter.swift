import AppKit

/// 自绘极简 toast：窗口顶部下滑，2 秒自动消失。
final class ToastPresenter {
    static let shared = ToastPresenter()

    private weak var currentToast: NSView?
    private weak var attachedWindow: NSWindow?

    func show(message: String, in window: NSWindow) {
        DispatchQueue.main.async { [weak self] in
            self?.present(message: message, in: window)
        }
    }

    private func present(message: String, in window: NSWindow) {
        guard let contentView = window.contentView else {
            return
        }
        // 切换窗口时清理旧 toast
        if attachedWindow !== window {
            currentToast?.removeFromSuperview()
            currentToast = nil
        }
        attachedWindow = window

        currentToast?.removeFromSuperview()

        let toast = NSView()
        toast.wantsLayer = true
        toast.layer?.backgroundColor = NSColor.labelColor.withAlphaComponent(0.9).cgColor
        toast.layer?.cornerRadius = 8
        toast.layer?.cornerCurve = .continuous

        let label = NSTextField(labelWithString: message)
        label.font = .systemFont(ofSize: 12, weight: .medium)
        label.textColor = NSColor.textBackgroundColor
        label.alignment = .center
        label.lineBreakMode = .byTruncatingTail
        label.translatesAutoresizingMaskIntoConstraints = false

        toast.addSubview(label)
        toast.translatesAutoresizingMaskIntoConstraints = false
        contentView.addSubview(toast)

        NSLayoutConstraint.activate([
            label.leadingAnchor.constraint(equalTo: toast.leadingAnchor, constant: 14),
            label.trailingAnchor.constraint(equalTo: toast.trailingAnchor, constant: -14),
            label.topAnchor.constraint(equalTo: toast.topAnchor, constant: 7),
            label.bottomAnchor.constraint(equalTo: toast.bottomAnchor, constant: -7),
            toast.topAnchor.constraint(equalTo: contentView.topAnchor, constant: 8),
            toast.centerXAnchor.constraint(equalTo: contentView.centerXAnchor),
        ])

        currentToast = toast
        toast.alphaValue = 0
        NSAnimationContext.runAnimationGroup { ctx in
            ctx.duration = 0.18
            toast.animator().alphaValue = 1
        }

        DispatchQueue.main.asyncAfter(deadline: .now() + 2.0) { [weak toast] in
            guard let toast, toast === self.currentToast else { return }
            NSAnimationContext.runAnimationGroup({ ctx in
                ctx.duration = 0.2
                toast.animator().alphaValue = 0
            }, completionHandler: {
                toast.removeFromSuperview()
                if toast === self.currentToast {
                    self.currentToast = nil
                }
            })
        }
    }
}
