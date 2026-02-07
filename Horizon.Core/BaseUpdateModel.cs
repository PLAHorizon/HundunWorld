using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Core
{
    /// <summary>
    /// 视图更新数据基类
    /// </summary>
    public class BaseUpdateModel
    {
        /// <summary>
        /// 数据记录Id
        /// </summary>
        public object Id { get; set; }

        /// <summary>
        /// 待更新数据,数据字段为Key,数据值为Value
        /// </summary>
        public Dictionary<string, object> Data { get; set; }
    }
}
