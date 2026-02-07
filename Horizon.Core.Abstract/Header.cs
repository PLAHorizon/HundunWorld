using System;
using System.Runtime.Serialization;
using Horizon.Core;
using Horizon.Core.Abstract;

namespace Horizon.Core.Abstract
{
    /// <summary>
    /// 传输消息头
    /// </summary>
    [Serializable]
    public class Header
    {
        /// <summary>
        /// 用于Grain 激活标识
        /// </summary>

        public Guid GuidKey { get; set; }
        /// <summary>
        /// 通信证Id
        /// </summary>

        public string PassportId { get; set; }
        /// <summary>
        /// 应用类型
        /// </summary>

        public AppType AppType { get; set; }
        /// <summary>
        /// 应用Id
        /// </summary>
        public long APPId { get; set; }
        /// <summary>
        /// 游戏Id
        /// </summary>
        public long GameId { get; set; }
        /// <summary>
        /// 区域Id
        /// </summary>
        public long AreaId { get; set; }
        /// <summary>
        /// 服务Id
        /// </summary>
        public long ServerId { get; set; }
        /// <summary>
        /// 用户/游戏 角色Id
        /// </summary>
        public long? UserRoleId { get; set; }
        /// <summary>
        /// 令牌
        /// </summary>
        public ServerToken Token { get; set; }
        /// <summary>
        /// 消息传输类型
        /// </summary>

        public RRPC MessageType { get; set; }
        /// <summary>
        /// 请求服务名
        /// </summary>

        public string ServiceName { get; set; }
        /// <summary>
        /// 请求的服务操作方法
        /// </summary>

        public string ServiceAction { get; set; }
    }
}