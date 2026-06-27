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
- `../windows-context-helper` can parse `copy-link <path>` and send a local IPC request to the shell.
- The shell starts local context IPC on `127.0.0.1:19528` and local short-link redirect on `127.0.0.1:19527`.
- Short-link activation can already resolve to an Explorer open path / selected file target in tests.
- Real Explorer right-click registration and SMB session behavior still need Windows verification.

## Checks

```powershell
scripts\windows-shell\build-check.ps1
scripts\windows-shell\build-check.ps1 -Offline
scripts\windows-shell\pull-build-check.bat
scripts\windows-shell\pull-build-check.bat -Offline
```

Generate registry preview files:

```powershell
scripts\windows-shell\write-registration-preview.ps1 `
  -ExecutablePath "C:\Program Files\RYNAT\RYNAT.exe" `
  -HelperPath "C:\Program Files\RYNAT\rynat-windows-context-helper.exe"
```

Review generated `.reg` files before importing them.
