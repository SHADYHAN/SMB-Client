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

This is the first WPF skeleton for the rebuilt Windows client. It includes:

- Login shell
- Share navigation area
- File list area
- Preview pane shell
- Status bar
- Rust core bridge reuse
- Initial services for bootstrap, SMB session, directory listing, links, preview, and shell drag/drop capability

Remaining feature migration should be added module by module rather than porting the old WinUI main window wholesale.
