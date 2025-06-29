namespace KCert.Challenge;

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using KCert.Models;
using KCert.Config;


public class CloudflareProvider(KCertConfig cfg, DnsUtils util, ILogger<CloudflareProvider> log) : IChallengeProvider
{
    public string AcmeChallengeType => "dns-01";
    private record CloudflareChallengeState(string ZoneId, string DomainName, string RecordName, string RecordValue);

    public async Task<object?> PrepareChallengesAsync(IEnumerable<AcmeAuthzResponse> auths, CancellationToken tok)
    {
        var res = new List<CloudflareChallengeState>();

        foreach (var auth in auths)
        {
            var domain = util.StripWildcard(auth.Identifier.Value);
            var recordName = util.GetTextRecordKey(domain);
            var recordValue = util.GetTextRecordValue(auth, domain);

            log.LogInformation("Preparing DNS challenge for domain {domain} with record {recordName} and value {recordValue}", domain, recordName, recordValue);
            var zoneId = await CreateTxtRecordAsync(domain, recordName, recordValue, tok);
            res.Add(new CloudflareChallengeState(zoneId, domain, recordName, recordValue));
        }

        return res;
    }

    public async Task CleanupChallengeAsync(object? state, CancellationToken tok)
    {
        if (state is not List<CloudflareChallengeState> states)
        {
            throw new ArgumentException("Invalid state provided for Cloudflare challenge cleanup. Expected List<CloudflareChallengeState>.", nameof(state));
        }

        foreach (var s in states)
        {
            await DeleteTxtRecordAsync(s.ZoneId, s.DomainName, s.RecordName, s.RecordValue, tok);
        }
    }

    private readonly HttpClient _httpClient = GetHttpClient(cfg);
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static HttpClient GetHttpClient(KCertConfig cfg)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri("https://api.cloudflare.com/client/v4/")
        };

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cfg.CloudflareApiToken);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private async Task<string> GetZoneIdAsync(string domainName, CancellationToken tok)
    {
        // Cloudflare's API matches zones by name. If domainName is "sub.example.com",
        // it will find "example.com" zone.
        // We extract the actual registrable domain part for a more direct match.
        var parts = domainName.Split('.');
        var registrableDomain = domainName; // Default for single-label domains or if extraction fails
        if (parts.Length >= 2)
        {
            registrableDomain = $"{parts[^2]}.{parts[^1]}";
        }

        log.LogDebug("Attempting to find zone ID for domain: {Domain}", registrableDomain);
        var resp = await GetAsync<JsonDocument>($"zones?name={registrableDomain}", tok);
        try
        {
            var zoneId = resp!.RootElement.GetProperty("Result")[0].GetProperty("Id").GetString()
                ?? throw new InvalidOperationException("Zone ID not found in response.");
            log.LogInformation("Found zone ID: {ZoneId} for domain: {Domain}", zoneId, registrableDomain);
            return zoneId;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"No zone found for domain {domainName}. Response: {JsonSerializer.Serialize(resp)}.", ex);
        }
    }

    public async Task<string> CreateTxtRecordAsync(string domainName, string recordName, string recordValue, CancellationToken tok)
    {
        var zoneId = await GetZoneIdAsync(domainName, tok);
        var payload = new
        {
            type = "TXT",
            name = recordName, // Cloudflare expects the full record name
            content = recordValue,
            ttl = 120 // Common for ACME challenges, min is 60 or 120 depending on plan
        };

        await PostAsync<JsonDocument>($"zones/{zoneId}/dns_records", payload, tok);
        return zoneId;
    }

    public async Task DeleteTxtRecordAsync(string zoneId, string domainName, string recordName, string recordValue, CancellationToken tok)
    {
        log.LogDebug("Attempting to find DNS record ID for Name: {RecordName}, Content: {RecordValue} in zone {ZoneId}", recordName, recordValue, zoneId);
        var resp = await GetAsync<JsonDocument>($"zones/{zoneId}/dns_records?type=TXT&name={recordName}", tok);
        var recordId = resp?.RootElement.GetProperty("Result")[0].GetProperty("Id").GetString()
            ?? throw new InvalidOperationException($"No TXT record found for {recordName} in zone {zoneId}. With response: {JsonSerializer.Serialize(resp)}");

        log.LogInformation("Found DNS record ID: {RecordId} for {RecordName}", recordId, recordName);
        log.LogInformation("Attempting to delete TXT record ID: {RecordId} ({RecordName}) in zone {ZoneId}", recordId, recordName, zoneId);
        var del = await DeleteAsync<JsonDocument>($"zones/{zoneId}/dns_records/{recordId}", tok);
        log.LogInformation("Successfully deleted TXT record ID: {RecordId}. Response: {ResponseBody}", recordId, JsonSerializer.Serialize(del));
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken tok) where T : class => await RequestAsync<T>(HttpMethod.Get, path, null, tok);

    private async Task<T?> PostAsync<T>(string path, object payload, CancellationToken tok) where T : class => await RequestAsync<T>(HttpMethod.Post, path, payload, tok);

    private Task<T?> DeleteAsync<T>(string path, CancellationToken tok) where T : class => RequestAsync<T>(HttpMethod.Delete, path, null, tok);

    private async Task<T?> RequestAsync<T>(HttpMethod method, string path, object? payload, CancellationToken tok) where T : class
    {
        using var request = new HttpRequestMessage(method, path);
        if (payload != null)
        {
            var jsonPayload = JsonSerializer.Serialize(payload, _jsonOptions);
            request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, tok);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(tok);
            throw new Exception($"Error {method}ing data to {path}. Status: {response.StatusCode}. Body: {errorBody}");
        }

        return await JsonSerializer.DeserializeAsync<T>(response.Content.ReadAsStream(tok), _jsonOptions, tok);
    }
}
