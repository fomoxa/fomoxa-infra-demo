# 03.8 Test with the Unity client and clean up

Top of this document: [03-DEPLOY-KUBERNETES](README.md) · Previous: [07-test-nakama.md](07-test-nakama.md)

## Step 13. Test with the Unity client (real players)

Goal: prove the whole path: client, backend, Nakama, directory, gateway, game server, and watch the Agones state change with the players.

13.1 Open the client (built in [04](../unity-client.md)). The k3d cluster forwards 9600 and 7777 to `localhost`, which matches the client's defaults, so no flags are needed:

```powershell
.\Builds\MyGame.exe
```

In the game, register, save the recovery code, and click room `arena:1`. Nakama is on, so a specific room needs two players. Open a second client, register another name, and click the same room.

13.2 Watch the cluster side (a WSL2 terminal, while playing):

```bash
watch -n 2 kubectl get gs,fleet -n fomoxa
```

One GameServer should go from `Ready` to `Allocated`, and the Fleet's `ALLOCATED` column should rise from 0 to 1. The log of that pod:

```bash
kubectl logs -n fomoxa -l agones.dev/role=gameserver -c fomoxa-server --prefix --tail=30 | grep -E "Agones|peer"
```

```
[Pod] peer 1 la 'ana'
[Agones] first player joined -> Allocate
[Agones] player 1 connected (active: 1)
...
[Agones] room empty, shutting down in 30s unless a player returns
[Agones] Shutting down...
```

When the last player leaves and the room stays empty for 30 seconds, the pod shuts down and the Fleet creates a new `Ready` pod.

13.3 The automatic version (needs `Builds/MyGame.exe`): `./scripts/test-p5.sh`

What the script does, step by step:

1. `kubectl get gs -n fomoxa` confirms there are GameServers and no `Allocated` pods before it starts.
2. It calls the Windows PowerShell through WSL2 interop and runs `scripts/test-p5.ps1`, which starts four hidden copies of `MyGame.exe`: `-batchmode -nographics -autojoin -name:ana|bo|cai|dan -device:p5-k8s-<name> -room:arena:1|arena:1|arena:2|arena:2`. Each room needs exactly two players, so ana pairs with bo and cai with dan.
3. It waits for each client to print `mine=True` in its log (it joined a pod and owns a character).
4. It counts `Allocated` pods with `kubectl` (through `wsl.exe` when started from `test-p5.sh`). There must be exactly two, or the script fails.
5. With `TEST_SCALE=1 ./scripts/test-p5.sh` it also scales the Fleet up by one, then back, and checks the GameServer count.

Expected output (sample):

```
=== Kiem tra DoD P5: Game server chay tren K8s + Agones ===
   ana -> arena:1 ... mine=True (OK)
   bo -> arena:1 ... mine=True (OK)
   cai -> arena:2 ... mine=True (OK)
   dan -> arena:2 ... mine=True (OK)
So pod o trang thai Allocated: 2 (dung ky vong)
=== DoD P5 HOAN TAT THANH CONG! ===
```

Common errors are in [07](../troubleshooting.md): a client that cannot connect, one stuck on `waiting` (no second player), and a GameServer that never becomes `Allocated`.

## Step 14. Clean up

Pick one of three levels:

```bash
# A) Remove only the application; keep the cluster and Agones (quick to redeploy)
kubectl delete namespace fomoxa

# B) Delete the whole k3d cluster (Agones and the Postgres data go too)
k3d cluster delete fomoxa

# C) Also delete the Docker images you built
docker rmi fomoxa-server:latest fomoxa-directory:latest fomoxa-gateway:latest fomoxa-backend:latest
```

Or run it in one go: `./scripts/k8s-down.sh --keep-cluster` (level A) or `./scripts/k8s-down.sh` (level B).

The keys in `deploy/secrets/` are not removed by these commands. Delete them yourself with `rm -rf deploy/secrets` if you want.
