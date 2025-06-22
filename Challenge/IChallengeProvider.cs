namespace KCert.Challenge;

public interface IChallengeProvider
{
    Task CreateTxtRecordAsync(string domainName, string recordName, string recordValue);
    Task DeleteTxtRecordAsync(string domainName, string recordName, string recordValue);
}
