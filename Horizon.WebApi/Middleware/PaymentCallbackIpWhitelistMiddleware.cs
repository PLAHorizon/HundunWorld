using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Horizon.WebApi.Middleware
{
    public class PaymentCallbackIpWhitelistMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<PaymentCallbackIpWhitelistMiddleware> _logger;
        private static readonly HashSet<string> AlipayCallbackPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "/FlowerPayment/callback/alipay"
        };
        private static readonly HashSet<string> WechatCallbackPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "/FlowerPayment/callback/wechat"
        };
        private static readonly HashSet<string> AlipayIpRanges = new(StringComparer.OrdinalIgnoreCase)
        {
            "110.75.151.0/24",
            "110.75.225.0/24",
            "110.75.227.0/24",
            "110.75.228.0/24",
            "110.75.229.0/24",
            "110.75.230.0/24",
            "110.75.231.0/24",
            "112.124.128.0/24",
            "112.124.129.0/24",
            "112.124.130.0/24",
            "112.124.131.0/24",
            "112.124.132.0/24",
            "112.124.133.0/24",
            "112.124.134.0/24",
            "112.124.135.0/24",
            "114.55.86.0/24",
            "114.55.87.0/24",
            "114.55.88.0/24",
            "114.55.89.0/24",
            "114.55.90.0/24",
            "114.55.91.0/24",
            "115.236.174.0/24",
            "115.236.175.0/24",
            "119.167.128.0/24",
            "119.167.129.0/24",
            "119.167.130.0/24",
            "119.167.131.0/24",
            "119.167.132.0/24",
            "119.167.133.0/24",
            "119.167.134.0/24",
            "119.167.135.0/24",
            "120.26.100.0/24",
            "120.26.101.0/24",
            "120.26.102.0/24",
            "120.26.103.0/24",
            "120.26.104.0/24",
            "120.26.105.0/24",
            "120.26.106.0/24",
            "120.26.107.0/24",
            "121.41.108.0/24",
            "121.41.109.0/24",
            "121.41.110.0/24",
            "121.41.111.0/24",
            "139.129.228.0/24",
            "139.129.229.0/24",
            "139.129.230.0/24",
            "139.129.231.0/24",
            "139.129.232.0/24",
            "139.129.233.0/24",
            "139.129.234.0/24",
            "139.129.235.0/24",
            "139.129.236.0/24",
            "139.129.237.0/24",
            "47.96.0.0/24",
            "47.96.1.0/24",
            "47.96.2.0/24",
            "47.96.3.0/24",
            "47.96.4.0/24",
            "47.96.5.0/24",
            "47.96.6.0/24",
            "47.96.7.0/24",
        };
        private static readonly HashSet<string> WechatIpRanges = new(StringComparer.OrdinalIgnoreCase)
        {
            "101.226.0.0/16",
            "140.207.0.0/16",
            "58.67.0.0/16",
            "183.60.0.0/16",
            "116.128.0.0/24",
            "116.131.0.0/24",
            "116.132.0.0/24",
            "116.133.0.0/24",
            "116.134.0.0/24",
            "116.135.0.0/24",
            "116.136.0.0/24",
            "116.137.0.0/24",
            "116.138.0.0/24",
            "116.139.0.0/24",
            "116.140.0.0/24",
            "116.141.0.0/24",
            "116.142.0.0/24",
            "116.143.0.0/24",
            "116.144.0.0/24",
            "116.145.0.0/24",
            "116.146.0.0/24",
            "116.147.0.0/24",
            "116.148.0.0/24",
            "116.149.0.0/24",
            "116.150.0.0/24",
            "116.151.0.0/24",
        };
        private readonly HashSet<string> _allowedLocalIps = new(StringComparer.OrdinalIgnoreCase)
        {
            "127.0.0.1", "::1"
        };
        private readonly bool _enableIpWhitelist;

        public PaymentCallbackIpWhitelistMiddleware(RequestDelegate next, ILogger<PaymentCallbackIpWhitelistMiddleware> logger, bool enableIpWhitelist = true)
        {
            _next = next;
            _logger = logger;
            _enableIpWhitelist = enableIpWhitelist;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!_enableIpWhitelist)
            {
                await _next(context);
                return;
            }

            var path = context.Request.Path.Value;
            if (path == null)
            {
                await _next(context);
                return;
            }

            var isAlipayCallback = AlipayCallbackPaths.Any(p => path.Contains(p, StringComparison.OrdinalIgnoreCase));
            var isWechatCallback = WechatCallbackPaths.Any(p => path.Contains(p, StringComparison.OrdinalIgnoreCase));

            if (!isAlipayCallback && !isWechatCallback)
            {
                await _next(context);
                return;
            }

            var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "";
            if (_allowedLocalIps.Contains(remoteIp))
            {
                await _next(context);
                return;
            }

            if (isAlipayCallback)
            {
                if (!IsIpInCidrRanges(remoteIp, AlipayIpRanges))
                {
                    _logger.LogWarning("支付宝回调IP不在白名单: IP={IP}, Path={Path}", remoteIp, path);
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
            }

            if (isWechatCallback)
            {
                if (!IsIpInCidrRanges(remoteIp, WechatIpRanges))
                {
                    _logger.LogWarning("微信支付回调IP不在白名单: IP={IP}, Path={Path}", remoteIp, path);
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
            }

            await _next(context);
        }

        private static bool IsIpInCidrRanges(string ip, HashSet<string> cidrRanges)
        {
            if (!IPAddress.TryParse(ip, out var ipAddress))
                return false;

            var ipBytes = ipAddress.GetAddressBytes();
            if (ipBytes.Length != 4) return false;

            var ipInt = BitConverter.ToUInt32(ipBytes.Reverse().ToArray(), 0);

            foreach (var cidr in cidrRanges)
            {
                var parts = cidr.Split('/');
                if (parts.Length != 2) continue;
                if (!IPAddress.TryParse(parts[0], out var networkAddress)) continue;
                if (!int.TryParse(parts[1], out var prefixLength)) continue;

                var networkBytes = networkAddress.GetAddressBytes();
                if (networkBytes.Length != 4) continue;
                var networkInt = BitConverter.ToUInt32(networkBytes.Reverse().ToArray(), 0);

                var mask = prefixLength == 0 ? 0 : ~((1u << (32 - prefixLength)) - 1);
                if ((ipInt & mask) == (networkInt & mask))
                    return true;
            }
            return false;
        }
    }
}
