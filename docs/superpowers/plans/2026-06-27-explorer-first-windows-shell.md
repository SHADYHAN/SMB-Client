# Explorer-first Windows Shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the new Windows Explorer-first client line without extending the archived WPF file manager.

**Architecture:** Add a small Rust support crate for Windows Shell path handling, SMB session setup contracts, Explorer launch targets, and context-menu request parsing. Use a Tauri 2 shell for login/status/settings UI and a thin native context helper for Explorer right-click copy-link entry; business logic remains in the main app and `rynat-core`.

**Tech Stack:** Rust 2024 workspace, `rynat-core`, Tauri 2 shell, Web UI, Windows Win32 networking APIs for SMB sessions, Windows Shell context entry/helper for right-click activation.

---

## File Structure

- `crates/rynat-windows-shell-support/` — reusable Rust support library shared by the Tauri shell and context helper.
- `apps/windows-shell/` — new Tauri 2 main app for login, connected state, tray, protocol activation, and Explorer launch.
- `apps/windows-context-helper/` — thin helper executable invoked by Explorer context-menu registration.
- `docs/explorer-first-windows-client.md` — active product and architecture direction.
- `README.md` and `apps/windows/README.md` — route contributors away from WPF UI work.

## Task 1: Support Crate Skeleton And UNC Mapping

**Files:**
- Create: `crates/rynat-windows-shell-support/Cargo.toml`
- Create: `crates/rynat-windows-shell-support/src/lib.rs`
- Create: `crates/rynat-windows-shell-support/src/unc_path.rs`
- Create: `crates/rynat-windows-shell-support/src/explorer.rs`
- Modify: `Cargo.toml`

- [ ] **Step 1: Add crate to the workspace**

Add `crates/rynat-windows-shell-support` to the workspace members and depend on `rynat-core`.

- [ ] **Step 2: Implement UNC parsing**

`UncPath::parse` must accept `\\host\share\dir\file.txt`, normalize slashes, lowercase host for matching, and expose a core `QuickLinkTarget`.

- [ ] **Step 3: Implement Explorer target generation**

`ExplorerTarget::from_link_target` must map directories to `\\host\share\path` and files to parent folder plus selected item path.

- [ ] **Step 4: Test UNC mapping**

Run: `cargo test -p rynat-windows-shell-support`

Expected: tests pass for file and directory targets.

## Task 2: Context Helper Contract

**Files:**
- Create: `crates/rynat-windows-shell-support/src/context_request.rs`
- Modify: `crates/rynat-windows-shell-support/src/lib.rs`
- Create: `apps/windows-context-helper/Cargo.toml`
- Create: `apps/windows-context-helper/src/main.rs`
- Modify: `Cargo.toml`

- [ ] **Step 1: Define context command format**

The helper accepts `copy-link <path>` and serializes `{ "action": "copy_link", "path": "..." }` to the main app activation channel.

- [ ] **Step 2: Implement parser tests**

Run: `cargo test -p rynat-windows-shell-support context_request`

Expected: valid `copy-link` requests parse, missing path fails with a clear message.

- [ ] **Step 3: Add helper executable**

The first helper version prints the normalized request JSON. Later tasks replace stdout with IPC/deep-link handoff.

## Task 3: Windows SMB Session Adapter

**Files:**
- Create: `crates/rynat-windows-shell-support/src/smb_session.rs`
- Modify: `crates/rynat-windows-shell-support/src/lib.rs`

- [ ] **Step 1: Define cross-platform trait**

Define `SmbSessionConnector` with `connect`, `disconnect`, and `unc_root` methods. Non-Windows returns a clear unsupported error.

- [ ] **Step 2: Add Windows API implementation boundary**

On Windows, implement `WindowsSmbSessionConnector` with the Win32 `WNetAddConnection2W` / `WNetCancelConnection2W` boundary. Keep credentials out of logs and error messages.

- [ ] **Step 3: Add non-Windows tests for formatting**

Run: `cargo test -p rynat-windows-shell-support smb_session`

Expected: UNC roots are formatted without needing Windows APIs.

## Task 4: Tauri Shell Scaffold

**Files:**
- Create: `apps/windows-shell/package.json`
- Create: `apps/windows-shell/index.html`
- Create: `apps/windows-shell/src/main.ts`
- Create: `apps/windows-shell/src/styles.css`
- Create: `apps/windows-shell/src-tauri/Cargo.toml`
- Create: `apps/windows-shell/src-tauri/tauri.conf.json`
- Create: `apps/windows-shell/src-tauri/src/lib.rs`
- Create: `apps/windows-shell/src-tauri/src/main.rs`

- [ ] **Step 1: Add Tauri project shell**

Create a minimal Tauri 2 app with commands for `get_bootstrap_state`, `connect_profile`, `open_explorer`, and `copy_link_for_unc_path`.

- [ ] **Step 2: Add refined lightweight UI**

Build a simple login/connected layout using Web UI: one focused window, calm neutral palette, clear server state, primary “打开资源管理器” action, and diagnostics area.

- [ ] **Step 3: Wire commands to support crate**

Commands can return mocked data at first, but path parsing and Explorer target generation must use `rynat-windows-shell-support`.

## Task 5: Docs And Verification

**Files:**
- Modify: `README.md`
- Modify: `docs/explorer-first-windows-client.md`
- Modify: `apps/windows/README.md`

- [ ] **Step 1: Document new directories**

Add `apps/windows-shell`, `apps/windows-context-helper`, and `crates/rynat-windows-shell-support`.

- [ ] **Step 2: Verify workspace**

Run: `cargo fmt --check`

Expected: no formatting changes needed.

Run: `cargo test -p rynat-windows-shell-support`

Expected: support crate tests pass.

Run if Tauri dependencies are available: `cargo check --manifest-path apps/windows-shell/src-tauri/Cargo.toml`

Expected: shell compiles. If dependency fetch is unavailable, record that Windows/Tauri dependency verification remains for the Windows machine.

## Self-Review

- The plan covers the core MVP boundary: login shell, Explorer launch, right-click copy-link entry, and link path mapping.
- The plan intentionally does not extend WPF file-list, preview pane, or drag/drop behavior.
- The first implementation slice is testable without Windows Explorer by focusing on path mapping and command contracts.
