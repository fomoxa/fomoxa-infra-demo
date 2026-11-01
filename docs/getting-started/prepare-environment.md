# 01 Prepare the environment (Ubuntu or WSL2)

By the end of this document you will have `docker`, `kubectl`, `k3d`, `helm`, `openssl`, `git` and the .NET 8 SDK installed, and the repository cloned. After that you can run the [mock cluster](quickstart-mock-cluster.md) or the [Kubernetes deployment](../deploy/README.md).

Not verified: the install commands below are each tool's standard procedure. They were not re-run on a clean machine while writing, so the output is labeled (sample).

## Step 1. Check that the machine is big enough

Goal: make sure the machine can run k3d, Agones, Nakama and Postgres together.

| Item | Minimum | Notes |
|---|---|---|
| RAM | 8 GB (4 GB free) | the full cluster uses about 3 to 4 GB; monitoring adds about 1 GB |
| CPU | 4 cores | |
| Disk | 20 GB free | Docker images are about 3 to 4 GB; Rust builds add a few GB of cache |
| OS | Ubuntu 22.04 or newer | WSL2 or a real Linux machine |
| Network | Internet | images are pulled on the first run |

Commands:

```bash
free -h          # RAM
nproc            # CPU cores
df -h ~          # disk holding your home directory
lsb_release -d   # Ubuntu version
```

The `available` column of `free -h` is the memory you can actually use. Below about 3 GB, k3d and Agones become slow or get killed for lack of memory.

Expected output (sample):

```
               total        used        free      shared  buff/cache   available
Mem:            15Gi       2.1Gi        10Gi       0.0Ki       3.0Gi        13Gi
8
Filesystem      Size  Used Avail Use% Mounted on
/dev/sdc        251G   40G  199G  17% /
Description:    Ubuntu 22.04.4 LTS
```

On WSL2 with little RAM, create or edit `C:\Users\<your name>\.wslconfig` on Windows:

```ini
[wsl2]
memory=8GB
processors=4
```

Then run `wsl --shutdown` in PowerShell and reopen Ubuntu. WSL2 takes 50% of the machine's RAM by default.

## Step 2. Install the basic packages

Goal: have `curl`, `git`, `openssl` and certificate support.

```bash
sudo apt update
sudo apt install -y curl git openssl ca-certificates gnupg
```

`curl` downloads files, `git` clones the repository, `openssl` generates random keys and TLS certificates, `ca-certificates` makes HTTPS work, and `gnupg` lets you add Docker's apt key.

Check:

```bash
curl --version | head -1
git --version
openssl version
```

Expected output (sample):

```
curl 7.81.0 (x86_64-pc-linux-gnu) ...
git version 2.34.1
OpenSSL 3.0.2 15 Mar 2022 (Library: OpenSSL 3.0.2 15 Mar 2022)
```

## Step 3. Install Docker

WSL2 and plain Ubuntu need different setups. Do only one of them. If you use Docker Desktop, do not also install Docker Engine inside WSL2, because the two engines will compete.

### 3A. WSL2 (Ubuntu on Windows): Docker Desktop

1. Install Docker Desktop for Windows from <https://www.docker.com/products/docker-desktop/>. Restart if asked.
2. Open Docker Desktop, go to Settings, then Resources, then WSL Integration. Turn on your Ubuntu distro and choose Apply & Restart.
3. Check from the Ubuntu terminal:

```bash
docker version --format 'client={{.Client.Version}} server={{.Server.Version}}'
docker ps
```

The first command prints both versions when the Ubuntu terminal can reach Docker Desktop. `docker ps` lists running containers, and an empty list is fine.

Expected output (sample):

```
client=27.3.1 server=27.3.1
CONTAINER ID   IMAGE   COMMAND   CREATED   STATUS   PORTS   NAMES
```

If your Unity client runs on Windows, Docker Desktop is the better choice. It publishes container ports straight onto Windows `localhost`, including UDP, so the client can reach `127.0.0.1:7777` of the cluster running in WSL2.

### 3B. Plain Ubuntu (Linux machine or VM): Docker Engine

```bash
# 1) Docker's official GPG key
sudo install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg \
    | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
sudo chmod a+r /etc/apt/keyrings/docker.gpg

# 2) Docker's apt repository
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] \
https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" \
    | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null

# 3) Install Docker Engine
sudo apt update
sudo apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin

# 4) Let your user run docker without sudo, and start the service
sudo usermod -aG docker "$USER"
sudo systemctl enable --now docker
newgrp docker        # apply the group in this terminal (or log out and back in)
```

Steps 1 and 2 register the official repository so apt installs the right Docker. Step 3 installs the engine, CLI, buildx and `docker compose`. Step 4 adds you to the `docker` group (otherwise every command needs `sudo`) and starts the service at boot.

Check:

```bash
docker run --rm hello-world
```

Expected output (sample): a line that says `Hello from Docker!`.

### Common errors (Docker)

| Symptom | Cause | Fix |
|---|---|---|
| `Cannot connect to the Docker daemon at unix:///var/run/docker.sock` | WSL2: Docker Desktop is not running or WSL Integration is off. Linux: the service is stopped | WSL2: open Docker Desktop and redo 3A step 2. Linux: `sudo systemctl start docker` |
| `permission denied while trying to connect to the Docker daemon socket` | your user is not in the `docker` group | `sudo usermod -aG docker "$USER" && newgrp docker` |
| Still denied after `usermod` | the group has not been applied | log out and in again, or run `newgrp docker` |

## Step 4. Install kubectl

Goal: `kubectl`, the command-line tool for talking to Kubernetes. Every `kubectl get`, `apply` and `logs` command uses it.

```bash
KUBECTL_VERSION=$(curl -L -s https://dl.k8s.io/release/stable.txt)
echo "Installing kubectl ${KUBECTL_VERSION}"
curl -LO "https://dl.k8s.io/release/${KUBECTL_VERSION}/bin/linux/amd64/kubectl"
sudo install -o root -g root -m 0755 kubectl /usr/local/bin/kubectl
rm kubectl
kubectl version --client
```

This reads the latest stable version number, downloads the binary, installs it into `/usr/local/bin` (which is on `PATH`), deletes the download, and prints the version.

Expected output (sample):

```
Client Version: v1.31.2
Kustomize Version: v5.4.2
```

If you see `kubectl: command not found`, `/usr/local/bin` is missing from `PATH` (check `echo $PATH`) or the `install` command failed on permissions (rerun with `sudo`).

## Step 5. Install k3d

Goal: `k3d`, which creates Kubernetes (k3s) clusters inside Docker containers.

```bash
curl -s https://raw.githubusercontent.com/k3d-io/k3d/main/install.sh | TAG=v5.7.4 bash
k3d version
```

This is k3d's official install script, pinned to `v5.7.4`, the version this repository was written against. Other versions may change the `--port` syntax.

Expected output (sample):

```
k3d version v5.7.4
k3s version v1.30.4-k3s1 (default)
```

k3d 5.7.4 defaults to k3s v1.30. Agones only runs on the Kubernetes versions that release supports. If the newest Agones reports a compatibility error, pin `AGONES_VERSION` (see [03, Step 6](../deploy/03-cluster-and-agones.md#step-6-install-agones-with-helm)).

## Step 6. Install Helm

Goal: `helm`, the package manager for Kubernetes. It installs Agones with one command instead of dozens of YAML files.

```bash
curl -fsSL https://raw.githubusercontent.com/helm/helm/main/scripts/get-helm-3 | bash
helm version --short
```

Expected output (sample):

```
v3.16.2+g13654a5
```

## Step 7. Install the .NET 8 SDK

Goal: `dotnet`, needed to run the mock cluster, the tests, and the `fomoxac` code generator. You do not need it if you only build images with Docker (the Dockerfiles carry their own SDK), but you should install it.

Ubuntu 22.04 and 24.04:

```bash
sudo apt install -y dotnet-sdk-8.0
dotnet --version
```

If apt cannot find the package, use Microsoft's script:

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
bash dotnet-install.sh --channel 8.0 --install-dir "$HOME/.dotnet"
echo 'export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$PATH:$HOME/.dotnet"' >> ~/.bashrc
source ~/.bashrc
dotnet --version
```

Expected output (real, the version used while writing):

```
8.0.427
```

Any `8.0.x` works.

## Step 8. Check every tool at once

```bash
echo "docker : $(docker version --format '{{.Client.Version}}')"
echo "kubectl: $(kubectl version --client -o json | grep -m1 gitVersion | tr -d ' ,\"')"
echo "k3d    : $(k3d version | head -1)"
echo "helm   : $(helm version --short)"
echo "dotnet : $(dotnet --version)"
echo "openssl: $(openssl version | cut -d' ' -f1-2)"
echo "git    : $(git --version)"
```

Expected output (sample):

```
docker : 27.3.1
kubectl: gitVersion:v1.31.2
k3d    : k3d version v5.7.4
helm   : v3.16.2+g13654a5
dotnet : 8.0.427
openssl: OpenSSL 3.0.2
git    : git version 2.34.1
```

A line that says `command not found` means you should go back to the matching install step.

## Step 9. Clone the repository

Goal: get the source onto the machine and stand in the right directory.

On WSL2, clone into the Linux filesystem (`~`), not `/mnt/c/...`. Files under `/mnt/c` are much slower, and Docker builds and `dotnet` slow down with them.

```bash
cd ~
git clone https://github.com/fomoxa/fomoxa-infra-demo.git
cd fomoxa-infra-demo
git branch --show-current
ls
```

Expected output (sample):

```
main
MyGame  MyGame.Backend  PLAN.md  README.md  deploy  docs  scripts  server-deps  tools
```

If you are working on another branch (for example `claude/vibrant-bardeen-g301qh`), switch to it with `git checkout <branch>`.

From here on, commands run in this directory: `cd ~/fomoxa-infra-demo`.

If the clone lost the executable bit on the scripts:

```bash
chmod +x scripts/*.sh
```

## Checklist

- [ ] `free -h` shows at least 4 GB available
- [ ] `docker run --rm hello-world` prints `Hello from Docker!`
- [ ] `kubectl version --client`, `k3d version`, `helm version --short` and `dotnet --version` all print a version
- [ ] You are in `~/fomoxa-infra-demo` and `ls` shows `deploy`, `scripts` and `MyGame`

Next: [02 Quick start with the mock cluster](quickstart-mock-cluster.md) (a running game in a few minutes) or [03 Deploy to Kubernetes](../deploy/README.md).
