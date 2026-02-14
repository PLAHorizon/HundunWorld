using MemoryPack;
using System;

namespace Horizon.Game.Message.Enums
{
   
    
    /// <summary>
    /// 邮件分类枚举
    /// </summary>
    
    public enum MailCategory
    {
        All,        // 全部
        System,     // 系统邮件
        Player,     // 玩家邮件
        Guild,      // 公会邮件
        Reward,     // 奖励邮件
        Notification, // 通知邮件
        Unread,    // 未读邮件
        Read,      // 已读邮件
    }
    
    /// <summary>
    /// 邮件状态枚举
    /// </summary>
    
    public enum MailStatus
    {
        Unread,     // 未读
        Read,       // 已读
        Replied,    // 已回复
        Claimed,    // 已领取附件
        Deleted     // 已删除
    }
    
    /// <summary>
    /// 邮件类型枚举
    /// </summary>
    public enum MailType
    {
        System,         // 系统邮件
        Player,         // 玩家邮件
        Guild,          // 公会邮件
        ActivityReward  // 活动奖励邮件
    }
    
    
    
    /// <summary>
    /// 好友状态枚举
    /// </summary>
    
    public enum FriendStatus
    {
        Online,     // 在线
        Offline,    // 离线
        Away,       // 离开
        Busy,       // 忙碌
        Invisible   // 隐身
    }
}