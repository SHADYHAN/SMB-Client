# Explorer-first Windows Client Direction

## 当前状态

本文档现在是 Windows 端新的主方向。

2026-06-27 决策：Windows 主线进入 Explorer-first：RYNAT 负责登录、服务器设置、凭据接入、Explorer 右键复制分享链接、链接唤醒、托盘和诊断；Windows Explorer 负责文件浏览、打开、复制、移动、删除、重命名、缩略图和系统文件操作。

2026-06-30 决策：旧 WinUI 3、WPF 和 Tauri Windows 主程序线已移除，避免继续维护多条路线。历史结论保留在文档中，代码树只保留 Explorer-first 主线。

当前新线技术栈已在 2026-06-30 调整为更轻的 Windows 托盘主程序：

- `apps/windows-tray`：.NET 10 WinForms 托盘宿主 + WebView2 本地 UI，作为新的 Windows 主线。它负责登录窗口、托盘常驻、本地短链监听、Explorer 唤醒和右键 helper IPC。
- `crates/rynat-windows-shell-support`：共享 Rust 支撑层，负责 UNC 路径解析、Explorer 打开目标、SMB session 接入边界和右键 helper 请求格式。
- `apps/windows-context-helper`：Explorer 右键薄 helper，第一版只接收 `copy-link <UNC path>` 并输出 / 转发结构化请求。

## 背景

旧 Windows 自研文件管理器已经验证过登录、目录树、文件列表、拖拽、预览、复制链接和链接唤醒等能力。但 Windows 端继续自研完整文件管理器，会持续遇到资源管理器级交互细节：

- 拖入 / 拖出和同名覆盖行为。
- 多选、右键、键盘快捷键、焦点状态。
- 缩略图、文件打开方式、视频 poster。
- 大文件复制 / 移动 / 删除进度和取消。
- 与桌面、Explorer、其他软件之间的 Shell 交互一致性。

这些能力 Windows Explorer 已经天然提供。新的方向是让 RYNAT 不再承担完整文件管理器职责，而是作为登录器、链接服务和 Shell 集成后台。

## 总体结论

推荐 Windows 端长期主方向：

```text
Explorer-first Windows Client

RYNAT 负责：
  登录 / 服务器管理
  SMB 访问凭据接入
  Explorer 右键复制分享链接
  分享链接唤醒与定位
  托盘状态 / 登出 / 诊断

Explorer 负责：
  浏览目录
  打开文件
  复制 / 移动 / 删除 / 重命名
  拖拽
  同名覆盖提示
  文件缩略图
  系统右键菜单
```

明确不做：

- 不做虚拟盘。
- 不做 Cloud Files Provider。
- 不做同步盘。
- 不做占位文件。
- 不做自定义 Explorer 左侧命名空间入口作为第一阶段目标。

这个方向的目标不是让 Windows 端消失，而是让 Windows 端把重心从“模拟 Explorer”改成“接入 Explorer”。

## 目标体验

用户打开 RYNAT 后先进入登录页。登录成功后，RYNAT 将 SMB 访问能力交给 Windows 系统，并自动打开 Explorer 到共享根或上次目录。

示例：

```text
\\192.168.102.136
\\192.168.102.136\共享名
\\192.168.102.136\共享名\目录
```

之后用户在 Explorer 中完成日常文件操作：

- 双击打开。
- 拖拽上传 / 下载 / 移动。
- 复制、剪切、粘贴。
- 删除、重命名、新建文件夹。
- 使用系统缩略图和默认打开方式。

RYNAT 主程序留在后台或托盘中，提供：

- 当前连接状态。
- 退出登录。
- 切换服务器。
- 重新打开 Explorer。
- Explorer 右键复制分享链接。
- 链接唤醒处理。

## 核心功能边界

### 1. 登录与服务器管理

登录页继续由 RYNAT 提供。它负责：

- 服务器地址、用户名、密码输入。
- 记住密码 / 自动登录。
- 多 profile 管理。
- 登录成功后建立 Windows 可用的 SMB 访问状态。
- 登出时清理当前 profile 对应的连接状态。

重点要求：

- 不通过会闪命令窗口的 `net use` 方式接入。
- 不把 A 服务器密码带到 B 服务器。
- 同一服务器切换不同账号时，要处理 Windows SMB 凭据冲突。
- 退出登录时要释放或失效当前连接，避免旧连接残留。

### 2. 打开 Explorer

登录成功后，RYNAT 打开 Windows Explorer 到目标 UNC 路径。

第一阶段推荐打开共享根或服务器根：

```text
\\host
\\host\share
```

链接唤醒时可以打开到具体目录。若目标是文件，优先打开父目录并选中文件；如果选择文件不稳定，则退化为打开父目录。

### 3. 右键复制分享链接

Explorer 中选中文件或文件夹后，右键菜单出现：

```text
复制 RYNAT 分享链接
```

这是 Explorer-first MVP 的核心链路，不是后续增强。因为文件浏览交给 Explorer 后，复制分享链接也必须从 Explorer 里发起，否则“打开文件”和“分享文件”会被拆成两个产品体验。

推荐实现原则：

- 右键集成本身保持很薄。
- 它只负责拿到选中的本地 Shell 路径 / UNC 路径。
- 它把路径交给 RYNAT 主程序。
- 主程序负责识别 profile、解析 share/path、调用 core 生成链接、复制到剪贴板、展示结果通知。

这样可以降低 Explorer 稳定性风险。右键扩展不应该直接连接 SMB、不应该直接调用复杂业务、不应该持有长期状态。

第一版优先打通稳定闭环：

```text
Explorer 右键
  -> RYNAT context helper
  -> 唤醒 / 转发给已运行的 RYNAT 主程序
  -> 主程序解析 UNC 路径
  -> core 生成 HTTP 分享链接
  -> 复制到剪贴板并通知
```

后续如果需要更贴近 Windows 11 一级菜单体验，再评估更完整的 Explorer command / COM 集成。无论采用哪种 Shell 入口，业务逻辑都不进入 Explorer 进程。

## 技术栈判断

Explorer-first 以后，Windows 端不再是文件管理器 UI，而是登录、设置、托盘、链接和 Shell 集成轻壳。2026-06-30 决策：主程序采用：

```text
.NET 10 WinForms Tray Host + WebView2 Local UI
```

推荐理由：

- 产品实际形态是登录一次后最小化常驻，不需要完整跨平台应用壳。
- WinForms 负责托盘、单实例、注册表、Explorer 打开和本地监听，贴近 Windows 系统工具模型。
- WebView2 只负责登录 / 设置 / 状态等可见界面，避免纯 WinForms 控件的老旧观感。
- 构建环境压缩到 .NET 10 SDK + WebView2 Runtime，不再要求 Node/npm/Tauri/Rust/MSVC 参与主程序构建。
- 右键 helper 和链接监听仍保持薄集成，业务逻辑留在主程序。

Tauri、WPF 和旧 WinUI 文件管理器路线不再作为当前主程序路线。右键入口应按 Windows Shell 集成单独处理：薄 helper / shell verb / Explorer command 只负责取路径和转发，主程序负责生成链接。

### 4. 分享链接唤醒

分享链接被点击后，链路保持现有方向：

```text
HTTP 分享链接
  -> 本地中转 / rynat://
  -> RYNAT 单实例唤醒
  -> 解析链接
  -> 确认是否已登录对应服务器
  -> 打开 Explorer 到目录或文件位置
```

如果尚未登录：

- 展示登录页。
- 登录成功后继续消费待处理链接。

如果已登录但不是同一服务器：

- 提示切换服务器或重新登录。
- 不自动把当前凭据发给另一个服务器。

## 路径映射

Explorer 传回来的路径可能是：

```text
\\192.168.102.136\共享\目录\文件.txt
\\server-name\共享\目录\文件.txt
Z:\目录\文件.txt
```

RYNAT 需要把它映射为 core 需要的结构：

```text
serverHost
share
remotePath
kind
```

映射规则需要支持：

- IP 地址。
- DNS / NetBIOS 名称。
- 已知 profile 的 host 别名。
- 映射盘符到 UNC 的反查。
- 文件和文件夹类型判断。

无法识别路径时，不生成错误链接，应提示用户该路径不属于当前 RYNAT 连接。

## 稳定性判断

### 稳定性收益

这个方向的稳定性收益主要来自：文件操作交给 Explorer。

Explorer 已经处理：

- 大文件复制进度。
- 同名覆盖确认。
- 拖拽到桌面。
- 系统文件打开方式。
- 缩略图。
- 多选和键盘操作。
- 与其他 Windows 软件之间的 Shell 互操作。

因此 RYNAT 不再需要在自研 Windows 文件管理器内复刻这些复杂行为。

### 主要风险

主要风险转移到 Shell 集成和凭据管理：

1. Windows SMB 凭据冲突。
   同一 host 不能同时用多套凭据访问，切换账号时必须处理旧连接。

2. 右键扩展稳定性。
   Shell 集成如果做重，会影响 Explorer。必须采用薄扩展，把业务放回主程序。

3. 路径映射准确性。
   `\\host\share\path`、别名、盘符映射要能可靠反推 profile。

4. 权限和安装。
   右键菜单注册、协议注册、开机自启、托盘后台都要有明确安装 / 卸载策略。

5. 唤醒后的登录状态。
   链接指向的服务器和当前登录服务器可能不同，不能静默串用凭据。

## 分阶段建议

### Phase 1：Explorer-first MVP（当前下一步）

目标：

- 保留登录页和服务器设置。
- 登录成功后打开 Explorer 到服务器根或共享根。
- RYNAT 留在后台 / 托盘。
- 登出时清理当前连接状态。
- 链接唤醒后打开 Explorer 到目标目录。
- Explorer 右键“复制 RYNAT 分享链接”可用，支持文件和文件夹。
- 右键入口只转发路径，生成链接、复制剪贴板和通知由主程序完成。

暂不要求：

- 选中文件定位 100% 成功。
- 映射盘符支持。
- Windows 11 一级右键菜单的最终形态。

这一阶段验证完整核心闭环：登录后进入 Explorer、在 Explorer 中复制分享链接、点击分享链接再唤醒并定位回 Explorer。

### Phase 2：右键体验与路径覆盖增强

目标：

- 优化 Windows 11 右键菜单显示层级。
- 对 RYNAT 管理的 UNC 路径更精确地显示 / 隐藏右键入口。
- 支持更多路径来源：IP、主机名、别名、映射盘符。
- 优化多选、异常提示和通知文案。

重点：

- 右键扩展必须薄。
- 业务逻辑在 RYNAT 主程序。
- 无法识别路径时给出明确提示。

### Phase 3：完善唤醒与系统集成

目标：

- 链接唤醒后打开父目录并尽量选中文件。
- 支持从钉钉文档 / 聊天中点击 HTTP 分享链接唤醒。
- 支持本地中转页无法自动关闭时的替代说明或浏览器扩展备选方案。
- 支持托盘菜单：打开 Explorer、切换服务器、退出登录、诊断。
- 支持路径别名和映射盘符反查。

### 不进入本路线的 Phase

以下能力不属于本路线：

- Cloud Files Provider。
- OneDrive 式占位文件。
- 同步状态角标。
- 自定义虚拟盘。
- Explorer 左侧云盘入口。

如果未来产品需要这些能力，应另开 Cloud Files 方向评估，不和 Explorer-first 轻客户端混在同一阶段推进。

## 与旧 Windows 客户端的关系

旧 WPF 文件工作台和 Tauri 轻壳已从代码树移除。长期主体验从“RYNAT 自研文件管理器”转为“Windows Explorer 文件管理器 + RYNAT 后台服务”。

后续 Windows UI 投入不再放在 WPF 文件列表、预览面板、拖拽模拟上的深度打磨，把精力转到：

- SMB 凭据接入。
- Explorer 打开 / 定位。
- 右键菜单。
- 托盘后台。
- 链接唤醒。
- 安装 / 卸载 / 诊断。

## 决策建议

当前决策：

```text
当前：用 .NET 10 WinForms + WebView2 实现 Explorer-first MVP，包含登录、SMB / UNC 接入、打开 Explorer、右键复制分享链接、链接唤醒和托盘入口
长期：保持单一 Windows 主程序路线，避免 WPF / Tauri / WinUI 多线并行
```

先做 Phase 1 的完整闭环，但右键入口采用薄实现，不追求第一版就完成所有 Windows 11 Shell 细节。这样既不丢掉复制分享链接这个核心能力，也避免把复杂业务放进 Explorer 扩展里。
