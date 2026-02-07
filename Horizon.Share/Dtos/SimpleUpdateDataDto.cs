using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Share.Dtos
{
    /// <summary>
    /// 更新数据值
    /// </summary>
    public class SimpleUpdateDataDto
    {
        /// <summary>
        /// 数据主键Id
        /// </summary>
        public object Id { get; set; }
        /// <summary>
        /// 需要更新数据的新值
        /// </summary>
        public object UpdateValue { get; set; }
        /// <summary>
        /// 数据字段名
        /// </summary>
        public string FieldName { get; set; }
    }
}
