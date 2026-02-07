using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 事/服务的 可用状态
    /// </summary>
    public enum EventStatus
    {
        /// <summary>
        /// 正常
        /// </summary>
        [Description("正常")]
        Normal = 0,
        /// <summary>
        /// 冻结
        /// </summary>
        [Description("冻结")]
        Frozen = 1,
        /// <summary>
        /// 禁用
        /// </summary>
        [Description("禁用")]
        Disable = 2

    }
    /// <summary>
    ///会员间产生事件关系的时间当前状态
    /// </summary>
    public enum MemberEventStatus
    {
        /// <summary>
        /// 创建
        /// </summary>
        [Description("创建")]
        Create = -1,
        /// <summary>
        /// 开始
        /// </summary>
        [Description("开始")]
        Start = 0,
        /// <summary>
        /// 完成  
        /// </summary>
        [Description("完成")]
        Complete = 1,
        /// <summary>
        /// 等待
        /// </summary>
        [Description("等待")]
        Wait = 2,
        /// <summary>
        /// 失败
        /// </summary>
        [Description("失败")]
        Fail = 3,
        /// <summary>
        /// 退款
        /// </summary>
        [Description("退款")]
        Refund = 4,
        /// <summary>
        /// 进行中
        /// </summary>
        [Description("进行中")]
        Progress = 5,
    }
    /// <summary>
    /// 事的类型
    /// </summary>
    public enum WbcEventType
    {
        /// <summary>
        /// 验光
        /// </summary>
        [Description("验光")]
        Optometry = 0,
        /// <summary>
        /// 家政
        /// </summary>
        [Description("家政")]
        Housekeeping = 1,
    }
}
