using KCert.Models;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using System.Collections.Concurrent;

namespace KCert.Services;

[Service]
public class RenewalHandler(ILogger<RenewalHandler> log, AcmeClient acme, K8sClient kube, KCertConfig cfg, CertClient cert)
{
    private readonly TimeSpan WaitTime = TimeSpan.FromSeconds(10);
    private readonly int NumRetries = 3;
    private readonly ConcurrentDictionary<Guid, Action> DnsChallenges = [];

    public void TryDnsChallenge(Guid challengeId)
    {
        if (DnsChallenges.TryRemove(challengeId, out var callback))
        {
            callback();
        }
    }

    public async Task<(Guid, string, string)> StartDnsChallengeAsync(string ns, string secretName, string domain, CancellationToken tok)
    {
        var hosts = new[] { domain };
        var rsa = RSA.Create();
        var key = cfg.AcmeKey;
        var logbuf = new BufferedLogger<RenewalHandler>(log);

        var (kid, nonce) = await InitAsync();
        log.LogInformation("Initialized renewal process for hosts {hosts} - kid {kid}", string.Join(",", hosts), kid);

        var order = await acme.CreateOrderAsync(key, kid, hosts, nonce);
        var orderUri = new Uri(order.Location);
        var finalizeUri = new Uri(order.Finalize);
        var authorizations = order.Authorizations.Select(a => new Uri(a)).ToList();
        var ids = order.Identifiers;
        var orderNonce = order.Nonce;
        nonce = orderNonce;

        log.LogInformation("Order {orderUri} created with finalizeUri {finalizeUri}", orderUri, finalizeUri);

        var thumb = cert.GetThumbprint();
        var auths = new List<AcmeAuthzResponse>();
        foreach (var (authUrl, host) in authorizations.Zip(ids))
        {
            var auth = await acme.GetAuthzAsync(key, authUrl, kid, nonce);
            auths.Add(auth);
            nonce = auth.Nonce;
        }

        var token = auths.SelectMany(a => a.Challenges).First(a => a.Type == "dns-01").Token;

        var challengeId = Guid.NewGuid();
        var wait = new TaskCompletionSource();
        DnsChallenges[challengeId] = () => _ = ContinueDnsChallengeAsync();

        var code = Base64UrlTextEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes($"{token}.{thumb}")));
        var dnsEntry = "_acme-challenge" + (domain.StartsWith("*.") ? domain[2..] : domain);

        log.LogInformation("Waiting for DNS to be setup...");
        return (challengeId, dnsEntry, code);

        async Task ContinueDnsChallengeAsync()
        {
            foreach (var (auth, uri) in auths.Zip(authorizations))
            {
                nonce = await ValidateDnsAuthorizationAsync(key, kid, nonce, uri, auth, tok);
            }

            var (certUri, finalizeNonce) = await FinalizeOrderAsync(key, orderUri, finalizeUri, hosts, kid, nonce, logbuf);
            log.LogInformation("Finalized order and received cert URI: {certUri}", certUri);

            await SaveCertAsync(cfg.AcmeKey, ns, secretName, certUri, kid, finalizeNonce);
            logbuf.LogInformation("Saved cert");
        }
    }


    private async Task<string> ValidateDnsAuthorizationAsync(string key, string kid, string nonce, Uri authUri, AcmeAuthzResponse auth, CancellationToken tok)
    {
        var challengeUri = new Uri(auth.Challenges.First(c => c.Type == "dns-01").Url);
        var chall = await acme.TriggerChallengeAsync(key, challengeUri, kid, nonce);
        nonce = chall.Nonce;
        log.LogInformation("TriggerChallenge {challengeUri}: {status}", challengeUri, chall.Status);

        var numRetries = NumRetries;
        do
        {
            log.LogInformation("Waiting for challenge to complete.");
            await Task.Delay(WaitTime, tok);
            auth = await acme.GetAuthzAsync(key, authUri, kid, nonce);
            nonce = auth.Nonce;
            log.LogInformation("Get Auth {authUri}: {status}", authUri, auth.Status);

        } while (numRetries-- >= 0 && !auth.Challenges.Any(c => c.Status == "valid"));


        if (!auth.Challenges.Any(c => c.Status == "valid"))
        {
            throw new Exception($"Auth {authUri} did not complete in time. Last Response: {JsonSerializer.Serialize(auth)}");
        }

        return nonce;
    }

    public async Task RenewCertAsync(string ns, string secretName, string[] hosts)
    {
        var logbuf = new BufferedLogger<RenewalHandler>(log);
        logbuf.Clear();

        try
        {
            var (kid, initNonce) = await InitAsync();
            logbuf.LogInformation("Initialized renewal process for secret {ns}/{secretName} - hosts {hosts} - kid {kid}",
                ns, secretName, string.Join(",", hosts), kid);

            var (orderUri, finalizeUri, authorizations, orderNonce) = await CreateOrderAsync(cfg.AcmeKey, hosts, kid, initNonce, logbuf);
            logbuf.LogInformation("Order {orderUri} created with finalizeUri {finalizeUri}", orderUri, finalizeUri);

            var validateNonce = orderNonce;
            foreach (var authUrl in authorizations)
            {
                validateNonce = await ValidateAuthorizationAsync(cfg.AcmeKey, kid, validateNonce, authUrl, logbuf);
                logbuf.LogInformation("Validated auth: {authUrl}", authUrl);
            }

            var (certUri, finalizeNonce) = await FinalizeOrderAsync(cfg.AcmeKey, orderUri, finalizeUri, hosts, kid, validateNonce, logbuf);
            logbuf.LogInformation("Finalized order and received cert URI: {certUri}", certUri);
            await SaveCertAsync(cfg.AcmeKey, ns, secretName, certUri, kid, finalizeNonce);
            logbuf.LogInformation("Saved cert");
        }
        catch (Exception ex)
        {
            logbuf.LogError(ex, "Certificate renewal failed.");
            throw new RenewalException(ex.Message, ex)
            {
                SecretNamespace = ns,
                SecretName = secretName,
                Logs = logbuf.Dump(),
            };
        }
    }

    private async Task<(string KID, string Nonce)> InitAsync()
    {
        await acme.ReadDirectoryAsync(cfg.AcmeDir);
        var nonce = await acme.GetNonceAsync();
        var account = await acme.CreateAccountAsync(nonce);
        var kid = account.Location;
        nonce = account.Nonce;
        return (kid, nonce);
    }

    private async Task<(Uri OrderUri, Uri FinalizeUri, List<Uri> Authorizations, string Nonce)> CreateOrderAsync(string key, string[] hosts, string kid, string nonce, ILogger logbuf)
    {
        var order = await acme.CreateOrderAsync(key, kid, hosts, nonce);
        logbuf.LogInformation("Created order: {status}", order.Status);
        var urls = order.Authorizations.Select(a => new Uri(a)).ToList();
        return (new Uri(order.Location), new Uri(order.Finalize), urls, order.Nonce);
    }

    private async Task<string> ValidateAuthorizationAsync(string key, string kid, string nonce, Uri authUri, ILogger logbuf)
    {
        var (waitTime, numRetries) = (cfg.AcmeWaitTime, cfg.AcmeNumRetries);
        var auth = await acme.GetAuthzAsync(key, authUri, kid, nonce);
        nonce = auth.Nonce;
        logbuf.LogInformation("Get Auth {authUri}: {status}", authUri, auth.Status);

        var url = auth.Challenges.FirstOrDefault(c => c.Type == "http-01")?.Url;
        var challengeUri = new Uri(url ?? throw new Exception("No http-01 url found in challenge"));
        var chall = await acme.TriggerChallengeAsync(key, challengeUri, kid, nonce);
        nonce = chall.Nonce;
        logbuf.LogInformation("TriggerChallenge {challengeUri}: {status}", challengeUri, chall.Status);

        do
        {
            await Task.Delay(waitTime);
            auth = await acme.GetAuthzAsync(key, authUri, kid, nonce);
            nonce = auth.Nonce;
            logbuf.LogInformation("Get Auth {authUri}: {status}", authUri, auth.Status);
        } while (numRetries-- > 0 && !auth.Challenges.Any(c => c.Status == "valid"));

        if (!auth.Challenges.Any(c => c.Status == "valid"))
        {
            throw new Exception($"Auth {authUri} did not complete in time. Last Response: {auth.Status}");
        }

        return nonce;
    }

    private async Task<(Uri CertUri, string Nonce)> FinalizeOrderAsync(string key, Uri orderUri, Uri finalizeUri,
        IEnumerable<string> hosts, string kid, string nonce, ILogger logbuf)
    {
        var (waitTime, numRetries) = (cfg.AcmeWaitTime, cfg.AcmeNumRetries);
        var finalize = await acme.FinalizeOrderAsync(key, finalizeUri, hosts, kid, nonce);
        logbuf.LogInformation("Finalize {finalizeUri}: {status}", finalizeUri, finalize.Status);

        while (numRetries-- >= 0 && finalize.Status != "valid")
        {
            await Task.Delay(waitTime);
            finalize = await acme.GetOrderAsync(key, orderUri, kid, finalize.Nonce);
            logbuf.LogInformation("Check Order {orderUri}: {finalize.Status}", orderUri, finalize.Status);
        }

        if (finalize.Status != "valid")
        {
            throw new Exception($"Order not complete: {finalize.Status}");
        }

        return (new Uri(finalize.Certificate), finalize.Nonce);
    }

    private async Task SaveCertAsync(string key, string ns, string secretName, Uri certUri, string kid, string nonce)
    {
        var certVal = await acme.GetCertAsync(key, certUri, kid, nonce);
        var pem = cert.GetPemKey();
        await kube.UpdateTlsSecretAsync(ns, secretName, pem, certVal);
    }
}
