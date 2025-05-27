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
        _mockK8sClient = new Mock<K8sClient>(null!, null!, null!, null!);
        _mockConfig = new Mock<KCertConfig>(Mock.Of<Microsoft.Extensions.Configuration.IConfiguration>());
        
        var mockRenewalLogger = new Mock<ILogger<RenewalHandler>>();
        var mockAcmeClient = new Mock<AcmeClient>(Mock.Of<ILogger<AcmeClient>>(), Mock.Of<HttpClient>(), _mockConfig.Object);
        var mockAwsProvider = new Mock<AwsRoute53Provider>(Mock.Of<KCertConfig>(), Mock.Of<ILogger<AwsRoute53Provider>>());
        var mockCfProvider = new Mock<CloudflareProvider>(Mock.Of<KCertConfig>(), Mock.Of<ILogger<CloudflareProvider>>());
        
        _mockCertClient = new Mock<CertClient>(_mockConfig.Object);

        _mockRenewalHandler = new Mock<RenewalHandler>(
            mockRenewalLogger.Object,
            mockAcmeClient.Object,
            _mockK8sClient.Object,
            _mockConfig.Object,
            _mockCertClient.Object,
            mockAwsProvider.Object,
            mockCfProvider.Object
        );

        _mockLogger = new Mock<ILogger<KCertClient>>();
        _mockEmailClient = new Mock<EmailClient>(null!, null!, null!); 

        _kcertClient = new KCertClient(
            _mockK8sClient.Object,
            _mockConfig.Object,
            _mockRenewalHandler.Object,
            _mockLogger.Object,
            _mockEmailClient.Object,
            _mockCertClient.Object
        );

        _mockConfig.Setup(c => c.KCertNamespace).Returns("kcert-ns");
        _mockConfig.Setup(c => c.KCertIngressName).Returns("kcert-ingress");
        _mockConfig.Setup(c => c.ChallengeIngressMaxPropagationWaitTime).Returns(TimeSpan.FromMilliseconds(100)); 
        _mockConfig.Setup(c => c.ChallengeIngressPropagationCheckInterval).Returns(TimeSpan.FromMilliseconds(10));
    }

    private async Task RunRenewCertAsync(string ns = "test-ns", string secretName = "test-secret", string[]? hosts = null)
    {
        hosts ??= new[] { "test.example.com" };
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

        if (expectIngressManagement)
        {
            _mockK8sClient.Setup(k => k.GetIngressAsync(_mockConfig.Object.KCertNamespace, _mockConfig.Object.KCertIngressName))
                .ReturnsAsync((k8s.Models.V1Ingress)null!); 
            _mockK8sClient.Setup(k => k.CreateIngressAsync(It.IsAny<k8s.Models.V1Ingress>()))
                .Returns(Task.CompletedTask);
            _mockK8sClient.Setup(k => k.DeleteIngressAsync(_mockConfig.Object.KCertNamespace, _mockConfig.Object.KCertIngressName))
                .Returns(Task.CompletedTask);
            
            // Refactored section for mocking the GetIngressAsync call for propagation check
            var mockIngressMeta = new k8s.Models.V1ObjectMeta 
            { 
                Name = _mockConfig.Object.KCertIngressName, 
                NamespaceProperty = _mockConfig.Object.KCertNamespace 
            };
            var mockIngressForLambda = new k8s.Models.V1Ingress { Metadata = mockIngressMeta };

            var ingressIpList = new List<k8s.Models.V1LoadBalancerIngress> { new k8s.Models.V1LoadBalancerIngress { Ip = "1.2.3.4" } };
            var loadBalancerStatus = new k8s.Models.V1LoadBalancerStatus { Ingress = ingressIpList };
            var ingressStatus = new k8s.Models.V1IngressStatus { LoadBalancer = loadBalancerStatus };
            // Ensure the returned Ingress object also has metadata if any part of the code being tested might access it from the result of GetIngressAsync
            var returnedIngress = new k8s.Models.V1Ingress { Metadata = mockIngressMeta, Status = ingressStatus }; 

            _mockK8sClient.Setup(k => k.GetIngressAsync(mockIngressForLambda.Namespace(), mockIngressForLambda.Name()))
                .ReturnsAsync(returnedIngress);
        }

        await RunRenewCertAsync();

        _mockRenewalHandler.Verify(rh => rh.RenewCertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string[]>()), Times.Once);

        if (expectIngressManagement)
        {
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
                It.Is<object>(v => v != null && v.ToString()!.Contains("Skipping HTTP challenge Ingress setup")),
                null, 
                It.IsAny<Func<object, Exception?, string>>()), 
                Times.Once);
            _mockLogger.Verify(log => log.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<object>(v => v != null && v.ToString()!.Contains("Skipping deletion of HTTP challenge Ingress")),
                null, 
                It.IsAny<Func<object, Exception?, string>>()),
                Times.Once);
        }
    }
}
