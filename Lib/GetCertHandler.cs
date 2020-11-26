using k8s.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace KCert.Lib
{
    public class GetCertHandler
    {
        private readonly AcmeClient _acme;
        private readonly K8sClient _kube;
        private readonly IConfiguration _cfg;
        private readonly ILogger<GetCertHandler> _log;

        public GetCertHandler(ILogger<GetCertHandler> log, IConfiguration cfg, AcmeClient acme, K8sClient kube)
        {
            _log = log;
            _cfg = cfg;
            _acme = acme;
            _kube = kube;
        }

        public async Task<GetCertResult> GetCertAsync(string ns, string ingressName, KCertParams p, ECDsa sign)
        {
            string domain, kid, nonce;
            Uri orderUri, finalizeUri, certUri;
            IList<Uri> authorizations;

            var waitTime = TimeSpan.FromSeconds(_cfg.GetValue<int>("AcmeWaitTimeSeconds"));
            var numRetries = _cfg.GetValue<int>("AcmeNumRetries");
            var serviceName = _cfg.GetValue<string>("ServiceName");
            var servicePort = _cfg.GetValue<string>("ServicePort");
            var secretName = _cfg.GetValue<string>("SecretName");
            var kcertNs = _cfg.GetValue<string>("Namespace");

            var result = new GetCertResult { IngressNamespace = ns, IngressName = ingressName };

            try
            {
                if (ns != kcertNs)
                {
                    await _kube.CreateServiceAsync(ns, serviceName, kcertNs, servicePort);
                    AddLog(result, $"Temporary service in namespace {ns} created");
                }

                await UpdateIngressAsync(ns, ingressName, i => i.AddHttpChallenge(serviceName, servicePort));
                AddLog(result, $"Route Added");

                (domain, kid, nonce) = await InitAsync(sign, p.AcmeDirUrl, p.Email, ns, ingressName);
                AddLog(result, $"Initialized renewal process for intress {ns}/{ingressName} - domain {domain} - kid {kid}");

                (orderUri, finalizeUri, authorizations, nonce) = await CreateOrderAsync(sign, domain, kid, nonce);
                AddLog(result, $"Order {orderUri} created with finlizeUri {finalizeUri}");

                foreach (var authUrl in authorizations)
                {
                    nonce = await ValidateAuthorizationAsync(sign, kid, nonce, authUrl, waitTime, numRetries);
                    AddLog(result, $"Validated auth: {authUrl}");
                }

                var rsa = RSA.Create(2048);
                (certUri, nonce) = await FinalizeOrderAsync(sign, rsa, orderUri, finalizeUri, domain, kid, nonce, waitTime, numRetries);
                AddLog(result, $"Finalized order and received cert URI: {certUri}");
                await SaveCertAsync(sign, ns, ingressName, rsa, certUri, kid, nonce);
                AddLog(result, $"Saved cert");

                await UpdateIngressAsync(ns, ingressName, i => i.RemoveHttpChallenge());
                AddLog(result, $"Route Removed");

                if (ns != kcertNs)
                {
                    await _kube.DeleteServiceAsync(ns, serviceName);
                    AddLog(result, $"Deleted temporary service in namespace {ns} created");
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex;
            }

            return result;
        }

        private void AddLog(GetCertResult result, string message)
        {
            _log.LogInformation(message);
            result.Logs.Add(message);
        }

        private async Task UpdateIngressAsync(string ns, string name, Action<Networkingv1beta1Ingress> action)
        {
            var ingress = await _kube.GetIngressAsync(ns, name);
            action(ingress);
            await _kube.UpdateIngressAsync(ingress);
        }

        private async Task<(string Domain, string KID, string Nonce)> InitAsync(ECDsa sign, Uri acmeDir, string email, string ns, string ingressName)
        {
            await _acme.ReadDirectoryAsync(acmeDir);
            var ingress = await _kube.GetIngressAsync(ns, ingressName);
            if (ingress.Spec.Rules.Count != 1)
            {
                throw new Exception($"Ingress {ingress.Namespace()}:{ingress.Name()} must have a single rule defined");
            }

            var rule = ingress.Spec.Rules.First();
            var domain = rule.Host;

            var nonce = await _acme.GetNonceAsync();
            var account = await _acme.CreateAccountAsync(sign, email, nonce);
            _log.LogInformation($"Fetched account: {JsonSerializer.Serialize(account.Content)}");

            var kid = account.Location;
            nonce = account.Nonce;
            return (domain, kid, nonce);
        }

        private async Task<(Uri OrderUri, Uri FinalizeUri, List<Uri> Authorizations, string Nonce)> CreateOrderAsync(ECDsa sign, string domain, string kid, string nonce)
        {
            var order = await _acme.CreateOrderAsync(sign, kid, new[] { domain }, nonce);
            _log.LogInformation($"Created order: {JsonSerializer.Serialize(order.Content)}");
            return (new Uri(order.Location), order.FinalizeUri, order.AuthorizationUrls, order.Nonce);
        }

        private async Task<string> ValidateAuthorizationAsync(ECDsa sign, string kid, string nonce, Uri authUri, TimeSpan waitTime, int numRetries)
        {
            var auth = await _acme.GetAuthzAsync(sign, authUri, kid, nonce);
            nonce = auth.Nonce;
            _log.LogInformation($"Get Auth {authUri}: {JsonSerializer.Serialize(auth.Content)}");

            var chall = await _acme.TriggerChallengeAsync(sign, auth.HttpChallengeUri, kid, nonce);
            nonce = chall.Nonce;
            _log.LogInformation($"TriggerChallenge {auth.HttpChallengeUri}: {JsonSerializer.Serialize(chall.Content)}");

            do
            {
                await Task.Delay(waitTime);
                auth = await _acme.GetAuthzAsync(sign, authUri, kid, nonce);
                nonce = auth.Nonce;
                _log.LogInformation($"Get Auth {authUri}: {JsonSerializer.Serialize(auth.Content)}");
            } while (numRetries-- > 0 && !auth.IsChallengeDone);

            if (!auth.IsChallengeDone)
            {
                throw new Exception($"Auth {authUri} did not complete in time. Last Response: {auth.Content}");
            }

            return nonce;
        }

        private async Task<(Uri CertUri, string Nonce)> FinalizeOrderAsync(ECDsa sign, RSA rsa, Uri orderUri, Uri finalizeUri,
            string domain, string kid, string nonce, TimeSpan waitTime, int numRetries)
        {
            var finalize = await _acme.FinalizeOrderAsync(rsa, sign, finalizeUri, domain, kid, nonce);
            _log.LogInformation($"Finalize {finalizeUri}: {JsonSerializer.Serialize(finalize.Content)}");

            do
            {
                await Task.Delay(waitTime);
                finalize = await _acme.GetOrderAsync(sign, orderUri, kid, finalize.Nonce);
                _log.LogInformation($"Check Order {orderUri}: {JsonSerializer.Serialize(finalize.Content)}");
            } while (numRetries-- > 0 && !finalize.IsOrderFinalized);

            if (!finalize.IsOrderFinalized)
            {
                throw new Exception($"Order not complete: {JsonSerializer.Serialize(finalize.Content)}");
            }

            return (finalize.CertUri, finalize.Nonce);
        }

        private async Task SaveCertAsync(ECDsa sign, string ns, string ingressName, RSA rsa, Uri certUri, string kid, string nonce)
        {
            var cert = await _acme.GetCertAsync(sign, certUri, kid, nonce);
            var key = rsa.GetPemKey();
            var ingress = await _kube.GetIngressAsync(ns, ingressName);
            var secret = ingress.Spec.Tls.First().SecretName;
            await _kube.UpdateTlsSecretAsync(ns, secret, key, cert);
        }
    }
}
