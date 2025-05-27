# KCert: A Simple Let's Encrypt Cert Manager for Kubernetes

KCert is a simple alternative to [cert-manager](https://github.com/jetstack/cert-manager):

- Deploys with around 100 lines of yaml (vs. thousands of lines for [cert-manager](https://cert-manager.io/docs/installation/))
- Does not create or need any CRDs (Custom Resource Definitions) to operate
- Runs a single service in your cluster, isolated in its own namespace

## How it Works

- KCert runs as a single-replica deployment in your cluster
- By default, an ingress is managed to route `/.well-known/acme-challenge/` requests to the kcert service for HTTP-01 challenges.
- Alternatively, KCert now supports DNS-01 challenges with AWS Route53 and Cloudflare, which may not require managing a public-facing ingress for challenges.
- Service provides a web UI for basic information and configuration details.
- Checks for certificates needing renewal every 6 hours.
- Automatically renews certificates with less than 30 days of validity.
- Watches for created and updated ingresses in the cluster.
- Automatically creates certificates for ingresses with the `kcert.dev/ingress=managed` label.

## Installing KCert

### Deploy with Helm

First, add the Helm repo with: `helm repo add nabsul https://nabsul.github.io/helm`.

Then install with the following command (filling in your details):

```sh
kubectl create ns kcert
helm install kcert nabsul/kcert -n kcert --debug --set acmeTermsAccepted=true,acmeEmail=[YOUR EMAIL]
```

Note: This defaults to running KCert against Let's Encrypt's staging environment.
After you've tested against staging, you can switch to production with:

```sh
helm install kcert nabsul/kcert -n kcert --debug --set acmeTermsAccepted=true,acmeEmail=[YOUR EMAIL],acmeDirUrl=https://acme-v02.api.letsencrypt.org/directory
```

For setting up SMTP email notifications and other parameters, please check the `charts/kcert/values.yaml` file and set the values under `smtp` accordingly.
The SMTP password must be stored in a secret. If you stick with the defaults, you can simply create that secret with the following command:

```sh
kubectl create secret -n [YOUR NAMESPACE] generic kcert-smpt-secret --from-literal=password=[YOUR PASSWORD]
```

### Creating a Certificate via Ingress

KCert automatically looks for ingresses that reference a certicate.
If that certificate doesn't exist, it will create it (and renew it).
KCert only monitors ingresses with the `kcert.dev/ingress: "managed"` label.
You can either create your own ingress manually, or use the `kcert-ingress` chart:

```sh
helm install myingress1 nabsul/kcert-ingress -n kcert --debug --set name=[INGRESS_NAME],host=[DOMAIN],service=[SERVICE_NAME],port=[SERVICE_PORT]
```

### Creating a Certificate via ConfigMap

KCert can create TLS certificates based on definitions found in Kubernetes `ConfigMap` resources. This is useful if you need a certificate for a service that doesn't have an Ingress, or if you prefer to manage certificate definitions separately.

**Key Fields:**

-   **`data.hosts`**: This field is **required** and triggers KCert to process the ConfigMap. It should contain a comma-separated list of hostnames to be included in the certificate (e.g., `service.example.com,api.example.com`).
-   **`metadata.name`**: The name of the ConfigMap resource will be used as the name for the generated Kubernetes `Secret` containing the TLS certificate and private key.

**Discovery Process:**

-   KCert **scans all ConfigMaps** within the namespaces it is configured to monitor.
-   Currently, **no special annotations or labels (like `kcert.dev/configmap: "managed"`) are required** on the ConfigMap for KCert to consider it. The presence of the `data.hosts` key is the sole criterion.
-   If `NamespaceConstraints` are active (see "Namespace-constrained installations" section), the ConfigMap must reside in one of the specified namespaces.

**Example: ConfigMap for `service.example.com`**

```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: my-service-tls # This name will be used for the resulting TLS Secret
  namespace: my-app-namespace # Ensure this namespace is monitored by KCert
data:
  hosts: "service.example.com,service.internal.example.com"
```

KCert will detect this ConfigMap and attempt to create (or renew) a TLS Secret named `my-service-tls` in the `my-app-namespace` namespace, containing a certificate for `service.example.com` and `service.internal.example.com`.

The `kcert-configmap` Helm chart is a convenient way to create such ConfigMaps:
```sh
helm install my-cert-def nabsul/kcert-configmap -n [TARGET_NAMESPACE] --set name=my-service-tls,hosts="service.example.com,service.internal.example.com"
```
Replace `[TARGET_NAMESPACE]` with the namespace where you want the ConfigMap and the resulting Secret to reside.

**Troubleshooting:**
If KCert doesn't seem to be processing your ConfigMap:
- Verify the `data.hosts` key exists and is correctly formatted.
- Check KCert's logs. Enabling Debug logging (e.g., by setting the environment variable `Logging__LogLevel__KCert` to `Debug` for the KCert deployment) will provide more detailed information about the ConfigMap scanning process, including which ConfigMaps are found and why they might be skipped (e.g., missing `data.hosts`, null `Data` section).
- Ensure the ConfigMap is in a namespace monitored by KCert, especially if `NamespaceConstraints` are used.

### Namespace-constrained installations

If you are using Rancher clusters and are assigned a specific namespace without access to cluster-wide resources,
it is possible to instruct KCert to query only a list of namespaces.

To enable the namespace-constrained mode, set the environment variable `KCERT__NAMESPACECONSTRAINTS` to a list of namespaces, separated by ",".
Example: `KCERT__NAMESPACECONSTRAINTS=ns-1,ns-2,ns-3`.

### Helm Charts

Check resulting yaml files without deploying:

```sh
helm template kcert-test .\charts\kcert --values .\temp\kcert-values.yaml
```

## Other Advice

### Test in Staging First

If this is your first time using KCert you should probably start out with `https://acme-staging-v02.api.letsencrypt.org/directory`.
Experiment and make sure everything is working as expected, then switch over to `https://acme-v02.api.letsencrypt.org/directory`.
More information this topic can be found [here](https://letsencrypt.org/docs/staging-environment/).

### Using EAB (External Account Binding)

KCert supports the EAB authentication protocol for providers requiring it. To set it up, set the following environment variables:

```
ACME__EABKEYID: Key identifier given by your ACME provider
ACME__EABHMACKEY: HMAC key given by your ACME provider
```

### DNS Provider Configuration (for DNS-01 Challenge)

KCert now supports the DNS-01 challenge type with AWS Route53 and Cloudflare. This allows KCert to obtain certificates without requiring an externally accessible HTTP challenge endpoint, which can be beneficial in private networks or when managing wildcard certificates (though KCert does not explicitly request wildcard certificates itself yet).

All settings are configured via environment variables, following the .NET Core convention (e.g., `KCert:Namespace` becomes `KCERT__NAMESPACE`).

#### Global DNS Settings

-   **`KCert:PreferredChallengeType`** (`KCERT__PREFERREDCHallenGETYPE` env var):
    -   Specifies the preferred ACME challenge type.
    -   Possible values:
        -   `"http-01"` (Default): Uses the traditional HTTP-01 challenge, requiring the KCert service to be reachable via an Ingress for challenge validation.
        -   `"dns-01"`: Uses the DNS-01 challenge, where KCert will create temporary TXT records in your configured DNS provider.
    -   If `dns-01` is preferred and a DNS provider is enabled, KCert will attempt the DNS-01 challenge first. If it fails, or if no DNS provider is configured/enabled, or if the ACME server does not offer a DNS-01 challenge for the identifier, KCert will fall back to `http-01` if that challenge type is available from the ACME server.

#### AWS Route53 Configuration

-   **`KCert:Route53:EnableRoute53`** (`KCERT__ROUTE53__ENABLEROUTE53`): Set to `true` to enable AWS Route53 as a DNS provider.
-   **`KCert:Route53:AccessKeyId`** (`KCERT__ROUTE53__ACCESSKEYID`): Your AWS Access Key ID.
-   **`KCert:Route53:SecretAccessKey`** (`KCERT__ROUTE53__SECRETACCESSKEY`): Your AWS Secret Access Key. It's highly recommended to store this in a Kubernetes secret and mount it as an environment variable.
-   **`KCert:Route53:Region`** (`KCERT__ROUTE53__REGION`): The AWS region where your Route53 hosted zones are managed (e.g., `us-east-1`). This is required if Route53 is enabled.

**Recommended IAM Policy for AWS Route53:**

The following IAM policy grants the necessary permissions for KCert to manage TXT records for DNS-01 challenges.

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "route53:ListHostedZonesByName",
        "route53:ListHostedZones",
        "route53:ChangeResourceRecordSets"
      ],
      "Resource": "*"
    }
  ]
}
```

**Note:** For enhanced security, it is highly recommended to restrict the `Resource` to the specific ARNs of the hosted zones that KCert will manage, rather than using `*`. For example: `"Resource": "arn:aws:route53:::hostedzone/YOUR_HOSTED_ZONE_ID"`

#### Cloudflare Configuration

-   **`KCert:Cloudflare:EnableCloudflare`** (`KCERT__CLOUDFLARE__ENABLECLOUDFLARE`): Set to `true` to enable Cloudflare as a DNS provider.
-   **`KCert:Cloudflare:ApiToken`** (`KCERT__CLOUDFLARE__APITOKEN`): Your Cloudflare API Token. Store this securely, e.g., in a Kubernetes secret.
-   **`KCert:Cloudflare:AccountId`** (`KCERT__CLOUDFLARE__ACCOUNTID`): Your Cloudflare Account ID. This is required if Cloudflare is enabled.

**Required Cloudflare API Token Permissions:**

The API Token needs the following permissions for the zones KCert will manage:
-   **Zone**: `Read` (e.g., `Zone Resources:Read`)
-   **DNS**: `Edit` (e.g., `DNS:Edit`)

You can create a custom token with these specific permissions in your Cloudflare dashboard.

### Challenge Mechanisms

KCert supports two types of ACME challenges to verify domain ownership: HTTP-01 and DNS-01.

#### HTTP-01 Challenge
This is the default method. KCert configures an Ingress resource pointing to itself. The Let's Encrypt server (or other ACME provider) makes an HTTP request to a specific URL under your domain. If KCert successfully serves the expected challenge response, the domain is validated. This requires KCert to be accessible from the internet.

#### DNS-01 Challenge
When `KCert:PreferredChallengeType` is set to `"dns-01"` and a DNS provider (AWS Route53 or Cloudflare) is enabled and configured:
1.  KCert requests a new certificate order from the ACME server.
2.  For each domain in the certificate, the ACME server provides a unique token for DNS-01 challenge.
3.  KCert creates a temporary TXT DNS record (`_acme-challenge.<yourdomain>`) with a value derived from this token. This is done using the configured DNS provider's API (AWS Route53 or Cloudflare).
4.  After attempting to create the TXT record, KCert waits for a short period to allow for DNS propagation.
5.  KCert then asks the ACME server to validate the challenge. The ACME server performs a DNS lookup for the TXT record.
6.  Once the challenge is validated (or fails), KCert removes the temporary TXT record.

**Behavior with `PreferredChallengeType`:**
-   If `dns-01` is preferred and a DNS provider is configured: KCert attempts DNS-01 first. If this process fails (e.g., API error, validation timeout), or if the ACME server doesn't offer DNS-01 for a given identifier, KCert will automatically fall back to the HTTP-01 challenge if available.
-   If `http-01` is preferred (or is the default), KCert uses the HTTP-01 challenge.

**Skipping HTTP Challenge Ingress:**
If `dns-01` is the preferred challenge type and a DNS provider is successfully configured and used, KCert will **not** create or manage the temporary HTTP challenge Ingress (`kcert-ingress` by default) that is typically used for HTTP-01 challenges. This can simplify deployments where exposing KCert directly via an Ingress is undesirable or complex. If DNS-01 fails and KCert falls back to HTTP-01, it will then manage the HTTP challenge Ingress as needed.

**DNS Provider Selection:**
-   If both AWS Route53 and Cloudflare are enabled, **AWS Route53 will be used by default.**
-   It is generally recommended to enable and configure only one DNS provider to avoid ambiguity.

**Auto-Renewal:**
The DNS-01 challenge mechanism is fully compatible with KCert's automatic certificate renewal process.

### Wildcard Certificates

KCert supports issuing certificates for wildcard domains (e.g., `*.example.com`). This allows a single certificate to cover multiple subdomains under a specific domain.

**Key Requirements for Wildcard Certificates:**

1.  **DNS-01 Challenge is Mandatory:** Wildcard domain validation can **only** be performed using the DNS-01 challenge type. You must configure KCert to use DNS-01 by:
    *   Setting `KCert:PreferredChallengeType` (`KCERT__PREFERREDCHALLENGETYPE` env var) to `"dns-01"`.
    *   Properly configuring a DNS provider (AWS Route53 or Cloudflare) as detailed in the "DNS Provider Configuration" section.
    *   If DNS-01 is not configured or fails for a wildcard domain, KCert will **not** fall back to HTTP-01 for that domain, and certificate issuance will fail.

2.  **Explicitly List Both Wildcard and Apex/Base Domain:** If you want a certificate that covers both the wildcard domain (e.g., `*.example.com`) AND the apex/base domain (e.g., `example.com`), you **must explicitly list both hostnames** in your Ingress `spec.tls[].hosts` array or ConfigMap `data.hosts` field. KCert requests certificates for exactly the hostnames provided.

**Example: Ingress for Wildcard Certificate (`*.example.com` and `example.com`)**

```yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: my-wildcard-app
  # namespace: my-namespace # Optional: specify if not default
  labels:
    kcert.dev/ingress: "managed" # Required for KCert to manage this Ingress
  # annotations:
  #   kubernetes.io/ingress.class: "nginx" # Example Ingress controller
spec:
  tls:
  - hosts:
    - "*.example.com"
    - "example.com" # Important: Include the base domain for SAN coverage
    secretName: my-example-wildcard-tls # Name of the secret to store the certificate
  rules:
  - host: "www.example.com" # Example specific subdomain covered by the wildcard
    http:
      paths:
      - path: /
        pathType: Prefix
        backend:
          service:
            name: my-app-service
            port:
              number: 80
  # Optional: Rule for the apex domain if it also serves traffic directly
  - host: "example.com"
    http:
      paths:
      - path: /
        pathType: Prefix
        backend:
          service:
            name: my-app-service 
            port:
              number: 80
```
*Note: Ensure `kcert.dev/ingress: "managed"` label is present. To cover both `*.example.com` and `example.com`, list both in `spec.tls[].hosts`.*

**Example: ConfigMap for Wildcard Certificate (`*.example.com` and `example.com`)**

```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: my-example-wildcard-tls # This ConfigMap name will be the Kubernetes Secret name
  # namespace: my-namespace # Optional: specify if not default
  # No specific annotations or labels are currently required by KCert for ConfigMap discovery.
  # The presence of 'data.hosts' is the trigger.
data:
  hosts: "*.example.com,example.com" # Comma-separated list
```
*Note: The ConfigMap's `metadata.name` is used as the Kubernetes Secret name for the certificate. Ensure `data.hosts` is present.*

**DNS Provider Setup:**
Remember to refer to the "DNS Provider Configuration" section to correctly set up AWS Route53 or Cloudflare for DNS-01 challenges. Without a functional DNS provider configuration, wildcard certificate issuance will fail.

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
contains the full list of settings with reasonable default values. Configuration for DNS providers (Route53, Cloudflare) and preferred challenge type are typically set via environment variables in your Kubernetes deployment.

All settings shown in `appsettings.json` can be modified via environment variables.
For example, you can override the value of the `Acme:RenewalCheckTimeHours` setting
with a `ACME__RENEWALCHECKTIMEHOURS` environment variable.
Settings with colons in their names, like `KCert:Route53:EnableRoute53`, are transformed by replacing `:` with `__` (double underscore), e.g., `KCERT__ROUTE53__ENABLEROUTE53`.
For more information see the [official .NET Core documentation](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-6.0).

### Deployment and Setup Notes for DNS-01

- **Update Configuration:** When deploying KCert, ensure you set the necessary environment variables for your chosen DNS provider (AWS Route53 or Cloudflare) and the `KCERT__PREFERREDCHallenGETYPE` if you wish to use DNS-01. Remember to store sensitive information like API keys and tokens securely, preferably using Kubernetes secrets.
- **Kubernetes RBAC:** The existing Kubernetes Role-Based Access Control (RBAC) permissions for KCert (ClusterRoles for watching Ingresses and Secrets, and a Role for managing its own challenge Ingress if HTTP-01 is used) generally do **not** need to be changed for DNS-01 support. DNS provider interactions happen directly with the provider's API, not through Kubernetes resources for the challenge itself.

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
