using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 文章评论类型
    /// </summary>
    public enum ArticleCommetKind
    {
        /// <summary>
        /// 文章主题评论
        /// </summary>
        [Description("文章主题评论")]
        Article = 0,
        /// <summary>
        /// 文章章节评论
        /// </summary>
        [Description("文章章节评论")]
        Chapters = 1
    }
}
