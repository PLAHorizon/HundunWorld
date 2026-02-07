using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 社交添加关系来源类型
    /// </summary>
    public enum IMSourceType
    {
        /// <summary>
        /// 搜索Id
        /// </summary>
        [Description("搜索Id")]
        SearchId = 0,
        /// <summary>
        /// 搜索姓名
        /// </summary>
        [Description("搜索姓名")] SearchName = 1,
        /// <summary>
        /// 搜索手机号
        /// </summary>
        [Description("搜索手机号")] SearchPhone = 2,
        /// <summary>
        /// 推荐添加
        /// </summary>
        [Description("推荐添加")] Referee = 3,
        /// <summary>
        /// 手机通信录
        /// </summary>
        [Description("手机通信录")] Phone = 4,
        /// <summary>
        /// 其它
        /// </summary>
        [Description("其它")] Other = 5,
    }
}
