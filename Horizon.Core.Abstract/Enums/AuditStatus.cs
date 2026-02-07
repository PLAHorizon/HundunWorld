using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 审核状态
    /// </summary>
    public enum AuditStatus
    {
        /// <summary>
        /// 未审核
        /// </summary>
        [Description("未审核")]
        Default = 0,
        /// <summary>
        /// 正常
        /// </summary>
        [Description("正常")]
        Normal = 1,
        /// <summary>
        /// 推荐
        /// </summary>
        [Description("推荐")]
        RecommendLevel1 = 2,
        /// <summary>
        /// 推荐
        /// </summary>
        [Description("推荐")]
        RecommendLevel2 = 3,
        /// <summary>
        /// 推荐
        /// </summary>
        [Description("推荐")]
        RecommendLevel3 = 4,
        /// <summary>
        /// 推荐
        /// </summary>
        [Description("推荐")]
        RecommendLevel4 = 5,
        /// <summary>
        /// 推荐
        /// </summary>
        [Description("推荐")]
        RecommendLevel5 = 6,
        /// <summary>
        /// 拒绝
        /// </summary>
        [Description("拒绝")]
        Refuse = -1,
        /// <summary>
        /// 删除
        /// </summary>
        [Description("删除")]
        Deleted = -9
    }
}
