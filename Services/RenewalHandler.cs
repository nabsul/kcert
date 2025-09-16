using KCert.Challenge;
using KCert.Models;

namespace KCert.Services;

public class RenewalHandler(ILogger<RenewalHandler> log, AcmeClient acme, K8sClient kube, KCertConfig cfg, CertClient cert, IChallengeProvider chal)
{
    public async Task RenewCertAsync(string ns, string secretName, string[] hosts, CancellationToken tok)
    {
        var logbuf = new BufferedLogger<RenewalHandler>(log);
        logbuf.Clear();

        try
        {
            var account = await InitAsync(tok);
            var kid = account.Location;
            logbuf.LogInformation("Initialized renewal process for secret {ns}/{secretName} - hosts {hosts} - kid {kid}", ns, secretName, string.Join(",", hosts), kid);

            var (orderUri, finalizeUri, authorizations) = await CreateOrderAsync(cfg.AcmeKey, hosts, kid, logbuf, tok);
            logbuf.LogInformation("Order {orderUri} created with finalizeUri {finalizeUri}", orderUri, finalizeUri);

            List<AcmeAuthzResponse> auths = [];
            foreach (var authUrl in authorizations)
            {
                var auth = await acme.GetAuthzAsync(cfg.AcmeKey, authUrl, kid, tok);
                auths.Add(auth);
                logbuf.LogInformation("Get Auth {authUri}: {status}", authUrl, auth.Status);
            }

            var chalState = await chal.PrepareChallengesAsync(auths, tok);

            foreach (var auth in auths)
            {
                await ValidateAuthorizationAsync(auth, cfg.AcmeKey, kid, new Uri(auth.Location), logbuf, tok);
                logbuf.LogInformation("Validated auth: {authUrl}", auth.Location);
            }

            var certUri = await FinalizeOrderAsync(cfg.AcmeKey, orderUri, finalizeUri, hosts, kid, logbuf, tok);
            logbuf.LogInformation("Finalized order and received cert URI: {certUri}", certUri);
            await SaveCertAsync(cfg.AcmeKey, ns, secretName, certUri, kid, tok);
            logbuf.LogInformation("Saved cert");

            await chal.CleanupChallengeAsync(chalState, tok);
        }
        catch (Exception ex)
        {
            throw new RenewalException(ex.Message, ex)
            {
                SecretNamespace = ns,
                SecretName = secretName,
                Logs = logbuf.Dump(),
            };
        }
    }

    private async Task<AcmeAccountResponse> InitAsync(CancellationToken tok)
    {
        await acme.InitAsync(tok);
        return await acme.CreateAccountAsync(tok);
    }

    private async Task<(Uri OrderUri, Uri FinalizeUri, List<Uri> Authorizations)> CreateOrderAsync(string key, string[] hosts, string kid, ILogger logbuf, CancellationToken tok)
    {
        var order = await acme.CreateOrderAsync(key, kid, hosts, tok);
        logbuf.LogInformation("Created order: {status}", order.Status);
        var urls = order.Authorizations.Select(a => new Uri(a)).ToList();
        return (new Uri(order.Location), new Uri(order.Finalize), urls);
    }

    private async Task ValidateAuthorizationAsync(AcmeAuthzResponse auth, string key, string kid, Uri authUri, ILogger logbuf, CancellationToken tok)
    {
        var (waitTime, numRetries) = (cfg.AcmeWaitTime, cfg.AcmeNumRetries);
        var url = auth.Challenges.First(c => c.Type == chal.AcmeChallengeType).Url;
        var challengeUri = new Uri(url);
        var chall = await acme.TriggerChallengeAsync(key, challengeUri, kid, tok);
        logbuf.LogInformation("TriggerChallenge {challengeUri}: {status}", challengeUri, chall.Status);

        do
        {
            await Task.Delay(waitTime, tok);
            auth = await acme.GetAuthzAsync(key, authUri, kid, tok);
            logbuf.LogInformation("Get Auth {authUri}: {status}", authUri, auth.Status);
        } while (numRetries-- > 0 && !auth.Challenges.Any(c => c.Status == "valid"));

        if (!auth.Challenges.Any(c => c.Status == "valid"))
        {
            throw new Exception($"Auth {authUri} did not complete in time. Last Response: {auth.Status}");
        }
    }

    private async Task<Uri> FinalizeOrderAsync(string key, Uri orderUri, Uri finalizeUri,
        IEnumerable<string> hosts, string kid, ILogger logbuf, CancellationToken tok)
    {
        var (waitTime, numRetries) = (cfg.AcmeWaitTime, cfg.AcmeNumRetries);
        var finalize = await acme.FinalizeOrderAsync(key, finalizeUri, hosts, kid, tok);
        logbuf.LogInformation("Finalize {finalizeUri}: {status}", finalizeUri, finalize.Status);

        while (numRetries-- >= 0 && finalize.Status != "valid")
        {
            await Task.Delay(waitTime, tok);
            finalize = await acme.GetOrderAsync(key, orderUri, kid, tok);
            logbuf.LogInformation("Check Order {orderUri}: {finalize.Status}", orderUri, finalize.Status);
        }

        if (finalize.Status != "valid")
        {
            throw new Exception($"Order not complete: {finalize.Status}");
        }

        return new Uri(finalize.Certificate);
    }

    private async Task SaveCertAsync(string key, string ns, string secretName, Uri certUri, string kid, CancellationToken tok)
    {
        var certVal = await acme.GetCertAsync(key, certUri, kid, tok);
        var pem = cert.GetPemKey();
        await kube.UpdateTlsSecretAsync(ns, secretName, pem, certVal, tok);
    }
}
