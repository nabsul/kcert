using KCert;

namespace KCert.Services;

[Service]
public class ChallengeHandlerHttp : IChallengeHandler
{
    public string ChallengeIdentifier => "http-01";
}
