# Testing KCert

This doc describes how to create a temporary cluster and test KCert's functionality end to end.

## nginx ingress controller

```sh
helm repo add ingress-nginx https://kubernetes.github.io/ingress-nginx
helm repo update
helm install nginx-ingress ingress-nginx/ingress-nginx \
  --namespace ingress-nginx --create-namespace \
  --set controller.publishService.enabled=true
```