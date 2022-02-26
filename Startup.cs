using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Linq;
using System.Reflection;

namespace KCert;

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        AddKCertServices(services);
        services.AddControllersWithViews();
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
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
    }

    private static void AddKCertServices(IServiceCollection services)
    {
        var serviceTypes = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t.GetCustomAttribute<ServiceAttribute>() != null);

        foreach (var t in serviceTypes)
        {
            services.AddSingleton(t);
        }
    }
}
