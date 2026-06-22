import AppKit
import Foundation

// MARK: - Links

extension WorkspaceController {
    func previewPlan(for item: RynatFileItem) throws -> PreviewPlan {
        let server = currentServer()
        guard let location = session?.location(for: item) else {
            throw RynatCoreError.bridgeError("请选择共享内的文件或目录", code: "invalid_request")
        }
        return try core.previewPlan(serverHost: server.host, share: location.share, path: location.remotePath, kind: item.kind)
    }

    func generatedQuickLink() -> QuickLink? {
        let server = currentServer()
        let item = currentItem()
        guard let location = session?.location(for: item) else {
            return nil
        }
        do {
            return try core.generateLink(serverHost: server.host, share: location.share, path: location.remotePath, kind: item.kind)
        } catch {
            appendLog("Generate link failed: \(error.localizedDescription)")
            return nil
        }
    }

    func builtQuickLink() -> QuickLink? {
        let server = currentServer()
        let item = currentItem()
        guard let location = session?.location(for: item) else {
            return nil
        }
        do {
            return try core.buildLink(serverHost: server.host, share: location.share, path: location.remotePath, kind: item.kind)
        } catch {
            appendLog("Build link failed: \(error.localizedDescription)")
            return nil
        }
    }

    func activateExternalLink(_ rawURL: String) -> String {
        do {
            let activation = try core.activateLink(rawURL)
            let target = activation.target
            let browse = activation.browseLocation
            let preview = activation.previewPlan

            if session == nil {
                pendingActivation = activation
                showLogin()
                appendLog("Pending external link: \(rawURL)")
                return "已收到快速访问链接，请先登录服务器"
            }
            guard canCurrentSessionOpen(activation) else {
                pendingActivation = activation
                let targetHost = activation.matchedServer?.linkHost ?? target.serverHost
                setActivityMessage("此链接属于 \(targetHost)，请登录对应服务器后打开。")
                appendLog("External link target does not match current session: \(targetHost)")
                return "链接属于其他服务器"
            }
            let displayPath = Self.displayPathForRemote(share: target.share, remotePath: browse.remotePath)
            let selectedDisplayPath = browse.selectedPath.map {
                Self.displayPathForRemote(share: target.share, remotePath: $0)
            }
            navigateToDirectory(displayPath, selectPath: selectedDisplayPath)

            appendLog("Opened external link: \(rawURL), preview=\(preview?.contentType ?? "none")")
            return "已打开快速访问链接"
        } catch {
            appendLog("Activate link failed: \(rawURL), error=\(error.localizedDescription)")
            return "无法打开链接，请重试"
        }
    }

    func consumePendingActivationIfPossible() {
        guard let activation = pendingActivation, let session else {
            return
        }
        guard canCurrentSessionOpen(activation) else {
            let targetHost = activation.matchedServer?.linkHost ?? activation.target.serverHost
            setActivityMessage("已登录 \(session.server.host)，待打开链接属于 \(targetHost)。请登录对应服务器。")
            return
        }
        pendingActivation = nil
        let browse = activation.browseLocation
        let displayPath = Self.displayPathForRemote(share: activation.target.share, remotePath: browse.remotePath)
        let selectedDisplayPath = browse.selectedPath.map {
            Self.displayPathForRemote(share: activation.target.share, remotePath: $0)
        }
        navigateToDirectory(displayPath, selectPath: selectedDisplayPath)
        appendLog("Consumed pending external link: \(activation.target.serverHost)/\(activation.target.share)\(activation.target.path)")
        setActivityMessage("已打开快速访问链接")
    }

    func canCurrentSessionOpen(_ activation: LinkActivation) -> Bool {
        guard let session else {
            return false
        }
        if let matched = activation.matchedServer {
            return matched.linkHost.caseInsensitiveCompare(session.server.host) == .orderedSame
                || matched.id == session.server.id
        }
        return activation.target.serverHost.caseInsensitiveCompare(session.server.host) == .orderedSame
    }

    func previewSymbol(for contentType: String) -> String {
        switch contentType {
        case "video": return "play.rectangle"
        case "image": return "photo"
        case "pdf": return "doc.richtext"
        default: return "doc"
        }
    }

    func contentTypeLabel(_ contentType: String) -> String {
        switch contentType {
        case "video": return "视频"
        case "image": return "图片"
        case "pdf": return "PDF"
        default: return "暂不支持"
        }
    }

    static func displayPathForRemote(share: String, remotePath: String) -> String {
        let normalizedRemote = remotePath.trimmingCharacters(in: .whitespacesAndNewlines)
        let suffix = normalizedRemote == "/" || normalizedRemote.isEmpty ? "" : "/\(normalizedRemote.trimmingCharacters(in: CharacterSet(charactersIn: "/")))"
        return "/\(share)\(suffix)"
    }
}
