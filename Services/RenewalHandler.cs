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

    private async Task<string> ValidateAuthorizationDnsAsync(string key, string kid, string nonce, Uri authUri, ILogger logbuf)
    {
        var (waitTime, numRetriesOriginal) = (cfg.AcmeWaitTime, cfg.AcmeNumRetries);
        var auth = await acme.GetAuthzAsync(key, authUri, kid, nonce);
        nonce = auth.Nonce;

        string originalIdentifier = auth.Identifier.Value;
        bool isWildcard = originalIdentifier.StartsWith("*.");
        string domainForDnsChallenge = isWildcard ? originalIdentifier.Substring(2) : originalIdentifier;
        logbuf.LogInformation("Get Auth {authUri}: Initial Status: {status}, Identifier: {originalIdentifier}, Wildcard: {isWildcard}, Domain for DNS: {domainForDnsChallenge}",
            authUri, auth.Status, originalIdentifier, isWildcard, domainForDnsChallenge);

        var dnsChallenge = auth.Challenges.First(c => c.Type == "dns-01");

        // This block assumes dnsChallenge is not null due to the logic setting attemptDns = true
        logbuf.LogInformation("Attempting DNS-01 challenge for {originalIdentifier} (using DNS domain {domainForDnsChallenge})", originalIdentifier, domainForDnsChallenge);
        string txtRecordName = $"_acme-challenge.{domainForDnsChallenge}";
        string keyAuth = cert.GetKeyAuthorization(dnsChallenge.Token); // dnsChallenge asserted non-null by attemptDns logic
        string txtRecordValue;
        using (var sha256 = SHA256.Create())
        {
            txtRecordValue = Base64UrlTextEncoder.Encode(sha256.ComputeHash(Encoding.UTF8.GetBytes(keyAuth)));
        }

        logbuf.LogInformation("Attempting to create TXT record {txtRecordName} with value {txtRecordValue} for DNS domain {domainForDnsChallenge} using {providerName}.",
            txtRecordName, txtRecordValue, domainForDnsChallenge, selectedProvider.GetType().Name);
        await selectedProvider.CreateTxtRecordAsync(domainForDnsChallenge, txtRecordName, txtRecordValue);

        try
        {
            logbuf.LogInformation("TXT record created. Waiting for DNS propagation ({waitTime}).", waitTime);
            await Task.Delay(waitTime);

            logbuf.LogInformation("Triggering DNS-01 challenge validation with ACME server for {dnsChallengeUrl}.", dnsChallenge.Url);
            var chall = await acme.TriggerChallengeAsync(key, new Uri(dnsChallenge.Url), kid, nonce);
            nonce = chall.Nonce;
            logbuf.LogInformation("TriggerChallenge {dnsChallengeUrl} for DNS-01: {status}", dnsChallenge.Url, chall.Status);

            int numRetries = numRetriesOriginal;
            do
            {
                await Task.Delay(waitTime);
                auth = await acme.GetAuthzAsync(key, authUri, kid, nonce);
                nonce = auth.Nonce;
                var currentDnsChallengeStatus = auth.Challenges.FirstOrDefault(c => c.Type == "dns-01")?.Status;
                logbuf.LogInformation("Get Auth {authUri} for DNS-01 ({originalIdentifier}): Status: {status}, Challenge Status: {dnsStatus}",
                    authUri, originalIdentifier, auth.Status, currentDnsChallengeStatus ?? "not found");
                if (currentDnsChallengeStatus == "valid") break;
            } while (numRetries-- > 0 && auth.Status != "valid" && auth.Status != "invalid");

            if (!auth.Challenges.Any(c => c.Status == "valid"))
            {
                throw new Exception($"Auth {authUri} did not complete in time. Last Response: {auth.Status}");
            }
        }
        finally
        {
            try
            {
                await selectedProvider.DeleteTxtRecordAsync(domainForDnsChallenge, txtRecordName, txtRecordValue);
                logbuf.LogInformation("TXT record {txtRecordName} deleted.", txtRecordName);
            }
            catch (Exception deleteEx)
            {
                logbuf.LogError(deleteEx, "Failed to delete TXT record {txtRecordName}.", txtRecordName);
            }
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
