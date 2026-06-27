# RYNAT Windows WPF Fallback Client

This WPF client is now archived as a fallback / reference implementation.

It remains in `apps/windows` so existing build scripts keep working and so the project retains a known-good native Windows implementation for debugging, regression checks, and reuse of login / link / platform-adapter code. New Windows product work should move to the Explorer-first direction documented in `docs/explorer-first-windows-client.md`.

## Direction

- UI framework: WPF
- Shared core: Rust `rynat-core`
- Core bridge: `CoreAdapter`
- App flow: `Services`
- Windows-specific integration: `Platform`
- Presentation: small WPF Views and ViewModels under `UI`

The former WinUI 3 implementation is kept under `apps/windows-winui-legacy` as a historical reference only. This WPF line is the fallback client, not the next primary Windows experience.

Windows primary direction after archive:

- RYNAT handles login, server settings, SMB access setup, share-link generation, protocol activation, tray / diagnostics.
- Windows Explorer handles browsing, opening, copying, moving, deleting, renaming, thumbnails, drag/drop, and system context menus.
- The WPF client should receive only blocker fixes, security fixes, and small reuse-oriented adjustments.

## Architecture Rules

- Do not put SMB, preview, link, drag/drop, or file operation logic into `MainWindow.xaml.cs`.
- Do not create a giant all-purpose ViewModel.
- UI ViewModels can coordinate screen state, but business operations go through services.
- Services must not depend on WPF controls or view models.
- Windows Shell behavior belongs under `Platform`.
- Rust FFI details stay inside `CoreAdapter`.

## Archive Stage

This WPF client reached a usable internal validation point and is now preserved as fallback. It currently includes:

- Login and saved-server bootstrap
- Server settings dialog
- SMB session connection and share navigation
- Login lands on an "全部共享" virtual root in the content pane, matching the macOS share-root directory model.
- Explorer-style navigation tree basics
- Favorites tab in the left navigation, backed by stored quick links
- File list with refresh, search, rename, delete, create folder, and copy link
- Multi-select remote cut/copy/paste with same-name overwrite confirmation
- Local file drop upload
- In-app remote drag/drop to directory targets for move, or copy when Ctrl is held / crossing shares
- Shell drag-out download foundation using virtual file data
- Preview pane for image/video cache playback basics
- Fixed HTTP share link generation through Rust core
- Link activation plumbing: `rynat://`, local HTTP redirect helper, and single-instance forwarding

No further large UI redesign or Explorer-replication work should be added here. If a future fix is needed, keep it narrow and avoid expanding WPF file-manager responsibilities.

## Completed Highlights

- Windows build and runtime flow has been validated through the one-click script.
- Link activation works through `rynat://`, local HTTP redirect, single-instance forwarding, and foreground activation.
- Compact `/s/<code>` quick links are supported.
- Windows copy-link keeps only the document-friendly HTTP share link so DingTalk/chat/docs do not rewrite `rynat://` hrefs into broken web links.
- Local short-link requests return an already-activated close page after activation instead of reopening the protocol.
- WPF smoke checks now target the current `CoreAdapter` / `Services` / `Platform` / `UI` layout instead of the old WinUI app-services tree.
- Cross-platform WPF static smoke checks cover startup arguments, local redirect, protocol registration, and single-instance forwarding.
- Remote copy/move/paste remains isolated in dormant services, but the main WPF workbench no longer exposes it as a primary interaction.
- Explicit “下载到...” is available from the file-list context menu for selected files.
- File-list navigation polish includes an 上级 toolbar button, a visible preview toggle, a current-location line, Ctrl+A for selecting visible rows, Esc for leaving search / clearing selection, and Alt+Up / Backspace for parent-directory navigation.
- Directory loading has a bounded SMB list wait and duplicate in-flight load feedback; navigation-tree double-click toggles expansion locally.
- Login preserves the remember-password choice when choosing a profile without stored credentials, and typed passwords are saved instead of only updating credential options.
- Favorites can be added from the current item, opened from the sidebar, and removed without changing document-friendly copy-link behavior; the favorites list supports Enter to open and Delete to remove.
- Internal remote drag/drop has been disabled in the main UI; local drag-in upload remains the supported drag/drop path.
- Upload and explicit download report lightweight item-level progress in the status bar.
- Link startup parsing, pending activation, and session matching are isolated in `LinkActivationCoordinator`.
- Login, auto-login, credential option persistence, and server settings are isolated in `LoginCoordinator`.
- Directory loading, virtual share-root display, current path/share state, and navigation-tree selection are isolated in `DirectoryNavigationCoordinator`.
- File selection preview loading is isolated in `PreviewCoordinator`.
- Image previews now cache a lightweight local thumbnail; video previews use a Windows Shell poster before playback; oversized images/videos skip automatic preview caching.
- File drag-out and local drop upload coordination are isolated in `FileDragDropCoordinator`.
- Explorer/Desktop drag-out advertises a local copy effect only; remote move/copy should use explicit file operations if it is reintroduced later.
- Preview and drag caches now have age/size cleanup with stale `.part` removal; oversized media no longer auto-caches for inline preview.

## Archived Scope

Keep only:

- Build blockers.
- Login / credential safety fixes.
- Link generation / activation fixes that are reusable by Explorer-first.
- Platform adapter fixes that can be moved into the Explorer-first client.
- Documentation updates that prevent this line from being mistaken for the active Windows product direction.

Do not continue:

- WPF visual polish as a primary goal.
- In-app Explorer replication.
- Internal remote drag/drop expansion.
- Virtual-file drag-out refinement beyond blocker fixes.
- Remote copy / move / paste UX expansion.

The next Windows implementation should begin with Explorer-first Phase 1: login, SMB / UNC access setup, open Explorer, Explorer context-menu copy link, protocol activation to Explorer location, and a minimal tray / diagnostic shell. The new shell may use Tauri 2 + Rust backend + Web UI; this WPF line remains fallback / reference only.
