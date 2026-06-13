using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Game.GengDi.Core.Services
{
    internal sealed class AuthHeaderHandler : DelegatingHandler
    {
        public AuthHeaderHandler(HttpMessageHandler innerHandler)
            : base(innerHandler)
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var token = AccountService.GetAccessToken();
            var hadToken = !string.IsNullOrWhiteSpace(token);
            if (hadToken)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            byte[] contentBytes = null;
            string contentType = null;
            Dictionary<string, IEnumerable<string>> contentHeaders = null;
            if (request.Content != null)
            {
                contentBytes = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                contentType = request.Content.Headers.ContentType?.ToString();
                contentHeaders = new Dictionary<string, IEnumerable<string>>();
                foreach (var header in request.Content.Headers)
                {
                    contentHeaders[header.Key] = header.Value;
                }
            }

            var httpMethod = request.Method;
            var requestUri = request.RequestUri;
            var requestHeaders = new Dictionary<string, IEnumerable<string>>();
            foreach (var header in request.Headers)
            {
                requestHeaders[header.Key] = header.Value;
            }

            HttpResponseMessage response;
            try
            {
                response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new HttpRequestException("An error occurred while sending the HTTP request.", ex);
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && hadToken)
            {
                var refreshed = await AccountService.RefreshTokenAsync().ConfigureAwait(false);
                if (refreshed)
                {
                    response.Dispose();

                    var retryRequest = new HttpRequestMessage(httpMethod, requestUri);
                    var newToken = AccountService.GetAccessToken();
                    if (!string.IsNullOrWhiteSpace(newToken))
                    {
                        retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", newToken);
                    }

                    foreach (var header in requestHeaders)
                    {
                        if (!string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
                        {
                            retryRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
                        }
                    }

                    if (contentBytes != null)
                    {
                        retryRequest.Content = new ByteArrayContent(contentBytes);
                        if (!string.IsNullOrWhiteSpace(contentType))
                        {
                            retryRequest.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
                        }
                        if (contentHeaders != null)
                        {
                            foreach (var header in contentHeaders)
                            {
                                retryRequest.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                            }
                        }
                    }

                    return await base.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);
                }
            }

            return response;
        }
    }
}
