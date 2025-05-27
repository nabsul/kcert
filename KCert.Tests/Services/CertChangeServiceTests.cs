using k8s.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging; // Explicitly for ILogger
using KCert.Models; // For CertRequest

namespace KCert.Tests.Services;

[TestClass]
public class CertChangeServiceTests
{
    private Mock<ILogger<CertChangeService>> _mockLogger = null!;
    private Mock<K8sClient> _mockK8sClient = null!;
    private Mock<KCertClient> _mockKcertClient = null!;
    private Mock<KCertConfig> _mockConfig = null!;
    private CertChangeService _certChangeService = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _mockLogger = new Mock<ILogger<CertChangeService>>();
        _mockK8sClient = new Mock<K8sClient>(null!, null!, null!, null!); // Basic mock, dependencies not vital for these tests
        
        // KCertClient's dependencies (can be basic if not deeply used by CertChangeService's tested logic directly)
        var mockKcertLogger = new Mock<ILogger<KCertClient>>();
        var mockRenewalHandler = new Mock<RenewalHandler>(null!, null!, null!, null!, null!, null!, null!); // Full mock not needed
        var mockEmailClient = new Mock<EmailClient>(null!, null!, null!);
        var mockCertClient = new Mock<CertClient>(null!); // Needs KCertConfig, but can be a basic mock if not used in RenewIfNeededAsync path for these tests

        _mockConfig = new Mock<KCertConfig>(Mock.Of<Microsoft.Extensions.Configuration.IConfiguration>());
        _mockConfig.Setup(c => c.WatchIngresses).Returns(true);
        _mockConfig.Setup(c => c.WatchConfigMaps).Returns(true);
        _mockConfig.Setup(c => c.NamespaceConstraints).Returns(Array.Empty<string>()); // No constraints by default


        _mockKcertClient = new Mock<KCertClient>(
            _mockK8sClient.Object,
            _mockConfig.Object, 
            mockRenewalHandler.Object,
            mockKcertLogger.Object,
            mockEmailClient.Object,
            mockCertClient.Object
        );

        _certChangeService = new CertChangeService(_mockLogger.Object, _mockK8sClient.Object, _mockKcertClient.Object, _mockConfig.Object);
    }

    // Helper method to access private GetIngressCertsAsync
    private IAsyncEnumerable<CertRequest> CallGetIngressCertsAsync()
    {
        var methodInfo = typeof(CertChangeService).GetMethod("GetIngressCertsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.IsNotNull(methodInfo, "GetIngressCertsAsync method not found.");
        return (IAsyncEnumerable<CertRequest>)methodInfo.Invoke(_certChangeService, null)!;
    }

    // Helper method to access private GetConfigMapCertsAsync
    private IAsyncEnumerable<CertRequest> CallGetConfigMapCertsAsync()
    {
        var methodInfo = typeof(CertChangeService).GetMethod("GetConfigMapCertsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.IsNotNull(methodInfo, "GetConfigMapCertsAsync method not found.");
        return (IAsyncEnumerable<CertRequest>)methodInfo.Invoke(_certChangeService, null)!;
    }


    [TestMethod]
    public async Task GetIngressCertsAsync_WildcardHost()
    {
        var ns = "test-namespace";
        var ingress = new V1Ingress
        {
            Metadata = new V1ObjectMeta { Name = "test-ingress", NamespaceProperty = ns, Labels = new Dictionary<string, string> { { "kcert.dev/ingress", "managed" } } },
            Spec = new V1IngressSpec
            {
                Tls = new List<V1IngressTLS>
                {
                    new V1IngressTLS { Hosts = new List<string> { "*.example.com", "example.com" }, SecretName = "test-secret" }
                }
            }
        };
        _mockK8sClient.Setup(k => k.GetAllIngressesAsync(It.IsAny<string[]>())).ReturnsAsync(new List<V1Ingress> { ingress });

        var results = new List<CertRequest>();
        await foreach (var certRequest in CallGetIngressCertsAsync())
        {
            results.Add(certRequest);
        }

        Assert.AreEqual(1, results.Count);
        var result = results[0];
        Assert.AreEqual(ns, result.Namespace);
        Assert.AreEqual("test-secret", result.SecretName);
        CollectionAssert.AreEquivalent(new List<string> { "*.example.com", "example.com" }, result.Hosts.ToList());
    }

    [TestMethod]
    public async Task GetConfigMapCertsAsync_WildcardHost()
    {
        var ns = "test-namespace-cm";
        var configMap = new V1ConfigMap
        {
            Metadata = new V1ObjectMeta { Name = "test-secretcm", NamespaceProperty = ns, Annotations = new Dictionary<string, string> { { "kcert.dev/configmap", "managed" } } },
            Data = new Dictionary<string, string> { { "hosts", "*.foo.com,foo.com" } }
        };
        _mockK8sClient.Setup(k => k.GetAllConfigMapsAsync(It.IsAny<string[]>())).ReturnsAsync(new List<V1ConfigMap> { configMap });

        var results = new List<CertRequest>();
        await foreach (var certRequest in CallGetConfigMapCertsAsync())
        {
            results.Add(certRequest);
        }

        Assert.AreEqual(1, results.Count);
        var result = results[0];
        Assert.AreEqual(ns, result.Namespace);
        Assert.AreEqual("test-secretcm", result.SecretName);
        CollectionAssert.AreEquivalent(new List<string> { "*.foo.com", "foo.com" }, result.Hosts.ToList());
    }

    [TestMethod]
    public async Task CheckForChangesAsync_AggregatesWildcardAndBase_CallsKCertClientCorrectly()
    {
        var ns = "wildcard-ns";
        var secretName = "wild-secret";
        var hosts = new List<string> { "*.example.com", "example.com" };

        var ingress = new V1Ingress
        {
            Metadata = new V1ObjectMeta { Name = "wild-ingress", NamespaceProperty = ns, Labels = new Dictionary<string, string> { { "kcert.dev/ingress", "managed" } } },
            Spec = new V1IngressSpec
            {
                Tls = new List<V1IngressTLS> { new V1IngressTLS { Hosts = hosts, SecretName = secretName } }
            }
        };
        _mockK8sClient.Setup(k => k.GetAllIngressesAsync(It.IsAny<string[]>())).ReturnsAsync(new List<V1Ingress> { ingress });
        _mockK8sClient.Setup(k => k.GetAllConfigMapsAsync(It.IsAny<string[]>())).ReturnsAsync(new List<V1ConfigMap>()); // Empty CMs

        _mockKcertClient.Setup(kc => kc.RenewIfNeededAsync(ns, secretName, It.Is<string[]>(h => h.SequenceEqual(hosts)), CancellationToken.None))
            .Returns(Task.CompletedTask)
            .Verifiable();

        await _certChangeService.CheckForChangesAsync(CancellationToken.None);

        _mockKcertClient.Verify(); // Verifies RenewIfNeededAsync was called with the correct parameters
    }
    
    [TestMethod]
    public async Task GetIngressCertsAsync_NoTls_YieldsNothing()
    {
        var ns = "test-namespace";
        var ingress = new V1Ingress
        {
            Metadata = new V1ObjectMeta { Name = "test-ingress", NamespaceProperty = ns, Labels = new Dictionary<string, string> { { "kcert.dev/ingress", "managed" } } },
            Spec = new V1IngressSpec { Tls = null } // No TLS spec
        };
        _mockK8sClient.Setup(k => k.GetAllIngressesAsync(It.IsAny<string[]>())).ReturnsAsync(new List<V1Ingress> { ingress });

        var results = new List<CertRequest>();
        await foreach (var certRequest in CallGetIngressCertsAsync())
        {
            results.Add(certRequest);
        }
        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public async Task GetConfigMapCertsAsync_NoHostsData_YieldsNothing()
    {
        var ns = "test-namespace-cm";
        var configMap = new V1ConfigMap
        {
            Metadata = new V1ObjectMeta { Name = "test-secretcm", NamespaceProperty = ns, Annotations = new Dictionary<string, string> { { "kcert.dev/configmap", "managed" } } },
            Data = null // No Data
        };
         var configMap2 = new V1ConfigMap
        {
            Metadata = new V1ObjectMeta { Name = "test-secretcm2", NamespaceProperty = ns, Annotations = new Dictionary<string, string> { { "kcert.dev/configmap", "managed" } } },
            Data = new Dictionary<string, string>() // Empty Data
        };
        _mockK8sClient.Setup(k => k.GetAllConfigMapsAsync(It.IsAny<string[]>())).ReturnsAsync(new List<V1ConfigMap> { configMap, configMap2 });

        var results = new List<CertRequest>();
        await foreach (var certRequest in CallGetConfigMapCertsAsync())
        {
            results.Add(certRequest);
        }
        Assert.AreEqual(0, results.Count);
    }
}
