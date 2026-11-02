using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model.GameModel
{
    /// <summary>
    /// NPC 模板实体（NPC 编辑器的**主表**：编号 → 名字/外观/立场/服务位图/AI 档位/速度）。
    /// <para>
    /// <b>与 <see cref="MonsterTemplateEntity"/> 分表而合并内核</b>（决策 D1 已拍板）：
    /// WoW 里 NPC 就是 creature + 一张服务位图，但本工程怪物侧八张表已被 3289 条测试钉住，
    /// 合并等于重做迁移与全部断言。分表的代价（若干公共列要各维护一份对账）远小于那个。
    /// </para>
    /// <para>
    /// <b>为什么这里没有血量/攻击这类数值列</b>：可击杀 NPC 的属性走怪物侧同一套等级→属性公式
    /// （<c>MonsterStatsCore</c>），经验与让分走 <c>MonsterXpCore</c>/<c>MonsterDifficultyCore</c>。
    /// 在这里再存一份数字，就会有两套"这个 NPC 有多少血"，而玩家只看到一个血条。
    /// </para>
    /// <para>
    /// <b>为什么这里没有 <c>movement_mode</c> 列</b>：移动方式（站桩/踱步/走路线）由 AI 能力位派生，
    /// <c>NpcBehaviorCore.MovementModeOf</c> 是唯一读法。再存一列就是同一件事有两个真相
    /// （记忆 [声明了却没人写的键会静默失效] 的反向形态：两个都写、各说各话）。
    /// </para>
    /// </summary>
    [Table("Game_HunduShijie_NpcTemplate"), TableDescription(Name = "Game_HunduShijie_NpcTemplate", Order = "HunduShijie_040", Description = "NPC模板信息")]
    [Comment("NPC 模板表（NPC 编辑器主表）")]
    [EntityStorage("Game")]
    public class NpcTemplateEntity : BaseGameModel<int>
    {
        /// <summary>
        /// NPC 编号（号段规则见设计文档 §1.3：<c>400000+ 服务型</c>、<c>500000+ 敌对型</c>）。
        /// 既有 1/2/3（铸剑师/守门人/游方商队）**不得改号**——
        /// <c>QuestTemplateEntity.giver_template_id</c> 里已经写了它们，改号会让存量任务的发布者指向空气。
        /// </summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        [Column("npc_id", TypeName = "int", Order = 1), TableDescription(TypeName = "int", Name = "npc_id", Order = "1", Description = "NPC编号")]
        [Comment("NPC 编号（号段即身份）")]
        public new int Id { get; set; }

        /// <summary>显示名。空名会让客户端回退成占位符，故目录内禁止空名。</summary>
        [Required]
        [Column("name", TypeName = "nvarchar(64)", Order = 2), TableDescription(TypeName = "nvarchar(64)", Name = "name", Order = "2", Description = "NPC名称")]
        [Comment("显示名")]
        public string Name { get; set; } = string.Empty;

        /// <summary>头顶头衔（"铁匠"/"守门人"），对应 WoW 的 <c>subname</c>，随 Spawn 的 Identity 下发。</summary>
        [Column("title", TypeName = "nvarchar(64)", Order = 3), TableDescription(TypeName = "nvarchar(64)", Name = "title", Order = "3", Description = "头衔")]
        [Comment("头顶头衔")]
        public string Title { get; set; } = string.Empty;

        /// <summary>客户端模型键（稳定分流标识；与 <see cref="MonsterTemplateEntity.ModelKey"/> 同一族语义）。</summary>
        [Column("model_key", TypeName = "nvarchar(64)", Order = 4), TableDescription(TypeName = "nvarchar(64)", Name = "model_key", Order = "4", Description = "模型键")]
        [Comment("客户端模型键")]
        public string ModelKey { get; set; } = string.Empty;

        /// <summary>
        /// prefab 资产 GUID（外观链的落点）。<b>空串是合法值</b>，含义是"这条 NPC 还没有模型资产"，
        /// 客户端要显式回退并记诊断，而不是加载一个空壳（怪物侧同口径：导出 0 条 + N 条"没有 prefab"是如实状态）。
        /// </summary>
        [Column("prefab_guid", TypeName = "nvarchar(64)", Order = 5), TableDescription(TypeName = "nvarchar(64)", Name = "prefab_guid", Order = "5", Description = "预制体GUID")]
        [Comment("prefab GUID（空=未配美术，客户端显式回退）")]
        public string PrefabGuid { get; set; } = string.Empty;

        /// <summary>体型倍数（1 = 原始大小）。观测：玩家一眼看出"这个人比卫兵大三分之一"。</summary>
        [Column("scale", TypeName = "float", Order = 6), TableDescription(TypeName = "float", Name = "scale", Order = "6", Description = "体型倍数")]
        [Comment("体型倍数")]
        public float Scale { get; set; } = 1f;

        /// <summary>基础等级。<b>服务型 NPC 也要有等级</b>：名牌要显示数字，且敌对型 NPC 的让分曲线要用它。</summary>
        [Column("level", TypeName = "int", Order = 7), TableDescription(TypeName = "int", Name = "level", Order = "7", Description = "基础等级")]
        [Comment("基础等级")]
        public int Level { get; set; } = 1;

        /// <summary>等级浮动（0 = 固定）。刷怪点上还能再覆盖一次；两处都有时以刷点为准（越近现场越具体）。</summary>
        [Column("level_variance", TypeName = "int", Order = 8), TableDescription(TypeName = "int", Name = "level_variance", Order = "8", Description = "等级浮动")]
        [Comment("等级浮动")]
        public int LevelVariance { get; set; }

        /// <summary>
        /// 立场：0 友好 1 中立 2 敌对。<b>这一列决定能不能打、给不给经验、打了好不好引来守卫</b>
        /// （判据在 <c>NpcReactionCore</c>，本列只是输入）。阵营敌对关系轴今天还没有权威来源，
        /// 见规格 §14 待读与决策 D13，所以这里先不存"阵营"的语义列。
        /// </summary>
        [Column("reaction", TypeName = "tinyint", Order = 9), TableDescription(TypeName = "tinyint", Name = "reaction", Order = "9", Description = "立场")]
        [Comment("立场 0友好 1中立 2敌对")]
        public byte Reaction { get; set; }

        /// <summary>
        /// 阵营组号。<b>今天没有消费者，只作占位记录</b>：本工程的 <c>CharacterEntity.faction</c>
        /// 建角时写死 0、全仓无人读写，所以"按阵营算敌对"还无法成立（决策 D13 待拍板）。
        /// 留这一列是为了等口径落地时不必再改表结构——但任何读它的代码都必须先确认玩家侧有真值。
        /// </summary>
        [Column("faction_group", TypeName = "int", Order = 10), TableDescription(TypeName = "int", Name = "faction_group", Order = "10", Description = "阵营组")]
        [Comment("阵营组（暂无权威玩家阵营，未参与判据）")]
        public int FactionGroup { get; set; }

        /// <summary>五行域（1金2木3水4火5土，0=未定域）。好感记账的来源，与怪物侧同一条 fail-closed 纪律。</summary>
        [Column("wuxing_domain", TypeName = "int", Order = 11), TableDescription(TypeName = "int", Name = "wuxing_domain", Order = "11", Description = "五行域")]
        [Comment("五行域 1金2木3水4火5土 0=未定")]
        public int WuxingDomain { get; set; }

        /// <summary>
        /// 服务能力位图（<c>NpcServiceBits</c> 的位值，本工程自己定义、不照抄 WoW 的十进制）。
        /// <b>未识别的位会被 <c>NpcServiceCore.UnknownBits</c> 拒</b>——写了没人读的位等于埋一个静默失效的开关。
        /// </summary>
        [Column("service_flags", TypeName = "int", Order = 12), TableDescription(TypeName = "int", Name = "service_flags", Order = "12", Description = "服务位图")]
        [Comment("服务能力位图（NpcServiceBits）")]
        public int ServiceFlags { get; set; }

        /// <summary>AI 智能档位预设（0 站桩 … 7 精英）。真开关是下面两列的覆盖。</summary>
        [Column("ai_tier", TypeName = "tinyint", Order = 13), TableDescription(TypeName = "tinyint", Name = "ai_tier", Order = "13", Description = "AI档位")]
        [Comment("AI 档位 0-7")]
        public byte AiTier { get; set; }

        /// <summary>能力位强制开（<c>NpcAiBits</c> 位值）。档位只是"预设按钮"。</summary>
        [Column("ai_bits_force_on", TypeName = "int", Order = 14), TableDescription(TypeName = "int", Name = "ai_bits_force_on", Order = "14", Description = "能力位强制开")]
        [Comment("能力位强制开")]
        public int AiBitsForceOn { get; set; }

        /// <summary>能力位强制关。<b>与强制开同位时关赢，且冲突会被报成配置缺陷</b>（<c>NpcBehaviorCore.Resolve</c>）。</summary>
        [Column("ai_bits_force_off", TypeName = "int", Order = 15), TableDescription(TypeName = "int", Name = "ai_bits_force_off", Order = "15", Description = "能力位强制关")]
        [Comment("能力位强制关")]
        public int AiBitsForceOff { get; set; }

        /// <summary>
        /// 可攻击覆盖：−1 跟立场、0 强制不可打、1 强制可打。
        /// <b>必须是三态而不是 bool</b>：bool 的默认 false 会把所有漏填的敌对 NPC 变成打不动的哑巴，
        /// 而且再也配不出"剧情里必须能杀的友好 NPC"与"不可击杀的敌对靶子"。
        /// </summary>
        [Column("attackable_override", TypeName = "int", Order = 16), TableDescription(TypeName = "int", Name = "attackable_override", Order = "16", Description = "可攻击覆盖")]
        [Comment("可攻击覆盖 -1跟立场 0强制不可 1强制可")]
        public int AttackableOverride { get; set; } = -1;

        [Column("speed_walk", TypeName = "float", Order = 17), TableDescription(TypeName = "float", Name = "speed_walk", Order = "17", Description = "步行速度")]
        [Comment("步行速度（米/秒）")]
        public float SpeedWalk { get; set; } = 1.6f;

        [Column("speed_run", TypeName = "float", Order = 18), TableDescription(TypeName = "float", Name = "speed_run", Order = "18", Description = "奔跑速度")]
        [Comment("奔跑速度（米/秒）")]
        public float SpeedRun { get; set; } = 4f;

        /// <summary>呼叫增援半径（米）。仅对开了 <c>CallsReinforcement</c> 的 NPC 有意义；≤0 会让守卫永远叫不来人（validate 拦）。</summary>
        [Column("alert_radius_meters", TypeName = "int", Order = 19), TableDescription(TypeName = "int", Name = "alert_radius_meters", Order = "19", Description = "增援半径")]
        [Comment("呼叫增援半径（米）")]
        public int AlertRadiusMeters { get; set; } = 20;

        /// <summary>台词组号（gossip）。0 = 无台词组。</summary>
        [Column("gossip_group_id", TypeName = "int", Order = 20), TableDescription(TypeName = "int", Name = "gossip_group_id", Order = "20", Description = "台词组")]
        [Comment("台词组 id")]
        public int GossipGroupId { get; set; }

        /// <summary>档位（精英框）。与怪物侧同一个 <c>MonsterRank</c> 枚举值域，稀有/精英的观感靠前缀与血条框，不靠新模型。</summary>
        [Column("rank", TypeName = "tinyint", Order = 21), TableDescription(TypeName = "tinyint", Name = "rank", Order = "21", Description = "NPC档位")]
        [Comment("rank 0普通 1精英 2稀有精英 3首领 4世界BOSS")]
        public byte Rank { get; set; }

        /// <summary>
        /// 击杀归属转移：打死这个 NPC 时把贡献记到另一个 NPC 编号（对应 WoW 的 <c>KillCredit1/2</c>）。
        /// 0 = 不转移。这是"贡献经验"链上唯一允许"被杀对象 ≠ 记账对象"的口子。
        /// </summary>
        [Column("kill_credit_npc_id", TypeName = "int", Order = 22), TableDescription(TypeName = "int", Name = "kill_credit_npc_id", Order = "22", Description = "击杀归属转移")]
        [Comment("击杀归属转移到的 NPC 编号，0=不转移")]
        public int KillCreditNpcId { get; set; }

        [Column("publish_state", TypeName = "tinyint", Order = 23), TableDescription(TypeName = "tinyint", Name = "publish_state", Order = "23", Description = "发布状态")]
        [Comment("发布状态 0草稿 1已发布 2已退役")]
        public byte PublishState { get; set; }

        [Column("content_version", TypeName = "int", Order = 24), TableDescription(TypeName = "int", Name = "content_version", Order = "24", Description = "内容版本")]
        [Comment("内容版本（乐观锁）")]
        public int ContentVersion { get; set; }
    }
}
