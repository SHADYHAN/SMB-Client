# RYNAT Windows Client

This is the active Windows client line.

## Direction

- UI framework: WPF
- Shared core: Rust `rynat-core`
- Core bridge: `CoreAdapter`
- App flow: `Services`
- Windows-specific integration: `Platform`
- Presentation: small WPF Views and ViewModels under `UI`

The former WinUI 3 implementation is kept under `apps/windows-winui-legacy` as a reference only. New Windows client work should happen here.

## Architecture Rules

- Do not put SMB, preview, link, drag/drop, or file operation logic into `MainWindow.xaml.cs`.
- Do not create a giant all-purpose ViewModel.
- UI ViewModels can coordinate screen state, but business operations go through services.
- Services must not depend on WPF controls or view models.
- Windows Shell behavior belongs under `Platform`.
- Rust FFI details stay inside `CoreAdapter`.

## Current Stage

This is the active WPF client for the rebuilt Windows line. It currently includes:

- Login and saved-server bootstrap
- Server settings dialog
- SMB session connection and share navigation
- Explorer-style navigation tree basics
- File list with refresh, search, rename, delete, create folder, and copy link
- Multi-select remote cut/copy/paste with same-name overwrite confirmation
- Local file drop upload
- In-app remote drag/drop to directory targets for move, or copy when Ctrl is held / crossing shares
- Shell drag-out download foundation using virtual file data
- Preview pane for image/video cache playback basics
- Fixed HTTP share link generation through Rust core
- Link activation plumbing: `rynat://`, local HTTP redirect helper, and single-instance forwarding

Remaining feature migration should be added module by module rather than porting the old WinUI main window wholesale.

## Completed Highlights

- Windows build and runtime flow has been validated through the one-click script.
- Link activation works through `rynat://`, local HTTP redirect, single-instance forwarding, and foreground activation.
- Compact `/s/<code>` quick links are supported.
- Local short-link pages now close through an already-activated page instead of reopening the protocol.
- WPF smoke checks now target the current `CoreAdapter` / `Services` / `Platform` / `UI` layout instead of the old WinUI app-services tree.
- Cross-platform WPF static smoke checks cover startup arguments, local redirect, protocol registration, and single-instance forwarding.
- Remote copy/move logic is isolated in `RemoteCopyMoveService`, and shell paste state is isolated in `RemoteClipboardCoordinator`.
- Remote cut/copy/paste supports extended multi-selection in the file list.
- Internal remote drag/drop publishes a Windows-local payload and commits through `FileDragDropCoordinator`.
- Link startup parsing, pending activation, and session matching are isolated in `LinkActivationCoordinator`.
- Directory loading, current path/share state, and navigation-tree selection are isolated in `DirectoryNavigationCoordinator`.
- File selection preview loading is isolated in `PreviewCoordinator`.
- File drag-out and local drop upload coordination are isolated in `FileDragDropCoordinator`.
- Preview and drag caches now have age/size cleanup with stale `.part` removal; large videos no longer auto-cache for inline preview.

## Remaining Work

Near-term product work:

- Validate and refine in-app remote copy, move, paste, and drag/drop on real Windows SMB shares.
- Refine internal drag visuals and hover feedback so dragging without a valid cross-directory target remains visual only.
- Validate and refine shell drag-out visuals and same-name behavior on the Desktop.
- Decide whether Windows needs the same favorites/quick-link library UI as macOS.

Follow-up quality work:

- Improve preview performance with thumbnails/video first frames; large videos already avoid automatic inline caching.
- Observe preview/drag cache cleanup behavior during Windows real-use testing.
- Keep `ShellViewModel` focused on composition and command routing; continue extracting login/server-setting coordination only if it starts to grow.
- Keep broadening smoke checks when new Windows activation or Shell integration paths are added.
