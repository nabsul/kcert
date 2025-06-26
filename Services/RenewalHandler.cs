using KCert.Challenge;
using KCert.Models;
using Microsoft.AspNetCore.Authentication;
using System.Security.Cryptography;
using System.Text;

namespace KCert.Services;

[Service]
public class RenewalHandler(
    ILogger<RenewalHandler> log, 
    AcmeClient acme, 
    K8sClient kube, 
    KCertConfig cfg, 
    CertClient cert, 
    ChallengeProviderFactory providerFactory)
{
    private readonly IChallengeProvider chal = providerFactory.CreateProvider();

    public async Task RenewCertAsync(string ns, string secretName, string[] hosts, CancellationToken tok)
    {
        var logbuf = new BufferedLogger<RenewalHandler>(log);
        logbuf.Clear();

        try
        {
            var account = await InitAsync();
            var kid = account.Location;
            logbuf.LogInformation("Initialized renewal process for secret {ns}/{secretName} - hosts {hosts} - kid {kid}", ns, secretName, string.Join(",", hosts), kid);

            var (orderUri, finalizeUri, authorizations, nonce) = await CreateOrderAsync(cfg.AcmeKey, hosts, kid, account.Nonce, logbuf);
            logbuf.LogInformation("Order {orderUri} created with finalizeUri {finalizeUri}", orderUri, finalizeUri);

            List<AcmeAuthzResponse> auths = [];
            foreach (var authUrl in authorizations)
            {
                var auth = await acme.GetAuthzAsync(cfg.AcmeKey, authUrl, kid, nonce);
                auths.Add(auth);
                nonce = auth.Nonce;
                logbuf.LogInformation("Get Auth {authUri}: {status}", authUrl, auth.Status);
            }

            var chalState = await chal.PrepareChallengesAsync(auths, tok);

            foreach (var auth in auths)
            {
                nonce = await ValidateAuthorizationAsync(auth, cfg.AcmeKey, kid, nonce, new Uri(auth.Location), logbuf);
                logbuf.LogInformation("Validated auth: {authUrl}", auth.Location);
            }

            var (certUri, finalizeNonce) = await FinalizeOrderAsync(cfg.AcmeKey, orderUri, finalizeUri, hosts, kid, nonce, logbuf);
            logbuf.LogInformation("Finalized order and received cert URI: {certUri}", certUri);
            await SaveCertAsync(cfg.AcmeKey, ns, secretName, certUri, kid, finalizeNonce);
            logbuf.LogInformation("Saved cert");

            await chal.CleanupChallengeAsync(chalState, tok);
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

    private async Task<AcmeAccountResponse> InitAsync()
    {
        await acme.ReadDirectoryAsync(cfg.AcmeDir);
        var nonce = await acme.GetNonceAsync();
        return await acme.CreateAccountAsync(nonce);
    }

    private async Task<(Uri OrderUri, Uri FinalizeUri, List<Uri> Authorizations, string Nonce)> CreateOrderAsync(string key, string[] hosts, string kid, string nonce, ILogger logbuf)
    {
        var order = await acme.CreateOrderAsync(key, kid, hosts, nonce);
        logbuf.LogInformation("Created order: {status}", order.Status);
        var urls = order.Authorizations.Select(a => new Uri(a)).ToList();
        return (new Uri(order.Location), new Uri(order.Finalize), urls, order.Nonce);
    }

    private async Task<string> ValidateAuthorizationAsync(AcmeAuthzResponse auth, string key, string kid, string nonce, Uri authUri, ILogger logbuf)
    {
        var (waitTime, numRetries) = (cfg.AcmeWaitTime, cfg.AcmeNumRetries);
        var url = auth.Challenges.First(c => c.Type == chal.AcmeChallengeType).Url;
        var challengeUri = new Uri(url);
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
