# 03.4 Namespace and secrets

Top of this document: [03-DEPLOY-KUBERNETES](README.md) · Previous: [03-cluster-and-agones.md](03-cluster-and-agones.md) · Next: [05-apply-manifests.md](05-apply-manifests.md)

## Step 8. Namespace and secrets

Goal: create the `fomoxa` namespace and load the keys from Step 2 into the cluster as secrets, the objects that hold sensitive data. Pods read them as environment variables or mounted files.

```bash
# a namespace is a separate compartment for the whole application
kubectl apply -f deploy/k8s/namespace.yaml

# Directory keys: the directory reads all five; the gateway only mounts the 'gateway' key
kubectl create secret generic fomoxa-directory-keys -n fomoxa \
    --from-file=backend=deploy/secrets/directory/backend \
    --from-file=pod=deploy/secrets/directory/pod \
    --from-file=gateway=deploy/secrets/directory/gateway \
    --from-file=admin=deploy/secrets/directory/admin \
    --from-file=root=deploy/secrets/directory/root \
    --dry-run=client -o yaml | kubectl apply -f -

# Backend: the key for talking to the directory, plus the key for the pod port
kubectl create secret generic fomoxa-backend-keys -n fomoxa \
    --from-file=backend=deploy/secrets/directory/backend \
    --from-file=backend-pod=deploy/secrets/backend-pod \
    --dry-run=client -o yaml | kubectl apply -f -

# Game servers: the key for registering rooms with the directory
kubectl create secret generic fomoxa-pod-key -n fomoxa \
    --from-file=pod=deploy/secrets/directory/pod \
    --dry-run=client -o yaml | kubectl apply -f -

# Game servers: the key for reporting counts and scores to the backend (separate, without the backend-to-directory key)
kubectl create secret generic fomoxa-backend-pod-key -n fomoxa \
    --from-file=backend-pod=deploy/secrets/backend-pod \
    --dry-run=client -o yaml | kubectl apply -f -

# Nakama and Postgres
kubectl create secret generic fomoxa-nakama -n fomoxa \
    --from-file=server-key=deploy/secrets/nakama/server-key \
    --from-file=http-key=deploy/secrets/nakama/http-key \
    --from-file=console-password=deploy/secrets/nakama/console-password \
    --from-file=session-key=deploy/secrets/nakama/session-key \
    --from-file=refresh-key=deploy/secrets/nakama/refresh-key \
    --from-file=db-password=deploy/secrets/nakama/db-password \
    --dry-run=client -o yaml | kubectl apply -f -

# TLS certificate for the tls-proxy
kubectl create secret tls fomoxa-tls -n fomoxa \
    --cert=deploy/secrets/tls/tls.crt --key=deploy/secrets/tls/tls.key \
    --dry-run=client -o yaml | kubectl apply -f -
```

`--from-file=<name>=<path>` puts a file's contents into the secret under the key `<name>`, and in a pod it appears as the file `/secrets/.../<name>`. The `--dry-run=client -o yaml | kubectl apply -f -` pattern makes each command safe to repeat: instead of `create` (which fails if the secret exists) it prints the YAML and applies it, creating or updating. Each service receives only the keys it needs, so a game pod never sees the `root` or `admin` key.

Or run it in one go: `./scripts/gen-k8s-secrets.sh` generates the keys if missing and the certificate if missing, then creates the secrets.

Expected output (sample):

```
namespace/fomoxa created
secret/fomoxa-directory-keys created
secret/fomoxa-backend-keys created
secret/fomoxa-pod-key created
secret/fomoxa-backend-pod-key created
secret/fomoxa-nakama created
secret/fomoxa-tls created
```

Check:

```bash
kubectl get secrets -n fomoxa
kubectl get secret fomoxa-directory-keys -n fomoxa -o jsonpath='{.data}' | tr ',' '\n' | cut -d'"' -f2
```

You should see six secrets, and the directory secret lists five keys: `admin backend gateway pod root`.

Common errors: `no such file or directory: deploy/secrets/...` means Step 2 was skipped or you are in the wrong directory. `Error from server (NotFound): namespaces "fomoxa" not found` means the first `kubectl apply -f deploy/k8s/namespace.yaml` line did not run.

### Which secret goes into which workload

| Secret | Keys | Mounted by | Path inside the container |
|---|---|---|---|
| `fomoxa-directory-keys` | `backend` `pod` `gateway` `admin` `root` | `directory` (all five) | `/secrets/directory/*` |
| `fomoxa-directory-keys` | only `gateway` (through `items`) | `gateway` | `/secrets/directory/gateway` |
| `fomoxa-backend-keys` | `backend` `backend-pod` | `backend` | `/secrets/backend/*` |
| `fomoxa-pod-key` | `pod` | game server | `/secrets/pod/pod` |
| `fomoxa-backend-pod-key` | `backend-pod` | game server | `/secrets/backend-pod/backend-pod` |
| `fomoxa-nakama` | `server-key` `http-key` `console-password` `session-key` `refresh-key` `db-password` | `nakama`, `postgres`, `backend` (as environment variables) | environment variables, not files |
| `fomoxa-tls` | `tls.crt` `tls.key` | `tls-proxy` | `/tls/*` |

This table is why a game pod cannot see the `root` key: it only receives `fomoxa-pod-key` and `fomoxa-backend-pod-key`.

### Check the secrets are mounted correctly (after step 9)

```bash
kubectl exec deploy/gateway -n fomoxa -- ls /secrets/directory
```

Expected output (sample): only `gateway`. If you also see `root` and `admin`, the `items` block in `gateway.yaml` was lost.

```bash
kubectl exec deploy/directory -n fomoxa -- ls /secrets/directory
```

Expected output (sample): `admin backend gateway pod root`. The gateway uses a small image that may not contain `ls`. If you get `executable file not found`, that is expected. Run `kubectl describe pod -l app=gateway -n fomoxa` and read the `Mounts` section instead.

### After you change a secret

A mounted secret file is refreshed by the kubelet after a few dozen seconds, but the process does not reread it. A secret passed through environment variables (like `fomoxa-nakama`) only takes effect when the pod is recreated. Both cases need:

```bash
kubectl rollout restart deployment/backend -n fomoxa
```

Game servers belong to the Fleet. After changing a secret, delete and re-apply the Fleet (see [05 part 1](../operations/01-status-and-updates.md)).
