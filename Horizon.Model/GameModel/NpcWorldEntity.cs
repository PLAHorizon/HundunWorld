using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model.GameModel
{
    // 与 NpcTemplateEntity 一样走 [EntityStorage("Game")]，但**不写逐列 TableDescription**：
    // 这批表不进出入参模型，导出工具按"要么全写、要么全不写"的口径产出；
    // 补半份 TableDescription 会让它导出半个模型（怪物侧同一教训，见 MonsterWorldEntity.cs 顶部注释）。

    /// <summary>NPC 刷点（位置 + 等级覆盖 + 刷新 + 路线）。</summary>
    [Table("Game_HunduShijie_NpcSpawnPoint"), TableDescription(Name = "Game_HunduShijie_NpcSpawnPoint", Order = "HunduShijie_041", Description = "NPC刷点")]
    [Comment("NPC 刷点表")]
    [EntityStorage("Game")]
    public class NpcSpawnPointEntity : BaseGameModel<int>
    {
        [Key]
        [Column("spawn_point_id", TypeName = "int")]
        [Comment("刷点编号")]
        public new int Id { get; set; }

        [Column("zone_id", TypeName = "int")]
        [Comment("区域编号")]
        public int ZoneId { get; set; }

        [Column("shard_id", TypeName = "bigint")]
        [Comment("分片编号")]
        public long ShardId { get; set; }

        // 服务端世界坐标口径：米、Z-up（水平面 = XY）。客户端 Flax 是 Y-up，下发时要换轴。
        [Column("x", TypeName = "float")]
        [Comment("X")]
        public float X { get; set; }

        [Column("y", TypeName = "float")]
        [Comment("Y")]
        public float Y { get; set; }

        [Column("z", TypeName = "float")]
        [Comment("Z")]
        public float Z { get; set; }

        [Column("yaw", TypeName = "float")]
        [Comment("朝向（度）")]
        public float Yaw { get; set; }

        [Column("npc_id", TypeName = "int")]
        [Comment("NPC 编号")]
        public int NpcId { get; set; }

        /// <summary>等级覆盖。0 = 用模板等级（与怪物侧同一条"越近现场越具体"的口径）。</summary>
        [Column("level_override", TypeName = "int")]
        [Comment("等级覆盖，0=用模板")]
        public int LevelOverride { get; set; }

        [Column("max_alive", TypeName = "int")]
        [Comment("同时存在数量")]
        public int MaxAlive { get; set; } = 1;

        /// <summary>死亡后多少秒刷新。0 = 用模板/全局默认曲线，不表示"立刻刷"。</summary>
        [Column("respawn_seconds", TypeName = "float")]
        [Comment("刷新秒，0=用默认")]
        public float RespawnSeconds { get; set; }

        /// <summary>路线覆盖。≤0 = 不覆盖（用 NPC 自己作息/能力位推出来的移动方式）。</summary>
        [Column("route_id", TypeName = "int")]
        [Comment("路线覆盖，≤0=不覆盖")]
        public int RouteId { get; set; }

        /// <summary>初始朝向模式：0 固定 Yaw 1 随机 2 面向玩家（对应规格 §9.1 档 1）。</summary>
        [Column("facing_mode", TypeName = "tinyint")]
        [Comment("朝向模式")]
        public byte FacingMode { get; set; }

        [Column("enabled", TypeName = "tinyint")]
        [Comment("是否启用")]
        public byte Enabled { get; set; } = 1;

        [Column("publish_state", TypeName = "tinyint")]
        [Comment("发布状态")]
        public byte PublishState { get; set; }

        [Column("content_version", TypeName = "int")]
        [Comment("内容版本")]
        public int ContentVersion { get; set; }
    }

    /// <summary>NPC 货架行（商人卖什么、多少钱、库存几件）。</summary>
    [Table("Game_HunduShijie_NpcVendorEntry"), TableDescription(Name = "Game_HunduShijie_NpcVendorEntry", Order = "HunduShijie_042", Description = "NPC货架")]
    [Comment("NPC 商人货架表")]
    [EntityStorage("Game")]
    public class NpcVendorEntryEntity : BaseGameModel<int>
    {
        [Key]
        [Column("entry_id", TypeName = "int")]
        [Comment("行编号")]
        public new int Id { get; set; }

        [Column("npc_id", TypeName = "int")]
        [Comment("NPC 编号")]
        public int NpcId { get; set; }

        /// <summary>窗口里的位置（0 起，左上到右下）。<b>顺序是数据不是 id 序</b>：玩家记得住"那瓶药在第二格"。</summary>
        [Column("slot", TypeName = "int")]
        [Comment("槽位")]
        public int Slot { get; set; }

        [Column("item_id", TypeName = "int")]
        [Comment("物品编号")]
        public int ItemId { get; set; }

        /// <summary>买入价覆盖（玩家付的钱）。≤0 = 不覆盖，落回 <c>ItemTemplate.buy_price</c>。</summary>
        [Column("buy_price_override", TypeName = "bigint")]
        [Comment("买入价覆盖，≤0=用模板")]
        public long BuyPriceOverride { get; set; }

        /// <summary>收购价覆盖（玩家卖得的钱）。≤0 = 不覆盖，落回 <c>ItemTemplate.sell_price</c>。</summary>
        [Column("sell_price_override", TypeName = "bigint")]
        [Comment("收购价覆盖，≤0=用模板")]
        public long SellPriceOverride { get; set; }

        /// <summary>库存上限。<b>0 = 不限库存</b>（照 WoW 的 maxcount 口径，语义由 <c>NpcCommerceCore</c> 独占解释）。</summary>
        [Column("stock_max", TypeName = "int")]
        [Comment("库存上限，0=不限")]
        public int StockMax { get; set; }

        /// <summary>补货秒数。与 <c>stock_max</c> 配对才有意义；有上限却是 0 会被 validate 拦。</summary>
        [Column("restock_seconds", TypeName = "int")]
        [Comment("补货秒")]
        public int RestockSeconds { get; set; }

        /// <summary>货品类别（仅用于窗口分组显示，不参与成交判据）。</summary>
        [Column("category", TypeName = "int")]
        [Comment("类别（仅显示分组）")]
        public int Category { get; set; }

        [Column("enabled", TypeName = "tinyint")]
        [Comment("是否启用")]
        public byte Enabled { get; set; } = 1;
    }

    /// <summary>
    /// NPC 技能教学行。
    /// <para><b>这里刻意没有 <c>req_level</c> / <c>req_realm</c> / <c>req_prerequisite_skill</c> 三列</b>：
    /// 游戏门槛已经在 <c>SkillGrain.CheckLearnGates</c> 算过一次并有 6 组 live 测试钉着，
    /// 在 NPC 侧再存一份就会得到"训练师说我不够格、技能面板说够格"这种自相矛盾。
    /// 本行只带 NPC 才有的信息：教什么、收多少钱、排第几。</para>
    /// </summary>
    [Table("Game_HunduShijie_NpcTrainerEntry"), TableDescription(Name = "Game_HunduShijie_NpcTrainerEntry", Order = "HunduShijie_043", Description = "NPC教学")]
    [Comment("NPC 教学行表（技能与配方）")]
    [EntityStorage("Game")]
    public class NpcTrainerEntryEntity : BaseGameModel<int>
    {
        [Key]
        [Column("entry_id", TypeName = "int")]
        [Comment("行编号")]
        public new int Id { get; set; }

        [Column("npc_id", TypeName = "int")]
        [Comment("NPC 编号")]
        public int NpcId { get; set; }

        /// <summary>技能 id（service_kind=1）或配方 id（service_kind=2）。</summary>
        [Column("skill_id", TypeName = "int")]
        [Comment("技能或配方编号")]
        public int SkillId { get; set; }

        /// <summary>服务种类：1 技能教学 2 配方教学。0/越界按坏行拒绝。</summary>
        [Column("service_kind", TypeName = "tinyint")]
        [Comment("服务种类 1技能 2配方")]
        public byte ServiceKind { get; set; }

        /// <summary>学费。<b>0 是合法值</b>（免费教入门式），所以"没定价"不能靠 0 表达。</summary>
        [Column("cost_gold", TypeName = "bigint")]
        [Comment("学费（金币）")]
        public long CostGold { get; set; }

        /// <summary>额外材料代价的物品 id；≤0 = 不要材料。</summary>
        [Column("cost_item_id", TypeName = "int")]
        [Comment("材料物品编号，≤0=不要")]
        public int CostItemId { get; set; }

        [Column("cost_item_count", TypeName = "int")]
        [Comment("材料件数")]
        public int CostItemCount { get; set; }

        [Column("seq", TypeName = "int")]
        [Comment("窗口排序")]
        public int Seq { get; set; }

        [Column("enabled", TypeName = "tinyint")]
        [Comment("是否启用")]
        public byte Enabled { get; set; } = 1;
    }

    /// <summary>制作配方（做什么、要什么料、在哪个设施做）。</summary>
    [Table("Game_HunduShijie_NpcCraftRecipe"), TableDescription(Name = "Game_HunduShijie_NpcCraftRecipe", Order = "HunduShijie_044", Description = "NPC制作配方")]
    [Comment("制作配方表")]
    [EntityStorage("Game")]
    public class NpcCraftRecipeEntity : BaseGameModel<int>
    {
        [Key]
        [Column("recipe_id", TypeName = "int")]
        [Comment("配方编号")]
        public new int Id { get; set; }

        [Column("name", TypeName = "nvarchar(64)")]
        [Comment("配方名")]
        public string Name { get; set; } = string.Empty;

        [Column("product_item_id", TypeName = "int")]
        [Comment("产物物品编号")]
        public int ProductItemId { get; set; }

        /// <summary>材料清单 JSON 数组 <c>[{"item":501,"count":3}]</c>。<b>空数组 = 空配方，内核显式拒绝</b>（凭空造物）。</summary>
        [Column("materials_json", TypeName = "nvarchar(max)")]
        [Comment("材料清单 JSON")]
        public string MaterialsJson { get; set; } = "[]";

        [Column("cost_gold", TypeName = "bigint")]
        [Comment("制作工费")]
        public long CostGold { get; set; }

        /// <summary>要求站着的设施 NPC 编号；≤0 = 随处可做。</summary>
        [Column("station_npc_id", TypeName = "int")]
        [Comment("设施 NPC 编号，≤0=随处")]
        public int StationNpcId { get; set; }

        /// <summary>配方要求的专业技能点数。与玩家技能点之差决定成功档。</summary>
        [Column("skill_req", TypeName = "int")]
        [Comment("要求技能点数")]
        public int SkillReq { get; set; }

        /// <summary>
        /// 成功档的技能差上限，JSON <c>[橙,黄,绿,灰]</c>（<b>由易到难</b>，严格递增）。
        /// 与 <see cref="QualityChanceBpsJson"/> 同序，判据在 <c>NpcCraftChanceCurve</c>；
        /// <b>表不整就不算，内核不内置任何"逆向数值"默认值</b>。
        /// </summary>
        [Column("quality_gap_caps_json", TypeName = "nvarchar(128)")]
        [Comment("成功档技能差上限 JSON")]
        public string QualityGapCapsJson { get; set; } = "[]";

        /// <summary>四档成功率（万分比），同序由易到难、非递增。</summary>
        [Column("quality_chance_bps_json", TypeName = "nvarchar(128)")]
        [Comment("四档成功率 JSON（万分比）")]
        public string QualityChanceBpsJson { get; set; } = "[]";

        /// <summary>学会来源：1 训练师教 2 图纸/书学 3 任务奖 4 掉落。只有 1 走 NPC 通路。</summary>
        [Column("learn_source_kind", TypeName = "tinyint")]
        [Comment("学习来源 1训练师 2书 3任务 4掉落")]
        public byte LearnSourceKind { get; set; } = 1;

        [Column("publish_state", TypeName = "tinyint")]
        [Comment("发布状态")]
        public byte PublishState { get; set; }

        [Column("content_version", TypeName = "int")]
        [Comment("内容版本")]
        public int ContentVersion { get; set; }
    }

    /// <summary>哪个 NPC 教哪张配方（多对多，带各自的学费）。</summary>
    [Table("Game_HunduShijie_NpcRecipeTaught"), TableDescription(Name = "Game_HunduShijie_NpcRecipeTaught", Order = "HunduShijie_045", Description = "NPC配方教学")]
    [Comment("配方 ↔ NPC 教学关系表")]
    [EntityStorage("Game")]
    public class NpcRecipeTaughtEntity : BaseGameModel<int>
    {
        [Key]
        [Column("link_id", TypeName = "int")]
        [Comment("关系编号")]
        public new int Id { get; set; }

        [Column("recipe_id", TypeName = "int")]
        [Comment("配方编号")]
        public int RecipeId { get; set; }

        [Column("npc_id", TypeName = "int")]
        [Comment("NPC 编号")]
        public int NpcId { get; set; }

        /// <summary>这位师傅的学费（可与另一位的不同——观测：不同训练师教同一张图收费不一样）。</summary>
        [Column("cost_gold", TypeName = "bigint")]
        [Comment("学费")]
        public long CostGold { get; set; }

        [Column("enabled", TypeName = "tinyint")]
        [Comment("是否启用")]
        public byte Enabled { get; set; } = 1;
    }

    /// <summary>NPC 对话选项（gossip 菜单；一条文本 + 一个动作）。</summary>
    [Table("Game_HunduShijie_NpcGossipOption"), TableDescription(Name = "Game_HunduShijie_NpcGossipOption", Order = "HunduShijie_046", Description = "NPC对话选项")]
    [Comment("NPC 对话选项表")]
    [EntityStorage("Game")]
    public class NpcGossipOptionEntity : BaseGameModel<int>
    {
        [Key]
        [Column("option_id", TypeName = "int")]
        [Comment("选项编号")]
        public new int Id { get; set; }

        [Column("npc_id", TypeName = "int")]
        [Comment("NPC 编号")]
        public int NpcId { get; set; }

        /// <summary>台词组（与 <c>NpcTemplate.gossip_group_id</c> 和作息档的覆盖配对）。</summary>
        [Column("group_id", TypeName = "int")]
        [Comment("台词组")]
        public int GroupId { get; set; }

        [Column("seq", TypeName = "int")]
        [Comment("窗口排序")]
        public int Seq { get; set; }

        /// <summary>文本行号（走既有文本表；0 = 无文本，只做动作项）。</summary>
        [Column("text_id", TypeName = "int")]
        [Comment("文本编号")]
        public int TextId { get; set; }

        /// <summary>动作种类：0 仅文本 1 开商店 2 开训练 3 开制作 4 交任务 5 下一层菜单。</summary>
        [Column("action_kind", TypeName = "tinyint")]
        [Comment("动作种类")]
        public byte ActionKind { get; set; }

        /// <summary>动作参数（商店/训练窗口号、下一层组号等，随 <see cref="ActionKind"/> 解释）。</summary>
        [Column("action_arg", TypeName = "int")]
        [Comment("动作参数")]
        public int ActionArg { get; set; }

        /// <summary>可见等级下限（含）。菜单项的可见性是 NPC 轴的判据，与技能/任务门槛无关。</summary>
        [Column("req_min_level", TypeName = "int")]
        [Comment("可见等级下限")]
        public int ReqMinLevel { get; set; }

        /// <summary>可见等级上限（含）。0 = 不设上限（<b>不是"只能 0 级看"</b>，见 <c>NpcGossipCore</c>）。</summary>
        [Column("req_max_level", TypeName = "int")]
        [Comment("可见等级上限，0=不限")]
        public int ReqMaxLevel { get; set; }

        [Column("enabled", TypeName = "tinyint")]
        [Comment("是否启用")]
        public byte Enabled { get; set; } = 1;
    }

    /// <summary>
    /// 任务 ↔ NPC 关系（发布 / 交付）。
    /// <para>
    /// <b>为什么要另立这张表而不是只读 <c>QuestTemplate.giver_template_id</c></b>：
    /// 那边是"一条任务写一个发布者"，要画任务链图、要算头顶指示器都需要<b>反查</b>
    /// （这个 NPC 手上有哪些任务）。反查得靠倒排索引；<c>prerequisite_quests</c> 是 CSV，
    /// 每次现算既慢又会在 UI 里长出一份与权威端不同的排序。本表由发布时从任务表同步生成，
    /// <b>唯一消费者是 <c>NpcQuestIndicatorCore</c> 的取数层与编辑器的链图</b>。
    /// </para>
    /// </summary>
    [Table("Game_HunduShijie_NpcQuestLink"), TableDescription(Name = "Game_HunduShijie_NpcQuestLink", Order = "HunduShijie_047", Description = "NPC任务关系")]
    [Comment("任务与 NPC 的发布/交付关系表")]
    [EntityStorage("Game")]
    public class NpcQuestLinkEntity : BaseGameModel<int>
    {
        [Key]
        [Column("link_id", TypeName = "int")]
        [Comment("关系编号")]
        public new int Id { get; set; }

        [Column("npc_id", TypeName = "int")]
        [Comment("NPC 编号")]
        public int NpcId { get; set; }

        [Column("quest_id", TypeName = "int")]
        [Comment("任务编号")]
        public int QuestId { get; set; }

        /// <summary>角色：1 发布者 2 交付者。<b>同一条任务可以同时是 1 和 2（两行）</b>，
        /// 对应 WoW 里"给任务的人就是收任务的人"这种单 NPC 链。</summary>
        [Column("role", TypeName = "tinyint")]
        [Comment("角色 1发布 2交付")]
        public byte Role { get; set; }

        [Column("order_seq", TypeName = "int")]
        [Comment("窗口排序")]
        public int OrderSeq { get; set; }
    }

    /// <summary>NPC 巡逻/工作路线（与怪物侧 <c>MonsterPatrolRoute</c> 逐列同形，共用 <c>MonsterPatrolCore</c>）。</summary>
    [Table("Game_HunduShijie_NpcWaypointPath"), TableDescription(Name = "Game_HunduShijie_NpcWaypointPath", Order = "HunduShijie_048", Description = "NPC路线")]
    [Comment("NPC 路线表")]
    [EntityStorage("Game")]
    public class NpcWaypointPathEntity : BaseGameModel<int>
    {
        [Key]
        [Column("path_id", TypeName = "int")]
        [Comment("路线编号")]
        public new int Id { get; set; }

        [Column("name", TypeName = "nvarchar(64)")]
        [Comment("路线名")]
        public string Name { get; set; } = string.Empty;

        /// <summary>走法：0 往返 1 循环 2 随机（与怪物侧 <c>MonsterPatrolCore</c> 的 mode 值域一致）。</summary>
        [Column("mode", TypeName = "tinyint")]
        [Comment("走法 0往返 1循环 2随机")]
        public byte Mode { get; set; }

        [Column("pause_at_point_seconds", TypeName = "float")]
        [Comment("每点停留秒")]
        public float PauseAtPointSeconds { get; set; }

        /// <summary>是否走导航图（0 = 直线）。与能力位 <c>UseNavigation</c> 的分工同怪物侧：这里是"这条路线怎么走"。</summary>
        [Column("use_navmesh", TypeName = "tinyint")]
        [Comment("是否走图")]
        public byte UseNavMesh { get; set; } = 1;

        [Column("point_count", TypeName = "int")]
        [Comment("点数（冗余，供校验与进度显示）")]
        public int PointCount { get; set; }

        [Column("publish_state", TypeName = "tinyint")]
        [Comment("发布状态")]
        public byte PublishState { get; set; }
    }

    /// <summary>NPC 路线点。</summary>
    [Table("Game_HunduShijie_NpcWaypointNode"), TableDescription(Name = "Game_HunduShijie_NpcWaypointNode", Order = "HunduShijie_049", Description = "NPC路线点")]
    [Comment("NPC 路线点表")]
    [EntityStorage("Game")]
    public class NpcWaypointNodeEntity : BaseGameModel<int>
    {
        [Key]
        [Column("node_id", TypeName = "int")]
        [Comment("点编号")]
        public new int Id { get; set; }

        [Column("path_id", TypeName = "int")]
        [Comment("所属路线")]
        public int PathId { get; set; }

        [Column("seq", TypeName = "int")]
        [Comment("顺序")]
        public int Seq { get; set; }

        [Column("x", TypeName = "float")]
        [Comment("X")]
        public float X { get; set; }

        [Column("y", TypeName = "float")]
        [Comment("Y")]
        public float Y { get; set; }

        [Column("z", TypeName = "float")]
        [Comment("Z")]
        public float Z { get; set; }

        [Column("pause_seconds", TypeName = "float")]
        [Comment("本点停留秒（覆盖路线默认）")]
        public float PauseSeconds { get; set; }
    }

    /// <summary>
    /// NPC 作息档（一天里某时段走哪条路线、用什么姿态、讲哪组台词）。
    /// <para>秒数是<b>周期内秒</b>（不是世界绝对秒、不是时刻），换算与判档都在
    /// <c>NpcScheduleCore</c>；<c>from == to</c> 表示"全天兜底档"，<c>to == 周期长度</c> 是合法的"做到日终"。</para>
    /// </summary>
    [Table("Game_HunduShijie_NpcScheduleSlot"), TableDescription(Name = "Game_HunduShijie_NpcScheduleSlot", Order = "HunduShijie_050", Description = "NPC作息档")]
    [Comment("NPC 作息表")]
    [EntityStorage("Game")]
    public class NpcScheduleSlotEntity : BaseGameModel<int>
    {
        [Key]
        [Column("slot_id", TypeName = "int")]
        [Comment("档编号")]
        public new int Id { get; set; }

        [Column("npc_id", TypeName = "int")]
        [Comment("NPC 编号")]
        public int NpcId { get; set; }

        [Column("seq", TypeName = "int")]
        [Comment("同刻冲突时的优先序（小者优先）")]
        public int Seq { get; set; }

        [Column("from_seconds", TypeName = "int")]
        [Comment("起（含），周期内秒")]
        public int FromSeconds { get; set; }

        [Column("to_seconds", TypeName = "int")]
        [Comment("止（不含），周期内秒；等于起点=全天")]
        public int ToSeconds { get; set; }

        /// <summary>这一时段走哪条路线。≤0 = 不覆盖刷点路线。</summary>
        [Column("route_id", TypeName = "int")]
        [Comment("路线，≤0=不覆盖")]
        public int RouteId { get; set; }

        /// <summary>姿态：0 站 1 坐 2 工作 3 持械。未登记值按站立画（<c>NpcScheduleCore.IsPostureKnown</c>）。</summary>
        [Column("posture", TypeName = "tinyint")]
        [Comment("姿态")]
        public byte Posture { get; set; }

        /// <summary>这一时段的台词组覆盖。≤0 = 用模板台词组。</summary>
        [Column("gossip_group_id", TypeName = "int")]
        [Comment("台词组覆盖，≤0=用模板")]
        public int GossipGroupId { get; set; }
    }
}
