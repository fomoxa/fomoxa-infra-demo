# 03 Deploy to Kubernetes (k3d + Agones), step by step

Vietnamese version: [../03-TRIEN-KHAI-K8S.md](../03-TRIEN-KHAI-K8S.md)

This document builds the real cluster on your machine: Postgres, Nakama, directory, gateway, backend, and the Agones Fleet of game servers, all inside k3d. You do each step by hand to see what happens. At the end there is a one-command shortcut (`./scripts/k8s-up.sh --build`) and a table of which steps that script performs.

Prerequisite: [01 Prepare the environment](01-PREPARE-ENVIRONMENT.md) is done (`docker`, `kubectl`, `k3d`, `helm`, `openssl`, `git`). The first run takes 30 to 60 minutes, mostly building the Rust images and pulling images.

Not verified: steps 4 (Rust image builds) and 5 to 11 (k3d, Agones, manifests) were not re-run while writing. The commands come straight from `scripts/k8s-up.sh`, `scripts/build-images.sh`, `scripts/gen-*.sh` and the manifests, and the output is labeled (sample). Steps 2 and 12 were run for real.

## Map of the steps

| # | Task | Result |
|---|---|---|
| 1 | Prepare, check free ports | the machine is ready |
| 2 | Generate keys and certificate | `deploy/secrets/` |
| 3 | Get the `gateway` (Rust) source | `gateway/` directory |
| 4 | Build four Docker images | `fomoxa-{server,directory,gateway,backend}:latest` |
| 5 | Create the k3d cluster | cluster `fomoxa` with ports 7777, 9600, 9643, 7350 |
| 6 | Install Agones with Helm | `Fleet` and `GameServer` resources |
| 7 | Import images into k3d | the cluster can see the images |
| 8 | Namespace and secrets | namespace `fomoxa` plus six secrets |
| 9 | Apply manifests in order | the services run |
| 10 | Check the whole cluster | pods `Running`, GameServers `Ready` |
| 11 | Check from the client's side | ports 9600, 9643, 7777 reachable |
| 12 | Test Nakama with `curl` | pairing works |
| 13 | Test with the Unity client | GameServer becomes `Allocated` |
| 14 | Clean up | |

## Step 1. Prepare and check free ports

Goal: stand in the right directory and make sure the ports the cluster will take are free.

```bash
cd ~/fomoxa-infra-demo

# Is Docker running?
docker info --format 'Docker {{.ServerVersion}} - OK'

# Does any container already hold a port we need? (compose Nakama, an old cluster, ...)
docker ps --format '{{.Names}}\t{{.Ports}}' | grep -E ':(7777|9600|9643|7350)->' || echo "The ports are free"

# Does any process outside Docker listen on them? (the mock cluster from document 02, ...)
ss -ltnu | grep -E ':(7777|9600|9643|7350)\b' || echo "Nothing is listening"
```

k3d forwards four ports of your machine into the cluster. If one is taken, `k3d cluster create` fails partway through.

Expected output (sample):

```
Docker 27.3.1 - OK
The ports are free
Nothing is listening
```

Common errors:

| Symptom | Fix |
|---|---|
| `fomoxa-test-nakama-1` holds `7350` | That is the local-development `docker compose`. Run `docker compose -f deploy/compose.yaml down`. If you leave it up, the test scripts hit the compose Nakama and can report a false success |
| The mock cluster from document 02 is running | Stop it with Ctrl+C. It also uses 9600 and 9643 |
| Docker is not running | [01, Step 3](01-PREPARE-ENVIRONMENT.md#step-3-install-docker) |

## Step 2. Generate keys and the certificate

Goal: create the random keys the services use to authenticate each other, separate secrets for Nakama, and a TLS certificate. Everything goes into `deploy/secrets/`, which is in `.gitignore`. Never commit it.

Why: the directory only accepts a backend, pod or gateway that presents the right key. The backend only accepts pods that have its own key. Nakama uses its own keys instead of `defaultkey`.

```bash
mkdir -p deploy/secrets/directory deploy/secrets/nakama deploy/secrets/tls

# gen <file> <bytes>: write <bytes> random bytes as hex (2 characters per byte), no trailing newline
gen() { openssl rand -hex "$2" | tr -d '\n' > "$1"; echo "  + $1 ($(wc -c < "$1") characters)"; }

# --- keys for the directory and the programs that connect to it ---
gen deploy/secrets/directory/root     32   # the directory's root key (MUST be exactly 64 hex characters)
gen deploy/secrets/directory/admin    24   # admin rights (drain, read the pod list)
gen deploy/secrets/directory/backend  24   # backend asks for tickets
gen deploy/secrets/directory/gateway  24   # gateway reads the pod list
gen deploy/secrets/directory/pod      24   # game servers register rooms
gen deploy/secrets/backend-pod        24   # game servers report player counts and scores to the backend (port 9602)

# --- Nakama: its own keys and passwords (instead of defaultkey, defaulthttpkey, admin:admin, localdb) ---
gen deploy/secrets/nakama/server-key       16   # clients and the backend log in to Nakama
gen deploy/secrets/nakama/http-key         24   # only the backend calls internal RPCs (kv_get, verify_ticket, ...)
gen deploy/secrets/nakama/console-password 16   # Nakama admin console password
gen deploy/secrets/nakama/session-key      24   # encrypts session tokens
gen deploy/secrets/nakama/refresh-key      24   # encrypts refresh tokens
gen deploy/secrets/nakama/db-password      16   # Postgres password

# --- self-signed TLS certificate for the TLS proxy (port 9643) ---
openssl req -x509 -newkey rsa:2048 -nodes -days 730 \
    -keyout deploy/secrets/tls/tls.key -out deploy/secrets/tls/tls.crt \
    -subj "/CN=fomoxa.local" \
    -addext "subjectAltName=DNS:fomoxa.local,DNS:localhost,IP:127.0.0.1" \
    -addext "extendedKeyUsage=serverAuth" 2>/dev/null
chmod 600 deploy/secrets/tls/tls.key

# SHA-256 fingerprint of the certificate (the "pin"). The client uses it to confirm it talks to the right proxy
openssl x509 -in deploy/secrets/tls/tls.crt -outform DER | openssl dgst -sha256 -hex | awk '{print toupper($NF)}'
```

`openssl rand -hex N` prints N random bytes as 2N hex characters, and `tr -d '\n'` removes the trailing newline (a key must not have an extra line). The self-signed certificate is for development and internal use. For a public deployment use a certificate from a CA (see [05, TLS](05-OPERATIONS.md#tls-between-client-and-backend)).

Or run it in one go: `./scripts/gen-keys.sh` writes the 12 keys above, and `./scripts/gen-tls-cert.sh` writes the certificate and prints the pin. Neither overwrites an existing file unless you pass `-f`.

Expected output (real, from `gen-keys.sh` and `gen-tls-cert.sh`):

```
=== Sinh keys cho Fomoxa ===
  + directory/root
  + directory/admin
  + directory/backend
  + directory/gateway
  + directory/pod
  + secrets/backend-pod
  + nakama/server-key
  + nakama/http-key
  + nakama/console-password
  + nakama/session-key
  + nakama/refresh-key
  + nakama/db-password
Hoan tat sinh keys tai /home/you/fomoxa-infra-demo/deploy/secrets
Da tao /home/you/fomoxa-infra-demo/deploy/secrets/tls/tls.crt va tls.key (CN=fomoxa.local)
Van tay SHA-256 (pin): FB3FF3780A89AD4A3CB83402C454F0247AA0238C1B304EA8DB475497B73C94DE
```

Check:

```bash
find deploy/secrets -type f | sort
wc -c deploy/secrets/directory/root deploy/secrets/directory/backend deploy/secrets/nakama/http-key
```

You should see 14 files (12 keys plus `tls.crt` and `tls.key`). `root` is exactly 64 bytes, `backend` is 48 and `http-key` is 48.

Common errors:

| Symptom | Fix |
|---|---|
| `openssl: command not found` | `sudo apt install -y openssl` |
| The `root` key is not 64 characters | Recreate it with `gen deploy/secrets/directory/root 32`. The directory rejects a root key of the wrong length |
| An old cluster is running with old keys and you regenerated them | The secrets in the cluster are still the old ones, so they no longer match. Redo [Step 8](#step-8-namespace-and-secrets) and restart the deployments |

## Step 3. Get the `gateway` (Rust) source

Goal: a `gateway/` directory with the source of `fomoxa-directory` and `fomoxa-gateway`. They are written in Rust and live outside this repository, but the image builds need them.

```bash
[ -d gateway ] || git clone --depth 1 -b 0.2.0 https://github.com/fomoxa/gateway.git gateway
ls gateway | head
```

This clones tag `0.2.0`, the version this repository uses, and `--depth 1` takes only the latest commit. The `gateway/` directory is in `.gitignore`. `[ -d gateway ] ||` skips the clone if the directory exists.

Or run it in one go: `./scripts/build-images.sh` clones it if missing, as its first action.

Expected output (sample): a `Cargo.toml` and the crate directories.

If you get `Repository not found` or a password prompt, `fomoxa/gateway` is private or your account cannot see it. Ask for access, or place the source in `gateway/` yourself.

## Step 4. Build four Docker images

Goal: package the four components as Docker images.

| Image | Dockerfile | Contents |
|---|---|---|
| `fomoxa-server` | `deploy/Dockerfile.server` | game server (.NET 8), built from `MyGame/Server~` and `server-deps` |
| `fomoxa-directory` | `deploy/Dockerfile.directory` | directory (Rust, from `gateway/`) |
| `fomoxa-gateway` | `deploy/Dockerfile.gateway` | UDP gateway (Rust, from `gateway/`) |
| `fomoxa-backend` | `MyGame.Backend/Dockerfile` | backend (.NET 8) and the health tool |

```bash
docker build -f deploy/Dockerfile.server    -t fomoxa-server:latest    .
docker build -f deploy/Dockerfile.directory -t fomoxa-directory:latest .
docker build -f deploy/Dockerfile.gateway   -t fomoxa-gateway:latest   .
docker build -f MyGame.Backend/Dockerfile   -t fomoxa-backend:latest   MyGame.Backend
```

`-f <Dockerfile>` picks the build recipe, `-t <name:tag>` names the image, and the last argument is the build context, the directory Docker sends as the root for `COPY`. The first three use the repository root (`.`). The backend has its own context, `MyGame.Backend`.

Each Dockerfile is multi-stage. The `build` stage has the heavy SDK or compiler (`dotnet/sdk`, `rust`), and the final stage copies only the result into a small runtime image. `.dockerignore` excludes `MyGame/Library`, `deploy/secrets`, `gateway/target` and similar, so builds stay fast and no key lands in an image.

Or run it in one go: `./scripts/build-images.sh` clones `gateway/` if needed and runs exactly these four commands, then lists the images.

The two Rust images take the longest, 10 to 25 minutes the first time, because Docker downloads and compiles the crates. Later builds are faster thanks to the cache.

Expected output (sample, the end of a build):

```
 => exporting to image
 => => naming to docker.io/library/fomoxa-server:latest
```

Check:

```bash
docker images | grep -E '^fomoxa-(server|directory|gateway|backend)\b'
```

You should see four lines, all tagged `latest` (sample):

```
fomoxa-backend     latest   3f1c...   2 minutes ago   260MB
fomoxa-gateway     latest   9a77...   6 minutes ago   95MB
fomoxa-directory   latest   c04b...   9 minutes ago   98MB
fomoxa-server      latest   e1d2...   12 minutes ago  235MB
```

Common errors:

| Symptom | Fix |
|---|---|
| `COPY gateway/ .: not found` | Step 3 was skipped |
| `error NU1301 ... api.nuget.org` while building a .NET image | The build container cannot reach the Internet (a corporate proxy). Configure the proxy for the Docker daemon or build on another network |
| A Rust build prints `Killed` or runs out of memory | Give Docker Desktop or WSL2 more RAM ([01, Step 1](01-PREPARE-ENVIRONMENT.md#step-1-check-that-the-machine-is-big-enough)). `docker builder prune` frees cache |
| `no space left on device` | `docker system df`, then `docker image prune -a` |

## Step 5. Create the k3d cluster

Goal: a one-node Kubernetes cluster named `fomoxa` with four of your machine's ports forwarded into it.

```bash
k3d cluster create fomoxa \
    --port "7777:7777/udp@loadbalancer" \
    --port "9600:9600@loadbalancer" \
    --port "9643:9643@loadbalancer" \
    --port "7350:7350@loadbalancer" \
    --wait
```

Each `--port` has the form `machine-port:cluster-port[/protocol]@loadbalancer`:

| Flag | Your machine's port goes to | Who uses it |
|---|---|---|
| `7777:7777/udp` | UDP 7777 to `gateway` | the client (gameplay) |
| `9600:9600` | TCP 9600 to `backend` | the client (login, rooms) |
| `9643:9643` | TCP 9643 to `tls-proxy` | the client, TLS version (optional) |
| `7350:7350` | TCP 7350 to `nakama` | test scripts only |

`@loadbalancer` means the traffic goes through k3d's load balancer container, which forwards only to `LoadBalancer` services that use that port number (already declared in the manifests). `--wait` returns once the cluster is ready.

Declare the ports now. k3d cannot add ports to an existing cluster. If one is missing you must delete and recreate it (`k3d cluster delete fomoxa`).

Or run it in one go: `./scripts/k8s-up.sh` checks the ports are free and runs this command if the cluster does not exist.

Expected output (sample):

```
INFO[0000] Prep: Network
INFO[0000] Created network 'k3d-fomoxa'
INFO[0000] Created image volume k3d-fomoxa-images
INFO[0001] Creating node 'k3d-fomoxa-server-0'
INFO[0001] Creating LoadBalancer 'k3d-fomoxa-serverlb'
INFO[0009] Cluster 'fomoxa' created successfully!
INFO[0009] You can now use it like this:
kubectl cluster-info
```

Check:

```bash
kubectl config use-context k3d-fomoxa
kubectl get nodes
docker ps --format '{{.Names}}' | grep k3d
docker port k3d-fomoxa-serverlb
```

`use-context` selects the cluster `kubectl` talks to. One machine can have several, and picking the wrong context is a common mistake. `docker port` lists the published ports.

Expected output (sample):

```
NAME                  STATUS   ROLES                  AGE   VERSION
k3d-fomoxa-server-0   Ready    control-plane,master   30s   v1.30.4+k3s1
k3d-fomoxa-serverlb
k3d-fomoxa-server-0
7350/tcp -> 0.0.0.0:7350
7777/udp -> 0.0.0.0:7777
9600/tcp -> 0.0.0.0:9600
9643/tcp -> 0.0.0.0:9643
```

You may also see a `6443/tcp` line, the Kubernetes API port.

Common errors:

| Symptom | Fix |
|---|---|
| `port is already allocated` | Step 1 did not clear the ports. Find the holder with `docker ps \| grep 9600` and `ss -ltnp \| grep 9600` |
| Node stays `NotReady` | Not enough RAM. Look at `docker logs k3d-fomoxa-server-0 \| tail` |
| `kubectl` points at another cluster | `kubectl config get-contexts`, then `kubectl config use-context k3d-fomoxa` |

## Step 6. Install Agones with Helm

Goal: install Agones, the controller that manages the game server lifecycle (creating, monitoring and replacing pods, and the states `Ready`, `Allocated`, `Shutdown`). It adds new resource types to Kubernetes: `Fleet`, `GameServer` and `FleetAutoscaler`.

```bash
helm repo add agones https://agones.dev/chart/stable --force-update
helm repo update agones

helm install agones agones/agones \
    --namespace agones-system \
    --create-namespace \
    --set "agones.allocator.install=false" \
    --set "agones.ping.install=false" \
    --wait --timeout 5m
```

What the flags do:

- `helm repo add` registers Agones' chart repository, like adding a PPA to apt. The correct URL ends in `/chart/stable`.
- `helm install <release> <chart>` installs it. `--namespace agones-system --create-namespace` puts it in its own namespace.
- `agones.allocator.install=false` skips the allocator. It is a `LoadBalancer` service on port 443, which collides with k3s's Traefik and can make `--wait` hang. This repository does not use `GameServerAllocation`.
- `agones.ping.install=false` skips the ping service, which is not needed.
- `--wait --timeout 5m` waits up to five minutes for all pods to be ready.
- To pin the version (recommended for a stable setup), add `--version <x.y.z>`; list versions with `helm search repo agones/agones --versions | head`. The Agones release must support the Kubernetes version of your k3s (k3d 5.7.4 ships k3s 1.30).

Or run it in one go: `./scripts/k8s-up.sh` runs these commands and skips them if `helm list -n agones-system` already shows `agones`. Set `AGONES_VERSION=1.xx.y` to pin the chart.

Expected output (sample):

```
NAME: agones
LAST DEPLOYED: Sat Nov  1 10:12:03 2026
NAMESPACE: agones-system
STATUS: deployed
```

Check:

```bash
kubectl get pods -n agones-system
kubectl get crd | grep agones.dev
```

```
NAME                                 READY   STATUS    RESTARTS   AGE   (sample)
agones-controller-6b9c7c6f5d-7xk2p   1/1     Running   0          2m
agones-controller-6b9c7c6f5d-q9wzl   1/1     Running   0          2m
agones-extensions-7d5b8c9d6-4hjfm    1/1     Running   0          2m
agones-extensions-7d5b8c9d6-ztk8s    1/1     Running   0          2m

fleetautoscalers.autoscaling.agones.dev   ...
fleets.agones.dev                         ...
gameservers.agones.dev                    ...
```

Common errors:

| Symptom | Fix |
|---|---|
| `404 Not Found` on `helm repo add` | You used the old URL `.../chart/helm`. The correct one is `https://agones.dev/chart/stable` |
| `helm install ... timed out waiting for the condition` | The Agones pods did not come up (slow image pull). `kubectl get pods -n agones-system`, `kubectl describe pod ... -n agones-system`, then `helm uninstall agones -n agones-system` and install again |
| `release name "agones" in namespace ... exists` | It is already installed. Skip this step |
| A CRD complains about the Kubernetes version | Pin an older `--version` that matches your k3s |

## Step 7. Import the images into k3d

Goal: make the cluster able to see the four images you built.

k3s runs inside a container and has its own image store. It cannot see the images in your machine's Docker. The manifests use `imagePullPolicy: IfNotPresent`, so an image already inside the cluster is used as is, and nothing is pulled from the Internet (these images are not on Docker Hub).

```bash
k3d image import \
    fomoxa-server:latest \
    fomoxa-directory:latest \
    fomoxa-gateway:latest \
    fomoxa-backend:latest \
    -c fomoxa
```

This packs each image into a tar file and loads it into the node `k3d-fomoxa-server-0`. `-c fomoxa` selects the cluster.

Or run it in one go: `./scripts/k8s-up.sh` (its "4/6 Import images" stage).

Expected output (sample):

```
INFO[0000] Importing image(s) into cluster 'fomoxa'
INFO[0006] Successfully imported 4 image(s) into 1 cluster(s)
```

Check:

```bash
docker exec k3d-fomoxa-server-0 crictl images | grep fomoxa
```

You should see all four `fomoxa-*` images.

The import is slow for images of a few hundred MB, so do not interrupt it. After every rebuild of an image you must import again and restart the workload ([05, updating images](05-OPERATIONS.md#updating-after-rebuilding-an-image)).

## Step 8. Namespace and secrets

Goal: create the `fomoxa` namespace and load the keys from Step 2 into the cluster as secrets, the objects that hold sensitive data. Pods read them as environment variables or mounted files.

```bash
# a namespace is a separate compartment for the whole application
kubectl apply -f deploy/k8s/namespace.yaml

# Directory keys: the directory reads all five; the gateway only mounts the 'gateway' key
kubectl create secret generic fomoxa-directory-keys -n fomoxa \
    --from-file=backend=deploy/secrets/directory/backend \
    --from-file=pod=deploy/secrets/directory/pod \
    --from-file=gateway=deploy/secrets/directory/gateway \
    --from-file=admin=deploy/secrets/directory/admin \
    --from-file=root=deploy/secrets/directory/root \
    --dry-run=client -o yaml | kubectl apply -f -

# Backend: the key for talking to the directory, plus the key for the pod port
kubectl create secret generic fomoxa-backend-keys -n fomoxa \
    --from-file=backend=deploy/secrets/directory/backend \
    --from-file=backend-pod=deploy/secrets/backend-pod \
    --dry-run=client -o yaml | kubectl apply -f -

# Game servers: the key for registering rooms with the directory
kubectl create secret generic fomoxa-pod-key -n fomoxa \
    --from-file=pod=deploy/secrets/directory/pod \
    --dry-run=client -o yaml | kubectl apply -f -

# Game servers: the key for reporting counts and scores to the backend (separate, without the backend-to-directory key)
kubectl create secret generic fomoxa-backend-pod-key -n fomoxa \
    --from-file=backend-pod=deploy/secrets/backend-pod \
    --dry-run=client -o yaml | kubectl apply -f -

# Nakama and Postgres
kubectl create secret generic fomoxa-nakama -n fomoxa \
    --from-file=server-key=deploy/secrets/nakama/server-key \
    --from-file=http-key=deploy/secrets/nakama/http-key \
    --from-file=console-password=deploy/secrets/nakama/console-password \
    --from-file=session-key=deploy/secrets/nakama/session-key \
    --from-file=refresh-key=deploy/secrets/nakama/refresh-key \
    --from-file=db-password=deploy/secrets/nakama/db-password \
    --dry-run=client -o yaml | kubectl apply -f -

# TLS certificate for the tls-proxy
kubectl create secret tls fomoxa-tls -n fomoxa \
    --cert=deploy/secrets/tls/tls.crt --key=deploy/secrets/tls/tls.key \
    --dry-run=client -o yaml | kubectl apply -f -
```

`--from-file=<name>=<path>` puts a file's contents into the secret under the key `<name>`, and in a pod it appears as the file `/secrets/.../<name>`. The `--dry-run=client -o yaml | kubectl apply -f -` pattern makes each command safe to repeat: instead of `create` (which fails if the secret exists) it prints the YAML and applies it, creating or updating. Each service receives only the keys it needs, so a game pod never sees the `root` or `admin` key.

Or run it in one go: `./scripts/gen-k8s-secrets.sh` generates the keys if missing and the certificate if missing, then creates the secrets.

Expected output (sample):

```
namespace/fomoxa created
secret/fomoxa-directory-keys created
secret/fomoxa-backend-keys created
secret/fomoxa-pod-key created
secret/fomoxa-backend-pod-key created
secret/fomoxa-nakama created
secret/fomoxa-tls created
```

Check:

```bash
kubectl get secrets -n fomoxa
kubectl get secret fomoxa-directory-keys -n fomoxa -o jsonpath='{.data}' | tr ',' '\n' | cut -d'"' -f2
```

You should see six secrets, and the directory secret lists five keys: `admin backend gateway pod root`.

Common errors: `no such file or directory: deploy/secrets/...` means Step 2 was skipped or you are in the wrong directory. `Error from server (NotFound): namespaces "fomoxa" not found` means the first `kubectl apply -f deploy/k8s/namespace.yaml` line did not run.

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

The ConfigMap `directory-config` holds `directory.toml`, which includes `public_address = "127.0.0.1:7777"`, the gateway address the client sees (see [08, directory and gateway settings](08-REFERENCE.md#2-directory-and-gateway-settings)). The service `directory` opens 9500 (pods, backend and gateway connect here) and 9501 (admin).

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
| Nakama `Init:Error` | The `migrate` init container cannot reach Postgres: `kubectl logs deploy/nakama -n fomoxa -c migrate`. Often the DB password differs from old data in the volume ([07](07-TROUBLESHOOTING.md#changing-the-db-password-stops-nakama-from-starting)) |
| GameServer stuck in `Starting` | `kubectl logs <gs-pod> -n fomoxa -c fomoxa-server`. Usually the pod cannot reach the directory on 9500 |

## Step 10. Check the whole cluster

Goal: confirm every component runs and the game servers registered with the directory.

```bash
kubectl get pods -n fomoxa
kubectl get svc -n fomoxa
kubectl get fleet,gs -n fomoxa
```

Expected output (sample):

```
NAME                         READY   STATUS    RESTARTS   AGE
backend-7f9d8c6b5-4hq2l      1/1     Running   0          3m
directory-5c7b9d8f6-m8x2p    1/1     Running   0          4m
fomoxa-server-hl9fz-2xkpq    2/2     Running   0          2m
fomoxa-server-hl9fz-8mnrd    2/2     Running   0          2m
fomoxa-server-hl9fz-c4v7t    2/2     Running   0          2m
fomoxa-server-hl9fz-wp5xs    2/2     Running   0          2m
gateway-6d5f7b8c9-z7k3q      1/1     Running   0          4m
nakama-79c8d6b5f4-9rt6w      1/1     Running   0          5m
postgres-6d8f9c7b5-x2k4m     1/1     Running   0          6m
tls-proxy-5b8f6d7c4-j2n9v    1/1     Running   0          3m

NAME                TYPE           PORT(S)
backend             LoadBalancer   9600:3xxxx/TCP,9601:3xxxx/TCP
backend-pods        ClusterIP      9602/TCP
directory           ClusterIP      9500/TCP,9501/TCP
gateway             LoadBalancer   7777:3xxxx/UDP
nakama              LoadBalancer   7350:3xxxx/TCP
nakama-console      ClusterIP      7351/TCP
postgres            ClusterIP      5432/TCP
tls-proxy           LoadBalancer   9643:3xxxx/TCP

NAME                                 SCHEDULING   DESIRED   CURRENT   ALLOCATED   READY   AGE
fleet.agones.dev/fomoxa-server       Packed       4         4         0           4       2m

NAME                                      STATE   ADDRESS      PORT   NODE                  AGE
gameserver.agones.dev/fomoxa-server-...   Ready   10.42.0.x    7778   k3d-fomoxa-server-0   2m
```

How to read it:

- `READY 2/2` on a game server pod means both containers (the game and Agones' sidecar) are ready.
- A GameServer's `STATE` goes `Starting`, then `Ready` (registered, waiting for players), then `Allocated` (the first player has connected), then `Shutdown` (the room has been empty for 30 seconds; the Fleet creates a replacement).
- `ALLOCATED 0 / READY 4` means four empty rooms are waiting for players.

To confirm the game servers registered their rooms:

```bash
kubectl logs -n fomoxa -l agones.dev/role=gameserver -c fomoxa-server --tail=20 --prefix | grep -E "registered|claimed|Ready|Backend"
```

Expected output (the line format is real, the pod name is a sample):

```
[pod/fomoxa-server-hl9fz-2xkpq/fomoxa-server] [Pod] registered id=1 changed=False
[pod/fomoxa-server-hl9fz-2xkpq/fomoxa-server] [Pod] listening on udp 7778
[pod/fomoxa-server-hl9fz-2xkpq/fomoxa-server] [Pod] claimed room from pool: arena:1
[pod/fomoxa-server-hl9fz-2xkpq/fomoxa-server] [Agones] Ready
[pod/fomoxa-server-hl9fz-2xkpq/fomoxa-server] [Backend] da ket noi
```

`[Backend] da ket noi` means the pod reached the backend on port 9602, which it uses to report player counts and scores.

The backend log:

```bash
kubectl logs deploy/backend -n fomoxa | head -5
```

```
[Nakama] cấu hình http://nakama.fomoxa.svc.cluster.local:7350
[Metrics] http://0.0.0.0:9610/metrics
backend: players on tcp 0.0.0.0:9600, websocket 0.0.0.0:9601; pods on tcp 0.0.0.0:9602; directory at directory.fomoxa.svc.cluster.local:9500
[Nakama] self test ok <uuid>
```

The directory and gateway are Rust programs with their own log format. A healthy one shows no repeating error lines: `kubectl logs deploy/directory -n fomoxa --tail=20` and `kubectl logs deploy/gateway -n fomoxa --tail=20`.

If a game server pod has no `registered` line, look in `kubectl logs ... -c fomoxa-server` for `Connection refused` to `directory...:9500`. Either the directory is not up, or the `pod` key differs between the secrets `fomoxa-pod-key` and `fomoxa-directory-keys`.

## Step 11. Check from the client's side

Goal: confirm your machine can reach the ports a client will use.

```bash
# TCP 9600 (backend), 9643 (TLS) and 7350 (Nakama, tests only)
for p in 9600 9643 7350; do
  timeout 3 bash -c "</dev/tcp/127.0.0.1/$p" && echo "TCP $p: open" || echo "TCP $p: cannot connect"
done

# UDP 7777: UDP has no handshake, so you cannot test it by connecting. Check the port is published
docker port k3d-fomoxa-serverlb | grep 7777

# The backend's own health check (the command Kubernetes uses as a probe)
kubectl exec -n fomoxa deploy/backend -- dotnet /app/health/fomoxa-rpc-health.dll 127.0.0.1:9600; echo "exit=$?"

# Nakama
curl -s -o /dev/null -w "Nakama healthcheck: HTTP %{http_code}\n" http://127.0.0.1:7350/healthcheck
```

UDP cannot be tested by opening a connection, so you check that k3d published the port. Step 13 shows the gateway receiving packets, when a client plays. The `fomoxa-rpc-health` tool prints `SERVING` and exits with 0 when the backend is healthy.

Expected output (sample):

```
TCP 9600: open
TCP 9643: open
TCP 7350: open
7777/udp -> 0.0.0.0:7777
SERVING
exit=0
Nakama healthcheck: HTTP 200
```

If `TCP 9600` cannot connect, the cluster was created without the `--port` flag (Step 5) or the backend service has no `EXTERNAL-IP` yet (`kubectl get svc backend -n fomoxa`). If the Unity client is on Windows and the cluster is in WSL2, you need Docker Desktop so the ports reach Windows `localhost`. See [07](07-TROUBLESHOOTING.md#a-windows-client-cannot-reach-a-cluster-in-wsl2).

## Step 12. Test Nakama with `curl`

Goal: show that Nakama pairs two players into one room, issues a valid ticket, and rejects outsiders. This test was run for real, against the Nakama from `docker compose` that has the same `main.lua` module.

Get the cluster's Nakama keys:

```bash
BASE=http://127.0.0.1:7350
SERVER_KEY=$(kubectl get secret fomoxa-nakama -n fomoxa -o jsonpath='{.data.server-key}' | base64 -d)
HTTP_KEY=$(kubectl get secret fomoxa-nakama -n fomoxa -o jsonpath='{.data.http-key}' | base64 -d)
echo "server-key=${SERVER_KEY:0:6}...  http-key=${HTTP_KEY:0:6}..."
```

The `server-key` lets players log in to Nakama. The `http-key` is only for the backend's internal RPCs. `base64 -d` decodes the value stored in the secret. If you are testing the `docker compose` Nakama instead of the cluster, use `SERVER_KEY=defaultkey; HTTP_KEY=defaulthttpkey`.

### 12.1 Log in two players and get tokens

```bash
CREDS=$(printf '%s:' "$SERVER_KEY" | base64)
login() {
  curl -s -X POST "$BASE/v2/account/authenticate/device?create=true" \
       -H "Authorization: Basic $CREDS" -H 'Content-Type: application/json' \
       -d "{\"id\":\"$1\"}" | grep -o '"token":"[^"]*' | cut -d'"' -f4
}
TOKEN_A=$(login fomoxa-test-ana)
TOKEN_B=$(login fomoxa-test-bo)
echo "token A: ${TOKEN_A:0:20}...   token B: ${TOKEN_B:0:20}..."
```

`authenticate/device` creates the account if needed, tied to a device id, and returns a JWT `token`. The header `Authorization: Basic <base64(server_key:)>` is how Nakama authenticates the application.

### 12.2 Clear the queue, then both ask for `arena:1`

```bash
HTTP_CREDS=$(printf '%s:' "$HTTP_KEY" | base64)

# (backend only) clear the old queue, which needs the http_key
curl -s -X POST "$BASE/v2/rpc/reset_match?unwrap" -H "Authorization: Basic $HTTP_CREDS" \
     -H 'Content-Type: application/json' -d '{"room":"arena:1"}'; echo

# ana asks first: expect "waiting"
curl -s -X POST "$BASE/v2/rpc/find_match?unwrap" -H "Authorization: Bearer $TOKEN_A" \
     -H 'Content-Type: application/json' -d '{"room":"arena:1"}'; echo

# bo asks second: expect "matched" with a ticket
RESP_B=$(curl -s -X POST "$BASE/v2/rpc/find_match?unwrap" -H "Authorization: Bearer $TOKEN_B" \
     -H 'Content-Type: application/json' -d '{"room":"arena:1"}')
echo "$RESP_B"
TICKET=$(echo "$RESP_B" | grep -o '"ticket":"[^"]*' | cut -d'"' -f4)
```

Expected output (real):

```
{"ok":true}
{"room":"arena:1","status":"waiting"}
{"exp":1793533330,"room":"arena:1","status":"matched","ticket":"f32f69a2-72cc-4ecb-b7ee-5c23eb0fa788"}
```

The first player joins the queue (`waiting`). The second completes the pair, so Nakama creates a ticket (`matched`, valid until `exp`).

### 12.3 The backend verifies the ticket (and rejects an outsider)

```bash
# the user id is inside the token (the middle part of the JWT, base64url)
uid_of() { echo "$1" | cut -d. -f2 | tr '_-' '/+' | awk '{print $0 substr("====",1,(4-length($0)%4)%4)}' | base64 -d 2>/dev/null | grep -o '"uid":"[^"]*' | cut -d'"' -f4; }
UID_B=$(uid_of "$TOKEN_B")

# the right person: ok:true
curl -s -X POST "$BASE/v2/rpc/verify_ticket?unwrap" -H "Authorization: Basic $HTTP_CREDS" \
     -H 'Content-Type: application/json' \
     -d "{\"ticket\":\"$TICKET\",\"room\":\"arena:1\",\"user_id\":\"$UID_B\"}"; echo

# a stranger: rejected
curl -s -X POST "$BASE/v2/rpc/verify_ticket?unwrap" -H "Authorization: Basic $HTTP_CREDS" \
     -H 'Content-Type: application/json' \
     -d "{\"ticket\":\"$TICKET\",\"room\":\"arena:1\",\"user_id\":\"00000000-0000-0000-0000-000000000001\"}"; echo
```

Expected output (real):

```
{"exp":1793533330,"ok":true,"room":"arena:1"}
{"message":"ticket khong thuoc user nay","ok":false}
```

### 12.4 Players must not call internal RPCs

```bash
curl -s -X POST "$BASE/v2/rpc/verify_ticket?unwrap" -H "Authorization: Bearer $TOKEN_A" \
     -H 'Content-Type: application/json' -d '{}'; echo
```

Expected output (real):

```
{"code":7,"error":{},"message":"chi backend duoc goi RPC nay"}
```

Code 7 is `PERMISSION_DENIED`. The RPCs `verify_ticket`, `reset_match`, `list_rooms`, `kv_get` and `kv_put` can only be called by the backend, which holds the `http_key`.

Or run it in one go: `./scripts/test-nakama.sh` performs 12.1 to 12.3 and checks the results automatically. It reads the keys from the `fomoxa-nakama` secret when Nakama runs in k3d. `./scripts/test-nakama-pool.sh` sends four players to "auto", and they must land in two different rooms.

Expected output of `test-nakama.sh` (real, the end):

```
3) ana find_match...
   -> {"room":"arena:1","status":"waiting"}
4) bo find_match...
   -> {"exp":1793533330,"room":"arena:1","status":"matched","ticket":"f32f69a2-72cc-4ecb-b7ee-5c23eb0fa788"}
5) ana poll_match...
   -> {"exp":1793533330,"room":"arena:1","status":"matched","ticket":"f32f69a2-72cc-4ecb-b7ee-5c23eb0fa788"}
6) Backend verify_ticket...
   -> {"exp":1793533330,"ok":true,"room":"arena:1"}
7) Verify voi user khac (phai bi tu choi)...
   -> {"message":"ticket khong thuoc user nay","ok":false}

OK: 2 client ghep dung room, ticket dung, user ngoai bi chan!
```

Expected output of `test-nakama-pool.sh` (real, the end):

```
   -> Cap 1 matched vao room: arena:1 (ticket: e0d102e4-...)
   -> Cap 2 matched vao room: arena:2 (ticket: 324b9b2d-...)
Chung minh thanh cong: Cap 1 (arena:1) va Cap 2 (arena:2) duoc cap 2 room khac nhau tu pool!
=== DoD P5.5 HOAN TAT THANH CONG! ===
```

Common errors:

| Symptom | Fix |
|---|---|
| The script stops saying port 7350 points at a compose container | `docker compose` still holds 7350: `docker compose -f deploy/compose.yaml down`. To test compose on purpose: `ALLOW_COMPOSE=1 ./scripts/test-nakama.sh` |
| `401 Unauthorized` on `authenticate/device` | Wrong `SERVER_KEY` (`defaultkey` against a cluster with its own keys, or the reverse) |
| `lua runtime error` | The Lua module failed: `kubectl logs deploy/nakama -n fomoxa \| grep -i lua` |
| The second player stays `waiting` | The two players asked for different rooms, or an old queue entry remains (rerun `reset_match`) |

## Step 13. Test with the Unity client (real players)

Goal: prove the whole path: client, backend, Nakama, directory, gateway, game server, and watch the Agones state change with the players.

13.1 Open the client (built in [04](04-UNITY-CLIENT.md)). The k3d cluster forwards 9600 and 7777 to `localhost`, which matches the client's defaults, so no flags are needed:

```powershell
.\Builds\MyGame.exe
```

In the game, register, save the recovery code, and click room `arena:1`. Nakama is on, so a specific room needs two players. Open a second client, register another name, and click the same room.

13.2 Watch the cluster side (a WSL2 terminal, while playing):

```bash
watch -n 2 kubectl get gs,fleet -n fomoxa
```

One GameServer should go from `Ready` to `Allocated`, and the Fleet's `ALLOCATED` column should rise from 0 to 1. The log of that pod:

```bash
kubectl logs -n fomoxa -l agones.dev/role=gameserver -c fomoxa-server --prefix --tail=30 | grep -E "Agones|peer"
```

```
[Pod] peer 1 la 'ana'
[Agones] first player joined -> Allocate
[Agones] player 1 connected (active: 1)
...
[Agones] room empty, shutting down in 30s unless a player returns
[Agones] Shutting down...
```

When the last player leaves and the room stays empty for 30 seconds, the pod shuts down and the Fleet creates a new `Ready` pod.

13.3 The automatic version (needs `Builds/MyGame.exe`): `./scripts/test-p5.sh`

What the script does, step by step:

1. `kubectl get gs -n fomoxa` confirms there are GameServers and no `Allocated` pods before it starts.
2. It calls the Windows PowerShell through WSL2 interop and runs `scripts/test-p5.ps1`, which starts four hidden copies of `MyGame.exe`: `-batchmode -nographics -autojoin -name:ana|bo|cai|dan -device:p5-k8s-<name> -room:arena:1|arena:1|arena:2|arena:2`. Each room needs exactly two players, so ana pairs with bo and cai with dan.
3. It waits for each client to print `mine=True` in its log (it joined a pod and owns a character).
4. It counts `Allocated` pods with `kubectl` (through `wsl.exe` when started from `test-p5.sh`). There must be exactly two, or the script fails.
5. With `TEST_SCALE=1 ./scripts/test-p5.sh` it also scales the Fleet up by one, then back, and checks the GameServer count.

Expected output (sample):

```
=== Kiem tra DoD P5: Game server chay tren K8s + Agones ===
   ana -> arena:1 ... mine=True (OK)
   bo -> arena:1 ... mine=True (OK)
   cai -> arena:2 ... mine=True (OK)
   dan -> arena:2 ... mine=True (OK)
So pod o trang thai Allocated: 2 (dung ky vong)
=== DoD P5 HOAN TAT THANH CONG! ===
```

Common errors are in [07](07-TROUBLESHOOTING.md): a client that cannot connect, one stuck on `waiting` (no second player), and a GameServer that never becomes `Allocated`.

## Step 14. Clean up

Pick one of three levels:

```bash
# A) Remove only the application; keep the cluster and Agones (quick to redeploy)
kubectl delete namespace fomoxa

# B) Delete the whole k3d cluster (Agones and the Postgres data go too)
k3d cluster delete fomoxa

# C) Also delete the Docker images you built
docker rmi fomoxa-server:latest fomoxa-directory:latest fomoxa-gateway:latest fomoxa-backend:latest
```

Or run it in one go: `./scripts/k8s-down.sh --keep-cluster` (level A) or `./scripts/k8s-down.sh` (level B).

The keys in `deploy/secrets/` are not removed by these commands. Delete them yourself with `rm -rf deploy/secrets` if you want.

## One-command shortcut

Once you understand the steps, build the whole cluster with:

```bash
./scripts/k8s-up.sh --build
```

What the script does:

| Part of `k8s-up.sh` | Matches |
|---|---|
| checks `docker info`; installs k3d and helm if missing | [01](01-PREPARE-ENVIRONMENT.md) |
| checks ports 7777, 9600, 9643, 7350 are free; `k3d cluster create fomoxa ...` | Steps 1, 5 |
| `helm repo add/update`, `helm install agones ...` | Step 6 |
| `--build` runs `scripts/build-images.sh` | Steps 3, 4 |
| `k3d image import ...` | Step 7 |
| `kubectl apply` namespace, then `scripts/gen-k8s-secrets.sh` (which calls `gen-keys.sh` and `gen-tls-cert.sh` if needed) | Steps 2, 8 |
| `kubectl apply` postgres, nakama, directory, gateway, backend, tls-proxy, fleet | Step 9 |
| `kubectl rollout status` for each deployment, then `kubectl get gs` | Steps 9, 10 |

Optional environment variables, set before the command, for example `AUTOSCALE=1 INSTALL_MONITORING=1 ./scripts/k8s-up.sh`:

| Variable | Effect | Details |
|---|---|---|
| `AGONES_VERSION=1.xx.y` | pins the Agones chart version | Step 6 |
| `AUTOSCALE=1` | turns on the `FleetAutoscaler` (keeps 2 Ready pods, 4 to 16 pods) | [05](05-OPERATIONS.md#autoscaling) |
| `INSTALL_MONITORING=1` | installs Prometheus, Alertmanager, Grafana and Loki | [05](05-OPERATIONS.md#monitoring) |
| `APPLY_NETPOL=1` | turns on NetworkPolicy (not yet tried on a real cluster) | [05](05-OPERATIONS.md#networkpolicy-optional) |
| `TLS_ONLY=1` | closes unencrypted port 9600, leaving only TLS on 9643 | [05](05-OPERATIONS.md#tls-between-client-and-backend) |
| `APPLY_INGRESS=1` | applies the experimental Ingress | [08](08-REFERENCE.md) |

## Checklist

- [ ] `deploy/secrets/` has 14 files and `root` is exactly 64 characters
- [ ] `docker images` shows four `fomoxa-*:latest` images
- [ ] `kubectl get nodes` is `Ready`; `docker port k3d-fomoxa-serverlb` shows 7777/udp, 9600, 9643, 7350
- [ ] `kubectl get pods -n agones-system` shows the controller and extensions `Running`
- [ ] Six secrets exist in the `fomoxa` namespace
- [ ] `kubectl get pods -n fomoxa` shows every pod `Running`, game servers `2/2`
- [ ] `kubectl get fleet,gs -n fomoxa` shows `READY 4` and GameServers `Ready`
- [ ] Game server logs contain `registered`, `claimed room` and `[Backend] da ket noi`
- [ ] `./scripts/test-nakama.sh` ends with `OK: 2 client ghep dung room...`
- [ ] A Unity client joins a room and the GameServer becomes `Allocated`
- [ ] `public_address` in `directory-config` is `127.0.0.1:7777` (local setup)

Next: [04 Unity client](04-UNITY-CLIENT.md) and [05 Operations](05-OPERATIONS.md).
