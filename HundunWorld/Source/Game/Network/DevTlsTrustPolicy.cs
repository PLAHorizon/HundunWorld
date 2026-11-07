using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FlaxEngine;

namespace HundunWorld.Game.Network
{
    /// <summary>
    /// 开发/灰度环境的服务端 TLS 信任策略（游戏客户端 HTTPS 通道专用）。
    /// <para>
    /// 背景：开发环境 WebApi（如 https://192.168.1.78:5101）使用自签名证书
    /// （Horizon.WebApi/cert.pfx，CN=192.168.1.78 且无 SAN），不在操作系统信任链内。
    /// 发布构建（Development/Release）只定义 BUILD_DEVELOPMENT 而不定义 DEBUG，
    /// 旧的“#if DEBUG 才跳过校验”逻辑在其它电脑上必然走系统校验失败，
    /// 登录 / 刷新令牌报 SSL 连接错误。
    /// </para>
    /// <para>
    /// 放行顺序（任一命中即放行，全部未命中则拒绝，保持生产语义）：
    /// 1. 系统校验通过——生产安全路径，行为不变；
    /// 2. 服务端证书与锁定证书一致——与耕地启动器 PinnedServerCertificate 同一信任锚，
    ///    由 Config/server_cert.pem（存在时优先）或内置 SHA-256 指纹提供，支持证书轮换；
    /// 3. 显式跳过校验开关（HUNDUN_FORCE_BYPASS_SSL / HUNDUN_ALLOW_INSECURE_SSL=1、
    ///    network_config.json 的 AllowInsecureSsl=true、或调试构建）；
    /// 4. 回环 / 私有网段地址（RFC1918、CGNAT、链路本地）——开发环境局域网直连，
    ///    network_config.json 显式设置 AllowInsecureSsl=false 可关闭该放行（严格模式）。
    /// </para>
    /// </summary>
    public static class DevTlsTrustPolicy
    {
        /// <summary>旧版兼容开关：任意证书全部放行（存在即生效，不限主机）。</summary>
        private const string ForcedBypassEnvVar = "HUNDUN_FORCE_BYPASS_SSL";

        /// <summary>显式开发开关：跳过 TLS 校验（存在即生效，不限主机）。</summary>
        private const string AllowInsecureEnvVar = "HUNDUN_ALLOW_INSECURE_SSL";

        /// <summary>可选的外部锁定证书文件名（放在游戏目录或 Config 目录下即可覆盖内置指纹）。</summary>
        private const string PinnedCertFileName = "server_cert.pem";

        /// <summary>
        /// 开发环境 WebApi 自签证书指纹（SHA-256，对证书原始数据计算），
        /// 与 Horizon.Game.GengDi/Core/Security/server_cert.pem、Horizon.WebApi/cert-2026-11-07.pfx
        /// 为同一张证书（CN=192.168.1.78，有效期至 2031-11-07）。
        /// 证书轮换后在游戏目录放置 Config/server_cert.pem 即可覆盖，无需重新编译。
        /// </summary>
        private const string BuiltInPinnedThumbprint = "502991A3F465FE54D9E022B112866E9D40CDBE636C61B38D33476B78C21661C8";

        private static readonly Lazy<string> s_pinnedThumbprint = new Lazy<string>(LoadPinnedThumbprint, isThreadSafe: true);
        private static readonly HashSet<string> s_loggedOnce = new HashSet<string>(StringComparer.Ordinal);
        private static readonly object s_logLock = new object();

        private enum LogLevel
        {
            Info,
            Warning,
            Error
        }

        /// <summary>
        /// 创建带开发环境 TLS 信任策略的 HTTP 处理器。
        /// </summary>
        public static HttpClientHandler CreateHttpClientHandler()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = ValidateServerCertificate
            };

            LogOnce("policy-init",
                $"[DevTls] TLS 信任策略已启用（锁定证书={DescribePinnedThumbprint()}，显式跳过校验={IsInsecureSslEnabled()}，严格模式={IsStrictMode()}）",
                LogLevel.Info);

            return handler;
        }

        /// <summary>
        /// 服务端证书校验回调：按放行顺序判定，任一命中返回 true。
        /// </summary>
        public static bool ValidateServerCertificate(HttpRequestMessage request, X509Certificate2 certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            // 1. 系统校验通过：生产安全路径，行为不变。
            if (sslPolicyErrors == SslPolicyErrors.None)
            {
                return true;
            }

            var host = request?.RequestUri?.Host ?? "<unknown>";

            // 2. 与锁定证书一致：适应开发环境自签证书，且不依赖操作系统信任链。
            if (certificate != null && IsPinnedCertificate(certificate))
            {
                LogOnce($"pinned:{host}",
                    $"[DevTls] 服务端证书与锁定证书一致，放行 {host}（系统校验错误：{sslPolicyErrors}）",
                    LogLevel.Info);
                return true;
            }

            // 3. 显式跳过校验开关：环境变量 / network_config.json / 调试构建。
            if (IsInsecureSslEnabled())
            {
                LogOnce($"insecure:{host}",
                    $"[DevTls] 显式跳过 TLS 校验，放行 {host}（系统校验错误：{sslPolicyErrors}）。仅限开发调试环境使用",
                    LogLevel.Warning);
                return true;
            }

            // 4. 回环 / 私有网段地址：开发环境局域网直连（严格模式下不放行）。
            if (!IsStrictMode() && IsPrivateOrLoopbackHost(request?.RequestUri))
            {
                LogOnce($"lan:{host}",
                    $"[DevTls] {host} 为回环/私有网段地址（开发环境），放行自签证书（系统校验错误：{sslPolicyErrors}）。" +
                    $"如需严格校验请在 network_config.json 设置 AllowInsecureSsl=false",
                    LogLevel.Warning);
                return true;
            }

            LogOnce($"reject:{host}",
                $"[DevTls] 拒绝对 {host} 的 TLS 连接：证书校验失败（{sslPolicyErrors}，Subject={certificate?.Subject ?? "<无证书>"}）。" +
                $"开发环境可将服务端证书保存为 Config/{PinnedCertFileName}，或在 network_config.json 开启 AllowInsecureSsl",
                LogLevel.Error);
            return false;
        }

        /// <summary>
        /// 显式跳过 TLS 校验：环境变量（HUNDUN_FORCE_BYPASS_SSL / HUNDUN_ALLOW_INSECURE_SSL）、
        /// network_config.json 的 AllowInsecureSsl=true、或调试构建（BUILD_DEBUG/DEBUG）。
        /// </summary>
        public static bool IsInsecureSslEnabled()
        {
#if DEBUG || BUILD_DEBUG
            return true;
#else
            if (IsEnvFlagEnabled(ForcedBypassEnvVar) || IsEnvFlagEnabled(AllowInsecureEnvVar))
            {
                return true;
            }

            try
            {
                if (NetworkConfigManager.LoadConfig()?.AllowInsecureSsl == true)
                {
                    return true;
                }
            }
            catch
            {
                // 配置读取失败按未开启处理
            }

            return false;
#endif
        }

        /// <summary>
        /// 严格模式：network_config.json 显式设置 AllowInsecureSsl=false 时，
        /// 不再对回环 / 私有网段地址放行自签证书（仅保留系统校验与锁定证书）。
        /// </summary>
        public static bool IsStrictMode()
        {
            try
            {
                return NetworkConfigManager.LoadConfig()?.AllowInsecureSsl == false;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsPinnedCertificate(X509Certificate2 certificate)
        {
            var pinned = s_pinnedThumbprint.Value;
            if (string.IsNullOrEmpty(pinned))
            {
                return false;
            }

            try
            {
                using (var sha256 = SHA256.Create())
                {
                    var thumbprint = Convert.ToHexString(sha256.ComputeHash(certificate.GetRawCertData()));
                    return thumbprint.Equals(pinned, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool IsPrivateOrLoopbackHost(Uri uri)
        {
            if (uri == null)
            {
                return false;
            }

            if (uri.IsLoopback)
            {
                return true;
            }

            // 域名不按私有网段放行，避免把域名解析结果误判为开发环境。
            if (!IPAddress.TryParse(uri.Host, out var address))
            {
                return false;
            }

            if (IPAddress.IsLoopback(address))
            {
                return true;
            }

            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                var bytes = address.GetAddressBytes();
                return bytes[0] == 10                                     // 10.0.0.0/8
                    || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) // 172.16.0.0/12
                    || (bytes[0] == 192 && bytes[1] == 168)               // 192.168.0.0/16
                    || (bytes[0] == 169 && bytes[1] == 254)               // 169.254.0.0/16 链路本地
                    || (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127); // 100.64.0.0/10 CGNAT
            }

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal)
                {
                    return true;
                }

                // fc00::/7 唯一本地地址
                return (address.GetAddressBytes()[0] & 0xFE) == 0xFC;
            }

            return false;
        }

        private static string LoadPinnedThumbprint()
        {
            foreach (var path in EnumeratePinnedCertPaths())
            {
                try
                {
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    var der = ReadPemToDer(File.ReadAllText(path));
                    if (der == null)
                    {
                        Debug.LogWarning($"[DevTls] 锁定证书解析失败（非 PEM 证书格式）: {path}");
                        continue;
                    }

                    using (var sha256 = SHA256.Create())
                    {
                        var thumbprint = Convert.ToHexString(sha256.ComputeHash(der));
                        Debug.Log($"[DevTls] 已加载外部锁定证书: {path}（SHA-256={thumbprint}）");
                        return thumbprint;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[DevTls] 加载锁定证书失败（{path}）: {ex.Message}");
                }
            }

            return BuiltInPinnedThumbprint;
        }

        private static IEnumerable<string> EnumeratePinnedCertPaths()
        {
            var baseDir = Directory.GetCurrentDirectory();
            yield return Path.Combine(baseDir, "Config", PinnedCertFileName);
            yield return Path.Combine(baseDir, PinnedCertFileName);

            string assemblyDir = null;
            try
            {
                assemblyDir = Path.GetDirectoryName(typeof(DevTlsTrustPolicy).Assembly.Location);
            }
            catch
            {
                // 忽略程序集路径获取失败
            }

            if (!string.IsNullOrEmpty(assemblyDir))
            {
                yield return Path.Combine(assemblyDir, PinnedCertFileName);
            }
        }

        /// <summary>
        /// 读取 PEM 证书并返回 DER 原始数据；与 X509Certificate.GetRawCertData() 字节一致，
        /// 保证外部锁定证书与握手证书可用同一 SHA-256 指纹比对。
        /// </summary>
        private static byte[] ReadPemToDer(string pemText)
        {
            const string beginMarker = "-----BEGIN CERTIFICATE-----";
            const string endMarker = "-----END CERTIFICATE-----";

            int start = pemText.IndexOf(beginMarker, StringComparison.Ordinal);
            int end = pemText.IndexOf(endMarker, StringComparison.Ordinal);
            if (start < 0 || end <= start)
            {
                return null;
            }

            var base64 = pemText.Substring(start + beginMarker.Length, end - start - beginMarker.Length)
                .Replace("\r", string.Empty)
                .Replace("\n", string.Empty)
                .Replace(" ", string.Empty)
                .Trim();

            return Convert.FromBase64String(base64);
        }

        private static bool IsEnvFlagEnabled(string name)
        {
            var raw = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            var value = raw.Trim();
            return value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1";
        }

        private static string DescribePinnedThumbprint()
        {
            var pinned = s_pinnedThumbprint.Value;
            if (string.IsNullOrEmpty(pinned))
            {
                return "无";
            }

            return pinned.Substring(0, Math.Min(16, pinned.Length));
        }

        private static void LogOnce(string key, string message, LogLevel level)
        {
            lock (s_logLock)
            {
                if (!s_loggedOnce.Add(key))
                {
                    return;
                }
            }

            switch (level)
            {
                case LogLevel.Warning:
                    Debug.LogWarning(message);
                    break;
                case LogLevel.Error:
                    Debug.LogError(message);
                    break;
                default:
                    Debug.Log(message);
                    break;
            }
        }
    }
}
