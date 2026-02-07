using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Core.Abstract
{
    /// <summary>
    /// 服务令牌模型
    /// </summary>
    [Serializable]
    public class ServerToken
    {
        public ServerToken() { }
        public ServerToken(string controller, string action, params object[] parame)
        {
            ServiceName = controller;
            ServiceAction = action;
            Parame = parame;
        }
        /// <summary>
        /// 授权标识键
        /// </summary>
        public string AccessKey { get; set; }
        /// <summary>
        /// 授权密匙
        /// </summary>
        public string AccessKeySecret { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string OpenId { get; set; }
        /// <summary>
        /// 令牌时效，单位毫秒
        /// </summary>
        public int TokenExpire { get; set; }
        /// <summary>
        /// 时间戳
        /// 令牌产生时间
        /// </summary>
        public string DateStamp { get; set; }
        /// <summary>
        /// 当前令牌使用的服务名
        /// </summary>
        public string ServiceName { get; }
        /// <summary>
        /// 当前令牌使用的服务操作名
        /// </summary>
        public string ServiceAction { get; }
        /// <summary>
        /// 当前令牌使用的操作参数
        /// </summary>
        public object[] Parame { get; }
    }
}
