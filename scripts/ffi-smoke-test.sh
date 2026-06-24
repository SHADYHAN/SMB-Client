#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LIB_PATH="$ROOT_DIR/target/release/librynat_core.dylib"
TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

"$ROOT_DIR/scripts/check-bridge-surface.sh"

cargo build -p rynat-core --release >/dev/null

cat >"$TMP_DIR/smoke.c" <<'EOF'
#include <string.h>
#include <stdio.h>
#include "rynat_core.h"

static int call_and_print(const char *name, char *(*fn)(const char *), const char *json, int expect_ok) {
    char *output = fn(json);
    if (output == NULL) {
        fprintf(stderr, "%s returned NULL\n", name);
        return 1;
    }
    printf("%s\n%s\n", name, output);
    int has_expected_status = expect_ok
        ? strstr(output, "\"ok\":true") != NULL
        : strstr(output, "\"ok\":false") != NULL;
    int failed = output[0] == '\0' || !has_expected_status;
    rynat_free_string(output);
    return failed;
}

int main(void) {
    int status = 0;
    status |= call_and_print(
        "generate",
        rynat_generate_link_json,
        "{\"server_host\":\"files.example\",\"share\":\"Team\",\"path\":\"/Project/file.pdf\",\"kind\":\"file\"}",
        1
    );
    status |= call_and_print(
        "activate",
        rynat_activate_link_json,
        "{\"raw_link\":\"rynat://s/AQENZmlsZXMuZXhhbXBsZQRUZWFtES9Qcm9qZWN0L2ZpbGUucGRm\"}",
        1
    );
    status |= call_and_print(
        "preview",
        rynat_preview_plan_json,
        "{\"server_host\":\"files.example\",\"share\":\"Team\",\"path\":\"/Project/image.jpg\",\"kind\":\"file\",\"max_edge_px\":512}",
        1
    );
    status |= call_and_print(
        "upload",
        rynat_upload_plan_json,
        "{\"local_path\":\"/Users/a/Desktop/file.pdf\",\"server_host\":\"files.example\",\"share\":\"Team\",\"remote_path\":\"/Project/file.pdf\"}",
        1
    );
    status |= call_and_print(
        "smb_list_without_login",
        rynat_smb_list_directory_json,
        "{\"share\":\"Media\",\"path\":\"/\"}",
        0
    );
    return status;
}
EOF

cc \
  "$TMP_DIR/smoke.c" \
  -I "$ROOT_DIR/include" \
  -L "$ROOT_DIR/target/release" \
  -lrynat_core \
  -Wl,-rpath,"$ROOT_DIR/target/release" \
  -o "$TMP_DIR/smoke"

"$TMP_DIR/smoke"
