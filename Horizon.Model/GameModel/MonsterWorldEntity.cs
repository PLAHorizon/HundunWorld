using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model.GameModel
{
    // 本文件的五张表是**怪物编辑器私有产物**（巡逻路线/巡逻点/刷怪点/掉落绑定/逐件概率）。
    // 与 MonsterTemplateEntity 一样走 [EntityStorage("Game")]，但**不写逐列 TableDescription**：
    // 那套属性是模型代码导出工具（TypeDumper）消费的，而这些表的列只在编辑器 ↔ CLI 之间流动，
    // 不进出入参模型。要补的话一次性补，别补半份——半份 TableDescription 会让导出工具产出半个模型。

    /// <summary>
    /// 怪物巡逻路线（waypoint 折线）。
    /// <para>
    /// <b>路线与寻路是两件事（决策 D6）</b>：路线回答"要去哪"，导航图回答"两点之间怎么走"。
    /// 合成一条会让"改一个点就得重算整条路径"变成必然，而 WoW 的观测是策划手打点、引擎负责走。
    /// </para>
    /// </summary>
    [Table("Game_HunduShijie_MonsterPatrolRoute")]
    [Comment("怪物巡逻路线表")]
    [EntityStorage("Game")]
    public class MonsterPatrolRouteEntity : BaseGameModel<int>
    {
        [Key]
        [Column("route_id", TypeName = "int", Order = 1)]
        [Comment("路线 id（自增）")]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public new int Id { get; set; }

        [Column("name", TypeName = "nvarchar(64)", Order = 2)]
        [Comment("路线名")]
        public string Name { get; set; } = string.Empty;

        /// <summary>行进方式：0往返 1循环 2随机下一点。</summary>
        [Column("mode", TypeName = "tinyint", Order = 3)]
        [Comment("0往返 1循环 2随机")]
        public byte Mode { get; set; } = 1;

        /// <summary>每点默认停留秒（点级可覆盖）。</summary>
        [Column("pause_at_point_seconds", TypeName = "float", Order = 4)]
        [Comment("每点默认停留秒")]
        public float PauseAtPointSeconds { get; set; } = 2f;

        /// <summary>是否用导航图连段（0 = 直线连，配穿墙/飞行怪）。</summary>
        [Column("use_navmesh", TypeName = "tinyint", Order = 5)]
        [Comment("是否走图连段")]
        public byte UseNavMesh { get; set; } = 1;

        /// <summary>点数（写入时由 CLI 统计；<b>声明了必须有人写</b>，否则编辑器列表恒为 0）。</summary>
        [Column("point_count", TypeName = "int", Order = 6)]
        [Comment("点数（由 CLI 统计写入）")]
        public int PointCount { get; set; }

        [Column("publish_state", TypeName = "tinyint", Order = 7)]
        [Comment("0草稿 1已发布 2停用")]
        public byte PublishState { get; set; }
    }

    /// <summary>巡逻点（服务端 Z-up 世界坐标，水平面 = XY；编辑器从场景拾取点时负责换轴）。</summary>
    [Table("Game_HunduShijie_MonsterPatrolPoint")]
    [Comment("怪物巡逻点表")]
    [EntityStorage("Game")]
    public class MonsterPatrolPointEntity : BaseGameModel<int>
    {
        [Key]
        [Column("point_id", TypeName = "int", Order = 1)]
        [Comment("点 id（自增）")]
        public new int Id { get; set; }

        [Column("route_id", TypeName = "int", Order = 2)]
        [Comment("所属路线")]
        public int RouteId { get; set; }

        /// <summary>顺序（运行期按此升序遍历；同 seq 的顺序歧义由 CLI 拒绝保存挡在写入侧）。</summary>
        [Column("seq", TypeName = "int", Order = 3)]
        [Comment("点顺序")]
        public int Seq { get; set; }

        [Column("x", TypeName = "float", Order = 4)]
        [Comment("世界坐标 X")]
        public float X { get; set; }

        [Column("y", TypeName = "float", Order = 5)]
        [Comment("世界坐标 Y（水平面第二轴）")]
        public float Y { get; set; }

        [Column("z", TypeName = "float", Order = 6)]
        [Comment("世界坐标 Z（高度）")]
        public float Z { get; set; }

        /// <summary>该点停留秒；NULL = 用路线默认值。</summary>
        [Column("pause_seconds", TypeName = "float", Order = 7)]
        [Comment("该点停留秒（NULL=用默认）")]
        public float? PauseSeconds { get; set; }
    }

    /// <summary>
    /// 刷怪点（把今天硬编码在 <c>Horizon.DsHost/Program.cs:372 BuildSpawnPoints()</c> 的东西数据化）。
    /// <para>
    /// <b>本表不再存 MaxHp/AttackPower</b>：数值一律从模板公式派生（逆向文档 §1.3 指出的错位——
    /// 现在刷怪点带数值、模板只带名字，而 WoW 是模板带数值、点位带位置）。
    /// 保留 <c>*_override_bps</c> 两列做"这一只特别硬"的局部覆盖，避免为了改一只怪去改模板。
    /// </para>
    /// </summary>
    [Table("Game_HunduShijie_MonsterSpawnPoint")]
    [Comment("怪物刷怪点表")]
    [EntityStorage("Game")]
    public class MonsterSpawnPointEntity : BaseGameModel<int>
    {
        [Key]
        [Column("spawn_point_id", TypeName = "int", Order = 1)]
        [Comment("刷怪点 id")]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public new int Id { get; set; }

        [Column("zone_id", TypeName = "int", Order = 2)]
        [Comment("区域 id")]
        public int ZoneId { get; set; }

        [Column("shard_id", TypeName = "bigint", Order = 3)]
        [Comment("分片 id")]
        public long ShardId { get; set; }

        [Column("x", TypeName = "float", Order = 4)]
        [Comment("中心 X")]
        public float X { get; set; }

        [Column("y", TypeName = "float", Order = 5)]
        [Comment("中心 Y")]
        public float Y { get; set; }

        [Column("z", TypeName = "float", Order = 6)]
        [Comment("中心 Z")]
        public float Z { get; set; }

        [Column("yaw", TypeName = "float", Order = 7)]
        [Comment("初始朝向（弧度）")]
        public float Yaw { get; set; }

        [Column("monster_id", TypeName = "int", Order = 8)]
        [Comment("怪物编号（必须已发布）")]
        public int MonsterId { get; set; }

        /// <summary>等级覆盖；NULL = 用模板基准等级。超出模板 [level_min, level_max] 时 CLI 拒绝。</summary>
        [Column("level_override", TypeName = "int", Order = 9)]
        [Comment("等级覆盖（NULL=模板基准）")]
        public int? LevelOverride { get; set; }

        [Column("max_alive", TypeName = "int", Order = 10)]
        [Comment("同时存活上限（=槽位数）")]
        public int MaxAlive { get; set; } = 3;

        [Column("slot_spacing", TypeName = "float", Order = 11)]
        [Comment("槽位横向间距（米）")]
        public float SlotSpacing { get; set; } = 8f;

        /// <summary>刷新秒覆盖；&gt;0 时优先于模板公式。</summary>
        [Column("respawn_override_seconds", TypeName = "float", Order = 12)]
        [Comment("刷新秒覆盖（0=用公式）")]
        public float RespawnOverrideSeconds { get; set; }

        /// <summary>巡逻路线覆盖；0 = 用行为表上的路线。</summary>
        [Column("route_override_id", TypeName = "int", Order = 13)]
        [Comment("路线覆盖（0=用行为表）")]
        public int RouteOverrideId { get; set; }

        [Column("enabled", TypeName = "tinyint", Order = 14)]
        [Comment("是否启用")]
        public byte Enabled { get; set; } = 1;

        [Column("publish_state", TypeName = "tinyint", Order = 15)]
        [Comment("0草稿 1已发布 2停用")]
        public byte PublishState { get; set; }

        [Column("content_version", TypeName = "int", Order = 16)]
        [Comment("内容版本")]
        public int ContentVersion { get; set; } = 1;
    }

    /// <summary>
    /// 怪物 → 掉落表绑定（<b>一怪 N 表</b>）。
    /// <para>
    /// 现状是"怪 → 一张表"且表号由五行域**推导**（<c>MonsterTemplateCatalog.LootTableIdOf</c>）。
    /// WoW 的观测是一只怪可挂多张表、每张表配数量区间与独立概率，所以改成**配置**；
    /// 推导保留为兜底——否则"世界 BOSS → 100+域"那条顶端链会静默断（该函数注释专门警告过）。
    /// </para>
    /// </summary>
    [Table("Game_HunduShijie_MonsterLootBinding")]
    [Comment("怪物掉落表绑定表")]
    [EntityStorage("Game")]
    public class MonsterLootBindingEntity : BaseGameModel<int>
    {
        [Key]
        [Column("binding_id", TypeName = "int", Order = 1)]
        [Comment("绑定 id（自增）")]
        public new int Id { get; set; }

        [Column("monster_id", TypeName = "int", Order = 2)]
        [Comment("怪物编号")]
        public int MonsterId { get; set; }

        [Column("loot_table_id", TypeName = "int", Order = 3)]
        [Comment("掉落表 id")]
        public int LootTableId { get; set; }

        /// <summary>本表整体触发概率（基点）。0=必触。</summary>
        [Column("roll_chance_bps", TypeName = "int", Order = 4)]
        [Comment("本表触发概率基点（0=必触）")]
        public int RollChanceBps { get; set; }

        /// <summary>从本表抽多少件的下界/上界。</summary>
        [Column("min_count", TypeName = "int", Order = 5)]
        [Comment("抽取件数下界")]
        public int MinCount { get; set; } = 1;

        [Column("max_count", TypeName = "int", Order = 6)]
        [Comment("抽取件数上界")]
        public int MaxCount { get; set; } = 1;

        /// <summary>条件：0无条件 1任务持有者 2组队 3稀有度门槛。</summary>
        [Column("condition_kind", TypeName = "tinyint", Order = 7)]
        [Comment("条件类型")]
        public byte ConditionKind { get; set; }

        /// <summary>条件关联的任务 id（condition_kind=1 时生效）。任务物品必掉的载体。</summary>
        [Column("quest_id", TypeName = "int", Order = 8)]
        [Comment("条件任务 id")]
        public int QuestId { get; set; }

        /// <summary>金币掉落区间（0/0 = 不掉钱）。WoW 观测：金币是独立一格、按等级给区间。</summary>
        [Column("money_min", TypeName = "int", Order = 9)]
        [Comment("金币下界")]
        public int MoneyMin { get; set; }

        [Column("money_max", TypeName = "int", Order = 10)]
        [Comment("金币上界")]
        public int MoneyMax { get; set; }
    }

    /// <summary>
    /// 逐件掉落条目（决策 D4：概率挂在**绑定**上，而不是复用 <c>ItemTemplate.drop_rate</c>）。
    /// <para>
    /// 理由是观测本身：WoW 的尸体窗口里每件**独立出现**、可同时出多件、任务物品只对做了任务的人出现。
    /// 一列 <c>drop_rate</c> 装不下"同一件在不同怪身上概率不同"这件事——它会变成"物品全局概率"，
    /// 于是给 A 怪配的数值会作用到 B 怪身上，而日志里没有任何痕迹。
    /// </para>
    /// </summary>
    [Table("Game_HunduShijie_MonsterDropEntry")]
    [Comment("怪物逐件掉落表")]
    [EntityStorage("Game")]
    public class MonsterDropEntryEntity : BaseGameModel<int>
    {
        [Key]
        [Column("entry_id", TypeName = "int", Order = 1)]
        [Comment("条目 id（自增）")]
        public new int Id { get; set; }

        [Column("binding_id", TypeName = "int", Order = 2)]
        [Comment("所属绑定")]
        public int BindingId { get; set; }

        [Column("item_id", TypeName = "int", Order = 3)]
        [Comment("物品模板 id")]
        public int ItemId { get; set; }

        /// <summary>该件独立概率（基点 0..10000）。逐件掷，不是抽一件。</summary>
        [Column("chance_bps", TypeName = "int", Order = 4)]
        [Comment("该件独立概率基点")]
        public int ChanceBps { get; set; } = 1000;

        [Column("count_min", TypeName = "int", Order = 5)]
        [Comment("数量下界")]
        public int CountMin { get; set; } = 1;

        [Column("count_max", TypeName = "int", Order = 6)]
        [Comment("数量上界")]
        public int CountMax { get; set; } = 1;

        /// <summary>顺序（掷骰顺序影响确定性，必须是数据）。</summary>
        [Column("seq", TypeName = "int", Order = 7)]
        [Comment("掷骰顺序")]
        public int Seq { get; set; }

        /// <summary>
        /// 掉落组号：<c>0</c> = 逐件独立掷（一件不影响另一件），<c>&gt;0</c> = 同组互斥、整组最多出一件。
        /// <para>魔兽把这件事放在 <c>creature_loot_template.GroupId</c> 上，
        /// 上限 7 位（AzerothCore <c>LootStoreItem::groupid : 7</c>）。没有这一列，
        /// "从这 10 件里必出 1 件"只能配成 10 条独立概率——既会一次掉 3 件，又永远做不到必出一件，
        /// 而编辑器里每件的概率数字看着全都对。</para>
        /// </summary>
        [Column("group_id", TypeName = "int", Order = 8)]
        [Comment("掉落组号（0=逐件独立掷，>0=同组互斥抽一件）")]
        public int GroupId { get; set; }

        /// <summary>
        /// 这一件自己的掉落条件：<c>-1</c> = 继承所属绑定，0=无条件，1=任务持有者，2=组队，3=稀有度门槛。
        /// <para>魔兽把 <c>QuestRequired</c> 挂在 <c>LootStoreItem</c> 每一行上（AzerothCore
        /// <c>Loot/LootMgr.h:127-149</c>），所以"一张表里既有普通货又有一件任务物品"是常态配法；
        /// 默认必须是 -1 而不是 0——0 恰好是"无条件"，那样旧数据里没这一列的行会集体变成人人可拿。</para>
        /// </summary>
        [Column("condition_kind", TypeName = "int", Order = 9)]
        [Comment("逐件掉落条件（-1=继承绑定，0=无条件，1=任务持有者，2=组队，3=稀有度）")]
        public int ConditionKind { get; set; } = -1;

        /// <summary>条件=任务持有者时的任务 id；0 = 用绑定上那一份。</summary>
        [Column("quest_id", TypeName = "int", Order = 10)]
        [Comment("逐件任务条件 id（0=用绑定上的）")]
        public int QuestId { get; set; }
    }
}
