using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 脸型
    /// </summary>
    public enum FaceShape
    {
        /// <summary>
        /// 方脸
        /// </summary>
        [Description("方脸")]
        Square = 0,
        /// <summary>
        /// 三角脸
        /// </summary>
        [Description("三角脸")] Triangle = 1,
        /// <summary>
        /// 鹅蛋脸
        /// </summary>
        [Description("鹅蛋脸")] Oval = 2,
        /// <summary>
        /// 心形
        /// </summary>
        [Description("心形")] Heart = 3,
        /// <summary>
        /// 圆脸
        /// </summary>
        [Description("圆脸")] Round = 4
    }

    /// <summary>
    /// 肤色
    /// </summary>
    public enum Race
    {
        /*yellow、white、black、arabs*/
        /// <summary>
        /// 黄色人种
        /// </summary>
        [Description("黄")]
        Yellow = 0,
        /// <summary>
        /// 白色人种
        /// </summary>
        [Description("白")]
        White = 0,
        /// <summary>
        /// 黑色人种
        /// </summary>
        [Description("黑")]
        Black = 0,
        /// <summary>
        /// 棕色人种
        /// </summary>
        [Description("棕")]
        Arabs = 0,
    }
}
