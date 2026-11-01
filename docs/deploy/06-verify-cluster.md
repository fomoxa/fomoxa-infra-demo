# 03.6 Check the whole cluster and check from the client side

Top of this document: [03-DEPLOY-KUBERNETES](README.md) · Previous: [05-apply-manifests.md](05-apply-manifests.md) · Next: [07-test-nakama.md](07-test-nakama.md)

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

If `TCP 9600` cannot connect, the cluster was created without the `--port` flag (Step 5) or the backend service has no `EXTERNAL-IP` yet (`kubectl get svc backend -n fomoxa`). If the Unity client is on Windows and the cluster is in WSL2, you need Docker Desktop so the ports reach Windows `localhost`. See [07](../troubleshooting.md#a-windows-client-cannot-reach-a-cluster-in-wsl2).
