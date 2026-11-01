# 07 Troubleshooting

Vietnamese version: [../07-XU-LY-SU-CO.md](../07-XU-LY-SU-CO.md)

Find your symptom, read the cause, then run the fix. If you do not know where the problem is, start with the [quick diagnostic commands](#quick-diagnostic-commands).

Contents: [Diagnostics](#quick-diagnostic-commands), [1 Docker, k3d, kubectl](#1-docker-k3d-kubectl), [2 Pods and images](#2-pods-and-images), [3 Agones and game servers](#3-agones-and-game-servers), [4 Nakama](#4-nakama), [5 Backend and accounts](#5-backend-and-accounts), [6 Client connection](#6-client-connection), [7 TLS](#7-tls), [8 Monitoring and NetworkPolicy](#8-monitoring-and-networkpolicy), [9 Mock cluster and tests](#9-mock-cluster-and-tests)

## Quick diagnostic commands

```bash
kubectl config current-context                       # must be k3d-fomoxa
kubectl get nodes
kubectl get pods -A | grep -vE 'Running|Completed'   # every pod in any namespace that is NOT in a good state
kubectl get pods,svc -n fomoxa
kubectl get fleet,gs -n fomoxa
kubectl get events -n fomoxa --sort-by=.lastTimestamp | tail -15
docker ps --format '{{.Names}}\t{{.Status}}\t{{.Ports}}'
docker port k3d-fomoxa-serverlb
free -h
```

How to read the results: the first line must print `k3d-fomoxa`. The third command should print nothing (or only pods that are starting). `docker port` must list `7777/udp`, `9600`, `9643` and `7350`. If `available` in `free -h` is below about 2 GB, memory is a problem.

## 1. Docker, k3d, kubectl

### `Cannot connect to the Docker daemon`

Cause: on WSL2, Docker Desktop is not running or WSL Integration is off. On Linux, the service is stopped.

Fix: on WSL2, open Docker Desktop, go to Settings, Resources, WSL Integration, turn it on for Ubuntu, and choose Apply & Restart. On Linux run `sudo systemctl start docker`.

Check: `docker info --format '{{.ServerVersion}}'` prints a version.

### `permission denied while trying to connect to the Docker daemon socket`

Fix: `sudo usermod -aG docker "$USER" && newgrp docker`, or log out and in again.

### `port is already allocated`, or the cluster cannot be created

Cause: 7777, 9600, 9643 or 7350 is held by something else (the compose Nakama, an old cluster, the mock cluster, another application).

Fix:

```bash
docker ps --format '{{.Names}}\t{{.Ports}}' | grep -E ':(7777|9600|9643|7350)->'      # which container holds it?
ss -ltnup | grep -E ':(7777|9600|9643|7350)\b'                                          # which process holds it?
docker compose -f deploy/compose.yaml down        # if it is the compose Nakama
k3d cluster delete fomoxa                         # if it is an old cluster
```

### `kubectl` shows the wrong thing or finds no resources

Cause: `kubectl` points at another cluster. This is very common when you have several.

Fix: `kubectl config get-contexts`, then `kubectl config use-context k3d-fomoxa`. Add `-n fomoxa` to every command, or set a default: `kubectl config set-context --current --namespace=fomoxa`.

### `k3d image import` takes very long

That is normal for images of a few hundred MB. Wait. To confirm the result: `docker exec k3d-fomoxa-server-0 crictl images | grep fomoxa`.

### The cluster is missing ports (the client cannot reach 9600 or 7777)

Cause: k3d accepts `--port` only when the cluster is created.

Fix: delete and recreate it: `k3d cluster delete fomoxa`, then repeat [03, Steps 5 to 9](k8s/03-cluster-and-agones.md#step-5-create-the-k3d-cluster).

## 2. Pods and images

| Pod state | Common cause | Fix |
|---|---|---|
| `ImagePullBackOff` or `ErrImagePull` | the `fomoxa-*:latest` image is not in k3d (these images are not on Docker Hub) | `k3d image import fomoxa-<name>:latest -c fomoxa`, then `kubectl rollout restart deployment/<name> -n fomoxa` |
| `CreateContainerConfigError` | a secret or a key in a secret is missing | `kubectl describe pod <pod> -n fomoxa` (the `Error:` line names the missing secret or key); redo [03, Step 8](k8s/04-secrets.md#step-8-namespace-and-secrets) |
| `CrashLoopBackOff` | the application exits immediately | `kubectl logs <pod> -n fomoxa --previous` shows why |
| `Pending` for a long time | not enough CPU or RAM on the node | `kubectl describe pod <pod> -n fomoxa` (Events: `Insufficient memory`); reduce pods (`kubectl scale fleet ... --replicas=2`) or give Docker or WSL2 more RAM |
| `Init:Error` (nakama) | the `migrate` init container cannot reach Postgres | see [Nakama](#4-nakama) |
| `Evicted` or `OOMKilled` | out of memory, or over the `limits` | `kubectl describe pod ...` (Last State); add RAM or raise `limits` in the manifest |

After every image rebuild, remember to import and restart. A Fleet must be deleted and applied again ([05](ops/01-status-and-updates.md#updating-after-rebuilding-an-image)). The sign that you forgot: you changed the code and the behavior did not change.

## 3. Agones and game servers

### `helm repo add` returns 404

You used the old URL. The correct command is `helm repo add agones https://agones.dev/chart/stable --force-update`.

### `helm install` times out (`timed out waiting for the condition`)

Cause: the Agones pods did not come up (slow image pull), or the allocator is fighting Traefik for port 443 (turned off with `agones.allocator.install=false`).

Fix:

```bash
kubectl get pods -n agones-system
kubectl describe pod <pod> -n agones-system | tail -20
helm uninstall agones -n agones-system && helm install agones agones/agones --namespace agones-system \
    --set "agones.allocator.install=false" --set "agones.ping.install=false" --wait --timeout 10m
```

### A GameServer stays in `Starting` and never reaches `Ready`

Cause: the game container cannot reach the Agones sidecar or the directory.

Fix:

```bash
GS=$(kubectl get gs -n fomoxa -o name | head -1 | cut -d/ -f2)
kubectl logs "$GS" -n fomoxa -c fomoxa-server --tail=30
kubectl logs "$GS" -n fomoxa -c agones-gameserver-sidecar --tail=30
```

Look for `Connection refused` to `directory...:9500` (the directory is not up, or the `pod` key differs between `fomoxa-pod-key` and `fomoxa-directory-keys`), and for `ReadyAsync attempt ...` (the sidecar is not ready yet; wait up to 30 seconds).

### A game server is `Ready` but never `Allocated`

Cause: no player has actually connected over UDP to the pod yet. `Allocated` happens when the first player connects, not when someone requests a room.

Fix: see [the client cannot connect](#6-client-connection). The pod log must show `[Pod] peer N la '<name>'` and then `[Agones] first player joined -> Allocate`.

### A pod goes to `Shutdown` about 30 seconds after everyone leaves

That is expected. A room that stays empty for 30 seconds shuts its pod down (`[Agones] room empty, shutting down`), and the Fleet creates a new `Ready` pod. If a player comes back within 30 seconds, the shutdown is cancelled.

### Many pods but few rooms

Each pod holds one room out of `arena:1..16`. Beyond 16 pods the extra pods have no room and cannot be used. Do not set `maxReplicas` above 16 unless you extend `FOMOXA_ROOM` in `fleet.yaml`.

## 4. Nakama

### `lua runtime error`, or an RPC returns a strange error

```bash
kubectl logs deploy/nakama -n fomoxa | grep -iE 'lua|error' | tail
```

Cause: usually you changed `main.lua` but not the ConfigMap in `nakama.yaml` (or the reverse), or the Lua has a syntax error.

Fix: catch the mismatch with the [static checks](06-TESTING.md#5-static-checks-generated-code-yaml-shell-lua-monitoring-config). After fixing, run `kubectl apply -f deploy/k8s/nakama.yaml && kubectl rollout restart deployment/nakama -n fomoxa`.

### Matchmaking: one player stays `waiting`

Cause: a specific room such as `arena:1` needs exactly two players to ask for that same room within 30 seconds. Or the two asked for different rooms, or an old queue entry remains.

Fix: use "Auto matchmaking" (`auto`), or clear the queue with `reset_match` (needs the `http_key`). See [03, Step 12.2](k8s/07-test-nakama.md#step-12-test-nakama-with-curl).

### A player who just finished a match asks for another room and lands in the old one

Fixed in `main.lua`: polling no longer returns the result of the previous match. If you still see it, check that Nakama runs the new `main.lua` (compare the ConfigMap and restart Nakama).

### `401 Unauthorized` from Nakama

The `server-key` or `http-key` is wrong. The Kubernetes cluster uses its own keys in the secret `fomoxa-nakama`, while `docker compose` uses `defaultkey` and `defaulthttpkey`. See [03, Step 12](k8s/07-test-nakama.md#step-12-test-nakama-with-curl).

### `{"code":7,...,"message":"chi backend duoc goi RPC nay"}`

That is expected. Players (a Bearer token) cannot call internal RPCs. Only the backend, which holds the `http_key`, can.

### Changing the DB password stops Nakama from starting

Symptom: the `nakama` pod is `Init:Error` or `CrashLoopBackOff`, and the log says `password authentication failed for user "postgres"`.

Cause: the `postgres` password is stored inside the data volume the first time it is initialized. Changing the secret's `db-password` later does not change the password inside the database.

Fix, losing the data:

```bash
kubectl delete deployment nakama postgres -n fomoxa
kubectl delete pvc postgres-data -n fomoxa
kubectl apply -f deploy/k8s/postgres.yaml -f deploy/k8s/nakama.yaml
```

Or keep the data: set the secret back to the old value (the old `deploy/secrets/nakama/db-password`), or first change the password in Postgres with `kubectl exec -n fomoxa deploy/postgres -- psql -U postgres -c "ALTER USER postgres PASSWORD '<new-password>';"` and only then update the secret.

### The Nakama console (7351) is unreachable

The console is not exposed. Use `kubectl port-forward -n fomoxa svc/nakama-console 7351:7351`. The password is in the secret (see [05, section 2](ops/01-status-and-updates.md#2-reaching-internal-services)).

## 5. Backend and accounts

### Login says the account store or Nakama is not responding

The backend cannot talk to Nakama, where accounts are stored.

Fix: `kubectl get pods -n fomoxa | grep nakama`; `kubectl logs deploy/backend -n fomoxa | grep -i nakama`; check that the backend's `Fomoxa__Nakama__HttpKey` matches the secret's `http-key` (the backend takes it from the same `fomoxa-nakama` secret, so they differ only if you edited something by hand).

### "Nakama đang bật: hãy đăng nhập bằng tài khoản" (Nakama is on: log in with an account)

A backend with Nakama refuses a name-only login. Use the Register or Login tab (with a password), or the guest form `-autojoin ... -device:<id>`.

### "tên này đã có người dùng", "sai tên hoặc mật khẩu", or a too-many-tries lock

The name is already registered (case-insensitive), the password is wrong, or five wrong tries in a row locked that name for 60 seconds. Wait a minute.

### Forgot the password and lost the recovery code

No tool resets it by hand. As a workaround, delete that name's `account` record in the Nakama console (Storage) so it can be registered again ([05, section 7](ops/02-drain-and-accounts.md#7-accounts-and-player-data)).

### The recovery code does not show after registering

The code is kept for only 10 minutes in backend memory, and the client takes it once, right after registering. If the client closes first, or the backend restarts at that moment, it is lost. Change the password while you are still logged in, or register another account.

### The scoreboard is empty or records are not saved

The pod must reach the backend on port 9602 (the pod log must contain `[Backend] da ket noi`). If not, check the secret `fomoxa-backend-pod-key` and the service `backend-pods`: `kubectl get svc backend-pods -n fomoxa`. Guests (no account) still appear on the scoreboard, but their records are not saved.

## 6. Client connection

### Login works but joining a room hangs at "connecting to the game server"

The most common cause is a wrong `public_address`. The directory gives this address to the client, and on a local setup it must be `127.0.0.1:7777`.

Fix:

```bash
kubectl get configmap directory-config -n fomoxa -o jsonpath='{.data.directory\.toml}' | grep public_address
docker port k3d-fomoxa-serverlb | grep 7777
```

You should see `public_address = "127.0.0.1:7777"` and `7777/udp -> 0.0.0.0:7777`. For a client on another machine, see [05, section 14](ops/05-scaling-security-backup.md#14-changing-the-public-address-public_address).

### A Windows client cannot reach a cluster in WSL2

Symptom: TCP 9600 logs in, but UDP 7777 never arrives.

Cause: WSL2's `localhost` forwarding is not reliable for UDP. Docker Desktop (with WSL Integration) publishes container ports straight onto Windows `localhost`, UDP included.

Fix: use Docker Desktop instead of a Docker Engine you installed inside WSL2 ([01, Step 3A](01-PREPARE-ENVIRONMENT.md#3a-wsl2-ubuntu-on-windows-docker-desktop)). From Windows PowerShell, `Test-NetConnection 127.0.0.1 -Port 9600` checks TCP. Alternatively run the client inside WSL2 or Linux, or use the mock cluster ([02](02-QUICKSTART-MOCK-CLUSTER.md)).

### "Không có game server chạy phiên bản X" (no game server runs version X)

The client version (Player Settings Version, default `1.0`) differs from the pod's `FOMOXA_VERSION` (in `fleet.yaml`, default `1.0`). The directory pairs only pods of the same version (`filter = "pod.version == client.version"`). The mock cluster skips this check by default.

### Handshake or schema errors; an old `MyGame.exe` cannot get in

The old client build has a different protocol fingerprint from the new backend and pod. Rebuild all three together ([04, regenerating code](04-UNITY-CLIENT.md#regenerating-code-after-changing-a-model-or-rpc)).

### You enter a room but see nobody, or have no character

The prefab is not registered with the `NetworkManager`. Run Tools, Demo, Rebuild Scene again, and the Console must show no `[Demo] prefab:` error ([04](04-UNITY-CLIENT.md#step-2-build-the-two-scenes-rebuild-scene)).

### Space leaves the room

The HUD was visible and a button had focus. The current build hides the HUD while you play and the buttons do not take keyboard focus. If you still see it, rebuild the client from the latest code.

### The camera does not follow the character

Check the Console for `[Player] spawn on client ... mine=True`. If it is missing, your character has not spawned (the connection did not reach the pod). The line `[Camera] chưa thấy nhân vật của mình sau 8 giây` confirms that.

## 7. TLS

| Symptom | Cause | Fix |
|---|---|---|
| "không kết nối được backend: TlsRejected (… không khớp)" (cannot connect: TLS rejected, fingerprint mismatch) | the client's pin differs from the certificate the server presents | Get the right pin: `openssl s_client -connect 127.0.0.1:9643 </dev/null 2>/dev/null \| openssl x509 -noout -fingerprint -sha256`, and remove the colons. Or `./scripts/gen-tls-cert.sh` prints the pin of the existing file. If you regenerated the certificate, update the pin in every client |
| "không kết nối được backend: Failed" with `-backend:...:9643` | the TLS proxy is not running or port 9643 is not mapped | `kubectl get pods -n fomoxa \| grep tls-proxy`; `docker port k3d-fomoxa-serverlb \| grep 9643` (a cluster created before port 9643 existed must be recreated) |
| `tls-proxy` is in `CrashLoopBackOff` | the `fomoxa-tls` secret is missing or wrong | `kubectl get secret fomoxa-tls -n fomoxa`; recreate it ([03, Step 8](k8s/04-secrets.md#step-8-namespace-and-secrets)) |
| You want a public CA certificate but the client still asks for a pin | leave the pin empty and pass `-tls` | [05, section 8.4](ops/03-tls-and-keys.md#84-production-with-a-domain-name) |

## 8. Monitoring and NetworkPolicy

### A Prometheus target is `DOWN`, or there are no targets

Cause: the pod lacks the annotation `prometheus.io/scrape: "true"`, a NetworkPolicy blocks the `monitoring` namespace, or the pod has not opened its metrics port.

Fix: `kubectl get pod <pod> -n fomoxa -o jsonpath='{.metadata.annotations}'`. The backend uses port 9610 and game servers use 9611. If you turned on NetworkPolicy, the policy `metrics-from-monitoring` must exist (see `deploy/k8s/networkpolicy.yaml`).

### Something stopped connecting after turning on NetworkPolicy

Remove it right away to confirm the cause: `kubectl delete -f deploy/k8s/networkpolicy.yaml`. If the problem goes away, the policy is missing a path. See the list of paths at the top of that file. It has not been tried on a real cluster.

### Grafana shows no data

Go to Connections, Data sources, Prometheus, and choose Save & test. Look at Prometheus Status, then Targets first.

## 9. Mock cluster and tests

| Symptom | Fix |
|---|---|
| `Address already in use` when starting the mock cluster | `ss -ltnp \| grep -E '9600\|9500\|9643\|9602'`; stop the running mock or k3d cluster, or change ports with `--players=`, `--directory=` and the like |
| `--selftest` fails at "register a duplicate name" against a **real** backend | accounts persist; the selftest already uses random names, so rerun it |
| `test-nakama.sh` stops: "port 7350 points at a compose container" | You would be testing the compose Nakama. Stop compose, or do it on purpose: `ALLOW_COMPOSE=1 ./scripts/test-nakama.sh` |
| `test-p5`: "Truoc khi join da co N pod Allocated" (N pods were already Allocated) | Old players remain; wait for empty pods to shut down in 30 seconds, or `kubectl delete gs --all -n fomoxa` |
| `test-p5` reports "Ky vong 2 pod Allocated nhung thuc te la 0" (expected 2, got 0) | The clients did not reach a pod: see [section 6](#6-client-connection) and read `logs/p5-k8s-*.log` |
| `test-real-backend.sh`: `Connection refused` right at the start of the selftest | The backend was not up yet (raise `sleep 6` in the script), or port 9600 or 9500 is taken |
| `dotnet publish` fails on NuGet | The machine cannot reach `api.nuget.org`; check the network or proxy |
| A .NET image build reports `NU1301` | The build container cannot reach the Internet (a proxy). Configure a proxy for the Docker daemon |

## If you still cannot find it

Collect this and send it with your report:

```bash
kubectl get pods,svc,fleet,gs -n fomoxa -o wide
kubectl describe pod <failing-pod> -n fomoxa | tail -40
kubectl logs <failing-pod> -n fomoxa --tail=100
git rev-parse --short HEAD
```

For the client, send Unity's log file (on Windows `%USERPROFILE%\AppData\LocalLow\<Company>\MyGame\Player.log`) or the `-logFile` you passed.
