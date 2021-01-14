using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using KCert.Lib;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System;

namespace KCert
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var host = CreateHostBuilder(args).Build();
            _ = StartRenewalServiceAsync(host);
            host.Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.AddEnvironmentVariables(prefix: "KCERT_");
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });

        private static async Task StartRenewalServiceAsync(IHost host)
        {
            var log = host.Services.GetService<ILogger<Program>>();
            var renewal = host.Services.GetService<RenewalManager>();
            try
            {
                await renewal.StartRenewalServiceAsync();
            }
            catch(Exception ex)
            {
                log.LogError(ex, $"Renewal service failed unexpectedly");
            }
        }
    }
}
