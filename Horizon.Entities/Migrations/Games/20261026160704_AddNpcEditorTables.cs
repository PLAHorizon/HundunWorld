using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Horizon.Entities.Migrations.Games
{
    /// <inheritdoc />
    public partial class AddNpcEditorTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_NpcCraftRecipe",
                columns: table => new
                {
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    recipe_id = table.Column<int>(type: "int", nullable: false, comment: "配方编号")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    name = table.Column<string>(type: "nvarchar(64)", nullable: false, comment: "配方名"),
                    product_item_id = table.Column<int>(type: "int", nullable: false, comment: "产物物品编号"),
                    materials_json = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "材料清单 JSON"),
                    cost_gold = table.Column<long>(type: "bigint", nullable: false, comment: "制作工费"),
                    station_npc_id = table.Column<int>(type: "int", nullable: false, comment: "设施 NPC 编号，≤0=随处"),
                    skill_req = table.Column<int>(type: "int", nullable: false, comment: "要求技能点数"),
                    quality_gap_caps_json = table.Column<string>(type: "nvarchar(128)", nullable: false, comment: "成功档技能差上限 JSON"),
                    quality_chance_bps_json = table.Column<string>(type: "nvarchar(128)", nullable: false, comment: "四档成功率 JSON（万分比）"),
                    learn_source_kind = table.Column<byte>(type: "tinyint", nullable: false, comment: "学习来源 1训练师 2书 3任务 4掉落"),
                    publish_state = table.Column<byte>(type: "tinyint", nullable: false, comment: "发布状态"),
                    content_version = table.Column<int>(type: "int", nullable: false, comment: "内容版本"),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_NpcCraftRecipe", x => x.recipe_id);
                },
                comment: "制作配方表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_NpcGossipOption",
                columns: table => new
                {
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    option_id = table.Column<int>(type: "int", nullable: false, comment: "选项编号")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    npc_id = table.Column<int>(type: "int", nullable: false, comment: "NPC 编号"),
                    group_id = table.Column<int>(type: "int", nullable: false, comment: "台词组"),
                    seq = table.Column<int>(type: "int", nullable: false, comment: "窗口排序"),
                    text_id = table.Column<int>(type: "int", nullable: false, comment: "文本编号"),
                    action_kind = table.Column<byte>(type: "tinyint", nullable: false, comment: "动作种类"),
                    action_arg = table.Column<int>(type: "int", nullable: false, comment: "动作参数"),
                    req_min_level = table.Column<int>(type: "int", nullable: false, comment: "可见等级下限"),
                    req_max_level = table.Column<int>(type: "int", nullable: false, comment: "可见等级上限，0=不限"),
                    enabled = table.Column<byte>(type: "tinyint", nullable: false, comment: "是否启用"),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_NpcGossipOption", x => x.option_id);
                },
                comment: "NPC 对话选项表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_NpcQuestLink",
                columns: table => new
                {
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    link_id = table.Column<int>(type: "int", nullable: false, comment: "关系编号")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    npc_id = table.Column<int>(type: "int", nullable: false, comment: "NPC 编号"),
                    quest_id = table.Column<int>(type: "int", nullable: false, comment: "任务编号"),
                    role = table.Column<byte>(type: "tinyint", nullable: false, comment: "角色 1发布 2交付"),
                    order_seq = table.Column<int>(type: "int", nullable: false, comment: "窗口排序"),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_NpcQuestLink", x => x.link_id);
                },
                comment: "任务与 NPC 的发布/交付关系表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_NpcRecipeTaught",
                columns: table => new
                {
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    link_id = table.Column<int>(type: "int", nullable: false, comment: "关系编号")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    recipe_id = table.Column<int>(type: "int", nullable: false, comment: "配方编号"),
                    npc_id = table.Column<int>(type: "int", nullable: false, comment: "NPC 编号"),
                    cost_gold = table.Column<long>(type: "bigint", nullable: false, comment: "学费"),
                    enabled = table.Column<byte>(type: "tinyint", nullable: false, comment: "是否启用"),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_NpcRecipeTaught", x => x.link_id);
                },
                comment: "配方 ↔ NPC 教学关系表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_NpcScheduleSlot",
                columns: table => new
                {
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    slot_id = table.Column<int>(type: "int", nullable: false, comment: "档编号")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    npc_id = table.Column<int>(type: "int", nullable: false, comment: "NPC 编号"),
                    seq = table.Column<int>(type: "int", nullable: false, comment: "同刻冲突时的优先序（小者优先）"),
                    from_seconds = table.Column<int>(type: "int", nullable: false, comment: "起（含），周期内秒"),
                    to_seconds = table.Column<int>(type: "int", nullable: false, comment: "止（不含），周期内秒；等于起点=全天"),
                    route_id = table.Column<int>(type: "int", nullable: false, comment: "路线，≤0=不覆盖"),
                    posture = table.Column<byte>(type: "tinyint", nullable: false, comment: "姿态"),
                    gossip_group_id = table.Column<int>(type: "int", nullable: false, comment: "台词组覆盖，≤0=用模板"),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_NpcScheduleSlot", x => x.slot_id);
                },
                comment: "NPC 作息表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_NpcSpawnPoint",
                columns: table => new
                {
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    spawn_point_id = table.Column<int>(type: "int", nullable: false, comment: "刷点编号")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    zone_id = table.Column<int>(type: "int", nullable: false, comment: "区域编号"),
                    shard_id = table.Column<long>(type: "bigint", nullable: false, comment: "分片编号"),
                    x = table.Column<double>(type: "float", nullable: false, comment: "X"),
                    y = table.Column<double>(type: "float", nullable: false, comment: "Y"),
                    z = table.Column<double>(type: "float", nullable: false, comment: "Z"),
                    yaw = table.Column<double>(type: "float", nullable: false, comment: "朝向（度）"),
                    npc_id = table.Column<int>(type: "int", nullable: false, comment: "NPC 编号"),
                    level_override = table.Column<int>(type: "int", nullable: false, comment: "等级覆盖，0=用模板"),
                    max_alive = table.Column<int>(type: "int", nullable: false, comment: "同时存在数量"),
                    respawn_seconds = table.Column<double>(type: "float", nullable: false, comment: "刷新秒，0=用默认"),
                    route_id = table.Column<int>(type: "int", nullable: false, comment: "路线覆盖，≤0=不覆盖"),
                    facing_mode = table.Column<byte>(type: "tinyint", nullable: false, comment: "朝向模式"),
                    enabled = table.Column<byte>(type: "tinyint", nullable: false, comment: "是否启用"),
                    publish_state = table.Column<byte>(type: "tinyint", nullable: false, comment: "发布状态"),
                    content_version = table.Column<int>(type: "int", nullable: false, comment: "内容版本"),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_NpcSpawnPoint", x => x.spawn_point_id);
                },
                comment: "NPC 刷点表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_NpcTemplate",
                columns: table => new
                {
                    npc_id = table.Column<int>(type: "int", nullable: false, comment: "NPC 编号（号段即身份）"),
                    name = table.Column<string>(type: "nvarchar(64)", nullable: false, comment: "显示名"),
                    title = table.Column<string>(type: "nvarchar(64)", nullable: false, comment: "头顶头衔"),
                    model_key = table.Column<string>(type: "nvarchar(64)", nullable: false, comment: "客户端模型键"),
                    prefab_guid = table.Column<string>(type: "nvarchar(64)", nullable: false, comment: "prefab GUID（空=未配美术，客户端显式回退）"),
                    scale = table.Column<double>(type: "float", nullable: false, comment: "体型倍数"),
                    level = table.Column<int>(type: "int", nullable: false, comment: "基础等级"),
                    level_variance = table.Column<int>(type: "int", nullable: false, comment: "等级浮动"),
                    reaction = table.Column<byte>(type: "tinyint", nullable: false, comment: "立场 0友好 1中立 2敌对"),
                    faction_group = table.Column<int>(type: "int", nullable: false, comment: "阵营组（暂无权威玩家阵营，未参与判据）"),
                    wuxing_domain = table.Column<int>(type: "int", nullable: false, comment: "五行域 1金2木3水4火5土 0=未定"),
                    service_flags = table.Column<int>(type: "int", nullable: false, comment: "服务能力位图（NpcServiceBits）"),
                    ai_tier = table.Column<byte>(type: "tinyint", nullable: false, comment: "AI 档位 0-7"),
                    ai_bits_force_on = table.Column<int>(type: "int", nullable: false, comment: "能力位强制开"),
                    ai_bits_force_off = table.Column<int>(type: "int", nullable: false, comment: "能力位强制关"),
                    attackable_override = table.Column<int>(type: "int", nullable: false, comment: "可攻击覆盖 -1跟立场 0强制不可 1强制可"),
                    speed_walk = table.Column<double>(type: "float", nullable: false, comment: "步行速度（米/秒）"),
                    speed_run = table.Column<double>(type: "float", nullable: false, comment: "奔跑速度（米/秒）"),
                    alert_radius_meters = table.Column<int>(type: "int", nullable: false, comment: "呼叫增援半径（米）"),
                    gossip_group_id = table.Column<int>(type: "int", nullable: false, comment: "台词组 id"),
                    rank = table.Column<byte>(type: "tinyint", nullable: false, comment: "rank 0普通 1精英 2稀有精英 3首领 4世界BOSS"),
                    kill_credit_npc_id = table.Column<int>(type: "int", nullable: false, comment: "击杀归属转移到的 NPC 编号，0=不转移"),
                    publish_state = table.Column<byte>(type: "tinyint", nullable: false, comment: "发布状态 0草稿 1已发布 2已退役"),
                    content_version = table.Column<int>(type: "int", nullable: false, comment: "内容版本（乐观锁）"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_NpcTemplate", x => x.npc_id);
                },
                comment: "NPC 模板表（NPC 编辑器主表）");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_NpcTrainerEntry",
                columns: table => new
                {
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    entry_id = table.Column<int>(type: "int", nullable: false, comment: "行编号")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    npc_id = table.Column<int>(type: "int", nullable: false, comment: "NPC 编号"),
                    skill_id = table.Column<int>(type: "int", nullable: false, comment: "技能或配方编号"),
                    service_kind = table.Column<byte>(type: "tinyint", nullable: false, comment: "服务种类 1技能 2配方"),
                    cost_gold = table.Column<long>(type: "bigint", nullable: false, comment: "学费（金币）"),
                    cost_item_id = table.Column<int>(type: "int", nullable: false, comment: "材料物品编号，≤0=不要"),
                    cost_item_count = table.Column<int>(type: "int", nullable: false, comment: "材料件数"),
                    seq = table.Column<int>(type: "int", nullable: false, comment: "窗口排序"),
                    enabled = table.Column<byte>(type: "tinyint", nullable: false, comment: "是否启用"),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_NpcTrainerEntry", x => x.entry_id);
                },
                comment: "NPC 教学行表（技能与配方）");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_NpcVendorEntry",
                columns: table => new
                {
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    entry_id = table.Column<int>(type: "int", nullable: false, comment: "行编号")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    npc_id = table.Column<int>(type: "int", nullable: false, comment: "NPC 编号"),
                    slot = table.Column<int>(type: "int", nullable: false, comment: "槽位"),
                    item_id = table.Column<int>(type: "int", nullable: false, comment: "物品编号"),
                    buy_price_override = table.Column<long>(type: "bigint", nullable: false, comment: "买入价覆盖，≤0=用模板"),
                    sell_price_override = table.Column<long>(type: "bigint", nullable: false, comment: "收购价覆盖，≤0=用模板"),
                    stock_max = table.Column<int>(type: "int", nullable: false, comment: "库存上限，0=不限"),
                    restock_seconds = table.Column<int>(type: "int", nullable: false, comment: "补货秒"),
                    category = table.Column<int>(type: "int", nullable: false, comment: "类别（仅显示分组）"),
                    enabled = table.Column<byte>(type: "tinyint", nullable: false, comment: "是否启用"),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_NpcVendorEntry", x => x.entry_id);
                },
                comment: "NPC 商人货架表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_NpcWaypointNode",
                columns: table => new
                {
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    node_id = table.Column<int>(type: "int", nullable: false, comment: "点编号")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    path_id = table.Column<int>(type: "int", nullable: false, comment: "所属路线"),
                    seq = table.Column<int>(type: "int", nullable: false, comment: "顺序"),
                    x = table.Column<double>(type: "float", nullable: false, comment: "X"),
                    y = table.Column<double>(type: "float", nullable: false, comment: "Y"),
                    z = table.Column<double>(type: "float", nullable: false, comment: "Z"),
                    pause_seconds = table.Column<double>(type: "float", nullable: false, comment: "本点停留秒（覆盖路线默认）"),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_NpcWaypointNode", x => x.node_id);
                },
                comment: "NPC 路线点表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_NpcWaypointPath",
                columns: table => new
                {
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    path_id = table.Column<int>(type: "int", nullable: false, comment: "路线编号")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    name = table.Column<string>(type: "nvarchar(64)", nullable: false, comment: "路线名"),
                    mode = table.Column<byte>(type: "tinyint", nullable: false, comment: "走法 0往返 1循环 2随机"),
                    pause_at_point_seconds = table.Column<double>(type: "float", nullable: false, comment: "每点停留秒"),
                    use_navmesh = table.Column<byte>(type: "tinyint", nullable: false, comment: "是否走图"),
                    point_count = table.Column<int>(type: "int", nullable: false, comment: "点数（冗余，供校验与进度显示）"),
                    publish_state = table.Column<byte>(type: "tinyint", nullable: false, comment: "发布状态"),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_NpcWaypointPath", x => x.path_id);
                },
                comment: "NPC 路线表");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Game_HunduShijie_NpcCraftRecipe");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_NpcGossipOption");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_NpcQuestLink");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_NpcRecipeTaught");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_NpcScheduleSlot");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_NpcSpawnPoint");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_NpcTemplate");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_NpcTrainerEntry");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_NpcVendorEntry");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_NpcWaypointNode");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_NpcWaypointPath");

        }
    }
}
