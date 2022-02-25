# KCert: A Simple Let's Encrypt Cert Manager for Kubernetes

KCert is a simple alternative to [cert-manager](https://github.com/jetstack/cert-manager):

- Deploys with less than 150 lines (vs. 26000 lines of yaml for cert-manager)
- Does not create or need any CRDs (Custom Resource Definitions) to operate
- Runs a single service in your cluster, isolated in its own namespace

## How it Works

- KCert runs as a single-replica deployment in your cluster
- An ingress is managed to route `.acme/challenge` requests to the service
- Service provides a web UI for basic information and configuration details
- Checks for certificates needing renewal every 6 hours
- Automatically renews certificates with less than 30 days of validity
- Watches for created and updated ingresses in the cluster
- Automatically creates certificates for ingresses with the `kcert.dev/kcert=managed` label

## Installing KCert

The following instructions assume that you will be using the included `deploy.yml` file as your template to install KCert.
If you are customizing your setup you will likely need to modify the following instructions accordingly.

Below you can find more details, but setting up KCert involves the following steps:

- Create SMTP credentials for KCert to send automatic email notifications (or skip SMTP related instructions)
- Generate an ECDSA key by running `docker run -it nabsul/kcert:v1.0.0 dotnet KCert.dll generate-key`
- Create the KCert namespace with `kubectl create namespace kcert`
- Create a secret with `kubectl -n kcert create secret generic kcert --from-literal=acme=[...] --from-literal=smtp=[...]`
- Fill in the `deploy.yml` file and run `kubectl apply -f deploy.yml`
- Start `kubectl -n kcert port-forward svc/kcert 80` and view the dashboard at `http://localhost`

The following sections describe the above in more detail.

### Create KCert secrets

It's bad practice to save secrets in yaml files, so we will be creating them separately.
KCert configuration involves two secrets:

- The ACME ECDSA key which is needed to create and renew certificates
- The optional SMTP password if you want to have email notifications

You can create the ECDSA key using KCert from the command line:

- If you have .NET Core installed you can check out this repo and run `dotnet run generate-key`
- If you have Docker (or Podman) you can run `docker run -it nabsul/kcert:v1.0.0 dotnet KCert.dll generate-key`

You can then create your Kubernetes secret with the following (replace the placeholders with your own values):

```sh
kubectl create namespace kcert
kubectl -n kcert create secret generic kcert --from-literal=acme=[YOUR ACME KEY] --from-literal=smtp=[YOUR SMTP PASSWORD]
```

### Deploy KCert

Starting with the `deploy.yml` template in this repo, find the `env:` section.
Fill in all the required values (marked with `#` comments).
If you don't want to set up email notifications you can delete all environment variables that start with `SMTP__`.

Once you've configured your settings, deploy KCert by running `kubectl apply -f ./deploy.yml`.
Congratulations, KCert should now be running!

To check that everything is running as expected:

- Run `kubectl -n kcert logs svc/kcert` and make sure there are no error messages
- Run `kubectl -n kcert port-forward svc/kcert 80` and go to `http://localhost:80` in your browser

## Creating Certificates

KCert watches for changes to ingresses in cluster and reacts to them accordingly.
KCert will ignore an ingress unless it is labelled with `kubernetes.io/ingress.class=nginx`.
For example, you could configure an ingress as follows:

```yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: test1-ingress
  labels:
    kcert.dev/ingress: "managed"
  annotations:
    kubernetes.io/ingress.class: "nginx"
spec:
  tls:
  - hosts:
    - test1.kcert.dev
    - test2.kcert.dev
    secretName: test1-tls
  rules:
  - host: test1.kcert.dev
    http:
      paths:
      - path: /
        pathType: Prefix
        backend:
          service:
            name: hello-world
            port:
              number: 80
  - host: test2.kcert.dev
    http:
      paths:
      - path: /
        pathType: Prefix
        backend:
          service:
            name: hello-world
            port:
              number: 80
```

KCert should automatically detect this new ingress and generate a TLS secret called `test1-tls`
for the two domains listed above.
You could also create one certificate per host as follows:

```yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: test1-ingress
  labels:
    kcert.dev/ingress: "managed"
  annotations:
    kubernetes.io/ingress.class: "nginx"
spec:
  tls:
  - hosts:
    - test1.kcert.dev
    secretName: test1-tls
  tls:
  - hosts:
    - test2.kcert.dev
    secretName: test2-tls
  rules:
  - host: test1.kcert.dev
    http:
      paths:
      - path: /
        pathType: Prefix
        backend:
          service:
            name: hello-world
            port:
              number: 80
  - host: test2.kcert.dev
    http:
      paths:
      - path: /
        pathType: Prefix
        backend:
          service:
            name: hello-world
            port:
              number: 80
```

## Automatic Certificate Renewal

Once every 6 hours KCert will check for certificates that are expiring in 30 days or less.
It will attempt to automatically renew those certificates.
If you have email notifications set up, you will receive a notifications of success of failure of the renewal process.

## Further Configuration Settings

KCert uses the standard .NET Core configuration library to manage its settings.
The [appsettings.json](https://github.com/nabsul/kcert/blob/main/appsettings.json)
contains the full list of settings with reasonable default values.

All settings shown in `appsettings.json` can be modified via environment variables.
For example, you can override the value of the `Acme:RenewalCheckTimeHours` setting
with a `ACME__RENEWALCHECKTIMEHOURS` environment variable.
Note that there are two underscore (`_`) characters in between the two parts of the setting name.
For more information see the [official .NET Core documentation](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-6.0).

## Building from Scratch

To build your own container image: `docker build -t [your tag] .`

## Running Locally

For local development, I recommend using `dotnet user-secrets` to configure all of KCert's required settings.
You can run KCert locally with `dotnet run`.
KCert will use your local kubectl configuration to connect to a Kubernetes cluster.
It will behave as if it is running in the cluster and you will be able to explore any settings that might be there.

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
