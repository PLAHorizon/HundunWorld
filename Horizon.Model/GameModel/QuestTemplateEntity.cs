using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model.GameModel
{
    /// <summary>
    /// 任务模板实体（§14.2 的"任务获得初级技能书 / 加该域好感"这条产出通路的权威定义）。
    /// <para>
    /// 为什么必须由服务端持有奖励表：<see cref="Horizon.Orleans.Interface.IQuestGrain.AcceptQuestAsync"/>
    /// 的旧签名让调用方把 <c>rewards</c> 字典整个传进来。若"接受任务"也照这个口子接网络，
    /// 客户端就能自己声明"这个任务给我 30 本秘籍 + 5000 点好感"，整套 §14 习得门槛当场失效——
    /// 而这套门槛的价值恰恰是"不能绕"。所以模板在这里，接受时只收 questId，奖励一律读库。
    /// </para>
    /// </summary>
    [Table("Game_HunduShijie_QuestTemplate"), TableDescription(Name = "Game_HunduShijie_QuestTemplate", Order = "HunduShijie_021", Description = "任务模板信息")]
    [Comment("任务模板表")]
    [EntityStorage("Game")]
    public class QuestTemplateEntity : BaseGameModel<int>
    {
        /// <summary>任务模板 ID（约定：五行入门任务用 90000+域号，1=金 … 5=土）。</summary>
        [Key]
        [Column("quest_id", TypeName = "int", Order = 1), TableDescription(TypeName = "int", Name = "quest_id", Order = "1", Description = "任务模板ID")]
        [Comment("任务模板ID")]
        public new int Id { get; set; }

        /// <summary>任务名称。</summary>
        [Required]
        [Column("quest_name", TypeName = "nvarchar(50)", Order = 2), TableDescription(TypeName = "nvarchar(50)", Name = "quest_name", Order = "2", Description = "任务名称")]
        [Comment("任务名称")]
        public string QuestName { get; set; } = string.Empty;

        /// <summary>任务描述。</summary>
        [Column("description", TypeName = "nvarchar(500)", Order = 3), TableDescription(TypeName = "nvarchar(500)", Name = "description", Order = "3", Description = "任务描述")]
        [Comment("任务描述")]
        public string Description { get; set; } = string.Empty;

        /// <summary>任务类型：0=主线 1=支线 2=日常 3=周常。</summary>
        [Column("quest_type", TypeName = "int", Order = 4), TableDescription(TypeName = "int", Name = "quest_type", Order = "4", Description = "任务类型")]
        [Comment("任务类型 0主线 1支线 2日常 3周常")]
        public int QuestType { get; set; }

        /// <summary>接取所需等级。</summary>
        [Column("level_require", TypeName = "int", Order = 5), TableDescription(TypeName = "int", Name = "level_require", Order = "5", Description = "接取所需等级")]
        [Comment("接取所需等级")]
        public int LevelRequire { get; set; }

        /// <summary>
        /// 归属的五行域（1金 2木 3水 4火 5土，0=通用）。
        /// 与物品 <c>source_detail.domain</c> 同一套编号：任务和"与该域的往来"共用一条口径，
        /// 否则同一个域在两条链上会有两个号，早晚对不上。
        /// </summary>
        [Column("wuxing_domain", TypeName = "int", Order = 6), TableDescription(TypeName = "int", Name = "wuxing_domain", Order = "6", Description = "五行域")]
        [Comment("五行域 1金2木3水4火5土 0=通用")]
        public int WuxingDomain { get; set; }

        /// <summary>
        /// 奖励表（JSON 对象，键为发放约定）。已接线的两族键：<c>item:&lt;物品模板id&gt;</c> = 数量、
        /// <c>favor:&lt;五行域&gt;</c> = 好感点数；<c>gold</c>/<c>exp</c> 沿用既有语义。
        /// </summary>
        [Column("rewards", TypeName = "nvarchar(max)", Order = 7), TableDescription(TypeName = "nvarchar(max)", Name = "rewards", Order = "7", Description = "奖励JSON")]
        [Comment("奖励 JSON：{\"item:810101\":1,\"favor:1\":20,\"exp\":100}")]
        public string Rewards { get; set; } = string.Empty;

        /// <summary>完成目标（JSON 数组，元素形如 {"kind":"kill_item","target":10101,"need":1}）。</summary>
        [Column("objectives", TypeName = "nvarchar(max)", Order = 8), TableDescription(TypeName = "nvarchar(max)", Name = "objectives", Order = "8", Description = "目标JSON")]
        [Comment("完成目标 JSON")]
        public string Objectives { get; set; } = string.Empty;

        /// <summary>可重复完成次数：0=一次性，N=最多交 N 次。好感产出必须受这个约束，否则反复交同一任务能刷满一域。</summary>
        [Column("repeat_limit", TypeName = "int", Order = 9), TableDescription(TypeName = "int", Name = "repeat_limit", Order = "9", Description = "可重复次数")]
        [Comment("可重复完成次数 0=一次性")]
        public int RepeatLimit { get; set; }

        /// <summary>
        /// 发布者类别：1=NPC 2=怪物 3=物品 4=场景物件 5=区域。
        /// 与 <c>Horizon.Game.Sim.Quests.QuestEntityKind</c> 同序；任务编辑器下拉直接写这一列，
        /// 不再有"人是 NPC、物是 Item"两套字段。
        /// </summary>
        [Column("giver_kind", TypeName = "int", Order = 10), TableDescription(TypeName = "int", Name = "giver_kind", Order = "10", Description = "发布者类别")]
        [Comment("发布者类别 1NPC 2怪物 3物品 4物件 5区域")]
        public int GiverKind { get; set; }

        /// <summary>发布者模板 id（NPC/怪物/物品/物件的模板号；0=未配置）。</summary>
        [Column("giver_template_id", TypeName = "int", Order = 11), TableDescription(TypeName = "int", Name = "giver_template_id", Order = "11", Description = "发布者模板ID")]
        [Comment("发布者模板ID 0=未配置")]
        public int GiverTemplateId { get; set; }

        /// <summary>发布者限定区域（0=不限）。脚本生成点分布在不同区域时用它收窄可接范围。</summary>
        [Column("giver_zone_id", TypeName = "int", Order = 12), TableDescription(TypeName = "int", Name = "giver_zone_id", Order = "12", Description = "发布者限定区域")]
        [Comment("发布者限定区域 0=不限")]
        public int GiverZoneId { get; set; }

        /// <summary>发布者限定生成点（0=不限）。</summary>
        [Column("giver_spawn_point_id", TypeName = "int", Order = 13), TableDescription(TypeName = "int", Name = "giver_spawn_point_id", Order = "13", Description = "发布者限定生成点")]
        [Comment("发布者限定生成点 0=不限")]
        public int GiverSpawnPointId { get; set; }

        /// <summary>交付者类别（与 giver 同一套编号；0=沿用发布者）。</summary>
        [Column("turn_in_kind", TypeName = "int", Order = 14), TableDescription(TypeName = "int", Name = "turn_in_kind", Order = "14", Description = "交付者类别")]
        [Comment("交付者类别 0=沿用发布者")]
        public int TurnInKind { get; set; }

        /// <summary>交付者模板 id（0=沿用发布者）。</summary>
        [Column("turn_in_template_id", TypeName = "int", Order = 15), TableDescription(TypeName = "int", Name = "turn_in_template_id", Order = "15", Description = "交付者模板ID")]
        [Comment("交付者模板ID 0=沿用发布者")]
        public int TurnInTemplateId { get; set; }

        /// <summary>前置任务 id 列表（逗号分隔）。空表示无前置。</summary>
        [Column("prerequisite_quests", TypeName = "varchar(200)", Order = 16), TableDescription(TypeName = "varchar(200)", Name = "prerequisite_quests", Order = "16", Description = "前置任务ID列表")]
        [Comment("前置任务ID 逗号分隔")]
        public string PrerequisiteQuests { get; set; } = string.Empty;

        /// <summary>
        /// 发布状态：0=下线（不出现在可接列表）1=已发布。
        /// 与 <c>IsValid</c> 分列：作废是数据治理，上下线是玩法开关——热更新只动后者，
        /// 否则"临时关掉一条任务"会顺手把历史数据标成无效。
        /// </summary>
        [Column("publish_state", TypeName = "int", Order = 17), TableDescription(TypeName = "int", Name = "publish_state", Order = "17", Description = "发布状态")]
        [Comment("发布状态 0下线 1已发布（实时开关，不影响已接实例）")]
        public int PublishState { get; set; }

        /// <summary>内容版本号：每次入库自增。客户端与 Grain 用它对账"我缓存的任务描述是否过期"。</summary>
        [Column("content_version", TypeName = "int", Order = 18), TableDescription(TypeName = "int", Name = "content_version", Order = "18", Description = "内容版本号")]
        [Comment("内容版本号 每次入库自增")]
        public int ContentVersion { get; set; }
    }
}
