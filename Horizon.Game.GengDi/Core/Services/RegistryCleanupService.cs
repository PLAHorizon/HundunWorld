using System;
using System.Reflection;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.Services
{
    /// <summary>
    /// Windows 注册表卸载项管理。非 Windows 平台下所有方法均为 no-op，以保证同一份二进制可以在 CI Linux 上构建和测试。
    ///
    /// 注册路径：<c>HKCU\Software\HundunWorld\InstalledGames\{GameId}</c>。
    ///
    /// 实现说明：通过反射访问 <c>Microsoft.Win32.Registry</c>（Windows 专属），避免为非 Windows 目标引入包依赖。
    /// 在 Windows 下该类型始终可用；若反射失败（例如裁剪场景），方法会静默回退为 no-op，不阻塞主流程。
    /// </summary>
    public static class RegistryCleanupService
    {
        private const string BaseKeyPath = @"Software\HundunWorld\InstalledGames";

        /// <summary>
        /// 写入 / 更新游戏的卸载注册项。调用方不必判断平台，内部已 guard。
        /// 写入成功后把 RegistryKey 回填到 <paramref name="game"/>，用于后续卸载时精确清理。
        /// </summary>
        public static void Register(GameInfo game)
        {
            if (game == null) return;
            if (!OperatingSystem.IsWindows()) return;

            var subKeyPath = BuildKeyPath(game);
            if (subKeyPath == null) return;

            if (TryWriteRegistry(subKeyPath, game))
            {
                game.RegistryKey = @"HKEY_CURRENT_USER\" + subKeyPath;
            }
        }

        /// <summary>
        /// 删除游戏的卸载注册项。失败时静默吞掉异常（例如权限不足），以避免阻塞卸载流程。
        /// </summary>
        public static void Remove(GameInfo game)
        {
            if (game == null) return;
            if (!OperatingSystem.IsWindows()) return;

            var subKeyPath = BuildKeyPath(game);
            if (subKeyPath == null) return;

            TryDeleteRegistry(subKeyPath);
            game.RegistryKey = null;
        }

        private static string BuildKeyPath(GameInfo game)
        {
            var id = !string.IsNullOrWhiteSpace(game.Id) ? game.Id : game.Name;
            if (string.IsNullOrWhiteSpace(id)) return null;
            // 仅保留路径安全字符；避免把反斜杠注入到 key path 造成意外层级。
            var safe = id.Replace('\\', '_').Replace('/', '_');
            return BaseKeyPath + "\\" + safe;
        }

        private static bool TryWriteRegistry(string subKeyPath, GameInfo game)
        {
            try
            {
                var registryType = Type.GetType("Microsoft.Win32.Registry, Microsoft.Win32.Registry", throwOnError: false)
                    ?? Type.GetType("Microsoft.Win32.Registry, System.Private.CoreLib", throwOnError: false)
                    ?? Type.GetType("Microsoft.Win32.Registry", throwOnError: false);
                if (registryType == null) return false;

                var currentUser = registryType.GetProperty("CurrentUser", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (currentUser == null) return false;

                var keyType = currentUser.GetType();
                var createSubKey = keyType.GetMethod("CreateSubKey", new[] { typeof(string) });
                if (createSubKey == null) return false;

                using var key = createSubKey.Invoke(currentUser, new object[] { subKeyPath }) as IDisposable;
                if (key == null) return false;

                var setValue = key.GetType().GetMethod("SetValue", new[] { typeof(string), typeof(object) });
                if (setValue == null) return false;

                setValue.Invoke(key, new object[] { "DisplayName", game.Name ?? game.Id ?? string.Empty });
                if (!string.IsNullOrEmpty(game.InstallationPath))
                {
                    setValue.Invoke(key, new object[] { "InstallLocation", game.InstallationPath });
                }
                if (!string.IsNullOrEmpty(game.Version))
                {
                    setValue.Invoke(key, new object[] { "DisplayVersion", game.Version });
                }
                if (!string.IsNullOrEmpty(game.Publisher))
                {
                    setValue.Invoke(key, new object[] { "Publisher", game.Publisher });
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void TryDeleteRegistry(string subKeyPath)
        {
            try
            {
                var registryType = Type.GetType("Microsoft.Win32.Registry, Microsoft.Win32.Registry", throwOnError: false)
                    ?? Type.GetType("Microsoft.Win32.Registry, System.Private.CoreLib", throwOnError: false)
                    ?? Type.GetType("Microsoft.Win32.Registry", throwOnError: false);
                if (registryType == null) return;

                var currentUser = registryType.GetProperty("CurrentUser", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (currentUser == null) return;

                var delete = currentUser.GetType().GetMethod(
                    "DeleteSubKeyTree",
                    new[] { typeof(string), typeof(bool) });
                if (delete == null)
                {
                    delete = currentUser.GetType().GetMethod("DeleteSubKeyTree", new[] { typeof(string) });
                    delete?.Invoke(currentUser, new object[] { subKeyPath });
                    return;
                }

                delete.Invoke(currentUser, new object[] { subKeyPath, /*throwOnMissingSubKey*/ false });
            }
            catch
            {
                // 静默忽略：注册表不可用或权限不足时不阻塞卸载主流程。
            }
        }
    }
}
