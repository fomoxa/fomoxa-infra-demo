# Development guide

Top of this document: [documentation home](../README.md) · Previous: [frpc-add-a-method.md](frpc-add-a-method.md)

Recipes for the changes people make most often. Each one lists the files to touch and the checks to run. The checks are described in [testing](../testing.md).

## Set up

```bash
git clone https://github.com/fomoxa/fomoxa-infra-demo.git
cd fomoxa-infra-demo
./scripts/run-mock-cluster.sh --selftest
```

You need the .NET 8 SDK ([prepare the environment](../getting-started/prepare-environment.md#step-7-install-the-net-8-sdk)). The command builds the game server and the mock cluster and runs the selftest. If it ends with `TAT CA DAT`, the toolchain works.

## Where the code lives

| Area | Path |
|---|---|
| Backend | `MyGame.Backend/MyGame.Backend/` ([backend](backend.md)) |
| Game server | `MyGame/Server~/` ([game server](game-server.md)) |
| Unity client and shared gameplay | `MyGame/Assets/Network/` ([client code](client-code.md)) |
| Mock cluster | `tools/MockCluster/` |
| Nakama module | `deploy/nakama/modules/main.lua` ([Nakama module](nakama-module.md)) |
| Kubernetes manifests | `deploy/k8s/` ([walkthrough](../deploy/manifest-walkthrough.md)) |
| Monitoring | `deploy/monitoring/` |
| Scripts | `scripts/` ([list](../reference.md#4-scripts-and-their-manual-equivalents)) |
| CI | `.github/workflows/ci.yml` |

## Add or change an RPC

[Add an fRPC method](frpc-add-a-method.md) walks through these steps with a working example.

1. Add or edit the message in `MyGame.Backend/MyGame.Backend/Models/*.cs`. Mark the class `[Network]` and `[Codec("rpc")]`, and each property with `[Network("<type>")]` and `[Codec("rpc")]`.
2. Declare the method in `Services.cs` on the right service. Use `Requires = Roles.Player` for lobby calls.
3. Implement it in `Players.cs` (for `Lobby`) or `Pods.cs` (for `PodGate`). Put logic shared with the mock cluster in `Accounts/LobbyCore.cs`.
4. Mirror the same model and service in the Unity tree: `MyGame/Assets/Network/Models/Backend/*.cs` and `MyGame/Assets/Network/Rpc/Services.cs`.
5. Regenerate both trees ([next section](#regenerate-the-code)).
6. If the mock cluster should support it, implement it in `tools/MockCluster/MockBackend.cs`.
7. Add a step to `--selftest` in `tools/MockCluster/Program.cs`.
8. Update the [RPC interface](rpc-api.md) page in both languages.

## Regenerate the code

The backend and Unity each have their own generated tree, and they must match. `fomoxac` reads the models and writes the codecs.

```bash
FOMOXAC=$PWD/MyGame.Backend/tools/fomoxac/linux-x64/fomoxac
chmod +x "$FOMOXAC"
( cd MyGame.Backend/MyGame.Backend && "$FOMOXAC" generate )
( cd MyGame && "$FOMOXAC" generate )
```

CI runs `generate --check` on both and fails when a committed file is out of date. Commit the generated files together with the change.

## Change the gameplay

The rules live in `MyGame/Assets/Network/Models/Course.cs` and are shared by the client and the server. After changing a constant:

1. Rebuild the scene in Unity: **Tools > Demo > Rebuild Scene** (it redraws the platforms).
2. Rebuild the game server image: `./scripts/build-images.sh`, then `k3d image import` and recreate the Fleet ([updates](../operations/01-status-and-updates.md)).
3. Check with `./scripts/run-mock-cluster.sh --bot-test`. The bot should still climb the platforms. If you made the jump harder, expect fewer steps.

## Add a room

Rooms are listed in `FOMOXA_ROOM` of `deploy/k8s/fleet.yaml` (`arena:1` to `arena:16`). To add more, extend the list and raise `maxReplicas` in `fleet-autoscaler.yaml` so the pod count and the room count stay equal. A pod without a free room waits and retries every 2 seconds.

The `auto` pool in the Lua module falls back to `arena:1` to `arena:4`. The backend normally overrides it with the rooms that pods report, so you do not edit the Lua for this.

## Change the Nakama module

See [changing the module](nakama-module.md#changing-the-module). The ConfigMap copy and the file must match, or CI fails.

## Add a metric

In the backend, call `Metrics?.Count("name")` for a counter or `metrics.Gauge("name", "help", () => value)` for a gauge. In the game server, use the `Metrics("fomoxa_pod")` instance in `Program.cs`. Add a panel to the dashboard in `deploy/monitoring/30-grafana.yaml` and, if needed, an alert in `10-prometheus.yaml`. Then validate with `promtool` ([monitoring](../operations/04-monitoring.md)).

## Before you push

```bash
python3 docs/tools/check_docs.py
./scripts/run-mock-cluster.sh --selftest
./scripts/run-mock-cluster.sh --bot-test
```

Add `./scripts/test-real-backend.sh` when you changed accounts, matchmaking or the Lua module (it needs Docker). The CI jobs are listed at the end of [testing](../testing.md#how-this-maps-to-ci-github-actions).

When you change a document, change the Vietnamese page at the same path under `docs/vi/`. The rules are in [documentation](../contributing/documentation.md).
