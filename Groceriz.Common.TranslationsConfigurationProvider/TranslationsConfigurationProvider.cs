using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Groceriz.Common.TranslationsConfigurationProvider.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Groceriz.Common.TranslationsConfigurationProvider
{
    public class TranslationsConfigurationProvider : ConfigurationProvider, ITranslationsConfigurationRefresher
    {
        private static readonly TimeSpan MinDelayForUnhandledFailure = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan DefaultMaxSetDirtyDelay = TimeSpan.FromSeconds(30);

        // To avoid concurrent network operations, this flag is used to achieve synchronization between multiple threads.
        private int _networkOperationsInProgress = 0;
        private ILogger _logger;
        private ILoggerFactory _loggerFactory;

        
        public ILoggerFactory LoggerFactory
        {
            get
            {
                return _loggerFactory;
            }
            set
            {
                _loggerFactory = value;
                _logger = _loggerFactory?.CreateLogger(LoggingConstants.AppConfigRefreshLogCategory);
            }
        }

        public TranslationsConfigurationProvider(AzureAppConfigurationOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// Loads (or reloads) the data for this provider.
        /// </summary>
        public override void Load()
        {
            var watch = Stopwatch.StartNew();

            try
            {
                // Load() is invoked only once during application startup. We don't need to check for concurrent network
                // operations here because there can't be any other startup or refresh operation in progress at this time.
                LoadAll(CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch (ArgumentException)
            {
                // Instantly re-throw the exception
                throw;
            }
            catch
            {
                // AzureAppConfigurationProvider.Load() method is called in the application's startup code path.
                // Unhandled exceptions cause application crash which can result in crash loops as orchestrators attempt to restart the application.
                // Knowing the intended usage of the provider in startup code path, we mitigate back-to-back crash loops from overloading the server with requests by waiting a minimum time to propogate fatal errors.

                var waitInterval = MinDelayForUnhandledFailure.Subtract(watch.Elapsed);

                if (waitInterval.Ticks > 0)
                {
                    Task.Delay(waitInterval).ConfigureAwait(false).GetAwaiter().GetResult();
                }

                // Re-throw the exception after the additional delay (if required)
                throw;
            }
            finally
            {
                // Set the provider for AzureAppConfigurationRefresher instance after LoadAll has completed.
                // This stops applications from calling RefreshAsync until config has been initialized during startup.
                var refresher = (TranslationsConfigurationRefresher)_options.GetRefresher();
                refresher.SetProvider(this);
            }
        }

        public async Task RefreshAsync(CancellationToken cancellationToken)
        {
            // Ensure that concurrent threads do not simultaneously execute refresh operation. 
            if (Interlocked.Exchange(ref _networkOperationsInProgress, 1) == 0)
            {
                try
                {
                    await LoadAll(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Exchange(ref _networkOperationsInProgress, 0);
                }
            }
        }

        public async Task<bool> TryRefreshAsync(CancellationToken cancellationToken)
        {
            try
            {
                await RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (AggregateException e) when (e?.InnerExceptions?.All(e => e is RequestFailedException) ?? false)
            {
                _logger?.LogWarning(e, LoggingConstants.RefreshFailedError);

                return false;
            }
            catch (OperationCanceledException)
            {
                _logger?.LogWarning(LoggingConstants.RefreshCanceledError);
                return false;
            }

            return true;
        }

        public void SetDirty(TimeSpan? maxDelay)
        {
            DateTimeOffset cacheExpires = AddRandomDelay(DateTimeOffset.UtcNow, maxDelay ?? DefaultMaxSetDirtyDelay);

            foreach (KeyValueWatcher changeWatcher in _options.ChangeWatchers)
            {
                changeWatcher.CacheExpires = cacheExpires;
            }

            foreach (KeyValueWatcher changeWatcher in _options.MultiKeyWatchers)
            {
                changeWatcher.CacheExpires = cacheExpires;
            }
        }

        private async Task LoadAll(CancellationToken cancellationToken)
        {
            IDictionary<string, ConfigurationSetting> data = null;
            string cachedData = null;
            bool success = false;

            try
            {
                var serverData = new Dictionary<string, ConfigurationSetting>(StringComparer.OrdinalIgnoreCase);

                // Use default query if there are no key-values specified for use other than the feature flags
                bool useDefaultQuery = !_options.KeyValueSelectors;

                if (useDefaultQuery)
                {
                    // Load all key-values with the null label.
                    var selector = new SettingSelector
                    {
                        KeyFilter = KeyFilter.Any,
                        LabelFilter = LabelFilter.Null
                    };

                    await CallWithRequestTracing(async () =>
                    {
                        await foreach (ConfigurationSetting setting in _client.GetConfigurationSettingsAsync(selector, cancellationToken).ConfigureAwait(false))
                        {
                            serverData[setting.Key] = setting;
                        }
                    }).ConfigureAwait(false);
                }

                foreach (var loadOption in _options.KeyValueSelectors)
                {
                    if ((useDefaultQuery && LabelFilter.Null.Equals(loadOption.LabelFilter)) ||
                        _options.KeyValueSelectors.Any(s => s != loadOption &&
                           string.Equals(s.KeyFilter, KeyFilter.Any) &&
                           string.Equals(s.LabelFilter, loadOption.LabelFilter)))
                    {
                        // This selection was already encapsulated by a wildcard query
                        // Or would select kvs obtained by a different selector
                        // We skip it to prevent unnecessary requests
                        continue;
                    }

                    var selector = new SettingSelector
                    {
                        KeyFilter = loadOption.KeyFilter,
                        LabelFilter = loadOption.LabelFilter
                    };

                    await CallWithRequestTracing(async () =>
                    {
                        await foreach (ConfigurationSetting setting in _client.GetConfigurationSettingsAsync(selector, cancellationToken).ConfigureAwait(false))
                        {
                            serverData[setting.Key] = setting;
                        }
                    }).ConfigureAwait(false);
                }

                // Block current thread for the initial load of key-values registered for refresh that are not already loaded
                await Task.Run(() => LoadKeyValuesRegisteredForRefresh(serverData, cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult()).ConfigureAwait(false);
                data = serverData;
                success = true;
            }
            catch (Exception exception) when (exception is RequestFailedException ||
                                            ((exception as AggregateException)?.InnerExceptions?.All(e => e is RequestFailedException) ?? false) ||
                                            exception is OperationCanceledException)
            {
                if (_options.OfflineCache != null)
                {
                    // During startup or refreshAll scenario, we'll try to populate config from offline cache, if available
                    cachedData = _options.OfflineCache.Import(_options);
                    
                    if (cachedData != null)
                    {
                        data = JsonSerializer.Deserialize<IDictionary<string, ConfigurationSetting>>(cachedData);
                    }
                }

                // If we're unable to load data from offline cache, check if we need to ignore or rethrow the exception 
                if (data == null)
                {
                    throw;
                }
            }
            finally
            {
                // Update the cache expiration time for all refresh registered settings and feature flags
                foreach (KeyValueWatcher changeWatcher in _options.ChangeWatchers.Concat(_options.MultiKeyWatchers))
                {
                    UpdateCacheExpirationTime(changeWatcher, success);
                }
            }

            if (data != null)
            {
                // Invalidate all the cached KeyVault secrets
                foreach (IKeyValueAdapter adapter in _options.Adapters)
                {
                    adapter.InvalidateCache();
                }

                await SetData(data, ignoreFailures, cancellationToken).ConfigureAwait(false);
                
                if (_options.OfflineCache != null && cachedData == null)
                {
                    _options.OfflineCache.Export(_options, JsonSerializer.Serialize(data));
                }
            }
        }

        private async Task LoadKeyValuesRegisteredForRefresh(IDictionary<string, ConfigurationSetting> data, CancellationToken cancellationToken)
        {
            _watchedSettings.Clear();

            foreach (KeyValueWatcher changeWatcher in _options.ChangeWatchers)
            {
                string watchedKey = changeWatcher.Key;
                string watchedLabel = changeWatcher.Label;
                KeyValueIdentifier watchedKeyLabel = new KeyValueIdentifier(watchedKey, watchedLabel);

                // Skip the loading for the key-value in case it has already been loaded
                if (data.TryGetValue(watchedKey, out ConfigurationSetting loadedKv)
                    && watchedKeyLabel.Equals(new KeyValueIdentifier(loadedKv.Key, loadedKv.Label)))
                {
                    _watchedSettings[watchedKeyLabel] = loadedKv;
                    continue;
                }

                // Send a request to retrieve key-value since it may be either not loaded or loaded with a different label or different casing
                ConfigurationSetting watchedKv = null;
                try
                {
                    await CallWithRequestTracing(async () => watchedKv = await _client.GetConfigurationSettingAsync(watchedKey, watchedLabel, cancellationToken)).ConfigureAwait(false);
                }
                catch (RequestFailedException e) when (e.Status == (int)HttpStatusCode.NotFound)
                {
                    watchedKv = null;
                }

                // If the key-value was found, store it for updating the settings
                if (watchedKv != null)
                {
                    data[watchedKey] = watchedKv;
                    _watchedSettings[watchedKeyLabel] = watchedKv;
                }
            }
        }
        
        private async Task SetData(IDictionary<string, ConfigurationSetting> data, CancellationToken cancellationToken)
        {
            // Set the application data for the configuration provider
            var applicationData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, ConfigurationSetting> kvp in data)
            {
                IEnumerable<KeyValuePair<string, string>> keyValuePairs = null;

                keyValuePairs = await ProcessAdapters(kvp.Value, cancellationToken).ConfigureAwait(false);
                

                foreach (KeyValuePair<string, string> kv in keyValuePairs)
                {
                    string key = kv.Key;
                    foreach (string prefix in _options.KeyPrefixes)
                    {
                        if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            key = key.Substring(prefix.Length);
                            break;
                        }
                    }

                    applicationData[key] = kv.Value;
                }
            }

            Data = applicationData;

            // Notify that the configuration has been updated
            OnReload();
        }

        private async Task<IEnumerable<KeyValuePair<string, string>>> ProcessAdapters(ConfigurationSetting setting, CancellationToken cancellationToken)
        {
            List<KeyValuePair<string, string>> keyValues = null;

            foreach (IKeyValueAdapter adapter in _options.Adapters)
            {
                if (!adapter.CanProcess(setting))
                {
                    continue;
                }

                IEnumerable<KeyValuePair<string, string>> kvs = await adapter.ProcessKeyValue(setting, cancellationToken).ConfigureAwait(false);

                if (kvs != null)
                {
                    keyValues = keyValues ?? new List<KeyValuePair<string, string>>();

                    keyValues.AddRange(kvs);
                }
            }

            return keyValues ?? Enumerable.Repeat(new KeyValuePair<string, string>(setting.Key, setting.Value), 1);
        }

        private DateTimeOffset AddRandomDelay(DateTimeOffset dt, TimeSpan maxDelay)
        {
            long randomTicks = (long)(maxDelay.Ticks * RandomGenerator.NextDouble());
            return dt.AddTicks(randomTicks);
        }

        private bool IsAuthenticationError(Exception ex)
        {
            if (ex is RequestFailedException rfe)
            {
                return rfe.Status == (int)HttpStatusCode.Unauthorized || rfe.Status == (int)HttpStatusCode.Forbidden;
            }

            if (ex is AggregateException ae)
            {
                return ae.InnerExceptions?.Any(inner => IsAuthenticationError(inner)) ?? false;
            }

            return false;
        }

        private void UpdateCacheExpirationTime(KeyValueWatcher changeWatcher, bool success)
        {
            TimeSpan cacheExpirationTime;

            if (success)
            {
                changeWatcher.RefreshAttempts = 0;
                cacheExpirationTime = changeWatcher.CacheExpirationInterval;
            }
            else
            {
                if (changeWatcher.RefreshAttempts < int.MaxValue)
                {
                    changeWatcher.RefreshAttempts++;
                }

                cacheExpirationTime = changeWatcher.CacheExpirationInterval.CalculateBackoffTime(changeWatcher.RefreshAttempts);
            }

            changeWatcher.CacheExpires = DateTimeOffset.UtcNow.Add(cacheExpirationTime);
        }
    }
}
