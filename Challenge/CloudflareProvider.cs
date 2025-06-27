namespace KCert.Challenge;

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;
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
    private static readonly ConcurrentDictionary<string, string> _zoneIdCache = new();
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
            registrableDomain = $"{parts[parts.Length - 2]}.{parts[parts.Length - 1]}";
        }
        
        if (_zoneIdCache.TryGetValue(registrableDomain, out var cachedZoneId))
        {
            log.LogDebug("Cache hit for zone ID: {ZoneId} for domain: {Domain}", cachedZoneId, registrableDomain);
            return cachedZoneId;
        }

        log.LogDebug("Attempting to find zone ID for domain: {Domain}", registrableDomain);
        var response = await _httpClient.GetAsync($"zones?name={registrableDomain}", tok);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(tok);
            throw new Exception($"Error fetching zone ID for {registrableDomain}. Status: {response.StatusCode}. Body: {errorBody}");
        }

        var content = await response.Content.ReadAsStringAsync(tok);
        var cfResponse = JsonSerializer.Deserialize<CloudflareZonesResponse>(content, _jsonOptions);

        if (cfResponse?.Result == null || !cfResponse.Result.Any())
        {
            throw new Exception($"No zone found for domain: {registrableDomain}");
        }

        // Assuming the first result is the correct one if multiple are returned (shouldn't happen for exact name match)
        var zoneId = cfResponse.Result.First().Id ?? throw new InvalidOperationException($"No zone ID found for domain {registrableDomain} in Cloudflare response.");
        log.LogInformation("Found zone ID: {ZoneId} for domain: {Domain}", zoneId, registrableDomain);
        _zoneIdCache.TryAdd(registrableDomain, zoneId);
        return zoneId;
    }

    public async Task<string> CreateTxtRecordAsync(string domainName, string recordName, string recordValue, CancellationToken tok)
    {
        var zoneId = await GetZoneIdAsync(domainName, tok);
        var payload = new CloudflareDnsRequest
        {
            Type = "TXT",
            Name = recordName, // Cloudflare expects the full record name
            Content = recordValue,
            Ttl = 120 // Common for ACME challenges, min is 60 or 120 depending on plan
        };

        var jsonPayload = JsonSerializer.Serialize(payload);
        var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        log.LogInformation("Attempting to create TXT record: {RecordName} with value: {RecordValue} in zone {ZoneId}", recordName, recordValue, zoneId);
        var response = await _httpClient.PostAsync($"zones/{zoneId}/dns_records", httpContent, tok);
        var responseBody = await response.Content.ReadAsStringAsync(tok);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Error creating TXT record {recordName}. Status: {response.StatusCode}. Response: {responseBody}");
        }

        return zoneId;
    }

    public async Task DeleteTxtRecordAsync(string zoneId, string domainName, string recordName, string recordValue, CancellationToken tok)
    {
        // Note: Cloudflare API expects the 'name' to be the FQDN of the record.
        log.LogDebug("Attempting to find DNS record ID for Name: {RecordName}, Content: {RecordValue} in zone {ZoneId}", recordName, recordValue, zoneId);
        var listResponse = await _httpClient.GetAsync($"zones/{zoneId}/dns_records?type=TXT&name={recordName}&content={recordValue}", tok);

        if (!listResponse.IsSuccessStatusCode)
        {
            var errorBody = await listResponse.Content.ReadAsStringAsync(tok);
            throw new Exception($"Error listing DNS records to find ID for {recordName}. Status: {listResponse.StatusCode}. Body: {errorBody}");
        }

        var listContent = await listResponse.Content.ReadAsStringAsync(tok);
        var cfListResponse = JsonSerializer.Deserialize<CloudflareDnsListResponse>(listContent, _jsonOptions);

        var result = cfListResponse?.Result;
        if (result == null || result.Count == 0)
        {
            throw new Exception($"No TXT record found for Name: {recordName} and Content: {recordValue} in zone {zoneId}. It might have been already deleted.");
        }

        var recordId = result.First().Id; // Assuming first one is the match
        if (string.IsNullOrEmpty(recordId))
        {
            throw new Exception($"TXT record ID for {recordName} not found, cannot delete.");
        }

        log.LogInformation("Found DNS record ID: {RecordId} for {RecordName}", recordId, recordName);

        log.LogInformation("Attempting to delete TXT record ID: {RecordId} ({RecordName}) in zone {ZoneId}", recordId, recordName, zoneId);
        var deleteResponse = await _httpClient.DeleteAsync($"zones/{zoneId}/dns_records/{recordId}", tok);
        var deleteResponseBody = await deleteResponse.Content.ReadAsStringAsync(tok);

        if (!deleteResponse.IsSuccessStatusCode)
        {
            throw new Exception($"Error deleting TXT record ID: {recordId}. Status: {deleteResponse.StatusCode}. Response: {deleteResponseBody}");
        }

        log.LogInformation("Successfully deleted TXT record ID: {RecordId}. Response: {ResponseBody}", recordId, deleteResponseBody);
    }

    // Helper classes for JSON deserialization

    private class CloudflareZonesResponse
    {
        public List<CloudflareZone>? Result { get; set; }
        public bool Success { get; set; }
        // public List<object>? Errors { get; set; } // Can be added for more detailed error handling
        // public List<object>? Messages { get; set; }
    }

    private class CloudflareZone
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
    }

    private class CloudflareDnsRequest
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "TXT";
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        [JsonPropertyName("content")]
        public string? Content { get; set; }
        [JsonPropertyName("ttl")]
        public int Ttl { get; set; } = 120; // Default TTL
         // proxied is not applicable for TXT records
    }

    private class CloudflareDnsListResponse
    {
        public List<CloudflareDnsRecord>? Result { get; set; }
        public bool Success { get; set; }
    }

    private class CloudflareDnsRecord
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Content { get; set; }
        // other fields like type, zone_id, zone_name etc. can be added if needed
    }
}
