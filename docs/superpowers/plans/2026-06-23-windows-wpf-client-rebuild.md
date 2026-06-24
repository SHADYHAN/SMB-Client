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
- [x] Add compact `/s/<code>` link support and local browser tab close page for already-activated links.
- [x] Add basic image/video preview cache and playback panel.
- [x] Add virtual-file drag-out foundation for downloading files to Explorer/Desktop.
- [x] Add one-click Windows pull/build/run scripts.

## Current Validation

- `cargo test -p rynat-core` passes locally: 79 tests.
- Windows build was confirmed by the user after the build script was simplified.
- Link activation now brings the client to the foreground in the tested cases.
- Local short-link browser tabs now return an already-activated close page instead of reopening `rynat://`.

## Remaining Product Work

- [ ] Remote copy / move / paste inside the client.
- [ ] Same-name conflict confirmation for remote copy / move flows.
- [ ] In-app drag/drop for remote move/copy: dragging without a valid cross-directory target should be visual only.
- [ ] Further refine Explorer/Desktop drag-out visuals and same-name overwrite behavior on real Windows.
- [ ] Improve preview performance: avoid caching large videos when a lightweight thumbnail/first-frame path is available.
- [ ] Add preview/drag cache cleanup policy.
- [ ] Add favorites/quick-link library UI if Windows needs parity with macOS favorites.
- [ ] Add broader keyboard shortcuts and selection polish after real-user testing.

## Remaining Architecture Work

- [ ] Continue splitting `UI/Shell/ShellViewModel.cs` as features grow. It is much smaller than the old WinUI ViewModel, but at about 800 lines it is still the next pressure point.
- [ ] Consider extracting dedicated coordinators/use cases for link activation, directory navigation, preview loading, and file drag/drop.
- [ ] Keep `MainWindow.xaml.cs` thin and prevent Shell/platform logic from leaking into UI code.
- [ ] Add Windows-side automated smoke checks for bridge surface, startup parsing, and link activation helpers.
- [ ] Revisit Cloud Files API only if the product later needs Explorer-level virtual drive / placeholder-file behavior.

## Reference Boundaries

The old WinUI 3 code can be used to understand previous behavior, but should not be copied into the new WPF line wholesale. Avoid reintroducing:

- Giant all-purpose window or ViewModel classes.
- UI-control types in service layer inputs or domain models.
- WinUI `DataPackage`-specific drag/drop assumptions.
- File operation logic in view code.
