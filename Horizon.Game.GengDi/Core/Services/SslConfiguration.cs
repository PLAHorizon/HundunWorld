using System;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Horizon.Game.GengDi.Core.Services
{
    public static class SslConfiguration
    {
        // 修复（安全）：默认不跳过 TLS 校验（_forceBypassSsl = null），仅显式强制或
        // HUNDUN_FORCE_BYPASS_SSL 环境变量（开发/测试/灰度环境）才跳过。
        // 原实现无条件 return true，使所有 HTTP 客户端接受任意证书，可被中间人攻击。
        private static bool? _forceBypassSsl;

        public static bool ShouldBypassSslValidation
        {
            get
            {
                if (_forceBypassSsl.HasValue)
                    return _forceBypassSsl.Value;

                // 环境变量后门仅存在于调试构建；发布构建无法通过环境变量全局关闭证书校验
                // （安全审查 2026-08：防投放环境被诱导关闭 TLS 校验）。
#if DEBUG
                var forceBypass = Environment.GetEnvironmentVariable("HUNDUN_FORCE_BYPASS_SSL");
                if (!string.IsNullOrWhiteSpace(forceBypass) &&
                    (forceBypass.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) || forceBypass.Trim() == "1"))
                    return true;
#endif

                return false;
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

                    // 私有部署自签证书：与程序集内嵌的锁定证书（server_cert.pem）完全一致才放行，
                    // 其余证书仍走系统信任链校验失败语义。与鸿蒙端 caPath 方案同一信任锚。
                    if (certificate != null && PinnedServerCertificate.IsPinned(certificate))
                        return true;

                    return false;
                };
            }

            return handler;
        }

        public static HttpClientHandler CreateStandardHandler()
        {
            // 标准处理器：使用系统默认 TLS 校验（严格验证证书链与主机名），不注入任何自定义回调。
            // 与 CreateTestEnvironmentHandler 解耦，避免"标准"悄悄等同于"跳过校验"。
            return new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
            };
        }
    }
}
