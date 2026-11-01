# 03.2 Get the gateway source and build the Docker images

Top of this document: [03-DEPLOY-KUBERNETES](../03-DEPLOY-KUBERNETES.md) · Previous: [01-prepare-and-keys.md](01-prepare-and-keys.md) · Next: [03-cluster-and-agones.md](03-cluster-and-agones.md)

## Step 3. Get the `gateway` (Rust) source

Goal: a `gateway/` directory with the source of `fomoxa-directory` and `fomoxa-gateway`. They are written in Rust and live outside this repository, but the image builds need them.

```bash
[ -d gateway ] || git clone --depth 1 -b 0.2.0 https://github.com/fomoxa/gateway.git gateway
ls gateway | head
```

This clones tag `0.2.0`, the version this repository uses, and `--depth 1` takes only the latest commit. The `gateway/` directory is in `.gitignore`. `[ -d gateway ] ||` skips the clone if the directory exists.

Or run it in one go: `./scripts/build-images.sh` clones it if missing, as its first action.

Expected output (sample): a `Cargo.toml` and the crate directories.

If you get `Repository not found` or a password prompt, `fomoxa/gateway` is private or your account cannot see it. Ask for access, or place the source in `gateway/` yourself.

## Step 4. Build four Docker images

Goal: package the four components as Docker images.

| Image | Dockerfile | Contents |
|---|---|---|
| `fomoxa-server` | `deploy/Dockerfile.server` | game server (.NET 8), built from `MyGame/Server~` and `server-deps` |
| `fomoxa-directory` | `deploy/Dockerfile.directory` | directory (Rust, from `gateway/`) |
| `fomoxa-gateway` | `deploy/Dockerfile.gateway` | UDP gateway (Rust, from `gateway/`) |
| `fomoxa-backend` | `MyGame.Backend/Dockerfile` | backend (.NET 8) and the health tool |

```bash
docker build -f deploy/Dockerfile.server    -t fomoxa-server:latest    .
docker build -f deploy/Dockerfile.directory -t fomoxa-directory:latest .
docker build -f deploy/Dockerfile.gateway   -t fomoxa-gateway:latest   .
docker build -f MyGame.Backend/Dockerfile   -t fomoxa-backend:latest   MyGame.Backend
```

`-f <Dockerfile>` picks the build recipe, `-t <name:tag>` names the image, and the last argument is the build context, the directory Docker sends as the root for `COPY`. The first three use the repository root (`.`). The backend has its own context, `MyGame.Backend`.

Each Dockerfile is multi-stage. The `build` stage has the heavy SDK or compiler (`dotnet/sdk`, `rust`), and the final stage copies only the result into a small runtime image. `.dockerignore` excludes `MyGame/Library`, `deploy/secrets`, `gateway/target` and similar, so builds stay fast and no key lands in an image.

Or run it in one go: `./scripts/build-images.sh` clones `gateway/` if needed and runs exactly these four commands, then lists the images.

The two Rust images take the longest, 10 to 25 minutes the first time, because Docker downloads and compiles the crates. Later builds are faster thanks to the cache.

Expected output (sample, the end of a build):

```
 => exporting to image
 => => naming to docker.io/library/fomoxa-server:latest
```

Check:

```bash
docker images | grep -E '^fomoxa-(server|directory|gateway|backend)\b'
```

You should see four lines, all tagged `latest` (sample):

```
fomoxa-backend     latest   3f1c...   2 minutes ago   260MB
fomoxa-gateway     latest   9a77...   6 minutes ago   95MB
fomoxa-directory   latest   c04b...   9 minutes ago   98MB
fomoxa-server      latest   e1d2...   12 minutes ago  235MB
```

Common errors:

| Symptom | Fix |
|---|---|
| `COPY gateway/ .: not found` | Step 3 was skipped |
| `error NU1301 ... api.nuget.org` while building a .NET image | The build container cannot reach the Internet (a corporate proxy). Configure the proxy for the Docker daemon or build on another network |
| A Rust build prints `Killed` or runs out of memory | Give Docker Desktop or WSL2 more RAM ([01, Step 1](../01-PREPARE-ENVIRONMENT.md#step-1-check-that-the-machine-is-big-enough)). `docker builder prune` frees cache |
| `no space left on device` | `docker system df`, then `docker image prune -a` |
