namespace KCert.Tests.Services;

public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handlerFunc;

    public MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handlerFunc)
    {
        _handlerFunc = handlerFunc;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return _handlerFunc(request, cancellationToken);
    }
}

[TestClass]
public class CloudflareProviderTests
{
    private Mock<KCertConfig> _mockConfig = null!;
    private Mock<ILogger<CloudflareProvider>> _mockLogger = null!;
    private MockHttpMessageHandler _mockHttpHandler = null!;
    private HttpClient _httpClient = null!;
    private CloudflareProvider _provider = null!;

    private HttpResponseMessage DefaultZoneResponse(string domainName, string zoneId = "testZoneId123")
    {
        var zonesResponse = new { result = new[] { new { id = zoneId, name = domainName } }, success = true };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(zonesResponse), Encoding.UTF8, "application/json")
        };
    }
    
    private HttpResponseMessage EmptyZoneResponse()
    {
        var zonesResponse = new { result = Array.Empty<object>(), success = true };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(zonesResponse), Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage DnsRecordListResponse(string recordId, string recordName, string recordContent)
    {
        var dnsResponse = new { result = new[] { new { id = recordId, name = recordName, content = recordContent, type = "TXT" } }, success = true };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(dnsResponse), Encoding.UTF8, "application/json")
        };
    }

    private HttpResponseMessage EmptyDnsRecordListResponse()
    {
         var dnsResponse = new { result = Array.Empty<object>(), success = true };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(dnsResponse), Encoding.UTF8, "application/json")
        };
    }
    
    private HttpResponseMessage GenericSuccessResponse()
    {
        var successResponse = new { success = true };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(successResponse), Encoding.UTF8, "application/json")
        };
    }


    [TestInitialize]
    public void TestInitialize()
    {
        _mockConfig = new Mock<KCertConfig>(Mock.Of<Microsoft.Extensions.Configuration.IConfiguration>());
        _mockLogger = new Mock<ILogger<CloudflareProvider>>();

        _mockConfig.Setup(c => c.EnableCloudflare).Returns(true);
        _mockConfig.Setup(c => c.CloudflareApiToken).Returns("testApiToken");
        _mockConfig.Setup(c => c.CloudflareAccountId).Returns("testAccountId");
    }

    private void SetupProviderWithHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handlerFunc)
    {
        _mockHttpHandler = new MockHttpMessageHandler(handlerFunc);
        _httpClient = new HttpClient(_mockHttpHandler) { BaseAddress = new Uri("https://api.cloudflare.com/client/v4/") };
        
        _provider = new CloudflareProvider(_mockConfig.Object, _mockLogger.Object);

        // Use reflection to set the private _httpClient field
        var httpClientField = typeof(CloudflareProvider).GetField("_httpClient", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (httpClientField != null)
        {
            httpClientField.SetValue(_provider, _httpClient);
        }
        else
        {
            Assert.Inconclusive("Cannot run HttpClient interaction tests: _httpClient field not accessible for mocking. Consider refactoring CloudflareProvider.");
        }
    }


    [TestMethod]
    public async Task CreateTxtRecordAsync_WhenDisabled_LogsAndReturns()
    {
        _mockConfig.Setup(c => c.EnableCloudflare).Returns(false);
        var requestReceived = false;
        SetupProviderWithHandler((req, ct) => {
            requestReceived = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); // Should not be called
        });
        
        await _provider.CreateTxtRecordAsync("example.com", "_acme-challenge.example.com", "testValue");

        _mockLogger.Verify(log => log.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Cloudflare client not available or provider disabled")),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        Assert.IsFalse(requestReceived, "HttpClient should not have been called when Cloudflare is disabled.");
    }

    [TestMethod]
    public async Task CreateTxtRecordAsync_Success()
    {
        var domainName = "example.com";
        var recordName = "_acme-challenge.example.com";
        var recordValue = "txtValue";
        var zoneId = "zoneId123";
        HttpRequestMessage? sentRequest = null;

        SetupProviderWithHandler(async (req, ct) => {
            sentRequest = req;
            if (req.RequestUri!.PathAndQuery.Contains($"/zones?name={domainName}"))
            {
                return DefaultZoneResponse(domainName, zoneId);
            }
            if (req.Method == HttpMethod.Post && req.RequestUri!.PathAndQuery.Contains($"/zones/{zoneId}/dns_records"))
            {
                return GenericSuccessResponse();
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        
        await _provider.CreateTxtRecordAsync(domainName, recordName, recordValue);

        Assert.IsNotNull(sentRequest);
        Assert.AreEqual(HttpMethod.Post, sentRequest.Method);
        Assert.IsTrue(sentRequest.RequestUri!.ToString().Contains($"/zones/{zoneId}/dns_records"));
        Assert.AreEqual("Bearer testApiToken", sentRequest.Headers.Authorization?.ToString());
        
        var requestBody = await sentRequest.Content!.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(requestBody);
        Assert.AreEqual("TXT", jsonDoc.RootElement.GetProperty("type").GetString());
        Assert.AreEqual(recordName, jsonDoc.RootElement.GetProperty("name").GetString());
        Assert.AreEqual(recordValue, jsonDoc.RootElement.GetProperty("content").GetString());
        Assert.AreEqual(120, jsonDoc.RootElement.GetProperty("ttl").GetInt32()); // Default TTL
    }
    
    [TestMethod]
    public async Task GetZoneIdAsync_CachesResult()
    {
        var domainName = "example.com";
        var zoneId = "zoneIdForCacheTest";
        var callCount = 0;

        SetupProviderWithHandler((req, ct) => {
            if (req.RequestUri!.PathAndQuery.Contains($"/zones?name={domainName}"))
            {
                callCount++; // Increment on actual API call
                return Task.FromResult(DefaultZoneResponse(domainName, zoneId));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        // Access private method GetZoneIdAsync via reflection for direct testing
        var methodInfo = typeof(CloudflareProvider).GetMethod("GetZoneIdAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.IsNotNull(methodInfo, "GetZoneIdAsync method not found.");

        var result1 = await (Task<string?>)methodInfo.Invoke(_provider, new object[] { domainName })!;
        var result2 = await (Task<string?>)methodInfo.Invoke(_provider, new object[] { domainName })!;

        Assert.AreEqual(zoneId, result1);
        Assert.AreEqual(zoneId, result2);
        Assert.AreEqual(1, callCount, "API should only be called once due to caching.");
    }


    [TestMethod]
    public async Task DeleteTxtRecordAsync_Success()
    {
        var domainName = "example.com";
        var recordName = "_acme-challenge.delete.example.com";
        var recordValue = "deleteValue";
        var zoneId = "zoneForDelete";
        var recordId = "recordIdToDelete";
        var deleteRequestMade = false;

        SetupProviderWithHandler(async (req, ct) => {
            if (req.RequestUri!.PathAndQuery.Contains($"/zones?name={domainName}"))
            {
                return DefaultZoneResponse(domainName, zoneId);
            }
            // Match listing records for deletion
            if (req.Method == HttpMethod.Get && req.RequestUri!.PathAndQuery.Contains($"/zones/{zoneId}/dns_records") &&
                req.RequestUri!.Query.Contains($"type=TXT&name={recordName}&content={recordValue}"))
            {
                return DnsRecordListResponse(recordId, recordName, recordValue);
            }
            if (req.Method == HttpMethod.Delete && req.RequestUri!.PathAndQuery.Contains($"/zones/{zoneId}/dns_records/{recordId}"))
            {
                deleteRequestMade = true;
                return GenericSuccessResponse();
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        await _provider.DeleteTxtRecordAsync(domainName, recordName, recordValue);
        Assert.IsTrue(deleteRequestMade, "DELETE request to Cloudflare API was not made.");
    }

    [TestMethod]
    public async Task DeleteTxtRecordAsync_RecordNotFound_DoesNotAttemptDelete()
    {
        var domainName = "example.com";
        var recordName = "_acme-challenge.notfound.example.com";
        var recordValue = "notFoundValue";
        var zoneId = "zoneForNotFound";
        var deleteRequestMade = false;

        SetupProviderWithHandler(async (req, ct) => {
            if (req.RequestUri!.PathAndQuery.Contains($"/zones?name={domainName}"))
            {
                return DefaultZoneResponse(domainName, zoneId);
            }
            if (req.Method == HttpMethod.Get && req.RequestUri!.PathAndQuery.Contains($"/zones/{zoneId}/dns_records"))
            {
                 // Return empty list, record not found
                return EmptyDnsRecordListResponse();
            }
            if (req.Method == HttpMethod.Delete) // This should not be reached
            {
                deleteRequestMade = true; 
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        await _provider.DeleteTxtRecordAsync(domainName, recordName, recordValue);
        
        Assert.IsFalse(deleteRequestMade, "DELETE request should not have been made if record was not found by list.");
         _mockLogger.Verify(log => log.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"No TXT record found for Name: {recordName}")),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }
}
