#!/usr/bin/env bash
set -euo pipefail

CLUSTER_NAME="fomoxa"
ACTION="${1:-delete}"

if [[ "$ACTION" == "--keep-cluster" || "$ACTION" == "clean" ]]; then
    echo "Deleting namespace 'fomoxa' from cluster '$CLUSTER_NAME'..."
    kubectl delete namespace fomoxa --ignore-not-found=true
    echo "Namespace fomoxa cleaned up."
else
    echo "Deleting k3d cluster '$CLUSTER_NAME' entirely..."
    k3d cluster delete "$CLUSTER_NAME"
    echo "Deleted k3d cluster $CLUSTER_NAME."
fi

