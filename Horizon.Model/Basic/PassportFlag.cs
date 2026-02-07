using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;
using System;

namespace Horizon.Model.Basic
{
    /// <summary>
    /// 生成通行证开关表
    /// </summary>
    [Table("Basic_Sys_PassportFlag")]
    [EntityStorage("Basic")]
    [Comment("生成通行证开关表")]
    public class PassportFlag : BaseModel<int>
    {
        /// <summary>
        /// 是否正在生成中
        /// </summary>
        [Comment("是否正在生成中")]
        public bool IsCreating { get; set; }
        /// <summary>
        /// 最后一次生成时间
        /// </summary>
        [Comment("最后一次生成时间")]
        public DateTime LastTime { get; set; }
        /// <summary>
        /// 生成总数
        /// </summary>
        [Comment("生成总数")]
        public long Total { get; set; }

    }
}
