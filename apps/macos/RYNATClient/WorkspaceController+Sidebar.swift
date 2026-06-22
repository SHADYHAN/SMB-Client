import AppKit
import Foundation

// MARK: - Sidebar

extension WorkspaceController {
    func controlTextDidChange(_ obj: Notification) {
        applySearch()
    }

    func sidebarView(_ view: SidebarView, didSelect tab: RynatSidebarTab) {
        activeSidebarTab = tab
        refreshSidebar()
    }

    func sidebarView(_ view: SidebarView, didSelectPath path: String) {
        let normalized = session?.normalizedDisplayPath(path) ?? path
        if normalized == session?.currentPath {
            return
        }
        if let item = session?.findItem(path: path), !item.isDirectory {
            navigateToDirectory(parentPath(for: item.path), selectPath: item.path)
        } else {
            navigateToDirectory(normalized)
        }
    }

    func sidebarView(_ view: SidebarView, didSelectFavorite link: QuickLink) {
        openFavorite(link)
    }

    func sidebarView(_ view: SidebarView, didRequestExpandPath path: String) {
        loadSidebarDirectory(path)
    }

    func sidebarViewDidRequestAddFavorite(_ view: SidebarView) {
        addCurrentFavorite()
    }

    func sidebarView(_ view: SidebarView, didRemoveFavorite id: String) {
        do {
            try core.deleteQuickLink(id: id)
        } catch {
            appendLog("Delete favorite failed: \(error.localizedDescription)")
        }
        session?.removeFavorite(id: id)
        refreshSidebar()
        setStatus("已移除收藏")
    }

    @objc
    func userMenuChanged(_ sender: NSPopUpButton) {
        defer {
            sender.selectItem(at: 0)
        }
        if sender.indexOfSelectedItem == 1 {
            presentServerSettings()
        } else if sender.indexOfSelectedItem == 2 {
            sessionGeneration += 1
            cancelPreviewOperations()
            clearWriteQueueForLogout()
            directoryLoader.clear()
            directoryLoadGeneration += 1
            try? core.smbDisconnect(connectionID: session?.connectionID)
            session = nil
            selectedItem = nil
            visibleItems = []
            showLogin()
        }
    }

    func presentServerSettings() {
        guard let action = ServerSettingsDialog.request(bootstrap: bootstrapState) else {
            return
        }

        do {
            let state: AppBootstrapState
            switch action {
            case .save(let draft):
                let existingProfile = draft.profileID.flatMap { id in
                    bootstrapState?.serverProfiles.first(where: { $0.id == id })
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
                if id == session?.server.id {
                    setStatus("请先退出登录")
                    setActivityMessage("当前正在使用该服务器\n请退出登录后再移除")
                    return
                }
                state = try core.deleteServerProfile(id: id)
            }
            bootstrapState = state
            loginController?.updateBootstrap(state)
            setStatus("服务器设置已保存")
            if session != nil {
                setActivityMessage("服务器设置已保存\n下次登录生效")
            }
        } catch {
            appendLog("Save server settings failed: \(error.localizedDescription)")
            setStatus("保存失败")
        }
    }

    func currentStoredServerProfile() -> StoredServerProfile? {
        if let active = bootstrapState?.activeServer {
            return active
        }
        if let serverID = session?.server.id,
           let profile = bootstrapState?.serverProfiles.first(where: { $0.id == serverID }) {
            return profile
        }
        return bootstrapState?.serverProfiles.first
    }

    @objc
    func showDiagnostics() {
        do {
            let diagnostics = try core.smbDiagnostics(connectionID: session?.connectionID)
            let copyMethod: String
            switch diagnostics.lastCopyMethod {
            case .serverSide:
                copyMethod = "NAS 内部复制"
            case .streamedFallback:
                copyMethod = "流式复制"
            case .none:
                copyMethod = "暂无"
            }
            let message = """
            连接状态：\(diagnostics.connected ? "已连接" : "未连接")
            服务器：\(diagnostics.host ?? "-")
            已缓存共享：\(diagnostics.cachedShareCount)
            最近复制：\(copyMethod)
            详细原因：\(diagnostics.lastCopyFallbackReason ?? "-")

            日志：\(logURL.path)
            """
            let alert = NSAlert()
            alert.messageText = "诊断信息"
            alert.informativeText = message
            alert.addButton(withTitle: "确定")
            alert.runModal()
        } catch {
            setStatus("诊断失败")
        }
    }
}
