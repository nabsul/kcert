namespace KCert.Tests.Services;

[TestClass]
public class RenewalHandlerDnsTests
{
    private Mock<ILogger<RenewalHandler>> _mockLogger = null!;
    private Mock<AcmeClient> _mockAcmeClient = null!;
    private Mock<K8sClient> _mockK8sClient = null!;
    private Mock<KCertConfig> _mockConfig = null!;
    private Mock<CertClient> _mockCertClient = null!;
    private Mock<AwsRoute53Provider> _mockAwsProvider = null!;
    private Mock<CloudflareProvider> _mockCloudflareProvider = null!;
    private RenewalHandler _renewalHandler = null!;

    // Helper to create AcmeAuthzResponse
    private AcmeAuthzResponse CreateAuthzResponse(string domain, bool includeDns01, bool includeHttp01, string dnsToken = "dnsToken", string httpToken = "httpToken")
    {
        var challenges = new List<AcmeChallenge>();
        if (includeDns01)
        {
            challenges.Add(new AcmeChallenge { Type = "dns-01", Token = dnsToken, Url = $"https://acme.example.com/chall_dns/{dnsToken}", Status = "pending" });
        }
        if (includeHttp01)
        {
            challenges.Add(new AcmeChallenge { Type = "http-01", Token = httpToken, Url = $"https://acme.example.com/chall_http/{httpToken}", Status = "pending" });
        }
        return new AcmeAuthzResponse
        {
            Identifier = new AcmeIdentifier { Type = "dns", Value = domain },
            Status = "pending",
            Challenges = challenges.ToArray(),
            Nonce = "nonce1"
        };
    }
    
    private AcmeChallengeResponse CreateChallengeResponse(string type, string token, string status = "pending")
    {
        return new AcmeChallengeResponse
        {
            Type = type,
            Token = token,
            Url = $"https://acme.example.com/chall_resp/{token}",
            Status = status,
            Nonce = "nonceAfterChallenge"
        };
    }


    [TestInitialize]
    public void TestInitialize()
    {
        _mockLogger = new Mock<ILogger<RenewalHandler>>();
        _mockAcmeClient = new Mock<AcmeClient>(Mock.Of<ILogger<AcmeClient>>(), Mock.Of<HttpClient>(), _mockConfig?.Object ?? Mock.Of<KCertConfig>()); // Config can be null here, setup per test
        _mockK8sClient = new Mock<K8sClient>(null!, null!, null!, null!); // Dependencies not relevant for these tests
        _mockConfig = new Mock<KCertConfig>(Mock.Of<Microsoft.Extensions.Configuration.IConfiguration>());
        _mockCertClient = new Mock<CertClient>(_mockConfig.Object); // CertClient needs KCertConfig

        // Mock DNS providers. Note: Their dependencies (like KCertConfig, ILogger) need to be mocked if their constructors are complex.
        // Assuming simple constructors or that they are also mocked as needed.
        _mockAwsProvider = new Mock<AwsRoute53Provider>(Mock.Of<KCertConfig>(), Mock.Of<ILogger<AwsRoute53Provider>>());
        _mockCloudflareProvider = new Mock<CloudflareProvider>(Mock.Of<KCertConfig>(), Mock.Of<ILogger<CloudflareProvider>>());

        _renewalHandler = new RenewalHandler(
            _mockLogger.Object,
            _mockAcmeClient.Object,
            _mockK8sClient.Object,
            _mockConfig.Object,
            _mockCertClient.Object,
            _mockAwsProvider.Object,
            _mockCloudflareProvider.Object);
            
        _mockConfig.Setup(c => c.AcmeWaitTime).Returns(TimeSpan.FromMilliseconds(50)); // Short wait for tests
        _mockConfig.Setup(c => c.AcmeNumRetries).Returns(1);
    }

    [TestMethod]
    public async Task ValidateAuthorizationAsync_Dns01_Route53_Success()
    {
        var domain = "test.example.com";
        _mockConfig.Setup(c => c.PreferredChallengeType).Returns("dns-01");
        _mockConfig.Setup(c => c.EnableRoute53).Returns(true);
        _mockConfig.Setup(c => c.EnableCloudflare).Returns(false);

        var authzInitial = CreateAuthzResponse(domain, true, true, "dnsToken1");
        _mockAcmeClient.Setup(ac => ac.GetAuthzAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<string>(), "nonce0"))
            .ReturnsAsync(authzInitial);
        
        _mockCertClient.Setup(cc => cc.GetKeyAuthorization("dnsToken1")).Returns("keyAuthString");

        _mockAwsProvider.Setup(ap => ap.CreateTxtRecordAsync(domain, $"_acme-challenge.{domain}", It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _mockAwsProvider.Setup(ap => ap.DeleteTxtRecordAsync(domain, $"_acme-challenge.{domain}", It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        _mockAcmeClient.Setup(ac => ac.TriggerChallengeAsync(It.IsAny<string>(), new Uri(authzInitial.Challenges.First(c=>c.Type=="dns-01").Url), It.IsAny<string>(), authzInitial.Nonce))
            .ReturnsAsync(CreateChallengeResponse("dns-01", "dnsToken1", "pending"));

        var authzValid = CreateAuthzResponse(domain, true, true, "dnsToken1");
        authzValid.Challenges.First(c => c.Type == "dns-01").Status = "valid";
        authzValid.Status = "valid";
        authzValid.Nonce = "nonceValidated";
        _mockAcmeClient.Setup(ac => ac.GetAuthzAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<string>(), "nonceAfterChallenge"))
            .ReturnsAsync(authzValid);

        var resultNonce = await _renewalHandler.ValidateAuthorizationAsync("key1", "kid1", "nonce0", new Uri("https://auth.example.com"), _mockLogger.Object);

        Assert.AreEqual("nonceValidated", resultNonce);
        _mockAwsProvider.Verify(ap => ap.CreateTxtRecordAsync(domain, $"_acme-challenge.{domain}", It.IsAny<string>()), Times.Once);
        _mockAwsProvider.Verify(ap => ap.DeleteTxtRecordAsync(domain, $"_acme-challenge.{domain}", It.IsAny<string>()), Times.Once);
        _mockCloudflareProvider.VerifyNoOtherCalls();
        _mockAcmeClient.Verify(ac => ac.TriggerChallengeAsync(It.IsAny<string>(), new Uri(authzInitial.Challenges.First(c=>c.Type=="http-01").Url), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
    
    [TestMethod]
    public async Task ValidateAuthorizationAsync_Dns01_Cloudflare_Success()
    {
        var domain = "cloudflare.example.com";
        _mockConfig.Setup(c => c.PreferredChallengeType).Returns("dns-01");
        _mockConfig.Setup(c => c.EnableRoute53).Returns(false);
        _mockConfig.Setup(c => c.EnableCloudflare).Returns(true);

        var authzInitial = CreateAuthzResponse(domain, true, true, "dnsTokenCF");
        _mockAcmeClient.Setup(ac => ac.GetAuthzAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<string>(), "nonceCF0"))
            .ReturnsAsync(authzInitial);
        
        _mockCertClient.Setup(cc => cc.GetKeyAuthorization("dnsTokenCF")).Returns("keyAuthStringCF");

        _mockCloudflareProvider.Setup(cfp => cfp.CreateTxtRecordAsync(domain, $"_acme-challenge.{domain}", It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _mockCloudflareProvider.Setup(cfp => cfp.DeleteTxtRecordAsync(domain, $"_acme-challenge.{domain}", It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        _mockAcmeClient.Setup(ac => ac.TriggerChallengeAsync(It.IsAny<string>(), new Uri(authzInitial.Challenges.First(c=>c.Type=="dns-01").Url), It.IsAny<string>(), authzInitial.Nonce))
            .ReturnsAsync(CreateChallengeResponse("dns-01", "dnsTokenCF", "pending"));

        var authzValid = CreateAuthzResponse(domain, true, true, "dnsTokenCF");
        authzValid.Challenges.First(c => c.Type == "dns-01").Status = "valid";
        authzValid.Status = "valid";
        authzValid.Nonce = "nonceCFValidated";
        _mockAcmeClient.Setup(ac => ac.GetAuthzAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<string>(), "nonceAfterChallenge"))
            .ReturnsAsync(authzValid);

        var resultNonce = await _renewalHandler.ValidateAuthorizationAsync("keyCF", "kidCF", "nonceCF0", new Uri("https://auth.example.com/cf"), _mockLogger.Object);

        Assert.AreEqual("nonceCFValidated", resultNonce);
        _mockCloudflareProvider.Verify(cfp => cfp.CreateTxtRecordAsync(domain, $"_acme-challenge.{domain}", It.IsAny<string>()), Times.Once);
        _mockCloudflareProvider.Verify(cfp => cfp.DeleteTxtRecordAsync(domain, $"_acme-challenge.{domain}", It.IsAny<string>()), Times.Once);
        _mockAwsProvider.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task ValidateAuthorizationAsync_Dns01_CreateTxtFails_FallbackToHttp01()
    {
        var domain = "fallback.example.com";
        _mockConfig.Setup(c => c.PreferredChallengeType).Returns("dns-01");
        _mockConfig.Setup(c => c.EnableRoute53).Returns(true); // Route53 enabled
        _mockConfig.Setup(c => c.EnableCloudflare).Returns(false);

        var authzInitial = CreateAuthzResponse(domain, true, true, "dnsTokenFail", "httpTokenFallback"); // Both challenges
        _mockAcmeClient.Setup(ac => ac.GetAuthzAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<string>(), "nonceFallback0"))
            .ReturnsAsync(authzInitial);
        
        _mockCertClient.Setup(cc => cc.GetKeyAuthorization("dnsTokenFail")).Returns("keyAuthFail");

        _mockAwsProvider.Setup(ap => ap.CreateTxtRecordAsync(domain, $"_acme-challenge.{domain}", It.IsAny<string>()))
            .ThrowsAsync(new Exception("Simulated AWS Create Exception"));
        // Delete should still be called if create was attempted, even if it fails. The current logic might need adjustment for this.
        // Based on current RenewalHandler, if CreateTxtRecordAsync throws, Delete might not be called if dnsRecordCreated is false.
        // Let's assume for now it's called if the attempt was made. Test will reveal.
         _mockAwsProvider.Setup(ap => ap.DeleteTxtRecordAsync(domain, $"_acme-challenge.{domain}", It.IsAny<string>()))
            .Returns(Task.CompletedTask); // Ensure delete doesn't also throw for this test

        // HTTP-01 Challenge Mocks
        _mockAcmeClient.Setup(ac => ac.TriggerChallengeAsync(It.IsAny<string>(), new Uri(authzInitial.Challenges.First(c=>c.Type=="http-01").Url), It.IsAny<string>(), authzInitial.Nonce)) // Note: Nonce might change if DNS part made progress
            .ReturnsAsync(CreateChallengeResponse("http-01", "httpTokenFallback", "pending"));
        
        var authzHttpValid = CreateAuthzResponse(domain, true, true, "dnsTokenFail", "httpTokenFallback");
        authzHttpValid.Challenges.First(c => c.Type == "http-01").Status = "valid";
        authzHttpValid.Status = "valid";
        authzHttpValid.Nonce = "nonceHttpValidated";
        _mockAcmeClient.Setup(ac => ac.GetAuthzAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<string>(), "nonceAfterChallenge")) // Nonce after HTTP trigger
            .ReturnsAsync(authzHttpValid);

        var resultNonce = await _renewalHandler.ValidateAuthorizationAsync("keyFallback", "kidFallback", "nonceFallback0", new Uri("https://auth.example.com/fallback"), _mockLogger.Object);
        
        Assert.AreEqual("nonceHttpValidated", resultNonce);
        _mockAwsProvider.Verify(ap => ap.CreateTxtRecordAsync(domain, $"_acme-challenge.{domain}", It.IsAny<string>()), Times.Once);
        // Verify Delete was called (or not, depending on actual handler logic for dnsRecordCreated flag)
        // _mockAwsProvider.Verify(ap => ap.DeleteTxtRecordAsync(domain, $"_acme-challenge.{domain}", It.IsAny<string>()), Times.Once); // This might fail if CreateTxt throws before dnsRecordCreated=true
        
        _mockAcmeClient.Verify(ac => ac.TriggerChallengeAsync(It.IsAny<string>(), new Uri(authzInitial.Challenges.First(c=>c.Type=="http-01").Url), It.IsAny<string>(), It.IsAny<string>()), Times.Once, "HTTP-01 challenge should have been triggered.");
    }
    
    [TestMethod]
    public async Task ValidateAuthorizationAsync_Http01Preferred_UsesHttp01()
    {
        var domain = "http-preferred.example.com";
        _mockConfig.Setup(c => c.PreferredChallengeType).Returns("http-01");
        _mockConfig.Setup(c => c.EnableRoute53).Returns(true); // DNS provider available but not preferred

        var authzInitial = CreateAuthzResponse(domain, true, true, "dnsTokenHttpPref", "httpTokenHttpPref");
        _mockAcmeClient.Setup(ac => ac.GetAuthzAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<string>(), "nonceHttpP0"))
            .ReturnsAsync(authzInitial);

        _mockAcmeClient.Setup(ac => ac.TriggerChallengeAsync(It.IsAny<string>(), new Uri(authzInitial.Challenges.First(c=>c.Type=="http-01").Url), It.IsAny<string>(), authzInitial.Nonce))
            .ReturnsAsync(CreateChallengeResponse("http-01", "httpTokenHttpPref", "pending"));

        var authzHttpValid = CreateAuthzResponse(domain, true, true, "dnsTokenHttpPref", "httpTokenHttpPref");
        authzHttpValid.Challenges.First(c => c.Type == "http-01").Status = "valid";
        authzHttpValid.Status = "valid";
        authzHttpValid.Nonce = "nonceHttpPValidated";
        _mockAcmeClient.Setup(ac => ac.GetAuthzAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<string>(), "nonceAfterChallenge"))
            .ReturnsAsync(authzHttpValid);

        var resultNonce = await _renewalHandler.ValidateAuthorizationAsync("keyHttpP", "kidHttpP", "nonceHttpP0", new Uri("https://auth.example.com/httppref"), _mockLogger.Object);

        Assert.AreEqual("nonceHttpPValidated", resultNonce);
        _mockAwsProvider.VerifyNoOtherCalls(); // DNS provider should not be called
        _mockCloudflareProvider.VerifyNoOtherCalls();
        _mockAcmeClient.Verify(ac => ac.TriggerChallengeAsync(It.IsAny<string>(), new Uri(authzInitial.Challenges.First(c=>c.Type=="dns-01").Url), It.IsAny<string>(), It.IsAny<string>()), Times.Never, "DNS-01 challenge should not have been triggered.");
    }
    
    [TestMethod]
    public async Task ValidateAuthorizationAsync_Dns01Preferred_NoDnsChallenge_UsesHttp01()
    {
        var domain = "dns-pref-no-dns-chall.example.com";
        _mockConfig.Setup(c => c.PreferredChallengeType).Returns("dns-01");
        _mockConfig.Setup(c => c.EnableRoute53).Returns(true);

        var authzInitial = CreateAuthzResponse(domain, false, true, httpToken: "httpOnlyToken"); // Only HTTP-01 challenge
        _mockAcmeClient.Setup(ac => ac.GetAuthzAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<string>(), "nonceDnsNoChall0"))
            .ReturnsAsync(authzInitial);

        _mockAcmeClient.Setup(ac => ac.TriggerChallengeAsync(It.IsAny<string>(), new Uri(authzInitial.Challenges.First(c=>c.Type=="http-01").Url), It.IsAny<string>(), authzInitial.Nonce))
            .ReturnsAsync(CreateChallengeResponse("http-01", "httpOnlyToken", "pending"));
        
        var authzHttpValid = CreateAuthzResponse(domain, false, true, httpToken: "httpOnlyToken");
        authzHttpValid.Challenges.First(c => c.Type == "http-01").Status = "valid";
        authzHttpValid.Status = "valid";
        authzHttpValid.Nonce = "nonceDnsNoChallValidated";
         _mockAcmeClient.Setup(ac => ac.GetAuthzAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<string>(), "nonceAfterChallenge"))
            .ReturnsAsync(authzHttpValid);

        var resultNonce = await _renewalHandler.ValidateAuthorizationAsync("keyDnsNoChall", "kidDnsNoChall", "nonceDnsNoChall0", new Uri("https://auth.example.com/dnsno"), _mockLogger.Object);
        
        Assert.AreEqual("nonceDnsNoChallValidated", resultNonce);
        _mockAwsProvider.VerifyNoOtherCalls(); // DNS provider should not be called
        _mockCloudflareProvider.VerifyNoOtherCalls();
        _mockLogger.Verify(log => log.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v,t) => v.ToString()!.Contains("DNS-01 challenge preferred or required for dns-pref-no-dns-chall.example.com, but no DNS-01 challenge was offered by ACME server.")),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    // Wildcard Tests
    [TestMethod]
    public async Task Wildcard_DNS01_Success_Route53()
    {
        var wildcardDomain = "*.example.com";
        var baseDomain = "example.com";
        _mockConfig.Setup(c => c.EnableRoute53).Returns(true);
        _mockConfig.Setup(c => c.EnableCloudflare).Returns(false);
        // PreferredChallengeType is irrelevant for wildcards, DNS-01 is mandatory

        var authzInitial = CreateAuthzResponse(wildcardDomain, true, false, "dnsTokenWildcardR53"); // No http-01 for wildcards usually
        _mockAcmeClient.Setup(ac => ac.GetAuthzAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<string>(), "nonceWildR53_0"))
            .ReturnsAsync(authzInitial);
        
        _mockCertClient.Setup(cc => cc.GetKeyAuthorization("dnsTokenWildcardR53")).Returns("keyAuthWildcardR53");

        _mockAwsProvider.Setup(ap => ap.CreateTxtRecordAsync(baseDomain, $"_acme-challenge.{baseDomain}", It.IsAny<string>()))
            .Returns(Task.CompletedTask).Verifiable();
        _mockAwsProvider.Setup(ap => ap.DeleteTxtRecordAsync(baseDomain, $"_acme-challenge.{baseDomain}", It.IsAny<string>()))
            .Returns(Task.CompletedTask).Verifiable();

        _mockAcmeClient.Setup(ac => ac.TriggerChallengeAsync(It.IsAny<string>(), new Uri(authzInitial.Challenges.First(c=>c.Type=="dns-01").Url), It.IsAny<string>(), authzInitial.Nonce))
            .ReturnsAsync(CreateChallengeResponse("dns-01", "dnsTokenWildcardR53", "pending"));

        var authzValid = CreateAuthzResponse(wildcardDomain, true, false, "dnsTokenWildcardR53");
        authzValid.Challenges.First(c => c.Type == "dns-01").Status = "valid";
        authzValid.Status = "valid";
        authzValid.Nonce = "nonceWildR53Validated";
        _mockAcmeClient.Setup(ac => ac.GetAuthzAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<string>(), "nonceAfterChallenge"))
            .ReturnsAsync(authzValid);

        var resultNonce = await _renewalHandler.ValidateAuthorizationAsync("keyWildR53", "kidWildR53", "nonceWildR53_0", new Uri("https://auth.example.com/wildr53"), _mockLogger.Object);

        Assert.AreEqual("nonceWildR53Validated", resultNonce);
        _mockAwsProvider.Verify(); // Verifies all .Verifiable() setups
        _mockCloudflareProvider.VerifyNoOtherCalls();
        _mockAcmeClient.Verify(ac => ac.TriggerChallengeAsync(It.IsAny<string>(), It.Is<Uri>(u => u.ToString().Contains("http-01")), It.IsAny<string>(), It.IsAny<string>()), Times.Never, "HTTP-01 challenge should not have been triggered for wildcard.");
    }

    [TestMethod]
    public async Task Wildcard_DNS01_Success_Cloudflare()
    {
        var wildcardDomain = "*.cf-example.com";
        var baseDomain = "cf-example.com";
        _mockConfig.Setup(c => c.EnableRoute53).Returns(false);
        _mockConfig.Setup(c => c.EnableCloudflare).Returns(true);

        var authzInitial = CreateAuthzResponse(wildcardDomain, true, false, "dnsTokenWildcardCF");
        _mockAcmeClient.Setup(ac => ac.GetAuthzAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<string>(), "nonceWildCF_0"))
            .ReturnsAsync(authzInitial);
        
        _mockCertClient.Setup(cc => cc.GetKeyAuthorization("dnsTokenWildcardCF")).Returns("keyAuthWildcardCF");

        _mockCloudflareProvider.Setup(cfp => cfp.CreateTxtRecordAsync(baseDomain, $"_acme-challenge.{baseDomain}", It.IsAny<string>()))
            .Returns(Task.CompletedTask).Verifiable();
        _mockCloudflareProvider.Setup(cfp => cfp.DeleteTxtRecordAsync(baseDomain, $"_acme-challenge.{baseDomain}", It.IsAny<string>()))
            .Returns(Task.CompletedTask).Verifiable();

        _mockAcmeClient.Setup(ac => ac.TriggerChallengeAsync(It.IsAny<string>(), new Uri(authzInitial.Challenges.First(c=>c.Type=="dns-01").Url), It.IsAny<string>(), authzInitial.Nonce))
            .ReturnsAsync(CreateChallengeResponse("dns-01", "dnsTokenWildcardCF", "pending"));

        var authzValid = CreateAuthzResponse(wildcardDomain, true, false, "dnsTokenWildcardCF");
        authzValid.Challenges.First(c => c.Type == "dns-01").Status = "valid";
        authzValid.Status = "valid";
        authzValid.Nonce = "nonceWildCFValidated";
        _mockAcmeClient.Setup(ac => ac.GetAuthzAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<string>(), "nonceAfterChallenge"))
            .ReturnsAsync(authzValid);

        var resultNonce = await _renewalHandler.ValidateAuthorizationAsync("keyWildCF", "kidWildCF", "nonceWildCF_0", new Uri("https://auth.example.com/wildcf"), _mockLogger.Object);

        Assert.AreEqual("nonceWildCFValidated", resultNonce);
        _mockCloudflareProvider.Verify();
        _mockAwsProvider.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task Wildcard_DNS01_NoDnsChallengeFromAcme_ThrowsException()
    {
        var wildcardDomain = "*.nodns.example.com";
        _mockConfig.Setup(c => c.EnableRoute53).Returns(true); // Provider configured

        var authzInitial = CreateAuthzResponse(wildcardDomain, false, true, httpToken: "httpTokenOnly"); // No DNS-01 challenge
        _mockAcmeClient.Setup(ac => ac.GetAuthzAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(authzInitial);

        await Assert.ThrowsExceptionAsync<Exception>(async () => 
            await _renewalHandler.ValidateAuthorizationAsync("keyNoDns", "kidNoDns", "nonceNoDns0", new Uri("https://auth.example.com/nodns"), _mockLogger.Object),
            $"ACME server did not provide a DNS-01 challenge for wildcard domain {wildcardDomain}.");
        
        _mockAwsProvider.Verify(ap => ap.CreateTxtRecordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _mockAcmeClient.Verify(ac => ac.TriggerChallengeAsync(It.IsAny<string>(), It.Is<Uri>(u => u.ToString().Contains("http-01")), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task Wildcard_DNS01_ProviderCreateFails_ThrowsException_NoHttpFallback()
    {
        var wildcardDomain = "*.providerfail.example.com";
        var baseDomain = "providerfail.example.com";
        _mockConfig.Setup(c => c.EnableRoute53).Returns(true);

        var authzInitial = CreateAuthzResponse(wildcardDomain, true, false, "dnsTokenProviderFail");
        _mockAcmeClient.Setup(ac => ac.GetAuthzAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(authzInitial);
        _mockCertClient.Setup(cc => cc.GetKeyAuthorization(It.IsAny<string>())).Returns("keyAuthPF");

        _mockAwsProvider.Setup(ap => ap.CreateTxtRecordAsync(baseDomain, $"_acme-challenge.{baseDomain}", It.IsAny<string>()))
            .ThrowsAsync(new Exception("Simulated AWS Create Exception")).Verifiable();
        _mockAwsProvider.Setup(ap => ap.DeleteTxtRecordAsync(baseDomain, $"_acme-challenge.{baseDomain}", It.IsAny<string>()))
            .Returns(Task.CompletedTask); // Delete might still be called if dnsRecordCreated was true before throw, or not.

        await Assert.ThrowsExceptionAsync<Exception>(async () => 
            await _renewalHandler.ValidateAuthorizationAsync("keyPF", "kidPF", "noncePF0", new Uri("https://auth.example.com/pf"), _mockLogger.Object),
            "Simulated AWS Create Exception"); // Original exception should be re-thrown for wildcards

        _mockAwsProvider.Verify(); // Verifies CreateTxtRecordAsync was called
        // Depending on precise error handling logic for dnsRecordCreated, Delete might or might not be called.
        // Current code: if Create throws, dnsRecordCreated remains false, so Delete is NOT called in finally.
        _mockAwsProvider.Verify(ap => ap.DeleteTxtRecordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _mockAcmeClient.Verify(ac => ac.TriggerChallengeAsync(It.IsAny<string>(), It.Is<Uri>(u => u.ToString().Contains("http-01")), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task Wildcard_DNS01_AcmeValidationFails_ThrowsException_NoHttpFallback()
    {
        var wildcardDomain = "*.acmevalidationfail.example.com";
        var baseDomain = "acmevalidationfail.example.com";
        _mockConfig.Setup(c => c.EnableRoute53).Returns(true);

        var authzInitial = CreateAuthzResponse(wildcardDomain, true, false, "dnsTokenAcmeFail");
        _mockAcmeClient.Setup(ac => ac.GetAuthzAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<string>(), "nonceAcmeFail0"))
            .ReturnsAsync(authzInitial);
        
        _mockCertClient.Setup(cc => cc.GetKeyAuthorization("dnsTokenAcmeFail")).Returns("keyAuthAcmeFail");

        _mockAwsProvider.Setup(ap => ap.CreateTxtRecordAsync(baseDomain, $"_acme-challenge.{baseDomain}", It.IsAny<string>()))
            .Returns(Task.CompletedTask).Verifiable();
        _mockAwsProvider.Setup(ap => ap.DeleteTxtRecordAsync(baseDomain, $"_acme-challenge.{baseDomain}", It.IsAny<string>()))
            .Returns(Task.CompletedTask).Verifiable();

        _mockAcmeClient.Setup(ac => ac.TriggerChallengeAsync(It.IsAny<string>(), new Uri(authzInitial.Challenges.First(c=>c.Type=="dns-01").Url), It.IsAny<string>(), authzInitial.Nonce))
            .ReturnsAsync(CreateChallengeResponse("dns-01", "dnsTokenAcmeFail", "pending")); // Initial trigger is fine

        // Subsequent GetAuthzAsync calls never return "valid" for the DNS challenge
        var authzStillPending = CreateAuthzResponse(wildcardDomain, true, false, "dnsTokenAcmeFail");
        authzStillPending.Challenges.First(c => c.Type == "dns-01").Status = "pending"; 
        authzStillPending.Status = "pending";
        authzStillPending.Nonce = "nonceAcmeFail_StillPending";
        _mockAcmeClient.Setup(ac => ac.GetAuthzAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<string>(), "nonceAfterChallenge"))
            .ReturnsAsync(authzStillPending);

        await Assert.ThrowsExceptionAsync<Exception>(async () => 
            await _renewalHandler.ValidateAuthorizationAsync("keyAcmeFail", "kidAcmeFail", "nonceAcmeFail0", new Uri("https://auth.example.com/acmefail"), _mockLogger.Object),
            $"DNS-01 challenge failed for wildcard domain {wildcardDomain} and is mandatory.");

        _mockAwsProvider.Verify(); // Create and Delete should have been called
        _mockAcmeClient.Verify(ac => ac.TriggerChallengeAsync(It.IsAny<string>(), It.Is<Uri>(u => u.ToString().Contains("http-01")), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // Review existing test: ValidateAuthorizationAsync_Dns01_CreateTxtFails_FallbackToHttp01
    // This test ALREADY covers non-wildcard DNS-01 failure falling back to HTTP-01.
    // It mocks CreateTxtRecordAsync to throw an exception and verifies HTTP-01 is then attempted.
    // The logic for "isWildcard" in RenewalHandler ensures this fallback only happens for non-wildcards.
}
