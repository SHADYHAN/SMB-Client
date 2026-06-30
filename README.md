# RYANT共享网盘

NAS 文件的原生桌面客户端，macOS 与 Windows 各自使用原生 UI，共用一套 Rust 跨平台业务核心。

## 项目定位

RYANT共享网盘是一个面向 NAS / SMB 共享文件的原生桌面客户端。产品围绕两件核心事设计：

1. **固定分享链接** — 选中 NAS 上的文件或目录，生成稳定的 HTTP 链接，粘到钉钉、飞书、在线文档等位置，点击后唤醒客户端并定位到对应目录或文件。
2. **本地原生预览** — 对图片、视频等文件提供缩略图、首帧、播放等预览能力，尽量通过流式处理和缓存策略减少界面内存消耗。

目录浏览、上传下载、复制移动、右键菜单、多服务器切换等能力，都是围绕这两个核心需求提供的易用性功能。

## 架构

```text
crates/rynat-core/      Rust 共享核心（业务逻辑 + C ABI FFI）
  bridge.rs             C ABI + JSON FFI，供两端原生壳调用
  smb_client.rs         SMB 连接/目录/文件操作（多连接隔离、任务化、服务端拷贝）
  storage.rs            SQLite 持久化（服务器配置、凭据、快速链接）
  credential.rs         凭据 AES-256-GCM 加密（macOS Keychain 派生密钥）
  link.rs               快速链接生成、解析、打开意图
  server.rs             多服务器配置和 SMB 协议偏好
  session.rs            外部链接唤醒后的业务状态
  preview.rs            预览服务接口模型和稳定缓存键
  transfer.rs           流式复制/上传/下载计划
  redirect_page.rs      无感浏览器中转页

apps/macos/RYNATClient/ AppKit 原生壳（Swift）
  AppDelegate.swift     应用外壳（URL 事件、菜单）
  WorkspaceController+*.swift  工作区中枢（导航/预览/传输/侧栏/链接/布局/窗口）
  RynatCore.swift       FFI 绑定与数据模型
  LoginViewController / SidebarView / FileListController / ServerSettingsDialog 等
apps/windows-tray/      Explorer-first Windows 主线（.NET 10 WinForms 托盘宿主 + WebView2 UI）
  App/                  托盘生命周期、运行状态
  UI/                   WebView2 本地登录 / 设置 / 状态界面
  Services/             Explorer 打开、本地短链、右键 helper IPC
apps/windows-context-helper/ Explorer 右键薄 helper，负责接收选中路径并转发
crates/rynat-windows-shell-support/ Explorer-first Windows 共享支撑逻辑
include/                跨平台 C ABI 头文件（rynat_core.h）
docs/                   当前 Windows / macOS 架构方向文档
scripts/                构建与桥接校验脚本
tools/                  FFI 冒烟测试、中转页样例
```

Rust Core 承载全部跨平台业务能力（链接、SMB、凭据、存储、预览/传输计划）。两端原生壳只负责 UI 与平台集成，通过同一套 C ABI + JSON FFI 调用 Core。

## Rust Core 能力

- **多连接模型** — SMB 连接按 `connection_id` 隔离，多服务器可并存，切换不互相打断。
- **任务 API** — 长操作（上传/复制/删除/缓存）走后台线程 + `start/poll/cancel/clear`，不阻塞 FFI 调用线程；已完成任务 TTL 自动清理。
- **凭据加密** — AES-256-GCM，macOS 用 Keychain 派生密钥（含随机秘密），失败显式报错不静默降级；Windows/Linux 回退机器标识派生。
- **持久化** — SQLite 存服务器配置 / 凭据 / 快速链接 / 当前服务器，含事务化迁移。
- **SMB 操作** — 服务端拷贝（FSCTL_SRV_COPYCHUNK）+ 流式回退、逐 chunk 上传（可取消）、临时文件原子提交、冲突处理。
- **链接确定性** — 同一服务器/共享/路径/类型生成的 URL 固定，不含用户身份。
- **错误分类** — 结构化 `error_code`（auth/permission/reconnectable/cancelled/not_found/smb 等）供 UI 精准提示。

## 客户端设计

- **原生 UI** — macOS 使用 AppKit。Windows 主方向为 Explorer-first：`apps/windows-tray` 提供 .NET 10 WinForms 托盘宿主 + WebView2 本地 UI，RYNAT 负责登录、链接和 Shell 集成，把文件浏览 / 复制 / 移动 / 预览交给 Windows Explorer。
- **文件管理体验** — 目录树、文件列表、右键菜单、拖拽、键盘操作尽量贴近系统文件管理器习惯。
- **登录与服务器** — 登录时选择服务器，支持保存服务器配置、保存凭据、自动登录，多服务器通过本地配置匹配链接目标。
- **文件操作** — 上传、下载、复制、移动、重命名、删除、新建文件夹、同名冲突确认、进度和取消。
- **预览** — 图片缩略图、视频首帧、视频播放等预览能力通过平台侧实现，缓存键和预览计划由 Rust Core 统一生成。
- **链接唤醒** — 客户端支持本地 HTTP 中转、`rynat://` 协议注册、单实例转发，外部文档中的链接可以唤醒客户端并定位目标。

## 快速链接格式

对外复制/分享的链接：

```text
http://127.0.0.1:19527/s/AQIPMTkyLjE2OC4xMDIuMTM2CkJhY2tvZmZpY2UQL0NvbnRyYWN0cy8yMDI0
http://127.0.0.1:19527/s/AQEJbmFzLmxvY2FsBU1lZGlhEC9Nb3ZpZXMvZGVtby5tcDQ
```

钉钉、飞书、在线文档里用 `http(s)` 中转链接。原生客户端注册 `rynat://`，浏览器中转页无感唤醒（页面初始隐藏，几十毫秒内尝试唤醒，失败才显示提示）。

- **本地助手模式** — `http://127.0.0.1:19527/s/<短码>`，要求客户端或常驻 helper 已运行。
- **公网 HTTPS 模式** — 可扩展为部署到公共域名的中转入口。

内部唤醒协议：

```text
rynat://s/AQEJbmFzLmxvY2FsBU1lZGlhEC9Nb3ZpZXMvZGVtby5tcDQ
```

`/s/<短码>` 中的短码是无填充 base64url 编码的固定二进制 payload，内部包含服务器地址、共享名、共享内路径和 `file`/`dir` 类型。中文共享名和路径不会在链接里展开成很长的 `%E5...` 转义串。

链接固定：同一服务器/共享/路径/类型，不管谁生成 URL 都一样，不带用户 ID、收藏 ID、生成时间、随机 token 或显示名。文件链接打开意图是进入父目录、选中文件、打开预览；目录链接是进入该目录。客户端不提供「输入链接打开」功能，链接入口来自文档、浏览器或系统协议唤醒。

## 多服务器与凭据

多服务器切换是本地 UI 状态。`server_host` 用来匹配本机保存的服务器配置，不代表用户账号；同一链接发给别人，对方用自己的本地配置和凭据访问。SMB 默认 `Smb3Preferred`。

## 构建与验证

```bash
# Rust Core 测试
cargo test -p rynat-core

# macOS 客户端构建（产出 .app / .dmg）
scripts/build-macos-client.sh

# Windows Explorer-first WinForms + WebView2 主线构建检查
scripts\windows-tray\build-check.bat

# Windows Explorer-first WinForms + WebView2 主线发布
scripts\windows-tray\build-release.bat

# Windows 安装包构建（先运行 build-release.bat）
scripts\windows-tray\build-installer.bat

# Windows Explorer-first 支撑逻辑测试（右键 helper / 路径映射参考线）
cargo test -p rynat-windows-shell-support --locked --offline

```

macOS 构建脚本先 `cargo build -p rynat-core --release`，再把 `librynat_core.dylib` 打包进 `.app`。Windows 工程 `csproj` 内置 cargo 构建目标，编译 Rust Core 并复制 `rynat_core.dll`。

Windows Tray 主线 release 脚本会把发布产物复制到 `build\windows-tray-release\<yyyyMMdd-HHmmss>\`，并写入 `build\windows-tray-release\latest.txt`。
默认发布为不指定 RID 的 framework-dependent 产物，依赖目标机器已安装 .NET 10 Windows Desktop Runtime / SDK；如需固定架构或自包含产物，可运行 `scripts\windows-tray\build-release.bat -RuntimeIdentifier win-x64` 或 `scripts\windows-tray\build-release.bat -RuntimeIdentifier win-x64 -SelfContained`。
安装包脚本使用 Inno Setup 6，会读取 `latest.txt` 指向的发布目录，生成中文安装程序。安装器按所有用户安装到 `C:\Program Files\RYANT共享网盘`，需要管理员权限，并注册机器级右键 helper、公共开始菜单和公共桌面快捷方式。

FFI 冒烟测试：

```bash
# macOS
scripts/ffi-smoke-test.sh
# Windows
scripts/ffi-smoke-test-windows.ps1
```

## 当前状态

- Rust Core 是共享业务底座，链接、SMB、存储、凭据、任务、错误码、预览计划等能力已形成双平台共用边界。
- macOS AppKit 客户端作为当前 macOS 主线继续演进，后续重点是拆薄 `WorkspaceController` 和 `RynatCore.swift`。
- Windows 主方向已经切到 Explorer-first：RYNAT 提供登录、服务器设置、凭据接入、Explorer 右键复制分享链接、链接唤醒和托盘常驻，文件浏览和文件操作交给 Windows Explorer。
- 旧 WinUI 3、WPF 和 Tauri Windows 线已移除，避免继续维护多条 Windows 主程序路线。
- 快速链接已切换为紧凑 `/s/<短码>` 格式；Windows UI 默认复制文档 / 聊天工具友好的 HTTP 短链接，避免钉钉文档把 `rynat://` 富文本 href 改写成坏链接。本地中转命中已运行客户端后返回已激活关闭页，尽量让浏览器标签页自动关闭，失败时显示可手动关闭提示。

## 后续重点

- Windows：完善正式安装包，安装主程序、右键 helper、桌面快捷方式和卸载清理。
- Windows：优化 Windows 11 一级右键入口；当前传统右键入口已由 `Rynat.WindowsContextHelper.exe` 转发给主程序处理。
- Windows：继续打磨登录后设置界面、托盘入口、链接唤醒和资源管理器前台定位体验。
- Windows：`apps/windows-tray` 与 `apps/windows-context-helper` 是 Explorer-first 主线。
- macOS：按 `docs/macos-architecture-evolution-plan.md` 渐进拆分桥接、服务和状态。
- 双平台：保持 `include/rynat_core.h`、Swift bridge、C# bridge 与 Rust ABI 同步。

## 目录约定

- `crates/rynat-core` — 共享业务协议与执行能力：SMB、链接、配置、凭据、任务、错误码、预览计划。
- `crates/rynat-windows-shell-support` — Explorer-first Windows 路径映射、Explorer 目标、SMB session 和右键 helper 契约。
- `apps/macos` — macOS AppKit 主客户端。
- `apps/windows-tray` / `apps/windows-context-helper` — Windows Explorer-first 新主线。
- `include/rynat_core.h` — 跨平台 C ABI 头文件，是两端 FFI 的唯一契约源。
- `docs/` — 当前 Windows / macOS 架构方向文档。
