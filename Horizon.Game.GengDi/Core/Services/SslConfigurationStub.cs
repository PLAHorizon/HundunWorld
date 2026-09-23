#if !DESKTOP
using System.Net.Http;

namespace WebViewControl
{
    /// <summary>
    /// Android 平台的 SslConfiguration 桩实现。
    /// 提供与 WebViewControl.SslConfiguration 相同的 API 接口，
    /// 使服务层代码在 Android 目标上无需 WebViewControl 包即可编译。
    /// </summary>
    public static class SslConfiguration
    {
        public static bool ShouldBypassSslValidation { get; private set; }

        public static void ForceBypassSslValidation(bool force)
        {
            ShouldBypassSslValidation = force;
        }

        public static HttpMessageHandler CreateTestEnvironmentHandler()
        {
            var handler = new HttpClientHandler();
            if (ShouldBypassSslValidation)
            {
                handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
            }
            return handler;
        }

        public static HttpMessageHandler CreateStandardHandler()
        {
            return new HttpClientHandler();
        }
    }
}
#endif
