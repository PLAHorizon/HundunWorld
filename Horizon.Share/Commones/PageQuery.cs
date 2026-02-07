using System;
using System.Collections.Generic;
using System.Text;
using Horizon.Core.Abstract;
using Orleans;

namespace Horizon.Share.Commones
{
    /// <summary>
    /// 分页数据查询Dto
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class PageQuery : IPageQuery
    {
        /// <summary>
        /// 每页条目数量
        /// </summary>
        [Id(0)] public int PageSize { get; set; } = 20;
        /// <summary>
        /// 页码
        /// </summary>
        [Id(1)] public int PageNumber { get; set; } = 0;
        /// <summary>
        /// 搜索关键字
        /// </summary>
        [Id(2)] public string? SearchKey { get; set; }
    }
}
