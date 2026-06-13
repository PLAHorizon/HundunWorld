using System;

using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.Services
{
    internal static class ImIdentity
    {
        public static string ResolvePassportId(User user)
        {
            return user?.PassportId?.Trim() ?? string.Empty;
        }

        public static bool TryResolveUserId(User user, out ulong userId)
        {
            return TryResolveUserId(ResolvePassportId(user), out userId);
        }

        public static bool TryResolveUserId(string passportId, out ulong userId)
        {
            userId = 0;
            return !string.IsNullOrWhiteSpace(passportId)
                && ulong.TryParse(passportId.Trim(), out userId)
                && userId > 0;
        }

        public static ulong ResolveUserId(User user)
        {
            var passportId = ResolvePassportId(user);
            if (TryResolveUserId(passportId, out var userId))
            {
                return userId;
            }

            throw new InvalidOperationException("IM 用户ID使用 PassportId，且 PassportId 必须是正整数。");
        }
    }
}