using Amazon;
using Amazon.Route53;
using Amazon.Route53.Model;
using KCert.Models; // Assuming CertClient is in here for GenerateNewKey, or some other model
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace KCert.Services;

[Service]
public class AwsRoute53Provider : IDnsProvider
{
    private readonly KCertConfig _cfg;
    private readonly ILogger<AwsRoute53Provider> _log;
    private readonly AmazonRoute53Client? _route53Client;

    public AwsRoute53Provider(KCertConfig cfg, ILogger<AwsRoute53Provider> log)
    {
        _cfg = cfg;
        _log = log;

        if (_cfg.EnableRoute53)
        {
            if (string.IsNullOrWhiteSpace(_cfg.Route53AccessKeyId) ||
                string.IsNullOrWhiteSpace(_cfg.Route53SecretAccessKey) ||
                string.IsNullOrWhiteSpace(_cfg.Route53Region))
            {
                _log.LogError("AWS Route53 is enabled, but one or more required configuration fields (AccessKeyId, SecretAccessKey, Region) are missing.");
                _route53Client = null; // Ensure client is null if config is incomplete
                return;
            }
            
            try
            {
                var awsCredentials = new Amazon.Runtime.BasicAWSCredentials(_cfg.Route53AccessKeyId, _cfg.Route53SecretAccessKey);
                _route53Client = new AmazonRoute53Client(awsCredentials, RegionEndpoint.GetBySystemName(_cfg.Route53Region));
                _log.LogInformation("AWS Route53 Provider initialized.");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error initializing AWS Route53 client.");
                _route53Client = null;
            }
        }
        else
        {
            _log.LogInformation("AWS Route53 Provider is disabled.");
        }
    }

    private async Task<string?> GetHostedZoneIdAsync(string domainName)
    {
        if (_route53Client == null || !_cfg.EnableRoute53)
        {
            _log.LogWarning("Route53 client not available or provider disabled. Cannot get hosted zone ID.");
            return null;
        }

        try
        {
            var zonesResponse = await _route53Client.ListHostedZonesAsync();
            var bestMatch = zonesResponse.HostedZones
                .Where(z => domainName.EndsWith(z.Name.TrimEnd('.')))
                .OrderByDescending(z => z.Name.Length)
                .FirstOrDefault();

            if (bestMatch != null)
            {
                _log.LogInformation($"Found hosted zone ID {bestMatch.Id} for domain {domainName}");
                return bestMatch.Id;
            }

            _log.LogWarning($"No hosted zone found for domain {domainName}");
            return null;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, $"Error getting hosted zone ID for domain {domainName}");
            return null;
        }
    }

    public async Task CreateTxtRecordAsync(string domainName, string recordName, string recordValue)
    {
        if (_route53Client == null || !_cfg.EnableRoute53)
        {
            _log.LogWarning("Route53 client not available or provider disabled. Cannot create TXT record.");
            return;
        }

        var hostedZoneId = await GetHostedZoneIdAsync(domainName);
        if (string.IsNullOrEmpty(hostedZoneId))
        {
            _log.LogError($"Cannot create TXT record for {recordName}. Hosted zone ID not found for domain {domainName}.");
            return;
        }

        // TXT record values need to be enclosed in quotes.
        var properlyQuotedValue = $"\"{recordValue.Replace("\"", "\\\"")}\"";

        var change = new Change
        {
            Action = ChangeAction.UPSERT,
            ResourceRecordSet = new ResourceRecordSet
            {
                Name = recordName,
                Type = RRType.TXT,
                TTL = 60, // Low TTL for ACME challenges
                ResourceRecords = new List<ResourceRecord> { new ResourceRecord { Value = properlyQuotedValue } }
            }
        };

        var request = new ChangeResourceRecordSetsRequest
        {
            HostedZoneId = hostedZoneId,
            ChangeBatch = new ChangeBatch(new List<Change> { change })
        };

        try
        {
            _log.LogInformation($"Attempting to create/update TXT record: {recordName} with value: {properlyQuotedValue} in zone {hostedZoneId}");
            var response = await _route53Client.ChangeResourceRecordSetsAsync(request);
            _log.LogInformation($"Successfully sent request to create/update TXT record {recordName}. Status: {response.ChangeInfo.Status}, ID: {response.ChangeInfo.Id}");
            // Optionally, wait for the change to propagate if needed, though ACME validation usually handles this.
        }
        catch (Exception ex)
        {
            _log.LogError(ex, $"Error creating/updating TXT record {recordName} in zone {hostedZoneId}");
        }
    }

    public async Task DeleteTxtRecordAsync(string domainName, string recordName, string recordValue)
    {
        if (_route53Client == null || !_cfg.EnableRoute53)
        {
            _log.LogWarning("Route53 client not available or provider disabled. Cannot delete TXT record.");
            return;
        }

        var hostedZoneId = await GetHostedZoneIdAsync(domainName);
        if (string.IsNullOrEmpty(hostedZoneId))
        {
            _log.LogError($"Cannot delete TXT record for {recordName}. Hosted zone ID not found for domain {domainName}.");
            return;
        }

        var properlyQuotedValue = $"\"{recordValue.Replace("\"", "\\\"")}\"";

        var change = new Change
        {
            Action = ChangeAction.DELETE,
            ResourceRecordSet = new ResourceRecordSet
            {
                Name = recordName,
                Type = RRType.TXT,
                TTL = 60, 
                ResourceRecords = new List<ResourceRecord> { new ResourceRecord { Value = properlyQuotedValue } }
            }
        };

        var request = new ChangeResourceRecordSetsRequest
        {
            HostedZoneId = hostedZoneId,
            ChangeBatch = new ChangeBatch(new List<Change> { change })
        };

        try
        {
            _log.LogInformation($"Attempting to delete TXT record: {recordName} with value: {properlyQuotedValue} in zone {hostedZoneId}");
            var response = await _route53Client.ChangeResourceRecordSetsAsync(request);
            _log.LogInformation($"Successfully sent request to delete TXT record {recordName}. Status: {response.ChangeInfo.Status}, ID: {response.ChangeInfo.Id}");
        }
        catch (InvalidChangeBatchException ex)
        {
            // This can happen if the record doesn't exist or doesn't match.
            // Check if it's a "tried to delete resource record set that does not exist" error
            if (ex.Message.Contains("tried to delete resource record set") && ex.Message.Contains("but it was not found"))
            {
                _log.LogWarning($"Attempted to delete TXT record {recordName} but it was not found or already deleted. This may be normal.");
            }
            else
            {
                _log.LogError(ex, $"Error deleting TXT record {recordName} in zone {hostedZoneId}. InvalidChangeBatchException.");
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, $"Error deleting TXT record {recordName} in zone {hostedZoneId}");
        }
    }
}
