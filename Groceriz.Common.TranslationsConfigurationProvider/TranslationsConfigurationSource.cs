using Microsoft.Extensions.Configuration;

namespace Groceriz.Common.TranslationsConfigurationProvider
{
    public class TranslationsConfigurationSource : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder) =>
            new TranslationsConfigurationProvider();
    }
}
