using KCert;

namespace KCert.Services;

[Service]
public class ChallengeHandlerDns : IChallengeHandler
{
    public string ChallengeIdentifier => "dns-01";
}
