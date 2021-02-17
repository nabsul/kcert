using KCert.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KCert
{
    public class Program
    {
        private const string EnvironmentPrefix = "KCERT_";

        public static void Main(string[] args)
        {
            Host.CreateDefaultBuilder(args)
             .ConfigureAppConfiguration((ctx, cfg) => cfg.AddEnvironmentVariables(prefix: EnvironmentPrefix))
             .ConfigureWebHostDefaults(webBuilder => webBuilder.UseStartup<Startup>())
             .ConfigureServices(services => services.AddHostedService<RenewalServiceWrapper>())
             .Build().Run();
        }
    }
}
