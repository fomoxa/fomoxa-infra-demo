#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SECRETS_DIR="$REPO_ROOT/deploy/secrets"
DIR_SECRETS="$SECRETS_DIR/directory"

mkdir -p "$DIR_SECRETS"

FORCE="${1:-}"

gen_key() {
    local path="$1"
    local bytes="$2"
    if [[ -f "$path" && "$FORCE" != "-f" && "$FORCE" != "--force" ]]; then
        return 0
    fi
    mkdir -p "$(dirname "$path")"
    openssl rand -hex "$bytes" | tr -d '\n' > "$path"
    echo "  + $(basename "$(dirname "$path")")/$(basename "$path")"
}

echo "=== Generating keys for Fomoxa ==="
gen_key "$DIR_SECRETS/root" 32
gen_key "$DIR_SECRETS/admin" 24
gen_key "$DIR_SECRETS/backend" 24
gen_key "$DIR_SECRETS/gateway" 24
gen_key "$DIR_SECRETS/pod" 24
gen_key "$SECRETS_DIR/backend-pod" 24

# Nakama: key rieng thay cho defaultkey/defaulthttpkey, mat khau console va DB
NAKAMA_SECRETS="$SECRETS_DIR/nakama"
gen_key "$NAKAMA_SECRETS/server-key" 16
gen_key "$NAKAMA_SECRETS/http-key" 24
gen_key "$NAKAMA_SECRETS/console-password" 16
gen_key "$NAKAMA_SECRETS/session-key" 24
gen_key "$NAKAMA_SECRETS/refresh-key" 24
gen_key "$NAKAMA_SECRETS/db-password" 16

echo "Finished generating keys at $SECRETS_DIR"

