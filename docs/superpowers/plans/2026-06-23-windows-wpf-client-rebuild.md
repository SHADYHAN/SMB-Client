# Windows WPF Client Rebuild Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the WinUI 3 Windows client line with a WPF-based native Windows client architecture while preserving Rust core as the shared business layer.

**Architecture:** The old WinUI client is kept under `apps/windows-winui-legacy` for reference only. The new official Windows client lives at `apps/windows` and is split into Core Adapter, Domain, Services, Platform adapters, and small WPF Views/ViewModels. No single window, service, or ViewModel should become the place where all file-manager behavior accumulates.

**Tech Stack:** Rust `rynat-core`, C#/.NET 8, WPF, P/Invoke bridge, Windows Shell adapters.

---

### Task 1: Rename Existing WinUI Client

**Files:**
- Move: `apps/windows` to `apps/windows-winui-legacy`

- [x] **Step 1: Preserve the current WinUI implementation**

Run:

```bash
git mv apps/windows apps/windows-winui-legacy
```

Expected: Existing WinUI source and any uncommitted local edits move with the directory.

### Task 2: Create New WPF Project Skeleton

**Files:**
- Create: `apps/windows/Rynat.WindowsClient.csproj`
- Create: `apps/windows/App.xaml`
- Create: `apps/windows/App.xaml.cs`
- Create: `apps/windows/MainWindow.xaml`
- Create: `apps/windows/MainWindow.xaml.cs`

- [ ] **Step 1: Add a WPF project file**

The project targets `net8.0-windows`, enables WPF, builds `rynat-core`, and copies `rynat_core.dll` beside the executable.

- [ ] **Step 2: Add WPF application entry**

`App.xaml` owns app-level resources and creates services in `App.xaml.cs`.

- [ ] **Step 3: Add a thin MainWindow**

`MainWindow` should only bind to `ShellViewModel` and contain layout regions. Business flow stays outside the window.

### Task 3: Establish Layer Boundaries

**Files:**
- Create: `apps/windows/CoreAdapter/RynatCoreBridge.cs`
- Create: `apps/windows/Domain/*.cs`
- Create: `apps/windows/Services/**/*.cs`
- Create: `apps/windows/Platform/**/*.cs`
- Create: `apps/windows/UI/**/*.cs`

- [ ] **Step 1: Reuse the Rust bridge as a transitional Core Adapter**

Copy the old `RynatCoreBridge.cs` into `CoreAdapter`. Later tasks may split native methods, DTOs, and JSON context, but UI code must call services rather than the bridge directly.

- [ ] **Step 2: Add domain models**

Models such as `RemoteFileItem`, `RemoteDirectory`, and `ServerSession` must not depend on WPF controls, brushes, or visual state.

- [ ] **Step 3: Add service interfaces**

Directory browsing, login/session, links, preview, and shell drag/drop get separate interfaces and default implementations.

### Task 4: Build Explorer-Like UI Regions

**Files:**
- Create: `apps/windows/UI/Shell/ShellViewModel.cs`
- Create: `apps/windows/UI/Navigation/NavigationTreeView.xaml`
- Create: `apps/windows/UI/Files/FileListView.xaml`
- Create: `apps/windows/UI/Preview/PreviewPaneView.xaml`
- Create: `apps/windows/UI/Status/StatusBarView.xaml`

- [ ] **Step 1: Split the main screen into independent regions**

Navigation tree, file list, preview pane, and status bar each get their own View and ViewModel.

- [ ] **Step 2: Keep file-manager behavior explicit**

Single click selects/navigates where appropriate, double click expands/opens, drag visuals are simple, and shell-specific behavior is delegated to Platform adapters.

### Task 5: Static Verification

**Files:**
- Verify: `apps/windows`
- Verify: `apps/windows-winui-legacy`

- [ ] **Step 1: Check that the official path is WPF**

Run:

```bash
rg -n "<UseWinUI>|<UseWPF>|MainWindow" apps/windows apps/windows-winui-legacy
```

Expected: `apps/windows` uses WPF and `apps/windows-winui-legacy` contains the old WinUI client.

- [ ] **Step 2: Check for oversized new files**

Run:

```bash
find apps/windows -name '*.cs' -o -name '*.xaml' | xargs wc -l | sort -n
```

Expected: No newly created file starts as a giant all-in-one controller.
