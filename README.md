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
helm install kcert nabsul/kcert -n kcert --debug --set acmeAcceptTerms=true,acmeEmail=[YOUR EMAIL]
```

Note: This defaults to running KCert against Let's Encrypt's staging environment.
After you've tested against staging, you can swicht to production with:

```sh
helm install kcert nabsul/kcert -n kcert --debug --set acmeAcceptTerms=true,acmeEmail=[YOUR EMAIL],acmeDirUrl=https://acme-v02.api.letsencrypt.org/directory
```

### Deploy with Plain YAML

The following instructions assume that you will be using the included `deploy.yml` file as your template to install KCert.
If you are customizing your setup you will likely need to modify the instructions accordingly.

> Note: KCert has been tested with [Ingress NGINX Controller](https://kubernetes.github.io/ingress-nginx/).
> If you'd like to use it with a different controller and have trouble, there may be some hidden settings that need to be tweaked.
> Please [create an issue](https://github.com/nabsul/kcert/issues) and I'd be happy to help.

Getting started with KCert is very straigh-forward.
Starting with the `deploy.yml` [template in this repo](https://raw.githubusercontent.com/nabsul/kcert/main/deploy.yml),
find the `env:` section.
Fill in all the required values (marked with `#` comments):

```yaml
        - name: ACME__DIRURL
          value: # https://acme-staging-v02.api.letsencrypt.org/directory or https://acme-v02.api.letsencrypt.org/directory
        - name: ACME__TERMSACCEPTED
          value: # You must set this to "true" to indicate your acceptance of Let's Encrypt's terms of service (https://letsencrypt.org/documents/LE-SA-v1.2-November-15-2017.pdf)
        - name: ACME__EMAIL
          value: # Your email address for Let's Encrypt and email notifications
```

If this is your first time using KCert you should probably start out with `https://acme-staging-v02.api.letsencrypt.org/directory`.
Experiment and make sure everything is working as expected, then switch over to `https://acme-v02.api.letsencrypt.org/directory`.
More information this topic can be found [here](https://letsencrypt.org/docs/staging-environment/).

Once you've configured your settings, deploy KCert by running `kubectl apply -f ./deploy.yml`.
Congratulations, KCert should now be running!

To check that everything is running as expected:

- Run `kubectl -n kcert logs svc/kcert` and make sure there are no error messages
- Run `kubectl -n kcert port-forward svc/kcert 8080` and go to `http://localhost:8080` in your browser

### Recommended: Email Notifications

KCert can auotmatically send you an email notification when it renews a certificate or fails to do so.
To configure email, you'll need to provide the following SMTP configuration details:

- The email address, username and password of the SMTP account
- The hostname and port of the SMTP server (SSL required)

The password should be placed in a Kubernetes secret as follows:

```sh
kubectl -n kcert create secret generic kcert-smtp --from-literal=password=[...]
```

You can then add the following to the `env:` section of your deployment:

```yaml
        - name: SMTP__EMAILFROM
          value: [...]
        - name: SMTP__HOST
          value: [...]
        - name: SMTP__PORT
          value: "[...]" # Be sure to put the port number between quotes
        - name: SMTP__USER
          value: [...]
        - name: SMTP__PASS
          valueFrom:
            secretKeyRef:
              name: kcert-smtp
              key: password
```

To test your email configuration you can connect to the KCert dasboard by running
`kubectl -n kcert port-forward svc/kcert 80` and opening `http://localhost` in your browser.
From there, navigate to the configuration section.
Check that your settings are listed there, and then click "Send Test Email" to receive a test email.

### Optional: Configure a fixed ACME Key

By default KCert will generate a random secret key at startup.
For many use cases this should be fine.
If you would like to use a fixed key, you can provide it with an environment variable.

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
Removing KCert from your cluster is as simple as executing `kubectl delete -f deploy.yml` or these three commands:

```sh
kubectl delete namespace kcert
kubectl delete clusterrolebinding kcert
kubectl delete clusterrole kcert
```

Note that certificates created by KCert in other namespaces will NOT be deleted.
You can keep those certificates or manually delete them.
