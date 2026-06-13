using Orleans;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Share.Dtos.User
{
    /// <summary>
    /// 微信小程序登录dto
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class WxLoginDto : LoginDto
    {
        /// <summary>
        /// 微信小程序 appid
        /// </summary>
        [Id(8)] public string WxAppId { get; set; }

        /// <summary>
        /// 微信小程序密钥(前端不用传)
        /// </summary>
        [Id(9)] public string? AppSecret { get; set; }

        /// <summary>
        /// 微信小程序请求码
        /// </summary>
        [Id(10)] public string Code { get; set; }
    }
}
