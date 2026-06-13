using Orleans;
using Orleans.CodeGeneration;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract
{
    /// <summary>
    /// 应用类型
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public enum AppType
    {
        /// <summary>
        /// 基础设施
        /// </summary>
        [Description("基础设施"), Id(0)]
        Basic = 0,
        /// <summary>
        /// 协同办公
        /// </summary>
        [Description("协同办公"), Id(1)]
        OA = 1,
        /// <summary>
        /// 人工智能
        /// </summary>
        [Description("人工智能"), Id(2)]
        AI = 2,
        /// <summary>
        /// 机器学习
        /// </summary>
        [Description("机器学习"), Id(3)]
        ML = 3,
        /// <summary>
        /// 国学
        /// </summary>
        [Description("国学"), Id(4)]
        GX = 9,
        /// <summary>
        /// 项目管理
        /// </summary>
        [Description("项目管理"), Id(5)]
        PM = 10,
        /// <summary>
        /// 医院信息系统
        /// </summary>
        [Description("医院信息系统"), Id(6)]
        HIS = 91,
        /// <summary>
        /// 医院实验室系统
        /// </summary>
        [Description("医院实验室系统"), Id(7)]
        LIS = 92,
        /// <summary>
        /// 医院影响医学系统
        /// </summary>
        [Description("医院影响医学系统"), Id(8)]
        PACS = 93,
        /// <summary>
        /// 游戏
        /// </summary>
        [Description("游戏"), Id(9)]
        Game = 369,
        /// <summary>
        /// 花卉产业
        /// </summary>
        [Description("花卉产业"), Id(10)]
        Flower = 520
    }
}
