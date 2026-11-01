# 03.9 Reading the manifests in `deploy/k8s/`

Up to the index: [03-DEPLOY-KUBERNETES](README.md) · Previous: [08-test-client-and-cleanup.md](08-test-client-and-cleanup.md)

This page walks through each YAML file that [step 9](05-apply-manifests.md) applies: what it creates, which fields matter, and what to change when you need to. For the background concepts see [00](concepts.md).

There are eleven files in `deploy/k8s/`:

| File | Creates | Applied in |
|---|---|---|
| `namespace.yaml` | namespace `fomoxa` | step 8 |
| `postgres.yaml` | PVC, Deployment, Service `postgres` | step 9.1 |
| `nakama.yaml` | ConfigMap `nakama-modules`, Deployment, Services `nakama` and `nakama-console` | step 9.2 |
| `directory.yaml` (+ `directory.k8s.toml`) | ConfigMap `directory-config`, Deployment, Service `directory` | step 9.3 |
| `gateway.yaml` (+ `gateway.k8s.toml`) | ConfigMap `gateway-config`, Deployment, Service `gateway` | step 9.4 |
| `backend.yaml` | Deployment, Services `backend` and `backend-pods` | step 9.5 |
| `tls-proxy.yaml` | ConfigMap, Deployment, Service `tls-proxy` | step 9.6 |
| `fleet.yaml` | Fleet `fomoxa-server` | step 9.7 |
| `fleet-autoscaler.yaml` | FleetAutoscaler (optional) | `AUTOSCALE=1` |
| `networkpolicy.yaml` | NetworkPolicy (optional) | `APPLY_NETPOL=1` |
| `ingress.yaml` | experimental Ingress (optional) | `APPLY_INGRESS=1` |

The `*.k8s.toml` files are the source copies of the config embedded in the ConfigMaps. If you edit one, edit the other too (`k8s-up.sh` uses the copy inside the YAML).

## Patterns that repeat in every file

Read these once and the rest only differ in detail.

The identity block. Every resource starts with:

```yaml
apiVersion: apps/v1     # API group; Deployment is apps/v1, Service is v1
kind: Deployment        # resource type
metadata:
  name: backend         # name, unique within the namespace
  namespace: fomoxa
```

Labels and selectors. A Deployment finds its pods by label, and so does a Service:

```yaml
selector:
  matchLabels: { app: backend }     # Deployment: "which pods are mine"
template:
  metadata:
    labels: { app: backend }        # label put on the pod
```

A Service selector (`app: backend`) must match the pod label. If it does not, `kubectl get endpoints` is empty and connections are refused.

Resources. `requests` is the reservation used to place the pod on a node, `limits` is the ceiling. Going over the memory limit kills the container (`OOMKilled`). Going over the CPU limit slows it down.

Container hardening. `directory`, `gateway`, `backend`, `tls-proxy` and the game server all have (postgres and nakama do not):

```yaml
securityContext:
  allowPrivilegeEscalation: false      # no privilege escalation
  capabilities: { drop: ["ALL"] }      # drop all special Linux capabilities
  seccompProfile: { type: RuntimeDefault }   # filter syscalls with the runtime default
```

`imagePullPolicy: IfNotPresent`. Use the image already on the node. The `fomoxa-*:latest` images only exist because you ran `k3d image import`. With the default policy and the `latest` tag, Kubernetes would try to pull from a registry and fail with `ImagePullBackOff`.

Mounting secrets and config. Always a pair: `volumes` (the source) and `volumeMounts` (the path inside the container), with `readOnly: true`.

## `namespace.yaml`

Five lines that create namespace `fomoxa`. It has to be applied before anything else because the other files set `namespace: fomoxa`.

## `postgres.yaml`

| Part | Value | Meaning |
|---|---|---|
| PVC `postgres-data` | `ReadWriteOnce`, 1 GiB | one disk, attached to one node at a time |
| image | `postgres:15` | official image |
| `POSTGRES_DB` | `nakama` | creates the database Nakama uses |
| `POSTGRES_PASSWORD` | from secret `fomoxa-nakama`, key `db-password` | not stored in the YAML |
| volumeMount | `/var/lib/postgresql/data` | where Postgres writes data |
| probes | `pg_isready -U postgres -d nakama` | ready after 5 seconds, liveness after 15 |

Postgres only reads `POSTGRES_PASSWORD` the first time, while the data directory is empty. Changing the secret later does not change the password inside an existing database. See [05 part 3](../operations/03-tls-and-keys.md) for the right way to rotate it.

## `nakama.yaml`

This is the longest file (475 lines) because it embeds all of `main.lua` in the ConfigMap `nakama-modules`. The rest is short.

Two copies of `main.lua`. The original is `deploy/nakama/modules/main.lua` (used by docker compose). The copy in the ConfigMap must be identical, and CI checks that. To change the Lua, edit the original, copy it into the YAML, run `kubectl apply`, then `rollout restart deployment/nakama`.

The `migrate` init container. It runs `nakama migrate up` before the main container to create or update tables. If Postgres is not ready it retries until it passes. The pod shows `Init:0/1`, and `Init:Error` or `Init:CrashLoopBackOff` when something is wrong (read it with `kubectl logs <pod> -c migrate`).

Nakama command-line arguments. Keys enter through environment variables, then `$(NAME)` substitutes them into the arguments:

| Argument | Source |
|---|---|
| `--database.address` | `postgres:$(DB_PASSWORD)@postgres:5432/nakama` |
| `--socket.server_key` | secret `server-key` (used by the Unity client) |
| `--runtime.http_key` | secret `http-key` (used by the backend for RPC) |
| `--session.encryption_key`, `--session.refresh_encryption_key` | secrets `session-key`, `refresh-key` |
| `--console.password` | secret `console-password` |
| `--session.token_expiry_sec` | 7200 (two hours) |
| `--metrics.prometheus_port` | 9100 |

Two Services. `nakama` (7350, `LoadBalancer`) lets your machine call the API. `nakama-console` (7351, `ClusterIP`) is only reachable through `port-forward`, because the admin console should not be exposed.

Changing the Nakama version. The tag `3.41.0` appears twice (init container and main container). Change both.

## `directory.yaml`

The ConfigMap `directory-config` holds `directory.toml`:

| Key | Value | Meaning |
|---|---|---|
| `listen` | `0.0.0.0:9500` | port the backend, gateway and pods connect to |
| `admin_listen` | `0.0.0.0:9501` | admin port |
| `public_address` | `127.0.0.1:7777` | the gateway address clients see, written into the ConnectToken |
| `grace_seconds` | 30 | wait before a pod is considered lost |
| `report_interval_seconds` | 5 | how often pods report |
| `drain_seconds` | 300 | longest a pod may take to drain |
| `ticket_seconds` | 30 | a room ticket expires after 30 seconds |
| `[keys]` | five keys read from `/secrets/directory/...` | `backend`, `pod`, `gateway`, `admin`, `root` |
| `[[placement]]` | three pod selection rules | same `version`; prefer same `region`; pack pods (`packed`) |

`public_address` is the value you change when you move to a real server (see [05 part 5](../operations/05-scaling-security-backup.md)). For k3d on your machine, `127.0.0.1:7777` is right because k3d maps that port to your host.

The Deployment uses image `fomoxa-directory:latest`, argument `/config/directory.toml`, two ports, and mounts the ConfigMap and the Secret `fomoxa-directory-keys` (all five keys) read-only. The probe is a `tcpSocket` on port 9500.

## `gateway.yaml`

The gateway accepts UDP from clients and forwards it to the chosen pod.

| `gateway.toml` key | Meaning |
|---|---|
| `listen = "0.0.0.0:7777"` | UDP from clients |
| `pod_bind = "0.0.0.0:0"` | ephemeral port for talking to pods |
| `directory = "directory.fomoxa.svc.cluster.local:9500"` | ask the directory which pod takes a ticket |
| `retry_seconds = 1` | retry when nothing is available yet |

The security detail: the secret `fomoxa-directory-keys` holds five keys, but the gateway mounts only one (`items: - key: gateway`). If the gateway is compromised, the attacker cannot reach the `root` or `admin` key.

The `gateway` Service is a `LoadBalancer` on UDP 7777. It is the only game port a client touches.

## `backend.yaml`

Configuration comes from environment variables using the .NET convention: `__` stands for a nesting level, so `Fomoxa__Nakama__Url` maps to `Fomoxa:Nakama:Url`.

| Variable | Value | Meaning |
|---|---|---|
| `Fomoxa__Directory__Address` | `directory...:9500` | where to register |
| `Fomoxa__Directory__Key__File` | `/secrets/backend/backend` | backend to directory key |
| `Fomoxa__Pods__Key__File` | `/secrets/backend/backend-pod` | backend to pod key |
| `Fomoxa__Nakama__Url` | `http://nakama...:7350` | Nakama RPC calls |
| `Fomoxa__Nakama__ServerKey`, `HttpKey` | from secret `fomoxa-nakama` | authenticate to Nakama |
| `Fomoxa__Players__Tcp` / `WebSocket` | `0.0.0.0:9600` / `9601` | ports for clients |
| `Fomoxa__Pods__Tcp` | `0.0.0.0:9602` | port for pod reports |
| `Fomoxa__Metrics__Listen` | `0.0.0.0:9610` | `/metrics` for Prometheus |

There are two Services. `backend` (`LoadBalancer`, 9600 and 9601) is for clients. `backend-pods` (`ClusterIP`, 9602) is for pods only. Splitting them keeps port 9602 from ever leaving the cluster.

The probe runs `fomoxa-rpc-health.dll`, which opens an fRPC connection to `127.0.0.1:9600`. It checks that the backend answers RPC calls, which is stronger than checking that the port is open.

The `prometheus.io/*` annotations let Prometheus discover port 9610 (see [05 part 4](../operations/04-monitoring.md)).

## `tls-proxy.yaml`

For the whole path from the client to the backend and where the certificate is configured, see [Traffic path and where TLS is configured](traffic-and-tls.md).

Nginx acts as the lock in front of the backend. The client connects over TLS to 9643, nginx decrypts, and forwards raw bytes to `backend:9600`.

```
client ──TLS──► tls-proxy:9643 ──raw TCP──► backend:9600
```

You need it because the backend's fRPC does not encrypt the client leg, and login passwords travel over it. Two details in `nginx.conf`:

- it uses a `stream { }` block because this is raw TCP;
- `proxy_timeout 1h` keeps long play sessions open.

The certificate comes from the secret `fomoxa-tls`, mounted at `/tls`. The `nginx-unprivileged` image runs without root, so its `pid` file goes to `/tmp`.

## `fleet.yaml`

Fleet `fomoxa-server` with `replicas: 4`. The fields that matter:

| Field | Value | Meaning |
|---|---|---|
| `ports[0]` | UDP 7778, `portPolicy: None` | the pod's game port; `None` because the gateway reaches the pod IP and no node port mapping is needed |
| `health` | delay 10, period 5, threshold 3 | Agones treats the pod as broken after three failed checks in a row |
| `terminationGracePeriodSeconds` | 360 | six minutes to drain before a hard kill |
| `FOMOXA_DIRECTORY` | directory address | where the pod registers |
| `POD_IP`, `FOMOXA_POD_NAME` | from `fieldRef` | Kubernetes fills in the pod IP and name |
| `FOMOXA_AGONES` | `true` | turn on Agones SDK calls (`Ready`, `Allocated`, `Shutdown`) |
| `FOMOXA_CLAIM_ONE` | `true` | each pod holds exactly one room |
| `FOMOXA_ROOM` | `arena:1` to `arena:16` | rooms a pod may hold; each pod takes one that is free |
| `FOMOXA_BACKEND` | `backend-pods...:9602` | where to report player counts and scoreboards |
| `FOMOXA_METRICS` | `0.0.0.0:9611` | the pod's `/metrics` |

`FOMOXA_ROOM` lists 16 rooms because the `FleetAutoscaler` allows up to 16 pods. With fewer rooms, pod number 17 would have nothing to hold.

The Fleet uses two secrets: `fomoxa-pod-key` (pod to directory) and `fomoxa-backend-pod-key` (pod to backend).

## `fleet-autoscaler.yaml` (optional)

Twenty-two lines. A `Buffer` policy keeps 2 spare `Ready` pods, with a minimum of 4 and a maximum of 16, checked every 15 seconds. The arithmetic and a test are in [05 part 5](../operations/05-scaling-security-backup.md). Turn it on with `AUTOSCALE=1`.

## `networkpolicy.yaml` (optional)

132 lines. The first policy, `default-deny-ingress`, blocks all traffic going into pods in the namespace. The later policies open each needed path (for example `postgres-from-nakama` lets only `nakama` pods reach port 5432). The paths opened are client to gateway (UDP 7777) to pod (UDP 7778); client to backend (9600, 9601); backend to directory (9500) and nakama (7350); gateway and pods to directory (9500); pods to backend (9602).

k3s ships with a NetworkPolicy enforcer (kube-router). If something stops connecting, remove it with `kubectl delete -f deploy/k8s/networkpolicy.yaml`. Turn it on with `APPLY_NETPOL=1`. The full flow table is in [05 part 5](../operations/05-scaling-security-backup.md).

## `ingress.yaml` (optional, experimental)

An Ingress for the `traefik` class (Traefik is the HTTP router bundled with k3s), host `fomoxa.local`, path `/ws` to the `backend` Service on port 9601 (WebSocket). The file itself lists its limits:

- no TLS yet;
- it only forwards HTTP and WebSocket. Port 9600 is fRPC over raw TCP and cannot pass through an Ingress, so clients still connect straight to the `backend` Service;
- `k8s-up.sh` does not apply it by default (it needs `APPLY_INGRESS=1`).

## Checking a manifest without a cluster

```bash
kubectl apply --dry-run=client -f deploy/k8s/backend.yaml
```

`--dry-run=client` only checks syntax on your machine and needs no cluster. `--dry-run=server` (needs a cluster) lets the API server validate the rules too. The Fleet needs Agones installed for this.

To see what a change would do before applying it:

```bash
kubectl diff -f deploy/k8s/directory.yaml
```

Lines starting with `-` are the current state and `+` is what you would get. No output means the cluster already matches the file.
