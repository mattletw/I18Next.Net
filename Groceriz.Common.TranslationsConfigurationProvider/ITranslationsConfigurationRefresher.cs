using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Groceriz.Common.TranslationsConfigurationProvider
{
    public interface ITranslationsConfigurationRefresher
    {
        Uri AppConfigurationEndpoint { get; }

        ILoggerFactory LoggerFactory { get; set; }

        Task RefreshAsync(CancellationToken cancellationToken = default);

        Task<bool> TryRefreshAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets the cached value for key-values registered for refresh as dirty.
        /// A random delay is added before the cached value is marked as dirty to reduce potential throttling in case multiple instances refresh at the same time.
        /// </summary>
        /// <param name="maxDelay">Maximum delay before the cached value is marked as dirty. Default value is 30 seconds.</param>
        void SetDirty(TimeSpan? maxDelay = null);
    }
}
