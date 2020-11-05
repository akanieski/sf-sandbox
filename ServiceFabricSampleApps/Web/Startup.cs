using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Web
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddRazorPages();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.Use((context, next) => {
                try
                {
                    if (context.Request.Headers.ContainsKey("X-Forwarded-PathBase"))
                    {
                        Microsoft.Extensions.Primitives.StringValues path;
                        context.Request.Headers.TryGetValue("X-Forwarded-PathBase", out path);
                        context.Request.PathBase = path.FirstOrDefault();
                    }
                }
                catch (Exception ex)
                {
                    System.IO.File.AppendAllTextAsync(@"c:\src\log.txt", ex.ToString() + Environment.NewLine);
                }
                return next();
            });
            app.UseForwardedHeaders();


            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
            }



            app.UseStaticFiles();

            app.UseRouting();

            //app.UseForwardedHeaders();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapRazorPages();
            });
        }
    }
}
