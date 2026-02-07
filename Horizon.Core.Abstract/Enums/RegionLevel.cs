using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 区域行政等级
    /// </summary>
    public enum RegionLevel : int
    {
        /// <summary>
        /// 省级
        /// </summary>
        Province = 1,
        /// <summary>
        /// 市级
        /// </summary>
        City = 2,
        /// <summary>
        /// 区级
        /// </summary>
        County = 3,
        /// <summary>
        /// 镇级
        /// </summary>
        Town = 4,
        /// <summary>
        /// 村级
        /// </summary>
        Village = 5,
    }
}
