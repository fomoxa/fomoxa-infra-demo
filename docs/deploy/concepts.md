# 03.0 Kubernetes concepts used in this demo

Up to the index: [03-DEPLOY-KUBERNETES](README.md) · Next: [01-prepare-and-keys.md](01-prepare-and-keys.md)

This page explains the concepts you meet in the later steps. Each one comes with a command that shows it in your cluster once it is built (steps 9 and 10). If you already know Kubernetes, skip to [01](01-prepare-and-keys.md).

Output marked is built from the manifests, not captured from a running cluster. Real pod names carry random suffixes that will differ.

## The big picture

```
your machine (Docker)
└── k3d: Docker containers pretending to be Kubernetes nodes
    ├── k3d-fomoxa-serverlb   entry point: maps 7777/udp, 9600, 9643, 7350 from your machine into the cluster
    ├── k3d-fomoxa-server-0   node running the control plane and the pods
    └── (pods)
        ├── namespace agones-system   Agones controllers
        └── namespace fomoxa          postgres, nakama, directory, gateway, backend, tls-proxy, game servers
```

k3d is k3s (a small Kubernetes) running inside Docker. To you it behaves like a real cluster, but the "nodes" are containers, so deleting the cluster leaves your machine clean.

## Namespace

A namespace is a drawer for grouping resources. The demo uses two: `fomoxa` (yours) and `agones-system` (installed by Helm). Every command needs `-n fomoxa` to pick the drawer, or `-A` to see all of them.

```bash
kubectl get namespaces
```

Expected output:

```
NAME              STATUS   AGE
default           Active   12m
kube-system       Active   12m
agones-system     Active   9m
fomoxa            Active   6m
```

Tip: set a default namespace for the session so you can stop typing `-n fomoxa`:

```bash
kubectl config set-context --current --namespace=fomoxa
```

This edits `~/.kube/config` and only affects the current context. Undo it with `--namespace=default`.

## Pod

A pod is the smallest unit that runs: one or a few containers sharing a network and disk. A pod has its own IP inside the cluster, and that IP changes when the pod is recreated, so nobody calls a pod IP directly. They call a Service.

An Agones game server pod has two containers (`2/2`): the game container `fomoxa-server` and the sidecar `agones-gameserver-sidecar`, which handles health checks and reports state to Agones.

```bash
kubectl get pods -n fomoxa -o wide
```

`READY` of `1/1` means every container in the pod is ready. A `RESTARTS` count that keeps climbing means a container dies and restarts (read the log with `kubectl logs ... --previous`).

Common `STATUS` values:

| STATUS | Meaning | What to do |
|---|---|---|
| `Pending` | cannot be placed on a node yet (resources, volume) | `kubectl describe pod`, read Events |
| `ContainerCreating` | pulling an image or attaching a volume or secret | wait; past a few minutes, read Events |
| `Running` | running | check the READY column |
| `ImagePullBackOff` / `ErrImagePull` | image cannot be pulled | for `fomoxa-*:latest` you forgot `k3d image import` (step 7) |
| `CrashLoopBackOff` | starts, dies, repeats | `kubectl logs <pod> --previous` |
| `Completed` | ran and exited cleanly | normal for jobs, odd for services |
| `Terminating` | shutting down (may wait for `terminationGracePeriodSeconds`) | game servers wait up to 360 seconds to drain |

## Deployment and ReplicaSet

A Deployment says "I want N copies of this pod". Underneath it creates a ReplicaSet, which creates the pods and replaces any that die. When you change the image or config, the Deployment swaps pods in a rolling update: start the new pod, wait until it is ready, then stop the old one.

The demo uses Deployments for `postgres`, `nakama`, `directory`, `gateway`, `backend` and `tls-proxy`, each with `replicas: 1`.

```bash
kubectl get deployments,replicasets -n fomoxa
```

Pod names look like `<deployment>-<replicaset hash>-<pod hash>`, for example `backend-7c9d8b6f5-abcde`. The middle hash changes whenever the pod template changes. That is why `kubectl rollout restart` creates a new ReplicaSet and you see different pod names.

Why `replicas: 1` everywhere? Postgres needs a `ReadWriteOnce` disk, the directory keeps state in memory, and the backend keeps the online list in memory. Running several copies before redesigning them would give each copy a different view of the data, so the demo keeps one.

## Service

A Service is a stable address in front of a group of pods, chosen by label (`selector`). Inside the cluster the DNS name looks like:

```
<service-name>.<namespace>.svc.cluster.local
```

For example the backend reaches Nakama at `http://nakama.fomoxa.svc.cluster.local:7350`. Inside the same namespace the short form `nakama:7350` also works.

| Type | Reachable from | Used in the demo |
|---|---|---|
| `ClusterIP` (default) | inside the cluster only | `postgres`, `directory`, `backend-pods`, `nakama-console` |
| `LoadBalancer` | outside the cluster; k3d ties it to a port on your machine | `gateway` (UDP 7777), `backend` (TCP 9600, 9601), `tls-proxy` (9643), `nakama` (7350) |

Port 9602 (pods reporting to the backend) has its own `ClusterIP` Service, `backend-pods`. It must never be exposed outside, and only pods inside the cluster can call it.

```bash
kubectl get svc -n fomoxa
```

Expected output:

```
NAME             TYPE           CLUSTER-IP     EXTERNAL-IP   PORT(S)
backend          LoadBalancer   10.43.12.7     172.18.0.2    9600:30960/TCP,9601:31001/TCP
backend-pods     ClusterIP      10.43.88.20    <none>        9602/TCP
directory        ClusterIP      10.43.5.61     <none>        9500/TCP,9501/TCP
gateway          LoadBalancer   10.43.201.9    172.18.0.2    7777:30777/UDP
nakama           LoadBalancer   10.43.77.3     172.18.0.2    7350:30735/TCP
nakama-console   ClusterIP      10.43.140.2    <none>        7351/TCP
postgres         ClusterIP      10.43.31.4     <none>        5432/TCP
tls-proxy        LoadBalancer   10.43.64.18    172.18.0.2    9643:30643/TCP
```

To see which pods sit behind a Service (empty means a wrong selector or pods that are not ready):

```bash
kubectl get endpoints backend -n fomoxa
```

## ConfigMap

A ConfigMap holds non-secret configuration as text: `directory.toml`, `gateway.toml`, `nginx.conf`, `main.lua`. It is mounted into a pod as a directory of files.

The thing people forget is that editing a ConfigMap does not make running pods reread it. After an edit, restart the workload that uses it:

```bash
kubectl rollout restart deployment/directory -n fomoxa
```

See the content currently stored:

```bash
kubectl get configmap directory-config -n fomoxa -o jsonpath='{.data.directory\.toml}'
```

## Secret

A Secret is like a ConfigMap but for sensitive data: keys, passwords, certificates. Values are stored as base64, which is only a representation and gives no real protection. Anyone allowed to read the Secret can read the value.

The demo has six Secrets (created in [step 8](04-secrets.md#step-8-namespace-and-secrets)). Each service receives only the Secrets it needs, and the gateway is further limited to the `gateway` key through `items`.

```bash
kubectl get secrets -n fomoxa
kubectl get secret fomoxa-nakama -n fomoxa -o jsonpath='{.data.server-key}' | base64 -d; echo
```

The second command prints a real key. Do not paste the result into chat or commit it.

## Volume and PersistentVolumeClaim

A container loses its data when the pod is deleted. To keep data (a database), the pod mounts a PersistentVolumeClaim (PVC) that asks for a disk. k3d provisions it with the `local-path` StorageClass, which is a directory on the node.

```bash
kubectl get pvc -n fomoxa
```

Expected output:

```
NAME            STATUS   VOLUME                                     CAPACITY   ACCESS MODES   STORAGECLASS
postgres-data   Bound    pvc-0e1f...                                1Gi        RWO            local-path
```

`Bound` means the disk exists. Deleting the k3d cluster deletes the disk too. To keep the data, run `pg_dump` first ([05 part 5](../operations/05-scaling-security-backup.md)).

## Probes: readiness and liveness

- A readiness probe asks whether the pod is ready to take work. If not, the Service sends it no traffic and a rollout waits.
- A liveness probe asks whether the pod is still alive. After repeated failures the kubelet restarts the container.

The demo uses three styles: `exec` (run a command such as `pg_isready`), `tcpSocket` (the port opens), and Agones' own health check for game servers.

When a pod never becomes `Ready`, read the Events section:

```bash
kubectl describe pod -l app=backend -n fomoxa | sed -n '/Events:/,$p'
```

## Agones: GameServer, Fleet, FleetAutoscaler

Agones adds three resource types for game servers:

| Type | Role | Similar to |
|---|---|---|
| `GameServer` | one game server pod with its own lifecycle | a pod with game state |
| `Fleet` | a group of GameServers, keeping the count | a Deployment for game servers |
| `FleetAutoscaler` | changes the Fleet size automatically | a HorizontalPodAutoscaler |

A GameServer lifecycle:

```
Starting → Scheduled → RequestReady → Ready → Allocated → Shutdown
```

- `Ready`: can accept players, none yet.
- `Allocated`: has players. Agones will not shut it down when the Fleet shrinks.
- `Shutdown`: stopping. With `terminationGracePeriodSeconds: 360`, the process has six minutes to drain.

In this demo the game server process calls the Agones SDK itself: it reports `Ready` at startup and moves to `Allocated` when the first player connects (see [05 part 2](../operations/02-drain-and-accounts.md)).

```bash
kubectl get fleet,gs -n fomoxa
kubectl get crd | grep agones
```

The second command lists the custom resource types Agones registers, such as `gameservers.agones.dev` and `fleets.agones.dev`.

## The kubectl commands you will use most

| Task | Command |
|---|---|
| See status | `kubectl get pods -n fomoxa` |
| Details and errors | `kubectl describe pod <name> -n fomoxa` |
| Read logs | `kubectl logs deploy/backend -n fomoxa --tail=50` |
| Logs of the container before it died | `kubectl logs <pod> -n fomoxa --previous` |
| Follow logs live | `kubectl logs -f deploy/backend -n fomoxa` |
| Open a shell in a pod | `kubectl exec -it deploy/backend -n fomoxa -- sh` |
| Forward a port to your machine | `kubectl port-forward svc/nakama-console 7351:7351 -n fomoxa` |
| See the declared resource | `kubectl get deploy backend -n fomoxa -o yaml` |
| Recent events | `kubectl get events -n fomoxa --sort-by=.lastTimestamp` |

If you do not know what a YAML field means, ask Kubernetes itself:

```bash
kubectl explain deployment.spec.strategy
```

Next: [01 Prepare and keys](01-prepare-and-keys.md). When you reach step 9, open [09 Manifest walkthrough](manifest-walkthrough.md) to understand each YAML file.
