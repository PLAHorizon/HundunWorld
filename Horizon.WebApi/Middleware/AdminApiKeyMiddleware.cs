using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Horizon.WebApi.Middleware
{
    public class AdminApiKeyMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IConfiguration _configuration;
        private const string AdminApiKeyHeaderName = "X-Admin-API-Key";

        public AdminApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
        {
            _next = next;
            _configuration = configuration;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var configuredKey = _configuration["AdminAuth:ApiKey"];
            if (!string.IsNullOrEmpty(configuredKey) &&
                context.Request.Headers.TryGetValue(AdminApiKeyHeaderName, out var headerValue) &&
                string.Equals(headerValue, configuredKey, StringComparison.Ordinal))
            {
                context.Items["IsAdmin"] = true;

                var claims = new[]
                {
                    new Claim(ClaimTypes.Role, "Admin"),
                    new Claim(ClaimTypes.NameIdentifier, "admin")
                };
                var identity = new ClaimsIdentity(claims, "AdminApiKey");
                var principal = new ClaimsPrincipal(identity);
                context.User = principal;
            }

            await _next(context);
        }
    }
}
