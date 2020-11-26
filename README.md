# KCert: A Basic Let's Encrypt Cert Manager for Kubernetes

Note: This is very experimental. Testers, feedback and feature requests are welcome, but please do not use in production environments.

The goal of this project is to provide a simple, easy to run, easy to understand alternative to `cert-manager`:

- Instead of a Helm chart or 26000 lines of yaml, `KCert` deploys with less than 100 lines of yaml
- Instead of custom resources, `KCert` uses the existing standard objects
- The codebase is small and easy to understand

## Caveats

- Multiple namespaces are not supported at the moment
- Certs are created manually not automatically renewed

## How it Works

- A service account is created to give `KCert` permission to modify ingresses and secrets
- A deployment runs a single KCert instance which serves the web UI and `.well-know/acme-challenge` HTTP Challenges
- Your encryption key and other settings are stored as a standard Kubernetes secret
- The KCert web server shows you a list of ingresses in the namespace
- You select an ingress and manually trigger fetching/renewing the cert
- The renewal runs "live" with success/failure logs displayed at the end

## How to Use

To run `KCert` in your `default` namespace:

- Deploy to your cluster using: `kubectl apply -f deploy.yml`
- Forward the web UI to your local machine: `kubectl port-forward service/kcert 8080:80`
- Open the web UI in your browser: `https://localhost:8080`
- Go to the configuration tab and configure your settings
- Go back to the main page and select an ingress
- Click the "Get Cert" button and wait for the result (be sure to only click once)

## Customizing

If you need to run this in a different namespace, you'll need to customize some of the runtime parameters.
The configuration options can be found in `appsettings.json` and can be modified using environment variables.
For example: To run in a namespace called `myns1`:

- Change the deployment yaml file so that everything is deployed to that namespace
- Add an environment variable in the deployment with name `KCERT_NAMESPACE` and value `myns1`
