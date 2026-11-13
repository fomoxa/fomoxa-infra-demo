#!/usr/bin/env bash
set -uo pipefail

echo "=== Stopping Fomoxa cluster processes ==="

NAMES=("fomoxa-directory" "fomoxa-gateway" "MyGame.Server" "MyGame.Backend" "MyGame")

for name in "${NAMES[@]}"; do
    if pgrep -f "$name" >/dev/null 2>&1; then
        echo "Stopping $name..."
        pkill -9 -f "$name" 2>/dev/null || true
    fi
done

# Neu tren Windows Host (interop)
if command -v taskkill.exe >/dev/null 2>&1; then
    for exe in "fomoxa-directory.exe" "fomoxa-gateway.exe" "MyGame.Server.exe" "MyGame.Backend.exe" "MyGame.exe"; do
        taskkill.exe /f /im "$exe" 2>/dev/null || true
    done
fi

echo "All processes stopped."

