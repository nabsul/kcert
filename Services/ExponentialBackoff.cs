namespace KCert.Services;

public class ExponentialBackoff(ILogger<ExponentialBackoff> log, KCertConfig cfg, EmailClient email)
{
    public async Task DoWithExponentialBackoffAsync(string actionName, Func<Task> action, CancellationToken tok)
    {
        int sleepOnFailure = cfg.InitialSleepOnFailure;
        while (true)
        {
            try
            {
                await action();
            }
            catch (TaskCanceledException ex)
            {
                log.LogError(ex, "{name} loop cancelled.", actionName);
                return;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "{name} failed", actionName);
                try
                {
                    await email.NotifyFailureAsync($"{actionName} failed unexpectedly", ex);
                }
                catch (Exception ex2)
                {
                    log.LogError(ex2, "{name} failed to send error notification", actionName);
                }
            }

            log.LogError("{name} failed. Sleeping for {n} seconds before trying again.", actionName, sleepOnFailure);
            await Task.Delay(TimeSpan.FromSeconds(sleepOnFailure), tok);
            sleepOnFailure *= 2;
        }
    }
}
