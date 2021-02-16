using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace KCert.Lib
{
    public static class KCertExtensions
    {
        public static void AddKCertServices(this IServiceCollection services)
        {
            foreach (var type in Assembly.GetEntryAssembly().GetTypes())
            {
                var attr = type.GetCustomAttribute<ServiceAttribute>();
                if (attr == null)
                {
                    continue;
                }

                services.AddSingleton(type);
            }
        }
    }
}
