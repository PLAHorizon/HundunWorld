using Horizon.Core.Abstract;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Share.Dtos
{
    public class PassportSupportDto : IPassportSupport
    {
        /// <summary>
        /// 评论Id
        /// </summary>
        public Guid SupportId { get; set; }
        /// <summary>
        /// 用户Id
        /// </summary>
        public string Passport { get; set; }
        /// <summary>
        /// 是否支持
        /// </summary>
        public bool IsSupport { get; set; }
    }
}
