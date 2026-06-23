# macOS Client Architecture Evolution Plan

## 背景

当前 macOS 客户端已经可以作为正式方向继续演进：Rust core 负责跨平台核心能力，macOS 端使用 AppKit 实现原生界面与系统集成。现有结构没有 Windows 端那种需要推倒重来的架构问题，但部分控制器和桥接文件仍偏重，后续应逐步减重、拆边界、提高可维护性。

本文档用于记录后续 macOS 端架构整理目标。调整原则是渐进式重构，不改变现有产品行为，不推翻 AppKit 方案。

## 目标架构

理想结构如下：

```text
Rust Core
  SMB、链接、预览计划、任务、配置、凭据、错误码

macOS Platform Adapter
  文件选择器、拖拽、Finder 打开/定位、播放器、缩略图、协议/本地中转

App Services / Use Cases
  登录、目录浏览、生成链接、收藏、上传、下载、复制、移动、预览、服务器管理

Workspace State / View Model
  当前目录、选中项、目录列表、侧边栏、预览、任务进度、登录状态

AppKit UI
  Login、Toolbar、Sidebar、FileList、PreviewPanel、StatusBar、ServerSettings
```

核心原则：

- UI 只负责展示状态和触发命令，不直接承载文件业务流程。
- 业务流程不依赖具体 AppKit 控件。
- macOS 平台能力单独封装，避免散落在主控制器里。
- Rust core 只负责跨平台稳定能力，不感知窗口、按钮、表格和颜色。
- 用户操作通过 use case 更新 state，再由 UI 刷新显示。

## 当前状态判断

macOS 端当前方向是正确的：

- `WorkspaceController` 已经拆成多个扩展文件，虽然仍是中心控制器，但职责已经比单文件堆叠更清晰。
- 已有 `SidebarView`、`FileListController`、`PreviewCoordinator`、`TransferCoordinator`、`WorkspaceOperations`、`SmbGateway` 等模块。
- AppKit 原生路线适合当前产品，不需要迁移到 SwiftUI 或跨平台 UI。
- Rust core 和 `RynatCore.swift` 桥接层作为共享底座可以继续保留。

主要问题是可维护性和边界清晰度，不是路线错误。

## 需要调整的模块

### 1. 拆分 `RynatCore.swift`

`RynatCore.swift` 目前承担 FFI 函数声明、请求模型、响应模型、错误处理和桥接调用，文件体量偏大。建议拆成：

- `RynatCoreBridge.swift`
  - 只保留 `RynatCore` 调用入口和 FFI 调用封装。
- `RynatCoreNative.swift`
  - C FFI 函数声明与字符串内存释放。
- `RynatCoreRequests.swift`
  - 所有 request DTO。
- `RynatCoreModels.swift`
  - 所有 response/domain DTO。
- `RynatCoreErrors.swift`
  - `RynatCoreError`、错误码映射、用户提示辅助。

验收标准：

- 对外调用方式尽量不变。
- 不改 JSON 字段名，不改 FFI ABI。
- 编译通过，链接/登录/预览/传输行为保持一致。

### 2. 让 `WorkspaceController` 继续变薄

`WorkspaceController` 当前仍是主协调器，负责窗口、状态、目录、预览、传输、右键、收藏、链接等流程。后续应把业务流程移到 service/use case 层。

优先抽离：

- 文件操作：上传、下载、复制、移动、删除、重命名、新建文件夹。
- 链接操作：生成链接、复制链接、打开外部链接、收藏/取消收藏。
- 预览操作：预览计划、缓存、缩略图渲染、播放准备。
- 登录/会话操作：登录、自动登录、登出、服务器切换。

保留在 `WorkspaceController` 中的职责：

- 创建窗口和主要视图。
- 绑定 UI 事件。
- 调用 use case。
- 根据 state 刷新 UI。
- 做少量 AppKit 生命周期协调。

### 3. 建立 App Services / Use Cases

建议逐步新增以下服务：

- `LoginService`
  - 账号密码登录、存储凭据登录、自动登录、登出前清理。
- `DirectoryService`
  - 打开目录、刷新目录、目录缓存策略、目录加载取消。
- `FileOperationService`
  - 新建、重命名、删除、上传、下载、复制、移动、同名冲突处理入口。
- `PreviewUseCase` 或 `PreviewService`
  - 预览计划、缓存文件、渲染缩略图、视频播放准备。
- `QuickLinkService`
  - 生成链接、收藏、取消收藏、打开收藏。
- `ServerProfileService`
  - 服务器列表、新增、修改、删除、默认服务器。

这些服务应依赖 Rust core bridge 和平台 adapter，不依赖 `NSTableView`、`NSButton`、`NSSplitView` 等 UI 控件。

### 4. 抽出 Workspace State

建议新增 `WorkspaceState` 或 `WorkspaceViewModel`，集中保存 UI 状态：

- 当前登录状态。
- 当前服务器。
- 当前目录。
- 当前选中项。
- 当前目录列表。
- 搜索关键字。
- 侧边栏 tab、展开项、收藏列表。
- 预览面板状态。
- 当前任务进度。
- 状态栏文案。

目标是减少控制器里大量分散变量，降低状态不同步风险。

注意：此调整可以渐进，不要求一次性引入完整响应式框架。

### 5. 平台能力独立封装

macOS 专属能力建议集中到 platform adapter：

- `MacFileDialogService`
  - 打开文件选择器、文件夹选择器、保存位置选择。
- `MacDragDropService`
  - 拖拽上传、拖出下载、拖拽视觉和 pasteboard 数据。
- `MacFinderService`
  - Finder 打开文件、定位文件。
- `MacMediaService`
  - 本地视频播放、调用系统播放器。
- `MacThumbnailService`
  - 图片、视频、PDF 缩略图渲染。
- `MacLinkActivationService`
  - URL scheme、本地中转服务、外部链接唤醒。

这样可以避免平台 API 散落在 `WorkspaceController` 的不同扩展里。

### 6. 继续拆 UI 组件

已有 `SidebarView` 和 `FileListController` 是正确方向。后续可继续拆：

- `WorkspaceToolbarView`
- `PreviewPanelView`
- `StatusBarView`
- `LoginCardView`
- `ServerSettingsView`
- `TaskProgressView`

目标不是为了拆而拆，而是让每个组件只处理自己的布局、样式和局部交互。

## 推荐实施顺序

### 阶段一：低风险整理

1. 拆 `RynatCore.swift`。
2. 把纯数据模型、请求 DTO、错误类型移出桥接文件。
3. 不改业务逻辑，不改 UI 行为。

### 阶段二：抽服务

1. 抽 `QuickLinkService`。
2. 抽 `DirectoryService`。
3. 抽 `PreviewService`。
4. 抽 `FileOperationService`。

先抽最清晰、耦合最少的部分，避免一次性大改。

### 阶段三：抽状态

1. 新增 `WorkspaceState`。
2. 把 `WorkspaceController` 里的状态变量逐步迁移。
3. 保持 UI 刷新入口稳定。

### 阶段四：拆 UI 组件

1. 抽 toolbar/status/preview panel。
2. 整理登录页和服务器设置弹窗。
3. 减少 `WorkspaceController+Layout.swift` 的体量。

## 不建议做的事

- 不建议把 macOS 端推倒重写。
- 不建议为了双平台统一而改成跨平台 UI。
- 不建议现在迁移 SwiftUI。现有 AppKit 对文件管理器类交互更稳。
- 不建议把 Windows 的 UI 结构反向套到 macOS。
- 不建议让 Rust core 处理平台 UI 细节。

## 后续评估标准

每次重构后至少检查：

- 登录、自动登录、登出。
- 服务器设置。
- 目录树展开/收起/双击。
- 文件列表选择、双击打开、右键菜单。
- 上传、下载、复制、移动、删除、重命名、新建文件夹。
- 同名冲突确认。
- 预览缩略图、视频播放。
- 生成链接、打开链接、收藏/取消收藏。
- 窗口缩放、预览面板开关、侧边栏宽度。

## 总结

macOS 端当前架构方向成立，后续目标是减重和边界清晰化。最重要的调整不是换技术栈，而是把 `WorkspaceController` 中的业务流程逐步迁移到 App Services / Use Cases，把平台能力集中封装，把 UI 状态独立出来。
