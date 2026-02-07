using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    ///资源类型
    /// </summary>
    public enum WbcEventResourceType
    {
        /// <summary>
        /// 文字
        /// </summary>
        [Description("文字")]
        Text = 0,
        /// <summary>
        /// 音频
        /// </summary>
        [Description("音频")]
        Audio = 1,
        /// <summary>
        /// 视频
        /// </summary>
        [Description("视频")]
        Video = 2,
        /// <summary>
        /// 图片
        /// </summary>
        [Description("图片")]
        Image = 3
    }
}
