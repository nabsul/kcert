namespace KCert.Challenge;

public interface IChallengeProvider
{
    string AcmeChallengeType { get; }
    Task<object?> PrepareChallengeAsync(string[] hosts, CancellationToken tok);
    Task CleanupChallengeAsync(object? state, CancellationToken tok);
}
