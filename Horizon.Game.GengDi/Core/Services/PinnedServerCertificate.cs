using System;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Horizon.Game.GengDi.Core.Services
{
    /// <summary>
    /// 服务端自签证书锁定（certificate pinning）。
    /// <para>
    /// 私有部署的 WebApi 使用自签证书（Horizon.WebApi/cert.pfx，SAN 为部署机 IP），
    /// 不在操作系统信任链内。此处把该证书公钥部分（server_cert.pem）嵌入程序集，
    /// TLS 校验失败时若服务端证书与锁定证书完全一致则放行——与移动端鸿蒙 App 的
    /// caPath 方案同一信任锚。除该证书外不放宽任何其他校验。
    /// </para>
    /// </summary>
    internal static class PinnedServerCertificate
    {
        private const string ResourceName = "GengDi.server_cert.pem";
        private static readonly Lazy<string> s_pinnedThumbprint = new(LoadThumbprint, isThreadSafe: true);

        /// <summary>加载失败时为空串（此时不影响原有校验语义，仅无法放行自签证书）。</summary>
        internal static string Thumbprint => s_pinnedThumbprint.Value;

        /// <summary>判断给定服务端证书是否与锁定的自签证书一致（SHA-256 指纹比对）。</summary>
        internal static bool IsPinned(X509Certificate certificate)
        {
            if (certificate is null || s_pinnedThumbprint.Value.Length == 0)
            {
                return false;
            }

            try
            {
                using var sha256 = SHA256.Create();
                var hash = sha256.ComputeHash(certificate.GetRawCertData());
                return Convert.ToHexString(hash).Equals(s_pinnedThumbprint.Value, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string LoadThumbprint()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream(ResourceName);
                if (stream is null)
                {
                    return string.Empty;
                }

                using var reader = new System.IO.StreamReader(stream);
                var pem = reader.ReadToEnd();
                using var cert = new X509Certificate2(System.Text.Encoding.ASCII.GetBytes(pem));
                using var sha256 = SHA256.Create();
                return Convert.ToHexString(sha256.ComputeHash(cert.GetRawCertData()));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SSL配置] 加载锁定证书失败: {ex.Message}");
                return string.Empty;
            }
        }
    }
}
