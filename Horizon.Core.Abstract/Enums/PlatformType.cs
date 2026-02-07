using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 系统应用平台类型
    /// </summary>
    public enum PlatformType
    {
        /// <summary>
        ///常用电脑
        /// </summary>
        [Description("PC")]
        PC = 0,
        /// <summary>
        /// 安卓手机
        /// </summary>
        [Description("Android")]
        Android = 1,
        /// <summary>
        /// 苹果手机
        /// </summary>
        [Description("IPhone")]
        IPhone = 2,
        /// <summary>
        /// 苹果电脑
        /// </summary>
        [Description("MacBook")]
        MacBook = 3,
        /// <summary>
        /// 平板电脑
        /// </summary>
        [Description("IPad")]
        IPad = 4,
        /// <summary>
        /// 网页
        /// </summary>
        [Description("Web")]
        Web = 5,
        /// <summary>
        /// 小程序
        /// </summary>
        [Description("SP")]
        SP = 6,
    }
}
