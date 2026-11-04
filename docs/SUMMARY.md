# Table of contents

* [Fomoxa documentation](README.md)

## Get started

* [Prepare the environment (Ubuntu or WSL2)](getting-started/prepare-environment.md)
* [Quick start with the mock cluster (no Kubernetes)](getting-started/quickstart-mock-cluster.md)

## Deploy

* [Deploy to Kubernetes (k3d + Agones), step by step](deploy/README.md)
  * [Kubernetes concepts used in this demo](deploy/concepts.md)
  * [Prepare, free ports, keys and certificate](deploy/01-prepare-and-keys.md)
  * [Get the gateway source and build the Docker images](deploy/02-build-images.md)
  * [Create the k3d cluster, install Agones, import images](deploy/03-cluster-and-agones.md)
  * [Namespace and secrets](deploy/04-secrets.md)
  * [Apply the manifests in order](deploy/05-apply-manifests.md)
  * [Check the whole cluster and check from the client side](deploy/06-verify-cluster.md)
  * [Test Nakama with curl](deploy/07-test-nakama.md)
  * [Test with the Unity client and clean up](deploy/08-test-client-and-cleanup.md)
  * [Reading the manifests in `deploy/k8s/`](deploy/manifest-walkthrough.md)
  * [Traffic path and where TLS is configured](deploy/traffic-and-tls.md)

## Unity client

* [Unity client: open the project, build the scenes, build the player, run it](unity-client.md)

## Operations

* [Operations](operations/README.md)
  * [Status, logs, scaling, restarts and image updates](operations/01-status-and-updates.md)
  * [Safe shutdown (drain) and player accounts](operations/02-drain-and-accounts.md)
  * [TLS and Nakama keys](operations/03-tls-and-keys.md)
  * [Monitoring](operations/04-monitoring.md)
  * [Autoscaling, NetworkPolicy, backups and the public address](operations/05-scaling-security-backup.md)

## Architecture

* [Overview](architecture/overview.md)
* [Backend](architecture/backend.md)
* [Nakama module](architecture/nakama-module.md)
* [Game server (pod)](architecture/game-server.md)
* [Unity client code](architecture/client-code.md)
* [RPC interface](architecture/rpc-api.md)
* [Using fRPC](architecture/using-frpc.md)
* [Add an fRPC method](architecture/frpc-add-a-method.md)
* [Development guide](architecture/development.md)

## Testing and problems

* [Testing](testing.md)
* [Troubleshooting](troubleshooting.md)

## Reference

* [Reference](reference.md)

## Contributing

* [Documentation](contributing/documentation.md)
