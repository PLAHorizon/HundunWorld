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
        /// 世界状态持久化 store（P4-a）：WorldChunkCellGrain / WorldDiffLogGrain 使用；
        /// 落在 SQL Server 的 chunk_state / diff_log 表。
        /// </summary>
        public const string WorldSqlStore = nameof(WorldSqlStore);
        public const string FlowerStore = nameof(FlowerStore);
        public const string AIStore = nameof(AIStore);
        /// <summary>
        /// 通用的消息流
        /// </summary>
        public const string CommonMessageStreamProvider = nameof(CommonMessageStreamProvider);
    }
}
