# KCert: A Simple Let's Encrypt Cert Manager for Kubernetes

KCert is a simple, easy to run and understand alternative to [cert-manager](https://github.com/jetstack/cert-manager):

- Instead of 26000 lines of yaml, `KCert` deploys with less than 100 lines
- Instead of custom resources, `KCert` uses the existing standard Kubernetes objects
- The codebase is small and easy to understand

## Installing KCert

The following instructions assume that you will be using the included `deploy.yml` file as your template for install KCert.
If you are customizing your setup you will likely need to modify the following instructions accordingly.

### Create KCert secrets

KCert configuration involves two secrets:

- The ACME ECDSA key which is needed to create and renew Let's Encrypt certificates
- The optional SMTP password if you want to have email notifications

You can create the ECDSA key using KCert from the command line:

- If you have .NET Core installed you can check out this repo and run `dotnet run generate-key`
- If you have Docker (or Podman) you can run `docker run -it nabsul/kcert:1.0.0 dotnet KCert.dll generate-key`

Assuming you have saved the ACME Key to a file called `acme.key` and your SMTP password to `smtp.pass`,
you can now create your Kubernetes secret with:

```sh
kubectl -n kcert create secret generic kcert --from-file=acme=./acme.key --from-file=smtp=./smtp.pass
```

### Deploy KCert

Starting with the `deploy.yml` in this repo, file the `appsettings.prod.yaml` section and review the JSON block there.
Fill in all the required values (marked with double underscores).
If you don't want to set up email notifications you can delete the `smtp` configuration block.

Once this you've configured your settings, deploy KCert by running `kubectl apply -f ./deploy.yml`.
Congratulations, KCert should now be running!

To check that everything is running as expected:

- Run `kubectl -n kcert logs svc/kcert` and make sure there are no error logs
- Run `kubectl -n kcert port-forward svc/kcert 80` and go to `http://localhost:80` in your browser

## How it Works

- An ingress definition routes `.acme/challenge` requests to KCert for HTTP challenge requests
- Service provides a web UI and to manually manage and certs
- Automatic renewal can be be enabled from the configuration

## How to Use

- Deploy to your cluster using: `kubectl apply -f deploy.yml`
- To access the web UI run `kubectl -n kcert port-forward svc/kcert 8080:80` and open your browser at `https://localhost:8080`
- Go to the configuration tab and configure your settings (email address, optional smtp settings, etc.)
- Go to "Unmanaged Certs" and select existing certs to be managed by KCert
- Create new certs or renew existing ones through the UI
- Turn on auto-renewal to automatically renew certs when they have less than 30 days validity

## Building from Scratch

To build your own: `docker build -t [your tag] .`
