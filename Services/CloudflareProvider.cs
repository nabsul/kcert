using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent; // For zone ID cache

namespace KCert.Services;

[Service]
public class CloudflareProvider(KCertConfig cfg, ILogger<CloudflareProvider> log) : IDnsProvider
{
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

    private async Task<string?> GetZoneIdAsync(string domainName)
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

        try
        {
            log.LogDebug("Attempting to find zone ID for domain: {Domain}", registrableDomain);
            var response = await _httpClient.GetAsync($"zones?name={registrableDomain}");
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                log.LogError("Error fetching zone ID for {Domain}. Status: {StatusCode}. Body: {Body}", registrableDomain, response.StatusCode, errorBody);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var cfResponse = JsonSerializer.Deserialize<CloudflareZonesResponse>(content, _jsonOptions);

            if (cfResponse?.Result == null || !cfResponse.Result.Any())
            {
                log.LogWarning("No zone found for domain: {Domain}", registrableDomain);
                return null;
            }

            // Assuming the first result is the correct one if multiple are returned (shouldn't happen for exact name match)
            var zoneId = cfResponse.Result.First().Id ?? throw new InvalidOperationException($"No zone ID found for domain {registrableDomain} in Cloudflare response.");
            log.LogInformation("Found zone ID: {ZoneId} for domain: {Domain}", zoneId, registrableDomain);
            _zoneIdCache.TryAdd(registrableDomain, zoneId);
            return zoneId;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Exception fetching zone ID for {Domain}", registrableDomain);
            throw;
        }
    }

    public async Task CreateTxtRecordAsync(string domainName, string recordName, string recordValue)
    {
        var zoneId = await GetZoneIdAsync(domainName);
        if (string.IsNullOrEmpty(zoneId))
        {
            log.LogError("Cannot create TXT record for {RecordName}. Zone ID not found for domain {DomainName}.", recordName, domainName);
            return;
        }

        var payload = new CloudflareDnsRequest
        {
            Type = "TXT",
            Name = recordName, // Cloudflare expects the full record name
            Content = recordValue,
            Ttl = 120 // Common for ACME challenges, min is 60 or 120 depending on plan
        };

        var jsonPayload = JsonSerializer.Serialize(payload);
        var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        try
        {
            log.LogInformation("Attempting to create TXT record: {RecordName} with value: {RecordValue} in zone {ZoneId}", recordName, recordValue, zoneId);
            var response = await _httpClient.PostAsync($"zones/{zoneId}/dns_records", httpContent);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                log.LogInformation("Successfully created TXT record {RecordName}. Response: {ResponseBody}", recordName, responseBody);
            }
            else
            {
                log.LogError("Error creating TXT record {RecordName}. Status: {StatusCode}. Response: {ResponseBody}", recordName, response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Exception creating TXT record {RecordName} in zone {ZoneId}", recordName, zoneId);
        }
    }

    public async Task DeleteTxtRecordAsync(string domainName, string recordName, string recordValue)
    {
        var zoneId = await GetZoneIdAsync(domainName);
        if (string.IsNullOrEmpty(zoneId))
        {
            log.LogError("Cannot delete TXT record for {RecordName}. Zone ID not found for domain {DomainName}.", recordName, domainName);
            return;
        }

        // First, find the record ID
        string? recordId = null;
        try
        {
            // Note: Cloudflare API expects the 'name' to be the FQDN of the record.
            log.LogDebug("Attempting to find DNS record ID for Name: {RecordName}, Content: {RecordValue} in zone {ZoneId}", recordName, recordValue, zoneId);
            var listResponse = await _httpClient.GetAsync($"zones/{zoneId}/dns_records?type=TXT&name={recordName}&content={recordValue}");
            
            if (!listResponse.IsSuccessStatusCode)
            {
                var errorBody = await listResponse.Content.ReadAsStringAsync();
                log.LogError("Error listing DNS records to find ID for {RecordName}. Status: {StatusCode}. Body: {Body}", recordName, listResponse.StatusCode, errorBody);
                return;
            }

            var listContent = await listResponse.Content.ReadAsStringAsync();
            var cfListResponse = JsonSerializer.Deserialize<CloudflareDnsListResponse>(listContent, _jsonOptions);

            if (cfListResponse?.Result != null && cfListResponse.Result.Any())
            {
                recordId = cfListResponse.Result.First().Id; // Assuming first one is the match
                log.LogInformation("Found DNS record ID: {RecordId} for {RecordName}", recordId, recordName);
            }
            else
            {
                log.LogWarning("No TXT record found for Name: {RecordName} and Content: {RecordValue} in zone {ZoneId}. It might have been already deleted.", recordName, recordValue, zoneId);
                return; // Nothing to delete
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Exception finding DNS record ID for {RecordName}", recordName);
            return;
        }

        if (string.IsNullOrEmpty(recordId))
        {
            log.LogWarning("TXT record ID for {RecordName} not found, cannot delete.", recordName);
            return;
        }

        // Now delete the record
        try
        {
            log.LogInformation("Attempting to delete TXT record ID: {RecordId} ({RecordName}) in zone {ZoneId}", recordId, recordName, zoneId);
            var deleteResponse = await _httpClient.DeleteAsync($"zones/{zoneId}/dns_records/{recordId}");
            var deleteResponseBody = await deleteResponse.Content.ReadAsStringAsync();

            if (deleteResponse.IsSuccessStatusCode)
            {
                log.LogInformation("Successfully deleted TXT record ID: {RecordId}. Response: {ResponseBody}", recordId, deleteResponseBody);
            }
            else
            {
                log.LogError("Error deleting TXT record ID: {RecordId}. Status: {StatusCode}. Response: {ResponseBody}", recordId, deleteResponse.StatusCode, deleteResponseBody);
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Exception deleting TXT record ID: {RecordId}", recordId);
        }
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
