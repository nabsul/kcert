﻿using KCert.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace KCert.Services
{
    public class RenewalHandler
    {
        private readonly AcmeClient _acme;
        private readonly K8sClient _kube;
        private readonly KCertConfig _cfg;
        private readonly CertClient _cert;
        private readonly BufferedLogger<RenewalHandler> _log;

        public RenewalHandler(BufferedLogger<RenewalHandler> log, AcmeClient acme, K8sClient kube, KCertConfig cfg, CertClient cert)
        {
            _log = log;
            _acme = acme;
            _kube = kube;
            _cfg = cfg;
            _cert = cert;
        }

        public async Task RenewCertAsync(string ns, string secretName, string[] hosts, KCertParams p)
        {
            _log.Clear();

            try
            {
                var (kid, initNonce) = await InitAsync(p.AcmeKey, p.AcmeDirUrl, p.AcmeEmail, p.TermsAccepted);
                _log.LogInformation($"Initialized renewal process for secret {ns}/{secretName} - hosts {string.Join(",", hosts)} - kid {kid}");

                var (orderUri, finalizeUri, authorizations, orderNonce) = await CreateOrderAsync(p.AcmeKey, hosts, kid, initNonce);
                _log.LogInformation($"Order {orderUri} created with finalizeUri {finalizeUri}");

                var validateNonce = orderNonce;
                foreach (var authUrl in authorizations)
                {
                    validateNonce = await ValidateAuthorizationAsync(p.AcmeKey, kid, validateNonce, authUrl);
                    _log.LogInformation($"Validated auth: {authUrl}");
                }

                var (certUri, finalizeNonce) = await FinalizeOrderAsync(p.AcmeKey, orderUri, finalizeUri, hosts, kid, validateNonce);
                _log.LogInformation($"Finalized order and received cert URI: {certUri}");
                await SaveCertAsync(p.AcmeKey, ns, secretName, certUri, kid, finalizeNonce);
                _log.LogInformation($"Saved cert");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, ex.Message);
                throw new RenewalException(ex.Message, ex)
                {
                    SecretNamespace = ns,
                    SecretName = secretName,
                    Logs = _log.Dump(),
                };
            }
        }

        private async Task<(string KID, string Nonce)> InitAsync(string key, Uri acmeDir, string email, bool termsAccepted)
        {
            await _acme.ReadDirectoryAsync(acmeDir);
            var nonce = await _acme.GetNonceAsync();
            var account = await _acme.CreateAccountAsync(key, email, nonce, termsAccepted);
            var kid = account.Location;
            nonce = account.Nonce;
            return (kid, nonce);
        }

        private async Task<(Uri OrderUri, Uri FinalizeUri, List<Uri> Authorizations, string Nonce)> CreateOrderAsync(string key, string[] hosts, string kid, string nonce)
        {
            var order = await _acme.CreateOrderAsync(key, kid, hosts, nonce);
            _log.LogInformation($"Created order: {order.Status}");
            var urls = order.Authorizations.Select(a => new Uri(a)).ToList();
            return (new Uri(order.Location), new Uri(order.Finalize), urls, order.Nonce);
        }

        private async Task<string> ValidateAuthorizationAsync(string key, string kid, string nonce, Uri authUri)
        {
            var (waitTime, numRetries) = (_cfg.AcmeWaitTime, _cfg.AcmeNumRetries);
            var auth = await _acme.GetAuthzAsync(key, authUri, kid, nonce);
            nonce = auth.Nonce;
            _log.LogInformation($"Get Auth {authUri}: {auth.Status}");

            var challengeUri = new Uri(auth.Challenges.FirstOrDefault(c => c.Type == "http-01")?.Url);
            var chall = await _acme.TriggerChallengeAsync(key, challengeUri, kid, nonce);
            nonce = chall.Nonce;
            _log.LogInformation($"TriggerChallenge {challengeUri}: {chall.Status}");

            do
            {
                await Task.Delay(waitTime);
                auth = await _acme.GetAuthzAsync(key, authUri, kid, nonce);
                nonce = auth.Nonce;
                _log.LogInformation($"Get Auth {authUri}: {auth.Status}");
            } while (numRetries-- > 0 && !auth.Challenges.Any(c => c.Status == "valid"));

            if (!auth.Challenges.Any(c => c.Status == "valid"))
            {
                throw new Exception($"Auth {authUri} did not complete in time. Last Response: {auth.Status}");
            }

            return nonce;
        }

        private async Task<(Uri CertUri, string Nonce)> FinalizeOrderAsync(string key, Uri orderUri, Uri finalizeUri,
            IEnumerable<string> hosts, string kid, string nonce)
        {
            var (waitTime, numRetries) = (_cfg.AcmeWaitTime, _cfg.AcmeNumRetries);
            var finalize = await _acme.FinalizeOrderAsync(key, finalizeUri, hosts, kid, nonce);
            _log.LogInformation($"Finalize {finalizeUri}: {finalize.Status}");

            while(numRetries-- >= 0 && finalize.Status != "valid")
            {
                await Task.Delay(waitTime);
                finalize = await _acme.GetOrderAsync(key, orderUri, kid, finalize.Nonce);
                _log.LogInformation($"Check Order {orderUri}: {finalize.Status}");
            }

            if (finalize.Status != "valid")
            {
                throw new Exception($"Order not complete: {finalize.Status}");
            }

            return (new Uri(finalize.Certificate), finalize.Nonce);
        }

        private async Task SaveCertAsync(string key, string ns, string secretName, Uri certUri, string kid, string nonce)
        {
            var cert = await _acme.GetCertAsync(key, certUri, kid, nonce);
            var pem = _cert.GetPemKey();
            await _kube.UpdateTlsSecretAsync(ns, secretName, pem, cert);
        }
    }
}
