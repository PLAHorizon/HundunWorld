using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model.GameModel
{
    /// <summary>
    /// 宠物物种模板实体（宠物编辑器的**主表**：宠物编号 → 名字/外观/携带段/孵化/捕捉）。
    /// <para>
    /// <b>为什么这里只有"规则"没有"个体"</b>：物种描述的是"这一种宠"，资质在这里是**区间**而不是值；
    /// 某一只具体宠物住在 <see cref="PetInstanceEntity"/>。把两者混成一张表，就会出现
    /// "编辑器改一次物种，全服被抓到的宠物跟着变"这种不可逆事故。
    /// </para>
    /// <para>
    /// <b>为什么权威必须在服务端</b>：与 <see cref="MonsterTemplateEntity"/> 同一条理由——
    /// 若资质/成长/技能可由调用方声明，客户端就能自己说"我的宠满成长满技能"。
    /// </para>
    /// <para>
    /// <b>号段即身份</b>：500000–599999 普通、600000–649999 变异/稀有、650000–699999 神兽、700000+ 活动限定。
    /// 号段的含义写死在 <c>PetIdentityCore.RarityTierOf</c>，本表的 <c>species_id</c> 必须落在其中。
    /// </para>
    /// </summary>
    [Table("Game_HunduShijie_PetTemplate"), TableDescription(Name = "Game_HunduShijie_PetTemplate", Order = "HunduShijie_040", Description = "宠物物种模板信息")]
    [Comment("宠物物种模板表（宠物编辑器主表）")]
    [EntityStorage("Game")]
    public class PetTemplateEntity : BaseGameModel<int>
    {
        /// <summary>宠物编号（物种号；号段规则见类注释）。个体另有 <c>pet_instance_id</c>。</summary>
        [Key]
        [Column("species_id", TypeName = "int", Order = 1), TableDescription(TypeName = "int", Name = "species_id", Order = "1", Description = "宠物编号")]
        [Comment("宠物编号（号段即身份）")]
        public new int Id { get; set; }

        /// <summary>显示名。可改、可翻译，<b>绝不参与任何判等</b>（记忆 display-text-must-not-be-identity）。</summary>
        [Required]
        [Column("name", TypeName = "nvarchar(64)", Order = 2), TableDescription(TypeName = "nvarchar(64)", Name = "name", Order = "2", Description = "宠物名称")]
        [Comment("显示名")]
        public string Name { get; set; } = string.Empty;

        /// <summary>客户端模型键（稳定标识，供 prefab 分流）。</summary>
        [Column("model_key", TypeName = "nvarchar(64)", Order = 3), TableDescription(TypeName = "nvarchar(64)", Name = "model_key", Order = "3", Description = "模型键")]
        [Comment("客户端模型键")]
        public string ModelKey { get; set; } = string.Empty;

        /// <summary>prefab 的**内容树路径**（可空；空 = 只登记了意图、还没进包）。</summary>
        [Column("prefab_path", TypeName = "nvarchar(256)", Order = 4), TableDescription(TypeName = "nvarchar(256)", Name = "prefab_path", Order = "4", Description = "预制体路径")]
        [Comment("预制体内容路径（客户端按路径解析 GUID）")]
        public string? PrefabPath { get; set; }

        /// <summary>直接指认 prefab 资产 Guid（可空；可解析性由导出工具逐条验证）。</summary>
        [Column("prefab_guid", TypeName = "nvarchar(64)", Order = 5), TableDescription(TypeName = "nvarchar(64)", Name = "prefab_guid", Order = "5", Description = "预制体Guid")]
        [Comment("预制体资产 Guid（可空）")]
        public string? PrefabGuid { get; set; }

        /// <summary>体型缩放。</summary>
        [Column("model_scale", TypeName = "decimal(4,2)", Order = 6), TableDescription(TypeName = "decimal(4,2)", Name = "model_scale", Order = "6", Description = "体型缩放")]
        [Comment("体型缩放")]
        public decimal ModelScale { get; set; } = 1.0m;

        /// <summary>待机姿态名（外观表导出用，与怪物侧同字段语义）。</summary>
        [Column("stand_state", TypeName = "nvarchar(32)", Order = 7), TableDescription(TypeName = "nvarchar(32)", Name = "stand_state", Order = "7", Description = "待机状态名")]
        [Comment("待机状态名")]
        public string StandState { get; set; } = "Idle";

        /// <summary>基色键（染色/变异覆盖前的默认色；只是显示层的键，不当身份用）。</summary>
        [Column("base_color_key", TypeName = "nvarchar(32)", Order = 8), TableDescription(TypeName = "nvarchar(32)", Name = "base_color_key", Order = "8", Description = "基色键")]
        [Comment("基色键")]
        public string BaseColorKey { get; set; } = "base";

        /// <summary>可携带等级段下界（梦幻 §B1：捕捉与携带都按等级段分层）。</summary>
        [Column("carry_level_min", TypeName = "int", Order = 9), TableDescription(TypeName = "int", Name = "carry_level_min", Order = "9", Description = "携带等级下界")]
        [Comment("可携带等级下界")]
        public int CarryLevelMin { get; set; }

        /// <summary>可携带等级上界。</summary>
        [Column("carry_level_max", TypeName = "int", Order = 10), TableDescription(TypeName = "int", Name = "carry_level_max", Order = "10", Description = "携带等级上界")]
        [Comment("可携带等级上界")]
        public int CarryLevelMax { get; set; } = 175;

        /// <summary>允许的出身集合（CSV：0野生1宝宝2成年3变异4二代5神兽）。运行时按 CSV 解析，空 = 只允许野生。</summary>
        [Column("allowed_origins", TypeName = "nvarchar(64)", Order = 11), TableDescription(TypeName = "nvarchar(64)", Name = "allowed_origins", Order = "11", Description = "允许出身")]
        [Comment("允许出身 CSV")]
        public string AllowedOrigins { get; set; } = "0,1";

        /// <summary>允许的成长档位集合（CSV 0–4；不是所有物种都能出"完美"）。</summary>
        [Column("allowed_growth_tiers", TypeName = "nvarchar(32)", Order = 12), TableDescription(TypeName = "nvarchar(32)", Name = "allowed_growth_tiers", Order = "12", Description = "允许成长档")]
        [Comment("允许成长档 CSV")]
        public string AllowedGrowthTiers { get; set; } = "0,1,2";

        /// <summary>孵化秒数（0 = 捕捉即得，WoW 档）。</summary>
        [Column("hatch_seconds", TypeName = "int", Order = 13), TableDescription(TypeName = "int", Name = "hatch_seconds", Order = "13", Description = "孵化秒数")]
        [Comment("孵化秒数 0=捕捉即得")]
        public int HatchSeconds { get; set; }

        /// <summary>出壳等级（梦幻宝宝 = 0）。</summary>
        [Column("hatch_level", TypeName = "int", Order = 14), TableDescription(TypeName = "int", Name = "hatch_level", Order = "14", Description = "出壳等级")]
        [Comment("出壳等级")]
        public int HatchLevel { get; set; }

        /// <summary>允许超出主人的级数（天龙 10、WoW 0；超出即无法出战）。</summary>
        [Column("owner_level_slack", TypeName = "int", Order = 15), TableDescription(TypeName = "int", Name = "owner_level_slack", Order = "15", Description = "超主级数")]
        [Comment("允许超出主人的级数")]
        public int OwnerLevelSlack { get; set; } = 10;

        /// <summary>每级潜能点（梦幻/天龙均为 5）。</summary>
        [Column("potential_per_level", TypeName = "int", Order = 16), TableDescription(TypeName = "int", Name = "potential_per_level", Order = "16", Description = "每级潜能点")]
        [Comment("每级潜能点")]
        public int PotentialPerLevel { get; set; } = 5;

        /// <summary>寿命是否无限（神兽出战不掉寿命）。</summary>
        [Column("immortal_lifespan", TypeName = "bit", Order = 17), TableDescription(TypeName = "bit", Name = "immortal_lifespan", Order = "17", Description = "寿命无限")]
        [Comment("寿命无限（神兽）")]
        public bool ImmortalLifespan { get; set; }

        /// <summary>快乐刻度（1000 = WoW/梦幻千分制；100 = 天龙口径）。</summary>
        [Column("happiness_scale", TypeName = "int", Order = 18), TableDescription(TypeName = "int", Name = "happiness_scale", Order = "18", Description = "快乐刻度")]
        [Comment("快乐刻度 1000或100")]
        public int HappinessScale { get; set; } = 1000;

        /// <summary>捕捉方式：0左键瞬捕 1战斗残血 2引导驯服 3对战收服 4不可捕捉。</summary>
        [Column("capture_mode", TypeName = "tinyint", Order = 19), TableDescription(TypeName = "tinyint", Name = "capture_mode", Order = "19", Description = "捕捉方式")]
        [Comment("捕捉方式 0瞬捕1残血2驯服3对战4不可捕")]
        public byte CaptureMode { get; set; } = 1;

        /// <summary>捕捉基准概率（万分比）。</summary>
        [Column("capture_base_bps", TypeName = "int", Order = 20), TableDescription(TypeName = "int", Name = "capture_base_bps", Order = "20", Description = "捕捉基准概率")]
        [Comment("捕捉基准概率 bps")]
        public int CaptureBaseBps { get; set; } = 3000;

        /// <summary>驯服引导秒数（WoW = 20；非驯服方式忽略此列）。</summary>
        [Column("capture_guidance_seconds", TypeName = "int", Order = 21), TableDescription(TypeName = "int", Name = "capture_guidance_seconds", Order = "21", Description = "驯服引导秒")]
        [Comment("驯服引导秒")]
        public int CaptureGuidanceSeconds { get; set; }

        /// <summary>变异 roll 概率（万分比）；0 = 该物种不变异。</summary>
        [Column("mutation_chance_bps", TypeName = "int", Order = 22), TableDescription(TypeName = "int", Name = "mutation_chance_bps", Order = "22", Description = "变异概率")]
        [Comment("变异概率 bps")]
        public int MutationChanceBps { get; set; }

        /// <summary>发布状态：0草稿 1已发布 2已停用。运行时只装配 1。</summary>
        [Column("publish_state", TypeName = "tinyint", Order = 23), TableDescription(TypeName = "tinyint", Name = "publish_state", Order = "23", Description = "发布状态")]
        [Comment("发布状态 0草稿 1已发布 2已停用")]
        public byte PublishState { get; set; }

        /// <summary>内容版本（乐观锁 + 装配对账）。</summary>
        [Column("content_version", TypeName = "int", Order = 24), TableDescription(TypeName = "int", Name = "content_version", Order = "24", Description = "内容版本")]
        [Comment("内容版本")]
        public int ContentVersion { get; set; } = 1;

        /// <summary>备注。</summary>
        [Column("remark", TypeName = "nvarchar(500)", Order = 25), TableDescription(TypeName = "nvarchar(500)", Name = "remark", Order = "25", Description = "备注")]
        [Comment("策划备注")]
        public string Remark { get; set; } = string.Empty;
    }

    /// <summary>
    /// 宠物资质区间（与模板 1:1）。
    /// <para>五维资质在这里是 <b>[min, max] 区间</b>：一只具体宠物的资质是捕捉/洗炼时在区间内 roll 出来的。
    /// 存区间而不是存常数，才能表达"野生平均 21 档、变异可再高 1.01–1.08 倍"（梦幻 §B3）这类分布。</para>
    /// </summary>
    [Table("Game_HunduShijie_PetStatBand")]
    [Comment("宠物资质区间表（与物种 1:1）")]
    [EntityStorage("Game")]
    public class PetStatBandEntity : BaseGameModel<int>
    {
        [Key]
        [Column("species_id", TypeName = "int", Order = 1)]
        [Comment("物种编号")]
        public new int Id { get; set; }

        [Column("attack_min", TypeName = "int", Order = 2)]
        [Comment("攻击资质下界")]
        public int AttackMin { get; set; } = 1000;

        [Column("attack_max", TypeName = "int", Order = 3)]
        [Comment("攻击资质上界")]
        public int AttackMax { get; set; } = 1500;

        [Column("defense_min", TypeName = "int", Order = 4)]
        [Comment("防御资质下界")]
        public int DefenseMin { get; set; } = 1000;

        [Column("defense_max", TypeName = "int", Order = 5)]
        [Comment("防御资质上界")]
        public int DefenseMax { get; set; } = 1500;

        [Column("stamina_min", TypeName = "int", Order = 6)]
        [Comment("体力资质下界")]
        public int StaminaMin { get; set; } = 1000;

        [Column("stamina_max", TypeName = "int", Order = 7)]
        [Comment("体力资质上界")]
        public int StaminaMax { get; set; } = 1500;

        [Column("mana_min", TypeName = "int", Order = 8)]
        [Comment("法力资质下界")]
        public int ManaMin { get; set; } = 1000;

        [Column("mana_max", TypeName = "int", Order = 9)]
        [Comment("法力资质上界")]
        public int ManaMax { get; set; } = 1500;

        [Column("speed_min", TypeName = "int", Order = 10)]
        [Comment("速度资质下界")]
        public int SpeedMin { get; set; } = 1000;

        [Column("speed_max", TypeName = "int", Order = 11)]
        [Comment("速度资质上界")]
        public int SpeedMax { get; set; } = 1500;

        [Column("growth_min_bps", TypeName = "int", Order = 12)]
        [Comment("成长下界 万分比")]
        public int GrowthMinBps { get; set; } = 10000;

        [Column("growth_max_bps", TypeName = "int", Order = 13)]
        [Comment("成长上界 万分比")]
        public int GrowthMaxBps { get; set; } = 12000;

        /// <summary>悟性上限（天龙 10；0 = 该物种不可提悟性，技能格也不会随悟性开放）。</summary>
        [Column("comprehension_max", TypeName = "int", Order = 14)]
        [Comment("悟性上限")]
        public int ComprehensionMax { get; set; } = 10;

        /// <summary>灵性上限（天龙 10；0 = 不可提灵性）。</summary>
        [Column("spirit_max", TypeName = "int", Order = 15)]
        [Comment("灵性上限")]
        public int SpiritMax { get; set; } = 10;

        /// <summary>是否超灵档（灵性加成走 1–34% 而不是 1–31%）。</summary>
        [Column("super_spirit", TypeName = "bit", Order = 16)]
        [Comment("是否超灵档")]
        public bool SuperSpirit { get; set; }

        /// <summary>变异档数（梦幻"额外 8 档"）。</summary>
        [Column("mutation_tier_max", TypeName = "int", Order = 17)]
        [Comment("变异最高档")]
        public int MutationTierMax { get; set; } = 8;
    }

    /// <summary>
    /// 宠物技能槽位规则（与模板 1:1）。
    /// <para>四款游戏的开格判据完全不同（梦幻按物种上限 + 顶书条件、大话按品级、天龙按悟性、
    /// WoW BP 是"池 6 槽 3"），所以一张表里同时容纳三种判据，<b>用 0 关掉不想要的那一种</b>。</para>
    /// </summary>
    [Table("Game_HunduShijie_PetSkillRule")]
    [Comment("宠物技能槽位规则表（与物种 1:1）")]
    [EntityStorage("Game")]
    public class PetSkillRuleEntity : BaseGameModel<int>
    {
        [Key]
        [Column("species_id", TypeName = "int", Order = 1)]
        [Comment("物种编号")]
        public new int Id { get; set; }

        [Column("base_slots", TypeName = "int", Order = 2)]
        [Comment("基础技能格数")]
        public int BaseSlots { get; set; } = 4;

        [Column("absolute_max_slots", TypeName = "int", Order = 3)]
        [Comment("技能格绝对上限")]
        public int AbsoluteMaxSlots { get; set; } = 8;

        /// <summary>按品级给的总格数（CSV，如大话 4,6,8,11,13）；空 = 不按品级分档。</summary>
        [Column("slots_by_grade", TypeName = "nvarchar(64)", Order = 4)]
        [Comment("按品级格数 CSV（空=不分）")]
        public string SlotsByGrade { get; set; } = string.Empty;

        /// <summary>悟性达到该等级 +1 格（天龙 4；0 = 不按悟性开格）。</summary>
        [Column("comprehension_unlock_a", TypeName = "int", Order = 5)]
        [Comment("悟性开格甲")]
        public int ComprehensionUnlockA { get; set; } = 4;

        /// <summary>悟性达到该等级再 +1 格（天龙 7；0 = 关闭）。</summary>
        [Column("comprehension_unlock_b", TypeName = "int", Order = 6)]
        [Comment("悟性开格乙")]
        public int ComprehensionUnlockB { get; set; } = 7;

        /// <summary>顶书开格的参战等级门（梦幻 125）。</summary>
        [Column("slot_grow_max_level", TypeName = "int", Order = 7)]
        [Comment("顶书开格等级门")]
        public int SlotGrowMaxLevel { get; set; } = 125;

        /// <summary>顶书开格的已有技能数门（梦幻 3）。</summary>
        [Column("slot_grow_max_existing", TypeName = "int", Order = 8)]
        [Comment("顶书开格已有技能门")]
        public int SlotGrowMaxExisting { get; set; } = 3;

        /// <summary>顶书开格概率（万分比）。</summary>
        [Column("slot_grow_chance_bps", TypeName = "int", Order = 9)]
        [Comment("顶书开格概率 bps")]
        public int SlotGrowChanceBps { get; set; } = 1500;

        /// <summary>满槽时顶替概率（万分比，大话"大概率顶替"）。</summary>
        [Column("overwrite_chance_bps", TypeName = "int", Order = 10)]
        [Comment("打书顶替概率 bps")]
        public int OverwriteChanceBps { get; set; } = 7000;

        /// <summary>可锁定格数（大话"8 格可锁 2 技"）。</summary>
        [Column("max_lockable_slots", TypeName = "int", Order = 11)]
        [Comment("可锁定格数")]
        public int MaxLockableSlots { get; set; } = 2;

        /// <summary>技能池上限（WoW BP 学 6）。</summary>
        [Column("learned_pool_max", TypeName = "int", Order = 12)]
        [Comment("技能池上限")]
        public int LearnedPoolMax { get; set; } = 6;

        /// <summary>同时装配上限（WoW BP 装 3）。</summary>
        [Column("equipped_max", TypeName = "int", Order = 13)]
        [Comment("同时装配上限")]
        public int EquippedMax { get; set; } = 3;

        /// <summary>内丹格数（梦幻按参战等级 1–6；本列是模板给的固定值，运行时可用等级表覆盖）。</summary>
        [Column("inner_core_slots", TypeName = "int", Order = 14)]
        [Comment("内丹格数")]
        public int InnerCoreSlots { get; set; } = 1;
    }

    /// <summary>
    /// 宠物技能池（一行一个技能位）。
    /// <para>天生必带技、可学池与互斥关系分在<b>同一张表的不同 kind 列</b>上，
    /// 而不是三张表：它们都是"物种 × 技能"的关联，分三张表会让"天生技同时也在可学池里"
    /// 这种冲突只能在运行时才发现。</para>
    /// </summary>
    [Table("Game_HunduShijie_PetSkillPool")]
    [Comment("宠物技能池表（物种 × 技能）")]
    [EntityStorage("Game")]
    public class PetSkillPoolEntity : BaseGameModel<int>
    {
        [Key]
        [Column("entry_id", TypeName = "int", Order = 1)]
        [Comment("条目 id（自增）")]
        public new int Id { get; set; }

        [Column("species_id", TypeName = "int", Order = 2)]
        [Comment("所属物种")]
        public int SpeciesId { get; set; }

        /// <summary>技能 id（引用五行技能树同一 id 域：800000 + 技能id；<b>宠物不另立技能编号</b>）。</summary>
        [Column("skill_id", TypeName = "int", Order = 3)]
        [Comment("技能编号")]
        public int SkillId { get; set; }

        /// <summary>条目种类：0可学 1天生必带 2领悟候选。</summary>
        [Column("kind", TypeName = "tinyint", Order = 4)]
        [Comment("0可学 1天生 2领悟候选")]
        public byte Kind { get; set; }

        /// <summary>天生技所在槽位（大话"恢复后回到第 1 格"）。</summary>
        [Column("innate_slot", TypeName = "int", Order = 5)]
        [Comment("天生技槽位（非天生为 -1）")]
        public int InnateSlot { get; set; } = -1;

        /// <summary>最低学习等级门（天龙的群攻书需对应形态/等级）。</summary>
        [Column("min_level", TypeName = "int", Order = 6)]
        [Comment("最低学习等级")]
        public int MinLevel { get; set; }

        /// <summary>互斥技能 id（CSV）；命中时新技生效、旧技失效（梦幻 §B4）。</summary>
        [Column("conflicts", TypeName = "nvarchar(256)", Order = 7)]
        [Comment("互斥技能 CSV")]
        public string Conflicts { get; set; } = string.Empty;

        /// <summary>是否高级版（"高级借力"给 2 倍加成）。</summary>
        [Column("advanced", TypeName = "bit", Order = 8)]
        [Comment("是否高级版")]
        public bool Advanced { get; set; }

        /// <summary>顺序（编辑器和运行期都按此升序遍历；同 seq 的歧义由 CLI 拒绝保存挡在写入侧）。</summary>
        [Column("seq", TypeName = "int", Order = 9)]
        [Comment("条目顺序")]
        public int Seq { get; set; }
    }
}
