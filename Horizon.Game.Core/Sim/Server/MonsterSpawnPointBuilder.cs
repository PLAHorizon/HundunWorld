using System.Collections.Generic;
using Horizon.Game.Sim;
using Horizon.Game.Sim.Monsters;

namespace Horizon.Game.Core.Sim.Server;

/// <summary>
/// 由已装配的怪物内容包造出权威刷怪配置（<see cref="SpawnPointConfig"/>）。
/// <para>
/// <b>为什么从 DsHost 里抽出来</b>：这份装配同时决定**血量、攻击、重生秒数、索敌/脱战半径与生效等级**，
/// 而会话内部（<c>DsWorldSession</c>）另有一条读内容包的通路。逻辑写在宿主 Program 里时，
/// 两条通路各算一遍同一个事实，历史上就是在这里分家的——
/// "模板刷新公式"被算进一份临时配置后没人用，重生延迟实际取的是默认 15 秒，
/// 于是世界 BOSS 死后 15 秒重开。抽成一处、并且可被测试直接调用，才算有证据说两边一致。
/// </para>
/// <para>
/// 数值一律不在这里凭空给：模板公式是唯一的数值来源（<see cref="MonsterStatsCore"/> /
/// <see cref="MonsterStatsCore.RespawnSeconds"/>），等级一律走
/// <see cref="MonsterSpawnSnapshot.ResolveLevel"/>，与会话内 <c>DsMonster.EffectiveLevel</c> 同一口径。
/// </para>
/// </summary>
public static class MonsterSpawnPointBuilder
{
    /// <summary>装配过程中必须让人看见的问题（模板没内容、等级非法等）。</summary>
    public sealed class Receipt
    {
        /// <summary>造出的刷怪配置。</summary>
        public List<SpawnPointConfig> Points { get; } = new List<SpawnPointConfig>();

        /// <summary>问题清单；空表示每个点都拿到了内容。</summary>
        public List<string> Problems { get; } = new List<string>();

        /// <summary>
        /// 因分片不符被跳过的刷怪点数。0 有两种含义（没给会话分片 / 没有点被挡），
        /// 所以宿主回执要连会话分片一起念，别让"零"自己承担两种解释。
        /// </summary>
        public int SkippedByShard { get; internal set; }
    }

    /// <summary>
    /// 生成刷怪配置。
    /// </summary>
    /// <param name="explicitShardId">命令行显式给的 shard（&lt;0 表示没给）；给了就用它，否则用刷怪点自己登记的 shard。</param>
    public static Receipt Build(long explicitShardId)
    {
        var receipt = new Receipt();
        foreach (var snapshot in MonsterContent.SpawnPoints())
        {
            // 分片门：0 = 任意分片（种子脚本与单片世界的开发点都是 0），非 0 必须与会话分片一致。
            // 与 NPC 那条（DsWorldSession.ApplyNpcContentSpawns）同一口径；没给会话分片（<0）时
            // 无从判断"本会话是哪一片"，照旧全部装配（此时 cfg 保留点自己登记的 shard）。
            // 为什么这扇门必须在这里而不是下游：`SpawnPointConfig.ShardId` 全仓只有写、没有比较——
            // 不加这一道，编辑器里"这条线只刷二区"就是一个存得进库、游戏中毫无后果的格子。
            if (explicitShardId >= 0 && snapshot.ShardId != 0 && snapshot.ShardId != explicitShardId)
            {
                receipt.SkippedByShard++;
                continue;
            }

            var cfg = SpawnPointConfig.Default;
            cfg.SpawnPointId = snapshot.SpawnPointId;
            cfg.ShardId = explicitShardId >= 0 ? explicitShardId : snapshot.ShardId;
            cfg.X = snapshot.X;
            cfg.Y = snapshot.Y;
            cfg.Z = snapshot.Z;
            cfg.Yaw = snapshot.Yaw;
            cfg.MonsterTemplateId = snapshot.MonsterTemplateId;
            cfg.MaxAlive = snapshot.MaxAlive > 0 ? snapshot.MaxAlive : 1;
            cfg.SlotSpacing = snapshot.SlotSpacing;

            if (!MonsterContent.TryGetConfig(snapshot.MonsterTemplateId, out var config) || config == null)
            {
                // 没装配内容的模板只能退回刷怪点默认值：血/攻/重生都不是策划配的那份。
                // 这条必须进 Problems，否则"库里配了怪却全是默认值"看起来和"库里就没配"一模一样。
                receipt.Problems.Add(string.Format(
                    "刷怪点 {0} 引用的模板 {1} 没有装配内容：血量/攻击/重生延迟全部退回默认值",
                    snapshot.SpawnPointId, snapshot.MonsterTemplateId));
                receipt.Points.Add(cfg);
                continue;
            }

            var behavior = config.Behavior;
            var level = snapshot.ResolveLevel(config);
            if (level < 1) level = 1;

            var stats = MonsterStatsCore.Compute(
                config.Stats, level, config.Rank, config.Stats.HpOverrideBps, config.Stats.AtkOverrideBps);
            cfg.MaxHp = stats.MaxHp;
            cfg.AttackPower = stats.AttackPower;

            // 重生秒：走模板公式（截距 + 每级 × 等级 × 档位倍率），刷怪点显式覆盖优先。
            // 以前这一行只是"覆盖值存在才改"，公式算出来的秒数被丢在另一份临时配置里没人读，
            // 结果所有怪的重生都固定在默认 15 秒。
            cfg.RespawnDelaySeconds = MonsterStatsCore.RespawnSeconds(
                config.RespawnBaseSeconds, config.RespawnPerLevelSeconds, level, config.Rank,
                snapshot.RespawnOverrideSeconds);

            cfg.WanderRadius = behavior.WanderRadius;
            cfg.AggroRadius = behavior.AggroRadius;
            cfg.LeashRadius = behavior.LeashRadius;
            if (behavior.MoveSpeed > 0f) cfg.MoveSpeed = behavior.MoveSpeed;

            receipt.Points.Add(cfg);
        }
        return receipt;
    }
}
