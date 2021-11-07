using System;
using Microsoft.AspNetCore.Builder;

namespace Groceriz.Common.TranslationsConfigurationProvider
{
    public static class TranslationsConfigurationExtensions
    {
        public static IApplicationBuilder UseAzureAppConfiguration(this IApplicationBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            // Verify if AddAzureAppConfiguration was done before calling UseAzureAppConfiguration.
            // We use the IConfigurationRefresherProvider to make sure if the required services were added.
            if (builder.ApplicationServices.GetService(typeof(ITranslationsConfigurationRefresherProvider)) == null)
            {
                throw new InvalidOperationException("Unable to find the required services. Please add all the required services by calling 'IServiceCollection.AddAzureAppConfiguration' inside the call to 'ConfigureServices(...)' in the application startup code.");
            }

            return builder.UseMiddleware<TranslationsConfigurationRefreshMiddleware>();
        }
    }
}
