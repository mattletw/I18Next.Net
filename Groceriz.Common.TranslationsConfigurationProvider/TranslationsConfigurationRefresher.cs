using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Groceriz.Common.TranslationsConfigurationProvider
{
    internal class TranslationsConfigurationRefresher : ITranslationsConfigurationRefresher
    {
        private TranslationsConfigurationProvider _provider = null;

        public Uri AppConfigurationEndpoint { get; private set; } = null;

        public ILoggerFactory LoggerFactory { 
            get 
            {
                ThrowIfNullProvider(nameof(LoggerFactory));
                return _provider.LoggerFactory;
            }
            set 
            { 
                ThrowIfNullProvider(nameof(LoggerFactory)); 
                _provider.LoggerFactory = value;
            } 
        }

        public void SetProvider(TranslationsConfigurationProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            AppConfigurationEndpoint = _provider.AppConfigurationEndpoint;
        }

        public async Task RefreshAsync(CancellationToken cancellationToken)
        {
            ThrowIfNullProvider(nameof(RefreshAsync));
            await _provider.RefreshAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> TryRefreshAsync(CancellationToken cancellationToken)
        {
            if (_provider == null)
            {
                return false;
            }

            return await _provider.TryRefreshAsync(cancellationToken).ConfigureAwait(false);
        }

        public void SetDirty(TimeSpan? maxDelay)
        {
            ThrowIfNullProvider(nameof(SetDirty));
            _provider.SetDirty(maxDelay);
        }

        private void ThrowIfNullProvider(string operation)
        {
            if (_provider == null)
            {
                throw new InvalidOperationException($"ConfigurationBuilder.Build() must be called before {operation} can be accessed.");
            }
        }
    }
    }
