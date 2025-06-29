namespace KCert.Challenge;

using Amazon;
using Amazon.Route53;
using Amazon.Route53.Model;
using KCert.Config;
using KCert.Models;

public class AwsRoute53Provider(KCertConfig cfg, DnsUtils util, ILogger<AwsRoute53Provider> log) : IChallengeProvider
{
    public string AcmeChallengeType => "dns-01";
    private readonly AmazonRoute53Client _client = GetClient(cfg.Route53AccessKeyId, cfg.Route53SecretAccessKey, cfg.Route53Region);
    private record AwsRoute53State(string HostedZoneId, string Domain, string RecordName, string RecordValue);

    public async Task<object?> PrepareChallengesAsync(IEnumerable<AcmeAuthzResponse> auths, CancellationToken tok)
    {
        var res = new List<AwsRoute53State>();

        foreach (var auth in auths)
        {
            var domain = util.StripWildcard(auth.Identifier.Value);
            var recordName = util.GetTextRecordKey(domain);
            var recordValue = util.GetTextRecordValue(auth, domain);

            log.LogInformation("Preparing DNS challenge for domain {domain} with record {recordName} and value {recordValue}", domain, recordName, recordValue);
            var zoneId = await CreateTxtRecordAsync(domain, recordName, recordValue, tok);
            res.Add(new AwsRoute53State(zoneId, domain, recordName, recordValue));
        }

        return res;
    }

    public async Task CleanupChallengeAsync(object? state, CancellationToken tok)
    {
        if (state is not List<AwsRoute53State> states)
        {
            throw new ArgumentException("Invalid state provided for AWS Route53 challenge cleanup. Expected List<AwsRoute53State>.", nameof(state));
        }

        foreach (var s in states)
        {
            log.LogInformation("Cleaning up DNS challenge for domain {domain} with record {recordName} and value {recordValue}", s.Domain, s.RecordName, s.RecordValue);
            await DeleteTxtRecordAsync(s.HostedZoneId, s.RecordName, s.RecordValue, tok);
        }
    }

    private static AmazonRoute53Client GetClient(string id, string key, string region)
    {
        var awsCredentials = new Amazon.Runtime.BasicAWSCredentials(id, key);
        return new AmazonRoute53Client(awsCredentials, RegionEndpoint.GetBySystemName(region));
    }

    private async Task<string> GetHostedZoneIdAsync(string domainName, CancellationToken tok)
    {
        var zonesResponse = await _client.ListHostedZonesAsync(tok);

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

    public async Task<string> CreateTxtRecordAsync(string domainName, string recordName, string recordValue, CancellationToken tok)
    {
        var hostedZoneId = await GetHostedZoneIdAsync(domainName, tok);

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
            var response = await _client.ChangeResourceRecordSetsAsync(request, tok);
            log.LogInformation("Successfully sent request to create/update TXT record {recordName}. Status: {response.ChangeInfo.Status}, ID: {response.ChangeInfo.Id}", recordName, response.ChangeInfo.Status, response.ChangeInfo.Id);
            return hostedZoneId;
        }
        catch (Exception ex)
        {
            throw new Exception($"Error creating/updating TXT record {recordName} in zone {hostedZoneId}", ex);
        }
    }

    public async Task DeleteTxtRecordAsync(string hostedZoneId, string recordName, string recordValue, CancellationToken tok)
    {
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
            var response = await _client.ChangeResourceRecordSetsAsync(request, tok);
            log.LogInformation("Successfully sent request to delete TXT record {recordName}. Status: {response.ChangeInfo.Status}, ID: {response.ChangeInfo.Id}", recordName, response.ChangeInfo.Status, response.ChangeInfo.Id);
        }
        catch (Exception ex)
        {
            throw new Exception($"Error deleting TXT record {recordName} in zone {hostedZoneId}", ex);
        }
    }
}
