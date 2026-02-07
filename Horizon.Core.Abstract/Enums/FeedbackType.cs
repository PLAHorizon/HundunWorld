using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 反馈类型
    /// </summary>
    public enum FeedbackType
    {
        /// <summary>
        /// UI无法直视，丑哭了
        /// </summary>
        [Description("UI无法直视，丑哭了")]
        UILower = 0,
        /// <summary>
        /// 界面显示错乱
        /// </summary>
        [Description("界面显示错乱")]
        UIError = 1,
        /// <summary>
        /// 程序错误
        /// </summary>
        [Description("程序错误")]
        ProgramError = 2,
        /// <summary>
        /// 偶发性奔溃
        /// </summary>
        [Description("偶发性奔溃")]
        ProgramCollapse = 3,
        /// <summary>
        /// 启动缓慢，卡出翔
        /// </summary>
        [Description("启动缓慢，卡出翔")]
        ProgramSlow = 4,
        /// <summary>
        /// 其它
        /// </summary>
        [Description("其它")]
        Other = 999,
    }
}
