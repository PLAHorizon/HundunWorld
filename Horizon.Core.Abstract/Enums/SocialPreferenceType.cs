using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 社交偏好类型
    /// </summary>
    public enum SocialPreferenceType
    {
        /// <summary>
        /// 标签
        /// </summary>
        [Description("标签")]
        Labe = 0,
        /// <summary>
        /// 运动
        /// </summary>
        [Description("运动")]
        Motion = 1,
        /// <summary>
        /// 音乐
        /// </summary>
        [Description("音乐")]
        Music = 2,
        /// <summary>
        /// 美食
        /// </summary>
        [Description("美食")]
        DeliciousFood = 3,
        /// <summary>
        /// 电影
        /// </summary>
        [Description("电影")]
        Film = 4,
        /// <summary>
        /// 书籍
        /// </summary>
        [Description("书籍")]
        Book = 5,
        /// <summary>
        /// 旅行
        /// </summary>
        [Description("旅行")]
        Travel = 6,
    }
}
