using System;
using System.Collections.Generic;
using System.IO;
using FlaxEngine;

namespace HundunWorld.Game.Services
{
    public class HorizonGameConfig
    {
        public AuthSection Auth { get; set; } = new AuthSection();
        public UserSection User { get; set; } = new UserSection();
        public GameSection Game { get; set; } = new GameSection();
        public GatewaySection GameGateway { get; set; } = new GatewaySection();
        public GatewaySection IMGateway { get; set; } = new GatewaySection();

        public bool IsValid => !string.IsNullOrEmpty(Auth?.AuthToken) && !string.IsNullOrEmpty(User?.PassportId);

        public class AuthSection
        {
            public string AuthToken { get; set; } = string.Empty;
        }

        public class UserSection
        {
            public string PassportId { get; set; } = string.Empty;
            public long UserId { get; set; }
        }

        public class GameSection
        {
            public int GameId { get; set; }
            public int AppType { get; set; }
            public int AreaId { get; set; }
            public int ServerId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Version { get; set; } = string.Empty;
            public string InstallationPath { get; set; } = string.Empty;
        }

        public class GatewaySection
        {
            public string Type { get; set; } = string.Empty;
            public string Host { get; set; } = string.Empty;
            public int Port { get; set; }
            public string InstanceId { get; set; } = string.Empty;
            public string PersistHistory { get; set; } = string.Empty;
        }
    }

    public static class HorizonGameIniReader
    {
        private const string IniFileName = "HorizonGame.ini";

        public static HorizonGameConfig TryRead()
        {
            string filePath = Path.Combine(Directory.GetCurrentDirectory(), IniFileName);

            if (!File.Exists(filePath))
            {
                Debug.Log($"[HorizonGameIni] 配置文件不存在: {filePath}");
                return null;
            }

            try
            {
                string[] lines = File.ReadAllLines(filePath);
                var config = ParseIni(lines);

                if (config == null)
                {
                    return null;
                }

                if (string.IsNullOrEmpty(config.Auth?.AuthToken))
                {
                    Debug.LogWarning("[HorizonGameIni] AuthToken 为空，配置无效");
                    return null;
                }

                Debug.Log($"[HorizonGameIni] 配置读取成功: Game={config.Game?.Name}, User={config.User?.PassportId}");
                return config;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HorizonGameIni] 读取配置文件失败: {ex.Message}");
                return null;
            }
        }

        private static HorizonGameConfig ParseIni(string[] lines)
        {
            var config = new HorizonGameConfig();
            string currentSection = null;

            var sectionMap = new Dictionary<string, Action<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                { "Auth", (key, value) => ApplyAuth(config, key, value) },
                { "User", (key, value) => ApplyUser(config, key, value) },
                { "Game", (key, value) => ApplyGame(config, key, value) },
                { "GameGateway", (key, value) => ApplyGameGateway(config, key, value) },
                { "IMGateway", (key, value) => ApplyIMGateway(config, key, value) },
            };

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();

                if (string.IsNullOrEmpty(line) || line.StartsWith(";"))
                {
                    continue;
                }

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    currentSection = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                int eqIndex = line.IndexOf('=');
                if (eqIndex <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, eqIndex).Trim();
                string value = line.Substring(eqIndex + 1).Trim();

                if (currentSection != null && sectionMap.TryGetValue(currentSection, out var applier))
                {
                    applier(key, value);
                }
            }

            return config;
        }

        private static void ApplyAuth(HorizonGameConfig config, string key, string value)
        {
            if (string.Equals(key, "AuthToken", StringComparison.OrdinalIgnoreCase))
            {
                config.Auth.AuthToken = value;
            }
        }

        private static void ApplyUser(HorizonGameConfig config, string key, string value)
        {
            if (string.Equals(key, "PassportId", StringComparison.OrdinalIgnoreCase))
            {
                config.User.PassportId = value;
            }
            else if (string.Equals(key, "UserId", StringComparison.OrdinalIgnoreCase))
            {
                if (long.TryParse(value, out long userId))
                {
                    config.User.UserId = userId;
                }
            }
        }

        private static void ApplyGame(HorizonGameConfig config, string key, string value)
        {
            if (string.Equals(key, "GameId", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, out int v)) config.Game.GameId = v;
            }
            else if (string.Equals(key, "AppType", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, out int v)) config.Game.AppType = v;
            }
            else if (string.Equals(key, "AreaId", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, out int v)) config.Game.AreaId = v;
            }
            else if (string.Equals(key, "ServerId", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, out int v)) config.Game.ServerId = v;
            }
            else if (string.Equals(key, "Name", StringComparison.OrdinalIgnoreCase))
            {
                config.Game.Name = value;
            }
            else if (string.Equals(key, "Version", StringComparison.OrdinalIgnoreCase))
            {
                config.Game.Version = value;
            }
            else if (string.Equals(key, "InstallationPath", StringComparison.OrdinalIgnoreCase))
            {
                config.Game.InstallationPath = value;
            }
        }

        private static void ApplyGameGateway(HorizonGameConfig config, string key, string value)
        {
            ApplyGateway(config.GameGateway, key, value);
        }

        private static void ApplyIMGateway(HorizonGameConfig config, string key, string value)
        {
            ApplyGateway(config.IMGateway, key, value);
        }

        private static void ApplyGateway(HorizonGameConfig.GatewaySection gateway, string key, string value)
        {
            if (string.Equals(key, "Type", StringComparison.OrdinalIgnoreCase))
            {
                gateway.Type = value;
            }
            else if (string.Equals(key, "Host", StringComparison.OrdinalIgnoreCase))
            {
                gateway.Host = value;
            }
            else if (string.Equals(key, "Port", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, out int v)) gateway.Port = v;
            }
            else if (string.Equals(key, "InstanceId", StringComparison.OrdinalIgnoreCase))
            {
                gateway.InstanceId = value;
            }
            else if (string.Equals(key, "PersistHistory", StringComparison.OrdinalIgnoreCase))
            {
                gateway.PersistHistory = value;
            }
        }
    }
}
