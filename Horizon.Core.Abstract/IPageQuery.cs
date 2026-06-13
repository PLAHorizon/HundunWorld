#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Core.Abstract
{
    public interface IPageQuery
    {
        /// <summary>
        /// 每页条目数量
        /// </summary>
        int PageSize { get; set; }
        /// <summary>
        /// 页码
        /// </summary>
        int PageNumber { get; set; }
        /// <summary>
        /// 搜索关键字
        /// </summary>
        string? SearchKey { get; set; }
    }
}
