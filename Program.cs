using KCert;
using KCert.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;

if (args.Length > 0 && args[^1] == "generate-key")
{
    Console.WriteLine("Generating ACME Key");
    var key = CertClient.GenerateNewKey();
    Console.WriteLine(key);
    return;
}

var fallbacks = new Dictionary<string, string>
{
    { "Acme:Key", CertClient.GenerateNewKey() }
};

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((ctx, cfg) =>
    {
        cfg.AddInMemoryCollection(fallbacks);
        cfg.AddUserSecrets<Program>(optional: true);
        cfg.AddEnvironmentVariables();
    })
    .ConfigureWebHostDefaults(webBuilder =>
    {
        webBuilder.ConfigureKestrel(opt =>
        {
            opt.ListenAnyIP(80);
            opt.ListenAnyIP(8080);
        });
        webBuilder.UseStartup<Startup>();
    })
    .ConfigureServices(services =>
    {
        services.AddHostedService<RenewalService>();
        services.AddHostedService<IngressMonitorService>();
        services.AddHostedService<ConfigMonitorService>();
    })
    .Build();

host.Run();
