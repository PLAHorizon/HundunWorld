using System;
using System.Text;

namespace Horizon.Game.GengDi.Core.Services.Security;

/// <summary>
/// 本地敏感凭证保护（密码/令牌落盘前加密，读取时解密）。
/// <para>
/// 修复（安全）：原 <see cref="Database.LiteDataContext.PassportInfo.Password"/>/<c>Token</c> 明文存入 LiteDB。
/// 桌面端（net10.0，Windows）使用 DPAPI（<c>ProtectedData</c>，CurrentUser 作用域）加密，密文加 <c>dpapi:</c> 前缀；
/// 读取时识别前缀解密，无前缀的旧明文数据原样返回（迁移兼容，下次保存时自动重加密）。
/// 非桌面端（Android/iOS）暂不回退加密——需平台 Keystore/Keychain，见待办。
/// </para>
/// </summary>
public static class CredentialProtector
{
    private const string Prefix = "dpapi:";

    /// <summary>加密明文（空串原样返回）；失败回退明文，绝不因加密失败丢数据。</summary>
    public static string Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return string.Empty;
#if DESKTOP
        try
        {
            var bytes = Encoding.UTF8.GetBytes(plaintext);
            var protectedBytes = System.Security.Cryptography.ProtectedData.Protect(
                bytes, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(protectedBytes);
        }
        catch
        {
            // 加密失败（如非 Windows 运行时）：回退明文，保证功能可用。
            return plaintext;
        }
#else
        // Android/iOS：无 DPAPI，暂不加密（需 Keystore/Keychain，待办）。
        return plaintext;
#endif
    }

    /// <summary>解密存储值；无 <c>dpapi:</c> 前缀视为旧明文，原样返回（迁移兼容）。</summary>
    public static string Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return string.Empty;
#if DESKTOP
        if (stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            try
            {
                var protectedBytes = Convert.FromBase64String(stored.Substring(Prefix.Length));
                var bytes = System.Security.Cryptography.ProtectedData.Unprotect(
                    protectedBytes, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                // 解密失败（换用户/换机器/数据损坏）：返回原值，避免抛异常中断登录。
                return stored;
            }
        }
#endif
        return stored;
    }
}
