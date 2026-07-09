using KCert;
using KCert.Challenge;
using KCert.Services;

// command line option for manually generating an ECDSA key
if (args.Length > 0 && args[^1] == "generate-key")
{
    Console.WriteLine("Generating ACME Key");
    var key = CertClient.GenerateNewKey();
    Console.WriteLine(key);
    return;
}

var builder = WebApplication.CreateBuilder(args);
Console.WriteLine($"KCert starting with environment {builder.Environment.EnvironmentName}...");
var cfg = new KCertConfig(builder.Configuration);

builder.Services.AddConnections();
builder.Services.AddControllersWithViews();
builder.Services.AddKCertServices(cfg);

var useHttpChallenge = cfg.ChallengeType == "http";
builder.WebHost.ConfigureKestrel(opt =>
{
    if (useHttpChallenge)
    {
        opt.ListenAnyIP(80);
    }
    opt.ListenAnyIP(8080);
});

var app = builder.Build();

await AcmeClient.ReadDirectoryAsync(cfg, app.Lifetime.ApplicationStopped);

// Port 8080: Full admin interface with static files and all controllers
app.MapWhen(c => c.Connection.LocalPort == 8080, b =>
{
    b.UseStaticFiles();
    b.UseRouting().UseEndpoints(e => e.MapControllers());
});

if (useHttpChallenge)
{
    // Port 80: Simple ACME challenge handler
    app.MapWhen(c => c.Connection.LocalPort == 80, b =>
    {
        b.UseRouting();
        b.UseEndpoints(endpoints =>
        {
            endpoints.MapGet("/.well-known/acme-challenge/{token}", (string token, HttpChallengeProvider c) => c.HandleChallenge(token));
        });
    });
}

app.Run();
