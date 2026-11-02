using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model.GameModel
{
    // 约定与怪物侧一致（见 MonsterWorldEntity.cs 的头注释）：**本文件的表不带 TableDescription**。
    // 半份 TableDescription 会让导出工具产出半个模型，比全没有更难发现。

    /// <summary>
    /// 宠物行为表（与物种 1:1）：AI 智能档位 + 五个能力位的三态覆盖 + 性格 + 跟随参数。
    /// <para><b>能力位为什么存可空 tinyint 而不是 bool</b>：0/1 表达不了"没配，跟档位默认"。
    /// 把"没配"当成"关掉了"会让所有物种退化成木讷档，而编译与建表都不会报错。
    /// 三态展开只在 <c>PetAiFlags.Resolve</c> 里做一次。</para>
    /// </summary>
    [Table("Game_HunduShijie_PetBehavior")]
    [Comment("宠物行为表（与物种 1:1）")]
    [EntityStorage("Game")]
    public class PetBehaviorEntity : BaseGameModel<int>
    {
        [Key]
        [Column("species_id", TypeName = "int", Order = 1)]
        [Comment("物种编号")]
        public new int Id { get; set; }

        /// <summary>智能档位：0木讷 1本能 2训练有素 3聪颖 4通灵。</summary>
        [Column("intellect_tier", TypeName = "tinyint", Order = 2)]
        [Comment("智能档位 0-4")]
        public byte IntellectTier { get; set; } = 2;

        [Column("flag_use_acquire", TypeName = "tinyint", Order = 3)]
        [Comment("自主索敌覆盖 NULL=跟档位")]
        public byte? FlagUseAcquire { get; set; }

        [Column("flag_use_autocast", TypeName = "tinyint", Order = 4)]
        [Comment("自动施法覆盖 NULL=跟档位")]
        public byte? FlagUseAutoCast { get; set; }

        [Column("flag_use_evade", TypeName = "tinyint", Order = 5)]
        [Comment("低血避让覆盖 NULL=跟档位")]
        public byte? FlagUseEvade { get; set; }

        [Column("flag_use_guard", TypeName = "tinyint", Order = 6)]
        [Comment("护主覆盖 NULL=跟档位")]
        public byte? FlagUseGuard { get; set; }

        [Column("flag_use_hold", TypeName = "tinyint", Order = 7)]
        [Comment("保留控制技覆盖 NULL=跟档位")]
        public byte? FlagUseHold { get; set; }

        /// <summary>默认姿态：0畏缩 1被动 2防御 3攻击。</summary>
        [Column("default_stance", TypeName = "tinyint", Order = 8)]
        [Comment("默认姿态 0-3")]
        public byte DefaultStance { get; set; } = 3;

        /// <summary>默认指令：0攻击 1跟随 2待命 3防御 4撤退。</summary>
        [Column("default_command", TypeName = "tinyint", Order = 9)]
        [Comment("默认指令 0-4")]
        public byte DefaultCommand { get; set; } = 1;

        /// <summary>允许性格 CSV：0勇猛 1精明 2胆小 3谨慎 4忠诚。</summary>
        [Column("allowed_personalities", TypeName = "nvarchar(32)", Order = 10)]
        [Comment("允许性格 CSV")]
        public string AllowedPersonalities { get; set; } = "0,3,4";

        /// <summary>领悟率（万分比，大话"每场后概率获得随机技能"）。</summary>
        [Column("epiphany_chance_bps", TypeName = "int", Order = 11)]
        [Comment("领悟概率 bps")]
        public int EpiphanyChanceBps { get; set; } = 1000;

        // ---- 跟随与寻路（数值形状取自 WoW：本作唯一被观测到 leash 的即时制样本）----

        [Column("follow_distance", TypeName = "float", Order = 12)]
        [Comment("理想跟随距离 米")]
        public float FollowDistance { get; set; } = 3f;

        [Column("leash_radius", TypeName = "float", Order = 13)]
        [Comment("脱战 leash 半径 米")]
        public float LeashRadius { get; set; } = 12f;

        [Column("teleport_beyond_radius", TypeName = "float", Order = 14)]
        [Comment("超距传送半径 米")]
        public float TeleportBeyondRadius { get; set; } = 40f;

        [Column("repath_interval_ticks", TypeName = "int", Order = 15)]
        [Comment("重算路径间隔 tick")]
        public int RepathIntervalTicks { get; set; } = 10;

        [Column("follow_speed", TypeName = "float", Order = 16)]
        [Comment("跟随移速 米每秒")]
        public float FollowSpeed { get; set; } = 5.5f;

        [Column("arrive_epsilon", TypeName = "float", Order = 17)]
        [Comment("到点判定距离")]
        public float ArriveEpsilon { get; set; } = 0.6f;

        /// <summary>尸体滞留秒（0 = 不留尸体、可直接重召，天龙口径）。</summary>
        [Column("corpse_seconds", TypeName = "int", Order = 18)]
        [Comment("尸体滞留秒 0=不留")]
        public int CorpseSeconds { get; set; }

        /// <summary>复活施法秒（WoW 经典 4 秒）。</summary>
        [Column("revive_cast_seconds", TypeName = "int", Order = 19)]
        [Comment("复活施法秒")]
        public int ReviveCastSeconds { get; set; } = 4;

        /// <summary>召唤/收回冷却秒（兽栏瞬召 = 0）。</summary>
        [Column("summon_cooldown_seconds", TypeName = "int", Order = 20)]
        [Comment("召唤收回冷却秒")]
        public int SummonCooldownSeconds { get; set; }

        [Column("follow_start_delay_ms", TypeName = "int", Order = 21)]
        [Comment("跟随起步延迟毫秒")]
        public int FollowStartDelayMs { get; set; } = 300;

        // 出生默契四轴：草稿里有这四个值，表里就必须有这四列。
        // 只在草稿里存在而库里没有列 = 编辑器改了、保存回执说成功、下次读取又回到默认值。
        [Column("initial_loyalty", TypeName = "int", Order = 22)]
        [Comment("初始忠诚 0-100")]
        public int InitialLoyalty { get; set; } = 100;

        [Column("initial_happiness", TypeName = "int", Order = 23)]
        [Comment("初始快乐（刻度见物种表 happiness_scale）")]
        public int InitialHappiness { get; set; } = 700;

        [Column("initial_lifespan", TypeName = "int", Order = 24)]
        [Comment("初始寿命点数")]
        public int InitialLifespan { get; set; } = 100;

        [Column("initial_intimacy", TypeName = "int", Order = 25)]
        [Comment("初始亲密度")]
        public int InitialIntimacy { get; set; }
    }

    /// <summary>
    /// 宠物装备规则（与物种 1:1）。
    /// <para><b>部位集合属于物种而不是全局常量</b>：梦幻 3 件、大话 3 件、天龙 5 件、WoW 0 件。
    /// <see cref="AllowedSlots"/> 为空即"这个物种没有装备系统"，运行时必须据此拒绝穿戴并给出原因，
    /// 而不是允许穿上再加成一个没人读的死值。</para>
    /// </summary>
    [Table("Game_HunduShijie_PetEquipRule")]
    [Comment("宠物装备规则表（与物种 1:1）")]
    [EntityStorage("Game")]
    public class PetEquipRuleEntity : BaseGameModel<int>
    {
        [Key]
        [Column("species_id", TypeName = "int", Order = 1)]
        [Comment("物种编号")]
        public new int Id { get; set; }

        /// <summary>开放部位 CSV（0护腕 1项圈 2躯干 3头 4爪 5护符 6环 7铃）；空 = 无装备。</summary>
        [Column("allowed_slots", TypeName = "nvarchar(64)", Order = 2)]
        [Comment("开放部位 CSV 空=无装备")]
        public string AllowedSlots { get; set; } = string.Empty;

        /// <summary>是否允许镶嵌灵石（梦幻允许、天龙只可强化不可镶宝石）。</summary>
        [Column("gem_allowed", TypeName = "bit", Order = 3)]
        [Comment("是否允许灵石")]
        public bool GemAllowed { get; set; } = true;

        /// <summary>最高星级（正文 5、怀旧 7）。</summary>
        [Column("max_star", TypeName = "int", Order = 4)]
        [Comment("最高星级")]
        public int MaxStar { get; set; } = 5;

        /// <summary>套装族（0无 1按性格成套 2按部位成套）。</summary>
        [Column("set_family", TypeName = "tinyint", Order = 5)]
        [Comment("套装族")]
        public byte SetFamily { get; set; } = 1;

        /// <summary>是否启用资质鉴定（天龙；关闭时鉴定列一律按"普通"算）。</summary>
        [Column("appraisal_enabled", TypeName = "bit", Order = 6)]
        [Comment("是否启用鉴定")]
        public bool AppraisalEnabled { get; set; } = true;

        /// <summary>是否启用觉醒格（大话：三件装备齐 + 品质/强化门槛）。</summary>
        [Column("awakening_enabled", TypeName = "bit", Order = 7)]
        [Comment("是否启用觉醒格")]
        public bool AwakeningEnabled { get; set; }

        /// <summary>觉醒格所需的件数（大话为 3）。</summary>
        [Column("awakening_piece_count", TypeName = "int", Order = 8)]
        [Comment("觉醒所需件数")]
        public int AwakeningPieceCount { get; set; } = 3;

        /// <summary>装备归属：0绑定宠物 1绑定角色可跨宠复用（天龙 §D7）。</summary>
        [Column("ownership", TypeName = "tinyint", Order = 9)]
        [Comment("装备归属 0绑宠1绑角色")]
        public byte Ownership { get; set; } = 1;
    }

    /// <summary>
    /// 宠物辅助规则（一行一个"辅助技能 → 主人属性"的映射）。
    /// <para>反哺主人是四款游戏差异最大的维度（天龙最强、WoW 完全没有），所以每条映射都单独成行、
    /// 可以逐条停用；把"反哺"写成一个总开关会让它只能全有或全无。</para>
    /// </summary>
    [Table("Game_HunduShijie_PetAssistRule")]
    [Comment("宠物辅助（反哺主人）规则表")]
    [EntityStorage("Game")]
    public class PetAssistRuleEntity : BaseGameModel<int>
    {
        [Key]
        [Column("entry_id", TypeName = "int", Order = 1)]
        [Comment("条目 id（自增）")]
        public new int Id { get; set; }

        [Column("species_id", TypeName = "int", Order = 2)]
        [Comment("所属物种")]
        public int SpeciesId { get; set; }

        [Column("skill_id", TypeName = "int", Order = 3)]
        [Comment("辅助技能编号")]
        public int SkillId { get; set; }

        /// <summary>作用类型：0力量 1灵气 2体力 3身法 4定力 5生命转化 6内力回复 7替挡会心 8吸敌内力 9伤害转化。</summary>
        [Column("assist_kind", TypeName = "tinyint", Order = 4)]
        [Comment("辅助类型 0-9")]
        public byte AssistKind { get; set; }

        /// <summary>是否出战才生效（天龙措辞"出战后"；关掉则留守也生效）。</summary>
        [Column("requires_battle", TypeName = "bit", Order = 5)]
        [Comment("是否出战才生效")]
        public bool RequiresBattle { get; set; } = true;

        /// <summary>是否可走附体位（附体位只按等级/资质给，<b>不受技能等级影响</b>）。</summary>
        [Column("possessable", TypeName = "bit", Order = 6)]
        [Comment("是否可附体")]
        public bool Possessable { get; set; } = true;

        /// <summary>系数覆盖（万分比；0 = 用内核默认）。给"高级借力 ×2"这类逐技能修正留口子。</summary>
        [Column("coefficient_bps", TypeName = "int", Order = 7)]
        [Comment("系数覆盖 bps 0=默认")]
        public int CoefficientBps { get; set; }

        [Column("seq", TypeName = "int", Order = 8)]
        [Comment("条目顺序")]
        public int Seq { get; set; }
    }

    /// <summary>
    /// 宠物合成/繁殖规则（与物种 1:1）。
    /// <para>炼妖（梦幻）与繁殖（天龙）在数据上是"同一张表的不同 mode 位"，
    /// 但在**校验上是两条分开的通路**（见 <c>PetMergeCore</c>）：合并校验会让炼妖也要求异性。</para>
    /// </summary>
    [Table("Game_HunduShijie_PetMergeRule")]
    [Comment("宠物合成/繁殖规则表（与物种 1:1）")]
    [EntityStorage("Game")]
    public class PetMergeRuleEntity : BaseGameModel<int>
    {
        [Key]
        [Column("species_id", TypeName = "int", Order = 1)]
        [Comment("物种编号")]
        public new int Id { get; set; }

        /// <summary>是否可炼妖（变异宝宝为 false，梦幻 §B1）。</summary>
        [Column("fuse_enabled", TypeName = "bit", Order = 2)]
        [Comment("是否可炼妖")]
        public bool FuseEnabled { get; set; } = true;

        /// <summary>是否可繁殖（变异/二代为 false，天龙 §D9）。</summary>
        [Column("breed_enabled", TypeName = "bit", Order = 3)]
        [Comment("是否可繁殖")]
        public bool BreedEnabled { get; set; } = true;

        [Column("fuse_min_level", TypeName = "int", Order = 4)]
        [Comment("炼妖最低等级")]
        public int FuseMinLevel { get; set; } = 30;

        [Column("fuse_success_bps", TypeName = "int", Order = 5)]
        [Comment("炼妖基准成功率 bps")]
        public int FuseSuccessBps { get; set; } = 6000;

        [Column("fuse_quality_min_bps", TypeName = "int", Order = 6)]
        [Comment("资质继承带下界 bps")]
        public int FuseQualityMinBps { get; set; } = 7000;

        [Column("fuse_quality_max_bps", TypeName = "int", Order = 7)]
        [Comment("资质继承带上界 bps")]
        public int FuseQualityMaxBps { get; set; } = 11000;

        [Column("fuse_growth_min_bps", TypeName = "int", Order = 8)]
        [Comment("成长继承带下界 bps")]
        public int FuseGrowthMinBps { get; set; } = 9600;

        [Column("fuse_growth_max_bps", TypeName = "int", Order = 9)]
        [Comment("成长继承带上界 bps")]
        public int FuseGrowthMaxBps { get; set; } = 10200;

        [Column("fuse_max_inherited_skills", TypeName = "int", Order = 10)]
        [Comment("继承技能上限")]
        public int FuseMaxInheritedSkills { get; set; } = 8;

        /// <summary>失败时是否仍消耗材料（梦幻为是；关掉它会让"失败不吃料"成为默认承诺）。</summary>
        [Column("fuse_consumes_parents", TypeName = "bit", Order = 11)]
        [Comment("炼妖失败是否仍消耗材料")]
        public bool FuseConsumesParents { get; set; } = true;

        [Column("breed_max_level_gap", TypeName = "int", Order = 12)]
        [Comment("繁殖等级差上限")]
        public int BreedMaxLevelGap { get; set; } = 5;

        [Column("breed_required_happiness", TypeName = "int", Order = 13)]
        [Comment("繁殖要求快乐")]
        public int BreedRequiredHappiness { get; set; } = 100;

        [Column("breed_required_lifespan", TypeName = "int", Order = 14)]
        [Comment("繁殖要求寿命")]
        public int BreedRequiredLifespan { get; set; } = 1000;

        [Column("breed_offspring_count", TypeName = "int", Order = 15)]
        [Comment("繁殖产仔数")]
        public int BreedOffspringCount { get; set; } = 2;

        /// <summary>领取时限秒（48 小时不领取则回归自然）。</summary>
        [Column("breed_claim_window_seconds", TypeName = "int", Order = 16)]
        [Comment("繁殖领取时限秒")]
        public int BreedClaimWindowSeconds { get; set; } = 172800;

        /// <summary>队长货币门槛（天龙"≥2 两黄金"；单位由运营定义）。</summary>
        [Column("breed_cost", TypeName = "int", Order = 17)]
        [Comment("繁殖货币门槛")]
        public int BreedCost { get; set; } = 2;
    }

    /// <summary>
    /// 临时变异模板（一行一种可施加的临时状态：染色/幻化/事件变体/限时鎏金）。
    /// <para>永久变异住在 <see cref="PetInstanceEntity"/> 的 <c>mutation_tier</c> 列上（出身的一部分）；
    /// 本表只描述**有有效期、到期整体回退**的那一层。两层混在一张表里，一次染色过期就会连带
    /// 把变异宝宝洗成普通种——那是数据损失，不是显示问题。</para>
    /// </summary>
    [Table("Game_HunduShijie_PetMutationRule")]
    [Comment("宠物临时变异模板表")]
    [EntityStorage("Game")]
    public class PetMutationRuleEntity : BaseGameModel<int>
    {
        [Key]
        [Column("entry_id", TypeName = "int", Order = 1)]
        [Comment("条目 id（自增）")]
        public new int Id { get; set; }

        [Column("species_id", TypeName = "int", Order = 2)]
        [Comment("所属物种（0=全物种通用）")]
        public int SpeciesId { get; set; }

        [Column("mutation_key", TypeName = "nvarchar(64)", Order = 3)]
        [Comment("临时变异键")]
        public string MutationKey { get; set; } = string.Empty;

        /// <summary>持续 tick（工程按 10Hz：1 小时 = 36000）。</summary>
        [Column("duration_ticks", TypeName = "int", Order = 4)]
        [Comment("持续 tick")]
        public int DurationTicks { get; set; } = 36000;

        [Column("quality_cap_bps", TypeName = "int", Order = 5)]
        [Comment("资质上限放大 bps")]
        public int QualityCapBps { get; set; } = 10000;

        [Column("color_override", TypeName = "nvarchar(32)", Order = 6)]
        [Comment("覆盖色键")]
        public string? ColorOverride { get; set; }

        /// <summary>临时追加技能格（0 = 不改格子）。</summary>
        [Column("extra_skill_slots", TypeName = "int", Order = 7)]
        [Comment("临时追加技能格")]
        public int ExtraSkillSlots { get; set; }

        [Column("enabled", TypeName = "bit", Order = 8)]
        [Comment("是否启用")]
        public bool Enabled { get; set; } = true;
    }

    /// <summary>
    /// 宠物个体实例（**运行时权威表**：一只具体宠物一行，被抓后永久存在）。
    /// <para>
    /// <b>这张表是"宠物编号 + 重复副本"问题的落点</b>：<c>pet_instance_id</c> = 物种号 × 100000 + 单调序号
    /// （算法在 <c>PetIdentityCore.NewInstanceId</c>，不要在 SQL 里重算）。同一个 <c>species_id</c>
    /// 可以有任意多行——任何按 (owner, species) 的去重都会静默吃掉玩家的第二、第三只宠。
    /// </para>
    /// <para>
    /// <b>昵称只用于展示，重复副本必须靠 <c>duplicate_fingerprint</c> 与实例号区分</b>：
    /// 四款游戏都只给玩家看"名字 + 等级 + 资质面板"，但服务端不能拿名字当身份。
    /// </para>
    /// <para>
    /// <b>本期只建表、不接 Orleans Grain</b>（设计文档 §8 D8）：读写通路留在 <c>docs/PET_EDITOR_PLUGIN_DESIGN.md</c> §9 的接缝清单里。
    /// </para>
    /// </summary>
    [Table("Game_HunduShijie_PetInstance")]
    [Comment("宠物个体实例表（运行时权威；本期未接入 Grain）")]
    [EntityStorage("Game")]
    public class PetInstanceEntity : BaseGameModel<long>
    {
        [Key]
        [Column("pet_instance_id", TypeName = "bigint", Order = 1)]
        [Comment("个体编号（物种×100000+序号；永不复用）")]
        public new long Id { get; set; }

        [Column("species_id", TypeName = "int", Order = 2)]
        [Comment("物种编号")]
        public int SpeciesId { get; set; }

        [Column("owner_character_id", TypeName = "bigint", Order = 3)]
        [Comment("主人角色 id（Game_HunduShijie_Character.Id；宠物归属角色，不跨角色共享）")]
        public long OwnerCharacterId { get; set; }

        [Column("nickname", TypeName = "nvarchar(64)", Order = 4)]
        [Comment("昵称（仅展示）")]
        public string Nickname { get; set; } = string.Empty;

        /// <summary>形态阶段：0蛋 1幼体 2成体 3觉醒体。</summary>
        [Column("generation", TypeName = "tinyint", Order = 5)]
        [Comment("形态阶段 0-3")]
        public byte Generation { get; set; } = 1;

        /// <summary>出身：0野生 1宝宝 2成年 3变异 4二代 5神兽。</summary>
        [Column("origin", TypeName = "tinyint", Order = 6)]
        [Comment("出身 0-5")]
        public byte Origin { get; set; }

        /// <summary>个体状态（<c>PetInstanceState</c> 的序号 0–9）。</summary>
        [Column("state", TypeName = "tinyint", Order = 7)]
        [Comment("个体状态 0-9")]
        public byte State { get; set; } = 2;

        [Column("level", TypeName = "int", Order = 8)]
        [Comment("等级")]
        public int Level { get; set; }

        [Column("current_xp", TypeName = "bigint", Order = 9)]
        [Comment("本级经验")]
        public long CurrentXp { get; set; }

        [Column("potential_points", TypeName = "int", Order = 10)]
        [Comment("未分配潜能点")]
        public int PotentialPoints { get; set; }

        [Column("alloc_strength", TypeName = "int", Order = 11)]
        [Comment("力量已分配")]
        public int AllocStrength { get; set; }

        [Column("alloc_endurance", TypeName = "int", Order = 12)]
        [Comment("耐力已分配")]
        public int AllocEndurance { get; set; }

        [Column("alloc_constitution", TypeName = "int", Order = 13)]
        [Comment("体质已分配")]
        public int AllocConstitution { get; set; }

        [Column("alloc_spirit", TypeName = "int", Order = 14)]
        [Comment("灵性已分配")]
        public int AllocSpirit { get; set; }

        [Column("alloc_agility", TypeName = "int", Order = 15)]
        [Comment("敏捷已分配")]
        public int AllocAgility { get; set; }

        [Column("attack_quality", TypeName = "int", Order = 16)]
        [Comment("攻击资质（裸资）")]
        public int AttackQuality { get; set; } = 1000;

        [Column("defense_quality", TypeName = "int", Order = 17)]
        [Comment("防御资质（裸资）")]
        public int DefenseQuality { get; set; } = 1000;

        [Column("stamina_quality", TypeName = "int", Order = 18)]
        [Comment("体力资质（裸资）")]
        public int StaminaQuality { get; set; } = 1000;

        [Column("mana_quality", TypeName = "int", Order = 19)]
        [Comment("法力资质（裸资）")]
        public int ManaQuality { get; set; } = 1000;

        [Column("speed_quality", TypeName = "int", Order = 20)]
        [Comment("速度资质（裸资）")]
        public int SpeedQuality { get; set; } = 1000;

        [Column("growth_bps", TypeName = "int", Order = 21)]
        [Comment("成长率 万分比")]
        public int GrowthBps { get; set; } = 10000;

        [Column("comprehension_level", TypeName = "int", Order = 22)]
        [Comment("悟性 0-10")]
        public int ComprehensionLevel { get; set; }

        [Column("spirit_level", TypeName = "int", Order = 23)]
        [Comment("灵性 0-10")]
        public int SpiritLevel { get; set; }

        [Column("fusion_level", TypeName = "int", Order = 24)]
        [Comment("融合度 0-10")]
        public int FusionLevel { get; set; }

        [Column("loyalty", TypeName = "int", Order = 25)]
        [Comment("忠诚 0-100")]
        public int Loyalty { get; set; } = 100;

        [Column("happiness", TypeName = "int", Order = 26)]
        [Comment("快乐（刻度见物种表 happiness_scale）")]
        public int Happiness { get; set; } = 700;

        [Column("lifespan", TypeName = "int", Order = 27)]
        [Comment("寿命点数")]
        public int Lifespan { get; set; } = 100;

        [Column("intimacy", TypeName = "int", Order = 28)]
        [Comment("亲密度")]
        public int Intimacy { get; set; }

        /// <summary>技能槽 JSON（<c>[{skillId,kind,level,locked}]</c>）；结构由 PetInstanceCodec 唯一负责。</summary>
        [Column("skill_slots_json", TypeName = "nvarchar(max)", Order = 29)]
        [Comment("技能槽 JSON")]
        public string SkillSlotsJson { get; set; } = "[]";

        /// <summary>穿戴清单 JSON（装备实例 id + 部位；归属可为角色）。</summary>
        [Column("equipment_json", TypeName = "nvarchar(max)", Order = 30)]
        [Comment("穿戴清单 JSON")]
        public string EquipmentJson { get; set; } = "[]";

        /// <summary>永久变异档（0 = 无变异）。</summary>
        [Column("mutation_tier", TypeName = "int", Order = 31)]
        [Comment("永久变异档 0=无")]
        public int MutationTier { get; set; }

        [Column("mutation_color_key", TypeName = "nvarchar(32)", Order = 32)]
        [Comment("永久变异色键")]
        public string? MutationColorKey { get; set; }

        /// <summary>临时变异 JSON（<c>{key,startTick,expireTick,...}</c>）；空 = 没有临时层。到期整体回退。</summary>
        [Column("timed_mutation_json", TypeName = "nvarchar(max)", Order = 33)]
        [Comment("临时变异 JSON（空=无）")]
        public string? TimedMutationJson { get; set; }

        [Column("personality", TypeName = "tinyint", Order = 34)]
        [Comment("性格 0-4")]
        public byte Personality { get; set; }

        [Column("intellect_tier_override", TypeName = "tinyint", Order = 35)]
        [Comment("智能档位个体覆盖 NULL=用物种")]
        public byte? IntellectTierOverride { get; set; }

        /// <summary>孵化进度秒（<see cref="Generation"/> 为蛋时有效）。</summary>
        [Column("hatch_progress_seconds", TypeName = "int", Order = 36)]
        [Comment("孵化进度秒")]
        public int HatchProgressSeconds { get; set; }

        /// <summary>已繁殖次数（决定婚配等级，天龙每次 +20）。</summary>
        [Column("breeding_count", TypeName = "int", Order = 37)]
        [Comment("已繁殖次数")]
        public int BreedingCount { get; set; }

        [Column("spouse_instance_id", TypeName = "bigint", Order = 38)]
        [Comment("配偶个体编号 0=无")]
        public long SpouseInstanceId { get; set; }

        /// <summary>领取期限 tick（繁殖产物 48 小时不领取则回归自然；0 = 无期限）。</summary>
        [Column("claim_deadline_tick", TypeName = "bigint", Order = 39)]
        [Comment("领取期限 tick 0=无")]
        public long ClaimDeadlineTick { get; set; }

        /// <summary>捕捉来源描述（场景/怪物编号/方式；审计用，<b>不参与判等</b>）。</summary>
        [Column("capture_source", TypeName = "nvarchar(128)", Order = 40)]
        [Comment("捕捉来源")]
        public string CaptureSource { get; set; } = string.Empty;

        [Column("captured_at", TypeName = "datetime2", Order = 41)]
        [Comment("获得时间")]
        public DateTime CapturedAt { get; set; }

        /// <summary>重复副本指纹（物种 + 形态 + 资质哈希 + 成长）；<b>只用于对账与展示，绝不用于身份判定</b>。</summary>
        [Column("duplicate_fingerprint", TypeName = "bigint", Order = 42)]
        [Comment("重复副本指纹")]
        public long DuplicateFingerprint { get; set; }

        [Column("content_version", TypeName = "int", Order = 43)]
        [Comment("内容版本（乐观锁）")]
        public int ContentVersion { get; set; } = 1;

        [Column("remark", TypeName = "nvarchar(500)", Order = 44)]
        [Comment("备注")]
        public string Remark { get; set; } = string.Empty;
    }

    /// <summary>
    /// 宠物个体事件流水（捕捉/孵化/打书/蜕变/炼妖/繁殖/放生）。
    /// <para>宠物永久存在 ⇒ "这只怎么变成现在这样"必须有可审计的历史。
    /// 差分状态本身不够：历史表让"某次打书顶掉了哪个技能"这种玩家投诉能被回答。</para>
    /// </summary>
    [Table("Game_HunduShijie_PetInstanceEvent")]
    [Comment("宠物个体事件流水表")]
    [EntityStorage("Game")]
    public class PetInstanceEventEntity : BaseGameModel<int>
    {
        [Key]
        [Column("event_id", TypeName = "int", Order = 1)]
        [Comment("事件 id（自增）")]
        public new int Id { get; set; }

        [Column("pet_instance_id", TypeName = "bigint", Order = 2)]
        [Comment("个体编号")]
        public long PetInstanceId { get; set; }

        /// <summary>事件类型：0捕捉 1孵化 2升级 3打书 4领悟 5洗炼 6蜕变 7提悟性 8提灵性 9提融合 10炼妖 11繁殖 12领取 13穿戴 14放生 15临时变异 16临时变异到期。</summary>
        [Column("event_kind", TypeName = "tinyint", Order = 3)]
        [Comment("事件类型 0-16")]
        public byte EventKind { get; set; }

        [Column("occurred_at", TypeName = "datetime2", Order = 4)]
        [Comment("发生时间")]
        public DateTime OccurredAt { get; set; }

        /// <summary>结果明细 JSON（含被顶技能、roll 值、产物编号等；结构由写入方版本化）。</summary>
        [Column("payload_json", TypeName = "nvarchar(max)", Order = 5)]
        [Comment("结果明细 JSON")]
        public string PayloadJson { get; set; } = "{}";

        /// <summary>确定性种子（打书/炼妖类事件必须记下种子，否则"这次为什么顶了我的连击"无法复现）。</summary>
        [Column("seed", TypeName = "bigint", Order = 6)]
        [Comment("确定性种子")]
        public long Seed { get; set; }

        [Column("operator_user_id", TypeName = "uniqueidentifier", Order = 7)]
        [Comment("操作者（GM 操作时与主人不同）")]
        public Guid? OperatorUserId { get; set; }
    }
}
