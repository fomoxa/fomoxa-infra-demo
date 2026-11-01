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

echo "=== Sinh keys cho Fomoxa ==="
gen_key "$DIR_SECRETS/root" 32
gen_key "$DIR_SECRETS/admin" 24
gen_key "$DIR_SECRETS/backend" 24
gen_key "$DIR_SECRETS/gateway" 24
gen_key "$DIR_SECRETS/pod" 24
gen_key "$SECRETS_DIR/backend-pod" 24

echo "Hoan tat sinh keys tai $SECRETS_DIR"

