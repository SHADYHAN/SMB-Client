# Windows 端代码审查报告

审查时间：2026-06-20
审查范围：`apps/windows/` 全部 C#/XAML 源码（约 1.6 万行，WinUI 3 / .NET 8 / MVVM）。
基线：Windows 端为完整实现（非占位），结构为 RynatCoreBridge(FFI) + AppServices(业务) + UI/Main(MVVM) + PlatformIntegration。本机无 dotnet/Windows，仅静态审查，未编译验证。关键发现已由复审者亲自核实代码。未修复任何问题。

修复状态：🔴严重 ｜ 🟠重要 ｜ 🟡轻微

---

## 一、严重（必须修）

### 🔴 W1. OpenStore 从未被调用 → 数据完全不持久化
- 位置：全项目无调用方；[RynatCoreBridge.cs:50、333](../apps/windows/RynatCoreBridge.cs#L50) 仅声明 P/Invoke 绑定，无业务代码调用；[App.xaml.cs](../apps/windows/App.xaml.cs) 启动流程只构造 WindowsAppContext；[AppBootstrapService.cs:23](../apps/windows/AppServices/Bootstrap/AppBootstrapService.cs#L23) 直接调 `AppBootstrap()` 未先 OpenStore。
- 问题：Core 的 `app_store()`（bridge.rs）在未 `open_store` 时回退 `CoreStore::in_memory()`。因此 **SaveServerProfile / SaveServerCredential / 收藏链接全部只存内存，应用每次重启后所有服务器、记住的密码、收藏全部丢失**。自动登录（依赖 `ActiveCredential.AutoLogin`）跨重启永远拿不到，形同虚设。
- 对照：macOS 端 [WorkspaceController+Layout.swift:104](../apps/macos/RYNATClient/WorkspaceController+Layout.swift#L104) 调 `core.openStore(path:)` 指向 rynat.sqlite。Windows 缺这一步。
- 建议：App 启动构造 WindowsAppContext 后、LoadBootstrapAsync 前，调 `Services.CoreBridge.OpenStore(new OpenStoreRequest(Path.Combine(LocalApplicationData,"Rynat","rynat.sqlite")))`，失败降级提示。

### 🔴 W2. MainShellViewModel 3099 行上帝对象
- 位置：[MainShellViewModel.cs](../apps/windows/UI/Main/MainShellViewModel.cs) 全文 3099 行。
- 问题：单类承担 bootstrap、profile 管理、会话、目录浏览、侧边栏树、文件操作（复制/剪切/粘贴/删除/重命名/上传/下载/拖拽）、链接激活、收藏、预览、任务进度、剪贴板、搜索、窗口可见性等全部职责。macOS 侧等价逻辑拆成 9 个 extension。当前结构任何改动都有高回归风险，并发边界（哪个字段跨线程）无法一眼看清。
- 建议：按 macOS 拆法切分；至少把侧边栏树、文件操作、预览协调各抽成独立 partial/service。

---

## 二、重要（应修）

### 🟠 W3. 登出不取消预览 / 拖出准备 / 文件传输任务
- 位置：[MainShellViewModel.cs:2690](../apps/windows/UI/Main/MainShellViewModel.cs#L2690) `ResetSessionState` 只清 UI 状态 + `ReplaceSession(null)`（disconnect），不调 `_previewEntryService.CancelActivePreview()`、`_fileDragDownloadPreparationService?.CancelActive()`、`_fileTaskService.CancelAll()`。
- 问题：登出后会话已 disconnect，但进行中的预览缓存、拖拽预热、文件下载/删除/上传任务仍持有旧 session 引用继续跑直到 FFI 失败；任务进度条仍显示、`_tasks` 残留、`CancelActiveTask` 仍可点。切到新服务器后任务栏挂着旧服务器失败任务。正是 macOS 已修掉的"登出不取消预览/传输"类 bug（对应 Core U2/U4）。
- 注：`SelectedDirectoryItem=null` 可能间接触发预览取消，但传输任务（W4）与拖拽预热明确未取消。
- 建议：ResetSessionState 开头调 CancelActivePreview + CancelActive + CancelAll，再 disconnect。

### 🟠 W4. 切换服务器配置不清理已输密码 — 密码腐蚀
- 位置：[MainShellViewModel.cs](../apps/windows/UI/Main/MainShellViewModel.cs) `ApplySelectedProfileState`（:618 调用点）只重置 `ManualUsername`，**不清 `_manualPassword` 也不清 PasswordBox**。
- 问题：用户为服务器 A 输入密码后，在设置里把活动服务器切到 B，密码仍保留；点登录走 ConnectWithCredentialsAsync 用 B 的 host + A 的密码连接 → 要么认证失败令用户困惑，要么（同密码）把 A 的密码发往 B。macOS LoginViewController.fillServer 切换服务器时重置密码字段。
- 注：登出路径经 IsWorkspaceVisible 变化触发 ClearPasswordInput 清密码（正确），唯独切换 profile 漏了。登录失败也不清（:1253）。
- 建议：ApplySelectedProfileState 清空 _manualPassword 并触发 PasswordBox 清空。

### 🟠 W5. 侧边栏展开加载无 generation/取消守卫 — 竞态致树错乱
- 位置：[MainShellViewModel.cs:819-849](../apps/windows/UI/Main/MainShellViewModel.cs#L819) `ToggleSidebarItemExpansionAsync`。
- 问题：`itemIndex = SidebarItems.IndexOf(item)` 在 `await LoadSidebarDirectoryAsync` 前捕获，await 后用陈旧 itemIndex 调 InsertSidebarChildren。期间用户可折叠：折叠分支同步执行，把路径从 _expandedSidebarPaths 移除并 SetExpansion(false)；随后挂起的展开 resume 执行 SetExpansion(true)+InsertSidebarChildren 但不 re-add 路径 → UI 显示展开+有子节点但 _expandedSidebarPaths 不含该路径，下次 RebuildSidebarItems 子节点消失；陈旧 itemIndex 还可能把子节点插到错位置 → 树结构损坏。macOS loadSidebarDirectory 用 generation+token 守卫并 cancel 旧 load；Windows 主目录 OpenPathAsync 有 _directoryRequestVersion 守卫，唯独侧边栏展开没有。
- 建议：侧边栏展开引入 generation/token，await 后校验 item 仍在列表且索引未变、路径仍在 _expandedSidebarPaths，否则放弃；对挂起 load 提供取消。

### 🟠 W6. 拖拽下载在 UI 线程同步阻塞 GetAwaiter().GetResult()
- 位置：[MainShellViewModel.cs:1512-1558](../apps/windows/UI/Main/MainShellViewModel.cs#L1512) `PrepareItemsForDragDownload`，GetResult() 在 :1541；调用点 [MainWindow.xaml.cs:819](../apps/windows/MainWindow.xaml.cs#L819) DirectoryItemsListView_DragItemsStarting。
- 问题：DragItemsStarting 事件同步调 PrepareItemsForDragDownload，内部 `.GetAwaiter().GetResult()` 在 UI 线程阻塞直到下载完成。选中项虽被 PrepareDragDownloadForSelectionAsync 预热，但对目录和 >64MB 文件跳过预热（FileDragDownloadPreparationService.cs:327）——拖拽目录或大文件时在 drag-start 期间整段冻结 UI。macOS 用异步回调准备拖拽不阻塞。
- 建议：改为 async Task，DragItemsStarting 里 await；或未就绪则提示稍后再拖。

### 🟠 W7. Core 任务 API（start/poll/cancel/clear）在 Windows 端是死代码
- 位置：[RynatCoreBridge.cs:191-213](../apps/windows/RynatCoreBridge.cs#L191)（声明）、SmbTaskStatus 模型 :711-732。
- 问题：SmbStartTask/SmbPollTask/SmbCancelTask/SmbClearTask 及 SmbTaskStartResult/SmbTaskStatus 在 Windows 端无任何调用方。Windows 改用同步 API + Task.Run + operation_id/SmbCancelOperation 实现取消。Core 侧为多连接隔离任务做的整套后台线程机制（thread::spawn、isolated connection、TTL 清理）在 Windows 完全未用。
- 隐患：SmbTaskStatus.Data 是 JsonElement?，作为 POCO 属性长期持有在 System.Text.Json 下不安全（缓冲区归还池后访问可能抛异常/读脏）。当前因未用是潜在隐患。
- 注：回答关键关切——**Windows 不轮询 Core 任务，WindowsFileTaskService 无 polling**，它是 UI 任务跟踪器通过 TaskChanged 事件 + DispatcherQueue.TryEnqueue 回 UI 线程，不阻塞 UI。
- 建议：要么移除死代码，要么启用前把 Data 改 JsonDocument+Clone。

---

## 三、轻微/优化

| 项 | 位置 | 说明 |
|---|---|---|
| W8 ServerProfileManagementService remember_password=false 仍发密码 | ServerProfileManagementService.cs:156-164 | 无论 rememberPassword 都调 SaveServerCredential(password)；Core 会删除凭据（结果正确、无占位符腐蚀），但明文密码无谓经 FFI 传输。SmbSessionService 一致性已用 DeleteServerCredential，两处不一致 |
| W9 PtrToUtf8 逐字节读取 O(n²) | RynatCoreBridge.cs:286-297 | `while ReadByte!=0` 对大 JSON 响应（数千项目录）性能差；改 Marshal.PtrToStringUTF8 |
| W10 缓存复用仅按大小判断 | PreviewEntryService.cs:426、FileDownloadService.cs:356 | `fileInfo.Length == item.SizeBytes` 判断复用，远端文件替换为同尺寸不同内容时误命中陈旧缓存；cache_key 不含内容哈希/mtime |
| W11 显式清空缓存跳过 .part | WindowsCacheManagementService.cs:274-295 | ClearAll 也跳过 .part（保护进行中下载），下载异常中断的 .part 永久残留，"清空全部"名不副实 |
| W12 缓存淘汰口径不一致 | WindowsCacheManagementService.cs:111 vs PreviewEntryService.cs:483 | 一个按 LastWriteTimeUtc 一个按 LastAccessTimeUtc |
| W13 目录加载无主动取消 | DirectoryBrowserService.cs:18-94 | LoadAsync 未传 operation_id、无 SmbCancelOperation，快速导航旧调用不被取消只丢弃结果，大目录浪费带宽 |
| W14 重连重试不响应取消 | DirectoryBrowserService.cs:96-115 | 重连与第二次 ListDirectory 之间无 token 检查，取消后仍完成重连+重试 |
| W15 FileBatchOperationService 子串匹配错误码 | FileBatchOperationService.cs:328/333/355/360 | `ErrorCode?.Contains("cancelled"/"skipped")` 子串匹配，脆弱，新增含该子串的码会误触发 |
| W16 UpdateCredentialOptions 返回 null 提示不准 | ServerProfileManagementService.cs:206-209 | remember_password=false 时 Core 返回 null（已删凭据），但提示"未找到可更新凭据" |
| W17 SmbDiagnostics/EncryptCredential/DecryptCredential 死代码 | RynatCoreBridge.cs:95-105/179-183 | Windows 无调用方；凭据加解密 Core 内部自动完成，不需要显式 encrypt/decrypt |
| W18 任务进度事件高频全量快照 | MainShellViewModel.cs:2775-2801 | 每次 TaskChanged 都 TryEnqueue + GetLatestActiveTaskSnapshot 锁内拷贝全部任务+排序，批量下载每项 ReportProgress 触发，高频进度下 UI 线程被淹没 |
| W19 侧边栏目录加载无 in-flight 守卫 | MainShellViewModel.cs:2276-2300 | LoadSidebarDirectoryAsync 只判 HasCached 不判是否在途，快速双击展开同节点发起两次 Load |
| W20 中转服务器每请求调 AppBootstrap() | WindowsLocalRedirectServer.cs:299-320 | IsAllowedTarget 每次校验都 FFI AppBootstrap 取全量 profile 比对 host，本机进程可高频打端口触发 FFI 风暴；建议缓存 profile host 集合 |
| W21 单实例转发异常不回 ACK | WindowsSingleInstanceManager.cs:153-159 | DispatchCommandAsync 先 await handler 再 SendAck，handler 抛则不回 ACK，调用方 2s 超时后"开独立窗口"，而链接激活可能已 fire-and-forget → 重复处理 |
| W22 启动期 forwardTask.Wait(3s) UI 线程同步等 | App.xaml.cs:74 | 单实例转发，3s 超时；超时后 fire-and-forget 可能未观察异常 |
| W23 MainWindow_Closed 不取消后端预览 | MainWindow.xaml.cs:139-149 | 只清 UI + renderVersion 让在途渲染 bail，未调 CancelActivePreview，后端 FFI 预览继续；主窗关闭即退进程影响有限 |
| W24 同步读盘加载 UI 偏好 | MainWindow.xaml.cs:1902-1947 | LoadWorkspaceUiState/Save 用 File.ReadAllText/WriteAllText 在 UI 线程同步 I/O，慢盘卡顿 |
| W25 死代码/重复 | MainShellViewModel.cs:206-210 PreviewPaneWidth/PreviewToggleGlyph 死属性；FormatBytes 在 VM 与 DirectoryItemViewModel 重复；CanOpenSelectedItem==CanDownloadSelectedItem；IsAlreadyExistsError/IsNotFoundError/AppendRemotePath/BuildDisplayPath 在 FileWriteService 与 FileFolderUploadService 重复 | 清理 |
| W26 每次属性变更新建 Brush | MainShellViewModel.cs:660-666、MainWindow.xaml.cs:1737-1751 | ShareSidebarTabForeground 等 new SolidColorBrush，建议静态缓存 |
| W27 多选右键状态机脆弱 | MainWindow.xaml.cs:878-1015 | 绕开 WinUI Extended 选择右键塌缩 quirk 的状态机，逻辑正确但脆弱 |
| W28 深路径侧边栏祖先未加载选择丢失 | MainShellViewModel.cs:2151-2191 | EnsureSidebarAncestorsVisible 祖先未缓存时直接 return 不加载，经链接打开深层路径时侧边栏祖先不展开、选中丢失；macOS 按需加载祖先 |
| W29 async void 事件处理器无顶层异常兜底 | MainWindow.xaml.cs 27 个 async void | 多数 VM 方法有 try/finally 无 catch-all，非 bridge 异常变未处理；建议注册 TaskScheduler.UnobservedTaskException |
| W30 DragItemsStarting 全量吞异常 | MainWindow.xaml.cs:839-842 | catch { e.Cancel=true } 静默取消拖拽，用户不知为何拖不出 |

---

## 四、与 macOS 端对等性核查

| 已修 bug | Windows 侧 | 说明 |
|---|---|---|
| 窗口长条（fittingSize） | ✅ 不存在 | Windows 用固定尺寸 ConfigureWindowSize，无 fittingSize 缩窗 |
| 目录加载竞态（generation） | ⚠️ 主目录有守卫，侧边栏无 | OpenPathAsync 有 _directoryRequestVersion；侧边栏展开无（W5） |
| 登出取消预览 | ❌ 仍存在 | ResetSessionState 不取消（W3） |
| 登出取消传输 | ❌ 仍存在 | macOS 经 TransferCoordinator 登出取消；Windows 不取消（W3/W4） |
| 密码腐蚀（占位符写回） | ✅ 已规避 | Core save_server_credential/update_server_credential_options 在 remember_password=false 时删除而非写占位符；但切换 profile 不清密码（W4） |
| appendLog 并发 | ✅ 不存在 | Windows 走线程安全 _diagnostics 服务 |
| 双轨加载 | ⚠️ | 主目录 + 侧边栏两套加载，侧边栏无 in-flight 守卫（W19） |
| 先传后判 replace | ✅ 已规避 | FileWriteService/FileFolderUploadService 先查冲突决策再传 |
| 中转服务器 recv 超时 | ✅ 已规避 | WindowsLocalRedirectServer 有 ReceiveTimeout/SendTimeout=2000 |

---

## 五、结论与优先级

Windows 端是与 macOS 对等的完整实现，结构清晰（FFI/AppServices/UI/PlatformIntegration 分层），多数 macOS 已修的 bug 在 Windows 侧已规避（窗口长条、目录主加载竞态、先传后判、中转超时、密码占位符腐蚀）。但有 **2 个 🔴 阻塞问题**和 **5 个 🟠 重要问题**需优先处理：

**最优先（阻塞性）**：
1. **W1 OpenStore 从未调用** — 数据完全不持久化，所有服务器/密码/收藏重启丢失，自动登录失效。这是最严重的，一行调用缺失导致全部保存功能失效。
2. **W2 MainShellViewModel 3099 行** — 上帝对象，维护性极差，是后续所有修复的风险放大器。

**重要（功能/安全）**：
3. **W3 登出不取消预览/传输** — 资源泄漏 + 任务栏混乱。
4. **W4 切换 profile 不清密码** — 密码腐蚀/错发。
5. **W5 侧边栏展开竞态** — 树结构损坏。
6. **W6 拖拽下载阻塞 UI** — 大文件/目录拖拽冻结。
7. **W7 Core 任务 API 死代码** — 多连接任务机制未用（功能上用同步 API 替代，可接受，但应清理或启用）。

**FFI 层评价**：RynatCoreBridge 内存管理正确（Call/Unwrap try/finally 配对 FreeHGlobal/rynat_free_string，null 处理到位），NativeMethods 覆盖 rynat_core.h 全部 34 个导出无遗漏。BridgeExceptionClassifier 信任 Core error_code 不重新分类，与 Core 一致。链接唤醒三态与 macOS 一致。FFI 层质量良好，主要问题是 PtrToUtf8 性能（W9）和死代码（W7/W17）。

**建议修复顺序**：W1（一行调用，立即修）→ W3/W4（安全对等）→ W5/W6（竞态/阻塞）→ W2 拆分（配合 W18/W19 收敛并发边界）→ W8-W30 质量收尾。

无法在本机编译验证（无 dotnet/Windows），建议在 Windows 环境补一次 `dotnet build` 确认无编译错误后再动手修。

---

# 修复记录（2026-06-20）

本轮修复了 W1/W3/W4/W5/W6/W8/W9 共 7 项。Rust Core 未改动（78 测试全过），仅改 Windows C#。本机无 .NET Windows SDK，未 dotnet build 验证，已做括号平衡 + 引用可见性静态核查。

| 项 | 状态 | 修复内容 |
|---|---|---|
| W1 OpenStore 未调用 | ✅已修 | [AppBootstrapService.cs](../apps/windows/AppServices/Bootstrap/AppBootstrapService.cs) LoadAsync 在 AppBootstrap 之前调 `OpenStore(rynat.sqlite)`（路径 `%LocalAppData%\Rynat\rynat.sqlite`），失败降级内存模式不阻断启动。解决重启后服务器/密码/收藏全丢、自动登录失效。 |
| W3 登出不取消后台任务 | ✅已修 | [MainShellViewModel.cs](../apps/windows/UI/Main/MainShellViewModel.cs) ResetSessionState 开头加 `CancelActivePreview` + `_fileDragDownloadPreparationService?.CancelActive()` + `_fileTaskService.CancelAll()` + 清 `_activeTaskId`。登出后不再有旧 session 预览/拖拽预热/传输任务残留。 |
| W4 切换 profile 密码腐蚀 | ✅已修 | VM 新增 `PasswordInputClearRequested` 事件；ApplySelectedProfileState 切换 profile 时清 `_manualPassword` 并触发事件（首次设置 `_manualPassword` 为空时跳过，不误触发）；[MainWindow.xaml.cs](../apps/windows/MainWindow.xaml.cs) 订阅事件调 ClearPasswordInput，构造订阅/Closed 解绑。不再把 A 服务器密码误用于 B。 |
| W5 侧边栏展开竞态 | ✅已修 | ToggleSidebarItemExpansionAsync 在 `await LoadSidebarDirectoryAsync` 后校验：路径仍在 `_expandedSidebarPaths`、item 仍在列表且索引未变，否则放弃插入或 RebuildSidebarItems。避免折叠/展开交错致树结构损坏。 |
| W6 拖拽下载阻塞 UI | ✅已修 | `PrepareItemsForDragDownload`（同步）→ `PrepareItemsForDragDownloadAsync`（async Task）；DragItemsStarting 里 `await`。不再 `GetAwaiter().GetResult()` 同步阻塞 UI 线程，大文件/目录拖拽不再冻结界面。 |
| W8 凭据传输不一致 | ✅已修 | [ServerProfileManagementService.cs](../apps/windows/AppServices/Smb/ServerProfileManagementService.cs) SaveCredentialAsync 在 `remember_password=false` 时改调 `DeleteServerCredential`，不发明文密码经 FFI（与 SmbSessionService 一致）。 |
| W9 PtrToUtf8 O(n²) | ✅已修 | [RynatCoreBridge.cs](../apps/windows/RynatCoreBridge.cs) PtrToUtf8 改用 `Marshal.PtrToStringUTF8`，消除逐字节 ReadByte 扫描，大 JSON 响应更快。 |

## 未修复（后续）

- **W2 MainShellViewModel 3099 行拆分** — 结构重构，风险大且非 bug，建议单独专项（配合 W18/W19 收敛并发边界）。
- **W7 Core 任务 API 死代码** — 功能上已用同步 API + Task.Run 替代，清理或启用需决策；JsonElement 生命周期隐患随启用再处理。
- **W10-W30** — 缓存陈旧命中（W10）、.part 残留（W11）、缓存淘汰口径（W12）、目录加载无主动取消（W13/W14）、子串匹配错误码（W15）、死代码（W17/W25）、任务进度高频快照（W18）、侧边栏无 in-flight 守卫（W19）、中转服务器每请求 AppBootstrap（W20）、单实例 ACK 耦合（W21）、同步读盘 UI 偏好（W24）等 🟡 优化项，非功能问题，可后续收尾。

## 待验证

本机无 dotnet/Windows SDK，无法 `dotnet build` 验证。已做静态核查：
- Rust Core 78 测试全过（未改动）。
- C# 改动文件括号平衡（AppBootstrapService / ServerProfileManagementService / RynatCoreBridge / MainShellViewModel / MainWindow.xaml.cs 均 `{`==`}`）。
- 引用可见性核查：OpenStoreRequest、DeleteServerCredentialRequest 可见；ServerProfileManagementResult.Credential 为 `StoredServerCredential?` 允许 null。

**建议推送后在 Windows 虚拟机跑 `dotnet build apps/windows/Rynat.WindowsClient.csproj`**，把编译输出贴回，确认 7 处改动无编译错误并解 WinUI 3 首次编译的 SDK/资源问题。


---

# 修复记录（2026-06-20）

本轮修复了 W1/W3/W4/W5/W6/W8/W9 共 7 项。Rust Core 未改动（78 测试全过），仅改 Windows C#。本机无 .NET Windows SDK，未 dotnet build 验证，已做括号平衡 + 引用可见性静态核查。

| 项 | 状态 | 修复内容 |
|---|---|---|
| W1 OpenStore 未调用 | ✅已修 | [AppBootstrapService.cs](../apps/windows/AppServices/Bootstrap/AppBootstrapService.cs) LoadAsync 在 AppBootstrap 之前调 `OpenStore(rynat.sqlite)`（路径 `%LocalAppData%\Rynat\rynat.sqlite`），失败降级内存模式不阻断启动。解决重启后服务器/密码/收藏全丢、自动登录失效。 |
| W3 登出不取消后台任务 | ✅已修 | [MainShellViewModel.cs](../apps/windows/UI/Main/MainShellViewModel.cs) ResetSessionState 开头加 `CancelActivePreview` + `_fileDragDownloadPreparationService?.CancelActive()` + `_fileTaskService.CancelAll()` + 清 `_activeTaskId`。登出后不再有旧 session 预览/拖拽预热/传输任务残留。 |
| W4 切换 profile 密码腐蚀 | ✅已修 | VM 新增 `PasswordInputClearRequested` 事件；ApplySelectedProfileState 切换 profile 时清 `_manualPassword` 并触发事件（首次设置 `_manualPassword` 为空时跳过，不误触发）；[MainWindow.xaml.cs](../apps/windows/MainWindow.xaml.cs) 订阅事件调 ClearPasswordInput，构造订阅/Closed 解绑。不再把 A 服务器密码误用于 B。 |
| W5 侧边栏展开竞态 | ✅已修 | ToggleSidebarItemExpansionAsync 在 `await LoadSidebarDirectoryAsync` 后校验：路径仍在 `_expandedSidebarPaths`、item 仍在列表且索引未变，否则放弃插入或 RebuildSidebarItems。避免折叠/展开交错致树结构损坏。 |
| W6 拖拽下载阻塞 UI | ✅已修 | `PrepareItemsForDragDownload`（同步）→ `PrepareItemsForDragDownloadAsync`（async Task）；DragItemsStarting 里 `await`。不再 `GetAwaiter().GetResult()` 同步阻塞 UI 线程，大文件/目录拖拽不再冻结界面。 |
| W8 凭据传输不一致 | ✅已修 | [ServerProfileManagementService.cs](../apps/windows/AppServices/Smb/ServerProfileManagementService.cs) SaveCredentialAsync 在 `remember_password=false` 时改调 `DeleteServerCredential`，不发明文密码经 FFI（与 SmbSessionService 一致）。 |
| W9 PtrToUtf8 O(n²) | ✅已修 | [RynatCoreBridge.cs](../apps/windows/RynatCoreBridge.cs) PtrToUtf8 改用 `Marshal.PtrToStringUTF8`，消除逐字节 ReadByte 扫描，大 JSON 响应更快。 |

## 未修复（后续）

- **W2 MainShellViewModel 3099 行拆分** — 结构重构，风险大且非 bug，建议单独专项（配合 W18/W19 收敛并发边界）。
- **W7 Core 任务 API 死代码** — 功能上已用同步 API + Task.Run 替代，清理或启用需决策；JsonElement 生命周期隐患随启用再处理。
- **W10-W30** — 缓存陈旧命中（W10）、.part 残留（W11）、缓存淘汰口径（W12）、目录加载无主动取消（W13/W14）、子串匹配错误码（W15）、死代码（W17/W25）、任务进度高频快照（W18）、侧边栏无 in-flight 守卫（W19）、中转服务器每请求 AppBootstrap（W20）、单实例 ACK 耦合（W21）、同步读盘 UI 偏好（W24）等 🟡 优化项，非功能问题，可后续收尾。

## 待验证

本机无 dotnet/Windows SDK，无法 `dotnet build` 验证。已做静态核查：
- Rust Core 78 测试全过（未改动）。
- C# 改动文件括号平衡（AppBootstrapService / ServerProfileManagementService / RynatCoreBridge / MainShellViewModel / MainWindow.xaml.cs 均 `{`==`}`）。
- 引用可见性核查：OpenStoreRequest、DeleteServerCredentialRequest 可见；ServerProfileManagementResult.Credential 为 `StoredServerCredential?` 允许 null。

**建议推送后在 Windows 虚拟机跑 `dotnet build apps/windows/Rynat.WindowsClient.csproj`**，把编译输出贴回，确认 7 处改动无编译错误并解 WinUI 3 首次编译的 SDK/资源问题。

