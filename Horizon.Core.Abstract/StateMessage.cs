using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Core.Abstract
{
    /// <summary>
    /// 操作标记信息
    /// </summary>
    [Serializable]
    public class StateMessage
    {
        public StateMessage()
        {
            Code = ServiceResultCode.StateCode_200;
            Message = "服务执行成功";
        }
        /// <summary>
        /// 执行结果返回消息码
        /// 标注消息状态
        /// </summary>
        public int Code { get; set; }
        /// <summary>
        ///标注消息类型
        /// </summary>
        public int MessageCode { get; set; }
        /// <summary>
        /// 执行结果返回消息字符串内容
        /// </summary>
        public string Message { get; set; }
    }
}
