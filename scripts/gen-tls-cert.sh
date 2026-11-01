#!/usr/bin/env bash
# Sinh chung chi TLS tu ky cho proxy TLS truoc backend (cong 9643) va in van tay SHA-256 de client "pin".
# Production nen dung chung chi do CA cong cong cap (cert-manager / Let's Encrypt) thay cho file nay;
# khi do client khong can pin (de trong Tls Pin Sha256).
#   TLS_HOST=game.example.com ./scripts/gen-tls-cert.sh
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DIR="$REPO_ROOT/deploy/secrets/tls"
HOST="${TLS_HOST:-fomoxa.local}"
mkdir -p "$DIR"

if [[ -f "$DIR/tls.crt" && "${1:-}" != "-f" ]]; then
    echo "Da co chung chi o $DIR (them -f de tao lai)."
else
    openssl req -x509 -newkey rsa:2048 -nodes -days 730 \
        -keyout "$DIR/tls.key" -out "$DIR/tls.crt" \
        -subj "/CN=$HOST" \
        -addext "subjectAltName=DNS:$HOST,DNS:localhost,IP:127.0.0.1" \
        -addext "extendedKeyUsage=serverAuth" 2>/dev/null
    chmod 600 "$DIR/tls.key"
    echo "Da tao $DIR/tls.crt va tls.key (CN=$HOST)"
fi

PIN="$(openssl x509 -in "$DIR/tls.crt" -outform DER | openssl dgst -sha256 -hex | awk '{print toupper($NF)}')"
echo "Van tay SHA-256 (pin): $PIN"
echo "Client Unity: -backend:127.0.0.1:9643 -tlspin:$PIN"
