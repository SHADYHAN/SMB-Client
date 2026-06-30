# Windows Client Architecture Direction

> 2026-06-30 archive note: This document records the old WinUI/WPF file-workbench phase and the reasoning that led to Explorer-first. The old WinUI, WPF, and Tauri Windows main-program lines have been removed from the active code tree. The active Windows direction is `apps/windows-tray`; see `docs/explorer-first-windows-client.md`.

## 背景

早期 Windows 端已经有过完整 WinUI 3 实现，但旧 UI / app 层更像是在 WinUI 3 上手工模拟资源管理器。实际体验中已经暴露出拖拽影子、拖出桌面临时路径、同名覆盖、目录树交互、文件列表列宽、选择状态等一系列问题。

这些问题不只是单个 bug，也不完全是 WinUI 3 本身的问题。根因是 Windows 端 app 层没有建立清晰产品边界：RYNAT 不需要复刻 Explorer，它应是围绕分享链接、预览缩略和上传下载的文件工作台。凡是为了模拟完整资源管理器而引入的复杂 Shell 行为、重缓存策略或多阶段文件操作，都要重新评估稳定性和性能成本。

本文档最初用于确定 Windows WPF 文件工作台方向。该阶段已完成内部功能验证，当前决策是不再继续把 WPF 文件工作台作为主产品体验打磨，而是进入 Explorer-first Windows Client Direction。

## 总体结论

当前推荐方向：

```text
Rust Core 保留
Windows 主体验切到 Explorer-first
RYNAT 负责登录、链接、唤醒、托盘和 Shell 集成
Windows Explorer 负责文件浏览、预览、复制、移动、删除和系统右键
.NET 10 WinForms Tray Host + WebView2 Local UI 成为 Explorer-first 主程序技术栈
```

不建议继续把旧 WinUI 3 版本作为长期成品基础。它的历史问题和经验只保留在文档结论中，不应继续堆补丁到最终版本。

旧 WPF 线已从代码树移除。它的阶段性价值是验证了三件核心事：

1. 复制和打开分享链接。
2. 通过缩略图和预览快速判断文件内容。
3. 稳定上传、下载和处理常见文件进出流程。

目录树、文件列表、多选、右键、拖拽、搜索、状态栏和预览面板等自研文件管理器能力不再继续作为主战场。后续 Windows 投入转到 Explorer-first：让系统文件管理器承担这些复杂交互。

## 历史目标架构

```text
Rust Core
  SMB 连接与读写
  链接生成 / 解析 / 激活
  服务器配置与凭据
  预览计划
  任务状态
  统一错误码

Windows Core Adapter
  C# FFI bridge
  DTO 映射
  错误码映射
  Rust core 调用封装

Windows App Services / Use Cases
  登录 / 登出 / 自动登录
  服务器管理
  目录浏览
  文件上传 / 下载
  文件删除 / 重命名 / 新建文件夹
  可选：远端复制 / 移动 / 粘贴
  同名冲突处理
  收藏 / 快捷链接
  预览加载
  任务调度

Windows Platform Adapters
  剪贴板
  文件选择器 / 保存对话框
  可选：Shell 拖拽
  可选：Explorer 打开 / 定位
  缩略图 / 视频首帧 / 系统图标
  协议注册 / HTTP 中转 / 单实例
  通知 / 进度集成

Windows UI State
  当前服务器
  当前目录
  选中项
  目录树展开状态
  文件列表状态
  预览面板状态
  任务状态
  错误提示状态

Native Windows UI
  Login
  Server Settings
  Toolbar
  Navigation Tree
  File List
  Preview Panel
  Status Bar
  Dialogs
```

核心原则：

- UI 不直接做 SMB / 文件业务。
- 文件业务不依赖具体 UI 控件。
- Shell 能力独立封装，不混进主窗口代码。
- 目录树、文件列表、预览、任务、服务器设置各有独立状态和控制器。
- Rust core 继续作为双平台共享业务底座。
- Windows 端按 Windows 用户习惯实现，但不以复刻 Explorer 为产品目标。
- 优先保证链接、预览缩略、上传下载三条主链路稳定。
- 复杂文件管理能力必须能被关闭、降级或移除，不应拖累核心链路。

## 技术路线建议

### 已归档：WPF 文件工作台

WPF 曾是替代旧 WinUI 3 的主线选择，原因是它比旧 WinUI 代码更适合快速建立稳定的桌面文件工作台。但在内部验证后，当前决策是移除 WPF 主程序线，而不是继续投入 UI / Explorer 复刻精修。

原因：

- WPF 对传统桌面业务工具和文件列表类应用更成熟。
- TreeView、ListView/GridView、ContextMenu、Command、DataTemplate、DragDrop 行为更稳定、更可控。
- 更适合做目录浏览、文件表格、右键菜单、多选、键盘操作和焦点状态。
- 与 Win32/Shell/COM 互操作资料更多，适合在必要时接入 Shell 能力。
- 默认视觉较旧，但可以通过自定义样式做到现代化，不等于 UI 老。

WPF 代码不再保留为可构建后备客户端。其阶段性结论只保留在文档中：自研文件工作台能验证链接、预览和传输链路，但不应继续作为 Windows 主程序路线。

### WinUI 3 的定位

WinUI 3 可以做现代 Windows UI，但对本项目不是最优主线。

适合 WinUI 3 的场景：

- 普通业务表单。
- 设置页。
- 轻量信息浏览。
- 非重度 Shell 文件交互。

旧路线的问题集中在：

- 把产品目标误判为完整资源管理器。
- 文件列表表格列宽与多选被过度放大为 Explorer 复刻问题。
- 拖入 / 拖出承担过多系统级期望。
- 桌面同名覆盖。
- 系统右键菜单。
- 缩略图与文件预览。
- 系统级文件体验。

这些地方继续修可以改善局部，但很难变成稳定的资源管理器体验。WPF 阶段曾回到 RYNAT 自身价值：链接、预览缩略、上传下载；当前主线进一步把文件管理交给 Explorer。

### 当前主线：Explorer-first / Shell 集成

用户更需要系统文件管理器体验，因此当前主线改为 Explorer-first：RYNAT 负责登录、右键复制链接和链接唤醒，文件浏览和复制移动交给 Windows Explorer。

该方向已单独记录在 `docs/explorer-first-windows-client.md`，后续不应和 WPF 文件工作台路线混在一起推进。

2026-06-30 技术栈更新：Explorer-first 主程序从 Tauri 评估线切换为 `.NET 10 WinForms Tray Host + WebView2 Local UI`。Tauri 线已从代码树移除，不再作为当前主产品路线。

### 不作为当前路线：Cloud Files API

如果后续目标升级为类似 OneDrive / 企业网盘的系统级体验，可以考虑 Cloud Files API。

适合 Cloud Files API 的能力：

- Explorer 中显示网盘入口。
- 占位文件。
- 按需下载。
- 系统级图标、状态、同步标识。
- 文件打开时自动拉取。
- 更自然的拖拽和复制体验。

不建议当前路线直接上 Cloud Files API。它更像独立的系统集成工程，复杂度比普通客户端高很多，也超出“链接、预览缩略、上传下载”的核心目标。

## 当前 Windows 代码的定位（2026-06-24）

旧 `apps/windows` WPF 客户端和旧 WinUI 3 客户端均已从代码树移除，不再是 Windows 主体验。WinUI / WPF 的业务流程和问题样本只保留在文档记录中，不应继续作为最终客户端基础。

旧 WPF 线曾经具备：

- 登录 / 自动登录入口。
- 服务器设置、新增/删除/默认服务器选择。
- SMB 连接与共享目录加载。
- 目录树单击进入、双击展开 / 收起并进入。
- 文件列表、刷新、新建文件夹、重命名、删除。
- 多选远端复制 / 移动 / 粘贴，以及同名覆盖确认的 WPF app 层基础流程；该能力已随 WPF 线移除，不再作为当前 Windows 主线能力。
- 软件内远端拖拽到目录目标移动 / 复制，跨共享或按 Ctrl 时走复制；若实机稳定性或性能不达标，可降级或移除。
- 本地文件拖入上传，并在同名时确认是否覆盖。
- 文件拖出到本地的虚拟文件下载基础能力；该能力属于高风险 Shell 交互，后续只做稳定性修复，不继续追求 Explorer 级完整体验。
- 图片 / 小视频预览基础能力，右侧预览面板可关闭；大视频不再自动整文件缓存。
- 生成固定 HTTP 分享链接并复制到剪贴板；Windows 侧默认只复制 HTTP，避免钉钉文档改写 `rynat://` href。
- `rynat://` 协议注册、本地 HTTP 中转、单实例转发、前台唤醒的链接链路。
- 短链接 `/s/<code>`，以及本地已激活后的关闭页响应，尽量自动关闭浏览器标签页。
- Windows 一键拉取、构建、启动脚本。

旧 WPF 线当时的阶段重点：

1. 验证文件工作台是否能覆盖链接、预览和传输核心链路。
2. 保持复制 HTTP 分享链接、协议注册、本地中转、单实例唤醒这些可复用链路不退化。
3. 只修阻断构建、登录凭据安全、链接唤醒等关键问题。
4. 不再继续投入 WPF 文件列表、预览面板、拖拽模拟和完整 Explorer 体验。
5. Explorer-first 开始后，只复用其设计结论；具体代码不再作为 active fallback 保留。

旧 WinUI 3 阶段的文档结论建议定位为：

- 功能验证参考。
- Rust bridge / DTO 参考。
- 业务流程参考。
- UI 交互问题反例和测试样本。

不建议长期继承：

- `MainWindow.xaml.cs` 超大窗口类。
- `MainShellViewModel` 超大 ViewModel。
- 用 `ListView` 扁平模拟目录树的方式。
- 用 Grid 手工模拟文件表格列宽的方式。
- 直接用 WinUI `DataPackage + StorageItems + 本地缓存文件` 实现 Explorer 级拖出体验。
- 服务层直接依赖 `DirectoryItemViewModel` 的结构。

可以保留 / 迁移：

- `RynatCoreBridge.cs` 的 FFI 调用思路。
- Rust core DTO 映射。
- 链接生成 / 激活业务规则。
- 服务器配置和凭据流程。
- 文件操作 use case 的业务顺序。
- 预览缓存策略中的可复用部分。
- 本地 HTTP 中转和单实例思路，但实现应进入 platform adapter。

旧 WPF 线曾经把本地 HTTP 中转、协议注册、单实例转发放入 `Platform/Activation`，Explorer-first 复用这些能力时应继续保持这个边界。

## 旧文件工作台模块拆分记录

### 1. Core Adapter

建议命名：

- `RynatCoreBridge`
- `RynatCoreModels`
- `RynatCoreRequests`
- `RynatCoreErrors`

职责：

- P/Invoke 到 `rynat_core.dll`。
- JSON 序列化 / 反序列化。
- 释放 Rust 返回字符串。
- 把 Rust error_code 映射为 Windows app 错误。

要求：

- 不依赖具体 UI 控件。
- 不依赖窗口。
- 可单元测试。

### 2. Domain Models

建议建立平台无关但属于 Windows app 层的模型：

- `ServerSession`
- `RemoteFileItem`
- `RemoteDirectory`
- `FileSelection`
- `FileOperationRequest`
- `FileOperationResult`
- `PreviewState`
- `TaskState`
- `QuickLinkItem`

要求：

- 不使用 UI 控件类型。
- 不把 Brush / Visibility / Font / DataTemplate 放进 domain model。
- UI 显示字段可由 ViewModel 计算。

### 3. App Services / Use Cases

建议拆分：

- `LoginService`
- `ServerProfileService`
- `DirectoryService`
- `FileOperationService`
- `QuickLinkService`
- `PreviewService`
- `TaskService`
- `CacheService`

每个 service 只负责业务流程，不直接打开窗口或弹 UI 对话框。

### 4. Platform Adapters

建议拆分：

- `WindowsShellDragDropService`（可选）
- `WindowsClipboardService`
- `WindowsFileDialogService`
- `WindowsExplorerService`（可选）
- `WindowsThumbnailService`
- `WindowsMediaService`
- `WindowsProtocolRegistrationService`
- `WindowsSingleInstanceService`
- `WindowsLocalRedirectService`

其中拖出桌面是高风险边界，不是当前路线的核心能力。

如果保留应用级拖拽，必须明确能力边界：

- 可拖出文件。
- 文件名尽量保留原名。
- 允许同名时走 Explorer 提示。
- 仍可能暴露本地缓存路径。

当前路线不追求真正 Explorer 级体验。若应用级拖拽影响稳定性或性能，应优先降级为明确的“下载到...”按钮 / 菜单能力，而不是继续加深 Shell 集成。

### 5. UI State / ViewModels

建议按区域拆 ViewModel：

- `ShellViewModel`
  - 当前页面、登录状态、全局命令。
- `LoginViewModel`
  - 登录表单和服务器选择。
- `ServerSettingsViewModel`
  - 服务器列表和编辑状态。
- `NavigationTreeViewModel`
  - 目录树、展开、选中、加载。
- `FileListViewModel`
  - 当前目录文件、多选、排序、搜索。
- `PreviewPaneViewModel`
  - 预览标题、缩略图、播放、复制链接。
- `TaskStatusViewModel`
  - 任务进度、取消、完成状态。

禁止重新出现一个 3000 行 ViewModel。

### 6. 旧 WPF UI Views

建议拆 View：

- `MainWindow`
- `LoginView`
- `ServerSettingsDialog`
- `ToolbarView`
- `NavigationTreeView`
- `FileListView`
- `PreviewPaneView`
- `StatusBarView`
- `ConflictDialog`

主窗口只负责布局组合，不承载文件业务。

## 核心体验标准

旧 Windows 文件工作台阶段不再以复刻资源管理器为目标，而以 RYNAT 文件工作台为目标。该阶段的核心体验围绕链接、预览缩略、上传下载三条链路建立。当前 Explorer-first 主线则把目录浏览和文件操作交给 Windows Explorer。

### 链接

- 文件 / 文件夹必须能一键复制分享链接。
- 默认复制 HTTP 链接，确保钉钉文档 / 聊天框可点击。
- 链接唤醒应支持冷启动 / 热启动。
- 未登录时先引导登录，登录成功后继续打开目标位置。
- 打开失败要给出明确、短句提示。
- 浏览器中转页无法自动关闭时，可保留浏览器扩展作为可选方案，但不作为核心功能依赖。

### 预览缩略

- 列表和预览区应帮助用户快速判断文件内容。
- 图片优先使用轻量缩略图，不自动缓存超大原图。
- 视频优先显示 poster / 首帧，用户点击后再播放。
- 快速切换文件时取消旧预览任务。
- 预览失败不影响目录浏览和链接复制。
- 缓存必须有大小限制和清理策略。

### 上传下载

- 本地拖入上传必须稳定。
- 下载到本地必须有明确入口、状态和失败提示。
- 长任务应接入任务 API，支持进度、取消和失败恢复提示。
- 同名冲突必须让用户确认，不静默覆盖。
- 大文件不应阻塞 UI。

### 目录浏览

目录树和文件列表服务于三条核心链路，不追求完整 Explorer 行为。

### 目录树

- 单击：选中并进入目录。
- 双击：进入并展开 / 收起目录。
- 展开方向：向下展开。
- 展开后不跳动。
- 当前访问目录必须有稳定选中态。
- 加载中状态要轻量明确。

### 文件列表

- 默认列表模式优先。
- 多选行为保持 Windows 用户可理解即可，不追求覆盖所有 Explorer 细节。
- 空白区域点击清除选择。
- 进入目录后不应强制选中第一项，除非产品明确需要。
- 列宽可自动适配，必要时用户可调整。
- 右键菜单围绕核心能力组织：复制链接、预览 / 打开、上传 / 下载、删除 / 重命名。系统级右键菜单不属于当前路线。

### 拖拽

- 拖入软件：从本地上传，是核心能力的一部分。
- 拖出软件：属于下载易用性增强，应以稳定为准。
- 软件内部拖拽：属于远端移动 / 复制增强，不是核心能力；若影响稳定和维护，可移除。
- 同名文件：应由用户确认覆盖 / 跳过，不静默失败。
- 拖出不应让用户感知复杂临时缓存逻辑；若第一阶段无法避免，应优先降级为显式下载入口。

### 预览

- 右侧预览可关闭。
- 有缩略图时优先显示缩略图。
- 视频显示首帧，用户点击播放时调用系统播放器或内置播放。
- 预览加载不应阻塞目录操作。
- 登出 / 切换服务器必须取消预览任务。

### 错误提示

- 用户提示只说成功 / 失败和简短原因。
- 详细错误写日志。
- 认证失败要明确提示账号或密码错误。
- 网络 / 服务器不可达提示“连接失败”即可，不暴露复杂内部原因。

## 推荐实施路线

### 阶段一：确定 WPF 骨架（历史完成，代码已移除）

完成内容：

1. 新建 WPF app。
2. 接入 Rust core bridge。
3. 建立应用服务和 platform adapter。
4. 实现登录页。
5. 实现服务器配置读取和设置窗口。
6. 实现主窗口三栏布局：目录树、文件列表、预览。

### 阶段二：文件工作台基础（历史完成，代码已移除）

已完成：

1. 目录树加载、展开、收起。
2. 文件列表选择、进入目录、刷新。
3. 搜索、状态栏、预览面板。
4. 基础右键菜单和复制链接。

当时计划继续打磨：

1. 文件列表扫描效率、列宽、排序和细节状态。
2. 大目录下的加载、取消和缓存策略。
3. 围绕复制链接、预览、上传下载优化右键和快捷键。
4. 不再追求覆盖完整 Explorer 键盘和多选细节。

### 阶段三：链接与预览缩略（历史完成，代码已移除）

已完成：

1. 生成链接 / 复制链接。
2. 链接唤醒定位目录 / 文件。
3. 图片 / 视频基础预览和视频播放。
4. 本地短链接已激活后的关闭页响应。

当时计划继续打磨：

1. 视频 poster / 首帧和大文件预览性能；图片预览已使用轻量缩略图，视频预览已接入 Windows Shell poster。
2. 链接在钉钉、飞书、浏览器、客户端冷启动 / 热启动下的持续实测。
3. 预览区视觉精修：固定舞台、居中缩放、标题 / meta / 操作区稳定，不因加载状态跳动。
4. 收藏 / 取消收藏 UI 只在确实能提升链接复用效率时补齐。
5. 可选浏览器扩展方案：Chrome / Edge MV3 扩展监听 `http://127.0.0.1:19527/s/*` 和 `http://localhost:19527/s/*`，命中后延迟约 500-800ms 调用 `chrome.tabs.remove(tabId)` 关闭中转标签页。该方案只适用于安装了扩展的浏览器，无法覆盖钉钉内置 WebView；默认复制链接仍保持 HTTP，避免破坏钉钉文档 / 聊天可点击性。

### 阶段四：上传下载（历史完成，代码已移除）

已完成：

1. 上传。
2. 下载基础能力和显式“下载到...”入口。
3. 本地拖入上传同名确认。
4. 拖出下载基础能力；软件内远端拖拽移动 / 复制已从主 UI 降级。

当时待完成：

1. 长任务取消和进度体验统一；当前已有上传 / 下载项级进度提示，字节级进度需 core task status 暴露进度字段后再做。
2. 上传 / 下载失败重试提示。
3. 同名冲突确认文案和流程统一。
4. 删除 / 重命名 / 新建文件夹保留为基础管理能力，但不应压过上传下载链路。

当时建议降级 / 移除：

1. 远端复制 / 移动 / 粘贴：已从主 UI 入口移除，服务层可后续删除或保留 dormant。
2. 软件内远端拖拽移动 / 复制：已从文件列表和目录树 drop 路径禁用。
3. 拖出下载的复杂虚拟文件行为。

这些能力如果在真实 Windows 环境中影响稳定、性能或维护成本，应退回到更简单的显式菜单 / 按钮流程。

### 阶段五：视觉精修与稳定化（已停止作为主线）

本阶段是 WPF 文件工作台时期的精修计划。Explorer-first 决策后，不再继续把 WPF 视觉精修作为 Windows 主产品路线；以下内容仅保留为历史记录。

已完成：

1. WPF 主题资源和基础控件样式。
2. DPI 感知、ClearType、像素对齐。
3. Fluent 图标资源集中管理。

原计划待完成，但当前不作为主线继续投入：

1. 统一间距阶：4 / 8 / 12 / 16 / 24 / 32。
2. 统一圆角规则：按钮 / 输入框 / 行 / 面板 / 登录卡。
3. 统一字号阶：caption、body、section、title。
4. 文件列表 hover / selected / pressed 状态精修。
5. 预览区舞台和操作区精修。
6. 登录页、服务器设置、顶部栏和状态栏节奏统一。
7. 必要的轻过渡动画，避免状态瞬切。

### 阶段六：Explorer-first 主线（当前方向）

WPF 文件工作台完成内部验证并从代码树移除后，Explorer-first 成为当前 Windows 主线。

评估项：

- 是否登录后直接打开 Windows Explorer。
- 是否在 MVP 内注册 Explorer 右键复制分享链接。
- 是否以单当前服务器模型降低复杂度。
- 主程序采用 `.NET 10 WinForms Tray Host + WebView2 Local UI`。
- 是否保持单一 Windows 主程序路线，避免 WPF / Tauri / WinUI 多线并行。

该方向见 `docs/explorer-first-windows-client.md`。

### 不进入当前路线：Cloud Files API

仅当产品需要系统级网盘体验时进入。

评估项：

- 是否需要 Explorer 里直接出现 RYNAT 网盘。
- 是否需要占位文件。
- 是否需要按需下载。
- 是否需要系统同步状态标记。
- 是否需要系统级文件打开和复制体验。

## 旧 WinUI 经验迁移建议

可复用的历史经验：

1. `RynatCoreBridge.cs` 的 FFI 思路。
2. Server profile / credential DTO。
3. Link DTO 和激活逻辑。
4. Directory browsing 的路径规则。
5. File operation 的业务顺序。
6. Preview cache 的基本策略。

不应复活的旧实现方向：

1. `MainWindow.xaml`。
2. `MainWindow.xaml.cs`。
3. `MainShellViewModel.cs`。
4. 旧 WinUI ListView 目录树模拟逻辑。
5. 旧 WinUI DataPackage 拖出逻辑。
6. 任何直接依赖 WinUI 控件的 service 入参。

## 不建议做的事

- 不建议继续在旧 WinUI 主窗口上修视觉和拖拽细节作为长期方案。
- 不建议换 Electron / Tauri / Avalonia / MAUI 来复刻 Windows 文件管理器体验。
- Explorer-first 轻壳采用 .NET WinForms 托盘宿主 + WebView2 本地 UI；Explorer 右键入口仍应采用 Windows Shell 薄集成。
- 不建议为了跨平台统一 UI 而牺牲 Windows 原生交互。
- 不建议把 Windows Shell 逻辑塞进 ViewModel。
- 不建议让 service 层依赖 `ListViewItem`、`Brush`、`Visibility`、`DataPackage` 等 UI 类型。
- 不建议把 WPF 目标定义成 Explorer 平替。
- 不建议为了软件内远端复制 / 移动 / 拖拽牺牲链接、预览和上传下载稳定性。
- 不建议继续加深虚拟文件拖出能力，除非真实 Windows 环境验证稳定且收益明确。
- 不建议在当前路线推进 Cloud Files / 虚拟盘 / 占位文件。

## 风险与取舍

### WPF 风险

- 默认视觉较旧，需要设计系统覆盖。
- 部分现代 WinUI 控件需要自建样式。
- 如果未来要上 Windows App SDK 新特性，需要额外集成。
- 若继续追求 Explorer 级能力，复杂度会快速超过产品核心收益。

### WinUI 3 继续修的风险

- 文件管理器交互继续补丁化。
- 拖拽和 Shell 行为难以彻底接近 Explorer。
- 主窗口和 ViewModel 会继续膨胀。
- 视觉修复容易破坏交互状态。

### Cloud Files API 风险

- 开发复杂度高。
- 测试成本高。
- 系统集成边界多。
- 不适合作为第一版普通客户端基础。

## 验收标准

新的 Windows 端方向完成后，应满足：

- 登录、自动登录、服务器管理稳定。
- 可把登录后的 SMB 访问交给 Windows / Explorer 使用。
- 复制分享链接和链接唤醒稳定。
- 链接在钉钉文档 / 聊天框、浏览器、客户端冷启动 / 热启动下可用，未登录时能登录后继续打开。
- 登录成功后能打开 Explorer 到服务器根 / 共享根 / 目标目录。
- 文件链接优先打开父目录并尽量选中文件，失败时降级为打开父目录。
- Explorer 负责图片 / 视频缩略、打开方式、复制、移动、删除、重命名、同名覆盖和拖拽。
- RYNAT 提供托盘 / 后台入口：打开 Explorer、切换服务器、退出登录、诊断。
- 右键复制分享链接采用薄 Shell 集成，业务逻辑回到 RYNAT 主程序。
- 旧 WPF / Tauri 主程序线不再维护，Windows 端保持 `apps/windows-tray` 单主线。

## 总结

Windows 端的长期方向已调整为：保留 Rust core 和共享业务协议，移除旧 WPF / Tauri 主程序线，将主体验切到 Explorer-first。

新的 Windows 主线优先打磨三条核心链路：登录后接入 Explorer、复制分享链接、链接唤醒定位。目录树、文件列表、预览、拖拽、复制移动、同名覆盖等复杂文件管理体验交给 Windows Explorer。

Cloud Files 仍作为未来系统级网盘方向独立评估，不和当前 Explorer-first 轻客户端混在一起推进。
