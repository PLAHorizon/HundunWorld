using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Horizon.Game.GengDi.Enums;
using Newtonsoft.Json;

namespace Horizon.Game.GengDi.Models
{
    public class GameInfo : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        [LiteDB.BsonId]
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string CoverImage { get; set; }
        public string IconImage { get; set; }
        public string Developer { get; set; }
        public string Publisher { get; set; }
        public DateTime ReleaseDate { get; set; }
        public string Category { get; set; }
        public string PopularityBadge { get; set; }
        public int OnlinePlayerCount { get; set; }
        public long PassportLoginCount { get; set; }
        public long CharacterEnterCount { get; set; }
        public bool IsInstalled { get; set; }
        public string InstallationPath { get; set; }
        public string Version { get; set; }
        public bool IsUpdatable { get; set; }
        public string UpdateVersion { get; set; }

        /// <summary>
        /// 是否属于官方推荐游戏列表。仅推荐游戏允许下载 / 安装，运行期由 <c>GameService</c> 做防御式校验。
        /// </summary>
        public bool IsRecommended { get; set; }

        /// <summary>
        /// 推荐游戏的规范下载 URL（安装包），由服务端推送到客户端并持久化。
        /// </summary>
        public string DownloadUrl { get; set; }

        /// <summary>
        /// 服务端上报的最新版本号，用于比较是否有可更新内容。
        /// </summary>
        public string LatestVersion { get; set; }

        /// <summary>
        /// 最近一次从服务端拉取更新信息的 UTC 时间。
        /// </summary>
        public DateTime? LastUpdateCheckUtc { get; set; }

        /// <summary>
        /// 游戏生命周期状态，UI 上"启动/更新/卸载"按钮可用性的唯一来源。
        /// </summary>
        private GameLifecycleState _state = GameLifecycleState.NotInstalled;
        public GameLifecycleState State
        {
            get => _state;
            set
            {
                if (_state != value)
                {
                    _state = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanInstall));
                    OnPropertyChanged(nameof(CanStart));
                    OnPropertyChanged(nameof(CanUpdate));
                    OnPropertyChanged(nameof(CanUninstall));
                    OnPropertyChanged(nameof(IsUpdating));
                    OnPropertyChanged(nameof(ShowInstalledBadge));
                    OnPropertyChanged(nameof(ShowNotInstalledBadge));
                }
            }
        }

        /// <summary>
        /// 服务端推送"有更新"标识，驱动 UI 上更新按钮的红点角标；用户成功更新到最新版本后清除。
        /// </summary>
        public bool HasPendingUpdate { get; set; }

        /// <summary>
        /// 最近一次下载/安装/更新失败原因，供 UI 提示用户并指导重试。
        /// </summary>
        public string LastOperationError { get; set; }

        /// <summary>
        /// 可选的注册表卸载项路径（例如 <c>HKCU\Software\HundunWorld\InstalledGames\{GameId}</c>），卸载时会被清理。
        /// </summary>
        public string RegistryKey { get; set; }

        /// <summary>
        /// 服务端游戏 ID（用于网关鉴权与游戏用户注册）
        /// </summary>
        public int GameId { get; set; } = 1;

        /// <summary>
        /// 应用类型（默认 369 = Game）
        /// </summary>
        public int AppType { get; set; } = 369;

        /// <summary>
        /// 大区 ID
        /// </summary>
        public int AreaId { get; set; } = 1;

        /// <summary>
        /// 服务器 ID
        /// </summary>
        public int ServerId { get; set; } = 1;

        /// <summary>
        /// 游戏可执行文件名（相对于 InstallationPath；不含扩展名）。
        /// 为空时以 Id 作为默认值。
        /// </summary>
        public string ExecutableName { get; set; }

        /// <summary>
        /// 有效游戏 ID。当 GameId 为 0 时（旧数据库记录），回退到将 Id 解析为整数。
        /// </summary>
        [LiteDB.BsonIgnore]
        public string DisplayIcon => string.IsNullOrWhiteSpace(IconImage) ? CoverImage : IconImage;

        [LiteDB.BsonIgnore]
        public bool HasPopularityBadge => !string.IsNullOrWhiteSpace(PopularityBadge);

        [LiteDB.BsonIgnore]
        public string OnlinePlayerCountText => FormatOnlinePlayerCount(OnlinePlayerCount);

        [LiteDB.BsonIgnore]
        public long TotalPlayCount => CalculateWeightedPlayCount(PassportLoginCount, CharacterEnterCount);

        [LiteDB.BsonIgnore]
        public string TotalPlayCountText => TotalPlayCount.ToString();

        [LiteDB.BsonIgnore]
        public int EffectiveGameId => GameId != 0 ? GameId : (int.TryParse(Id, out var parsed) ? parsed : 0);

        /// <summary>
        /// 下载/安装按钮是否可用：仅允许对推荐游戏且当前未安装的条目执行。
        /// </summary>
        [LiteDB.BsonIgnore]
        public bool CanInstall => IsRecommended
            && (State == GameLifecycleState.NotInstalled || State == GameLifecycleState.Failed);

        [LiteDB.BsonIgnore]
        public bool HasLastOperationError => !string.IsNullOrWhiteSpace(LastOperationError);

        /// <summary>启动按钮是否可用：仅在安装完成且状态为 Installed 时；State 为唯一权威来源。</summary>
        [LiteDB.BsonIgnore]
        public bool CanStart => State == GameLifecycleState.Installed;

        /// <summary>更新按钮是否可用：已安装状态下。有 HasPendingUpdate 时 UI 叠加红点。</summary>
        [LiteDB.BsonIgnore]
        public bool CanUpdate => State == GameLifecycleState.Installed;

        /// <summary>卸载按钮是否可用：仅在 Installed 状态下允许。</summary>
        [LiteDB.BsonIgnore]
        public bool CanUninstall => State == GameLifecycleState.Installed;

        /// <summary>是否正在更新中（State == Updating），驱动 UI 更新中 badge 和进度条。</summary>
        [LiteDB.BsonIgnore]
        public bool IsUpdating => State == GameLifecycleState.Updating;

        /// <summary>UI badge: 显示"已安装"（已安装且非更新中）</summary>
        [LiteDB.BsonIgnore]
        public bool ShowInstalledBadge => IsInstalled && State != GameLifecycleState.Updating;

        /// <summary>UI badge: 显示"未安装"（未安装且非更新中）</summary>
        [LiteDB.BsonIgnore]
        public bool ShowNotInstalledBadge => !IsInstalled && State != GameLifecycleState.Updating;

        public string ScreenshotsJson { get; set; } = JsonConvert.SerializeObject(new List<string>());
        public string VideosJson { get; set; } = JsonConvert.SerializeObject(new List<string>());

        [LiteDB.BsonIgnore]
        public List<string> Screenshots
        {
            get => JsonConvert.DeserializeObject<List<string>>(ScreenshotsJson);
            set => ScreenshotsJson = JsonConvert.SerializeObject(value);
        }

        [LiteDB.BsonIgnore]
        public List<string> Videos
        {
            get => JsonConvert.DeserializeObject<List<string>>(VideosJson);
            set => VideosJson = JsonConvert.SerializeObject(value);
        }

        public static string FormatOnlinePlayerCount(int onlinePlayerCount)
        {
            if (onlinePlayerCount <= 0)
            {
                return "0";
            }

            if (onlinePlayerCount >= 10000)
            {
                return "9k+";
            }

            if (onlinePlayerCount >= 1000)
            {
                return "999+";
            }

            return "99+";
        }

        public static long CalculateWeightedPlayCount(long passportLoginCount, long characterEnterCount)
        {
            var normalizedPassportLoginCount = Math.Max(0, passportLoginCount);
            var normalizedCharacterEnterCount = Math.Max(0, characterEnterCount);

            return (long)Math.Round(
                (normalizedPassportLoginCount + normalizedCharacterEnterCount) / 2m,
                MidpointRounding.AwayFromZero);
        }
    }
}
