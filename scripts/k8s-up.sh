#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

CLUSTER_NAME="fomoxa"

echo "=== Khoi dong cum Kubernetes (k3d + Agones) cho Fomoxa tren WSL ==="

# 1. Kiem tra docker
if ! docker info >/dev/null 2>&1; then
    echo "Loi: Docker daemon chua chay hoac chua cap quyen trong WSL." >&2
    exit 1
fi

# 2. Kiem tra k3d & helm
if ! command -v k3d >/dev/null 2>&1; then
    echo "k3d chua duoc cai dat. Dang cai dat k3d vao /usr/local/bin..."
    curl -s https://raw.githubusercontent.com/k3d-io/k3d/main/install.sh | TAG=v5.7.4 bash || {
        echo "Loi: Khong the tu dong cai k3d. Hay chay 'sudo apt install' hoac cai k3d thu cong." >&2
        exit 1
    }
fi

if ! command -v helm >/dev/null 2>&1; then
    echo "helm chua duoc cai dat. Dang cai dat helm..."
    curl -fsSL https://raw.githubusercontent.com/helm/helm/main/scripts/get-helm-3 | bash || {
        echo "Loi: Khong the tu dong cai helm." >&2
        exit 1
    }
fi

# 3. Tao cum k3d fomoxa
port_busy() {
    # true neu cong TCP/UDP dang duoc docker publish
    docker ps --format '{{.Names}} {{.Ports}}' | grep -E "[:]$1->" || true
}
if ! k3d cluster list "$CLUSTER_NAME" >/dev/null 2>&1; then
    for p in 7777 9600 7350; do
        busy="$(port_busy "$p")"
        if [[ -n "$busy" ]]; then
            echo "Loi: cong $p dang bi container khac giu:" >&2
            echo "$busy" >&2
            echo "Hay tat docker compose truoc: docker compose -f deploy/compose.yaml down" >&2
            exit 1
        fi
    done
    echo -e "\n1/6 Tao cum k3d '$CLUSTER_NAME' voi port mappings..."
    k3d cluster create "$CLUSTER_NAME" \
        --port "7777:7777/udp@loadbalancer" \
        --port "9600:9600@loadbalancer" \
        --port "7350:7350@loadbalancer" \
        --wait
else
    echo -e "\n1/6 Cum k3d '$CLUSTER_NAME' da ton tai."
fi

kubectl config use-context "k3d-$CLUSTER_NAME" >/dev/null
echo "Context hien tai: $(kubectl config current-context)"

# 4. Cai dat Agones qua Helm
echo -e "\n2/6 Cai dat Agones via Helm..."
# Dat AGONES_VERSION (vi du 1.47.0) de ghim version chart; de trong = ban moi nhat.
AGONES_VERSION="${AGONES_VERSION:-}"
helm repo add agones https://agones.dev/chart/stable --force-update
helm repo update agones

if ! helm list -n agones-system -q | grep -q "agones"; then
    # Project khong dung GameServerAllocation => tat allocator va ping.
    # Allocator la LoadBalancer cong 443, dung voi Traefik cua k3s tren cum 1 node.
    helm install agones agones/agones \
        ${AGONES_VERSION:+--version "$AGONES_VERSION"} \
        --namespace agones-system \
        --create-namespace \
        --set "agones.allocator.install=false" \
        --set "agones.ping.install=false" \
        --wait --timeout 5m
else
    echo "Agones da duoc cai dat trong agones-system."
fi

# 5. Build va Import Docker images vao k3d
BUILD="${1:-}"
if [[ "$BUILD" == "--build" || "$BUILD" == "-b" ]]; then
    echo -e "\n3/6 Build Docker images..."
    bash "$REPO_ROOT/scripts/build-images.sh"
fi

echo -e "\n4/6 Import images vao k3d cluster..."
k3d image import \
    fomoxa-server:latest \
    fomoxa-directory:latest \
    fomoxa-gateway:latest \
    fomoxa-backend:latest \
    -c "$CLUSTER_NAME"

# 6. Tao namespace va Secrets
echo -e "\n5/6 Tao namespace va secrets..."
kubectl apply -f "$REPO_ROOT/deploy/k8s/namespace.yaml"
bash "$REPO_ROOT/scripts/gen-k8s-secrets.sh" "fomoxa"

# 7. Apply K8s manifests
echo -e "\n6/6 Apply K8s manifests..."
kubectl apply -f "$REPO_ROOT/deploy/k8s/postgres.yaml"
kubectl apply -f "$REPO_ROOT/deploy/k8s/nakama.yaml"
kubectl apply -f "$REPO_ROOT/deploy/k8s/directory.yaml"
kubectl apply -f "$REPO_ROOT/deploy/k8s/gateway.yaml"
kubectl apply -f "$REPO_ROOT/deploy/k8s/backend.yaml"
kubectl apply -f "$REPO_ROOT/deploy/k8s/fleet.yaml"
# Ingress la thu nghiem (chua TLS), chi ap dung khi APPLY_INGRESS=1
if [[ "${APPLY_INGRESS:-}" == "1" && -f "$REPO_ROOT/deploy/k8s/ingress.yaml" ]]; then
    kubectl apply -f "$REPO_ROOT/deploy/k8s/ingress.yaml"
fi

echo -e "\nCho cac dich vu khoi dong..."
kubectl rollout status deployment/postgres -n fomoxa --timeout=90s
kubectl rollout status deployment/nakama -n fomoxa --timeout=120s
kubectl rollout status deployment/directory -n fomoxa --timeout=60s
kubectl rollout status deployment/gateway -n fomoxa --timeout=60s
kubectl rollout status deployment/backend -n fomoxa --timeout=60s

echo -e "\nKiem tra GameServers:"
kubectl get gs -n fomoxa

echo -e "\n=== Cum K8s Fomoxa da san sang tren WSL! ==="

