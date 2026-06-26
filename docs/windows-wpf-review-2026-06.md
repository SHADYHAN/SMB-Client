# Windows WPF 端代码审查报告

审查时间：2026-06
审查范围：`apps/windows/` 全部 WPF C#/XAML 源码（约 9800 行，.NET 8 / WPF）。
基线：Rust Core 79 测试全过、0 警告；`include/rynat_core.h` 36 个导出与 `NativeMethods.cs` 完全对齐。本机无 dotnet/Windows，仅静态审查，关键发现已由复审者亲自核实代码。未修复任何问题。

背景：Windows 端已从 WinUI 3 重写为 WPF（旧版 `apps/windows-winui-legacy` 仅作参考）。快速链接格式改为紧凑 `/s/<base64url 短码>`。本审查针对新 WPF 代码。

修复状态：🔴严重 ｜ 🟠重要 ｜ 🟡轻微

---

## 一、严重（必须修）

### 🔴 W1 切换 profile 不清密码 — 跨服务器密码泄露
[LoginViewModel.cs:23-43](../apps/windows/UI/Login/LoginViewModel.cs#L23) `SelectedProfile` setter 更新 host/username/rememberPassword/autoLogin，但**不清 Password**。PasswordBox 由 `PasswordInput_OnPasswordChanged` 单向写入 VM。

切到服务器 B 后旧密码残留，`LoginCoordinator.LoginAsync`([LoginCoordinator.cs:55-60](../apps/windows/UI/Shell/LoginCoordinator.cs#L55)) 判 `hasTypedPassword=true` → `ConnectAsync(host_B, user_B, password_A)` —— **把服务器 A 的密码发给服务器 B**。

旧 WinUI 版通过 `PasswordInputClearRequested` 事件清 PasswordBox（已修），WPF 重写丢弃了此机制。这是旧 W4 bug 的重现，且后果升级为跨服务器凭据泄露。

建议：`SelectedProfile` setter 中 `Password = string.Empty` 并同步清 PasswordBox。

### 🔴 W2 move / 目录 copy 在 replace 时先删后操作 — 失败丢数据
[RemoteCopyMoveService.cs:99-102](../apps/windows/Services/FileOperations/RemoteCopyMoveService.cs#L99) `MoveAsync` 在 `replaceExisting` 时先 `DeleteTargetIfExists` 再 `SmbRename`；[:131-134](../apps/windows/Services/FileOperations/RemoteCopyMoveService.cs#L131) `CopyDirectoryRecursive` 同样先删整个目标目录再逐文件复制。

若 rename/复制中途失败（权限/网络/锁），**目标原数据已删、源未到位，用户丢失文件**。这是「先删后写」而非原子覆盖。对照上传路径 [FileOperationService.cs:159-166](../apps/windows/Services/FileOperations/FileOperationService.cs#L159) 直接透传 Core `replace_existing`（原子处理）是对的——copy/move 走了危险实现。

建议：把 `replaceExisting` 透传给 `SmbCopyFile`/`SmbRename`（Core 已支持 `replace_existing`），由 Core 原子判定覆盖；或改「复制到临时名→删原目标→重命名」事务式保证可回退。

---

## 二、重要（应修）

### 🟠 W3 登出/退出完全不 disconnect — 连接与在途操作泄漏
[SmbSessionService.cs:86](../apps/windows/Services/Smb/SmbSessionService.cs#L86) `DisconnectAsync` 有定义且内部有调用，但 [App.xaml.cs:143](../apps/windows/App.xaml.cs#L143) `OnExit` 不调 disconnect、不关 store；UI 层**无登出流程**（无 Logout/SignOut/`_session = null`，已 grep 确认）。会话建立后永不清除，旧 SMB 连接 + tree 缓存 + 在途下载/复制悬挂到进程退出。

更彻底的是：WPF 全程**从不调用 `SmbCancelOperation`**（已确认 grep 仅 Core 内部），登出/退出时在途操作无法中止。这是旧 WinUI「登出不取消预览/传输」的重现。

建议：ShellViewModel 增加登出命令，调用 `DisconnectAsync` 前先 cancel 已知 operation_id；`App.OnExit` 同步 disconnect。服务层需维护当前会话活跃 operation_id 集合。

### 🟠 W4 无会话断线检测 — UI 永不回登录页
全 UI 层无 `_session = null`/`IsLoggedIn = false`/disconnect/reconnect 处理（已 grep 确认）。SMB 连接断开（网络掉线/服务重启）后，UI 仍显示已登录，所有操作报错但用户无法回到登录。

建议：增加会话健康检查或 error_code 驱动的回退（`reconnectable` 失败 N 次回登录页）。

### 🟠 W5 拖出下载在 UI 线程同步执行 — 阻塞 UI
[WindowsShellDragDropService.cs:36-41](../apps/windows/Platform/Shell/WindowsShellDragDropService.cs#L36) + [FileTransferService.cs:110-149](../apps/windows/Services/FileTransfers/FileTransferService.cs#L110) `DragDrop.DoDragDrop()` 是 UI 线程模态循环，Explorer 通过 OLE 回调读 `FileContents` 流，回调在 UI 线程执行 `LazyRemoteDownloadStream.EnsureInnerStream()` → `SmbCacheFile()` 同步 P/Invoke。大文件下载期间 UI 占用。旧 W6 bug 在 WPF 重写未解决。

建议：下载流读取移至后台线程（`Task.Run` 预下载或异步 IStream）。

### 🟠 W6 ShowShareRoot 绕过目录加载锁 — 竞态覆盖
[DirectoryNavigationCoordinator.cs:52-65](../apps/windows/UI/Shell/DirectoryNavigationCoordinator.cs#L52) `ShowShareRoot` 不获取 `_directoryLoadLock`、不检查 `_loadingDirectoryKey`，直接 Clear + 重置 FileList。当 `LoadAsync` 正在 await `ListAsync`（off-thread）期间，UI 线程处理 GoUp/GoShareRoot → ShowShareRoot 执行 → 随后 LoadAsync 返回覆盖 ShareRoot 视图为旧目录。旧 W5 部分重现。

建议：ShowShareRoot 也获取锁或用版本守卫。

### 🟠 W7 登录后密码框残留密码
[ShellViewModel.cs:295](../apps/windows/UI/Shell/ShellViewModel.cs#L295) `CompleteLoginAsync` 设 `Login.Password = ""`（VM 层），但 PasswordBox 无绑定回写仍显示密码点。登录后 LoginView 被 Collapsed，但密码驻留 PasswordBox 内存；会话断开后重新登录时用户看到旧密码点但 VM.Password 为空，行为不一致。

### 🟠 W8 Core 任务 API（start/poll/cancel/clear）完全未用
[NativeMethods.cs:100-110](../apps/windows/CoreAdapter/NativeMethods.cs#L100) + [RynatCoreBridge.cs:190-212](../apps/windows/CoreAdapter/RynatCoreBridge.cs#L190) 定义了全套任务 API，但全 `apps/windows`（CoreAdapter 外）**零调用**。所有文件操作走同步 P/Invoke + `Task.Run`。无法中途取消、无进度、退出无法收尾。Core 的 `thread::spawn` + 隔离连接 + TTL 清理机制白做了。

注：Core 大文件是分块的（per-chunk 20s 超时），不会因 20s 失败；tokio 多线程默认。所以非「大传输必然失败」，而是「无法取消/无进度/无优雅停机」。

### 🟠 W9 存储凭据登录复用 profile.Id 作 connection_id — 隔离退化
[SmbSessionService.cs:61,65-68](../apps/windows/Services/Smb/SmbSessionService.cs#L61) `ConnectStoredCredentialAsync` 用 `connectionId = profile.Id`，手动登录用随机 GUID。两条路径 connection_id 策略不一致：手动登录（GUID-A）→ 自动登录（profile.Id）时 GUID-A 连接不被 disconnect（见 W3）；预览/拖拽缓存按 `SafeFileName(session.ConnectionId)` 分桶，同服务器出现两套缓存。

建议：统一所有连接用随机 GUID 作 connection_id。

### 🟠 W10 预览选择无 CancellationToken — 并发下载不取消
[PreviewCoordinator.cs:43](../apps/windows/UI/Shell/PreviewCoordinator.cs#L43) `PlanAsync` 未传 token。`_previewLoadVersion` version 守卫只丢弃结果不停止工作：快速连点多个文件并发触发 `SmbCacheFile` 下载 + 缩略图解码，带宽/CPU 浪费。

建议：维护 CTS，新选择 Cancel 旧 token，传入 PlanAsync（签名已支持）。

### 🟠 W11 硬编码开发 IP 进生产
[ServerProfileService.cs:252-254](../apps/windows/Services/Profiles/ServerProfileService.cs#L252) `DisplayNameFor` 回退里 `host.Equals("192.168.102.136") ? "共享网盘" : host`，测试环境 IP 残留。任何该 IP 服务器被强制命名「共享网盘」。同样残留于 [LoginViewModel.cs:13](../apps/windows/UI/Login/LoginViewModel.cs#L13) `_serverHost = "192.168.102.136"`、macOS fallback、Core `DEFAULT_SERVER_HOST` 常量。

建议：删除特殊分支，回退用 host。

---

## 三、轻微/优化

| 项 | 位置 | 说明 |
|---|---|---|
| W12 目录加载超时不取消 Core 在途操作 | DirectoryService.cs:41-50 | 超时抛异常但不调 SmbCancelOperation，靠 Core 自身 20s 兜底，有竞态 |
| W13 文件名比较用 CurrentCultureIgnoreCase | RemoteCopyMoveService.cs:259 等 5 处 | 区域敏感（土耳其语 I），应 OrdinalIgnoreCase |
| W14 NormalizeRemotePath 5 份重复且 TrimEnd 不一致 | DirectoryService/RemoteCopyMoveService 等 | 抽 RemotePath 共享工具类 |
| W15 链接归一化是旧 query 格式残留 | LinkActivationService.cs:88-92 | `rynat://s/?` 替换对新 path 式无副作用，死逻辑，易误导 |
| W16 EncryptCredential/DecryptCredential 死代码 | NativeMethods.cs:52-57 | Core 内部自动加解密，WPF 无需手动 |
| W17 空文件预览缓存永不命中 | PreviewService.cs:303 | `Length > 0` 使 0 字节文件永远重下，应 `>=` |
| W18 启动期 sync-over-async + fire-and-forget | App.xaml.cs:42,98,107 | GetAwaiter().GetResult() 有死锁风险；fire-and-forget 未观察异常 |
| W19 Unwrap 对 bool 结果要求 Data 非空 | RynatCoreBridge.cs:251-262 | Core 漏 data 字段会误判失败，契约脆弱 |
| W20 目录加载单 SemaphoreSlim 串行全局 | DirectoryNavigationCoordinator.cs:19 | 一个慢目录阻塞全局导航 20s |
| W21 多架构声明与 cargo 单架构构建不匹配 | csproj:11,36 | x86/ARM64 配置下 cargo 仍按宿主构建，运行时 DllNotFound |
| W22 导航树刷新丢孙子节点展开 | NavigationTreeViewModel.cs:57-73 | ReplaceChildren 重建子节点为未展开 |
| W23 ToggleNavigationNodeAsync 死代码 | ShellViewModel.cs:183-196 | 无调用方 |
| W24 右键菜单 CanExecute 不随 CommandManager 刷新 | RelayCommand/AsyncRelayCommand | 不调 InvalidateRequerySuggested，启用态可能过时 |
| W25 ApplyFilter 逐项重建 ObservableCollection | FileListViewModel.cs:239-259 | 大目录搜索卡顿，应 CollectionView 过滤 |
| W26 预览 ReferenceEquals 守卫刷新后失效 | PreviewCoordinator.cs:44,51 | 刷新重建 FileItemViewModel，旧引用失效致结果被丢弃，应按路径比较 |
| W27 LocalLinkRedirectService 端口占用静默失败 | LocalLinkRedirectService.cs:34-43 | 无日志无降级，浏览器链接唤醒失效 |
| W28 单实例转发无 ACK | WindowsSingleInstanceService.cs:107-135 | 主实例是否处理成功未知 |
| W29 协议注册无卸载/反注册 | WindowsProtocolRegistrationService.cs | 卸载后注册表指向无效 exe |
| W30 AcceptLoop 异常紧密自旋 | LocalLinkRedirectService.cs:71-96 | 持续失败时 CPU 空转，应加退避 |
| W31 Stop 不等待 listenerTask | WindowsSingleInstanceService.cs:52-57 | Dispose 时可能对象已释放访问 |
| W32 跨共享 Cut+Paste 服务层失败而非协调器拦截 | RemoteClipboardCoordinator.cs:117-133 | 错误中途抛出，已移动部分项无法回滚 |
| W33 粘贴冲突检测基于内存快照 | RemoteClipboardCoordinator.cs:103-107 | 查 _allItems 而非实时列举，可能漏判 |
| W34 async void 事件处理器 | NavigationTreeView/FileListView 多处 | AsyncRelayCommand.Execute 无顶层 try-catch |
| W35 视频播放状态分散 View/VM 两处 | PreviewPaneView.xaml.cs:9 + PreviewPaneViewModel.cs:110-123 | _isVideoPlaying 字段与 IsVideoPlaying 属性双源易不一致 |
| W36 RememberPassword 切 profile 可能被误开 | LoginViewModel.cs:37 | `\|\| RememberPassword` 静默重开用户已关选项 |
| W37 UserFacingError 字符串匹配脆弱 | ShellViewModel.cs:746-753 | Contains("auth"/"password") 依赖消息文本 |
| W38 View 通过 VisualTree 回溯 ShellViewModel | FileListView.xaml.cs:367-381 | 绕过命令绑定，应改命令/消息机制 |
| W39 重复 helper | FormatSize/IsSameRemoteTarget/IsNestedDirectoryTarget 等 3+ 处重复 | 抽共享 helper |
| W40 IPv6 解析边角 | LinkActivationService.cs:101-112 | LastIndexOf(':') 对裸 IPv6 误析端口 |
| W41 FileContents 流异常未释放 | WindowsShellDragDropService.cs:62-92 | 中途抛异常 content 未 Dispose |
| W42 FFI 内存管理正确 | RynatCoreBridge.cs:214-249 | Call/Unwrap try/finally 配对，null 处理到位，PtrToStringUTF8 已用 |

---

## 四、旧 WinUI 已知 bug 核查

| 旧 bug | WPF 状态 | 说明 |
|---|---|---|
| OpenStore 从未调用（数据全丢） | ✅ 已修复 | BootstrapService.cs:21 启动即 OpenStore |
| 登出不取消预览/传输 | ❌ 重蹈（W3） | 无登出流程，OnExit 不 disconnect，从不 CancelOperation |
| 密码腐蚀（切 profile 串密码） | ❌ 重蹈（W1） | SelectedProfile setter 不清 Password，跨服务器泄露 |
| 目录卡空 | ✅ 未复发 | SemaphoreSlim + _loadingDirectoryKey 串行化（代价是 W20 全局串行） |
| 先传后判 replace 数据丢失 | ❌ 部分重蹈（W2） | 上传路径正确透传 Core；copy/move 先删后操作 |
| remember_password=false 不调 delete | ✅ 已修复 | ServerProfileService.cs:60-69 显式 DeleteServerCredential |
| 拖拽下载阻塞 UI（W6） | ❌ 重蹈（W5） | LazyRemoteDownloadStream 在 DoDragDrop 期间同步下载 |
| 侧边栏展开竞态（W5） | ❌ 部分重蹈（W6） | ShowShareRoot 绕过锁致覆盖 |

---

## 五、结论与优先级

WPF 端结构清晰（CoreAdapter/Domain/Services/Platform/UI 分层），FFI 内存管理正确（W42），OpenStore 持久化、目录加载串行化、remember_password 处理已修复。但有 **2 个 🔴 严重问题**和 **9 个 🟠 重要问题**，其中 3 项是旧 WinUI 已知 bug 的重现（W1 密码腐蚀、W3 登出不取消、W2 先删后操作），说明重写时未吸取旧版教训。

**最优先（严重/安全）**：
1. **W1 切 profile 清密码** — 跨服务器密码泄露，一行 setter 修复。
2. **W2 copy/move replace 透传 Core** — 数据丢失风险。
3. **W3 登出/退出 disconnect + cancel** — 资源泄漏 + 在途操作悬挂。

**重要（功能/体验）**：
4. W4 会话断线回登录页 ｜ W5 拖出下载异步化 ｜ W6 ShowShareRoot 加锁 ｜ W7 登录后清密码框 ｜ W8 接通任务 API ｜ W9 统一 connection_id ｜ W10 预览取消 ｜ W11 删硬编码 IP。

**FFI 层评价**：RynatCoreBridge 内存管理正确（Call/Unwrap try/finally 配对，PtrToStringUTF8 已用），NativeMethods 覆盖 Core 全部 36 导出无遗漏。主要问题是任务 API 死代码（W8）和 Unwrap 契约脆弱（W19）。

**建议修复顺序**：W1（一行）→ W2（透传 Core）→ W3（登出闭环）→ W4/W7（会话/密码）→ W5/W6（异步/竞态）→ W8-W11（任务/连接/预览/IP）→ W12-W41 质量收尾。

无法在本机编译验证（无 dotnet/Windows），建议在 Windows 环境补 `dotnet build` 确认无编译错误后再动手修。
