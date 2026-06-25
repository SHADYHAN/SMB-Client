# Windows WPF Client Rebuild Status

**Goal:** Replace the old WinUI 3 Windows client line with a WPF-based native Windows client while keeping Rust `rynat-core` as the shared business layer.

**Current result:** The WPF client is now the active Windows client under `apps/windows`. The old WinUI 3 implementation is kept under `apps/windows-winui-legacy` for historical reference only.

## Completed

- [x] Move the previous WinUI 3 implementation to `apps/windows-winui-legacy`.
- [x] Create the new WPF project at `apps/windows/Rynat.WindowsClient.csproj`.
- [x] Add WPF application startup, app resources, `MainWindow`, and Windows assets.
- [x] Build Rust core automatically from the Windows project and copy `rynat_core.dll` beside the executable.
- [x] Split the Windows code into clear layers:
  - `CoreAdapter/` for P/Invoke, DTOs, JSON context, and bridge exceptions.
  - `Domain/` for app-level file, server, preview, and link models.
  - `Services/` for bootstrap, SMB session, directory browsing, file operations, transfers, links, previews, and server profiles.
  - `Platform/` for clipboard, dialogs, Shell drag/drop, foregrounding, protocol registration, local HTTP redirect, and single-instance activation.
  - `UI/` for WPF Views and ViewModels.
- [x] Add login, auto-login bootstrap, and server settings.
- [x] Add directory tree, file list, status bar, and preview pane regions.
- [x] Add basic file operations: refresh, create folder, rename, delete, upload by local drag/drop.
- [x] Add upload same-name confirmation before replacing.
- [x] Add fixed quick-link generation and clipboard copy.
- [x] Add link activation flow: `rynat://`, local HTTP redirect, single-instance forwarding, and foreground activation.
- [x] Add compact `/s/<code>` link support and local `204 No Content` acknowledgement for already-activated links.
- [x] Add basic image/video preview cache and playback panel.
- [x] Add virtual-file drag-out foundation for downloading files to Explorer/Desktop.
- [x] Add one-click Windows pull/build/run scripts.

## Current Validation

- `cargo test -p rynat-core` passes locally: 79 tests.
- Windows build was confirmed by the user after the build script was simplified.
- Link activation now brings the client to the foreground in the tested cases.
- Windows copy-link keeps the HTTP short link so DingTalk/chat/docs can recognize it as clickable.
- Local short-link browser requests now return `204 No Content` after activation instead of rendering a script-close page or reopening `rynat://`.
- Current macOS-side validation also covers `scripts/check-bridge-surface.sh`, `scripts/ffi-smoke-test.sh`, and `scripts/windows-app-service-smoke.sh`.
- Windows WPF has multi-select remote copy / move / paste plumbing with same-name confirmation, pending Windows SMB validation.
- Remote copy/move implementation is isolated in `RemoteCopyMoveService`; paste state and conflict flow are isolated in `RemoteClipboardCoordinator`.
- Link startup parsing, pending activation, and active-session matching are isolated in `LinkActivationCoordinator`.
- Directory loading, current path/share state, and navigation-tree synchronization are isolated in `DirectoryNavigationCoordinator`.
- File selection preview loading and stale-preview protection are isolated in `PreviewCoordinator`.
- File drag-out and local drop upload coordination are isolated in `FileDragDropCoordinator`.
- In-app remote drag/drop now uses a Windows-local payload and reuses `RemoteCopyMoveService` for directory-target copy/move.
- File list remote drag hover now highlights only valid directory drop targets and clears the visual state on invalid targets, leave, or drop.
- Preview and drag caches have age/size cleanup with stale `.part` removal.
- Large videos no longer auto-cache for inline preview; they show a lightweight message until a thumbnail/first-frame path is added.
- Cross-platform WPF static smoke checks cover startup arguments, local redirect, protocol registration, and single-instance forwarding.

## Remaining Product Work

- [ ] Validate and refine remote copy / move / paste / drag/drop on real Windows SMB shares.
- [ ] Validate and refine in-app drag/drop hover visuals on real Windows, including navigation-tree parity and cursor feedback.
- [ ] Further refine Explorer/Desktop drag-out visuals and same-name overwrite behavior on real Windows.
- [ ] Improve preview performance: add lightweight thumbnail/video first-frame generation instead of relying on full media cache.
- [ ] Observe preview/drag cache cleanup behavior during Windows real-use testing.
- [ ] Add favorites/quick-link library UI if Windows needs parity with macOS favorites.
- [ ] Add broader keyboard shortcuts and selection polish after real-user testing.

## Remaining Architecture Work

- [ ] Keep `UI/Shell/ShellViewModel.cs` focused on composition and command routing; split login/server-setting coordination later only if those areas grow.
- [ ] Keep heavier recursive/long-running file workflows out of `FileOperationService`; put them behind dedicated use-case services like `RemoteCopyMoveService`.
- [ ] Keep `MainWindow.xaml.cs` thin and prevent Shell/platform logic from leaking into UI code.
- [ ] Keep Windows-side smoke checks current as new activation or Shell integration paths are added.
- [ ] Revisit Cloud Files API only if the product later needs Explorer-level virtual drive / placeholder-file behavior.

## Reference Boundaries

The old WinUI 3 code can be used to understand previous behavior, but should not be copied into the new WPF line wholesale. Avoid reintroducing:

- Giant all-purpose window or ViewModel classes.
- UI-control types in service layer inputs or domain models.
- WinUI `DataPackage`-specific drag/drop assumptions.
- File operation logic in view code.
