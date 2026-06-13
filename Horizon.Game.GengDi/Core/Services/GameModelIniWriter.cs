using System;
using System.IO;
using System.Text;
using GameModel = Horizon.Game.GengDi.Models.GameInfo;

namespace Horizon.Game.GengDi.Core.Services
{
    /// <summary>
    /// 将 GameModel 属性序列化为 INI 文件并写入游戏安装目录。
    /// UE5 客户端启动时读取该文件获取服务器连接参数。
    /// </summary>
    public static class GameModelIniWriter
    {
        /// <summary>
        /// INI 文件名（位于游戏安装根目录）
        /// </summary>
        public const string IniFileName = "HorizonGame.ini";

        /// <summary>
        /// 将 GameModel 写入 INI 文件，覆盖已有文件。
        /// </summary>
        /// <param name="game">游戏模型</param>
        /// <param name="passportId">通行证 ID</param>
        /// <param name="authToken">鉴权令牌</param>
        /// <param name="userId">游戏用户 ID</param>
        public static void Write(GameModel game, string passportId, string authToken, long userId)
        {
            if (game == null) throw new ArgumentNullException(nameof(game));
            if (string.IsNullOrWhiteSpace(game.InstallationPath))
                throw new ArgumentException("游戏安装路径不能为空", nameof(game));

            var iniPath = Path.Combine(game.InstallationPath, IniFileName);

            var sb = new StringBuilder();
            sb.AppendLine("; HorizonGame 配置文件 - 由耕地(GengDi)启动器自动生成，请勿手动修改");
            sb.AppendLine($"; 生成时间: {DateTime.UtcNow:O}");
            sb.AppendLine();

            // [Game] 区段 — 游戏基本信息
            sb.AppendLine("[Game]");
            sb.AppendLine($"GameId={game.EffectiveGameId}");
            sb.AppendLine($"AppType={game.AppType}");
            sb.AppendLine($"AreaId={game.AreaId}");
            sb.AppendLine($"ServerId={game.ServerId}");
            sb.AppendLine($"Name={EscapeIniValue(game.Name)}");
            sb.AppendLine($"Version={EscapeIniValue(game.Version)}");
            sb.AppendLine($"InstallationPath={EscapeIniValue(game.InstallationPath)}");
            sb.AppendLine();

            // [User] 区段 — 当前用户信息
            sb.AppendLine("[User]");
            sb.AppendLine($"PassportId={EscapeIniValue(passportId)}");
            sb.AppendLine($"UserId={userId}");
            sb.AppendLine();

            // [Auth] 区段 — 鉴权信息
            sb.AppendLine("[Auth]");
            sb.AppendLine($"AuthToken={EscapeIniValue(authToken)}");
            sb.AppendLine();

            // [GameGateway] 区段 — 游戏同步网关地址，供 UE5 客户端建立游戏同步长连接
            var gameGateway = GatewayDiscoveryService.GameGateway;
            sb.AppendLine("[GameGateway]");
            sb.AppendLine("Type=Game");
            if (gameGateway != null)
            {
                sb.AppendLine($"Host={EscapeIniValue(gameGateway.Host)}");
                sb.AppendLine($"Port={gameGateway.Port}");
                sb.AppendLine($"InstanceId={EscapeIniValue(gameGateway.InstanceId)}");
            }
            else
            {
                sb.AppendLine("Host=");
                sb.AppendLine("Port=0");
                sb.AppendLine("InstanceId=");
            }
            sb.AppendLine();

            // [IMGateway] 区段 — 即时通讯/聊天网关地址；游戏内聊天消息通过该网关实时收发，
            // 不在客户端持久化，游戏关闭后历史聊天不可再次查看。
            var imGateway = GatewayDiscoveryService.ImGateway;
            sb.AppendLine("[IMGateway]");
            sb.AppendLine("Type=IM");
            if (imGateway != null)
            {
                sb.AppendLine($"Host={EscapeIniValue(imGateway.Host)}");
                sb.AppendLine($"Port={imGateway.Port}");
                sb.AppendLine($"InstanceId={EscapeIniValue(imGateway.InstanceId)}");
            }
            else
            {
                sb.AppendLine("Host=");
                sb.AppendLine("Port=0");
                sb.AppendLine("InstanceId=");
            }
            sb.AppendLine("PersistHistory=false");

            // 确保目录存在
            Directory.CreateDirectory(game.InstallationPath);

            File.WriteAllText(iniPath, sb.ToString(), Encoding.UTF8);
        }

        /// <summary>
        /// 对 INI 值进行简单转义（去除换行）
        /// </summary>
        private static string EscapeIniValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\r", "").Replace("\n", "");
        }
    }
}
