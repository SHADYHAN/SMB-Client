# FFI Smoke Test

This directory contains lightweight cross-platform FFI smoke-test helpers.

The goal is not to launch the full desktop clients. The goal is to validate that the exported JSON bridge surface from `rynat-core` still behaves correctly for a small set of stable calls.

## macOS

Run:

```bash
scripts/ffi-smoke-test.sh
```

## Windows

Run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\ffi-smoke-test-windows.ps1 -CoreLibraryPath C:\path\to\rynat_core.dll
```

The Windows smoke test uses the C# bridge layer, so it validates:

- `rynat_core.dll` loading via P/Invoke
- bridge request and response DTO alignment
- JSON round-trip behavior for stable bridge calls
- bridge error wrapping for a controlled SMB failure path

If `rynat_core.dll` is not present yet, the script will stop and tell you which paths it expected.
