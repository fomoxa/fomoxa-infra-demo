# Fomoxa documentation

Vietnamese version: [../README.md](../README.md)

These documents take you from a blank machine to a running multiplayer game: Fomoxa networking, Nakama for matchmaking and accounts, and Agones game servers on Kubernetes. Every step is a shell command you can paste. You do not need to know Kubernetes beforehand.

## What the system contains

```
                         YOUR MACHINE (WSL2 or Ubuntu)
 +-----------------------------------------------------------------------------+
 |  Docker                                                                     |
 |  +- k3d = k3s (small Kubernetes) running inside Docker containers           |
 |     +- cluster "fomoxa"                                                     |
 |        +- namespace agones-system   Agones (controls game servers)          |
 |        +- namespace fomoxa                                                  |
 |           +- postgres      Nakama's database                                |
 |           +- nakama        matchmaking, plus accounts/profiles/friends      |
 |           +- directory     knows which pod holds which room, issues tickets |
 |           +- gateway       the only UDP entry point for game servers        |
 |           +- backend       login, room list, join requests                  |
 |           +- game-server   Agones Fleet: the pods that run the game         |
 +-----------------------------------------------------------------------------+
        ^  TCP 9600 (backend)                      ^  UDP 7777 (gateway)
        |  login, room list, ticket requests       |  gameplay, encrypted with the ticket
        +-------------------  UNITY CLIENT  -------+
```

A client sees exactly two ports:

| Port | Protocol | Goes to | Used for |
|---|---|---|---|
| 9600 | TCP | backend | login, room list, join requests (fRPC) |
| 7777 | UDP | gateway | gameplay; the gateway forwards to the right game server |

Everything else (postgres, nakama, directory, game servers) stays inside the cluster. Two ports are exposed on purpose:

| Port | When | Notes |
|---|---|---|
| 9643 TCP | optional | same job as 9600, but TLS-encrypted through the `tls-proxy`. Use this one outside your own network. |
| 7350 TCP | test scripts only | Nakama API. A real client never uses it. |

### One trip into a game

```
1. Client --TCP 9600--> backend      log in (name + password), get the room list
2. Client --TCP 9600--> backend      "let me into arena:1"
3.          backend --> nakama        pair two players, receive the assigned room
4.          backend --> directory     "issue a ticket for arena:1"
5.          directory returns the ticket and public_address (the GATEWAY address, not the pod's)
6. Client --UDP 7777--> gateway --> the game server that holds arena:1   (the pod checks the ticket)
```

`public_address` in `directory.toml` is the address the client sees. On a local setup it is `127.0.0.1:7777`, because k3d forwards your machine's port 7777 to the gateway. If it is wrong, the client tries an internal pod address and hangs.

### What k3d is

k3d runs k3s, a compact Kubernetes distribution, inside Docker containers. The whole cluster is a few containers on your machine, and deleting them leaves nothing behind. Ports on your machine reach the cluster through a load balancer container, configured with `--port ...@loadbalancer`.

## Which document to read

| You want to | Read |
|---|---|
| Try the Unity client on one machine, without Kubernetes | [02 Quick start with the mock cluster](02-QUICKSTART-MOCK-CLUSTER.md) |
| Build the real Kubernetes cluster (k3d + Agones) | [01](01-PREPARE-ENVIRONMENT.md), then [03 Deploy to Kubernetes](03-DEPLOY-KUBERNETES.md) |
| Open the Unity project, build the client, use command-line flags | [04 Unity client](04-UNITY-CLIENT.md) |
| Logs, scaling, updates, TLS, monitoring, autoscaling, backups | [05 Operations](05-OPERATIONS.md) |
| Run the tests and see what each one checks | [06 Testing](06-TESTING.md) |
| Fix an error | [07 Troubleshooting](07-TROUBLESHOOTING.md) |
| Look up ports, secrets, settings, or what a script runs | [08 Reference](08-REFERENCE.md) |

New readers should go 01, 02 (see the game run), 03 (the real cluster), 04, 05.

## How to read a step

Each step follows one pattern: the goal, the commands, what they do and why, the expected output, a check, and common errors.

- Commands are in `bash` blocks and run from the repository root unless noted (`cd ~/fomoxa-infra-demo`).
- Scripts are shortcuts. Every script is expanded into the manual commands beside it, so you can follow by hand to learn what happens, or run the script. Look for "Or run it in one go: `./scripts/xxx.sh`". The full mapping is in [08](08-REFERENCE.md#4-scripts-and-their-manual-equivalents).
- Expected output is labeled either (real), copied from a run made while writing these documents, or (sample), which shows the format only. Pod names, ages, hashes, and timings will differ on your machine. Log lines printed by the game programs are in Vietnamese or ASCII Vietnamese, and the documents quote them exactly.
- Kubernetes terms (pod, deployment, namespace, secret, configmap, service, context) are kept in English and explained when they first appear.

## How far to trust these documents

| Part | Run while writing? |
|---|---|
| Key and certificate generation, the mock cluster, selftest, bot test, the real backend with a real Nakama (docker compose), the Nakama test scripts, `promtool`/`amtool` | Yes. Output is labeled (real). |
| Installing Docker, kubectl, k3d and Helm; creating the k3d cluster; installing Agones; building the two Rust images (directory, gateway); applying manifests to a cluster; tls-proxy; NetworkPolicy; FleetAutoscaler; monitoring on a cluster; UDP through k3d | No. The commands come from the repository's scripts and manifests, and output is labeled (sample). |
| Unity (UI, scenes, camera, jumping, building the player) | No. The environment used to write these documents has no Unity. |

If a command in the second or third row behaves differently from what is described, check [07](07-TROUBLESHOOTING.md) and report it so the documents can be corrected.

## Repository layout

```
fomoxa-infra-demo/
+- deploy/
|  +- k8s/                 Kubernetes manifests (postgres, nakama, directory, gateway, backend, tls-proxy, fleet, ...)
|  +- monitoring/          Prometheus, Alertmanager, Grafana, Loki, Promtail
|  +- nakama/modules/      main.lua, the Nakama module for matchmaking and the key-value store
|  +- Dockerfile.{server,directory,gateway}
|  +- compose.yaml         Nakama + Postgres for local development (no Kubernetes)
|  +- secrets/             generated, never committed: keys and certificates
+- MyGame/                 Unity project (client) and Server~/ (console game server, .NET 8)
+- MyGame.Backend/         .NET backend (login, rooms, accounts, friends)
+- server-deps/            source of the Fomoxa libraries used to build the game server
+- tools/MockCluster/      mock cluster (backend + directory + real pods on one machine)
+- scripts/                helper scripts (.sh for Linux/WSL2, .ps1 for Windows)
+- gateway/                cloned on demand, never committed: Rust source of directory and gateway
+- docs/                   these documents (Vietnamese) and docs/en (English)
```
