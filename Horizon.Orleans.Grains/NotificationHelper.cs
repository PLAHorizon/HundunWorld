using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 通知类型枚举
    /// </summary>
    public enum NotificationType
    {
        /// <summary>首杀通知</summary>
        FirstKill = 1,
        /// <summary>活动开始通知</summary>
        ActivityStart = 2,
        /// <summary>活动结束通知</summary>
        ActivityEnd = 3,
        /// <summary>世界BOSS出现</summary>
        WorldBossSpawn = 4,
        /// <summary>玩家成就</summary>
        Achievement = 5
    }

    /// <summary>
    /// 通知消息构建结果
    /// </summary>
    public class NotificationMessage
    {
        public NotificationType Type { get; set; }
        public string Title { get; set; } = "";
        public string Content { get; set; } = "";
        public long Timestamp { get; set; }
        public Dictionary<string, string> ExtraData { get; set; } = new();
    }

    /// <summary>
    /// 通知辅助类 - 构建各类系统通知消息
    /// </summary>
    public static class NotificationHelper
    {
        /// <summary>
        /// 构建首杀通知消息
        /// </summary>
        public static NotificationMessage BuildFirstKillNotification(string playerName, string bossName, long timestamp = 0)
        {
            playerName ??= "";
            bossName ??= "";

            return new NotificationMessage
            {
                Type = NotificationType.FirstKill,
                Title = "首杀通知",
                Content = $"恭喜【{playerName}】成功首杀【{bossName}】！",
                Timestamp = timestamp > 0 ? timestamp : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }

        /// <summary>
        /// 构建活动开始通知
        /// </summary>
        public static NotificationMessage BuildActivityStartNotification(string activityName, string description, long durationMinutes, long timestamp = 0)
        {
            activityName ??= "";
            description ??= "";

            return new NotificationMessage
            {
                Type = NotificationType.ActivityStart,
                Title = "活动通知",
                Content = $"活动【{activityName}】已开始！{description} 持续{durationMinutes}分钟。",
                Timestamp = timestamp > 0 ? timestamp : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ExtraData = new Dictionary<string, string>
                {
                    { "ActivityName", activityName },
                    { "Duration", durationMinutes.ToString() }
                }
            };
        }

        /// <summary>
        /// 构建活动结束通知
        /// </summary>
        public static NotificationMessage BuildActivityEndNotification(string activityName, long timestamp = 0)
        {
            activityName ??= "";

            return new NotificationMessage
            {
                Type = NotificationType.ActivityEnd,
                Title = "活动通知",
                Content = $"活动【{activityName}】已结束！",
                Timestamp = timestamp > 0 ? timestamp : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }

        /// <summary>
        /// 构建世界BOSS出现通知
        /// </summary>
        public static NotificationMessage BuildWorldBossSpawnNotification(string bossName, string location, long timestamp = 0)
        {
            bossName ??= "";
            location ??= "";

            return new NotificationMessage
            {
                Type = NotificationType.WorldBossSpawn,
                Title = "世界BOSS",
                Content = $"世界BOSS【{bossName}】已在【{location}】出现！",
                Timestamp = timestamp > 0 ? timestamp : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ExtraData = new Dictionary<string, string>
                {
                    { "BossName", bossName },
                    { "Location", location }
                }
            };
        }

        /// <summary>
        /// 构建成就通知
        /// </summary>
        public static NotificationMessage BuildAchievementNotification(string playerName, string achievementName, long timestamp = 0)
        {
            playerName ??= "";
            achievementName ??= "";

            return new NotificationMessage
            {
                Type = NotificationType.Achievement,
                Title = "成就通知",
                Content = $"恭喜【{playerName}】达成成就【{achievementName}】！",
                Timestamp = timestamp > 0 ? timestamp : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }

        /// <summary>
        /// 将通知消息转换为ChatMessage用于系统频道广播
        /// </summary>
        public static ChatMessage ToChatMessage(NotificationMessage notification)
        {
            return new ChatMessage
            {
                ChannelType = ChatChannel.System,
                IsSystemMessage = true,
                Content = $"[{notification.Title}] {notification.Content}",
                SenderId = 0,
                SenderName = "系统",
                MessageId = Guid.NewGuid().ToString(),
                Timestamp = notification.Timestamp > 0 ? notification.Timestamp : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }
    }
}
