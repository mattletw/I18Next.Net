using I18Next.Net.AspNetCore;
using I18Next.Net.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace I18Next.Net.RemoteJsonFileBackend
{
    public static class ServiceCollectionExtensions
    {

        public static IServiceCollection AddRemoteJsonFileBackendServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddHttpClient(Constants.HttpClientName);
            
            services.Configure<RemoteJsonFileOptions>(configuration.GetSection(Constants.TranslationsOptions));
            
            services.AddHostedService<I18NextBackgroundService>();
            services.TryAddSingleton<RemoteFileCacheTranslator>();
            services.TryAddSingleton<ITranslator>(serviceProvider => serviceProvider.GetRequiredService<RemoteFileCacheTranslator>());
            services.TryAddSingleton<IRemoteTranslationFileCache>(serviceProvider => serviceProvider.GetRequiredService<RemoteFileCacheTranslator>());
            
            return services;
        }
    }
}
