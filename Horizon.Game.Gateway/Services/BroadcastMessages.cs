using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horizon.Game.Gateway.Services
{
    /// <summary>
    /// 广播消息数据结构
    /// </summary>
    public class BroadcastMessage
    {
        /// <summary>
        /// 消息数据
        /// </summary>
        public byte[] Data { get; set; }
        
        /// <summary>
        /// 广播类型
        /// </summary>
        public BroadcastType Type { get; set; }
        
        /// <summary>
        /// 筛选条件（可选）
        /// </summary>
        public BroadcastFilter Filter { get; set; }
    }

    /// <summary>
    /// 广播类型枚举
    /// </summary>
    public enum BroadcastType
    {
        /// <summary>
        /// 广播给所有连接
        /// </summary>
        All,
        
        /// <summary>
        /// 广播给认证用户
        /// </summary>
        AuthenticatedUsers,
        
        /// <summary>
        /// 广播给指定用户组
        /// </summary>
        UserGroup,
        
        /// <summary>
        /// 根据属性筛选广播
        /// </summary>
        ByProperty
    }

    /// <summary>
    /// 广播筛选条件
    /// </summary>
    public class BroadcastFilter
    {
        /// <summary>
        /// 用户ID列表（用于UserGroup类型）
        /// </summary>
        public List<long> UserIds { get; set; }
        
        /// <summary>
        /// 属性筛选条件（用于ByProperty类型）
        /// </summary>
        public Dictionary<string, object> Properties { get; set; }
    }
}