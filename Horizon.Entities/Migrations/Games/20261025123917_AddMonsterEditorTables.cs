using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Horizon.Entities.Migrations.Games
{
    /// <inheritdoc />
    public partial class AddMonsterEditorTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 这五列**不属于怪物编辑器**——它们是数字人（MetaHuman）那条线的，而本地库里它们已经被
            // 手工 SQL 加上了，没有任何"已应用"的迁移认领它们（实测：`Game` 库的
            // __EFMigrationsHistory 最后一条是 20261026160704_AddNpcEditorTables，且这五列已在）。
            // 后果是量出来的，不是猜的（2026-10-27 在独立库 Game_Monster019Probe 上跑本迁移的脚本）：
            // Msg 2705「列名在每个表中必须唯一」在脚本第 2 行就把整批中止，BEGIN TRANSACTION 回滚 ⇒
            // **8 张怪物表一张都不建**，history 里也不留记录（失败得干净，但整条数据面推不动，
            // 而且 `dotnet ef database update` 会撞在同一行上）。
            // 所以改成幂等：列已存在就当无事发生；冷启动的新库仍由本迁移补齐。
            // 代价如实写明：这五列的**归属仍在怪物迁移里**（因为没有那条线自己的迁移），
            // 等 MetaHuman 那条线补了自己的迁移时，这段应当整块搬走——不要在这里再加第六列。
            // 同一条路上还有第二坑（2026-10-27 在全新的 Game_MonsterRT 上量到）：COL_LENGTH 对
            // **不存在的表**返回"找不到对象"而不是 NULL ⇒ Msg 4902 在第 4 行中止整批，
            // 结果"幂等版"在空库上照样一张怪物表都不建。所以先问表在不在，不在就大声 PRINT 跳过
            // （跳过必须留在结果里，不能静默）。
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'dbo.Game_HunduShijie_Character', N'U') IS NULL
                BEGIN
                    PRINT 'meta_human_* 这五列没加：本库还没有 Game_HunduShijie_Character 表（它们归角色那条线，建表的迁移排在更早的序号）。';
                END
                ELSE
                BEGIN
                IF COL_LENGTH(N'dbo.Game_HunduShijie_Character', N'meta_human_checksum') IS NULL
                BEGIN
                    ALTER TABLE [Game_HunduShijie_Character] ADD [meta_human_checksum] int NULL;
                    EXEC sp_addextendedproperty N'MS_Description', N'数字人外观校验和', N'SCHEMA', N'dbo', N'TABLE', N'Game_HunduShijie_Character', N'COLUMN', N'meta_human_checksum';
                END
                IF COL_LENGTH(N'dbo.Game_HunduShijie_Character', N'meta_human_look') IS NULL
                BEGIN
                    ALTER TABLE [Game_HunduShijie_Character] ADD [meta_human_look] varbinary(max) NULL;
                    EXEC sp_addextendedproperty N'MS_Description', N'数字人外观 blob', N'SCHEMA', N'dbo', N'TABLE', N'Game_HunduShijie_Character', N'COLUMN', N'meta_human_look';
                END
                IF COL_LENGTH(N'dbo.Game_HunduShijie_Character', N'meta_human_published') IS NULL
                BEGIN
                    ALTER TABLE [Game_HunduShijie_Character] ADD [meta_human_published] bit NOT NULL DEFAULT CAST(0 AS bit);
                    EXEC sp_addextendedproperty N'MS_Description', N'数字人外观已发布', N'SCHEMA', N'dbo', N'TABLE', N'Game_HunduShijie_Character', N'COLUMN', N'meta_human_published';
                END
                IF COL_LENGTH(N'dbo.Game_HunduShijie_Character', N'meta_human_revision') IS NULL
                BEGIN
                    ALTER TABLE [Game_HunduShijie_Character] ADD [meta_human_revision] int NULL;
                    EXEC sp_addextendedproperty N'MS_Description', N'数字人外观版本号', N'SCHEMA', N'dbo', N'TABLE', N'Game_HunduShijie_Character', N'COLUMN', N'meta_human_revision';
                END
                IF COL_LENGTH(N'dbo.Game_HunduShijie_Character', N'meta_human_template_id') IS NULL
                BEGIN
                    ALTER TABLE [Game_HunduShijie_Character] ADD [meta_human_template_id] nvarchar(128) NULL;
                    EXEC sp_addextendedproperty N'MS_Description', N'数字人外观模板 Id', N'SCHEMA', N'dbo', N'TABLE', N'Game_HunduShijie_Character', N'COLUMN', N'meta_human_template_id';
                END
                END
                """);

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_MonsterBehavior",
                columns: table => new
                {
                    monster_id = table.Column<int>(type: "int", nullable: false, comment: "怪物编号")
,
                    ai_profile = table.Column<byte>(type: "tinyint", nullable: false, comment: "AI 档位 0笨1战2精3王"),
                    flag_use_threat = table.Column<byte>(type: "tinyint", nullable: true, comment: "威胁表覆盖 NULL跟档位"),
                    flag_use_skills = table.Column<byte>(type: "tinyint", nullable: true, comment: "技能覆盖 NULL跟档位"),
                    flag_use_navmesh = table.Column<byte>(type: "tinyint", nullable: true, comment: "导航图覆盖 NULL跟档位"),
                    flag_use_assist = table.Column<byte>(type: "tinyint", nullable: true, comment: "求援覆盖 NULL跟档位"),
                    flag_use_tactics = table.Column<byte>(type: "tinyint", nullable: true, comment: "战术覆盖 NULL跟档位"),
                    is_aggressive = table.Column<byte>(type: "tinyint", nullable: false, comment: "是否主动索敌"),
                    aggro_radius = table.Column<double>(type: "float", nullable: false, comment: "索敌半径（米）"),
                    aggro_cone_degrees = table.Column<double>(type: "float", nullable: false, comment: "索敌朝向锥（度）"),
                    leash_radius = table.Column<double>(type: "float", nullable: false, comment: "脱战半径（米）"),
                    evade_immunity_seconds = table.Column<double>(type: "float", nullable: false, comment: "回位免疫新仇恨秒"),
                    assist_radius = table.Column<double>(type: "float", nullable: false, comment: "同族求援半径（米）"),
                    detect_mask = table.Column<int>(type: "int", nullable: false, comment: "检测类型位掩码"),
                    wander_radius = table.Column<double>(type: "float", nullable: false, comment: "无路线时踱步半径（米）"),
                    idle_to_patrol_seconds = table.Column<double>(type: "float", nullable: false, comment: "待机转巡逻秒"),
                    walk_speed = table.Column<double>(type: "float", nullable: false, comment: "巡逻速度（米/秒）"),
                    move_speed = table.Column<double>(type: "float", nullable: false, comment: "追击速度（米/秒）"),
                    follow_type = table.Column<byte>(type: "tinyint", nullable: false, comment: "移动方式 0步行1直线2飞行3站桩"),
                    melee_swing_seconds = table.Column<double>(type: "float", nullable: false, comment: "近战挥击间隔秒"),
                    ranged_min_distance = table.Column<double>(type: "float", nullable: false, comment: "远程保持距离下界（米）"),
                    ranged_max_distance = table.Column<double>(type: "float", nullable: false, comment: "远程保持距离上界（米）"),
                    flee_hp_percent = table.Column<int>(type: "int", nullable: false, comment: "残血逃跑阈值百分比"),
                    patrol_route_id = table.Column<int>(type: "int", nullable: false, comment: "巡逻路线 id"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_MonsterBehavior", x => x.monster_id);
                },
                comment: "怪物行为表（与模板 1:1）");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_MonsterDropEntry",
                columns: table => new
                {
                    entry_id = table.Column<int>(type: "int", nullable: false, comment: "条目 id（自增）")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    binding_id = table.Column<int>(type: "int", nullable: false, comment: "所属绑定"),
                    item_id = table.Column<int>(type: "int", nullable: false, comment: "物品模板 id"),
                    chance_bps = table.Column<int>(type: "int", nullable: false, comment: "该件独立概率基点"),
                    count_min = table.Column<int>(type: "int", nullable: false, comment: "数量下界"),
                    count_max = table.Column<int>(type: "int", nullable: false, comment: "数量上界"),
                    seq = table.Column<int>(type: "int", nullable: false, comment: "掷骰顺序"),
                    group_id = table.Column<int>(type: "int", nullable: false, comment: "掉落组号（0=逐件独立掷，>0=同组互斥抽一件）"),
                    condition_kind = table.Column<int>(type: "int", nullable: false, comment: "逐件掉落条件（-1=继承绑定，0=无条件，1=任务持有者，2=组队，3=稀有度）"),
                    quest_id = table.Column<int>(type: "int", nullable: false, comment: "逐件任务条件 id（0=用绑定上的）"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_MonsterDropEntry", x => x.entry_id);
                },
                comment: "怪物逐件掉落表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_MonsterLootBinding",
                columns: table => new
                {
                    binding_id = table.Column<int>(type: "int", nullable: false, comment: "绑定 id（自增）")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    monster_id = table.Column<int>(type: "int", nullable: false, comment: "怪物编号"),
                    loot_table_id = table.Column<int>(type: "int", nullable: false, comment: "掉落表 id"),
                    roll_chance_bps = table.Column<int>(type: "int", nullable: false, comment: "本表触发概率基点（0=必触）"),
                    min_count = table.Column<int>(type: "int", nullable: false, comment: "抽取件数下界"),
                    max_count = table.Column<int>(type: "int", nullable: false, comment: "抽取件数上界"),
                    condition_kind = table.Column<byte>(type: "tinyint", nullable: false, comment: "条件类型"),
                    quest_id = table.Column<int>(type: "int", nullable: false, comment: "条件任务 id"),
                    money_min = table.Column<int>(type: "int", nullable: false, comment: "金币下界"),
                    money_max = table.Column<int>(type: "int", nullable: false, comment: "金币上界"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_MonsterLootBinding", x => x.binding_id);
                },
                comment: "怪物掉落表绑定表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_MonsterPatrolPoint",
                columns: table => new
                {
                    point_id = table.Column<int>(type: "int", nullable: false, comment: "点 id（自增）")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    route_id = table.Column<int>(type: "int", nullable: false, comment: "所属路线"),
                    seq = table.Column<int>(type: "int", nullable: false, comment: "点顺序"),
                    x = table.Column<double>(type: "float", nullable: false, comment: "世界坐标 X"),
                    y = table.Column<double>(type: "float", nullable: false, comment: "世界坐标 Y（水平面第二轴）"),
                    z = table.Column<double>(type: "float", nullable: false, comment: "世界坐标 Z（高度）"),
                    pause_seconds = table.Column<double>(type: "float", nullable: true, comment: "该点停留秒（NULL=用默认）"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_MonsterPatrolPoint", x => x.point_id);
                },
                comment: "怪物巡逻点表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_MonsterPatrolRoute",
                columns: table => new
                {
                    route_id = table.Column<int>(type: "int", nullable: false, comment: "路线 id（自增）")
,
                    name = table.Column<string>(type: "nvarchar(64)", nullable: false, comment: "路线名"),
                    mode = table.Column<byte>(type: "tinyint", nullable: false, comment: "0往返 1循环 2随机"),
                    pause_at_point_seconds = table.Column<double>(type: "float", nullable: false, comment: "每点默认停留秒"),
                    use_navmesh = table.Column<byte>(type: "tinyint", nullable: false, comment: "是否走图连段"),
                    point_count = table.Column<int>(type: "int", nullable: false, comment: "点数（由 CLI 统计写入）"),
                    publish_state = table.Column<byte>(type: "tinyint", nullable: false, comment: "0草稿 1已发布 2停用"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_MonsterPatrolRoute", x => x.route_id);
                },
                comment: "怪物巡逻路线表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_MonsterSkillMount",
                columns: table => new
                {
                    mount_id = table.Column<int>(type: "int", nullable: false, comment: "挂载 id（自增）")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    monster_id = table.Column<int>(type: "int", nullable: false, comment: "怪物编号"),
                    skill_id = table.Column<int>(type: "int", nullable: false, comment: "技能 id"),
                    slot_seq = table.Column<int>(type: "int", nullable: false, comment: "遍历顺序"),
                    slot_type = table.Column<byte>(type: "tinyint", nullable: false, comment: "时机 0战斗1非战斗2残血3受控"),
                    chance_bps = table.Column<int>(type: "int", nullable: false, comment: "释放概率基点 0..10000"),
                    cast_ms = table.Column<int>(type: "int", nullable: false, comment: "吟唱时长毫秒"),
                    cooldown_ms = table.Column<int>(type: "int", nullable: false, comment: "冷却毫秒"),
                    min_range = table.Column<double>(type: "float", nullable: false, comment: "最小射程（米）"),
                    max_range = table.Column<double>(type: "float", nullable: false, comment: "最大射程（米）"),
                    target_select = table.Column<byte>(type: "tinyint", nullable: false, comment: "选敌方式"),
                    target_count = table.Column<int>(type: "int", nullable: false, comment: "最大目标数"),
                    aoe_radius = table.Column<double>(type: "float", nullable: false, comment: "AOE 半径（米）"),
                    school_mask = table.Column<int>(type: "int", nullable: false, comment: "系别位掩码"),
                    interruptible = table.Column<byte>(type: "tinyint", nullable: false, comment: "是否可打断"),
                    allow_movement = table.Column<byte>(type: "tinyint", nullable: false, comment: "是否允许移动施法"),
                    damage_scale_bps = table.Column<int>(type: "int", nullable: false, comment: "伤害倍率基点"),
                    status_id = table.Column<int>(type: "int", nullable: false, comment: "附加状态 id"),
                    status_duration_seconds = table.Column<double>(type: "float", nullable: false, comment: "状态持续秒"),
                    start_emote = table.Column<int>(type: "int", nullable: false, comment: "起手喊话/表情 id"),
                    emergency_hp_percent = table.Column<int>(type: "int", nullable: false, comment: "残血阈值百分比"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_MonsterSkillMount", x => x.mount_id);
                },
                comment: "怪物技能挂载表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_MonsterSpawnPoint",
                columns: table => new
                {
                    spawn_point_id = table.Column<int>(type: "int", nullable: false, comment: "刷怪点 id")
,
                    zone_id = table.Column<int>(type: "int", nullable: false, comment: "区域 id"),
                    shard_id = table.Column<long>(type: "bigint", nullable: false, comment: "分片 id"),
                    x = table.Column<double>(type: "float", nullable: false, comment: "中心 X"),
                    y = table.Column<double>(type: "float", nullable: false, comment: "中心 Y"),
                    z = table.Column<double>(type: "float", nullable: false, comment: "中心 Z"),
                    yaw = table.Column<double>(type: "float", nullable: false, comment: "初始朝向（弧度）"),
                    monster_id = table.Column<int>(type: "int", nullable: false, comment: "怪物编号（必须已发布）"),
                    level_override = table.Column<int>(type: "int", nullable: true, comment: "等级覆盖（NULL=模板基准）"),
                    max_alive = table.Column<int>(type: "int", nullable: false, comment: "同时存活上限（=槽位数）"),
                    slot_spacing = table.Column<double>(type: "float", nullable: false, comment: "槽位横向间距（米）"),
                    respawn_override_seconds = table.Column<double>(type: "float", nullable: false, comment: "刷新秒覆盖（0=用公式）"),
                    route_override_id = table.Column<int>(type: "int", nullable: false, comment: "路线覆盖（0=用行为表）"),
                    enabled = table.Column<byte>(type: "tinyint", nullable: false, comment: "是否启用"),
                    publish_state = table.Column<byte>(type: "tinyint", nullable: false, comment: "0草稿 1已发布 2停用"),
                    content_version = table.Column<int>(type: "int", nullable: false, comment: "内容版本"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_MonsterSpawnPoint", x => x.spawn_point_id);
                },
                comment: "怪物刷怪点表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_MonsterTemplate",
                columns: table => new
                {
                    monster_id = table.Column<int>(type: "int", nullable: false, comment: "怪物编号（号段即身份）")
,
                    name = table.Column<string>(type: "nvarchar(64)", nullable: false, comment: "显示名"),
                    rank = table.Column<byte>(type: "tinyint", nullable: false, comment: "rank 0普通 1精英 2稀有精英 3首领 4世界BOSS"),
                    species = table.Column<byte>(type: "tinyint", nullable: false, comment: "种族编号"),
                    wuxing_domain = table.Column<int>(type: "int", nullable: false, comment: "五行域 1金2木3水4火5土 0=未定"),
                    model_key = table.Column<string>(type: "nvarchar(64)", nullable: false, comment: "客户端模型键"),
                    prefab_guid = table.Column<string>(type: "nvarchar(64)", nullable: true, comment: "预制体资产 Guid（可空）"),
                    model_scale = table.Column<decimal>(type: "decimal(4,2)", nullable: false, comment: "体型缩放"),
                    stand_state = table.Column<byte>(type: "tinyint", nullable: false, comment: "待机姿态 0站1坐2卧3潜水"),
                    level_base = table.Column<int>(type: "int", nullable: false, comment: "基准等级"),
                    level_min = table.Column<int>(type: "int", nullable: false, comment: "等级下界"),
                    level_max = table.Column<int>(type: "int", nullable: false, comment: "等级上界"),
                    hp_base = table.Column<int>(type: "int", nullable: false, comment: "生命公式截距"),
                    hp_per_level = table.Column<int>(type: "int", nullable: false, comment: "生命公式每级增量"),
                    atk_base = table.Column<int>(type: "int", nullable: false, comment: "攻击公式截距"),
                    atk_per_level = table.Column<int>(type: "int", nullable: false, comment: "攻击公式每级增量"),
                    def_base = table.Column<int>(type: "int", nullable: false, comment: "防御公式截距"),
                    def_per_level = table.Column<int>(type: "int", nullable: false, comment: "防御公式每级增量"),
                    hp_override_bps = table.Column<int>(type: "int", nullable: false, comment: "血量覆盖基点 0=不覆盖"),
                    atk_override_bps = table.Column<int>(type: "int", nullable: false, comment: "攻击覆盖基点 0=不覆盖"),
                    xp_base = table.Column<int>(type: "int", nullable: false, comment: "经验基准截距"),
                    xp_per_level = table.Column<int>(type: "int", nullable: false, comment: "经验每级增量"),
                    xp_mod_bps = table.Column<int>(type: "int", nullable: false, comment: "逐模板经验倍率基点（10000=×1）"),
                    health_mod_bps = table.Column<int>(type: "int", nullable: false, comment: "血薄削减经验基点（只有 0..7500 生效，10000=不削减）"),
                    respawn_base_seconds = table.Column<double>(type: "float", nullable: false, comment: "刷新秒截距"),
                    respawn_per_level_seconds = table.Column<double>(type: "float", nullable: false, comment: "刷新秒每级增量"),
                    corpse_seconds = table.Column<double>(type: "float", nullable: false, comment: "尸体停留秒"),
                    aggro_say_id = table.Column<int>(type: "int", nullable: false, comment: "索敌喊话 id"),
                    publish_state = table.Column<byte>(type: "tinyint", nullable: false, comment: "发布状态 0草稿 1已发布 2已停用"),
                    content_version = table.Column<int>(type: "int", nullable: false, comment: "内容版本"),
                    remark = table.Column<string>(type: "nvarchar(500)", nullable: false, comment: "策划备注"),
                    prefab_path = table.Column<string>(type: "nvarchar(256)", nullable: true, comment: "预制体内容路径（客户端按路径解析 GUID）"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_MonsterTemplate", x => x.monster_id);
                },
                comment: "怪物模板表（怪物编辑器主表）");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Game_HunduShijie_MonsterBehavior");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_MonsterDropEntry");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_MonsterLootBinding");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_MonsterPatrolPoint");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_MonsterPatrolRoute");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_MonsterSkillMount");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_MonsterSpawnPoint");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_MonsterTemplate");

            // Down() **故意不删**那五列。Up() 里它们只是"补齐冷启动的新库"（幂等守卫），
            // 而在已有库里它们装着数字人那条线的数据：回滚一个"怪物表"的迁移
            // 不该顺手把别的线的列连同数据一起丢掉（DROP COLUMN 会连带删掉整列内容）。
            // 于是 Up/Down 在这里是**刻意不对称**的：回滚怪物迁移只回收怪物自己的 8 张表。
            // 那五列真正的归属应由数字人那条线的迁移认领；认领之后，Up() 里那段守卫脚本整块搬走。
        }
    }
}
