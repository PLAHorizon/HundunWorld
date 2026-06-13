using System;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Horizon.Game.GengDi.Core.Services
{
    public static class SslConfiguration
    {
        private static bool? _forceBypassSsl = true;

        public static bool ShouldBypassSslValidation
        {
            get
            {
                return true;
//                if (_forceBypassSsl.HasValue)
//                    return _forceBypassSsl.Value;

//#if DEBUG
//                return true;
//#else
//                var forceBypass = Environment.GetEnvironmentVariable("HUNDUN_FORCE_BYPASS_SSL");
//                if (!string.IsNullOrWhiteSpace(forceBypass) &&
//                    (forceBypass.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) || forceBypass.Trim() == "1"))
//                    return true;

//                var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
//                if (string.IsNullOrWhiteSpace(env))
//                    env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
//                if (string.IsNullOrWhiteSpace(env))
//                    env = Environment.GetEnvironmentVariable("HUNDUN_ENVIRONMENT");

//                if (!string.IsNullOrWhiteSpace(env))
//                {
//                    var normalizedEnv = env.Trim().ToLowerInvariant();
//                    if (normalizedEnv.Contains("dev") ||
//                        normalizedEnv.Contains("test") ||
//                        normalizedEnv.Contains("staging") ||
//                        normalizedEnv.Contains("debug"))
//                        return true;
//                }

//                return false;
//#endif
            }
        }

        public static void ForceBypassSslValidation(bool bypass)
        {
            _forceBypassSsl = bypass;
            System.Diagnostics.Debug.WriteLine($"[SSL配置] 强制设置SSL验证跳过: {bypass}");
        }

        public static HttpClientHandler CreateTestEnvironmentHandler()
        {
            var bypass = ShouldBypassSslValidation;
            System.Diagnostics.Debug.WriteLine($"[SSL配置] 创建HttpClientHandler, 跳过验证: {bypass}");

            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
            };

            if (bypass)
            {
                handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            }
            else
            {
                handler.ServerCertificateCustomValidationCallback = (request, certificate, chain, errors) =>
                {
                    if (errors == SslPolicyErrors.None)
                        return true;

                    if (request?.RequestUri?.IsLoopback == true)
                        return true;

                    return false;
                };
            }

            return handler;
        }

        public static HttpClientHandler CreateStandardHandler()
        {
            return CreateTestEnvironmentHandler();
        }
    }
}
