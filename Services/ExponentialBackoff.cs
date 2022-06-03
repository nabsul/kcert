using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace KCert.Services;

[Service]
public class ExponentialBackoff
{
    private readonly ILogger<ExponentialBackoff> _log;
    private readonly KCertConfig _cfg;
    private readonly EmailClient _email;

    public ExponentialBackoff(ILogger<ExponentialBackoff> log, KCertConfig cfg, EmailClient email)
    {
        _log = log;
        _cfg = cfg;
        _email = email;
    }

    public async Task DoWithExponentialBackoffAsync(string actionName, Func<Task> action, CancellationToken tok)
    {
        int sleepOnFailure = _cfg.InitialSleepOnFailure;
        while (true)
        {
            try
            {
                await action();
            }
            catch (TaskCanceledException ex)
            {
                _log.LogError(ex, "{name} loop cancelled.", actionName);
                return;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "{name} failed", actionName);
                try
                {
                    await _email.NotifyFailureAsync($"{actionName} failed unexpectedly", ex);
                }
                catch (Exception ex2)
                {
                    _log.LogError(ex2, "{name} failed to send error notification", actionName);
                }
            }

            _log.LogError("{name} failed. Sleeping for {n} seconds before trying again.", actionName, sleepOnFailure);
            await Task.Delay(TimeSpan.FromSeconds(sleepOnFailure), tok);
            sleepOnFailure *= 2;
        }
    }
}
