using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core
{
    /// <summary>
    /// 实数字段验证标注
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class DecimalValidateAttribute : Attribute
    {
        /// <summary>
        /// 整数位数
        /// </summary>
        public int Integers { get; set; }
        /// <summary>
        /// 虚数位数
        /// </summary>
        public int ImaginaryNumbers { get; set; }
    }
}
