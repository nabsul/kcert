using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace KCert.Services
{
    public class RenewalServiceWrapper : IHostedService
    {
        private readonly IServiceProvider _services;
        private RenewalService _instance;

        public RenewalServiceWrapper(IServiceProvider services)
        {
            _services = services;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using var scope = _services.CreateScope();

            if (_instance == null)
            {
                lock (this)
                {
                    if (_instance == null)
                    {
                        _instance = scope.ServiceProvider.GetRequiredService<RenewalService>();
                    }
                }
            }
            
            await _instance.StartAsync(cancellationToken);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_instance != null)
            {
                await _instance.StopAsync(cancellationToken);
            }
        }
    }
}
