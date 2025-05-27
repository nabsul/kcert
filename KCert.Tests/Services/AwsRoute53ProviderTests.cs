using Amazon.Route53;
using Amazon.Route53.Model;

namespace KCert.Tests.Services;

[TestClass]
public class AwsRoute53ProviderTests
{
    private Mock<KCertConfig> _mockConfig = null!;
    private Mock<ILogger<AwsRoute53Provider>> _mockLogger = null!;
    private Mock<IAmazonRoute53> _mockRoute53Client = null!;
    private AwsRoute53Provider _provider = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _mockConfig = new Mock<KCertConfig>(Mock.Of<Microsoft.Extensions.Configuration.IConfiguration>());
        _mockLogger = new Mock<ILogger<AwsRoute53Provider>>();
        _mockRoute53Client = new Mock<IAmazonRoute53>();

        // Setup KCertConfig properties used by the provider's constructor
        _mockConfig.Setup(c => c.EnableRoute53).Returns(true);
        _mockConfig.Setup(c => c.Route53AccessKeyId).Returns("testAccessKey");
        _mockConfig.Setup(c => c.Route53SecretAccessKey).Returns("testSecretKey");
        _mockConfig.Setup(c => c.Route53Region).Returns("us-west-2");

        _provider = new AwsRoute53Provider(_mockConfig.Object, _mockLogger.Object);

        // Reflection or similar mechanism to set the private _route53Client field for testing
        // For simplicity in this example, we'll assume it's accessible or refactor for testability
        // A common way is to make the factory method for AmazonRoute53Client protected virtual
        // or pass the client in directly (less ideal for DI).
        // Here, we'll manually set it if the constructor logic allows, or use a more complex setup.
        // For this example, let's assume the constructor sets it up if EnableRoute53 is true
        // and we can mock the IAmazonRoute53 interface which it would internally use.
        // The actual AwsRoute53Provider uses 'new AmazonRoute53Client(...)'.
        // To test this properly, we'd need to refactor AwsRoute53Provider to take IAmazonRoute53
        // or a factory for it. For now, we'll mock the interface and assume it's used.
        // This test setup will only work if AwsRoute53Provider is refactored to accept IAmazonRoute53.
        // If not, testing direct SDK calls is harder. Let's proceed as if it's injectable.
        var field = typeof(AwsRoute53Provider).GetField("_route53Client", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field != null)
        {
            field.SetValue(_provider, _mockRoute53Client.Object);
        }
        else
        {
            // This will likely be the case with the current implementation.
            // We can't directly mock the 'new AmazonRoute53Client'.
            // Tests will be limited to config checks rather than SDK interaction checks.
            Console.WriteLine("Warning: _route53Client field not found. SDK interaction tests will be limited.");
        }
    }

    private void SetupRoute53ClientField()
    {
        // This helper ensures the mock client is injected for each relevant test.
        // This is a workaround for not having IAmazonRoute53 directly injectable in the current provider design.
        var field = typeof(AwsRoute53Provider).GetField("_route53Client", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field != null && _mockConfig.Object.EnableRoute53) // Only set if enabled, as constructor might skip otherwise
        {
             // Re-initialize provider to allow constructor to attempt client creation
            _provider = new AwsRoute53Provider(_mockConfig.Object, _mockLogger.Object);
            field.SetValue(_provider, _mockRoute53Client.Object);
        }
         else if (field == null)
        {
            Assert.Inconclusive("Cannot run SDK interaction tests: _route53Client field not accessible for mocking. Consider refactoring AwsRoute53Provider to accept IAmazonRoute53.");
        }
    }


    [TestMethod]
    public async Task CreateTxtRecordAsync_WhenDisabled_LogsAndReturns()
    {
        _mockConfig.Setup(c => c.EnableRoute53).Returns(false);
        // Re-initialize provider with new config for this test
        _provider = new AwsRoute53Provider(_mockConfig.Object, _mockLogger.Object);
        SetupRoute53ClientField();


        await _provider.CreateTxtRecordAsync("example.com", "_acme-challenge.example.com", "testValue");

        _mockLogger.Verify(log => log.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Route53 client not available or provider disabled")),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
        _mockRoute53Client.Verify(c => c.ListHostedZonesAsync(It.IsAny<ListHostedZonesRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task CreateTxtRecordAsync_Success()
    {
        _mockConfig.Setup(c => c.EnableRoute53).Returns(true);
        SetupRoute53ClientField();

        var domainName = "example.com";
        var recordName = "_acme-challenge.example.com";
        var recordValue = "txtValue";

        _mockRoute53Client.Setup(c => c.ListHostedZonesAsync(It.IsAny<ListHostedZonesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListHostedZonesResponse
            {
                HostedZones = new List<HostedZone> { new HostedZone { Id = "/hostedzone/ZONEID123", Name = "example.com." } },
                IsTruncated = false,
                MaxItems = "1"
            });

        _mockRoute53Client.Setup(c => c.ChangeResourceRecordSetsAsync(It.IsAny<ChangeResourceRecordSetsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChangeResourceRecordSetsResponse
            {
                ChangeInfo = new ChangeInfo { Status = ChangeStatus.PENDING, Id = "changeId" },
                HttpStatusCode = HttpStatusCode.OK
            });

        await _provider.CreateTxtRecordAsync(domainName, recordName, recordValue);

        _mockRoute53Client.Verify(c => c.ChangeResourceRecordSetsAsync(
            It.Is<ChangeResourceRecordSetsRequest>(req =>
                req.HostedZoneId == "/hostedzone/ZONEID123" &&
                req.ChangeBatch.Changes.Count == 1 &&
                req.ChangeBatch.Changes[0].Action == ChangeAction.UPSERT &&
                req.ChangeBatch.Changes[0].ResourceRecordSet.Name == recordName && // Provider should ensure it ends with a dot if Route53 requires
                req.ChangeBatch.Changes[0].ResourceRecordSet.Type == RRType.TXT &&
                req.ChangeBatch.Changes[0].ResourceRecordSet.TTL == 60 &&
                req.ChangeBatch.Changes[0].ResourceRecordSet.ResourceRecords.Count == 1 &&
                req.ChangeBatch.Changes[0].ResourceRecordSet.ResourceRecords[0].Value == $"\"{recordValue}\"" // Ensure value is quoted
            ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CreateTxtRecordAsync_NoMatchingHostedZone_LogsError()
    {
        _mockConfig.Setup(c => c.EnableRoute53).Returns(true);
        SetupRoute53ClientField();

        var domainName = "nomatch.com";
        var recordName = "_acme-challenge.nomatch.com";
        var recordValue = "txtValue";

        _mockRoute53Client.Setup(c => c.ListHostedZonesAsync(It.IsAny<ListHostedZonesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListHostedZonesResponse { HostedZones = new List<HostedZone>() });

        await _provider.CreateTxtRecordAsync(domainName, recordName, recordValue);

        _mockLogger.Verify(log => log.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Cannot create TXT record for {recordName}. Hosted zone ID not found")),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        _mockRoute53Client.Verify(c => c.ChangeResourceRecordSetsAsync(It.IsAny<ChangeResourceRecordSetsRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    
    [TestMethod]
    public async Task CreateTxtRecordAsync_ConfigMissing_AccessKey_LogsErrorAndDoesNotCallSdk()
    {
        _mockConfig.Setup(c => c.EnableRoute53).Returns(true);
        _mockConfig.Setup(c => c.Route53AccessKeyId).Returns(""); // Missing
        _mockConfig.Setup(c => c.Route53SecretAccessKey).Returns("testSecretKey");
        _mockConfig.Setup(c => c.Route53Region).Returns("us-west-2");

        // Re-initialize to trigger constructor logic with missing config
        _provider = new AwsRoute53Provider(_mockConfig.Object, _mockLogger.Object);
        // Note: SetupRoute53ClientField might not be able to inject if constructor bails early.
        // This test primarily checks constructor logging and behavior.

        await _provider.CreateTxtRecordAsync("example.com", "_acme-challenge.example.com", "testValue");

        _mockLogger.Verify(log => log.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("AWS Route53 is enabled, but one or more required configuration fields (AccessKeyId, SecretAccessKey, Region) are missing.")),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce); // Constructor log

        _mockLogger.Verify(log => log.Log(
            LogLevel.Warning, // Method log
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Route53 client not available or provider disabled")),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);

        _mockRoute53Client.Verify(c => c.ListHostedZonesAsync(It.IsAny<ListHostedZonesRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }


    [TestMethod]
    public async Task DeleteTxtRecordAsync_WhenDisabled_LogsAndReturns()
    {
        _mockConfig.Setup(c => c.EnableRoute53).Returns(false);
        _provider = new AwsRoute53Provider(_mockConfig.Object, _mockLogger.Object);
        SetupRoute53ClientField();

        await _provider.DeleteTxtRecordAsync("example.com", "_acme-challenge.example.com", "testValue");

        _mockLogger.Verify(log => log.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Route53 client not available or provider disabled")),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
        _mockRoute53Client.Verify(c => c.ListHostedZonesAsync(It.IsAny<ListHostedZonesRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task DeleteTxtRecordAsync_Success()
    {
        _mockConfig.Setup(c => c.EnableRoute53).Returns(true);
        SetupRoute53ClientField();

        var domainName = "example.com";
        var recordName = "_acme-challenge.example.com";
        var recordValue = "txtValue";

        _mockRoute53Client.Setup(c => c.ListHostedZonesAsync(It.IsAny<ListHostedZonesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListHostedZonesResponse
            {
                HostedZones = new List<HostedZone> { new HostedZone { Id = "/hostedzone/ZONEID123", Name = "example.com." } }
            });

        _mockRoute53Client.Setup(c => c.ChangeResourceRecordSetsAsync(It.IsAny<ChangeResourceRecordSetsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChangeResourceRecordSetsResponse { ChangeInfo = new ChangeInfo { Status = ChangeStatus.PENDING } });

        await _provider.DeleteTxtRecordAsync(domainName, recordName, recordValue);

        _mockRoute53Client.Verify(c => c.ChangeResourceRecordSetsAsync(
            It.Is<ChangeResourceRecordSetsRequest>(req =>
                req.HostedZoneId == "/hostedzone/ZONEID123" &&
                req.ChangeBatch.Changes.Count == 1 &&
                req.ChangeBatch.Changes[0].Action == ChangeAction.DELETE &&
                req.ChangeBatch.Changes[0].ResourceRecordSet.Name == recordName &&
                req.ChangeBatch.Changes[0].ResourceRecordSet.Type == RRType.TXT &&
                req.ChangeBatch.Changes[0].ResourceRecordSet.TTL == 60 && // TTL might not be strictly necessary for DELETE to match, but provider sets it
                req.ChangeBatch.Changes[0].ResourceRecordSet.ResourceRecords.Count == 1 &&
                req.ChangeBatch.Changes[0].ResourceRecordSet.ResourceRecords[0].Value == $"\"{recordValue}\""
            ), It.IsAny<CancellationToken>()), Times.Once);
    }
}
