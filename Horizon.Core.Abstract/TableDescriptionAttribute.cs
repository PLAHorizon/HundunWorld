using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract
{
    /// <summary>
    /// 表/字段 概要信息，用于标注实体类类型本身及其属性
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Enum | AttributeTargets.Field)]
    public class TableDescriptionAttribute : Attribute
    {
        /// <summary>
        /// 名称
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// 编号
        /// </summary>
        public string Order { get; set; }
        /// <summary>
        /// 描述
        /// </summary>
        public string Description { get; set; }
        /// <summary>
        /// 数据类型
        /// </summary>
        public string TypeName { get; set; }
        /// <summary>
        /// 是否是争议字段
        /// </summary>       
        public bool IsDisputeProperty { get; set; }
    }
}
