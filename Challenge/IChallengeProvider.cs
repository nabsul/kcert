using KCert.Models;

namespace KCert.Challenge;

public interface IChallengeProvider
{
    string AcmeChallengeType { get; }
    Task<object?> PrepareChallengesAsync(IEnumerable<AcmeAuthzResponse> auths, CancellationToken tok);
    Task CleanupChallengeAsync(object? state, CancellationToken tok);
}
