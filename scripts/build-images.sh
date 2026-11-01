#!/usr/bin/env bash
set -euo pipefail

TAG="${1:-latest}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

echo "=== Building fomoxa images (tag=$TAG) ==="

echo -e "\n--- 1/4 fomoxa-server ---"
docker build -f deploy/Dockerfile.server -t "fomoxa-server:$TAG" .

echo -e "\n--- 2/4 fomoxa-directory ---"
docker build -f deploy/Dockerfile.directory -t "fomoxa-directory:$TAG" .

echo -e "\n--- 3/4 fomoxa-gateway ---"
docker build -f deploy/Dockerfile.gateway -t "fomoxa-gateway:$TAG" .

echo -e "\n--- 4/4 fomoxa-backend ---"
docker build -f MyGame.Backend/Dockerfile -t "fomoxa-backend:$TAG" MyGame.Backend

echo -e "\n=== All images built successfully ==="
docker images | grep fomoxa || true

