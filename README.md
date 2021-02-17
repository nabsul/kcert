# KCert: A Basic Let's Encrypt Cert Manager for Kubernetes

WARNING: This code is experimental. Lots of churn, incomplete docs, and missing features.

KCert aims to be the simple, easy to run, easy to understand alternative to [cert-manager](https://github.com/jetstack/cert-manager):

- Instead of a Helm chart or 26000 lines of yaml, `KCert` deploys with less than 100 lines of yaml
- Instead of custom resources, `KCert` uses the existing standard objects
- The codebase is small and easy to understand

## How it Works

- Components (deployment, ingress, configs, etc.) deploy to their own namespace
- Service provides a management web UI and `.well-know/acme-challenge` HTTP Challenge endpoints
- Certs are manually renewed by clicking the "renew" button in the table

## How to Use

- Deploy to your cluster using: `kubectl apply -f deploy.yml`
- Forward the web UI to your local machine: `kubectl -n kcert port-forward service/kcert 8080:80`
- Open the web UI in your browser: `https://localhost:8080`
- Go to the configuration tab and configure your settings
- Refresh the Challenge Ingresses by clicking the "Sync" button
- Go back to the main page and select an ingress
- Click the "Get Cert" button and wait for the result (be sure to only click once)

## Building from Scratch

You're welcome to use my build which is hosted on Docker hub at `nabsul/kcert`.
However, it's not really a good security practice to run un-trusted Docker images.
To build your own:

- Create a Docker account if you don't have one
- Create your own repo to publish your images to
- Build the docker image: `docker build -t myusername/kcert:v0001 .`
- Publish the image to Docker Hub: `docker push myusername/kcert:v0001`
- In the `deploy.yml` file, replace `nabsul/kcert:test0001` with your image
