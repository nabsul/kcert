using System.Text.Json;
using KCert.Services;
using NSubstitute;

namespace KCert.Tests;

public class AcmeClientTests
{

    [Fact]
    public async Task FetchDirectoryAsync()
    {
        var tok = CancellationToken.None;
        var client = GetClient();
        var dir = await client.ReadDirectoryAsync(tok);
        Assert.NotNull(dir);
    }

    AcmeClient GetClient()
    {
        var cfg = Substitute.For<IKCertConfig>();
        cfg.AcmeDir.Returns(new Uri("https://localhost:14000/dir"));
        var cert = new CertClient(cfg);
        return new AcmeClient(cert, cfg);
    }
}
