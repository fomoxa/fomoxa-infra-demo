# 05 Operations

Vietnamese version: [../05-VAN-HANH.md](../05-VAN-HANH.md)

Day-to-day tasks and the operational features: logs, scaling, updates, graceful shutdown (drain), accounts, TLS, Nakama keys, monitoring, autoscaling, NetworkPolicy and backups. This document assumes a running cluster from [03](03-DEPLOY-KUBERNETES.md), your shell in `~/fomoxa-infra-demo`, and `kubectl config use-context k3d-fomoxa`.

Run while writing: the Nakama and RPC commands (`kv_get`), `openssl s_client` against a certificate, `/metrics`, and the `promtool`, `amtool`, `loki` and `promtail` config checks. Not run on a real Kubernetes cluster: the `kubectl` commands below (output labeled sample), and the manifests for `tls-proxy`, `networkpolicy`, `fleet-autoscaler` and `monitoring/`.

Contents: [1 Status and logs](#1-status-and-logs), [2 Internal services](#2-reaching-internal-services), [3 Scale](#3-scaling-the-fleet-by-hand), [4 Restart](#4-restarting-a-service), [Updating images](#updating-after-rebuilding-an-image), [6 Drain](#6-shutting-down-a-game-server-safely-drain), [7 Accounts](#7-accounts-and-player-data), [TLS](#tls-between-client-and-backend), [9 Nakama keys](#9-nakama-keys-and-passwords), [Monitoring](#monitoring), [Autoscaling](#autoscaling), [NetworkPolicy](#networkpolicy-optional), [13 Backups](#13-backing-up-and-restoring-postgres), [14 Public address](#14-changing-the-public-address-public_address)

## 1. Status and logs

Goal: see how the cluster is doing and why something is not running.

```bash
# Overview
kubectl get pods,svc -n fomoxa
kubectl get fleet,gs -n fomoxa

# Logs of one service (add -f to follow)
kubectl logs deploy/backend   -n fomoxa --tail=50
kubectl logs deploy/nakama    -n fomoxa --tail=50
kubectl logs deploy/directory -n fomoxa --tail=50
kubectl logs deploy/gateway   -n fomoxa --tail=50

# Logs of EVERY game server at once (each line is prefixed with the pod name)
kubectl logs -n fomoxa -l agones.dev/role=gameserver -c fomoxa-server --prefix --tail=20

# Logs of the PREVIOUS run of a crashed pod
kubectl logs <pod-name> -n fomoxa --previous

# Why is a pod not starting? (events and detailed status)
kubectl describe pod <pod-name> -n fomoxa
kubectl get events -n fomoxa --sort-by=.lastTimestamp | tail -20

# CPU and memory use (k3s ships metrics-server)
kubectl top pods -n fomoxa
```

`deploy/<name>` tells `kubectl logs` to pick any pod of that deployment. A game server has two containers, so you must name `-c fomoxa-server` (the other one is Agones' sidecar).

Expected `kubectl top` output (sample):

```
NAME                        CPU(cores)   MEMORY(bytes)
backend-7f9d8c6b5-4hq2l     12m          95Mi
fomoxa-server-hl9fz-2xkpq   6m           48Mi
nakama-79c8d6b5f4-9rt6w     25m          180Mi
```

## 2. Reaching internal services

Many services are cluster-internal (`ClusterIP`). Use `kubectl port-forward` to open one on your machine for a while. Keep the terminal running and press Ctrl+C to close it.

```bash
# Nakama admin console -> http://127.0.0.1:7351   (user: admin)
kubectl port-forward -n fomoxa svc/nakama-console 7351:7351
kubectl get secret fomoxa-nakama -n fomoxa -o jsonpath='{.data.console-password}' | base64 -d; echo   # the password

# Postgres -> psql
kubectl exec -it -n fomoxa deploy/postgres -- psql -U postgres -d nakama -c '\dt'

# The backend's metrics directly
kubectl port-forward -n fomoxa deploy/backend 9610:9610 &
curl -s http://127.0.0.1:9610/metrics | head -20
kill %1
```

`/metrics` output (real, from the backend):

```
# HELP fomoxa_online_players Số người chơi online (báo có mặt trong 20 giây gần nhất)
# TYPE fomoxa_online_players gauge
fomoxa_online_players 2
fomoxa_logins_total{mode="login",result="ok"} 5
fomoxa_logins_total{mode="login",result="fail"} 4
fomoxa_joins_total{result="ok"} 2
fomoxa_score_reports_total 3
```

## 3. Scaling the Fleet by hand

Goal: add or remove game servers manually, while no autoscaler is running.

```bash
kubectl scale fleet fomoxa-server -n fomoxa --replicas=6
kubectl get gs -n fomoxa                    # wait for the new GameServers to become Ready
kubectl scale fleet fomoxa-server -n fomoxa --replicas=4
```

Agones creates or deletes GameServers to match `replicas`. When shrinking, it deletes `Ready` pods (no players) first and keeps `Allocated` ones. Each pod holds one room out of `arena:1..16`, so 16 pods is the most that are useful (see `FOMOXA_ROOM` in `deploy/k8s/fleet.yaml`).

If a `FleetAutoscaler` is running it overwrites the `replicas` you set. Delete it first to scale by hand: `kubectl delete -f deploy/k8s/fleet-autoscaler.yaml`.

## 4. Restarting a service

```bash
kubectl rollout restart deployment/backend   -n fomoxa
kubectl rollout restart deployment/directory -n fomoxa
kubectl rollout status  deployment/backend   -n fomoxa
```

`rollout restart` replaces pods one at a time. With one replica per service there is a short gap. When you must restart several, use this order: `postgres`, `nakama`, `directory`, `backend`, `gateway`. After the directory restarts, game servers register again by themselves.

## Updating after rebuilding an image

Goal: put new code (backend, server, directory, gateway) into a running cluster.

```bash
# 1) Rebuild the images that changed (for example the backend and the game server)
docker build -f MyGame.Backend/Dockerfile  -t fomoxa-backend:latest MyGame.Backend
docker build -f deploy/Dockerfile.server   -t fomoxa-server:latest  .

# 2) Load them into k3d
k3d image import fomoxa-backend:latest fomoxa-server:latest -c fomoxa

# 3) Backend, directory, gateway: restart so they pick up the new image
kubectl rollout restart deployment/backend -n fomoxa

# 4) Game servers (the Fleet): the tag is unchanged (:latest), so Agones sees no change. Delete the Fleet and apply it again
kubectl delete fleet fomoxa-server -n fomoxa
kubectl apply -f deploy/k8s/fleet.yaml
kubectl wait --for=jsonpath='{.status.state}'=Ready gs -l agones.dev/fleet=fomoxa-server -n fomoxa --timeout=300s
```

Step 4 differs from step 3 because a `Deployment` has `rollout restart`, while a `Fleet` replaces pods only when its spec changes. The same `:latest` tag means the spec is identical, so you delete and recreate it. Players are disconnected when the Fleet is deleted. For a gentler update, use a versioned tag (`fomoxa-server:1.1`), change `image:` and `FOMOXA_VERSION` in `fleet.yaml`, and run `kubectl apply`. Agones then replaces pods in a rolling fashion.

When the network protocol changes (a model or RPC), the client (`MyGame.exe`), the backend and the game server must be built together ([04, regenerating code](04-UNITY-CLIENT.md#regenerating-code-after-changing-a-model-or-rpc)).

## 6. Shutting down a game server safely (drain)

Goal: stop or replace a game server without cutting off players.

How it works (implemented in `MyGame/Server~/Program.cs`): when the pod gets `SIGTERM` (Kubernetes sends it when a pod is deleted), the game server calls `RequestDrain` on the directory. The directory stops assigning new players and rooms to that pod, and the pod waits until no players remain before exiting. It waits at most `drain_seconds = 300`, after which the directory forces it to stop. Kubernetes gives the pod `terminationGracePeriodSeconds: 360` before `SIGKILL`, and 360 must stay larger than 300. A second `SIGTERM` makes it exit at once.

To try it:

```bash
# Find a GameServer that has players (STATE=Allocated)
kubectl get gs -n fomoxa
GS=<gameserver-name>

# Delete it and watch the log
kubectl delete gs "$GS" -n fomoxa --wait=false
kubectl logs -n fomoxa "$GS" -c fomoxa-server -f --tail=5
```

Expected output (the line formats are real):

```
[Pod] SIGTERM: xin drain, cho het nguoi choi (gui tin hieu lan nua de thoat ngay)
...(players keep playing normally)...
[Pod] drain: het nguoi choi, thoat
```

For a pod with no players (`Ready`) the last line appears almost immediately, and the Fleet creates a replacement GameServer.

This was checked at the process level (the pod exits when players are gone). It has not been run fully against the real directory with `kubectl delete` on a cluster.

## 7. Accounts and player data

Accounts, passwords (hashed with PBKDF2-SHA256, 120,000 iterations, a separate salt each), recovery codes (only the hash is stored), profiles and friends live in Nakama storage (the collections `account`, `profile` and `social`). They are reached through two internal RPCs, `kv_get` and `kv_put`, which only the backend can call because it holds the `http_key`.

Players handle their own accounts in the game: register, change the password (Account tab), recover a forgotten password (Forgot password tab with the recovery code), add friends, and invite friends to a room.

An administrator can check whether an account exists. This returns the password hash, so treat the output as sensitive:

```bash
BASE=http://127.0.0.1:7350
HTTP_KEY=$(kubectl get secret fomoxa-nakama -n fomoxa -o jsonpath='{.data.http-key}' | base64 -d)
HTTP_CREDS=$(printf '%s:' "$HTTP_KEY" | base64)

curl -s -X POST "$BASE/v2/rpc/kv_get?unwrap" -H "Authorization: Basic $HTTP_CREDS" \
     -H 'Content-Type: application/json' -d '{"collection":"profile","key":"ana"}'; echo
```

The reply is `{"found":false}` if there is no record, or `{"found":true,"value":"{\"BestSteps\":5,...}","version":"..."}`. The `key` is the lowercase name. The collections are `account` (password hash, sensitive), `profile` (record, match count) and `social` (friends).

Brute-force protection: five wrong passwords in a row lock that name for 60 seconds. Watch the metric `fomoxa_logins_total{result="fail"}` and the `HighLoginFailures` alert.

A player who loses both the password and the recovery code cannot be reset by any tool. As a workaround, delete their `account` record in the Nakama console (the Storage page, collection `account`) so they can register the name again. The old account is lost, and the `profile` record remains unless you delete it too.

## TLS between client and backend

The backend's fRPC does not encrypt by itself, and the login password travels over it. In production, put the backend behind a TLS proxy (`tls-proxy`, nginx, port 9643). The UDP game traffic is already encrypted by Fomoxa's ticket mechanism and needs nothing more.

### 8.1 See which certificate the proxy presents

```bash
# The fingerprint of the certificate the server PRESENTS (compare with the client's pin)
openssl s_client -connect 127.0.0.1:9643 -servername fomoxa.local </dev/null 2>/dev/null \
    | openssl x509 -noout -fingerprint -sha256 -subject
```

Expected output (real, against the mock cluster's TLS endpoint):

```
sha256 Fingerprint=C5:B5:02:A6:03:68:15:A3:9E:6E:60:AC:21:39:86:51:BF:FB:9A:D4:FF:19:BD:D5:C6:39:7D:9F:F7:4C:51:67
subject=CN = fomoxa-mock
```

Remove the colons and you have the exact string for the client's `-tlspin:` (`C5B502A6...5167`).

### 8.2 Replace the certificate

```bash
./scripts/gen-tls-cert.sh -f                     # recreate tls.crt and tls.key, print the NEW pin
kubectl create secret tls fomoxa-tls -n fomoxa \
    --cert=deploy/secrets/tls/tls.crt --key=deploy/secrets/tls/tls.key \
    --dry-run=client -o yaml | kubectl apply -f -
kubectl rollout restart deployment/tls-proxy -n fomoxa
```

Then update the pin in every client (`-tlspin:` or the Tls Pin Sha256 field of `RoomJoiner`). Otherwise clients refuse the connection with a fingerprint mismatch.

### 8.3 Allow only the encrypted path

```bash
kubectl patch svc backend -n fomoxa -p '{"spec":{"type":"ClusterIP"}}'     # close 9600 to the outside
# (or on first run: TLS_ONLY=1 ./scripts/k8s-up.sh)
```

After that, clients must use `-backend:127.0.0.1:9643 -tlspin:...`. A k3d cluster still maps port 9600 to your machine, but with the service no longer a `LoadBalancer` nothing answers behind it. To undo it, run `kubectl apply -f deploy/k8s/backend.yaml`.

### 8.4 Production with a domain name

Use a certificate from a public CA (for example cert-manager with Let's Encrypt) for the `fomoxa-tls` secret. Then no pin is needed: the client checks against the Mozilla root store built into the library, and you pass only `-tls`. Point the domain at the `tls-proxy` service's IP. The certificate must list that domain in its SAN.

## 9. Nakama keys and passwords

The Kubernetes cluster no longer uses `defaultkey`, `defaulthttpkey`, `admin:admin`, or the database password `localdb`. They are in the secret `fomoxa-nakama`, which `nakama`, `backend` and `postgres` read. (`deploy/compose.yaml`, used only for local development, keeps the defaults.)

To see the current values:

```bash
for k in server-key http-key console-password; do
  printf '%-18s ' "$k"; kubectl get secret fomoxa-nakama -n fomoxa -o jsonpath="{.data.$k}" | base64 -d; echo
done
```

To rotate a key, not including the database password:

```bash
openssl rand -hex 24 | tr -d '\n' > deploy/secrets/nakama/http-key       # for example, rotate the http-key
kubectl create secret generic fomoxa-nakama -n fomoxa \
    --from-file=server-key=deploy/secrets/nakama/server-key \
    --from-file=http-key=deploy/secrets/nakama/http-key \
    --from-file=console-password=deploy/secrets/nakama/console-password \
    --from-file=session-key=deploy/secrets/nakama/session-key \
    --from-file=refresh-key=deploy/secrets/nakama/refresh-key \
    --from-file=db-password=deploy/secrets/nakama/db-password \
    --dry-run=client -o yaml | kubectl apply -f -
kubectl rollout restart deployment/nakama deployment/backend -n fomoxa
```

Things to know:

- Changing `session-key` or `refresh-key` invalidates every existing session, so players must log in again.
- Changing `db-password` stops Nakama from starting on a Postgres volume that already has data, because the password is stored inside the Postgres data. To change it, first run `ALTER USER postgres PASSWORD '...'` inside Postgres and then change the secret, or accept losing the data (`kubectl delete pvc postgres-data -n fomoxa`).
- The `test-nakama*.sh` scripts read the keys from the secret by themselves. To force the default keys when testing the `docker compose` Nakama: `NAKAMA_USE_DEFAULT_KEYS=1 ./scripts/test-nakama.sh`.

## Monitoring

Goal: metrics (Prometheus), charts (Grafana), alerts (Alertmanager) and central logs (Loki).

### 10.1 Install

```bash
kubectl apply -f deploy/monitoring/00-namespace.yaml
kubectl create secret generic grafana-admin -n monitoring --from-literal=password="$(openssl rand -hex 12)"   # first time only
kubectl apply -f deploy/monitoring/
kubectl rollout status deployment/prometheus   -n monitoring
kubectl rollout status deployment/grafana      -n monitoring
```

Or run it in one go: `INSTALL_MONITORING=1 ./scripts/k8s-up.sh` runs these commands.

This creates a separate `monitoring` namespace, a secret with a random Grafana admin password, and then Prometheus, Alertmanager, Grafana, Loki and Promtail.

Check: `kubectl get pods -n monitoring` shows prometheus, alertmanager, grafana and loki `Running`, and one `promtail` per node.

### 10.2 Open the interfaces

```bash
kubectl port-forward -n monitoring svc/grafana 3000:3000        # http://127.0.0.1:3000  (user: admin)
kubectl get secret grafana-admin -n monitoring -o jsonpath='{.data.password}' | base64 -d; echo
kubectl port-forward -n monitoring svc/prometheus 9090:9090     # http://127.0.0.1:9090/targets
kubectl port-forward -n monitoring svc/alertmanager 9093:9093   # http://127.0.0.1:9093
```

In Grafana, open the Fomoxa folder and the "Fomoxa - tổng quan" dashboard (online players, Ready GameServers, logins per second, players per pod, highest step per pod). To read logs, use Explore, pick the Loki source, and query `{namespace="fomoxa", app="backend"}` or `{pod=~"fomoxa-server-.*"}`.

In Prometheus, Status, then Targets should list these as `UP`: the backend (`:9610`), the GameServers (`:9611`), nakama (`:9100`) and the Agones controller.

### 10.3 Fomoxa's metrics

| Metric | Type | Source | Meaning |
|---|---|---|---|
| `fomoxa_online_players` | gauge | backend | players who reported "still online" in the last 20 seconds |
| `fomoxa_logins_total{mode,result}` | counter | backend | logins, registrations and recoveries, ok or fail |
| `fomoxa_joins_total{result}` | counter | backend | successful room joins |
| `fomoxa_rooms_reported`, `fomoxa_room_players` | gauge | backend | rooms and total players as reported by pods |
| `fomoxa_score_reports_total`, `fomoxa_password_changes_total`, `fomoxa_friend_requests_total`, `fomoxa_room_invites_total` | counter | backend | social and score activity |
| `fomoxa_pod_players`, `fomoxa_pod_rooms`, `fomoxa_pod_registered`, `fomoxa_pod_draining`, `fomoxa_pod_best_steps`, `fomoxa_pod_uptime_seconds` | gauge | each game server | the state of one pod |
| `agones_fleets_replicas_count{type}` | gauge | Agones | the Fleet: `total`, `allocated`, `ready`, `desired` |

### 10.4 Built-in alerts

| Alert | Condition | Level |
|---|---|---|
| `BackendDown` | the backend's metrics cannot be scraped for 1 minute | critical |
| `NakamaDown` | Nakama's metrics cannot be scraped for 1 minute | critical |
| `NoReadyGameServer` | no `Ready` GameServer for 2 minutes | critical |
| `GameServerNotRegistered` | a pod runs but has not registered with the directory for 2 minutes | warning |
| `HighLoginFailures` | more than 1 failed login per second for 5 minutes (password guessing) | warning |
| `FleetAtMaxReplicas` | the Fleet has been at the 16-pod ceiling for 10 minutes | warning |
| `PodNearlyFull` | 14 or more players on one pod for 5 minutes | info |

By default Alertmanager only groups alerts and sends them nowhere. To receive notifications, edit `receivers` in `deploy/monitoring/20-alertmanager.yaml` (Slack and webhook examples are there as comments), then:

```bash
kubectl apply -f deploy/monitoring/20-alertmanager.yaml
kubectl rollout restart deployment/alertmanager -n monitoring
```

To validate the configuration before applying it (run for real while writing; needs Docker):

```bash
# extract the config to files, then check them with the official tool
python3 - <<'PY'
import yaml
d = list(yaml.safe_load_all(open("deploy/monitoring/10-prometheus.yaml")))
pick = lambda n: [x for x in d if x.get("metadata", {}).get("name") == n][0]["data"]
import os; os.makedirs("/tmp/prom/rules", exist_ok=True)
open("/tmp/prom/prometheus.yml", "w").write(pick("prometheus-config")["prometheus.yml"].replace("/etc/prometheus/rules/*.yml", "/tmp/prom/rules/*.yml"))
open("/tmp/prom/rules/fomoxa.yml", "w").write(pick("prometheus-rules")["fomoxa.yml"])
PY
docker run --rm --entrypoint promtool -v /tmp/prom:/tmp/prom prom/prometheus:v2.55.1 check config /tmp/prom/prometheus.yml
```

Expected output (real):

```
Checking /tmp/prom/prometheus.yml
  SUCCESS: 1 rule files found
 SUCCESS: /tmp/prom/prometheus.yml is valid prometheus config file syntax
```

Prometheus, Loki and Grafana store data in `emptyDir`, so it is lost when their pods restart. That is fine for development and demos. Production needs persistent volumes.

## Autoscaling

Goal: add and remove game servers automatically based on empty rooms.

```bash
kubectl apply -f deploy/k8s/fleet-autoscaler.yaml
kubectl get fleetautoscaler -n fomoxa
```

Or run it in one go: `AUTOSCALE=1 ./scripts/k8s-up.sh`.

The `FleetAutoscaler` uses the Buffer policy. It always keeps 2 `Ready` pods (empty rooms), keeps the total between 4 and 16 pods, and checks every 15 seconds. When players turn pods `Allocated` and fewer than 2 are `Ready`, Agones adds pods. Each pod holds one of 16 rooms, so 16 is a sensible ceiling. "Auto matchmaking" only picks among rooms that have a pod, because the backend sends Nakama the list of real rooms.

Expected output (sample):

```
NAME                       STRATEGY   BUFFER   MIN   MAX   REPLICAS
fomoxa-server-autoscaler   Buffer     2        4     16    4
```

To watch it while players join: `watch -n 2 kubectl get fleet,gs -n fomoxa`. As `ALLOCATED` rises, `DESIRED` rises too, to keep 2 `Ready`.

To turn it off: `kubectl delete -f deploy/k8s/fleet-autoscaler.yaml`. The Fleet keeps its current pod count, and then you can use `kubectl scale` by hand.

Do not edit `replicas` in `fleet.yaml` and re-apply it while the autoscaler is running. The two will fight for control.

## NetworkPolicy (optional)

Goal: by default block all incoming traffic in the `fomoxa` namespace and open only the needed paths: client to gateway and backend, backend to directory and Nakama, Nakama to Postgres, pods to directory and backend, and Prometheus to the metrics ports. The Nakama console (7351) stays closed.

```bash
kubectl apply -f deploy/k8s/networkpolicy.yaml          # or: APPLY_NETPOL=1 ./scripts/k8s-up.sh
kubectl get networkpolicy -n fomoxa
```

To undo it if something stops connecting:

```bash
kubectl delete -f deploy/k8s/networkpolicy.yaml
```

This has not been tried on a real cluster. After turning it on, rerun [Steps 11 to 13 of document 03](03-DEPLOY-KUBERNETES.md#step-11-check-from-the-clients-side) to confirm no path was blocked by mistake.

## 13. Backing up and restoring Postgres

All durable data (accounts, profiles, friends, Nakama state) is in Postgres.

```bash
# Back up
mkdir -p backups
kubectl exec -n fomoxa deploy/postgres -- pg_dump -U postgres nakama > "backups/nakama-$(date +%Y%m%d-%H%M).sql"
ls -lh backups/

# Restore into an empty database (WARNING: overwrites data; stop Nakama and the backend first)
kubectl scale deployment/nakama deployment/backend -n fomoxa --replicas=0
kubectl exec -i -n fomoxa deploy/postgres -- psql -U postgres -c 'DROP DATABASE IF EXISTS nakama;' -c 'CREATE DATABASE nakama;'
kubectl exec -i -n fomoxa deploy/postgres -- psql -U postgres -d nakama < backups/nakama-XXXX.sql
kubectl scale deployment/nakama deployment/backend -n fomoxa --replicas=1
```

`pg_dump` writes the whole `nakama` database as SQL, over a local connection inside the container. Keep backup files outside the cluster, because the Postgres data lives in a k3d volume and deleting the cluster erases it. Back up `deploy/secrets/` separately too (it is not in git). Without the keys, old data cannot be decrypted or logged in to.

## 14. Changing the public address (`public_address`)

On a local setup, `public_address = "127.0.0.1:7777"` works because your machine is where k3d publishes UDP port 7777 into the gateway. When a client is on another machine (LAN or Internet), the directory must return an address that client can reach:

```bash
# 1) change public_address in the directory-config ConfigMap
kubectl edit configmap directory-config -n fomoxa      # change the line to public_address = "203.0.113.42:7777"

# (or edit deploy/k8s/directory.yaml and run: kubectl apply -f deploy/k8s/directory.yaml)

# 2) restart the directory so it reads the new setting
kubectl rollout restart deployment/directory -n fomoxa
kubectl rollout status  deployment/directory -n fomoxa
```

UDP 7777 at that address must reach the `gateway` service. Open the port in the firewall or router, or on a cloud use the Service's LoadBalancer IP. The client also needs `-backend:<ip>:9600` (or `9643` with a pin or domain name). If this is wrong, the symptom is that login and room requests succeed but the UDP connection hangs, because the client keeps trying `127.0.0.1:7777` on its own machine.

## Checklist

- [ ] You can read each service's logs and all game servers' logs at once
- [ ] You can update images: build, `k3d image import`, then `rollout restart` (backend) or delete and apply the Fleet (game servers)
- [ ] You tried a drain: `kubectl delete gs ...` on an `Allocated` pod shows `xin drain`, then `het nguoi choi, thoat`
- [ ] You decided on TLS: the certificate, the pin in the client, and whether to use `TLS_ONLY`
- [ ] `deploy/secrets/` is backed up and `pg_dump` is scheduled
- [ ] Monitoring: Prometheus Targets are all `UP`, and Alertmanager has somewhere to send notifications
- [ ] The autoscaler is on if needed, and you know the 16-pod, 16-room ceiling
- [ ] `public_address` is the address clients see the gateway at
