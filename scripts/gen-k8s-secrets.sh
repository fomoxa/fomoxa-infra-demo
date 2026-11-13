#!/usr/bin/env bash
set -euo pipefail

NAMESPACE="${1:-fomoxa}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SECRETS_DIR="$REPO_ROOT/deploy/secrets"

if [[ ! -f "$SECRETS_DIR/directory/root" ]]; then
    echo "[gen-k8s-secrets] No keys found, calling gen-keys.sh..."
    bash "$REPO_ROOT/scripts/gen-keys.sh"
fi

echo "[gen-k8s-secrets] Creating Kubernetes Secrets in namespace '$NAMESPACE'..."

kubectl create secret generic fomoxa-directory-keys \
    --namespace="$NAMESPACE" \
    --from-file=backend="$SECRETS_DIR/directory/backend" \
    --from-file=pod="$SECRETS_DIR/directory/pod" \
    --from-file=gateway="$SECRETS_DIR/directory/gateway" \
    --from-file=admin="$SECRETS_DIR/directory/admin" \
    --from-file=root="$SECRETS_DIR/directory/root" \
    --dry-run=client -o yaml | kubectl apply -f -

kubectl create secret generic fomoxa-backend-keys \
    --namespace="$NAMESPACE" \
    --from-file=backend="$SECRETS_DIR/directory/backend" \
    --from-file=backend-pod="$SECRETS_DIR/backend-pod" \
    --dry-run=client -o yaml | kubectl apply -f -

kubectl create secret generic fomoxa-pod-key \
    --namespace="$NAMESPACE" \
    --from-file=pod="$SECRETS_DIR/directory/pod" \
    --dry-run=client -o yaml | kubectl apply -f -

# Pod -> backend (PodGate.ReportRoom): chi key backend-pod, khong chua key directory cua backend.
kubectl create secret generic fomoxa-backend-pod-key \
    --namespace="$NAMESPACE" \
    --from-file=backend-pod="$SECRETS_DIR/backend-pod" \
    --dry-run=client -o yaml | kubectl apply -f -

# Nakama: server key, http key, console, ma hoa session va mat khau DB (thay cho cac gia tri mac dinh)
kubectl create secret generic fomoxa-nakama \
    --namespace="$NAMESPACE" \
    --from-file=server-key="$SECRETS_DIR/nakama/server-key" \
    --from-file=http-key="$SECRETS_DIR/nakama/http-key" \
    --from-file=console-password="$SECRETS_DIR/nakama/console-password" \
    --from-file=session-key="$SECRETS_DIR/nakama/session-key" \
    --from-file=refresh-key="$SECRETS_DIR/nakama/refresh-key" \
    --from-file=db-password="$SECRETS_DIR/nakama/db-password" \
    --dry-run=client -o yaml | kubectl apply -f -

# TLS cho cong 9643 (proxy TLS truoc backend)
if [[ ! -f "$SECRETS_DIR/tls/tls.crt" ]]; then
    bash "$REPO_ROOT/scripts/gen-tls-cert.sh" >/dev/null
fi
kubectl create secret tls fomoxa-tls \
    --namespace="$NAMESPACE" \
    --cert="$SECRETS_DIR/tls/tls.crt" \
    --key="$SECRETS_DIR/tls/tls.key" \
    --dry-run=client -o yaml | kubectl apply -f -

echo "[gen-k8s-secrets] Finished creating K8s secrets!"

