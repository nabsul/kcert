using KCert.Models; // For potential future use, not strictly needed for this impl
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Collections.Concurrent; // For zone ID cache

namespace KCert.Services;

[Service]
public class CloudflareProvider : IDnsProvider
{
    private readonly KCertConfig _cfg;
    private readonly ILogger<CloudflareProvider> _log;
    private readonly HttpClient _httpClient;
    private readonly string? _accountId;
    private static readonly ConcurrentDictionary<string, string> _zoneIdCache = new();

    public CloudflareProvider(KCertConfig cfg, ILogger<CloudflareProvider> log)
    {
        _cfg = cfg;
        _log = log;

        _httpClient = null!; // Null forgiving, will be set if config is valid

        if (_cfg.EnableCloudflare)
        {
            bool configError = false;
            if (string.IsNullOrWhiteSpace(_cfg.CloudflareApiToken))
            {
                _log.LogError("Cloudflare is enabled, but CloudflareApiToken is missing.");
                configError = true;
            }
            if (string.IsNullOrWhiteSpace(_cfg.CloudflareAccountId))
            {
                _log.LogError("Cloudflare is enabled, but CloudflareAccountId is missing.");
                configError = true;
            }

            if (configError)
            {
                // _httpClient remains null
                return;
            }

            _accountId = _cfg.CloudflareAccountId;
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://api.cloudflare.com/client/v4/")
            };
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _cfg.CloudflareApiToken);
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _log.LogInformation("Cloudflare Provider initialized.");
        }
        else
        {
            _log.LogInformation("Cloudflare Provider is disabled.");
            // _httpClient remains null
        }
    }

    private async Task<string?> GetZoneIdAsync(string domainName)
    {
        if (_httpClient == null)
        {
            _log.LogError($"Cloudflare client is not initialized due to configuration errors. Cannot get zone ID for {domainName}.");
            return null;
        }
        if (!_cfg.EnableCloudflare)
        {
            // This case should ideally be caught by _httpClient == null if constructor logic is sound
            _log.LogWarning($"Cloudflare provider is disabled. Cannot get zone ID for {domainName}.");
            return null;
        }

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
            _log.LogDebug($"Cache hit for zone ID: {cachedZoneId} for domain: {registrableDomain}");
            return cachedZoneId;
        }

        try
        {
            _log.LogDebug($"Attempting to find zone ID for domain: {registrableDomain}");
            var response = await _httpClient.GetAsync($"zones?name={registrableDomain}");
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _log.LogError($"Error fetching zone ID for {registrableDomain}. Status: {response.StatusCode}. Body: {errorBody}");
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var cfResponse = JsonSerializer.Deserialize<CloudflareZonesResponse>(content, options);

            if (cfResponse?.Result == null || !cfResponse.Result.Any())
            {
                _log.LogWarning($"No zone found for domain: {registrableDomain}");
                return null;
            }

            // Assuming the first result is the correct one if multiple are returned (shouldn't happen for exact name match)
            var zoneId = cfResponse.Result.First().Id;
            _log.LogInformation($"Found zone ID: {zoneId} for domain: {registrableDomain}");
            _zoneIdCache.TryAdd(registrableDomain, zoneId);
            return zoneId;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, $"Exception fetching zone ID for {registrableDomain}");
            return null;
        }
    }

    public async Task CreateTxtRecordAsync(string domainName, string recordName, string recordValue)
    {
        if (_httpClient == null)
        {
            _log.LogError($"Cloudflare client is not initialized due to configuration errors. Cannot create TXT record for {recordName}.");
            return;
        }
        if (!_cfg.EnableCloudflare)
        {
            // This case should ideally be caught by _httpClient == null
            _log.LogWarning($"Cloudflare provider is disabled. Cannot create TXT record for {recordName}.");
            return;
        }

        var zoneId = await GetZoneIdAsync(domainName);
        if (string.IsNullOrEmpty(zoneId))
        {
            _log.LogError($"Cannot create TXT record for {recordName}. Zone ID not found for domain {domainName}.");
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
            _log.LogInformation($"Attempting to create TXT record: {recordName} with value: {recordValue} in zone {zoneId}");
            var response = await _httpClient.PostAsync($"zones/{zoneId}/dns_records", httpContent);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                _log.LogInformation($"Successfully created TXT record {recordName}. Response: {responseBody}");
            }
            else
            {
                _log.LogError($"Error creating TXT record {recordName}. Status: {response.StatusCode}. Response: {responseBody}");
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, $"Exception creating TXT record {recordName} in zone {zoneId}");
        }
    }

    public async Task DeleteTxtRecordAsync(string domainName, string recordName, string recordValue)
    {
        if (_httpClient == null)
        {
            _log.LogError($"Cloudflare client is not initialized due to configuration errors. Cannot delete TXT record for {recordName}.");
            return;
        }
        if (!_cfg.EnableCloudflare)
        {
            // This case should ideally be caught by _httpClient == null
            _log.LogWarning($"Cloudflare provider is disabled. Cannot delete TXT record for {recordName}.");
            return;
        }

        var zoneId = await GetZoneIdAsync(domainName);
        if (string.IsNullOrEmpty(zoneId))
        {
            _log.LogError($"Cannot delete TXT record for {recordName}. Zone ID not found for domain {domainName}.");
            return;
        }

        // First, find the record ID
        string? recordId = null;
        try
        {
            // Note: Cloudflare API expects the 'name' to be the FQDN of the record.
            _log.LogDebug($"Attempting to find DNS record ID for Name: {recordName}, Content: {recordValue} in zone {zoneId}");
            var listResponse = await _httpClient.GetAsync($"zones/{zoneId}/dns_records?type=TXT&name={recordName}&content={recordValue}");
            
            if (!listResponse.IsSuccessStatusCode)
            {
                var errorBody = await listResponse.Content.ReadAsStringAsync();
                _log.LogError($"Error listing DNS records to find ID for {recordName}. Status: {listResponse.StatusCode}. Body: {errorBody}");
                return;
            }

            var listContent = await listResponse.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var cfListResponse = JsonSerializer.Deserialize<CloudflareDnsListResponse>(listContent, options);

            if (cfListResponse?.Result != null && cfListResponse.Result.Any())
            {
                recordId = cfListResponse.Result.First().Id; // Assuming first one is the match
                _log.LogInformation($"Found DNS record ID: {recordId} for {recordName}");
            }
            else
            {
                _log.LogWarning($"No TXT record found for Name: {recordName} and Content: {recordValue} in zone {zoneId}. It might have been already deleted.");
                return; // Nothing to delete
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, $"Exception finding DNS record ID for {recordName}");
            return;
        }

        if (string.IsNullOrEmpty(recordId))
        {
            _log.LogWarning($"TXT record ID for {recordName} not found, cannot delete.");
            return;
        }

        // Now delete the record
        try
        {
            _log.LogInformation($"Attempting to delete TXT record ID: {recordId} ({recordName}) in zone {zoneId}");
            var deleteResponse = await _httpClient.DeleteAsync($"zones/{zoneId}/dns_records/{recordId}");
            var deleteResponseBody = await deleteResponse.Content.ReadAsStringAsync();

            if (deleteResponse.IsSuccessStatusCode)
            {
                _log.LogInformation($"Successfully deleted TXT record ID: {recordId}. Response: {deleteResponseBody}");
            }
            else
            {
                _log.LogError($"Error deleting TXT record ID: {recordId}. Status: {deleteResponse.StatusCode}. Response: {deleteResponseBody}");
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, $"Exception deleting TXT record ID: {recordId}");
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
