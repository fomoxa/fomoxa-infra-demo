# 05.5 Autoscaling, NetworkPolicy, backups and the public address

Top of this document: [05-OPERATIONS](README.md) · Previous: [04-monitoring.md](04-monitoring.md)

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

This has not been tried on a real cluster. After turning it on, rerun [Steps 11 to 13 of document 03](../deploy/06-verify-cluster.md#step-11-check-from-the-clients-side) to confirm no path was blocked by mistake.

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
