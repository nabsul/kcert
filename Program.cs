using KCert;
using KCert.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Linq;
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

// Add all services marked with the [Service] attribute
Assembly.GetExecutingAssembly().GetTypes()
    .Where(t => t.GetCustomAttribute<ServiceAttribute>() != null)
    .ToList().ForEach(t => builder.Services.AddSingleton(t));

builder.Services.AddControllersWithViews();

builder.WebHost.ConfigureKestrel(opt => {
    opt.ListenAnyIP(80);
    opt.ListenAnyIP(8080);
});

// add background services
builder.Services.AddHostedService<RenewalService>();
builder.Services.AddHostedService<IngressMonitorService>();
builder.Services.AddHostedService<ConfigMonitorService>();

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.UseEndpoints(endpoints => endpoints.MapControllers());

app.Run();
