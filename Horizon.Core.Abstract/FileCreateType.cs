using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text;

namespace Horizon.Core.Abstract
{
    /// <summary>
    /// 文件创建操作
    /// </summary>
    public enum FileCreateType
    {
        /// <summary>
        /// 创建新文件
        /// </summary>
        [Description("创建新文件")]
        CreateNew = 1,
        /// <summary>
        /// 覆盖原文件
        /// </summary>
        [Description("覆盖原文件")]
        Create = 2
    }
    /// <summary>
    /// 创建类型文件
    /// </summary>

    public enum CreateTypeFile
    {
        /// <summary>
        /// 图片
        /// </summary>
        [EnumMember]
        Image = 0,
        /// <summary>
        /// 视频
        /// </summary>
        [EnumMember] Video = 1,
        /// <summary>
        /// 声音
        /// </summary>
        [EnumMember] Voice = 2
    }
}
