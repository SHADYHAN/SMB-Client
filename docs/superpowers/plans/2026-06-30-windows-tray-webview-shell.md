# Windows Tray WebView Shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Explorer-first main shell candidate with a .NET 10 WinForms tray host that uses WebView2 only for the visible login/settings UI.

**Architecture:** Keep Windows Explorer as the file manager. The .NET tray host owns login state, tray lifetime, local link redirect, Explorer launch, and context-helper IPC. WebView2 renders a local HTML/CSS UI and talks to the host through `postMessage`; no Node/npm/Tauri/Rust shell build chain is required for the main app.

**Tech Stack:** .NET 10, WinForms, WebView2, local HTTP listener, Windows Explorer process launch, static Explorer shell verb helper integration.

---

## File Structure

- `apps/windows-tray/Rynat.WindowsTray.csproj` — .NET 10 WinForms entry project.
- `apps/windows-tray/Program.cs` — startup, visual styles, and application context bootstrap.
- `apps/windows-tray/App/TrayApplicationContext.cs` — owns tray icon, main window lifetime, local services, and shutdown.
- `apps/windows-tray/App/ShellState.cs` — runtime state sent to the WebView UI.
- `apps/windows-tray/UI/ShellWindow.cs` — WinForms host window for WebView2.
- `apps/windows-tray/UI/WebAssets/index.html` — local app UI markup.
- `apps/windows-tray/UI/WebAssets/styles.css` — modern lightweight visual system.
- `apps/windows-tray/UI/WebAssets/app.js` — WebView command bridge and UI state rendering.
- `apps/windows-tray/Services/ExplorerService.cs` — opens Explorer to UNC paths and optionally selects files.
- `apps/windows-tray/Services/LocalRedirectService.cs` — listens on `127.0.0.1:19527` and translates short links into Explorer activations.
- `apps/windows-tray/Services/ContextIpcService.cs` — listens on `127.0.0.1:19528` for context helper copy-link requests.
- `apps/windows-tray/Services/ShareLinkService.cs` — creates deterministic local HTTP links for UNC file/folder paths.
- `apps/windows-tray/Services/SmbSessionService.cs` — first-pass Windows SMB session boundary; MVP stores connected state and opens Explorer, real credential cleanup remains explicit follow-up.
- `scripts/windows-tray/build-check.ps1` — restores and builds the .NET tray project.
- `scripts/windows-tray/build-check.bat` — double-click friendly wrapper using PowerShell 7.
- `scripts/windows-tray/build-release.ps1` — publishes self-contained `win-x64` output.
- `scripts/windows-tray/build-release.bat` — one-click release wrapper.

## Task 1: Project And Host Skeleton

**Files:**
- Create: `apps/windows-tray/Rynat.WindowsTray.csproj`
- Create: `apps/windows-tray/Program.cs`
- Create: `apps/windows-tray/App/TrayApplicationContext.cs`
- Create: `apps/windows-tray/App/ShellState.cs`

- [ ] **Step 1: Create .NET 10 WinForms project**

Use `net10.0-windows`, `UseWindowsForms`, nullable, implicit usings, and the existing `RynatApp.ico` copied from the WPF fallback assets.

- [ ] **Step 2: Add tray application context**

The app starts in a tray-capable context, shows the login/status window on startup, and exits cleanly from the tray menu.

- [ ] **Step 3: Add runtime state model**

Track server host, username, connected flag, status text, redirect service status, context IPC status, and last activation.

## Task 2: WebView2 Local UI

**Files:**
- Create: `apps/windows-tray/UI/ShellWindow.cs`
- Create: `apps/windows-tray/UI/WebAssets/index.html`
- Create: `apps/windows-tray/UI/WebAssets/styles.css`
- Create: `apps/windows-tray/UI/WebAssets/app.js`

- [ ] **Step 1: Add WebView2 host window**

The window loads local `index.html`, disables default browser chrome, and bridges messages from JavaScript to C#.

- [ ] **Step 2: Add modern lightweight UI**

The visible UI has a login view and a connected dashboard view. Keep it sparse: server, username, password, remember password, status, open Explorer, copy test link, diagnostics.

- [ ] **Step 3: Add host commands**

Support JavaScript commands: `getState`, `connect`, `disconnect`, `openExplorer`, `copyTestLink`, and `hideWindow`.

## Task 3: Explorer And Link Services

**Files:**
- Create: `apps/windows-tray/Services/ExplorerService.cs`
- Create: `apps/windows-tray/Services/ShareLinkService.cs`
- Create: `apps/windows-tray/Services/LocalRedirectService.cs`
- Create: `apps/windows-tray/Services/ContextIpcService.cs`

- [ ] **Step 1: Implement Explorer launch**

Open `explorer.exe` to `\\host` after login. For file targets, prefer `/select,<path>`; for directories, open the directory.

- [ ] **Step 2: Implement deterministic test links**

Use `http://127.0.0.1:19527/s/<base64url-json>` for the first .NET tray skeleton, carrying host, path, and kind. Later integration can swap this to Rust core short-code generation.

- [ ] **Step 3: Implement local redirect**

Listen on `127.0.0.1:19527`, parse `/s/<code>`, open Explorer to the target path, and return a small browser page that attempts to close itself.

- [ ] **Step 4: Implement context IPC**

Listen on `127.0.0.1:19528/context`, accept the existing helper JSON request shape, create a share link, copy it to the Windows clipboard, and return JSON status.

## Task 4: Build Scripts And Docs

**Files:**
- Create: `scripts/windows-tray/build-check.ps1`
- Create: `scripts/windows-tray/build-check.bat`
- Create: `scripts/windows-tray/build-release.ps1`
- Create: `scripts/windows-tray/build-release.bat`
- Modify: `README.md`
- Modify: `docs/explorer-first-windows-client.md`
- Modify: `docs/windows-architecture-direction.md`

- [ ] **Step 1: Add check script**

Run `dotnet restore` and `dotnet build` for `apps/windows-tray/Rynat.WindowsTray.csproj`.

- [ ] **Step 2: Add release script**

Run `dotnet publish -c Release -r win-x64 --self-contained true`, copy output into `build/windows-tray-release/<timestamp>`, and write `latest.txt`.

- [ ] **Step 3: Update docs**

Document `apps/windows-tray` as the active Windows mainline. Mark Tauri as archived experiment/reference.

## Self-Review

- This plan preserves Explorer-first product scope: Windows Explorer owns file browsing and file operations.
- This plan removes Node/npm/Tauri/Rust shell build requirements from the main Windows client.
- This plan keeps WebView2 only as a small local UI layer so the client does not look like an old WinForms app.
- Right-click helper compatibility is preserved through the same local IPC port and request shape.
