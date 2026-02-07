using System;
using System.Collections.Generic;
using System.Text;
using Horizon.Core.Abstract;

namespace Horizon.Share.Commones
{
    /// <summary>
    /// 数据分页类
    /// </summary>
    /// <typeparam name="T">数据类型类型参数</typeparam>
    public class PageItems<T> : IPageItems<T>
    {
        /// <summary>
        /// 数据总条数
        /// </summary>
        public int Total { get; set; }
        /// <summary>
        /// 数据集合
        /// </summary>
        public List<T> Items { get; set; }
        public PageItems(int total, List<T> items)
        {
            Items = items;
            Total = total;
        }
    }
}
