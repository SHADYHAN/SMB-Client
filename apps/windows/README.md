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
- Local file drop upload
- Shell drag-out download foundation using virtual file data
- Preview pane for image/video cache playback basics
- Fixed HTTP share link generation through Rust core
- Link activation plumbing: `rynat://`, local HTTP redirect helper, and single-instance forwarding

Remaining feature migration should be added module by module rather than porting the old WinUI main window wholesale.

## Remaining Work

Near-term items before broader testing:

- Verify the Windows build and runtime flow on a Windows machine.
- Test link activation from browser and document apps when the client is both running and closed.
- Add in-app copy, move, and paste for remote files.
- Add same-name conflict confirmation to copy/move flows.
- Refine internal drag visuals so dragging without a valid cross-directory target is visual only.
- Validate and refine shell drag-out visuals and same-name behavior on the Desktop.

Follow-up quality work:

- Improve preview performance with thumbnails/video first frames instead of caching large media when possible.
- Add cache cleanup policy for preview and drag cache files.
- Continue splitting `ShellViewModel` into smaller coordinators as file operations grow.
