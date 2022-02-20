using KCert;
using KCert.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;

if (args.Length > 0 && args[args.Length - 1] == "generate-key")
{
    var key = CertClient.GenerateNewKey();
    Console.WriteLine(key);
    return;
}

var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((ctx, cfg) =>
    {
        cfg.AddJsonFile($"appsettings.{env}.json", optional: true);
        cfg.AddEnvironmentVariables();
    })
    .ConfigureWebHostDefaults(webBuilder => webBuilder.UseStartup<Startup>())
    .ConfigureServices(services => services.AddHostedService<RenewalService>())
    .Build().Run();
