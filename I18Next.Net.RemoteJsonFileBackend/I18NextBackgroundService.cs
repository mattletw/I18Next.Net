using System;
using System.Threading;
using System.Threading.Tasks;
using I18Next.Net.AspNetCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace I18Next.Net.RemoteJsonFileBackend
{
    public class I18NextBackgroundService : IHostedService
    {
        private readonly ILogger _logger;
        private Timer _timer;

        private readonly IRemoteTranslationFileCache _translator;
        private readonly IOptionsSnapshot<RemoteJsonFileOptions> _optionsSnapshot;
        
        public I18NextBackgroundService(ILogger<I18NextBackgroundService> logger, IRemoteTranslationFileCache translator, IOptionsSnapshot<RemoteJsonFileOptions> optionsSnapshot)
        {
            _logger = logger;
            _translator = translator;
            _optionsSnapshot = optionsSnapshot;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Timed Hosted Service running.");

            _timer = new Timer(DoWork, null, TimeSpan.Zero,
                TimeSpan.FromSeconds(_optionsSnapshot.Value.CacheTTL));

            return Task.CompletedTask;
        }

        private void DoWork(object state)
        {
            _translator.EmptyCache();
        }

        public Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Timed Hosted Service is stopping.");

            _timer?.Change(Timeout.Infinite, 0);

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
