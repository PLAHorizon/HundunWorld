using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Horizon.Core.Abstract
{
    /// <summary>
    /// 消息的传输类型
    /// Request/Response/Push/Callback
    /// </summary>
    public enum RRPC
    {
        /// <summary>
        /// 请求消息
        /// </summary>
        [Description("请求消息")]
        Request = 0,
        /// <summary>
        /// 响应消息
        /// </summary>
        [Description("响应消息")]
        Response = 1,
        /// <summary>
        /// 推送消息
        /// </summary>
        [Description("推送消息")]
        Push = 2,
        /// <summary>
        /// 保持连接，等待回调
        /// </summary>
        [Description("回调")]
        CallBack = 3
    }
}
