using KCert.Models;
using Microsoft.AspNetCore.Authentication; // For Base64UrlTextEncoder
using System;
using System.Linq;
using System.Security.Cryptography; // For SHA256
using System.Text; // For Encoding
using System.Threading.Tasks;

namespace KCert.Services;

[Service]
public class RenewalHandler(
    ILogger<RenewalHandler> log, 
    AcmeClient acme, 
    K8sClient kube, 
    KCertConfig cfg, 
    CertClient cert, 
    AwsRoute53Provider route53Provider, 
    CloudflareProvider cloudflareProvider)
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
        var (waitTime, numRetriesOriginal) = (cfg.AcmeWaitTime, cfg.AcmeNumRetries);
        var auth = await acme.GetAuthzAsync(key, authUri, kid, nonce);
        nonce = auth.Nonce;
        
        string originalIdentifier = auth.Identifier.Value;
        bool isWildcard = originalIdentifier.StartsWith("*.");
        string domainForDnsChallenge = isWildcard ? originalIdentifier.Substring(2) : originalIdentifier;
        logbuf.LogInformation("Get Auth {authUri}: Initial Status: {status}, Identifier: {originalIdentifier}, Wildcard: {isWildcard}, Domain for DNS: {domainForDnsChallenge}", 
            authUri, auth.Status, originalIdentifier, isWildcard, domainForDnsChallenge);

        var dnsChallenge = auth.Challenges.FirstOrDefault(c => c.Type == "dns-01");
        var httpChallenge = auth.Challenges.FirstOrDefault(c => c.Type == "http-01");
        bool attemptDns = false;

        if (isWildcard)
        {
            if (dnsChallenge != null)
            {
                logbuf.LogInformation($"Identifier '{originalIdentifier}' is a wildcard. DNS-01 challenge is mandatory and available.");
                attemptDns = true;
            }
            else
            {
                logbuf.LogError($"Identifier '{originalIdentifier}' is a wildcard, but no DNS-01 challenge is available from ACME server. This is required for wildcard validation.");
                throw new Exception($"ACME server did not provide a DNS-01 challenge for wildcard domain {originalIdentifier}.");
            }
        }
        else if (dnsChallenge != null && cfg.PreferredChallengeType?.ToLower() == "dns-01")
        {
            logbuf.LogInformation($"DNS-01 is preferred and available for '{originalIdentifier}'.");
            attemptDns = true;
        }
        else if (dnsChallenge != null && httpChallenge == null)
        {
            logbuf.LogInformation($"DNS-01 is the only challenge type available for '{originalIdentifier}'.");
            attemptDns = true;
        }

        if (attemptDns)
        {
            // This block assumes dnsChallenge is not null due to the logic setting attemptDns = true
            logbuf.LogInformation("Attempting DNS-01 challenge for {originalIdentifier} (using DNS domain {domainForDnsChallenge})", originalIdentifier, domainForDnsChallenge);
            string txtRecordName = $"_acme-challenge.{domainForDnsChallenge}";
            string keyAuth = cert.GetKeyAuthorization(dnsChallenge!.Token); // dnsChallenge asserted non-null by attemptDns logic
            string txtRecordValue;
            using (var sha256 = SHA256.Create())
            {
                txtRecordValue = Base64UrlTextEncoder.Encode(sha256.ComputeHash(Encoding.UTF8.GetBytes(keyAuth)));
            }

            IDnsProvider? selectedProvider = null;
            if (cfg.EnableRoute53) selectedProvider = route53Provider;
            else if (cfg.EnableCloudflare) selectedProvider = cloudflareProvider;

            if (selectedProvider != null)
            {
                bool dnsRecordCreated = false;
                try
                {
                    logbuf.LogInformation("Attempting to create TXT record {txtRecordName} with value {txtRecordValue} for DNS domain {domainForDnsChallenge} using {providerName}.",
                        txtRecordName, txtRecordValue, domainForDnsChallenge, selectedProvider.GetType().Name);
                    await selectedProvider.CreateTxtRecordAsync(domainForDnsChallenge, txtRecordName, txtRecordValue);
                    dnsRecordCreated = true;
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
                    
                    if (auth.Challenges.FirstOrDefault(c => c.Type == "dns-01")?.Status == "valid")
                    {
                         logbuf.LogInformation("DNS-01 challenge for {txtRecordName} ({originalIdentifier}) validated successfully.", txtRecordName, originalIdentifier);
                         return nonce;
                    }
                    else
                    {
                        logbuf.LogWarning("DNS-01 challenge for {txtRecordName} ({originalIdentifier}) did not validate in time. Last auth status: {authStatus}, DNS challenge status: {dnsStatus}", 
                            txtRecordName, originalIdentifier, auth.Status, auth.Challenges.FirstOrDefault(c => c.Type == "dns-01")?.Status ?? "not found");
                        if (isWildcard) throw new Exception($"DNS-01 challenge failed for wildcard domain {originalIdentifier} and is mandatory.");
                        // Fall through to HTTP-01 if DNS-01 failed for non-wildcard and HTTP-01 is available
                    }
                }
                catch (Exception ex)
                {
                    logbuf.LogError(ex, "DNS-01 challenge for {txtRecordName} ({originalIdentifier}) failed.", txtRecordName, originalIdentifier);
                    if (isWildcard) throw; // Re-throw if wildcard, DNS-01 is mandatory
                    // Fall through to HTTP-01 if DNS-01 failed for non-wildcard and HTTP-01 is available
                }
                finally
                {
                    if (dnsRecordCreated)
                    {
                        try
                        {
                            logbuf.LogInformation("Attempting to delete TXT record {txtRecordName} for DNS domain {domainForDnsChallenge} using {providerName}.",
                                txtRecordName, domainForDnsChallenge, selectedProvider.GetType().Name);
                            await selectedProvider.DeleteTxtRecordAsync(domainForDnsChallenge, txtRecordName, txtRecordValue);
                            logbuf.LogInformation("TXT record {txtRecordName} deleted.", txtRecordName);
                        }
                        catch (Exception deleteEx)
                        {
                            logbuf.LogError(deleteEx, "Failed to delete TXT record {txtRecordName}.", txtRecordName);
                        }
                    }
                }
            }
            else
            {
                logbuf.LogWarning("DNS-01 challenge required or preferred for {originalIdentifier}, but no DNS provider is configured/enabled.", originalIdentifier);
                if (isWildcard) throw new Exception($"DNS-01 challenge is mandatory for wildcard domain {originalIdentifier}, but no DNS provider is configured.");
                // Fall through to HTTP-01 if DNS-01 was preferred (non-wildcard) but no provider configured, and HTTP-01 is available
            }
        }
        
        // Try HTTP-01 Challenge (if not returned by DNS-01 or if DNS-01 failed for non-wildcard and HTTP is an option)
        if (httpChallenge != null)
        {
            // If DNS was attempted for a non-wildcard and failed, this is the fallback.
            // If DNS was not attempted because it wasn't preferred/available (for non-wildcard), this is the primary path.
            logbuf.LogInformation("Attempting HTTP-01 challenge for {originalIdentifier}", originalIdentifier);
            var challengeUri = new Uri(httpChallenge.Url ?? throw new Exception($"No http-01 url found in challenge for {originalIdentifier}"));
            var chall = await acme.TriggerChallengeAsync(key, challengeUri, kid, nonce);
            nonce = chall.Nonce;
            logbuf.LogInformation("TriggerChallenge {challengeUri} for HTTP-01: {status}", challengeUri, chall.Status);

            int numRetries = numRetriesOriginal;
            do
            {
                await Task.Delay(waitTime);
                auth = await acme.GetAuthzAsync(key, authUri, kid, nonce);
                nonce = auth.Nonce;
                var currentHttpChallengeStatus = auth.Challenges.FirstOrDefault(c => c.Type == "http-01")?.Status;
                logbuf.LogInformation("Get Auth {authUri} for HTTP-01 ({originalIdentifier}): Status: {status}, Challenge Status: {httpStatus}", 
                    authUri, originalIdentifier, auth.Status, currentHttpChallengeStatus ?? "not found");
                if (currentHttpChallengeStatus == "valid") break;
            } while (numRetries-- > 0 && auth.Status != "valid" && auth.Status != "invalid");

            if (auth.Challenges.FirstOrDefault(c => c.Type == "http-01")?.Status == "valid")
            {
                logbuf.LogInformation("HTTP-01 challenge for {originalIdentifier} validated successfully.", originalIdentifier);
                return nonce;
            }
            else
            {
                 throw new Exception($"HTTP-01 challenge for {originalIdentifier} did not validate in time. Last auth status: {auth.Status}, HTTP challenge status: {auth.Challenges.FirstOrDefault(c => c.Type == "http-01")?.Status ?? "not found"}");
            }
        }
        
        // This point is reached if:
        // 1. DNS was attempted and failed for a wildcard (exception re-thrown from DNS block).
        // 2. DNS was attempted, failed for non-wildcard, AND no HTTP challenge was available.
        // 3. DNS was not attempted (e.g. not preferred, not available) AND no HTTP challenge was available.
        // 4. Wildcard required DNS, but no DNS provider was configured.
        logbuf.LogError("No suitable and successful challenge type (DNS-01 or HTTP-01) found or completed for {originalIdentifier}. Last auth status: {authStatus}", originalIdentifier, auth.Status);
        throw new Exception($"No suitable and successful challenge type (DNS-01 or HTTP-01) found or completed for {originalIdentifier}. Last auth status: {auth.Status}");
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
