#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

CLUSTER_NAME="fomoxa"

echo "=== Starting Kubernetes cluster (k3d + Agones) for Fomoxa on WSL ==="

# 1. Kiem tra docker
if ! docker info >/dev/null 2>&1; then
    echo "Error: Docker daemon is not running or not permitted in WSL." >&2
    exit 1
fi

# 2. Kiem tra k3d & helm
if ! command -v k3d >/dev/null 2>&1; then
    echo "k3d is not installed. Installing k3d to /usr/local/bin..."
    curl -s https://raw.githubusercontent.com/k3d-io/k3d/main/install.sh | TAG=v5.7.4 bash || {
        echo "Error: Could not install k3d automatically. Run 'sudo apt install' or install k3d manually." >&2
        exit 1
    }
fi

if ! command -v helm >/dev/null 2>&1; then
    echo "helm is not installed. Installing helm..."
    curl -fsSL https://raw.githubusercontent.com/helm/helm/main/scripts/get-helm-3 | bash || {
        echo "Error: Could not install helm automatically." >&2
        exit 1
    }
fi

# 3. Tao cum k3d fomoxa
port_busy() {
    # true neu cong TCP/UDP dang duoc docker publish
    docker ps --format '{{.Names}} {{.Ports}}' | grep -E "[:]$1->" || true
}
if ! k3d cluster list "$CLUSTER_NAME" >/dev/null 2>&1; then
    for p in 7777 9600 9643 7350; do
        busy="$(port_busy "$p")"
        if [[ -n "$busy" ]]; then
            echo "Error: port $p is already held by another container:" >&2
            echo "$busy" >&2
            echo "Stop docker compose first: docker compose -f deploy/compose.yaml down" >&2
            exit 1
        fi
    done
    echo -e "\n1/6 Creating k3d cluster '$CLUSTER_NAME' with port mappings..."
    k3d cluster create "$CLUSTER_NAME" \
        --port "7777:7777/udp@loadbalancer" \
        --port "9600:9600@loadbalancer" \
        --port "9643:9643@loadbalancer" \
        --port "7350:7350@loadbalancer" \
        --wait
else
    echo -e "\n1/6 k3d cluster '$CLUSTER_NAME' already exists."
fi

kubectl config use-context "k3d-$CLUSTER_NAME" >/dev/null
echo "Current context: $(kubectl config current-context)"

# 4. Install Agones via Helm
echo -e "\n2/6 Installing Agones via Helm..."
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
    echo "Agones is already installed in agones-system."
fi

# 5. Build va Import Docker images vao k3d
BUILD="${1:-}"
if [[ "$BUILD" == "--build" || "$BUILD" == "-b" ]]; then
    echo -e "\n3/6 Building Docker images..."
    bash "$REPO_ROOT/scripts/build-images.sh"
fi

echo -e "\n4/6 Importing images into k3d cluster..."
k3d image import \
    fomoxa-server:latest \
    fomoxa-directory:latest \
    fomoxa-gateway:latest \
    fomoxa-backend:latest \
    -c "$CLUSTER_NAME"

# 6. Tao namespace va Secrets
echo -e "\n5/6 Creating namespace and secrets..."
kubectl apply -f "$REPO_ROOT/deploy/k8s/namespace.yaml"
bash "$REPO_ROOT/scripts/gen-k8s-secrets.sh" "fomoxa"

# 7. Apply K8s manifests
echo -e "\n6/6 Applying K8s manifests..."
kubectl apply -f "$REPO_ROOT/deploy/k8s/postgres.yaml"
kubectl apply -f "$REPO_ROOT/deploy/k8s/nakama.yaml"
kubectl apply -f "$REPO_ROOT/deploy/k8s/directory.yaml"
kubectl apply -f "$REPO_ROOT/deploy/k8s/gateway.yaml"
kubectl apply -f "$REPO_ROOT/deploy/k8s/backend.yaml"
kubectl apply -f "$REPO_ROOT/deploy/k8s/tls-proxy.yaml"
kubectl apply -f "$REPO_ROOT/deploy/k8s/fleet.yaml"
# Ingress la thu nghiem (chua TLS), chi ap dung khi APPLY_INGRESS=1
if [[ "${APPLY_INGRESS:-}" == "1" && -f "$REPO_ROOT/deploy/k8s/ingress.yaml" ]]; then
    kubectl apply -f "$REPO_ROOT/deploy/k8s/ingress.yaml"
fi

# NetworkPolicy la tuy chon (chua thu tren cum that): APPLY_NETPOL=1
if [[ "${APPLY_NETPOL:-}" == "1" ]]; then
    kubectl apply -f "$REPO_ROOT/deploy/k8s/networkpolicy.yaml"
fi

echo -e "\nWaiting for services to start..."
kubectl rollout status deployment/postgres -n fomoxa --timeout=90s
kubectl rollout status deployment/nakama -n fomoxa --timeout=120s
kubectl rollout status deployment/directory -n fomoxa --timeout=60s
kubectl rollout status deployment/gateway -n fomoxa --timeout=60s
kubectl rollout status deployment/backend -n fomoxa --timeout=60s

# AUTOSCALE=1: FleetAutoscaler giu 2 pod Ready, 4..16 pod (can Agones da cai xong)
if [[ "${AUTOSCALE:-}" == "1" ]]; then
    kubectl apply -f "$REPO_ROOT/deploy/k8s/fleet-autoscaler.yaml"
fi

# INSTALL_MONITORING=1: Prometheus + Alertmanager + Grafana + Loki/Promtail trong namespace monitoring
if [[ "${INSTALL_MONITORING:-}" == "1" ]]; then
    kubectl apply -f "$REPO_ROOT/deploy/monitoring/00-namespace.yaml"
    if ! kubectl get secret grafana-admin -n monitoring >/dev/null 2>&1; then
        kubectl create secret generic grafana-admin -n monitoring --from-literal=password="$(openssl rand -hex 12)"
    fi
    kubectl apply -f "$REPO_ROOT/deploy/monitoring/"
    echo "Monitoring: kubectl port-forward -n monitoring svc/grafana 3000:3000  (user admin, password: kubectl get secret grafana-admin -n monitoring -o jsonpath={.data.password} | base64 -d)"
fi

# TLS_ONLY=1: dong cong 9600 khong ma hoa ra ngoai (chi trong cum), client bat buoc di qua TLS 9643
if [[ "${TLS_ONLY:-}" == "1" ]]; then
    kubectl patch svc backend -n fomoxa -p '{"spec":{"type":"ClusterIP"}}' >/dev/null
    echo "Port 9600 is now closed (backend ClusterIP). Clients must use TLS port 9643."
fi
echo "TLS: clients use -backend:127.0.0.1:9643 -tlspin:<pin>; view the pin with ./scripts/gen-tls-cert.sh"

echo -e "\nChecking GameServers:"
kubectl get gs -n fomoxa

echo -e "\n=== Fomoxa K8s cluster is ready on WSL! ==="

