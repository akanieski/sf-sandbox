using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.ReverseProxy.Configuration;
using Microsoft.ReverseProxy.Configuration.ServiceFabric;

namespace ProxyTest
{
    public class Startup
    {
        public IConfiguration Configuration { get; }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddReverseProxy().LoadFromServiceFabric(); 
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/$ProxyUpdate", async (context) => {
                    await ServiceFabricConfigProvider.Update();
                    await context.Response.WriteAsync("Updated.");
                });
                endpoints.MapGet("/$ProxyConfig", async (context) => {
                    await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(ServiceFabricConfigProvider.GetCurrentConfig(), typeof(ServiceFabricConfig), new System.Text.Json.JsonSerializerOptions(){WriteIndented= true}));
                });
                endpoints.MapReverseProxy();
            });
        }
    }
}
