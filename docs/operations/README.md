# 05 Operations

Day-to-day tasks and the operational features: logs, scaling, updates, graceful shutdown (drain), accounts, TLS, Nakama keys, monitoring, autoscaling, NetworkPolicy and backups. This document assumes a running cluster from [03](../deploy/README.md), your shell in `~/fomoxa-infra-demo`, and `kubectl config use-context k3d-fomoxa`.

Run while writing: the Nakama and RPC commands (`kv_get`), `openssl s_client` against a certificate, `/metrics`, and the `promtool`, `amtool`, `loki` and `promtail` config checks. Not run on a real Kubernetes cluster: the `kubectl` commands below (output labeled sample), and the manifests for `tls-proxy`, `networkpolicy`, `fleet-autoscaler` and `monitoring/`.

## Parts

- [01-status-and-updates.md](01-status-and-updates.md): Status and logs, internal services, manual scaling, restarts, image updates
- [02-drain-and-accounts.md](02-drain-and-accounts.md): Safe shutdown (drain), accounts and player data
- [03-tls-and-keys.md](03-tls-and-keys.md): TLS between client and backend, Nakama keys and passwords
- [04-monitoring.md](04-monitoring.md): Prometheus, Grafana, Alertmanager, Loki: install, metrics, alerts
- [05-scaling-security-backup.md](05-scaling-security-backup.md): Autoscaling, NetworkPolicy, Postgres backups, changing `public_address`

## Who should read what

- Whoever watches the cluster day to day: part 1 (status, logs, updates) and part 4 (monitoring).
- Whoever owns security: part 3 (TLS, Nakama keys) and part 2 (player accounts).
- Whoever cares about performance and cost: part 4 (monitoring) and part 5 (autoscaling).
- Before exposing the cluster beyond your own network: parts 3 and 5 (NetworkPolicy, `public_address`).

## Checklist

- [ ] You can read each service's logs and all game servers' logs at once
- [ ] You can update images: build, `k3d image import`, then `rollout restart` (backend) or delete and apply the Fleet (game servers)
- [ ] You tried a drain: `kubectl delete gs ...` on an `Allocated` pod shows `xin drain`, then `het nguoi choi, thoat`
- [ ] You decided on TLS: the certificate, the pin in the client, and whether to use `TLS_ONLY`
- [ ] `deploy/secrets/` is backed up and `pg_dump` is scheduled
- [ ] Monitoring: Prometheus Targets are all `UP`, and Alertmanager has somewhere to send notifications
- [ ] The autoscaler is on if needed, and you know the 16-pod, 16-room ceiling
- [ ] `public_address` is the address clients see the gateway at
