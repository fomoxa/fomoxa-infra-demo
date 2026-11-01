# 03.3 Create the k3d cluster, install Agones, import images

Top of this document: [03-DEPLOY-KUBERNETES](../03-DEPLOY-KUBERNETES.md) · Previous: [02-build-images.md](02-build-images.md) · Next: [04-secrets.md](04-secrets.md)

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

The import is slow for images of a few hundred MB, so do not interrupt it. After every rebuild of an image you must import again and restart the workload ([05, updating images](../ops/01-status-and-updates.md#updating-after-rebuilding-an-image)).
