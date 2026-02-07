using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 问题答案类型
    /// </summary>
    public enum ProblemAnswerType
    {
        /// <summary>
        /// 主观答案
        /// </summary>
        [Description("主观")]
        Subjective = 0,

        /// <summary>
        /// 多选答案
        /// </summary>
        [Description("多选答案")]
        Multiselect = 2,
        /// <summary>
        /// 单选答案
        /// </summary>
        [Description("单选答案")]
        Radio = 3
    }
}
