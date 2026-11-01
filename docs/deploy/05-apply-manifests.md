# 03.5 Apply the manifests in order

Top of this document: [03-DEPLOY-KUBERNETES](README.md) · Previous: [04-secrets.md](04-secrets.md) · Next: [06-verify-cluster.md](06-verify-cluster.md)

## Step 9. Apply the manifests in order

Goal: create the services one at a time, waiting for each to be ready before the next. They depend on each other: Nakama needs Postgres, the backend needs the directory and Nakama, and game servers need the directory and the backend.

`kubectl apply -f <file>` reads YAML describing the desired state and updates the cluster to match. Running it again is safe.

### 9.1 Postgres (Nakama's database)

```bash
kubectl apply -f deploy/k8s/postgres.yaml
kubectl rollout status deployment/postgres -n fomoxa --timeout=120s
kubectl wait --for=condition=Ready pod -l app=postgres -n fomoxa --timeout=120s
```

This creates a PersistentVolumeClaim (1 GiB of disk), a Deployment and a Service `postgres:5432`. `rollout status` waits for the rollout to finish. `kubectl wait --for=condition=Ready` waits for the pod's readiness probe (`pg_isready`).

Expected output (sample):

```
persistentvolumeclaim/postgres-data created
deployment.apps/postgres created
service/postgres created
deployment "postgres" successfully rolled out
pod/postgres-6d8f9c7b5-x2k4m condition met
```

### 9.2 Nakama (matchmaking and the account store)

```bash
kubectl apply -f deploy/k8s/nakama.yaml
kubectl rollout status deployment/nakama -n fomoxa --timeout=180s
kubectl wait --for=condition=Ready pod -l app=nakama -n fomoxa --timeout=180s
```

`nakama.yaml` holds the ConfigMap `nakama-modules` (with `main.lua`, the matchmaking and key-value logic), the Deployment, and two services: `nakama` (API on 7350, type `LoadBalancer`) and `nakama-console` (7351, inside the cluster only). The pod runs an init container, `migrate`, that creates the tables in Postgres before the main container starts. The keys come from the secret `fomoxa-nakama`.

Check: `kubectl logs deploy/nakama -n fomoxa | grep -i "fomoxa match"` should print `fomoxa match module da nap (auto pool ho tro)`, which means the Lua module loaded.

### 9.3 Directory

```bash
kubectl apply -f deploy/k8s/directory.yaml
kubectl rollout status deployment/directory -n fomoxa --timeout=120s
```

The ConfigMap `directory-config` holds `directory.toml`, which includes `public_address = "127.0.0.1:7777"`, the gateway address the client sees (see [08, directory and gateway settings](../reference.md#2-directory-and-gateway-settings)). The service `directory` opens 9500 (pods, backend and gateway connect here) and 9501 (admin).

### 9.4 Gateway

```bash
kubectl apply -f deploy/k8s/gateway.yaml
kubectl rollout status deployment/gateway -n fomoxa --timeout=120s
```

The gateway listens on UDP 7777 (a `LoadBalancer` service), asks the directory which pod holds which room, and forwards the packets. It mounts only the `gateway` key.

### 9.5 Backend

```bash
kubectl apply -f deploy/k8s/backend.yaml
kubectl rollout status deployment/backend -n fomoxa --timeout=120s
```

The backend listens on TCP 9600 (players) and 9601 (WebSocket) through a `LoadBalancer` service. Port 9602 (pod reports) is reachable only through the `backend-pods` service (ClusterIP), so it is not exposed. The backend connects to Nakama (keys from the secret) and to the directory. Its health probe runs the `fomoxa-rpc-health` tool inside the container.

### 9.6 TLS proxy (port 9643)

```bash
kubectl apply -f deploy/k8s/tls-proxy.yaml
kubectl rollout status deployment/tls-proxy -n fomoxa --timeout=120s
```

nginx terminates TLS on port 9643 using the certificate from the secret `fomoxa-tls` and forwards the plain bytes to `backend:9600`. It protects the login password on the network.

### 9.7 The game server Fleet (Agones)

```bash
kubectl apply -f deploy/k8s/fleet.yaml
kubectl wait --for=jsonpath='{.status.state}'=Ready gs -l agones.dev/fleet=fomoxa-server -n fomoxa --timeout=300s
```

A `Fleet` is a pool of game servers. With `replicas: 4`, Agones creates four `GameServer`s, each a pod with two containers (`fomoxa-server` and Agones' sidecar). Each pod registers with the directory and holds one room out of `arena:1..16`. For GameServers use `--for=jsonpath='{.status.state}'=Ready` instead of `--for=condition=Ready`, because Agones records the state in `.status.state` and does not use `conditions`.

Expected output (sample):

```
fleet.agones.dev/fomoxa-server created
gameserver.agones.dev/fomoxa-server-hl9fz-2xkpq condition met
...(four lines)
```

Or run Steps 8 and 9 in one go: `./scripts/k8s-up.sh`. Once the cluster, Agones and images are ready, it applies the manifests in the order above and runs `rollout status` on each deployment.

Common errors for Step 9:

| Symptom | Fix |
|---|---|
| Pod `ImagePullBackOff` or `ErrImagePull` | The image was not imported into k3d (Step 7). `kubectl describe pod <name> -n fomoxa` |
| Pod `CreateContainerConfigError` | A secret is missing (Step 8). `kubectl describe pod ...` names it |
| Pod `CrashLoopBackOff` | `kubectl logs <pod> -n fomoxa --previous` shows why. Usually a wrong or missing key, or a dependency that is not up yet |
| Nakama `Init:Error` | The `migrate` init container cannot reach Postgres: `kubectl logs deploy/nakama -n fomoxa -c migrate`. Often the DB password differs from old data in the volume ([07](../troubleshooting.md#changing-the-db-password-stops-nakama-from-starting)) |
| GameServer stuck in `Starting` | `kubectl logs <gs-pod> -n fomoxa -c fomoxa-server`. Usually the pod cannot reach the directory on 9500 |
