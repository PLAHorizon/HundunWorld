using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 订单状态
    /// </summary>
    public enum OrderStatus
    {
        /// <summary>
        /// 订单处于暂时锁定状态
        /// </summary>
        [Description("订单处于暂时锁定状态")]
        Lock = -1,
        /// <summary>
        /// 创建
        /// </summary>
        [Description("创建")]
        Create = 0,
        /// <summary>
        /// 等待支付
        /// </summary>
        [Description("等待支付")]
        WaitPay = 1,
        /// <summary>
        /// 支付完成
        /// </summary>
        [Description("支付完成")]
        CompletePay = 2,
        /// <summary>
        /// 充值支付完成
        /// </summary>
        [Description("充值支付完成")]
        ChargeCompletePay = 22,
        /// <summary>
        /// 等待发货
        /// </summary>
        [Description("等待发货")]
        WaitExpress = 3,
        /// <summary>
        /// 货物运输中
        /// </summary>
        [Description("货物运输中")]
        Express = 6,
        /// <summary>
        /// 确认收货
        /// </summary>
        [Description("确认收货")]
        ConfirmExpress = 4,
        /// <summary>
        /// 完成
        /// </summary>
        [Description("完成")]
        Complete = 5,
        /// <summary>
        /// 退换货款创建
        /// </summary>
        [Description("退换货款创建")]
        RefundCreate = 7,
        /// <summary>
        /// 退换货款等待支付
        /// </summary>
        [Description("退换货款等待支付")]
        RefundWaitPay = 8,
        /// <summary>
        /// 退换货款支付完成
        /// </summary>
        [Description("退换货款支付完成")]
        RefundCompletePay = 9,
        /// <summary>
        /// 退换货款等待发货
        /// </summary>
        [Description("退换货款等待发货")]
        RefundWaitExpress = 10,
        /// <summary>
        /// 退换货款货物运输中
        /// </summary>
        [Description("退换货款货物运输中")]
        RefundExpress = 11,
        /// <summary>
        /// 退换货款确认收货
        /// </summary>
        [Description("退换货款确认收货")]
        RefundConfirmExpress = 12,
        /// <summary>
        /// 退换货款完成
        /// </summary>
        [Description("退换货款完成")]
        RefundComplete = 13,
        /// <summary>
        /// 服务创建
        /// </summary>
        [Description("服务创建")]
        ServiceCreate = 14,
        /// <summary>
        /// 服务等待
        /// </summary>
        [Description("服务等待")]
        ServiceWait = 15,
        /// <summary>
        /// 服务进行中
        /// </summary>
        [Description("服务进行中")]
        ServiceProcess = 16,
        /// <summary>
        /// 服务完成
        /// </summary>
        [Description("服务完成")]
        ServiceComplete = 17,
        /// <summary>
        /// 订单进行中
        /// </summary>
        [Description("订单进行中")]
        Process = 18,


    }
    /// <summary>
    /// 订单类型
    /// </summary>
    public enum OrderType
    {
        /// <summary>
        /// 企业授权
        /// </summary>
        [Description("企业授权")]
        EnterpriseAuth = 0,
        /// <summary>
        /// 验光
        /// </summary>
        [Description("验光")]
        Optometry = 1,
        /// <summary>
        /// 短信
        /// </summary>
        [Description("短信")]
        Sms = 2,
    }

    /// <summary>
    /// 企业授权订单的类型
    /// </summary>
    public enum OrderEnterpriseAuth
    {
        /// <summary>
        /// 镜通宝
        /// </summary>
        [Description("镜通宝")]
        MirrorMan = 1,
        /// <summary>
        /// 企业许可授信
        /// </summary>
        [Description("企业许可授信")]
        License = 2,
    }
    /// <summary>
    /// 订单关闭类型
    /// </summary>
    public enum OrderClose
    {
        /// <summary>
        /// 系统自动关闭
        /// </summary>
        [Description("系统自动关闭")]
        Auto = 0,
        /// <summary>
        /// 顾客主动动关闭
        /// </summary>
        [Description("顾客主动动关闭")]
        Member = 1,
        /// <summary>
        /// 门店主动关闭
        /// </summary>
        [Description("门店主动关闭")]
        Branch = 3,
        /// <summary>
        /// 门店相关工作员主动关闭
        /// </summary>
        [Description("门店相关工作员主动关闭")]
        Executor = 4,
    }
}
