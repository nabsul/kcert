using System.Threading.Tasks;

namespace KCert.Services;

public interface IDnsProvider
{
    Task CreateTxtRecordAsync(string domainName, string recordName, string recordValue);
    Task DeleteTxtRecordAsync(string domainName, string recordName, string recordValue);
}
