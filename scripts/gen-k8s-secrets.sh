#!/usr/bin/env bash
set -euo pipefail

NAMESPACE="${1:-fomoxa}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SECRETS_DIR="$REPO_ROOT/deploy/secrets"

if [[ ! -f "$SECRETS_DIR/directory/root" ]]; then
    echo "[gen-k8s-secrets] Chua co keys, goi gen-keys.sh..."
    bash "$REPO_ROOT/scripts/gen-keys.sh"
fi

echo "[gen-k8s-secrets] Tao Kubernetes Secrets trong namespace '$NAMESPACE'..."

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

echo "[gen-k8s-secrets] Hoan tat tao K8s secrets!"

