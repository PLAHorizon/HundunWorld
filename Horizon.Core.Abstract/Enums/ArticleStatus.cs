using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 文章状态
    /// </summary>
    public enum ArticleStatus
    {
        /// <summary>
        /// 创作中
        /// </summary>
        [Description("创作中")]
        Creating = 0,
        /// <summary>
        /// 已完成
        /// </summary>
        [Description("已完成")]
        Complete = 1,
        /// <summary>
        /// 已发表
        /// </summary>
        [Description("已发表")]
        Publish = 2,
        /// <summary>
        /// 禁止发表
        /// </summary>
        [Description("禁止发表")]
        Frozen = 3,
        /// <summary>
        /// 已删除
        /// </summary>
        [Description("已删除")]
        Deleted = 4,
    }
}
