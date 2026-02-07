using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Core.Abstract
{
    /// <summary>
    /// Orleans 常用常量
    /// </summary>
    public static class OrleansConst
    {
        /// <summary>
        /// 发布订阅模式
        /// </summary>
        public const string PubSubStore = nameof(PubSubStore);
        public const string GameStore = nameof(GameStore);
        public const string PassportStore = nameof(PassportStore);
        /// <summary>
        /// 通用的消息流
        /// </summary>
        public const string CommonMessageStreamProvider = nameof(CommonMessageStreamProvider);
    }
}
