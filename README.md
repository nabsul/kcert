# KCert: A Simple Let's Encrypt Cert Manager for Kubernetes

KCert is a simple, easy to run and understand alternative to [cert-manager](https://github.com/jetstack/cert-manager):

- Instead of 26000 lines of yaml, `KCert` deploys with less than 100 lines
- Instead of custom resources, `KCert` uses the existing standard Kubernetes objects
- The codebase is small and easy to understand

## How it Works

- Certs (TLS secrets) with a unique label set are managed by the service
- An ingress definition routes `.acme/challenge` requests to the application for HTTP challenge request
- Service provides a web UI and to manually manage and certs
- Automatic renewal can be be enabled from the configuration

## How to Use

- Deploy to your cluster using: `kubectl apply -f deploy.yml`
- To access the web UI run `kubectl -n kcert port-forward service/kcert 8080:80` and open your browser at `https://localhost:8080`
- Go to the configuration tab and configure your settings (email address, optional smtp settings, etc.)
- Go to "Unmanaged Certs" and select existing certs to be managed by KCert
- Create new certs or renew existing ones through the UI

## Building from Scratch

To build your own: `docker build -t [your tag] .`
