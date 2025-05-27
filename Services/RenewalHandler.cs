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
        logbuf.LogInformation("Get Auth {authUri}: Initial Status: {status}, Identifier: {identifier}", authUri, auth.Status, auth.Identifier.Value);

        string domainName = auth.Identifier.Value;

        // Try DNS-01 Challenge
        if (cfg.PreferredChallengeType?.ToLower() == "dns-01" || 
            !auth.Challenges.Any(c => c.Type == "http-01")) // Fallback to DNS if HTTP-01 not present
        {
            var dnsChallenge = auth.Challenges.FirstOrDefault(c => c.Type == "dns-01");
            if (dnsChallenge != null)
            {
                logbuf.LogInformation("Attempting DNS-01 challenge for {domainName}", domainName);
                string txtRecordName = $"_acme-challenge.{domainName}";
                string keyAuth = cert.GetKeyAuthorization(dnsChallenge.Token);
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
                        logbuf.LogInformation("Attempting to create TXT record {txtRecordName} with value {txtRecordValue} for domain {domainName} using {providerName}.",
                            txtRecordName, txtRecordValue, domainName, selectedProvider.GetType().Name);
                        await selectedProvider.CreateTxtRecordAsync(domainName, txtRecordName, txtRecordValue);
                        dnsRecordCreated = true;
                        logbuf.LogInformation("TXT record created. Waiting for DNS propagation ({waitTime}).", waitTime);
                        await Task.Delay(waitTime); // Consider a specific DNS propagation wait time if different

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
                            logbuf.LogInformation("Get Auth {authUri} for DNS-01: Status: {status}, Challenge Status: {dnsStatus}",
                                authUri, auth.Status, currentDnsChallengeStatus ?? "not found");
                            if (currentDnsChallengeStatus == "valid") break;
                        } while (numRetries-- > 0 && auth.Status != "valid" && auth.Status != "invalid");
                        
                        if (auth.Challenges.FirstOrDefault(c => c.Type == "dns-01")?.Status == "valid")
                        {
                             logbuf.LogInformation("DNS-01 challenge for {txtRecordName} validated successfully.", txtRecordName);
                             return nonce; // Successfully validated with DNS-01
                        }
                        else
                        {
                            logbuf.LogWarning("DNS-01 challenge for {txtRecordName} did not validate in time. Last auth status: {authStatus}, DNS challenge status: {dnsStatus}", 
                                txtRecordName, auth.Status, auth.Challenges.FirstOrDefault(c => c.Type == "dns-01")?.Status ?? "not found");
                            // Fall through to HTTP-01 if DNS-01 failed and HTTP-01 is available
                        }
                    }
                    catch (Exception ex)
                    {
                        logbuf.LogError(ex, "DNS-01 challenge for {txtRecordName} failed.", txtRecordName);
                        // Fall through to HTTP-01 if DNS-01 failed and HTTP-01 is available
                    }
                    finally
                    {
                        if (dnsRecordCreated)
                        {
                            try
                            {
                                logbuf.LogInformation("Attempting to delete TXT record {txtRecordName} for domain {domainName} using {providerName}.",
                                    txtRecordName, domainName, selectedProvider.GetType().Name);
                                await selectedProvider.DeleteTxtRecordAsync(domainName, txtRecordName, txtRecordValue);
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
                    logbuf.LogWarning("DNS-01 challenge preferred or required for {domainName}, but no DNS provider is configured/enabled.", domainName);
                }
            }
            else
            {
                logbuf.LogWarning("DNS-01 challenge preferred or required for {domainName}, but no DNS-01 challenge was offered by ACME server.", domainName);
            }
        }

        // Try HTTP-01 Challenge (if not returned by DNS-01 or if DNS-01 failed and HTTP is an option)
        var httpChallenge = auth.Challenges.FirstOrDefault(c => c.Type == "http-01");
        if (httpChallenge != null)
        {
            logbuf.LogInformation("Attempting HTTP-01 challenge for {domainName}", domainName);
            var challengeUri = new Uri(httpChallenge.Url ?? throw new Exception($"No http-01 url found in challenge for {domainName}"));
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
                logbuf.LogInformation("Get Auth {authUri} for HTTP-01: Status: {status}, Challenge Status: {httpStatus}", 
                    authUri, auth.Status, currentHttpChallengeStatus ?? "not found");
                if (currentHttpChallengeStatus == "valid") break;
            } while (numRetries-- > 0 && auth.Status != "valid" && auth.Status != "invalid");

            if (auth.Challenges.FirstOrDefault(c => c.Type == "http-01")?.Status == "valid")
            {
                logbuf.LogInformation("HTTP-01 challenge for {domainName} validated successfully.", domainName);
                return nonce;
            }
            else
            {
                 throw new Exception($"HTTP-01 challenge for {domainName} did not validate in time. Last auth status: {auth.Status}, HTTP challenge status: {auth.Challenges.FirstOrDefault(c => c.Type == "http-01")?.Status ?? "not found"}");
            }
        }
        
        throw new Exception($"No suitable and successful challenge type (DNS-01 or HTTP-01) found or completed for {domainName}. Last auth status: {auth.Status}");
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
