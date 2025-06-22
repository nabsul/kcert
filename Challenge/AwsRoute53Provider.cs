using Amazon;
using Amazon.Route53;
using Amazon.Route53.Model;
using KCert.Services;

namespace KCert.Challenge;

[Service]
public class AwsRoute53Provider(KCertConfig cfg, ILogger<AwsRoute53Provider> log) : IChallengeProvider
{
    private static AmazonRoute53Client GetClient(string id, string key, string region) {
        var awsCredentials = new Amazon.Runtime.BasicAWSCredentials(id, key);
        return new AmazonRoute53Client(awsCredentials, RegionEndpoint.GetBySystemName(region));
    }

    private readonly AmazonRoute53Client _client = GetClient(cfg.Route53AccessKeyId, cfg.Route53SecretAccessKey, cfg.Route53Region);

    private async Task<string> GetHostedZoneIdAsync(string domainName)
    {
        var zonesResponse = await _client.ListHostedZonesAsync();

        try
        {
            return zonesResponse.HostedZones
                .Where(z => domainName.EndsWith(z.Name.TrimEnd('.')))
                .OrderByDescending(z => z.Name.Length)
                .First().Id;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"No hosted zone found for domain {domainName}. Ensure the domain is managed by Route53.", ex);
        }
    }

    public async Task CreateTxtRecordAsync(string domainName, string recordName, string recordValue)
    {
        var hostedZoneId = await GetHostedZoneIdAsync(domainName);

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
                ResourceRecords = [new ResourceRecord { Value = properlyQuotedValue }]
            }
        };

        var request = new ChangeResourceRecordSetsRequest
        {
            HostedZoneId = hostedZoneId,
            ChangeBatch = new ChangeBatch([change])
        };

        try
        {
            log.LogInformation("Attempting to create/update TXT record: {recordName} with value: {properlyQuotedValue} in zone {hostedZoneId}", recordName, properlyQuotedValue, hostedZoneId);
            var response = await _client.ChangeResourceRecordSetsAsync(request);
            log.LogInformation("Successfully sent request to create/update TXT record {recordName}. Status: {response.ChangeInfo.Status}, ID: {response.ChangeInfo.Id}", recordName, response.ChangeInfo.Status, response.ChangeInfo.Id);
            // Optionally, wait for the change to propagate if needed, though ACME validation usually handles this.
        }
        catch (Exception ex)
        {
            throw new Exception($"Error creating/updating TXT record {recordName} in zone {hostedZoneId}", ex);
        }
    }

    public async Task DeleteTxtRecordAsync(string domainName, string recordName, string recordValue)
    {
        var hostedZoneId = await GetHostedZoneIdAsync(domainName);
        if (string.IsNullOrEmpty(hostedZoneId))
        {
            log.LogError("Cannot delete TXT record for {recordName}. Hosted zone ID not found for domain {domainName}.", recordName, domainName);
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
                ResourceRecords = [new ResourceRecord { Value = properlyQuotedValue }]
            }
        };

        var request = new ChangeResourceRecordSetsRequest
        {
            HostedZoneId = hostedZoneId,
            ChangeBatch = new ChangeBatch([change])
        };

        try
        {
            log.LogInformation("Attempting to delete TXT record: {recordName} with value: {properlyQuotedValue} in zone {hostedZoneId}", recordName, properlyQuotedValue, hostedZoneId);
            var response = await _client.ChangeResourceRecordSetsAsync(request);
            log.LogInformation("Successfully sent request to delete TXT record {recordName}. Status: {response.ChangeInfo.Status}, ID: {response.ChangeInfo.Id}", recordName, response.ChangeInfo.Status, response.ChangeInfo.Id);
        }
        catch (InvalidChangeBatchException ex)
        {
            // This can happen if the record doesn't exist or doesn't match.
            // Check if it's a "tried to delete resource record set that does not exist" error
            if (ex.Message.Contains("tried to delete resource record set") && ex.Message.Contains("but it was not found"))
            {
                log.LogWarning("Attempted to delete TXT record {recordName} but it was not found or already deleted. This may be normal.", recordName);
            }
            else
            {
                log.LogError(ex, "Error deleting TXT record {recordName} in zone {hostedZoneId}. InvalidChangeBatchException.", recordName, hostedZoneId);
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Error deleting TXT record {recordName} in zone {hostedZoneId}", ex);
        }
    }
}
