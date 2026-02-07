using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Core.Options
{
    /// <summary>
    /// 用户数据验证选项，单位分钟
    /// </summary>
    public class VerificationUserDataOptions
    {
        /// <summary>
        /// 手机验证有效时间
        /// </summary>
        public int Phone { get; set; }
        /// <summary>
        /// 邮箱验证有效时间
        /// </summary>
        public int Email { get; set; }
        /// <summary>
        /// 身份证验证有效时间
        /// </summary>
        public int IdCard { get; set; }
        /// <summary>
        /// 人脸识别
        /// </summary>
        public int FaceId { get; set; }

    }
}
