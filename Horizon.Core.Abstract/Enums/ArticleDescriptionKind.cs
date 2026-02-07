using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 文章注释类型
    /// </summary>
    public enum ArticleDescriptionKind
    {
        /// <summary>
        /// 共识注释
        /// </summary>
        [Description("共识注释")]
        Default = 0,
        /// <summary>
        /// 自定义注释
        /// </summary>
        [Description("自定义注释")]
        CustomDescription = 1,
        /// <summary>
        ///译文   
        /// </summary>
        [Description("译文")]
        Translate = 2,
        /// <summary>
        ///赏析   
        /// </summary>
        [Description("赏析")]
        Appreciate = 3,
        /// <summary>
        ///多媒体   
        /// </summary>
        [Description("多媒体")]
        Media = 4,
    }
}
