# RYNAT Windows Explorer-first Shell

This is the new Windows product line. It is intentionally not a file manager.

The shell owns:

- Login and connected state.
- SMB session setup for Windows Explorer.
- Opening Explorer to a UNC path.
- Local context IPC from Explorer right-click helper.
- Link activation to Explorer targets.
- Tray / diagnostics in later iterations.

Explorer owns:

- Browsing.
- Opening files.
- Copy / move / delete / rename.
- Thumbnails.
- System context menu behavior.

## Current Status

- `src-tauri` compiles and has tested commands for UNC link generation, Explorer target calculation, and registration preview generation.
- The frontend now uses a login page followed by a persistent two-column control shell for server status, sharing links, activation services, settings, shortcuts, and diagnostics.
- `../windows-context-helper` can parse `copy-link <path>` and send a local IPC request to the shell.
- The shell starts local context IPC on `127.0.0.1:19528` and local short-link redirect on `127.0.0.1:19527`.
- Short-link activation can already resolve to an Explorer open path / selected file target in tests.
- Real Explorer right-click registration and SMB session behavior still need Windows verification.

## Checks

```powershell
scripts\windows-shell\build-check.ps1
scripts\windows-shell\build-check.ps1 -Offline
scripts\windows-shell\build-check.ps1 -FullWorkspace
scripts\windows-shell\pull-build-check.bat
scripts\windows-shell\pull-build-check.bat -Offline
```

## Release Build

`build-check.ps1` only validates the Explorer-first code path. It does not produce installers.

Build a local release package on Windows:

```powershell
scripts\windows-shell\build-release.ps1
scripts\windows-shell\build-release.ps1 -SkipChecks
scripts\windows-shell\build-release.ps1 -NoClean
scripts\windows-shell\build-release.ps1 -CleanNodeModules
scripts\windows-shell\build-release.bat
```

The script builds the Tauri shell bundle, builds the Explorer context helper in release mode, and copies the useful outputs into:

```text
build\windows-shell-release\<yyyyMMdd-HHmmss>\
  installers\          Tauri .msi / NSIS .exe installers
  bin\                 app exe and rynat-windows-context-helper.exe
  registration-preview\ generated .reg preview files
```

The latest output directory is also written to:

```text
build\windows-shell-release\latest.txt
```

By default, the script removes the web `dist`, Tauri `src-tauri\target`, workspace `target`, and previous `build\windows-shell-release` outputs before rebuilding. Use `-NoClean` only when you intentionally want a faster incremental build.

Use `-CleanNodeModules` only when npm dependencies look corrupted. It removes `apps\windows-shell\node_modules`, so the next build must reinstall frontend dependencies.

Generate registry preview files:

```powershell
scripts\windows-shell\write-registration-preview.ps1 `
  -ExecutablePath "C:\Program Files\RYNAT\RYNAT.exe" `
  -HelperPath "C:\Program Files\RYNAT\rynat-windows-context-helper.exe"
```

Review generated `.reg` files before importing them.
