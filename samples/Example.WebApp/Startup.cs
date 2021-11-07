using System;
using I18Next.Net.AspNetCore;
using I18Next.Net.Backends;
using I18Next.Net.Extensions;
using I18Next.Net.RemoteJsonFileBackend;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Example.WebApp
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services
                .AddRemoteJsonFileBackendServices(Configuration)
                .AddI18NextLocalization(i18n =>
            {
                i18n.IntegrateToAspNetCore()
                    .UseDefaultNamespace("common")
                    .AddBackend<RemoteFileBackend>();
            });

            services.AddMvc()
                .AddI18NextViewLocalization();
        }
        
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
                app.UseDeveloperExceptionPage();
            else
                app.UseExceptionHandler("/Home/Error");

            // Enable request localization in order to determine the users desired language based on the Accept-Language header.
            app.UseRequestLocalization(options => options.AddSupportedCultures("de", "nl", "en"));
            
            app.UseRouting();
            
            app.UseStaticFiles();
            
            app.UseEndpoints(options =>
            {
                options.MapControllers();
                options.MapDefaultControllerRoute();
            });
        }

    }
}
