#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BRIDGE_RS="$ROOT_DIR/crates/rynat-core/src/bridge.rs"
HEADER="$ROOT_DIR/include/rynat_core.h"
SWIFT_BRIDGE="$ROOT_DIR/apps/macos/RYNATClient/RynatCore.swift"
CS_NATIVE_METHODS="$ROOT_DIR/apps/windows/CoreAdapter/NativeMethods.cs"
TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

extract_rust_exports() {
    sed -En 's/.*pub (unsafe )?extern "C" fn (rynat_[a-z0-9_]+).*/\2/p' "$BRIDGE_RS"
}

extract_header_exports() {
    sed -En 's/^(char \*|void )[[:space:]]*(rynat_[a-z0-9_]+)\(.*/\2/p' "$HEADER"
}

extract_swift_exports() {
    sed -En 's/.*@_silgen_name\("(rynat_[a-z0-9_]+)"\).*/\1/p' "$SWIFT_BRIDGE"
}

extract_csharp_exports() {
    sed -En 's/.*internal static extern (IntPtr|void)[[:space:]]+(rynat_[a-z0-9_]+)\(.*/\2/p' "$CS_NATIVE_METHODS"
}

normalize_exports() {
    grep -v '^rynat_free_string$' | sort -u
}

compare_surfaces() {
    local name="$1"
    local expected="$2"
    local actual="$3"
    local missing="$TMP_DIR/${name// /_}.missing"
    local extra="$TMP_DIR/${name// /_}.extra"

    comm -23 "$expected" "$actual" >"$missing"
    comm -13 "$expected" "$actual" >"$extra"

    if [[ ! -s "$missing" && ! -s "$extra" ]]; then
        printf '%s: OK (%s symbols)\n' "$name" "$(wc -l <"$actual" | tr -d ' ')"
        return 0
    fi

    printf '%s: mismatch\n' "$name" >&2
    if [[ -s "$missing" ]]; then
        printf '  Missing:\n' >&2
        sed 's/^/    /' "$missing" >&2
    fi
    if [[ -s "$extra" ]]; then
        printf '  Extra:\n' >&2
        sed 's/^/    /' "$extra" >&2
    fi
    return 1
}

extract_rust_exports | normalize_exports >"$TMP_DIR/rust"
extract_header_exports | normalize_exports >"$TMP_DIR/header"
extract_swift_exports | normalize_exports >"$TMP_DIR/swift"
extract_csharp_exports | normalize_exports >"$TMP_DIR/csharp"

ok=0
compare_surfaces "Header vs Rust" "$TMP_DIR/rust" "$TMP_DIR/header" || ok=1
compare_surfaces "Swift bridge vs Rust" "$TMP_DIR/rust" "$TMP_DIR/swift" || ok=1
compare_surfaces "C# bridge vs Rust" "$TMP_DIR/rust" "$TMP_DIR/csharp" || ok=1

if [[ "$ok" -ne 0 ]]; then
    exit 1
fi

printf 'Bridge surface check passed.\n'
