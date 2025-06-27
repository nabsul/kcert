using KCert.Challenge;
using KCert.Services;
using System.Reflection;

// command line option for manually generating an ECDSA key
if (args.Length > 0 && args[^1] == "generate-key")
{
    Console.WriteLine("Generating ACME Key");
    var key = CertClient.GenerateNewKey();
    Console.WriteLine(key);
    return;
}

var builder = WebApplication.CreateBuilder(args);
var cfg = new KCertConfig(builder.Configuration);
builder.Services.AddSingleton(cfg);

foreach (var t in Assembly.GetExecutingAssembly().GetTypes())
{
    if (t.GetCustomAttribute<ServiceAttribute>() is not null)
    {
        builder.Services.AddSingleton(t);
        continue;
    }

    if (t.GetCustomAttribute<ChallengeAttribute>() is ChallengeAttribute attr && attr.ChallengeType == cfg.ChallengeType)
    {
        builder.Services.AddSingleton(typeof(IChallengeProvider), t);
    }
}

builder.Services.AddSingleton(s => s.GetRequiredService<KubernetesFactory>().GetClient());
builder.Services.AddConnections();
builder.Services.AddControllersWithViews();

builder.WebHost.ConfigureKestrel(opt =>
{
    opt.ListenAnyIP(80);
    opt.ListenAnyIP(8080);
});

// add background services
builder.Services.AddHostedService<RenewalService>();
builder.Services.AddHostedService<IngressMonitorService>();
builder.Services.AddHostedService<ConfigMonitorService>();

var app = builder.Build();

app.MapWhen(c => c.Connection.LocalPort == 8080, b =>
{
    b.UseStaticFiles();
    b.UseRouting().UseEndpoints(e => e.MapControllers());
});

app.MapWhen(c => c.Connection.LocalPort == 80 && c.Request.Path.HasValue && c.Request.Path.Value.StartsWith("/.well-known/acme-challenge"), b =>
{
    b.UseRouting().UseEndpoints(e => e.MapControllers());
});

app.Run();
