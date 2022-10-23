# KCert: A Simple Let's Encrypt Cert Manager for Kubernetes

KCert is a simple alternative to [cert-manager](https://github.com/jetstack/cert-manager):

- Deploys with around 100 lines of yaml (vs. thousands of lines for [cert-manager](https://cert-manager.io/docs/installation/))
- Does not create or need any CRDs (Custom Resource Definitions) to operate
- Runs a single service in your cluster, isolated in its own namespace

## How it Works

- KCert runs as a single-replica deployment in your cluster
- An ingress is managed to route `.acme/challenge` requests to the service
- Service provides a web UI for basic information and configuration details
- Checks for certificates needing renewal every 6 hours
- Automatically renews certificates with less than 30 days of validity
- Watches for created and updated ingresses in the cluster
- Automatically creates certificates for ingresses with the `kcert.dev/ingress=managed` label

## Installing KCert

### Deploy with Helm

First, add the Helm repo with: `helm repo add nabsul https://nabsul.github.io/helm`.

Then install with the following command (filling in your details):

```sh
kubectl create ns kcert
helm install kcert nabsul/kcert -n kcert --debug --set acmeTermsAccepted=true,acmeEmail=[YOUR EMAIL],kcertImage=nabsul/kcert:v1.1.0
```

Note: This defaults to running KCert against Let's Encrypt's staging environment.
After you've tested against staging, you can swicht to production with:

```sh
helm install kcert nabsul/kcert -n kcert --debug --set acmeTermsAccepted=true,acmeEmail=[YOUR EMAIL],acmeDirUrl=https://acme-v02.api.letsencrypt.org/directory,kcertImage=nabsul/kcert:v1.1.0
```

For setting up SMTP email notifications and other parameters, please check the `charts/kcert/values.yaml` file.

### Creating a Certificate via Ingress

KCert automatically looks for ingresses that reference a certicate.
If that certificate doesn't exist, it will create it (and renew it).
KCert only monitors ingresses with the `kcert.dev/ingress: "managed"` label.
You can either create your own ingress manually, or use the `kcert-ingress` chart:

```sh
helm install myingress1 nabsul/kcert-ingress -n kcert --debug --set name=[INGRESS_NAME],host=[DOMAIN],service=[SERVICE_NAME],port=[SERVICE_PORT]
```

### Creating a Certificate via ConfigMap

If you want to create a certificate without creating an ingress, you can do so via a ConfigMap.
You can create one using the  `kcert-configmap` chart as follows:

```sh
helm install [VERSION] nabsul/kcert-configmap -n kcert --debug --set name=kcert,hosts=[HOSTS]
```

An example would be *helm install 1.1.0 nabsul/kcert-configmap -n kcert --debug --set name=kcert,hosts="www.yourdomain.duckdns.org"*

## Other Advice

### Test in Staging First

If this is your first time using KCert you should probably start out with `https://acme-staging-v02.api.letsencrypt.org/directory`.
Experiment and make sure everything is working as expected, then switch over to `https://acme-v02.api.letsencrypt.org/directory`.
More information this topic can be found [here](https://letsencrypt.org/docs/staging-environment/).

### Diagnostics

To check that everything is running as expected:

- Run `kubectl -n kcert logs svc/kcert` and make sure there are no error messages
- Run `kubectl -n kcert port-forward svc/kcert 8080` and go to `http://localhost:8080` in your browser

### Testing SMTP Configuration

To test your email configuration you can connect to the KCert dasboard by running
`kubectl -n kcert port-forward svc/kcert 8080` and opening `http://localhost:8080` in your browser.
From there, navigate to the configuration section.
Check that your settings are listed there, and then click "Send Test Email" to receive a test email.

### Optional: Configure a fixed ACME Key

By default KCert will generate a random secret key at startup.
For many use cases this will be fine.
If you would like to use a fixed key, you can provide it as an environment variable.

You can generate your own random key with the following:

```sh
docker run -it nabsul/kcert:v1.0.1 dotnet KCert.dll generate-key
```

Next you would need to put that generated key into a Kubernetes secret:

```sh
kubectl -n kcert create secret generic kcert-key --from-literal=key=[...]
```

Finally, add this to your deployment's environment variables:

```yaml
        - name: ACME__KEY
          valueFrom:
            secretKeyRef:
              name: kcert-key
              key: key
```

## Creating Certificates

KCert watches for changes to ingresses in cluster and reacts to them accordingly.
KCert will ignore an ingress unless it is labelled with `kcert.dev/ingress=managed`.
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
