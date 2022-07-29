using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using KCert.Constants;
using Microsoft.Extensions.Logging;

namespace KCert.Services;

[Service]
public class CallbackIngressService
{
    private const string TestRouteParameter = "test";
    
    private readonly KCertConfig _cfg;
    private readonly ILogger<CallbackIngressService> _log;

    public CallbackIngressService(KCertConfig cfg, ILogger<CallbackIngressService> log)
    {
        _cfg = cfg;
        _log = log;
    }
    
    public async Task AwaitChallengeIngressPropagatedAsync(IEnumerable<string> hosts)
    {
        foreach (var host in hosts)
        {
            var isHostReachable = await TryCallAcmeChallengeTestEndpointAsync(host);
            if (isHostReachable)
                _log.LogError(message: $"{host} could not be reached in time");
            else
                _log.LogInformation(message: $"{host} could be reached");
        }
    }

    private async Task<bool> TryCallAcmeChallengeTestEndpointAsync(string host)
    {
        for (var i = _cfg.PropagationNumRetries; i > default(int); i--)
        {
            try
            {
                await Task.Delay(delay: TimeSpan.FromSeconds(value: _cfg.PropagationWaitTimeSeconds));

                var acmeChallengeTestRequestUri = GetAcmeChallengeTestEndpointUri(host);

                using var httpClient = new HttpClient();
                var response = await httpClient.GetAsync(acmeChallengeTestRequestUri);

                if (response.IsSuccessStatusCode) return true;

                _log.LogInformation(
                    message:
                    "{AcmeChallengeTestRequestUri} could not be reached within {RetryCount} x {PropagationWaitTimeSeconds} seconds",
                    acmeChallengeTestRequestUri,
                    i,
                    _cfg.PropagationWaitTimeSeconds);
            }
            catch (Exception e)
            {
                _log.LogError(exception: e, message: $"{nameof(TryCallAcmeChallengeTestEndpointAsync)} faulted");
            }
        }
        return false;
    }

    private string GetAcmeChallengeTestEndpointUri(string host)
    {
        var requestUri = new UriBuilder(
            Uri.UriSchemeHttp,
            host,
            port: _cfg.KCertServicePort,
            pathValue: AcmeChallengeConstants.AcmeChallengeTestPath + TestRouteParameter).Uri.OriginalString;
        return requestUri;
    }
}