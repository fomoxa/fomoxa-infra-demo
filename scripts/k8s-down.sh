#!/usr/bin/env bash
set -euo pipefail

CLUSTER_NAME="fomoxa"
ACTION="${1:-delete}"

if [[ "$ACTION" == "--keep-cluster" || "$ACTION" == "clean" ]]; then
    echo "Xoa namespace 'fomoxa' tren cum '$CLUSTER_NAME'..."
    kubectl delete namespace fomoxa --ignore-not-found=true
    echo "Da don dep namespace fomoxa."
else
    echo "Xoa hoan toan cum k3d '$CLUSTER_NAME'..."
    k3d cluster delete "$CLUSTER_NAME"
    echo "Da xoa cum k3d $CLUSTER_NAME."
fi
