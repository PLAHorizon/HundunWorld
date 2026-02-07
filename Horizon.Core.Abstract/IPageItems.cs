using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Core.Abstract
{

    public interface IPageItems<T>
    {
        /// <summary>
        /// 数据总条数
        /// </summary>
        int Total { get; set; }
        /// <summary>
        /// 数据集合
        /// </summary>
        List<T> Items { get; set; }
    }
}
