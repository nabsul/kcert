using KCert.Models;

namespace KCert.Services;

[Service]
public class RenewalHandler(ILogger<RenewalHandler> log, AcmeClient acme, K8sClient kube, KCertConfig cfg, CertClient cert)
{
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
            
            if (finalize.Error is { } error)
            {
                logbuf.LogWarning("Check Order {orderUri}: {finalize.Status}\nError: {error}", orderUri, finalize.Status, error);
            }
            else
            {
                logbuf.LogInformation("Check Order {orderUri}: {finalize.Status}", orderUri, finalize.Status);
            }
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
