using System;
using System.Windows.Input;
using Avalonia.Media;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    /// <summary>
    /// 通知列表项视图模型，对应设计稿 notif-item。
    /// 承载标题、描述、分类 chip、时间戳、图标几何、状态色、已读/未读态、操作按钮。
    /// </summary>
    public sealed class NotificationItemViewModel : ViewModelBase
    {
        private bool _isRead;

        public NotificationItemViewModel(
            string title,
            string description,
            string category,
            string timestamp,
            string iconGeometryKey,
            string iconColorBrushKey,
            string iconSurfaceBrushKey,
            string categoryChipBackgroundKey,
            string categoryChipForegroundKey,
            bool isRead,
            bool hasActionButtons = false)
        {
            Title = title;
            Description = description;
            Category = category;
            Timestamp = timestamp;
            IconGeometryKey = iconGeometryKey;
            IconColorBrushKey = iconColorBrushKey;
            IconSurfaceBrushKey = iconSurfaceBrushKey;
            CategoryChipBackgroundKey = categoryChipBackgroundKey;
            CategoryChipForegroundKey = categoryChipForegroundKey;
            _isRead = isRead;
            HasActionButtons = hasActionButtons;
        }

        /// <summary>通知标题（未读态 SemiBold + foreground，已读态 medium + muted-foreground）</summary>
        public string Title { get; }

        /// <summary>通知描述文字（12px muted-foreground）</summary>
        public string Description { get; }

        /// <summary>分类标签文字（系统/好友/花卉/音乐/活动）</summary>
        public string Category { get; }

        /// <summary>时间戳显示文字（11px mono muted-foreground）</summary>
        public string Timestamp { get; }

        /// <summary>Lucide 图标几何资源 key（如 LucideMegaphoneGeometry）</summary>
        public string IconGeometryKey { get; }

        /// <summary>图标颜色画刷资源 key（如 GdInfoBrush）</summary>
        public string IconColorBrushKey { get; }

        /// <summary>图标背景画刷资源 key（如 GdInfoSurfaceBrush）</summary>
        public string IconSurfaceBrushKey { get; }

        /// <summary>分类 chip 背景画刷资源 key</summary>
        public string CategoryChipBackgroundKey { get; }

        /// <summary>分类 chip 文字画刷资源 key</summary>
        public string CategoryChipForegroundKey { get; }

        /// <summary>是否已读（已读项 opacity 0.6 弱化）</summary>
        public bool IsRead
        {
            get => _isRead;
            set => SetProperty(ref _isRead, value);
        }

        /// <summary>是否未读</summary>
        public bool IsUnread => !IsRead;

        /// <summary>是否显示操作按钮（如好友申请的 接受/忽略）</summary>
        public bool HasActionButtons { get; }

        /// <summary>标题字重：未读 SemiBold，已读 Medium</summary>
        public string TitleFontWeight => IsRead ? "Medium" : "SemiBold";

        /// <summary>标题颜色：未读 foreground，已读 muted-foreground</summary>
        public string TitleForegroundKey => IsRead ? "GdMutedForegroundBrush" : "GdForegroundBrush";

        /// <summary>图标颜色：已读态强制 muted-foreground</summary>
        public string EffectiveIconColorBrushKey => IsRead ? "GdMutedForegroundBrush" : IconColorBrushKey;

        /// <summary>图标背景：已读态强制 muted</summary>
        public string EffectiveIconSurfaceBrushKey => IsRead ? "GdMutedBrush" : IconSurfaceBrushKey;

        /// <summary>
        /// 创建设计稿示例数据（7条：5未读 + 2已读）。
        /// </summary>
        public static NotificationItemViewModel[] CreateSampleData()
        {
            return new[]
            {
                // 1. 未读 - 系统 - 版本更新
                new NotificationItemViewModel(
                    title: "星辰远征 v2.4 版本更新已发布",
                    description: "全新赛季「银河纪元」正式开启，点击查看完整更新公告与赛季奖励...",
                    category: "系统",
                    timestamp: "2026-07-26 09:30",
                    iconGeometryKey: "LucideMegaphoneGeometry",
                    iconColorBrushKey: "GdInfoBrush",
                    iconSurfaceBrushKey: "GdInfoSurfaceBrush",
                    categoryChipBackgroundKey: "GdInfoSurfaceBrush",
                    categoryChipForegroundKey: "GdInfoBrush",
                    isRead: false),

                // 2. 未读 - 好友 - 好友申请（含操作按钮）
                new NotificationItemViewModel(
                    title: "云间月 请求添加你为好友",
                    description: "附言：在花卉市场认识的你，一起交流种植心得吧～",
                    category: "好友",
                    timestamp: "2026-07-26 08:15",
                    iconGeometryKey: "LucideUserPlusGeometry",
                    iconColorBrushKey: "GdSuccessBrush",
                    iconSurfaceBrushKey: "GdSuccessSurfaceBrush",
                    categoryChipBackgroundKey: "GdSuccessSurfaceBrush",
                    categoryChipForegroundKey: "GdSuccessBrush",
                    isRead: false,
                    hasActionButtons: true),

                // 3. 未读 - 花卉 - 价格预警
                new NotificationItemViewModel(
                    title: "红玫瑰价格上涨 12%，已触发价格预警",
                    description: "你关注的红玫瑰当前均价 ¥58.00，高于设定阈值 ¥52.00",
                    category: "花卉",
                    timestamp: "2026-07-25 16:42",
                    iconGeometryKey: "LucideTrendingUpGeometry",
                    iconColorBrushKey: "GdWarningBrush",
                    iconSurfaceBrushKey: "GdWarningSurfaceBrush",
                    categoryChipBackgroundKey: "GdWarningSurfaceBrush",
                    categoryChipForegroundKey: "GdWarningBrush",
                    isRead: false),

                // 4. 未读 - 音乐 - 歌单更新
                new NotificationItemViewModel(
                    title: "歌单「田园午后」有 3 首新歌更新",
                    description: "由 风信子 分享的歌单新增了适合种植时聆听的轻音乐...",
                    category: "音乐",
                    timestamp: "2026-07-25 11:20",
                    iconGeometryKey: "LucideMusicGeometry",
                    iconColorBrushKey: "GdInfoBrush",
                    iconSurfaceBrushKey: "GdInfoSurfaceBrush",
                    categoryChipBackgroundKey: "GdSecondaryBrush",
                    categoryChipForegroundKey: "GdSecondaryForegroundBrush",
                    isRead: false),

                // 5. 未读 - 系统 - 下载暂停
                new NotificationItemViewModel(
                    title: "深空突围 下载任务已暂停",
                    description: "由于网络异常，下载已自动暂停，请检查网络后手动继续",
                    category: "系统",
                    timestamp: "2026-07-24 20:08",
                    iconGeometryKey: "LucideAlertTriangleGeometry",
                    iconColorBrushKey: "GdErrorBrush",
                    iconSurfaceBrushKey: "GdErrorSurfaceBrush",
                    categoryChipBackgroundKey: "GdInfoSurfaceBrush",
                    categoryChipForegroundKey: "GdInfoBrush",
                    isRead: false),

                // 6. 已读 - 活动 - 奖励发放
                new NotificationItemViewModel(
                    title: "夏日花卉庆典活动奖励已发放",
                    description: "限定花束头像框已发放至你的账户，请前往资料页查看",
                    category: "活动",
                    timestamp: "2026-07-23 18:00",
                    iconGeometryKey: "LucideGiftGeometry",
                    iconColorBrushKey: "GdMutedForegroundBrush",
                    iconSurfaceBrushKey: "GdMutedBrush",
                    categoryChipBackgroundKey: "GdSuccessSurfaceBrush",
                    categoryChipForegroundKey: "GdSuccessBrush",
                    isRead: true),

                // 7. 已读 - 好友 - 动态回复
                new NotificationItemViewModel(
                    title: "风信子 回复了你的动态",
                    description: "「这批百合开得真好，请问用的什么肥料？」",
                    category: "好友",
                    timestamp: "2026-07-22 14:30",
                    iconGeometryKey: "LucideMessageCircleGeometry",
                    iconColorBrushKey: "GdMutedForegroundBrush",
                    iconSurfaceBrushKey: "GdMutedBrush",
                    categoryChipBackgroundKey: "GdSuccessSurfaceBrush",
                    categoryChipForegroundKey: "GdSuccessBrush",
                    isRead: true),
            };
        }
    }
}
