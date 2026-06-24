# Windows Client Architecture Direction

## 背景

早期 Windows 端已经有过完整 WinUI 3 实现，但旧 UI / app 层更像是在 WinUI 3 上手工模拟资源管理器。实际体验中已经暴露出拖拽影子、拖出桌面临时路径、同名覆盖、目录树交互、文件列表列宽、选择状态等一系列问题。

这些问题不只是单个 bug，也不完全是 WinUI 3 本身的问题。根因是 Windows 端 app 层没有按“文件管理器类客户端”建立清晰边界：UI、交互状态、文件业务、平台 Shell 能力和本地缓存策略混在一起，导致后续修复容易补丁化。

本文档用于确定 Windows 端长期方向。原则是保留 Rust core，重新设计 Windows 原生客户端层，不继续在旧 WinUI 3 主窗口结构上无限打补丁。

## 总体结论

推荐方向：

```text
Rust Core 保留
Windows UI / App Layer 重新设计
优先 WPF 主客户端 + Windows Shell Adapter
后期按需求接 Cloud Files API
```

不建议继续把旧 WinUI 3 版本作为长期成品基础。它可以作为功能验证参考，但不应继续堆补丁到最终版本。

## 目标架构

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
  文件上传 / 下载 / 复制 / 移动 / 删除 / 重命名
  同名冲突处理
  收藏 / 快捷链接
  预览加载
  任务调度

Windows Platform Adapters
  Shell 拖拽 / 剪贴板
  文件选择器 / 保存对话框
  Explorer 打开 / 定位
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
- Windows 端按 Windows 用户习惯实现，不为了和 mac UI 代码结构一致而牺牲原生体验。

## 技术路线建议

### 推荐：WPF 主客户端

建议 Windows 桌面主客户端使用 WPF，而不是继续基于旧 WinUI 3 版本推进。

原因：

- WPF 对传统桌面文件管理器类应用更成熟。
- TreeView、ListView/GridView、ContextMenu、Command、DataTemplate、DragDrop 行为更稳定、更可控。
- 更适合做资源管理器式目录树、文件表格、右键菜单、多选、键盘操作、焦点状态。
- 与 Win32/Shell/COM 互操作资料更多，适合后续做 Shell 级拖拽和虚拟文件。
- 默认视觉较旧，但可以通过自定义样式做到现代化，不等于 UI 老。

### WinUI 3 的定位

WinUI 3 可以做现代 Windows UI，但对本项目不是最优主线。

适合 WinUI 3 的场景：

- 普通业务表单。
- 设置页。
- 轻量信息浏览。
- 非重度 Shell 文件交互。

本项目的问题集中在：

- 类资源管理器目录树。
- 文件列表表格列宽与多选。
- 拖入 / 拖出。
- 桌面同名覆盖。
- 右键菜单。
- 缩略图与文件预览。
- 系统级文件体验。

这些地方 WinUI 3 高层控件和 DataPackage 抽象不够贴近 Explorer 行为。继续修可以改善局部，但很难变成稳定的资源管理器体验。

### 后期：Cloud Files API

如果后续目标升级为类似 OneDrive / 企业网盘的系统级体验，可以考虑 Cloud Files API。

适合 Cloud Files API 的能力：

- Explorer 中显示网盘入口。
- 占位文件。
- 按需下载。
- 系统级图标、状态、同步标识。
- 文件打开时自动拉取。
- 更自然的拖拽和复制体验。

不建议第一阶段直接上 Cloud Files API。它更像第二阶段系统集成工程，复杂度比普通客户端高很多。

## 当前 Windows 代码的定位（2026-06-24）

当前 `apps/windows` 已经切换为新的 WPF 主线客户端，不再是旧 WinUI 代码。旧 WinUI 版本保留在 `apps/windows-winui-legacy`，只作为业务流程和问题样本参考，不应继续作为最终客户端基础。

当前 WPF 主线已经具备：

- 登录 / 自动登录入口。
- 服务器设置、新增/删除/默认服务器选择。
- SMB 连接与共享目录加载。
- 目录树单击进入、双击展开 / 收起并进入。
- 文件列表、刷新、新建文件夹、重命名、删除。
- 本地文件拖入上传，并在同名时确认是否覆盖。
- 文件拖出到本地的虚拟文件下载基础能力。
- 图片 / 视频预览基础能力，右侧预览面板可关闭。
- 生成固定 HTTP 分享链接并复制到剪贴板。
- `rynat://` 协议注册、本地 HTTP 中转、单实例转发、前台唤醒的链接链路。
- 短链接 `/s/<code>`，以及本地已激活后的浏览器关闭页。
- Windows 一键拉取、构建、启动脚本。

当前 WPF 主线的剩余重点：

1. 补齐软件内远端复制 / 移动 / 粘贴，以及复制 / 移动时的同名冲突确认。
2. 打磨拖拽体验：软件内拖动只做视觉反馈，跨目录后才触发移动 / 复制；拖出桌面的视觉影子和同名行为继续贴近 Explorer。
3. 优化预览：图片缩略图、视频首帧、大视频避免整文件缓存、缓存清理策略。
4. 继续拆薄 `ShellViewModel`。当前约 800 行，已远小于旧 WinUI 主 ViewModel，但仍是后续功能增长的压力点。
5. 补充 Windows 侧 smoke checks，覆盖桥接接口、启动参数、本地中转和链接唤醒。

`apps/windows-winui-legacy` 代码建议定位为：

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

当前 WPF 主线已经把本地 HTTP 中转、协议注册、单实例转发放入 `Platform/Activation`，后续应继续保持这个边界。

## 新 Windows 客户端建议模块

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

- 不依赖 WPF 控件。
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

- `WindowsShellDragDropService`
- `WindowsClipboardService`
- `WindowsFileDialogService`
- `WindowsExplorerService`
- `WindowsThumbnailService`
- `WindowsMediaService`
- `WindowsProtocolRegistrationService`
- `WindowsSingleInstanceService`
- `WindowsLocalRedirectService`

其中拖出桌面是最关键边界。

第一阶段可以做应用级拖拽，但要明确能力边界：

- 可拖出文件。
- 文件名尽量保留原名。
- 允许同名时走 Explorer 提示。
- 仍可能暴露本地缓存路径。

真正 Explorer 级体验应进入第二阶段 Shell / Cloud Files 集成。

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

### 6. WPF UI Views

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

## 关键交互标准

Windows 端必须以资源管理器直觉为基准。

### 目录树

- 单击：选中并进入目录。
- 双击：进入并展开 / 收起目录。
- 展开方向：向下展开。
- 展开后不跳动。
- 当前访问目录必须有稳定选中态。
- 加载中状态要轻量明确。

### 文件列表

- 默认列表模式优先。
- 多选行为接近 Explorer。
- 空白区域点击清除选择。
- 进入目录后不应强制选中第一项，除非产品明确需要。
- 列宽可自动适配，必要时用户可调整。
- 右键文件和右键空白区域菜单不同。

### 拖拽

- 软件内部拖拽：主要用于移动 / 复制远端文件，未跨目录时只表现为视觉反馈，不应触发无意义操作。
- 拖入软件：从本地上传，不要求拖到指定区域。
- 拖出软件：下载到目标位置，视觉影子应简洁，文件名跟随鼠标或接近系统表现。
- 同名文件：应由用户确认覆盖 / 跳过，不静默失败。
- 拖出不应让用户感知复杂临时缓存逻辑；若第一阶段无法避免，应在架构文档中标明限制。

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

### 阶段一：确定 WPF 骨架（已完成）

完成内容：

1. 新建 WPF app。
2. 接入 Rust core bridge。
3. 建立应用服务和 platform adapter。
4. 实现登录页。
5. 实现服务器配置读取和设置窗口。
6. 实现主窗口三栏布局：目录树、文件列表、预览。

### 阶段二：文件浏览核心功能（基本完成，继续打磨）

已完成：

1. 目录树加载、展开、收起。
2. 文件列表选择、进入目录、刷新。
3. 搜索、状态栏、预览面板。
4. 基础右键菜单和复制链接。

继续打磨：

1. 更完整的键盘操作和多选细节。
2. 大目录下的加载、取消和缓存策略。
3. 文件列表列宽、排序和细节状态。

### 阶段三：核心业务能力（大部分完成）

已完成：

1. 生成链接 / 复制链接。
2. 链接唤醒定位目录 / 文件。
3. 图片 / 视频基础预览和视频播放。
4. 本地短链接已激活后的浏览器关闭页。

继续打磨：

1. 收藏 / 取消收藏 UI 是否需要在 Windows 端补齐。
2. 视频首帧和大文件预览性能。
3. 链接在钉钉、飞书、浏览器、客户端冷启动 / 热启动下的持续实测。

### 阶段四：文件操作（部分完成）

已完成：

1. 上传。
2. 删除 / 重命名 / 新建文件夹。
3. 本地拖入上传同名确认。
4. 拖出下载基础能力。

待完成：

1. 远端复制 / 移动 / 粘贴。
2. 远端复制 / 移动同名冲突确认。
3. 长任务取消和进度体验统一。

### 阶段五：Windows Shell 集成（部分完成，继续实机打磨）

已完成：

1. 拖入上传。
2. 拖出桌面下载基础能力。
3. 协议注册 / 本地中转 / 单实例 / 前台唤醒。

待完成：

1. 软件内拖拽移动 / 复制。
2. 拖出视觉影子继续贴近 Explorer。
3. Explorer 同名覆盖行为持续实测。
4. 如第一阶段应用级拖拽仍无法满足体验，再评估更深的 Shell / Cloud Files 集成。

### 阶段六：Cloud Files API 评估

仅当产品需要系统级网盘体验时进入。

评估项：

- 是否需要 Explorer 里直接出现 RYNAT 网盘。
- 是否需要占位文件。
- 是否需要按需下载。
- 是否需要系统同步状态标记。
- 是否需要系统级文件打开和复制体验。

## 从旧 WinUI 代码迁移的建议顺序

优先迁移：

1. `RynatCoreBridge.cs` 的 FFI 思路。
2. Server profile / credential DTO。
3. Link DTO 和激活逻辑。
4. Directory browsing 的路径规则。
5. File operation 的业务顺序。
6. Preview cache 的基本策略。

暂不迁移或重写：

1. `MainWindow.xaml`。
2. `MainWindow.xaml.cs`。
3. `MainShellViewModel.cs`。
4. 旧 WinUI ListView 目录树模拟逻辑。
5. 旧 WinUI DataPackage 拖出逻辑。
6. 任何直接依赖 WinUI 控件的 service 入参。

## 不建议做的事

- 不建议继续在旧 WinUI 主窗口上修视觉和拖拽细节作为长期方案。
- 不建议换 Electron / Tauri / Avalonia / MAUI 来解决 Windows 文件体验问题。
- 不建议为了跨平台统一 UI 而牺牲 Windows 原生交互。
- 不建议把 Windows Shell 逻辑塞进 ViewModel。
- 不建议让 service 层依赖 `ListViewItem`、`Brush`、`Visibility`、`DataPackage` 等 UI 类型。

## 风险与取舍

### WPF 风险

- 默认视觉较旧，需要设计系统覆盖。
- 部分现代 WinUI 控件需要自建样式。
- 如果未来要上 Windows App SDK 新特性，需要额外集成。

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
- 可连接真实 SMB 服务器。
- 目录树和文件列表行为接近资源管理器。
- 生成链接和链接唤醒稳定。
- 图片 / 视频 / PDF 预览可用。
- 上传、下载、复制、移动、删除、重命名、新建文件夹可用。
- 同名冲突必须让用户确认。
- 窗口大小自由调整，布局自适应。
- 拖拽视觉简洁，不出现明显非原生的表格行影子。
- 代码结构中不存在超大主窗口类和超大 ViewModel。

## 总结

Windows 端的长期方向应是：保留 Rust core 和共享业务协议，重新设计 Windows 原生客户端层。第一阶段建议采用 WPF 构建主客户端，以更成熟可控的桌面控件和 Win32/Shell 互操作承载文件管理器体验。旧 WinUI 3 代码可作为功能验证和业务流程参考，但不建议作为最终成品架构继续补丁化推进。
