using System.Collections.Generic;

namespace Groceriz.Common.TranslationsConfigurationProvider
{
    public interface ITranslationsConfigurationRefresherProvider
    {
        /// <summary>
        /// List of instances of <see cref="IConfigurationRefresher"/> for App Configuration.
        /// </summary>
        IEnumerable<ITranslationsConfigurationRefresher> Refreshers { get; }
    }
}
