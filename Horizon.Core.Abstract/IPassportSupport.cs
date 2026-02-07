using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Core.Abstract
{
    /// <summary>
    /// 点赞表接口
    /// </summary>
    public interface IPassportSupport
    {
        /// <summary>
        /// 用户
        /// </summary>
        string Passport { get; set; }
        /// <summary>
        /// 评论Id
        /// </summary>
        Guid SupportId { get; set; }
        /// <summary>
        /// 是否是赞，true:赞，false：反对
        /// </summary>
        bool IsSupport { get; set; }
    }
}
