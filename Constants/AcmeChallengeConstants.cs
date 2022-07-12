namespace KCert.Constants;

internal static class AcmeChallengeConstants
{
    internal const string AcmeChallengePath = ".well-known/acme-challenge/";
    
    internal const string AcmeChallengeTestPath = AcmeChallengePath + "test/";

    internal const int AcmeChallengeHostPort = 80;
}