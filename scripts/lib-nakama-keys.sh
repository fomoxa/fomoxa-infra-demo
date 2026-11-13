#!/usr/bin/env bash
# Nap vao bang `source`. Dat DEFAULT_SERVER_KEY / DEFAULT_HTTP_KEY:
#  - Nakama cua cum k3d (cong 7350 do container k3d-* giu): doc key rieng tu secret fomoxa-nakama
#  - Nakama cua docker compose (hoac khong doc duoc secret): defaultkey / defaulthttpkey
# Ep dung key mac dinh: NAKAMA_USE_DEFAULT_KEYS=1
DEFAULT_SERVER_KEY="defaultkey"
DEFAULT_HTTP_KEY="defaulthttpkey"

if [[ "${NAKAMA_USE_DEFAULT_KEYS:-}" != "1" ]] && command -v kubectl >/dev/null 2>&1; then
    owner=""
    if command -v docker >/dev/null 2>&1; then
        owner="$(docker ps --format '{{.Names}} {{.Ports}}' 2>/dev/null | grep -E '[:]7350->' | awk '{print $1}' || true)"
    fi
    if [[ -z "$owner" || "$owner" == k3d-* ]]; then
        server_key="$(kubectl get secret fomoxa-nakama -n fomoxa -o jsonpath='{.data.server-key}' 2>/dev/null | base64 -d 2>/dev/null || true)"
        http_key="$(kubectl get secret fomoxa-nakama -n fomoxa -o jsonpath='{.data.http-key}' 2>/dev/null | base64 -d 2>/dev/null || true)"
        if [[ -n "$server_key" && -n "$http_key" ]]; then
            DEFAULT_SERVER_KEY="$server_key"
            DEFAULT_HTTP_KEY="$http_key"
            echo "Using the K8s cluster's dedicated Nakama key (secret fomoxa-nakama)."
        fi
    fi
fi
