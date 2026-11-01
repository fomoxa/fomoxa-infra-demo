# 05.1 Status, logs, scaling, restarts and image updates

Top of this document: [05-OPERATIONS](../05-OPERATIONS.md) · Next: [02-drain-and-accounts.md](02-drain-and-accounts.md)

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

When the network protocol changes (a model or RPC), the client (`MyGame.exe`), the backend and the game server must be built together ([04, regenerating code](../04-UNITY-CLIENT.md#regenerating-code-after-changing-a-model-or-rpc)).
