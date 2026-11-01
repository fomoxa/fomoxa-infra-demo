# 03 Deploy to Kubernetes (k3d + Agones), step by step

This document builds the real cluster on your machine: Postgres, Nakama, directory, gateway, backend, and the Agones Fleet of game servers, all inside k3d. You do each step by hand to see what happens. At the end there is a one-command shortcut (`./scripts/k8s-up.sh --build`) and a table of which steps that script performs.

Prerequisite: [01 Prepare the environment](../getting-started/prepare-environment.md) is done (`docker`, `kubectl`, `k3d`, `helm`, `openssl`, `git`). The first run takes 30 to 60 minutes, mostly building the Rust images and pulling images.

## Read first

- [Kubernetes concepts used here](concepts.md): pod, deployment, service, secret, configmap, Agones and so on, with commands that show each one in this cluster.
- [Reading the manifests](manifest-walkthrough.md): what each YAML file in `deploy/k8s/` contains and which fields matter.

## Map of the steps

This document is split into short files, grouped by steps. Work through them from top to bottom.

| # | Task | Result | Details |
|---|---|---|---|
| 1 | Prepare and check free ports | the machine is ready | [01-prepare-and-keys.md](01-prepare-and-keys.md#step-1-prepare-and-check-free-ports) |
| 2 | Generate keys and the certificate | `deploy/secrets/` | [01-prepare-and-keys.md](01-prepare-and-keys.md#step-2-generate-keys-and-the-certificate) |
| 3 | Get the `gateway` (Rust) source | `gateway/` directory | [02-build-images.md](02-build-images.md#step-3-get-the-gateway-rust-source) |
| 4 | Build four Docker images | `fomoxa-{server,directory,gateway,backend}:latest` | [02-build-images.md](02-build-images.md#step-4-build-four-docker-images) |
| 5 | Create the k3d cluster | cluster `fomoxa` with ports 7777, 9600, 9643, 7350 | [03-cluster-and-agones.md](03-cluster-and-agones.md#step-5-create-the-k3d-cluster) |
| 6 | Install Agones with Helm | `Fleet` and `GameServer` resources | [03-cluster-and-agones.md](03-cluster-and-agones.md#step-6-install-agones-with-helm) |
| 7 | Import images into k3d | the cluster can see the images | [03-cluster-and-agones.md](03-cluster-and-agones.md#step-7-import-the-images-into-k3d) |
| 8 | Namespace and secrets | namespace `fomoxa` plus six secrets | [04-secrets.md](04-secrets.md#step-8-namespace-and-secrets) |
| 9 | Apply manifests in order | the services run | [05-apply-manifests.md](05-apply-manifests.md#step-9-apply-the-manifests-in-order) |
| 10 | Check the whole cluster | pods `Running`, GameServers `Ready` | [06-verify-cluster.md](06-verify-cluster.md#step-10-check-the-whole-cluster) |
| 11 | Check from the client's side | ports 9600, 9643, 7777 reachable | [06-verify-cluster.md](06-verify-cluster.md#step-11-check-from-the-clients-side) |
| 12 | Test Nakama with `curl` | pairing works | [07-test-nakama.md](07-test-nakama.md#step-12-test-nakama-with-curl) |
| 13 | Test with the Unity client | GameServer becomes `Allocated` | [08-test-client-and-cleanup.md](08-test-client-and-cleanup.md#step-13-test-with-the-unity-client-real-players) |
| 14 | Clean up |  | [08-test-client-and-cleanup.md](08-test-client-and-cleanup.md#step-14-clean-up) |

## One-command shortcut

Once you understand the steps, build the whole cluster with:

```bash
./scripts/k8s-up.sh --build
```

What the script does:

| Part of `k8s-up.sh` | Matches |
|---|---|
| checks `docker info`; installs k3d and helm if missing | [01](../getting-started/prepare-environment.md) |
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
| `AUTOSCALE=1` | turns on the `FleetAutoscaler` (keeps 2 Ready pods, 4 to 16 pods) | [05](../operations/05-scaling-security-backup.md#autoscaling) |
| `INSTALL_MONITORING=1` | installs Prometheus, Alertmanager, Grafana and Loki | [05](../operations/04-monitoring.md#monitoring) |
| `APPLY_NETPOL=1` | turns on NetworkPolicy (not yet tried on a real cluster) | [05](../operations/05-scaling-security-backup.md#networkpolicy-optional) |
| `TLS_ONLY=1` | closes unencrypted port 9600, leaving only TLS on 9643 | [05](../operations/03-tls-and-keys.md#tls-between-client-and-backend) |
| `APPLY_INGRESS=1` | applies the experimental Ingress | [08](../reference.md) |

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

Next: [04 Unity client](../unity-client.md) and [05 Operations](../operations/README.md).
