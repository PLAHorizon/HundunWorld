using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 文章内容类型
    /// </summary>
    public enum ArticleContextKind
    {
        /// <summary>
        /// 默认
        /// </summary>
        [Description("默认")]
        Default = 0,
        /// <summary>
        /// 文本
        /// </summary>
        [Description("文本")]
        Text = 1,
        /// <summary>
        /// 图片
        /// </summary>
        [Description("图片")]
        Image = 2,
        /// <summary>
        /// 视频
        /// </summary>
        [Description("视频")]
        Video = 3,
        /// <summary>
        /// 音频
        /// </summary>
        [Description("音频")]
        Audio = 4,
        /// <summary>
        /// 实时流媒体
        /// </summary>
        [Description("实时流媒体")]
        RealStreamMedia = 5,
    }
}
