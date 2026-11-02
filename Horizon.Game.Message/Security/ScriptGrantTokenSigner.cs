using System;
using System.Security.Cryptography;
using System.Text;

namespace Horizon.Game.Message.Security
{
    /// <summary>
    /// <see cref="Network.ScriptGrantToken"/> 的 HMAC-SHA256 签名与校验。
    /// <para>
    /// 存在的理由：DS 是 60Hz 独立进程，tick 内不能做跨进程 RPC，所以 Grain 的裁决结果必须"自证"——
    /// 令牌由 Grain 用共享密钥签发，DS 用同一密钥验签。<b>DS 因此不需要、也不允许参与"谁有权"的判定</b>。
    /// </para>
    /// <para>
    /// 规范字节序（<see cref="Canonicalize"/>）必须与签发端完全一致：任何字段顺序/分隔符改动都会
    /// 使历史令牌全部验签失败，属破坏性变更，需同时升 <see cref="Version"/>。
    /// </para>
    /// </summary>
    public static class ScriptGrantTokenSigner
    {
        /// <summary>
        /// 签名格式版本，参与规范字节首字段。
        /// <para>v2：规范字节加入 <c>RequestId</c>。任何字段增删都必须同时升此版本，
        /// 否则会出现"一边签一边验不过"的静默不兼容。</para>
        /// </summary>
        public const int Version = 2;

        private const char Separator = '|';

        /// <summary>
        /// 产出令牌的规范字节（不含签名字段本身）。
        /// </summary>
        public static byte[] Canonicalize(Network.ScriptGrantToken token)
        {
            if (token == null) throw new ArgumentNullException(nameof(token));

            var sb = new StringBuilder(128);
            sb.Append(Version).Append(Separator);
            sb.Append(token.Nonce).Append(Separator);
            sb.Append(token.OperatorCharacterId).Append(Separator);
            sb.Append((byte)token.Op).Append(Separator);
            sb.Append((byte)token.Kind).Append(Separator);
            sb.Append(token.TemplateId).Append(Separator);
            sb.Append(token.TargetShardId).Append(Separator);
            // 坐标用 R("C") 固定格式：避免不同区域设置下小数点差异导致跨进程验签失败。
            sb.Append(token.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(Separator);
            sb.Append(token.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(Separator);
            sb.Append(token.Z.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(Separator);
            sb.Append(token.Count).Append(Separator);
            sb.Append(token.TtlSeconds).Append(Separator);
            sb.Append(token.Radius.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(Separator);
            sb.Append((byte)token.TriggerMode).Append(Separator);
            sb.Append(token.CooldownSeconds).Append(Separator);
            sb.Append(token.TargetEntityId).Append(Separator);
            sb.Append(token.RequestId).Append(Separator);
            sb.Append(token.IssuedAtUnixMs).Append(Separator);
            sb.Append(token.ExpiresAtUnixMs);
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        /// <summary>计算签名（返回 32 字节 HMAC-SHA256）。</summary>
        public static byte[] ComputeSignature(Network.ScriptGrantToken token, byte[] key)
        {
            var payload = Canonicalize(token);
            using var hmac = new HMACSHA256(key ?? throw new ArgumentNullException(nameof(key)));
            return hmac.ComputeHash(payload);
        }

        /// <summary>
        /// 就地签发：补 <see cref="Network.ScriptGrantToken.Signature"/>。
        /// </summary>
        /// <param name="token">待签发令牌，字段必须已定稿。</param>
        /// <param name="key">共享密钥，长度建议 ≥32 字节。</param>
        /// <param name="nowUnixMs">签发时刻（Unix 毫秒）。由调用方传入以便测试确定性。</param>
        /// <param name="ttlSeconds">有效期秒数。</param>
        public static void Sign(
            Network.ScriptGrantToken token,
            byte[] key,
            long nowUnixMs,
            int ttlSeconds)
        {
            token.IssuedAtUnixMs = nowUnixMs;
            token.ExpiresAtUnixMs = nowUnixMs + (long)ttlSeconds * 1000L;
            token.Signature = ComputeSignature(token, key);
        }

        /// <summary>
        /// 完整校验：签名（定长比较）+ 过期。
        /// </summary>
        /// <param name="token">待校验令牌。</param>
        /// <param name="key">共享密钥。</param>
        /// <param name="nowUnixMs">当前时刻（Unix 毫秒）。</param>
        /// <param name="reason">失败原因。</param>
        /// <returns>通过为 true。</returns>
        public static bool Validate(
            Network.ScriptGrantToken token,
            byte[] key,
            long nowUnixMs,
            out string reason)
        {
            reason = string.Empty;

            if (token == null)
            {
                reason = "null-token";
                return false;
            }

            if (key == null || key.Length == 0)
            {
                // fail-closed：没有密钥就不可能验签，绝不能当作"跳过校验"。
                reason = "missing-key";
                return false;
            }

            if (token.Signature == null || token.Signature.Length != 32)
            {
                reason = "bad-signature-length";
                return false;
            }

            var expected = ComputeSignature(token, key);
            if (!CryptographicOperations.FixedTimeEquals(expected, token.Signature))
            {
                reason = "signature-mismatch";
                return false;
            }

            if (token.ExpiresAtUnixMs <= nowUnixMs)
            {
                reason = "expired";
                return false;
            }

            if (token.IssuedAtUnixMs > nowUnixMs)
            {
                // 签发时刻在未来：时钟严重偏移或伪造，拒绝。
                reason = "issued-in-future";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 密钥指纹（SHA-256 前 4 字节，大写十六进制）。仅用于启动日志互相核对，<b>绝不用于还原密钥</b>。
        /// <para>
        /// 之所以需要它：签发在 Grain、自检在网关、验签在 DS，三处读同名配置项。任一处抄漏或写成不同值，
        /// 失败表现只有 <c>signature-mismatch</c> 一个字面，操作端无从判断是哪进程错位。
        /// 三行启动日志把指纹并排打出来，比对即可完成定位。
        /// </para>
        /// </summary>
        public static string KeyFingerprint(string? signingKeyBase64)
        {
            if (string.IsNullOrWhiteSpace(signingKeyBase64)) return "(missing)";

            byte[] key;
            try
            {
                key = Convert.FromBase64String(signingKeyBase64);
            }
            catch (FormatException)
            {
                return "(not-base64)";
            }

            return Convert.ToHexString(SHA256.HashData(key).AsSpan(0, 4));
        }
    }
}
