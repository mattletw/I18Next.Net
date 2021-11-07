using Groceriz.Common.TranslationsConfigurationProvider;

namespace Microsoft.Extensions.Configuration
{
    public static class ConfigurationBuilderExtensions
    {
        public static IConfigurationBuilder AddTranslationsConfiguration(
            this IConfigurationBuilder builder)
        {
            // var tempConfig = builder.Build();
            // var connectionString =
                // tempConfig.GetConnectionString("WidgetConnectionString");
                return builder.Add(new TranslationsConfigurationSource());
        }
    }
}
