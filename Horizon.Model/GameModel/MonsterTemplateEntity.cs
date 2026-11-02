using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model.GameModel
{
    /// <summary>
    /// 怪物模板实体（怪物编辑器的**主表**：编号 → 名字/外观/等级/属性公式/经验/刷新）。
    /// <para>
    /// <b>为什么数值存公式而不是存数字</b>：同一模板要在不同区域以不同等级铺开
    /// （WoW 观测：同怪在 A 处 Lv10、B 处 Lv12）。逐行数字会把"等级范围"变成"等级清单"，
    /// 编辑器无法维护。公式给形状，BOSS 这类要手调的走 <c>*_override_bps</c> 覆盖列（基点，0 = 不覆盖）。
    /// </para>
    /// <para>
    /// <b>为什么权威必须在服务端</b>：与 <see cref="QuestTemplateEntity"/> 同一条理由——
    /// 若属性/经验可由调用方声明，客户端就能自己说"这只怪给我 3000 经验"。所以模板在库里，
    /// 运行时只读，编辑器只写库。
    /// </para>
    /// </summary>
    [Table("Game_HunduShijie_MonsterTemplate"), TableDescription(Name = "Game_HunduShijie_MonsterTemplate", Order = "HunduShijie_030", Description = "怪物模板信息")]
    [Comment("怪物模板表（怪物编辑器主表）")]
    [EntityStorage("Game")]
    public class MonsterTemplateEntity : BaseGameModel<int>
    {
        /// <summary>
        /// 怪物编号（号段规则：1–99999 普通野怪、100000+ 精英/稀有精英、200000+ 世界 BOSS、300000+ 副本怪）。
        /// 既有 1–11 **不得改号**（脚本生成白名单与头顶信息卡显示断言按它们写过）。
        /// </summary>
        [Key]
        [Column("monster_id", TypeName = "int", Order = 1), TableDescription(TypeName = "int", Name = "monster_id", Order = "1", Description = "怪物编号")]
        [Comment("怪物编号（号段即身份）")]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public new int Id { get; set; }

        /// <summary>显示名。不得为空——空名会让客户端回退成 "Monster_&lt;id&gt;"。</summary>
        [Required]
        [Column("name", TypeName = "nvarchar(64)", Order = 2), TableDescription(TypeName = "nvarchar(64)", Name = "name", Order = "2", Description = "怪物名称")]
        [Comment("显示名")]
        public string Name { get; set; } = string.Empty;

        /// <summary>rank：0普通 1精英 2稀有精英 3首领 4世界BOSS（决定血条样式、经验/属性倍率、刷新量级）。</summary>
        [Column("rank", TypeName = "tinyint", Order = 3), TableDescription(TypeName = "tinyint", Name = "rank", Order = "3", Description = "怪物档位")]
        [Comment("rank 0普通 1精英 2稀有精英 3首领 4世界BOSS")]
        public byte Rank { get; set; }

        /// <summary>种族：0人形 1野兽 2元素 3龙类 4巨人 5亡灵 6机械 7昆虫（求援"同族"与检测类型的判据）。</summary>
        [Column("species", TypeName = "tinyint", Order = 4), TableDescription(TypeName = "tinyint", Name = "species", Order = "4", Description = "种族")]
        [Comment("种族编号")]
        public byte Species { get; set; }

        /// <summary>五行域（1金2木3水4火5土，0=未定域）。既是掉落兜底表号也是好感来源，见 MonsterTemplateCatalog。</summary>
        [Column("wuxing_domain", TypeName = "int", Order = 5), TableDescription(TypeName = "int", Name = "wuxing_domain", Order = "5", Description = "五行域")]
        [Comment("五行域 1金2木3水4火5土 0=未定")]
        public int WuxingDomain { get; set; }

        /// <summary>客户端模型键（稳定标识，供 prefab 分流）。</summary>
        [Column("model_key", TypeName = "nvarchar(64)", Order = 6), TableDescription(TypeName = "nvarchar(64)", Name = "model_key", Order = "6", Description = "模型键")]
        [Comment("客户端模型键")]
        public string ModelKey { get; set; } = string.Empty;

        /// <summary>直接指认 prefab 资产 Guid（可空；空则按 model_key 查表）。加载失败必须显式回退并记诊断。</summary>
        [Column("prefab_guid", TypeName = "nvarchar(64)", Order = 7), TableDescription(TypeName = "nvarchar(64)", Name = "prefab_guid", Order = "7", Description = "预制体Guid")]
        [Comment("预制体资产 Guid（可空）")]
        public string? PrefabGuid { get; set; }

        /// <summary>
        /// prefab 的**内容树路径**（可空；如 <c>Content/Game/Monsters/MoQilin.prefab</c>）。
        /// <para>
        /// 为什么 GUID 之外还要一列路径：GUID 在服务端只是"设计者指认了哪个资产"的意图记录，
        /// 客户端能不能按它解析取决于打包；本工程实测稳定的加载入口是**路径**
        /// （<c>TryGetAssetGuidFromPath</c> 现算真实 GUID），所以导给客户端的外观表必须带路径。
        /// 空路径 = 只登记了意图、还没进包，客户端按 model_key 查表后仍回退占位。
        /// </para>
        /// </summary>
        [Column("prefab_path", TypeName = "nvarchar(256)", Order = 32), TableDescription(TypeName = "nvarchar(256)", Name = "prefab_path", Order = "32", Description = "预制体路径")]
        [Comment("预制体内容路径（客户端按路径解析 GUID）")]
        public string? PrefabPath { get; set; }

        /// <summary>体型缩放（精英常见 1.0/1.2/1.5）。</summary>
        [Column("model_scale", TypeName = "decimal(4,2)", Order = 8), TableDescription(TypeName = "decimal(4,2)", Name = "model_scale", Order = "8", Description = "体型缩放")]
        [Comment("体型缩放")]
        public decimal ModelScale { get; set; } = 1.0m;

        /// <summary>待机姿态：0站 1坐 2卧 3潜于水下。</summary>
        [Column("stand_state", TypeName = "tinyint", Order = 9), TableDescription(TypeName = "tinyint", Name = "stand_state", Order = "9", Description = "待机姿态")]
        [Comment("待机姿态 0站1坐2卧3潜水")]
        public byte StandState { get; set; }

        /// <summary>模板基准等级（刷怪点可覆盖，覆盖不得超出下面两列的区间）。</summary>
        [Column("level_base", TypeName = "int", Order = 10), TableDescription(TypeName = "int", Name = "level_base", Order = "10", Description = "基准等级")]
        [Comment("基准等级")]
        public int LevelBase { get; set; } = 1;

        /// <summary>允许等级下界。</summary>
        [Column("level_min", TypeName = "int", Order = 11), TableDescription(TypeName = "int", Name = "level_min", Order = "11", Description = "等级下界")]
        [Comment("等级下界")]
        public int LevelMin { get; set; } = 1;

        /// <summary>允许等级上界。</summary>
        [Column("level_max", TypeName = "int", Order = 12), TableDescription(TypeName = "int", Name = "level_max", Order = "12", Description = "等级上界")]
        [Comment("等级上界")]
        public int LevelMax { get; set; } = 1;

        [Column("hp_base", TypeName = "int", Order = 13), TableDescription(TypeName = "int", Name = "hp_base", Order = "13", Description = "生命截距")]
        [Comment("生命公式截距")]
        public int HpBase { get; set; } = 20;

        [Column("hp_per_level", TypeName = "int", Order = 14), TableDescription(TypeName = "int", Name = "hp_per_level", Order = "14", Description = "生命每级增量")]
        [Comment("生命公式每级增量")]
        public int HpPerLevel { get; set; } = 6;

        [Column("atk_base", TypeName = "int", Order = 15), TableDescription(TypeName = "int", Name = "atk_base", Order = "15", Description = "攻击截距")]
        [Comment("攻击公式截距")]
        public int AtkBase { get; set; } = 2;

        [Column("atk_per_level", TypeName = "int", Order = 16), TableDescription(TypeName = "int", Name = "atk_per_level", Order = "16", Description = "攻击每级增量")]
        [Comment("攻击公式每级增量")]
        public int AtkPerLevel { get; set; } = 1;

        [Column("def_base", TypeName = "int", Order = 17), TableDescription(TypeName = "int", Name = "def_base", Order = "17", Description = "防御截距")]
        [Comment("防御公式截距")]
        public int DefBase { get; set; }

        [Column("def_per_level", TypeName = "int", Order = 18), TableDescription(TypeName = "int", Name = "def_per_level", Order = "18", Description = "防御每级增量")]
        [Comment("防御公式每级增量")]
        public int DefPerLevel { get; set; }

        /// <summary>血量覆盖基点（0 = 不覆盖；15000 = 按公式再 ×1.5）。BOSS 手调用，不必改公式。</summary>
        [Column("hp_override_bps", TypeName = "int", Order = 19), TableDescription(TypeName = "int", Name = "hp_override_bps", Order = "19", Description = "血量覆盖基点")]
        [Comment("血量覆盖基点 0=不覆盖")]
        public int HpOverrideBps { get; set; }

        /// <summary>攻击覆盖基点（0 = 不覆盖）。</summary>
        [Column("atk_override_bps", TypeName = "int", Order = 20), TableDescription(TypeName = "int", Name = "atk_override_bps", Order = "20", Description = "攻击覆盖基点")]
        [Comment("攻击覆盖基点 0=不覆盖")]
        public int AtkOverrideBps { get; set; }

        /// <summary>经验基准截距（WoW 旧世界观测 45；外域档 235）。</summary>
        [Column("xp_base", TypeName = "int", Order = 21), TableDescription(TypeName = "int", Name = "xp_base", Order = "21", Description = "经验截距")]
        [Comment("经验基准截距")]
        public int XpBase { get; set; } = 45;

        /// <summary>经验每级增量（定基口径=**怪物等级**，见 MonsterXpCore 的决策 D3 说明）。</summary>
        [Column("xp_per_level", TypeName = "int", Order = 22), TableDescription(TypeName = "int", Name = "xp_per_level", Order = "22", Description = "经验每级增量")]
        [Comment("经验每级增量")]
        public int XpPerLevel { get; set; } = 5;

        /// <summary>
        /// 逐模板经验倍率基点（10000 = ×1）——魔兽 <c>CreatureTemplate::ModExperience</c>
        /// （AzerothCore <c>Miscellaneous/Formulas.cpp:90</c>，与精英 ×2 在同一次取整之前乘进去）。
        /// 没有它，"同为 20 级精英，剧情怪给三倍"只能改 xp_base 或改等级去凑，而那两个旋钮会波及别的怪。
        /// </summary>
        [Column("xp_mod_bps", TypeName = "int", Order = 29), TableDescription(TypeName = "int", Name = "xp_mod_bps", Order = "29", Description = "逐模板经验倍率基点")]
        [Comment("逐模板经验倍率基点（10000=×1）")]
        public int XpModBps { get; set; } = 10000;

        /// <summary>
        /// "血薄的怪给更少经验"基点——魔兽 <c>CreatureTemplate::ModHealth</c>
        /// （AzerothCore <c>Entities/Player/KillRewarder.cpp:136-138</c>）。
        /// 照抄它那条容易被误读的门槛：<b>只有 0..7500 生效</b>，9000 与没配一样，15000 也不会放大。
        /// 缺省 10000（=不削减），旧行与旧快照文件里没有这一列时行为逐字不变。
        /// </summary>
        [Column("health_mod_bps", TypeName = "int", Order = 30), TableDescription(TypeName = "int", Name = "health_mod_bps", Order = "30", Description = "血薄削减经验基点（≤7500 才生效）")]
        [Comment("血薄削减经验基点（只有 0..7500 生效，10000=不削减）")]
        public int HealthModBps { get; set; } = 10000;

        /// <summary>刷新秒公式截距。</summary>
        [Column("respawn_base_seconds", TypeName = "float", Order = 23), TableDescription(TypeName = "float", Name = "respawn_base_seconds", Order = "23", Description = "刷新截距秒")]
        [Comment("刷新秒截距")]
        public float RespawnBaseSeconds { get; set; } = 15f;

        /// <summary>刷新秒每级增量（"等级越高刷越慢"的那条斜率）。</summary>
        [Column("respawn_per_level_seconds", TypeName = "float", Order = 24), TableDescription(TypeName = "float", Name = "respawn_per_level_seconds", Order = "24", Description = "刷新每级秒")]
        [Comment("刷新秒每级增量")]
        public float RespawnPerLevelSeconds { get; set; } = 0.5f;

        /// <summary>尸体停留秒（死亡后仍可拾取/交互的时间窗）。</summary>
        [Column("corpse_seconds", TypeName = "float", Order = 25), TableDescription(TypeName = "float", Name = "corpse_seconds", Order = "25", Description = "尸体停留秒")]
        [Comment("尸体停留秒")]
        public float CorpseSeconds { get; set; } = 60f;

        /// <summary>索敌喊话/表情 id（玩家的"预警"，可空）。</summary>
        [Column("aggro_say_id", TypeName = "int", Order = 26), TableDescription(TypeName = "int", Name = "aggro_say_id", Order = "26", Description = "索敌喊话")]
        [Comment("索敌喊话 id")]
        public int AggroSayId { get; set; }

        /// <summary>发布状态：0草稿 1已发布 2已停用。运行时只装配 1（"永久"判定不许只读热缓存）。</summary>
        [Column("publish_state", TypeName = "tinyint", Order = 27), TableDescription(TypeName = "tinyint", Name = "publish_state", Order = "27", Description = "发布状态")]
        [Comment("发布状态 0草稿 1已发布 2已停用")]
        public byte PublishState { get; set; }

        /// <summary>内容版本（乐观锁 + 装配对账）。</summary>
        [Column("content_version", TypeName = "int", Order = 28), TableDescription(TypeName = "int", Name = "content_version", Order = "28", Description = "内容版本")]
        [Comment("内容版本")]
        public int ContentVersion { get; set; } = 1;

        /// <summary>
        /// 尸体可拾取的窗口秒数（2026-10-28 拍板：缺省取 AC 的 60 秒，且**可逐怪配置**）。
        /// <para>与 <see cref="CorpseSeconds"/> 分开：那格是"尸体在地上躺多久"，这格是"这具尸体多久之内还能取东西"。
        /// 两者可以不同（例如尸体留 5 分钟但只有前 60 秒能拾取）。</para>
        /// </summary>
        [Column("loot_window_seconds", TypeName = "float", Order = 32), TableDescription(TypeName = "float", Name = "loot_window_seconds", Order = "32", Description = "可拾取窗口秒")]
        [Comment("尸体可拾取窗口秒（AC 缺省 60）")]
        public float LootWindowSeconds { get; set; } = 60f;

        /// <summary>拾取交互距离（米；AC `INTERACTION_DISTANCE` 缺省 5.5）。</summary>
        [Column("pickup_distance_meters", TypeName = "float", Order = 33), TableDescription(TypeName = "float", Name = "pickup_distance_meters", Order = "33", Description = "拾取距离米")]
        [Comment("拾取距离米（AC 缺省 5.5）")]
        public float PickupDistanceMeters { get; set; } = 5.5f;

        /// <summary>备注。</summary>
        [Column("remark", TypeName = "nvarchar(500)", Order = 31), TableDescription(TypeName = "nvarchar(500)", Name = "remark", Order = "31", Description = "备注")]
        [Comment("策划备注")]
        public string Remark { get; set; } = string.Empty;
    }

    /// <summary>
    /// 怪物行为实体（模板的 1:1 行为附件：AI 能力位、半径、索敌方式、移动方式、战术阈值）。
    /// <para>
    /// 与模板分表是因为**编辑节奏不同**：数值公式改动要过金轨迹对拍，而"把索敌半径从 10 米改成 12 米"
    /// 是策划随手做的事。分表后编辑器可以只提交行为那一半。
    /// </para>
    /// </summary>
    [Table("Game_HunduShijie_MonsterBehavior"), TableDescription(Name = "Game_HunduShijie_MonsterBehavior", Order = "HunduShijie_031", Description = "怪物行为配置")]
    [Comment("怪物行为表（与模板 1:1）")]
    [EntityStorage("Game")]
    public class MonsterBehaviorEntity : BaseGameModel<int>
    {
        /// <summary>怪物编号（与 <see cref="MonsterTemplateEntity.Id"/> 同值，也是主键）。</summary>
        [Key]
        [Column("monster_id", TypeName = "int", Order = 1), TableDescription(TypeName = "int", Name = "monster_id", Order = "1", Description = "怪物编号")]
        [Comment("怪物编号")]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public new int Id { get; set; }

        /// <summary>AI 智能档位预设：0笨 1战 2精 3王（下面五个覆盖位为 NULL 时跟它）。</summary>
        [Column("ai_profile", TypeName = "tinyint", Order = 2), TableDescription(TypeName = "tinyint", Name = "ai_profile", Order = "2", Description = "AI档位")]
        [Comment("AI 档位 0笨1战2精3王")]
        public byte AiProfile { get; set; }

        // 五个能力位覆盖：NULL = 跟档位预设；0/1 = 强制关/开。
        // 为什么不"只留档位"：只留档位时"这只小怪会求援但不会技能"配不出来，
        // 策划只能改档位——而改一个档位会连带改掉五项里另外四项。

        /// <summary>威胁表覆盖（NULL=跟档位）。</summary>
        [Column("flag_use_threat", TypeName = "tinyint", Order = 3), TableDescription(TypeName = "tinyint", Name = "flag_use_threat", Order = "3", Description = "威胁表覆盖")]
        [Comment("威胁表覆盖 NULL跟档位")]
        public byte? FlagUseThreat { get; set; }

        /// <summary>技能覆盖（NULL=跟档位）。</summary>
        [Column("flag_use_skills", TypeName = "tinyint", Order = 4), TableDescription(TypeName = "tinyint", Name = "flag_use_skills", Order = "4", Description = "技能覆盖")]
        [Comment("技能覆盖 NULL跟档位")]
        public byte? FlagUseSkills { get; set; }

        /// <summary>导航图覆盖（NULL=跟档位）。</summary>
        [Column("flag_use_navmesh", TypeName = "tinyint", Order = 5), TableDescription(TypeName = "tinyint", Name = "flag_use_navmesh", Order = "5", Description = "导航覆盖")]
        [Comment("导航图覆盖 NULL跟档位")]
        public byte? FlagUseNavMesh { get; set; }

        /// <summary>同族求援覆盖（NULL=跟档位）。</summary>
        [Column("flag_use_assist", TypeName = "tinyint", Order = 6), TableDescription(TypeName = "tinyint", Name = "flag_use_assist", Order = "6", Description = "求援覆盖")]
        [Comment("求援覆盖 NULL跟档位")]
        public byte? FlagUseAssist { get; set; }

        /// <summary>战术覆盖（NULL=跟档位）。</summary>
        [Column("flag_use_tactics", TypeName = "tinyint", Order = 7), TableDescription(TypeName = "tinyint", Name = "flag_use_tactics", Order = "7", Description = "战术覆盖")]
        [Comment("战术覆盖 NULL跟档位")]
        public byte? FlagUseTactics { get; set; }

        /// <summary>主动怪（1=靠近即索敌；0=被动，必须先打它）。玩家一眼能分辨的那种区别。</summary>
        [Column("is_aggressive", TypeName = "tinyint", Order = 8), TableDescription(TypeName = "tinyint", Name = "is_aggressive", Order = "8", Description = "是否主动")]
        [Comment("是否主动索敌")]
        public byte IsAggressive { get; set; } = 1;

        [Column("aggro_radius", TypeName = "float", Order = 9), TableDescription(TypeName = "float", Name = "aggro_radius", Order = "9", Description = "索敌半径")]
        [Comment("索敌半径（米）")]
        public float AggroRadius { get; set; } = 10f;

        /// <summary>索敌朝向锥（度）；360 = 全向。"背后贴着不一定会醒"的那条判据。</summary>
        [Column("aggro_cone_degrees", TypeName = "float", Order = 10), TableDescription(TypeName = "float", Name = "aggro_cone_degrees", Order = "10", Description = "索敌锥角")]
        [Comment("索敌朝向锥（度）")]
        public float AggroConeDegrees { get; set; } = 360f;

        [Column("leash_radius", TypeName = "float", Order = 11), TableDescription(TypeName = "float", Name = "leash_radius", Order = "11", Description = "脱战半径")]
        [Comment("脱战半径（米）")]
        public float LeashRadius { get; set; } = 30f;

        /// <summary>Evade 回位后的免疫秒（不接受新仇恨，但仍可受伤害）。</summary>
        [Column("evade_immunity_seconds", TypeName = "float", Order = 12), TableDescription(TypeName = "float", Name = "evade_immunity_seconds", Order = "12", Description = "脱战免疫秒")]
        [Comment("回位免疫新仇恨秒")]
        public float EvadeImmunitySeconds { get; set; } = 3f;

        [Column("assist_radius", TypeName = "float", Order = 13), TableDescription(TypeName = "float", Name = "assist_radius", Order = "13", Description = "求援半径")]
        [Comment("同族求援半径（米）")]
        public float AssistRadius { get; set; } = 10f;

        /// <summary>检测类型位（bit0视觉 bit1听觉 bit2隐蔽探测）。0 = 只看距离、不管隐蔽。</summary>
        [Column("detect_mask", TypeName = "int", Order = 14), TableDescription(TypeName = "int", Name = "detect_mask", Order = "14", Description = "检测位")]
        [Comment("检测类型位掩码")]
        public int DetectMask { get; set; } = 1;

        [Column("wander_radius", TypeName = "float", Order = 15), TableDescription(TypeName = "float", Name = "wander_radius", Order = "15", Description = "踱步半径")]
        [Comment("无路线时踱步半径（米）")]
        public float WanderRadius { get; set; } = 6f;

        [Column("idle_to_patrol_seconds", TypeName = "float", Order = 16), TableDescription(TypeName = "float", Name = "idle_to_patrol_seconds", Order = "16", Description = "待机转巡逻秒")]
        [Comment("待机转巡逻秒")]
        public float IdleToPatrolSeconds { get; set; } = 3f;

        /// <summary>巡逻速度（米/秒）。WoW 观测：明显慢于追击速度。</summary>
        [Column("walk_speed", TypeName = "float", Order = 17), TableDescription(TypeName = "float", Name = "walk_speed", Order = "17", Description = "巡逻速度")]
        [Comment("巡逻速度（米/秒）")]
        public float WalkSpeed { get; set; } = 2.5f;

        [Column("move_speed", TypeName = "float", Order = 18), TableDescription(TypeName = "float", Name = "move_speed", Order = "18", Description = "追击速度")]
        [Comment("追击速度（米/秒）")]
        public float MoveSpeed { get; set; } = 4f;

        /// <summary>移动方式：0步行(受图) 1直线(穿墙) 2飞行 3站桩。</summary>
        [Column("follow_type", TypeName = "tinyint", Order = 19), TableDescription(TypeName = "tinyint", Name = "follow_type", Order = "19", Description = "移动方式")]
        [Comment("移动方式 0步行1直线2飞行3站桩")]
        public byte FollowType { get; set; }

        /// <summary>近战挥击间隔（秒）——与技能冷却分开，玩家能听出攻击节奏。</summary>
        [Column("melee_swing_seconds", TypeName = "float", Order = 20), TableDescription(TypeName = "float", Name = "melee_swing_seconds", Order = "20", Description = "挥击间隔")]
        [Comment("近战挥击间隔秒")]
        public float MeleeSwingSeconds { get; set; } = 1.5f;

        [Column("ranged_min_distance", TypeName = "float", Order = 21), TableDescription(TypeName = "float", Name = "ranged_min_distance", Order = "21", Description = "保持距离下界")]
        [Comment("远程保持距离下界（米）")]
        public float RangedMinDistance { get; set; }

        [Column("ranged_max_distance", TypeName = "float", Order = 22), TableDescription(TypeName = "float", Name = "ranged_max_distance", Order = "22", Description = "保持距离上界")]
        [Comment("远程保持距离上界（米）")]
        public float RangedMaxDistance { get; set; }

        /// <summary>残血逃跑阈值百分比（0 = 从不逃）。</summary>
        [Column("flee_hp_percent", TypeName = "int", Order = 23), TableDescription(TypeName = "int", Name = "flee_hp_percent", Order = "23", Description = "逃跑阈值")]
        [Comment("残血逃跑阈值百分比")]
        public int FleeHpPercent { get; set; }

        /// <summary>巡逻路线 id（0 = 无路线，按 wander_radius 踱步）。</summary>
        [Column("patrol_route_id", TypeName = "int", Order = 24), TableDescription(TypeName = "int", Name = "patrol_route_id", Order = "24", Description = "巡逻路线")]
        [Comment("巡逻路线 id")]
        public int PatrolRouteId { get; set; }
    }

    /// <summary>
    /// 怪物技能挂载（一行 = 这只怪会放的一个技能；<b>复用玩家技能 id</b>）。
    /// <para>
    /// 不新造"怪物技能定义表"：效果链 <c>WuxingSkillEffects</c> 与技能模板表已经在服务端跑通，
    /// 另立一套会把它们旁路掉。怪物这一层只回答"会不会放、什么时候放、放给谁、打多重"。
    /// </para>
    /// </summary>
    [Table("Game_HunduShijie_MonsterSkillMount"), TableDescription(Name = "Game_HunduShijie_MonsterSkillMount", Order = "HunduShijie_032", Description = "怪物技能挂载")]
    [Comment("怪物技能挂载表")]
    [EntityStorage("Game")]
    public class MonsterSkillMountEntity : BaseGameModel<int>
    {
        [Key]
        [Column("mount_id", TypeName = "int", Order = 1), TableDescription(TypeName = "int", Name = "mount_id", Order = "1", Description = "挂载ID")]
        [Comment("挂载 id（自增）")]
        public new int Id { get; set; }

        [Column("monster_id", TypeName = "int", Order = 2), TableDescription(TypeName = "int", Name = "monster_id", Order = "2", Description = "怪物编号")]
        [Comment("怪物编号")]
        public int MonsterId { get; set; }

        /// <summary>技能 id（对齐 <see cref="SkillTemplateEntity"/>）。</summary>
        [Column("skill_id", TypeName = "int", Order = 3), TableDescription(TypeName = "int", Name = "skill_id", Order = "3", Description = "技能ID")]
        [Comment("技能 id")]
        public int SkillId { get; set; }

        /// <summary>挂载顺序（运行期按此升序遍历；顺序影响 tie 结果，故必须是数据而不是插入历史）。</summary>
        [Column("slot_seq", TypeName = "int", Order = 4), TableDescription(TypeName = "int", Name = "slot_seq", Order = "4", Description = "顺序")]
        [Comment("遍历顺序")]
        public int SlotSeq { get; set; }

        /// <summary>时机：0战斗 1非战斗 2残血应急 3受控时。</summary>
        [Column("slot_type", TypeName = "tinyint", Order = 5), TableDescription(TypeName = "tinyint", Name = "slot_type", Order = "5", Description = "时机")]
        [Comment("时机 0战斗1非战斗2残血3受控")]
        public byte SlotType { get; set; }

        /// <summary>独立释放概率（基点 0..10000）。是概率不是权重——"有时喷火有时近战"的实现方式。</summary>
        [Column("chance_bps", TypeName = "int", Order = 6), TableDescription(TypeName = "int", Name = "chance_bps", Order = "6", Description = "概率基点")]
        [Comment("释放概率基点 0..10000")]
        public int ChanceBps { get; set; } = 10000;

        /// <summary>吟唱毫秒（0 = 瞬发）。决定客户端施法条长度。</summary>
        [Column("cast_ms", TypeName = "int", Order = 7), TableDescription(TypeName = "int", Name = "cast_ms", Order = "7", Description = "吟唱毫秒")]
        [Comment("吟唱时长毫秒")]
        public int CastMs { get; set; }

        [Column("cooldown_ms", TypeName = "int", Order = 8), TableDescription(TypeName = "int", Name = "cooldown_ms", Order = "8", Description = "冷却毫秒")]
        [Comment("冷却毫秒")]
        public int CooldownMs { get; set; } = 5000;

        [Column("min_range", TypeName = "float", Order = 9), TableDescription(TypeName = "float", Name = "min_range", Order = "9", Description = "最小射程")]
        [Comment("最小射程（米）")]
        public float MinRange { get; set; }

        [Column("max_range", TypeName = "float", Order = 10), TableDescription(TypeName = "float", Name = "max_range", Order = "10", Description = "最大射程")]
        [Comment("最大射程（米）")]
        public float MaxRange { get; set; } = 8f;

        /// <summary>选敌方式：0当前威胁 1仇恨内随机 2最近 3血最低 4以目标为圆心AOE。</summary>
        [Column("target_select", TypeName = "tinyint", Order = 11), TableDescription(TypeName = "tinyint", Name = "target_select", Order = "11", Description = "选敌")]
        [Comment("选敌方式")]
        public byte TargetSelect { get; set; }

        [Column("target_count", TypeName = "int", Order = 12), TableDescription(TypeName = "int", Name = "target_count", Order = "12", Description = "目标数")]
        [Comment("最大目标数")]
        public int TargetCount { get; set; } = 1;

        [Column("aoe_radius", TypeName = "float", Order = 13), TableDescription(TypeName = "float", Name = "aoe_radius", Order = "13", Description = "AOE半径")]
        [Comment("AOE 半径（米）")]
        public float AoeRadius { get; set; }

        /// <summary>伤害系别位（金木水火土 + 物理），客户端据此给施法条分色。</summary>
        [Column("school_mask", TypeName = "int", Order = 14), TableDescription(TypeName = "int", Name = "school_mask", Order = "14", Description = "系别位")]
        [Comment("系别位掩码")]
        public int SchoolMask { get; set; }

        /// <summary>可打断（1=可）。决定客户端是否显示"可打断"。</summary>
        [Column("interruptible", TypeName = "tinyint", Order = 15), TableDescription(TypeName = "tinyint", Name = "interruptible", Order = "15", Description = "可打断")]
        [Comment("是否可打断")]
        public byte Interruptible { get; set; } = 1;

        /// <summary>移动施法（1=允许）。0 时吟唱期间位移清零。</summary>
        [Column("allow_movement", TypeName = "tinyint", Order = 16), TableDescription(TypeName = "tinyint", Name = "allow_movement", Order = "16", Description = "移动施法")]
        [Comment("是否允许移动施法")]
        public byte AllowMovement { get; set; }

        /// <summary>伤害倍率基点（10000 = ×1）。怪物用它做数值缩放而不改技能表。</summary>
        [Column("damage_scale_bps", TypeName = "int", Order = 17), TableDescription(TypeName = "int", Name = "damage_scale_bps", Order = "17", Description = "伤害倍率基点")]
        [Comment("伤害倍率基点")]
        public int DamageScaleBps { get; set; } = 10000;

        [Column("status_id", TypeName = "int", Order = 18), TableDescription(TypeName = "int", Name = "status_id", Order = "18", Description = "附加状态")]
        [Comment("附加状态 id")]
        public int StatusId { get; set; }

        [Column("status_duration_seconds", TypeName = "float", Order = 19), TableDescription(TypeName = "float", Name = "status_duration_seconds", Order = "19", Description = "状态秒")]
        [Comment("状态持续秒")]
        public float StatusDurationSeconds { get; set; }

        /// <summary>
        /// 状态强度（基点，10000 = 100%）。今天只有**减速**用它：5000 = 走速打五折。
        /// <para><b>0 = 没填</b>，与"填了 0"同义：都不生效（见 <c>MonsterStatusEntry.MagnitudeBps</c>）。
        /// AC 的减速比例是每一式技能自带的 effect 值，本作把它放在挂载行上（2026-10-28 用户拍板"加一列"）。</para>
        /// <para>这一列由 <c>scripts/sql/026_add_monster_status_magnitude.sql</c> 加上
        /// （2026-10-28 已对 `Game_MonsterRT` 与 `Game` 两个库执行，`COL_LENGTH` 实测 4 字节）。</para>
        /// </summary>
        [Column("status_magnitude_bps", TypeName = "int", Order = 21), TableDescription(TypeName = "int", Name = "status_magnitude_bps", Order = "21", Description = "状态强度基点")]
        [Comment("状态强度基点（减速用；0=没填）")]
        public int StatusMagnitudeBps { get; set; }

        [Column("start_emote", TypeName = "int", Order = 20), TableDescription(TypeName = "int", Name = "start_emote", Order = "20", Description = "起手表情")]
        [Comment("起手喊话/表情 id")]
        public int StartEmote { get; set; }

        /// <summary>残血阈值百分比（slot_type=2 专用）。</summary>
        [Column("emergency_hp_percent", TypeName = "int", Order = 22), TableDescription(TypeName = "int", Name = "emergency_hp_percent", Order = "22", Description = "残血阈值")]
        [Comment("残血阈值百分比")]
        public int EmergencyHpPercent { get; set; } = 30;
    }
}
