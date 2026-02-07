using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Share.Commones
{
    /// <summary>
    /// 选择项
    /// </summary>
    public class SelectItem
    {
        /// <summary>
        /// 标识
        /// </summary>
        public string Id { get; set; }
        /// <summary>
        /// 显示名
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// 不可选择
        /// </summary>
        public bool NotAvailable { get; set; }
    }
}
