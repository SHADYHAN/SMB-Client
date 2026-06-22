# RYNAT 共享网盘

NAS 文件的原生桌面客户端，macOS 与 Windows 各自使用原生 UI，共用一套 Rust 跨平台业务核心。

## 做什么

围绕两件核心事：

1. **生成链接** — 选中 NAS 上的文件或目录，生成固定链接，粘到钉钉、飞书、在线文档里，点击即唤醒客户端定位并预览。
2. **稳定预览** — 图片缩略图、视频首帧、视频点击播放，流式处理不把完整文件读入界面内存。

目录浏览、上传下载、复制剪切、右键菜单、多服务器切换都是围绕这两个核心能力的易用性功能。

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
apps/windows/           WinUI 3 原生壳（C# / .NET 8）
  RynatCoreBridge.cs    P/Invoke FFI 绑定与数据模型
  AppServices/          业务服务层（Bootstrap/Smb/Directory/Files/Tasks/Preview/Cache/Links）
  UI/Main/              MVVM（MainShellViewModel + 平台集成）
  PlatformIntegration/  协议注册、单实例、本地中转、预览承载、文件打开
include/                跨平台 C ABI 头文件（rynat_core.h）
docs/                   代码审查文档
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

## 客户端能力

两端功能对等：

- 三栏布局：侧栏目录树（共享/收藏）+ 文件区（列表/网格）+ 检视器（预览 + 操作，可折叠）。
- 单层工具栏：导航 + 面包屑 + 搜索 + 视图切换 + 检视器开关 + 服务器切换 + 主题 + 用户菜单。
- 极简登录页：用户名 + 密码 + 记住密码 + 自动登录；自动登录跳过登录页直连。
- 完整文件操作：拖拽上传/下载、复制/剪切/粘贴、重命名、删除、冲突对话框、进度与取消。
- 预览：图片缩略图、视频首帧 + 点击播放、PDF；流式缓存，固定缓存键。
- 外部链接唤醒三态：未登录→挂起待登录；不匹配→提示；匹配→定位目录并预览。
- 本地中转服务（127.0.0.1:19527）：浏览器 HTTP 链接无感唤醒原生客户端，含 recv 超时防阻塞。
- 单实例 + `rynat://` 协议注册。
- 深色模式跟随系统（macOS 支持主题三态切换）。

## 快速链接格式

对外复制/分享的链接：

```text
http://127.0.0.1:19527/s?h=192.168.102.136&s=Backoffice&p=/Contracts/2024
http://127.0.0.1:19527/s?h=nas.local&s=Media&p=/Movies/demo.mp4&t=file
```

钉钉、飞书、在线文档里用 `http(s)` 中转链接。原生客户端注册 `rynat://`，浏览器中转页无感唤醒（页面初始隐藏，几十毫秒内尝试唤醒，失败才显示提示）。

- **本地助手模式** — `http://127.0.0.1:19527/s?...`，要求客户端或常驻 helper 已运行。
- **公网 HTTPS 模式** — 后续扩展。

内部唤醒协议：

```text
rynat://s?h=nas.local&s=Media&p=/Movies/demo.mp4&t=file
```

参数：`h` 服务器地址、`s` 共享名、`p` 共享内路径、`t` `file`/`dir`（旧链接可缺省）。

链接固定：同一服务器/共享/路径/类型，不管谁生成 URL 都一样，不带用户 ID、收藏 ID、生成时间、随机 token 或显示名。文件链接打开意图是进入父目录、选中文件、打开预览；目录链接是进入该目录。客户端不提供「输入链接打开」功能，链接入口来自文档、浏览器或系统协议唤醒。

## 多服务器与凭据

多服务器切换是本地 UI 状态。`server_host` 用来匹配本机保存的服务器配置，不代表用户账号；同一链接发给别人，对方用自己的本地配置和凭据访问。SMB 默认 `Smb3Preferred`。

## 构建与验证

```bash
# Rust Core 测试
cargo test -p rynat-core

# macOS 客户端构建（产出 .app / .dmg）
scripts/build-macos-client.sh

# Windows 客户端构建（需 Windows + .NET 8 SDK + Windows App SDK）
dotnet build apps/windows/Rynat.WindowsClient.csproj
```

macOS 构建脚本先 `cargo build -p rynat-core --release`，再把 `librynat_core.dylib` 打包进 `.app`。Windows 工程 `csproj` 内置 cargo 构建目标，编译 Rust Core 并复制 `rynat_core.dll`。

桥接一致性校验（防止 C 头文件 / Swift bridge / C# bridge 与 Rust ABI 漂移）：

```bash
# macOS
scripts/ffi-smoke-test.sh
# Windows
scripts/check-bridge-surface.ps1
scripts/ffi-smoke-test-windows.ps1
```

## 目录约定

- `crates/rynat-core` — 共享业务协议与执行能力：SMB、链接、配置、凭据、任务、错误码、预览计划。
- `apps/macos` / `apps/windows` — 各自原生 UI 与平台集成，不把页面状态塞进 Core。
- `include/rynat_core.h` — 跨平台 C ABI 头文件，是两端 FFI 的唯一契约源。
- `docs/` — 代码审查记录。
