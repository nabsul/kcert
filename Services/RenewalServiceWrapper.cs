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

        public RenewalServiceWrapper(IServiceProvider services)
        {
            _services = services;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // The IHosted service is created as a singleton, but all other services are scoped
            // For this reason, we have to create a scope and manually fetch the services
            using var scope = _services.CreateScope();
            var svc = scope.ServiceProvider.GetService<RenewalService>();
            await svc.StartAsync(cancellationToken);
        }
    }
}
