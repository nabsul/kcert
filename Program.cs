using KCert;
using KCert.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Linq;
using System.Reflection;

if (args.Length > 0 && args[^1] == "generate-key")
{
    Console.WriteLine("Generating ACME Key");
    var key = CertClient.GenerateNewKey();
    Console.WriteLine(key);
    return;
}

var builder = WebApplication.CreateBuilder(args);

var serviceTypes = Assembly.GetExecutingAssembly().GetTypes()
    .Where(t => t.GetCustomAttribute<ServiceAttribute>() != null);

foreach (var t in serviceTypes)
{
    builder.Services.AddSingleton(t);
}

builder.Services.AddControllersWithViews();

builder.Configuration.AddUserSecrets<Program>(optional: true);
builder.Configuration.AddEnvironmentVariables();

builder.WebHost.ConfigureKestrel(opt => {
    opt.ListenAnyIP(80);
    opt.ListenAnyIP(8080);
});

builder.Services.AddHostedService<RenewalService>();
builder.Services.AddHostedService<IngressMonitorService>();
builder.Services.AddHostedService<ConfigMonitorService>();

var app = builder.Build();

if (builder.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/error");
}

app.UseStaticFiles();
app.UseRouting();
app.UseEndpoints(endpoints => endpoints.MapControllers());

app.Run();
