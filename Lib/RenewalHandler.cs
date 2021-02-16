using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace KCert.Lib
{
    [Service]
    public class RenewalHandler
    {
        private readonly AcmeClient _acme;
        private readonly K8sClient _kube;
        private readonly KCertConfig _cfg;
        private readonly ILogger<RenewalHandler> _log;

        public RenewalHandler(ILogger<RenewalHandler> log, AcmeClient acme, K8sClient kube, KCertConfig cfg)
        {
            _log = log;
            _acme = acme;
            _kube = kube;
            _cfg = cfg;
        }

        public async Task<RenewalResult> GetCertAsync(string ns, string secretName, string[] hosts, KCertParams p, ECDsa sign)
        {
            if (hosts.Length != 1)
            {
                throw new Exception($"Secret {ns}:{secretName} does must have one and only one host configured in ingresses. Found {hosts.Length}: {string.Join(",", hosts)}");
            }

            var result = new RenewalResult { SecretNamespace = ns, SecretName = secretName };

            try
            {
                var (kid, initNonce) = await InitAsync(sign, p.AcmeDirUrl, p.AcmeEmail, p.TermsAccepted);
                LogInformation(result, $"Initialized renewal process for secret {ns}/{secretName} - hosts {string.Join(",", hosts)} - kid {kid}");

                var (orderUri, finalizeUri, authorizations, orderNonce) = await CreateOrderAsync(sign, hosts, kid, initNonce);
                LogInformation(result, $"Order {orderUri} created with finalizeUri {finalizeUri}");

                var validateNonce = orderNonce;
                foreach (var authUrl in authorizations)
                {
                    validateNonce = await ValidateAuthorizationAsync(sign, kid, orderNonce, authUrl);
                    LogInformation(result, $"Validated auth: {authUrl}");
                }

                var rsa = RSA.Create(2048);
                var (certUri, finalizeNonce) = await FinalizeOrderAsync(sign, rsa, orderUri, finalizeUri, hosts.First(), kid, validateNonce);
                LogInformation(result, $"Finalized order and received cert URI: {certUri}");
                await SaveCertAsync(sign, ns, secretName, rsa, certUri, kid, finalizeNonce);
                LogInformation(result, $"Saved cert");

                result.Success = true;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Renew failed");
                result.Success = false;
                result.Error = ex;
            }

            return result;
        }

        private void LogInformation(RenewalResult result, string message)
        {
            _log.LogInformation(message);
            result.Logs.Add(message);
        }

        private async Task<(string KID, string Nonce)> InitAsync(ECDsa sign, Uri acmeDir, string email, bool termsAccepted)
        {
            await _acme.ReadDirectoryAsync(acmeDir);
            var nonce = await _acme.GetNonceAsync();
            var account = await _acme.CreateAccountAsync(sign, email, nonce, termsAccepted);
            var kid = account.Location;
            nonce = account.Nonce;
            return (kid, nonce);
        }

        private async Task<(Uri OrderUri, Uri FinalizeUri, List<Uri> Authorizations, string Nonce)> CreateOrderAsync(ECDsa sign, string[] hosts, string kid, string nonce)
        {
            var order = await _acme.CreateOrderAsync(sign, kid, hosts, nonce);
            _log.LogInformation($"Created order: {order.Status}");
            var urls = order.Authorizations.Select(a => new Uri(a)).ToList();
            return (new Uri(order.Location), new Uri(order.Finalize), urls, order.Nonce);
        }

        private async Task<string> ValidateAuthorizationAsync(ECDsa sign, string kid, string nonce, Uri authUri)
        {
            var (waitTime, numRetries) = (_cfg.AcmeWaitTime, _cfg.AcmeNumRetries);
            var auth = await _acme.GetAuthzAsync(sign, authUri, kid, nonce);
            nonce = auth.Nonce;
            _log.LogInformation($"Get Auth {authUri}: {auth.Status}");

            var challengeUri = new Uri(auth.Challenges.FirstOrDefault(c => c.Type == "http-01")?.Url);
            var chall = await _acme.TriggerChallengeAsync(sign, challengeUri, kid, nonce);
            nonce = chall.Nonce;
            _log.LogInformation($"TriggerChallenge {challengeUri}: {chall.Status}");

            do
            {
                await Task.Delay(waitTime);
                auth = await _acme.GetAuthzAsync(sign, authUri, kid, nonce);
                nonce = auth.Nonce;
                _log.LogInformation($"Get Auth {authUri}: {auth.Status}");
            } while (numRetries-- > 0 && !auth.Challenges.Any(c => c.Status == "valid"));

            if (!auth.Challenges.Any(c => c.Status == "valid"))
            {
                throw new Exception($"Auth {authUri} did not complete in time. Last Response: {auth.Status}");
            }

            return nonce;
        }

        private async Task<(Uri CertUri, string Nonce)> FinalizeOrderAsync(ECDsa sign, RSA rsa, Uri orderUri, Uri finalizeUri,
            string domain, string kid, string nonce)
        {
            var (waitTime, numRetries) = (_cfg.AcmeWaitTime, _cfg.AcmeNumRetries);
            var finalize = await _acme.FinalizeOrderAsync(rsa, sign, finalizeUri, domain, kid, nonce);
            _log.LogInformation($"Finalize {finalizeUri}: {finalize.Status}");

            while(numRetries-- >= 0 && finalize.Status != "valid")
            {
                await Task.Delay(waitTime);
                finalize = await _acme.GetOrderAsync(sign, orderUri, kid, finalize.Nonce);
                _log.LogInformation($"Check Order {orderUri}: {finalize.Status}");
            }

            if (finalize.Status != "valid")
            {
                throw new Exception($"Order not complete: {finalize.Status}");
            }

            return (new Uri(finalize.Certificate), finalize.Nonce);
        }

        private async Task SaveCertAsync(ECDsa sign, string ns, string secretName, RSA rsa, Uri certUri, string kid, string nonce)
        {
            var cert = await _acme.GetCertAsync(sign, certUri, kid, nonce);
            var key = rsa.GetPemKey();
            await _kube.UpdateTlsSecretAsync(ns, secretName, key, cert);
        }
    }
}
