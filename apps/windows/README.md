# Windows 客户端

原生 WinUI 3 客户端，接入共享 Rust Core，与 macOS 端功能对等。

## 结构

- `Rynat.WindowsClient.csproj` — WinUI 3 单项目应用，构建时自动编译并复制 `rynat-core`。
- `RynatCoreBridge.cs` — 对 `rynat_core.dll` 的薄适配层，只负责 DTO、P/Invoke、JSON 序列化和错误展开。
- `AppServices/` — 应用服务层，负责启动、SMB 会话、目录浏览、链接生成/唤起、预览入口、文件操作、任务跟踪、缓存管理等流程编排。
  - `Bootstrap/` 启动信息加载与持久化存储打开
  - `Smb/` 会话连接、服务器配置管理
  - `Directory/` 目录浏览与重连重试
  - `Files/` 上传/下载/复制/删除/重命名/拖拽/批量操作
  - `Tasks/` 文件任务跟踪与进度
  - `Preview/` 预览入口与缓存
  - `Cache/` 预览缓存管理
  - `Links/` 链接激活、分享、快速链接库
- `PlatformIntegration/` — Windows 专属集成：预览承载、`rynat://` 协议注册、单实例转发、本地中转服务、系统文件打开。
- `UI/Main/` — MVVM 视图模型（`MainShellViewModel` + 会话/侧栏/目录/服务器列表模型）。
- `MainWindow.xaml` / `MainWindow.xaml.cs` — 主窗口布局与交互。

## 能力

- 启动信息加载、持久化存储（`%LocalAppData%\Rynat\rynat.sqlite`）
- 已存凭据连接 / 账号密码手动连接 SMB
- 共享列表、目录浏览与缓存复用
- 单击选中触发预览，双击目录进入、双击文件系统打开
- 图片 / PDF / 视频预览
- 复制分享链接、复制路径
- 外部深链接唤起并定位到目标目录或文件
- 单实例命令转发、`rynat://` 协议注册、本地中转服务
- 拖拽上传/下载、复制/剪切/粘贴、重命名、删除、冲突处理
- 多服务器配置管理（设置面板）

## 分层约束

- `crates/rynat-core` — 共享业务协议与执行能力：SMB、链接、配置、凭据、任务、错误码、预览计划。
- `apps/windows/AppServices` — Windows 端应用流程编排，不把页面状态塞进 Core。
- `apps/windows/PlatformIntegration` — Windows 专属能力，不把 WinUI / Shell / 系统 API 混进 bridge。
- `apps/windows/RynatCoreBridge.cs` — Core adapter，只做 DTO/P/Invoke/JSON，不承接页面流程和应用策略。

## 桥接校验

为防止 `include/rynat_core.h`、Swift bridge、C# bridge 与 Rust ABI 漂移：

- `scripts/check-bridge-surface.ps1` — 校验 Rust exports、C header、Swift bridge、C# bridge 一致性。
- `scripts/ffi-smoke-test-windows.ps1` — surface check 后跑 Windows bridge smoke test。
- `scripts/ffi-smoke-test.sh` — macOS 侧 smoke test（同样先 surface check）。

## 常用命令

构建 Windows 客户端：

```powershell
dotnet build apps\windows\Rynat.WindowsClient.csproj -v minimal
```

单独构建 Windows 用 Rust DLL：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-windows-core.ps1 -Configuration Debug
```

检查 bridge surface：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\check-bridge-surface.ps1
```

运行 Windows bridge smoke test：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\ffi-smoke-test-windows.ps1
```

构建产物（`bin/`、`obj/`）已 gitignore，不会进入仓库。
