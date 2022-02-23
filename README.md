# KCert: A Simple Let's Encrypt Cert Manager for Kubernetes

KCert is a simple, easy to run and understand alternative to [cert-manager](https://github.com/jetstack/cert-manager):

- Instead of 26000 lines of yaml, `KCert` deploys with less than 150 lines
- Instead of custom resources, `KCert` uses the existing standard Kubernetes objects
- The codebase is small and easy to understand

## Installing KCert

The following instructions assume that you will be using the included `deploy.yml` file as your template for install KCert.
If you are customizing your setup you will likely need to modify the following instructions accordingly.

### Create KCert secrets

Since it's not good practice to save secrets in yaml files or plain environment variables,
we will be creating them separately. KCert configuration involves two secrets:

- The ACME ECDSA key which is needed to create and renew certificates
- The optional SMTP password if you want to have email notifications

You can create the ECDSA key using KCert from the command line:

- If you have .NET Core installed you can check out this repo and run `dotnet run generate-key`
- If you have Docker (or Podman) you can run `docker run -it nabsul/kcert:1.0.0 dotnet KCert.dll generate-key`

You can then create your Kubernetes secret with the following (replace the placeholders with your own values):

```sh
kubectl create namespace kcert
kubectl -n kcert create secret generic kcert --from-literal=acme=[YOUR ACME KEY] --from-literal=smtp=[YOUR SMTP PASSWORD]
```

### Deploy KCert

Starting with the `deploy.yml` template in this repo, find the `env:` section.
Fill in all the required values (marked with double underscores).
If you don't want to set up email notifications you can delete the `smtp` configuration block.

Once this you've configured your settings, deploy KCert by running `kubectl apply -f ./deploy.yml`.
Congratulations, KCert should now be running!

To check that everything is running as expected:

- Run `kubectl -n kcert logs svc/kcert` and make sure there are no error messages
- Run `kubectl -n kcert port-forward svc/kcert 80` and go to `http://localhost:80` in your browser

## Uninstalling KCert

KCert does not create many resources,
and most of them are restricted to the kcert namespace.
Removing KCert from your cluster is as simple as executing these three commands:

```sh
kubectl delete namespace kcert
kubectl delete clusterrolebinding kcert
kubectl delete clusterrole kcert
```

Note that certificates created by KCert in other namespaces will NOT be deleted.
You can keep those certificates or manually delete them.

## How it Works

- An ingress definition routes `.acme/challenge` requests to KCert for HTTP challenge requests
- Service provides a web UI and to manually manage and certs
- KCert will automatically check for certificates needing renewal every 6 hours
- KCert will renew a certificate if it expires in less than 30 days
- KCert watches for created and updated ingresses in the cluster
- KCert will automatically create and manage certificates for ingresses with the `kcert.dev/kcert=managed` label

## Building from Scratch

To build your own container image: `docker build -t [your tag] .`

## Running Locally

You can run KCert locally with `dotnet run`.
If you have Kubectl configured to connect to your cluster,
KCert will use those settings to do the same.
It will behave as if it is running in the cluster and you will be able to explore any settings that might be there.
