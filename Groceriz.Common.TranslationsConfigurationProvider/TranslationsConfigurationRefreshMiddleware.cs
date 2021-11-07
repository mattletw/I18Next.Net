using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Groceriz.Common.TranslationsConfigurationProvider
{
    public class TranslationsConfigurationRefreshMiddleware
    {
        private readonly RequestDelegate _next;
        public IEnumerable<ITranslationsConfigurationRefresher> Refreshers { get; }

        public TranslationsConfigurationRefreshMiddleware(RequestDelegate next, ITranslationsConfigurationRefresherProvider refresherProvider)
        {
            _next = next;
            Refreshers = refresherProvider.Refreshers;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            foreach (var refresher in Refreshers)
            {
                _ = refresher.TryRefreshAsync();
            }

            // Call the next delegate/middleware in the pipeline
            await _next(context).ConfigureAwait(false);
        }
    }
}
