namespace KCert.Tests.Services;

[TestClass]
public class KCertClientIngressTests
{
    private Mock<K8sClient> _mockK8sClient = null!;
    private Mock<RenewalHandler> _mockRenewalHandler = null!;
    private Mock<KCertConfig> _mockConfig = null!;
    private Mock<ILogger<KCertClient>> _mockLogger = null!;
    private Mock<EmailClient> _mockEmailClient = null!;
    private Mock<CertClient> _mockCertClient = null!;
    private KCertClient _kcertClient = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _mockK8sClient = new Mock<K8sClient>(null!, null!, null!, null!); // Dependencies not relevant for these tests
        _mockConfig = new Mock<KCertConfig>(Mock.Of<Microsoft.Extensions.Configuration.IConfiguration>());
        
        // RenewalHandler's dependencies (can be basic mocks if not deeply used by KCertClient's tested logic directly)
        var mockRenewalLogger = new Mock<ILogger<RenewalHandler>>();
        var mockAcmeClient = new Mock<AcmeClient>(Mock.Of<ILogger<AcmeClient>>(), Mock.Of<HttpClient>(), _mockConfig.Object);
        var mockAwsProvider = new Mock<AwsRoute53Provider>(Mock.Of<KCertConfig>(), Mock.Of<ILogger<AwsRoute53Provider>>());
        var mockCfProvider = new Mock<CloudflareProvider>(Mock.Of<KCertConfig>(), Mock.Of<ILogger<CloudflareProvider>>());
        
        _mockRenewalHandler = new Mock<RenewalHandler>(
            mockRenewalLogger.Object,
            mockAcmeClient.Object,
            _mockK8sClient.Object, // K8sClient might be used by RenewalHandler
            _mockConfig.Object,
            _mockCertClient?.Object ?? new Mock<CertClient>(_mockConfig.Object).Object, // CertClient used by RenewalHandler
            mockAwsProvider.Object,
            mockCfProvider.Object
        );

        _mockLogger = new Mock<ILogger<KCertClient>>();
        _mockEmailClient = new Mock<EmailClient>(null!, null!, null!); // Dependencies not relevant
        _mockCertClient = new Mock<CertClient>(_mockConfig.Object);

        _kcertClient = new KCertClient(
            _mockK8sClient.Object,
            _mockConfig.Object,
            _mockRenewalHandler.Object,
            _mockLogger.Object,
            _mockEmailClient.Object,
            _mockCertClient.Object
        );

        // Default KCertConfig values (can be overridden per test)
        _mockConfig.Setup(c => c.KCertNamespace).Returns("kcert-ns");
        _mockConfig.Setup(c => c.KCertIngressName).Returns("kcert-ingress");
        _mockConfig.Setup(c => c.ChallengeIngressMaxPropagationWaitTime).Returns(TimeSpan.FromMilliseconds(100)); // Short for tests
        _mockConfig.Setup(c => c.ChallengeIngressPropagationCheckInterval).Returns(TimeSpan.FromMilliseconds(10));
    }

    private async Task RunRenewCertAsync(string ns = "test-ns", string secretName = "test-secret", string[]? hosts = null)
    {
        hosts ??= new[] { "test.example.com" };
        // KCertClient.RenewCertAsync is private, but it's called by StartRenewalProcessAsync
        // We need to ensure the "previous task" logic is handled.
        // We can use a completed task as the "previous" for isolated testing.
        var prevTaskField = typeof(KCertClient).GetField("_running", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.IsNotNull(prevTaskField, "_running field not found.");
        prevTaskField.SetValue(_kcertClient, Task.CompletedTask);
        
        await _kcertClient.StartRenewalProcessAsync(ns, secretName, hosts, CancellationToken.None);
    }

    [DataTestMethod]
    [DataRow("dns-01", true, false, false, DisplayName = "DNS Preferred, Route53 Enabled, Ingress Not Managed")]
    [DataRow("dns-01", false, true, false, DisplayName = "DNS Preferred, Cloudflare Enabled, Ingress Not Managed")]
    [DataRow("http-01", false, false, true, DisplayName = "HTTP Preferred, Ingress Managed")]
    [DataRow("http-01", true, true, true, DisplayName = "HTTP Preferred, DNS Enabled, Ingress Still Managed")]
    [DataRow("dns-01", false, false, true, DisplayName = "DNS Preferred, No DNS Provider, Ingress Managed (Fallback)")]
    public async Task RenewCertAsync_ConditionalIngressManagement(
        string preferredChallengeType, bool enableRoute53, bool enableCloudflare, bool expectIngressManagement)
    {
        _mockConfig.Setup(c => c.PreferredChallengeType).Returns(preferredChallengeType);
        _mockConfig.Setup(c => c.EnableRoute53).Returns(enableRoute53);
        _mockConfig.Setup(c => c.EnableCloudflare).Returns(enableCloudflare);

        _mockRenewalHandler.Setup(rh => rh.RenewCertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string[]>()))
            .Returns(Task.CompletedTask);
        _mockEmailClient.Setup(ec => ec.NotifyRenewalResultAsync(It.IsAny<string>(), It.IsAny<string>(), null))
            .Returns(Task.CompletedTask);

        // Setup for AddChallengeHostsAsync if it's expected to be called
        if (expectIngressManagement)
        {
            _mockK8sClient.Setup(k => k.GetIngressAsync(_mockConfig.Object.KCertNamespace, _mockConfig.Object.KCertIngressName))
                .ReturnsAsync((k8s.Models.V1Ingress)null!); // Simulate ingress not existing initially
            _mockK8sClient.Setup(k => k.CreateIngressAsync(It.IsAny<k8s.Models.V1Ingress>()))
                .Returns(Task.CompletedTask);
            _mockK8sClient.Setup(k => k.DeleteIngressAsync(_mockConfig.Object.KCertNamespace, _mockConfig.Object.KCertIngressName))
                .Returns(Task.CompletedTask);
            
            // Mock for AwaitIngressPropagationAsync
            var mockIngress = new k8s.Models.V1Ingress { Metadata = new k8s.Models.V1ObjectMeta { Name = _mockConfig.Object.KCertIngressName, NamespaceProperty = _mockConfig.Object.KCertNamespace }};
             _mockK8sClient.Setup(k => k.GetIngressAsync(mockIngress.Namespace(), mockIngress.Name()))
                .ReturnsAsync(new k8s.Models.V1Ingress { Status = new k8s.Models.V1IngressStatus { LoadBalancer = new k8s.Models.V1LoadBalancerStatus { Ingress = new List<k8s.Models.V1LoadBalancerIngress>{ new k8s.Models.V1LoadBalancerIngress { Ip = "1.2.3.4" } } } } } });
        }

        await RunRenewCertAsync();

        _mockRenewalHandler.Verify(rh => rh.RenewCertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string[]>()), Times.Once);

        if (expectIngressManagement)
        {
            // AddChallengeHostsAsync calls Get, (maybe Delete), Create
            _mockK8sClient.Verify(k => k.GetIngressAsync(_mockConfig.Object.KCertNamespace, _mockConfig.Object.KCertIngressName), Times.AtLeastOnce); 
            _mockK8sClient.Verify(k => k.CreateIngressAsync(It.Is<k8s.Models.V1Ingress>(ing => ing.Metadata.Name == _mockConfig.Object.KCertIngressName)), Times.Once);
            _mockK8sClient.Verify(k => k.DeleteIngressAsync(_mockConfig.Object.KCertNamespace, _mockConfig.Object.KCertIngressName), Times.Once);
        }
        else
        {
            _mockK8sClient.Verify(k => k.GetIngressAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _mockK8sClient.Verify(k => k.CreateIngressAsync(It.IsAny<k8s.Models.V1Ingress>()), Times.Never);
            _mockK8sClient.Verify(k => k.DeleteIngressAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
             _mockLogger.Verify(log => log.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v,t) => v.ToString()!.Contains("Skipping HTTP challenge Ingress setup")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
            _mockLogger.Verify(log => log.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v,t) => v.ToString()!.Contains("Skipping deletion of HTTP challenge Ingress")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }
    }
}
