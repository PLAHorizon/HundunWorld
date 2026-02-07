using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 用户收藏类型
    /// </summary>
    public enum CollectionType
    {
        [Description("店铺")]
        Branch = 0,
        [Description("分饰角色")]
        Executor = 1,
        [Description("分饰角色中的事")]
        Event = 2,
        [Description("文章")]
        Article = 3,
    }
}
