import AppKit
import Foundation

final class WorkspaceContentHostView: NSView {
    override var intrinsicContentSize: NSSize {
        NSSize(width: NSView.noIntrinsicMetric, height: NSView.noIntrinsicMetric)
    }

    override var fittingSize: NSSize {
        bounds.size
    }

    override func layout() {
        super.layout()
        subviews.forEach { $0.frame = bounds }
    }

    override func setFrameSize(_ newSize: NSSize) {
        super.setFrameSize(newSize)
        subviews.forEach { $0.frame = bounds }
    }
}

final class WorkspaceController: NSObject,
    NSWindowDelegate,
    NSSplitViewDelegate,
    NSSearchFieldDelegate,
    LoginViewControllerDelegate,
    SidebarViewDelegate
{
    var window: NSWindow?
    var loginController: LoginViewController?

    let redirectServer = LocalRedirectServer()
    let core = RynatCore()
    let previewService = MacPreviewService()

    var session: RynatWorkspaceSession?
    var bootstrapState: AppBootstrapState?
    var visibleItems: [RynatFileItem] = []
    var selectedItem: RynatFileItem?
    var activeSidebarTab: RynatSidebarTab = .shares
    var fileClipboard: FileClipboard?
    var pendingActivation: LinkActivation?
    let directoryLoader = DirectoryLoader()
    var directoryLoadGeneration = 0
    var sessionGeneration = 0
    var didConfigureControls = false
    var hasInstalledContentView = false
    var previewTitleWidthConstraint: NSLayoutConstraint?
    var searchWidthConstraint: NSLayoutConstraint?
    var uploadProgressWidthConstraint: NSLayoutConstraint?
    weak var mainSplitView: NSSplitView?
    weak var previewToggleButton: RynatButton?
    var inspectorPane: NSView?
    weak var browserRootView: NSView?
    weak var browserBodyView: NSView?
    var isPreviewVisible = true
    var conflictApplyAllDecision: ConflictDecision?
    var sidebarPaneWidth: CGFloat = SidebarView.defaultWidth
    var previewPaneWidth: CGFloat = 340
    let fileWorkspaceMinimumWidth: CGFloat = 520
    let previewPaneMinimumWidth: CGFloat = 280
    let previewPaneMaximumWidth: CGFloat = 460
    var isApplyingWorkspaceSplitLayout = false
    var hasAppliedWorkspaceSplitLayout = false
    var isWindowLiveResizing = false
    let sidebarWidthDefaultsKey = "workspace.sidebarWidth.v2"
    let previewWidthDefaultsKey = "workspace.previewWidth.v2"

    let sidebarView = SidebarView()
    lazy var smbGateway = SmbGateway(
        status: { [weak self] message in
            self?.setStatus(message)
        },
        isSessionActive: { [weak self] context in
            self?.isCurrentSession(context) ?? false
        }
    )
    lazy var previewCoordinator = PreviewCoordinator { [weak self] operationID in
        try? self?.core.smbCancelOperation(operationID)
    }
    lazy var transferCoordinator = TransferCoordinator(
        hooks: TransferCoordinator.UIHooks(
            setQueued: { [weak self] count in
                self?.setStatus("已加入队列 \(count) 项")
                self?.setActivityMessage("已加入队列，稍后自动执行")
            },
            started: { [weak self] task in
                self?.conflictApplyAllDecision = nil
                self?.showTask(task)
            },
            progressed: { [weak self] task, completed, total in
                self?.updateTask(task, completed: completed, total: total)
            },
            completed: { [weak self] task, state, message in
                self?.finishTask(task, state: state)
                self?.setActivityMessage(message)
            },
            failed: { [weak self] task, error in
                self?.finishTask(task, state: .failed)
                self?.appendLog("Operation failed: \(error.localizedDescription)")
                self?.setActivityMessage("操作失败，请重试")
            },
            requestRefresh: { [weak self] context in
                guard let self, self.isCurrentSession(context) else {
                    return
                }
                self.reloadCurrentRemoteDirectory(keepSelection: false)
                self.refreshSidebar()
            },
            cancelOperation: { [weak self] operationID in
                do {
                    try self?.core.smbCancelOperation(operationID)
                } catch {
                    self?.appendLog("Cancel operation failed: \(error.localizedDescription)")
                }
            },
            log: { [weak self] message in
                self?.appendLog(message)
            }
        )
    )
    let fileTable = RynatFileTableView()
    let fileListController = FileListController()
    var listScrollView: NSScrollView?
    let searchField = NSSearchField()
    let breadcrumbField = NSTextField(labelWithString: "")
    let fileSummaryField = NSTextField(labelWithString: "")
    let generatedLinkField = NSTextField(labelWithString: "")
    let previewImageView = NSImageView()
    let previewTitleField = NSTextField(labelWithString: "")
    let previewMetaField = NSTextField(labelWithString: "")
    let fileKindField = NSTextField(labelWithString: "")
    let filePathField = NSTextField(labelWithString: "")
    let fileSizeField = NSTextField(labelWithString: "")
    let fileModifiedField = NSTextField(labelWithString: "")
    let statusField = NSTextField(labelWithString: "未连接")
    let statusPathField = NSTextField(labelWithString: "")
    let playButton = RynatButton(title: "播放", symbolName: "play.fill", style: .primary)
    let openButton = RynatButton(title: "打开", symbolName: "arrow.up.right.square", style: .secondary)
    let copyLinkButton = RynatButton(title: "复制链接", symbolName: "link", style: .subtle)
    let uploadProgress = NSProgressIndicator()
    let cancelTaskButton = RynatButton(title: "取消", symbolName: "xmark", style: .ghost)

    let logURL = FileManager.default
        .homeDirectoryForCurrentUser
        .appendingPathComponent("Library/Logs/RYNATClient.log")
    let logQueue = DispatchQueue(label: "com.rynat.shared-disk.log-writer")

    /// 由 AppDelegate 在启动时调用：打开 store、建窗、启动本地中转服务。
    func setup() {
        bootstrapState = loadBootstrapState()
        buildWindow()
        redirectServer.start(
            onLog: { [weak self] message in
                self?.appendLog(message)
            },
            onOpenLink: { [weak self] rawLink in
                guard let self else {
                    return
                }
                let summary = self.activateExternalLink(rawLink)
                self.showMainWindow()
                self.setActivityMessage(summary)
                self.appendLog(summary)
            }
        )
        appendLog("App launched")

        if bootstrapState?.activeCredential?.autoLogin == true, let profile = bootstrapState?.activeServer {
            attemptAutoLogin(profile: profile)
        }
    }

    /// 自动登录：用已存凭据直连默认服务器，成功则跳过登录页进主窗口。
    func attemptAutoLogin(profile: StoredServerProfile) {
        let profileID = profile.id
        let connectionID = profileID
        setStatus("正在自动登录...")
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            let backgroundCore = RynatCore()
            do {
                let result = try backgroundCore.smbConnectStoredCredential(
                    serverProfileID: profileID,
                    connectionID: connectionID
                )
                let matched = RynatServerProfile(
                    id: profile.id,
                    connectionID: result.connectionID,
                    name: profile.displayName,
                    host: result.host,
                    protocolLabel: result.dialectLabel,
                    accountName: profile.username ?? "",
                    rememberPassword: true,
                    autoLogin: true,
                    shares: result.shares.map { RynatShare(name: $0.name, comment: $0.comment) }
                )
                DispatchQueue.main.async {
                    self?.finishLogin(with: matched)
                }
            } catch {
                DispatchQueue.main.async {
                    self?.appendLog("Auto login failed: \(error.localizedDescription)")
                    self?.showLogin()
                }
            }
        }
    }

    /// 登录成功后的统一入口：建 session、切主窗口、消费待处理外部链接。
    /// 供自动登录与 LoginViewController 回调共用。
    func finishLogin(with server: RynatServerProfile) {
        bootstrapState = try? core.appBootstrap()
        let rootItems = shareRootItems(for: server)
        directoryLoader.clear()
        directoryLoadGeneration += 1
        session = RynatWorkspaceSession(server: server, rootItems: rootItems)
        refreshFavoritesFromStore()
        selectedItem = session?.currentDirectoryItems.first
        visibleItems = session?.currentDirectoryItems ?? []
        showBrowser()
        setActivityMessage("已连接 \(server.host)\n协议：\(server.protocolLabel)\n用户：\(server.accountName)\n共享：\(server.shares.count) 个")
        setStatus("已连接 \(server.host)，发现 \(server.shares.count) 个共享")
        consumePendingActivationIfPossible()
    }


}
