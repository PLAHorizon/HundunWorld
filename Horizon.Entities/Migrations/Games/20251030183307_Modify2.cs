using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Horizon.Entities.Migrations.Games
{
    /// <inheritdoc />
    public partial class Modify2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_Bag",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false, comment: "自增ID")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    character_id = table.Column<long>(type: "bigint", nullable: false, comment: "角色ID"),
                    bag_type = table.Column<int>(type: "int", nullable: false, comment: "背包类型 0-主背包 1-材料背包 2-任务背包 3-时装背包"),
                    current_slots = table.Column<int>(type: "int", nullable: false, comment: "当前格子数"),
                    max_slots = table.Column<int>(type: "int", nullable: false, comment: "最大格子数"),
                    used_slots = table.Column<int>(type: "int", nullable: false, comment: "已使用格子数"),
                    unlock_time = table.Column<DateTime>(type: "datetime", nullable: true, comment: "解锁时间"),
                    last_sort_time = table.Column<DateTime>(type: "datetime", nullable: true, comment: "最后整理时间"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_Bag", x => x.id);
                },
                comment: "背包信息表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_Character",
                columns: table => new
                {
                    character_id = table.Column<long>(type: "bigint", nullable: false, comment: "角色ID")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    user_id = table.Column<long>(type: "bigint", nullable: false, comment: "用户ID"),
                    character_name = table.Column<string>(type: "nvarchar(20)", nullable: false, comment: "角色名"),
                    level = table.Column<int>(type: "int", nullable: false, comment: "等级"),
                    experience = table.Column<long>(type: "bigint", nullable: false, comment: "经验值"),
                    profession = table.Column<int>(type: "int", nullable: false, comment: "职业 0-剑客 1-刀客 2-枪客 等"),
                    gender = table.Column<int>(type: "int", nullable: false, comment: "性别 0-男 1-女"),
                    sect = table.Column<int>(type: "int", nullable: false, comment: "门派 0-无门派 1-少林寺 2-武当派 等"),
                    faction = table.Column<int>(type: "int", nullable: false, comment: "阵营 0-中立 1-正派 2-邪派 3-隐世"),
                    realm_level = table.Column<int>(type: "int", nullable: false, comment: "境界等级"),
                    combat_power = table.Column<long>(type: "bigint", nullable: false, comment: "战斗力"),
                    map_id = table.Column<int>(type: "int", nullable: false, comment: "当前地图ID"),
                    position_x = table.Column<double>(type: "float", nullable: false, comment: "X坐标"),
                    position_y = table.Column<double>(type: "float", nullable: false, comment: "Y坐标"),
                    position_z = table.Column<double>(type: "float", nullable: false, comment: "Z坐标"),
                    rotation = table.Column<double>(type: "float", nullable: false, comment: "朝向"),
                    current_title_id = table.Column<int>(type: "int", nullable: true, comment: "当前称号ID"),
                    chivalry_points = table.Column<int>(type: "int", nullable: false, comment: "侠义值"),
                    evil_points = table.Column<int>(type: "int", nullable: false, comment: "恶名值"),
                    reputation = table.Column<int>(type: "int", nullable: false, comment: "声望值"),
                    create_time = table.Column<DateTime>(type: "datetime", nullable: false, comment: "创建时间"),
                    last_login_time = table.Column<DateTime>(type: "datetime", nullable: true, comment: "最后登录时间"),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false, comment: "是否删除"),
                    delete_time = table.Column<DateTime>(type: "datetime", nullable: true, comment: "删除时间"),
                    hair_model = table.Column<int>(type: "int", nullable: false, comment: "头发模型"),
                    hair_color = table.Column<int>(type: "int", nullable: false, comment: "头发颜色"),
                    face_model = table.Column<int>(type: "int", nullable: false, comment: "脸型模型"),
                    skin_color = table.Column<int>(type: "int", nullable: false, comment: "皮肤颜色"),
                    eye_color = table.Column<int>(type: "int", nullable: false, comment: "眼睛颜色"),
                    game_user_id = table.Column<long>(type: "bigint", nullable: false, comment: "游戏用户ID"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_Character", x => x.character_id);
                },
                comment: "角色信息表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_CharacterActivity",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false, comment: "自增ID")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    character_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    daily_reward_claimed = table.Column<bool>(type: "bit", nullable: false),
                    weekly_reward_claimed = table.Column<bool>(type: "bit", nullable: false),
                    monthly_reward_claimed = table.Column<bool>(type: "bit", nullable: false),
                    milestone_rewards = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_CharacterActivity", x => x.id);
                },
                comment: "角色活跃度表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_CharacterAttribute",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false, comment: "自增ID")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    character_id = table.Column<long>(type: "bigint", nullable: false),
                    constitution = table.Column<int>(type: "int", nullable: false),
                    comprehension = table.Column<int>(type: "int", nullable: false),
                    agility = table.Column<int>(type: "int", nullable: false),
                    strength = table.Column<int>(type: "int", nullable: false),
                    internal_force = table.Column<int>(type: "int", nullable: false),
                    current_health = table.Column<int>(type: "int", nullable: false),
                    max_health = table.Column<int>(type: "int", nullable: false),
                    current_internal_energy = table.Column<int>(type: "int", nullable: false),
                    max_internal_energy = table.Column<int>(type: "int", nullable: false),
                    attack_power = table.Column<int>(type: "int", nullable: false),
                    defense = table.Column<int>(type: "int", nullable: false),
                    hit_rate = table.Column<float>(type: "real", nullable: false),
                    dodge_rate = table.Column<float>(type: "real", nullable: false),
                    critical_rate = table.Column<float>(type: "real", nullable: false),
                    critical_damage = table.Column<float>(type: "real", nullable: false),
                    move_speed = table.Column<float>(type: "real", nullable: false),
                    attack_speed = table.Column<float>(type: "real", nullable: false),
                    metal_attack = table.Column<int>(type: "int", nullable: false),
                    wood_attack = table.Column<int>(type: "int", nullable: false),
                    water_attack = table.Column<int>(type: "int", nullable: false),
                    fire_attack = table.Column<int>(type: "int", nullable: false),
                    earth_attack = table.Column<int>(type: "int", nullable: false),
                    metal_resistance = table.Column<int>(type: "int", nullable: false),
                    wood_resistance = table.Column<int>(type: "int", nullable: false),
                    water_resistance = table.Column<int>(type: "int", nullable: false),
                    fire_resistance = table.Column<int>(type: "int", nullable: false),
                    earth_resistance = table.Column<int>(type: "int", nullable: false),
                    internal_attack = table.Column<int>(type: "int", nullable: false),
                    external_attack = table.Column<int>(type: "int", nullable: false),
                    internal_defense = table.Column<int>(type: "int", nullable: false),
                    external_defense = table.Column<int>(type: "int", nullable: false),
                    block_rate = table.Column<float>(type: "real", nullable: false),
                    tenacity = table.Column<int>(type: "int", nullable: false),
                    damage_reduction = table.Column<float>(type: "real", nullable: false),
                    reflect_damage = table.Column<float>(type: "real", nullable: false),
                    qi_shield = table.Column<int>(type: "int", nullable: false),
                    health_regeneration = table.Column<int>(type: "int", nullable: false),
                    energy_regeneration = table.Column<int>(type: "int", nullable: false),
                    update_time = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_CharacterAttribute", x => x.id);
                },
                comment: "角色属性表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_CharacterSkill",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false, comment: "自增ID")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    character_id = table.Column<long>(type: "bigint", nullable: false, comment: "角色ID"),
                    skill_id = table.Column<int>(type: "int", nullable: false, comment: "技能ID"),
                    skill_level = table.Column<int>(type: "int", nullable: false, comment: "技能等级"),
                    proficiency = table.Column<long>(type: "bigint", nullable: false, comment: "当前熟练度"),
                    max_proficiency = table.Column<long>(type: "bigint", nullable: false, comment: "升级所需熟练度"),
                    skill_realm = table.Column<int>(type: "int", nullable: false, comment: "技能境界 0-初窥门径 1-登堂入室 2-融会贯通 3-炉火纯青 4-登峰造极"),
                    comprehension_rate = table.Column<double>(type: "float", nullable: false, comment: "领悟度（0-100）"),
                    is_equipped = table.Column<bool>(type: "bit", nullable: false, comment: "是否装备到快捷栏"),
                    slot_index = table.Column<int>(type: "int", nullable: true, comment: "快捷栏位置"),
                    learn_source = table.Column<int>(type: "int", nullable: false, comment: "学习来源 0-门派传授 1-秘籍学习 2-自创 3-传功 4-顿悟"),
                    learn_time = table.Column<DateTime>(type: "datetime", nullable: false, comment: "学习时间"),
                    last_use_time = table.Column<DateTime>(type: "datetime", nullable: true, comment: "最后使用时间"),
                    use_count = table.Column<long>(type: "bigint", nullable: false, comment: "使用次数"),
                    is_custom = table.Column<bool>(type: "bit", nullable: false, comment: "是否自创技能"),
                    custom_name = table.Column<string>(type: "nvarchar(50)", nullable: false, comment: "自创技能名称"),
                    skill_bonus = table.Column<string>(type: "nvarchar(500)", nullable: false, comment: "技能加成（JSON格式）"),
                    is_locked = table.Column<bool>(type: "bit", nullable: false, comment: "是否锁定（防止误操作）"),
                    update_time = table.Column<DateTime>(type: "datetime", nullable: false, comment: "更新时间"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_CharacterSkill", x => x.id);
                },
                comment: "角色技能表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_CharacterTitle",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false, comment: "自增ID")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    character_id = table.Column<long>(type: "bigint", nullable: false, comment: "角色ID"),
                    title_name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, comment: "称号名称"),
                    acquire_condition = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, comment: "获得条件描述"),
                    attribute_bonus = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "属性加成（JSON格式）"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_CharacterTitle", x => x.id);
                },
                comment: "角色称号表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_ChatBlacklist",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false, comment: "记录ID")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    character_id = table.Column<long>(type: "bigint", nullable: false, comment: "角色ID"),
                    blocked_character_id = table.Column<long>(type: "bigint", nullable: false, comment: "被屏蔽的角色ID"),
                    blocked_character_name = table.Column<string>(type: "nvarchar(20)", nullable: false, comment: "被屏蔽的角色名"),
                    block_reason = table.Column<string>(type: "nvarchar(200)", nullable: false, comment: "屏蔽原因"),
                    block_time = table.Column<DateTime>(type: "datetime", nullable: false, comment: "屏蔽时间"),
                    is_permanent = table.Column<bool>(type: "bit", nullable: false, comment: "是否永久屏蔽"),
                    unblock_time = table.Column<DateTime>(type: "datetime", nullable: true, comment: "解除屏蔽时间"),
                    is_active = table.Column<bool>(type: "bit", nullable: false, comment: "是否有效"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_ChatBlacklist", x => x.id);
                },
                comment: "聊天黑名单表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_ChatChannelSetting",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false, comment: "设置ID")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    channel = table.Column<int>(type: "int", nullable: false, comment: "频道类型 0-私聊 1-队伍 2-帮会 3-世界 4-跨服 5-附近 6-系统 7-喇叭"),
                    channel_name = table.Column<string>(type: "nvarchar(20)", nullable: false, comment: "频道名称"),
                    min_level = table.Column<int>(type: "int", nullable: false, comment: "最小等级要求"),
                    min_activity_level = table.Column<int>(type: "int", nullable: false, comment: "最小活跃等级要求"),
                    cooldown = table.Column<int>(type: "int", nullable: false, comment: "发言间隔（秒）"),
                    consume_item_id = table.Column<int>(type: "int", nullable: true, comment: "消耗道具ID"),
                    consume_item_count = table.Column<int>(type: "int", nullable: false, comment: "消耗道具数量"),
                    max_length = table.Column<int>(type: "int", nullable: false, comment: "消息最大长度"),
                    allow_rich_text = table.Column<bool>(type: "bit", nullable: false, comment: "是否支持富文本"),
                    allow_voice = table.Column<bool>(type: "bit", nullable: false, comment: "是否支持语音"),
                    allow_item_link = table.Column<bool>(type: "bit", nullable: false, comment: "是否支持物品链接"),
                    allow_location = table.Column<bool>(type: "bit", nullable: false, comment: "是否支持位置分享"),
                    save_days = table.Column<int>(type: "int", nullable: false, comment: "消息保存天数"),
                    is_enabled = table.Column<bool>(type: "bit", nullable: false, comment: "是否启用"),
                    create_time = table.Column<DateTime>(type: "datetime", nullable: false, comment: "创建时间"),
                    update_time = table.Column<DateTime>(type: "datetime", nullable: false, comment: "更新时间"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_ChatChannelSetting", x => x.id);
                },
                comment: "聊天频道设置表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_ChatMessage",
                columns: table => new
                {
                    message_id = table.Column<long>(type: "bigint", nullable: false, comment: "消息ID")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    sender_id = table.Column<long>(type: "bigint", nullable: false, comment: "发送者角色ID"),
                    sender_name = table.Column<string>(type: "nvarchar(20)", nullable: false, comment: "发送者角色名"),
                    sender_level = table.Column<int>(type: "int", nullable: false, comment: "发送者等级"),
                    sender_activity_level = table.Column<int>(type: "int", nullable: false, comment: "发送者活跃等级"),
                    channel = table.Column<int>(type: "int", nullable: false, comment: "聊天频道 0-私聊 1-队伍 2-帮会 3-世界 4-跨服 5-附近 6-系统 7-喇叭"),
                    receiver_id = table.Column<long>(type: "bigint", nullable: true, comment: "接收者ID（私聊时使用）"),
                    receiver_name = table.Column<string>(type: "nvarchar(20)", nullable: false, comment: "接收者名称（私聊时使用）"),
                    content = table.Column<string>(type: "nvarchar(500)", nullable: false, comment: "消息内容"),
                    content_type = table.Column<int>(type: "int", nullable: false, comment: "消息类型 0-文本 1-表情 2-物品链接 3-位置信息 4-语音 5-图片 6-红包"),
                    ext_data = table.Column<string>(type: "nvarchar(2000)", nullable: false, comment: "扩展数据（JSON格式，包含物品信息、位置坐标等）"),
                    voice_duration = table.Column<int>(type: "int", nullable: true, comment: "语音时长（秒）"),
                    voice_url = table.Column<string>(type: "varchar(500)", nullable: false, comment: "语音文件URL"),
                    is_read = table.Column<bool>(type: "bit", nullable: false, comment: "是否已读（私聊使用）"),
                    send_time = table.Column<DateTime>(type: "datetime", nullable: false, comment: "发送时间"),
                    status = table.Column<int>(type: "int", nullable: false, comment: "消息状态 0-正常 1-已撤回 2-已屏蔽 3-已删除"),
                    server_id = table.Column<int>(type: "int", nullable: false, comment: "服务器ID"),
                    team_id = table.Column<long>(type: "bigint", nullable: true, comment: "队伍ID（队伍频道使用）"),
                    guild_id = table.Column<long>(type: "bigint", nullable: true, comment: "帮会ID（帮会频道使用）"),
                    consume_item_id = table.Column<int>(type: "int", nullable: true, comment: "消耗道具ID（喇叭频道使用）"),
                    has_sensitive = table.Column<bool>(type: "bit", nullable: false, comment: "是否含有敏感词"),
                    filtered_content = table.Column<string>(type: "nvarchar(500)", nullable: false, comment: "过滤后内容"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_ChatMessage", x => x.message_id);
                },
                comment: "聊天消息表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_ChatPrivateMessage",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false, comment: "记录ID")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    message_id = table.Column<long>(type: "bigint", nullable: false, comment: "消息ID"),
                    sender_id = table.Column<long>(type: "bigint", nullable: false, comment: "发送者角色ID"),
                    receiver_id = table.Column<long>(type: "bigint", nullable: false, comment: "接收者角色ID"),
                    session_id = table.Column<string>(type: "varchar(100)", nullable: false, comment: "会话ID（双方ID组合）"),
                    is_read = table.Column<bool>(type: "bit", nullable: false, comment: "是否已读"),
                    read_time = table.Column<DateTime>(type: "datetime", nullable: true, comment: "读取时间"),
                    is_deleted_sender = table.Column<bool>(type: "bit", nullable: false, comment: "是否删除（发送方）"),
                    is_deleted_receiver = table.Column<bool>(type: "bit", nullable: false, comment: "是否删除（接收方）"),
                    create_time = table.Column<DateTime>(type: "datetime", nullable: false, comment: "创建时间"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_ChatPrivateMessage", x => x.id);
                },
                comment: "私聊消息记录表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_Currency",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false, comment: "自增ID")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    character_id = table.Column<long>(type: "bigint", nullable: false),
                    currency_type = table.Column<int>(type: "int", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    total_earned = table.Column<long>(type: "bigint", nullable: false),
                    total_spent = table.Column<long>(type: "bigint", nullable: false),
                    update_time = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_Currency", x => x.id);
                },
                comment: "货币信息表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_Game",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    game_name = table.Column<string>(type: "varchar(255)", nullable: false, comment: "游戏名称"),
                    game_description = table.Column<string>(type: "text", nullable: false, comment: "游戏描述"),
                    game_version = table.Column<string>(type: "varchar(50)", nullable: false, comment: "游戏版本"),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "创建时间"),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "更新时间"),
                    developer = table.Column<string>(type: "varchar(255)", nullable: false, comment: "开发商"),
                    publisher = table.Column<string>(type: "varchar(255)", nullable: false, comment: "发行商"),
                    genre = table.Column<string>(type: "varchar(255)", nullable: false, comment: "游戏类型"),
                    platform = table.Column<string>(type: "varchar(255)", nullable: false, comment: "平台"),
                    cover_image_url = table.Column<string>(type: "varchar(500)", nullable: false, comment: "封面图片URL"),
                    cover_url = table.Column<string>(type: "varchar(500)", nullable: false, comment: "封面URL"),
                    trailer_url = table.Column<string>(type: "varchar(500)", nullable: false, comment: "预告片URL"),
                    website_url = table.Column<string>(type: "varchar(500)", nullable: false, comment: "官方网站URL"),
                    tags = table.Column<string>(type: "text", nullable: false, comment: "标签 (JSON 字符串)"),
                    languages = table.Column<string>(type: "text", nullable: false, comment: "支持语言 (JSON 字符串)"),
                    features = table.Column<string>(type: "text", nullable: false, comment: "游戏特色 (JSON 字符串)"),
                    system_requirements = table.Column<string>(type: "text", nullable: false, comment: "系统要求 (JSON 字符串)"),
                    screenshots = table.Column<string>(type: "text", nullable: false, comment: "游戏截图URL列表 (JSON 字符串)"),
                    videos = table.Column<string>(type: "text", nullable: false, comment: "游戏视频 (JSON 字符串)"),
                    videos_url = table.Column<string>(type: "text", nullable: false, comment: "游戏视频URL列表 (JSON 字符串)"),
                    dlcs = table.Column<string>(type: "text", nullable: false, comment: "DLC列表 (JSON 字符串)"),
                    achievements = table.Column<string>(type: "text", nullable: false, comment: "成就列表 (JSON 字符串)"),
                    mods = table.Column<string>(type: "text", nullable: false, comment: "模组列表 (JSON 字符串)"),
                    community_links = table.Column<string>(type: "text", nullable: false, comment: "社区链接 (JSON 字符串)"),
                    game_modes = table.Column<string>(type: "text", nullable: false, comment: "游戏模式 (JSON 字符串)"),
                    game_settings = table.Column<string>(type: "text", nullable: false, comment: "游戏设置 (JSON 字符串)"),
                    game_sources = table.Column<string>(type: "text", nullable: false, comment: "游戏来源 (JSON 字符串)"),
                    game_assets = table.Column<string>(type: "text", nullable: false, comment: "游戏资产 (JSON 字符串)"),
                    game_assets_url = table.Column<string>(type: "text", nullable: false, comment: "游戏资产URL (JSON 字符串)"),
                    game_assets_urls = table.Column<string>(type: "text", nullable: false, comment: "游戏资产URL列表 (JSON 字符串)"),
                    game_assets_urls_url = table.Column<string>(type: "text", nullable: false, comment: "游戏资产URL的URL (JSON 字符串)")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_Game", x => x.Id);
                },
                comment: "游戏信息表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_Guild",
                columns: table => new
                {
                    guild_id = table.Column<long>(type: "bigint", nullable: false, comment: "帮会ID")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    guild_name = table.Column<string>(type: "nvarchar(50)", nullable: false, comment: "帮会名称"),
                    guild_level = table.Column<int>(type: "int", nullable: false, comment: "帮会等级"),
                    guild_experience = table.Column<long>(type: "bigint", nullable: false, comment: "帮会经验"),
                    create_time = table.Column<DateTime>(type: "datetime", nullable: false, comment: "创建时间"),
                    leader_id = table.Column<long>(type: "bigint", nullable: false, comment: "帮主ID"),
                    announcement = table.Column<string>(type: "nvarchar(200)", nullable: false, comment: "帮会公告"),
                    guild_status = table.Column<int>(type: "int", nullable: false, comment: "帮会状态"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_Guild", x => x.guild_id);
                },
                comment: "帮会信息表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_Item",
                columns: table => new
                {
                    item_uid = table.Column<long>(type: "bigint", nullable: false, comment: "物品唯一ID")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    item_id = table.Column<int>(type: "int", nullable: false, comment: "物品模板ID"),
                    owner_id = table.Column<long>(type: "bigint", nullable: false, comment: "拥有者ID（角色ID）"),
                    item_type = table.Column<int>(type: "int", nullable: false, comment: "物品类型 0-武器 1-防具 2-饰品 3-消耗品 4-材料"),
                    quality = table.Column<int>(type: "int", nullable: false, comment: "物品品质 0-普通 1-精良 2-稀有 3-史诗 4-传说 5-神器"),
                    quantity = table.Column<int>(type: "int", nullable: false, comment: "数量"),
                    enhance_level = table.Column<int>(type: "int", nullable: false, comment: "强化等级"),
                    gem_slots = table.Column<int>(type: "int", nullable: false, comment: "宝石槽数量 0-5"),
                    element = table.Column<int>(type: "int", nullable: true, comment: "五行属性 0-金 1-木 2-水 3-火 4-土"),
                    set_id = table.Column<int>(type: "int", nullable: true, comment: "套装ID"),
                    bind_type = table.Column<int>(type: "int", nullable: false, comment: "绑定类型 0-不绑定 1-拾取绑定 2-装备绑定 3-使用绑定"),
                    is_bound = table.Column<bool>(type: "bit", nullable: false, comment: "是否已绑定"),
                    is_equipped = table.Column<bool>(type: "bit", nullable: false, comment: "是否已装备"),
                    equip_slot = table.Column<int>(type: "int", nullable: true, comment: "装备位置"),
                    location_type = table.Column<int>(type: "int", nullable: false, comment: "位置类型 0-背包 1-仓库 2-邮件 3-交易"),
                    bag_slot = table.Column<int>(type: "int", nullable: true, comment: "背包位置"),
                    durability = table.Column<int>(type: "int", nullable: true, comment: "耐久度"),
                    max_durability = table.Column<int>(type: "int", nullable: true, comment: "最大耐久度"),
                    enchant_id = table.Column<int>(type: "int", nullable: true, comment: "附魔ID"),
                    enchant_level = table.Column<int>(type: "int", nullable: true, comment: "附魔等级"),
                    expire_time = table.Column<DateTime>(type: "datetime", nullable: true, comment: "过期时间"),
                    acquire_time = table.Column<DateTime>(type: "datetime", nullable: false, comment: "获得时间"),
                    is_locked = table.Column<bool>(type: "bit", nullable: false, comment: "是否锁定"),
                    synthesis_materials = table.Column<string>(type: "nvarchar(500)", nullable: false, comment: "合成材料来源（JSON）"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_Item", x => x.item_uid);
                },
                comment: "物品信息表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_ItemAttribute",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false, comment: "自增ID")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    item_uid = table.Column<long>(type: "bigint", nullable: false),
                    attribute_type = table.Column<int>(type: "int", nullable: false),
                    attribute_value = table.Column<float>(type: "real", nullable: false),
                    value_type = table.Column<int>(type: "int", nullable: false),
                    is_random = table.Column<bool>(type: "bit", nullable: false),
                    source_type = table.Column<int>(type: "int", nullable: false),
                    source_id = table.Column<int>(type: "int", nullable: true),
                    attribute_quality = table.Column<int>(type: "int", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_ItemAttribute", x => x.id);
                },
                comment: "物品属性表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_ItemGem",
                columns: table => new
                {
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    item_uid = table.Column<long>(type: "bigint", nullable: false),
                    slot_index = table.Column<int>(type: "int", nullable: false),
                    gem_id = table.Column<int>(type: "int", nullable: false),
                    gem_level = table.Column<int>(type: "int", nullable: false),
                    gem_element = table.Column<int>(type: "int", nullable: false),
                    inlay_time = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_ItemGem", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_ItemTemplate",
                columns: table => new
                {
                    item_id = table.Column<int>(type: "int", nullable: false, comment: "物品模板ID")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    item_name = table.Column<string>(type: "nvarchar(50)", nullable: false, comment: "物品名称"),
                    description = table.Column<string>(type: "nvarchar(500)", nullable: false, comment: "物品描述"),
                    item_type = table.Column<int>(type: "int", nullable: false, comment: "物品类型 0-武器 1-防具 2-饰品 3-消耗品 4-材料 5-任务物品 6-秘籍 7-配方 8-宝箱"),
                    sub_type = table.Column<int>(type: "int", nullable: false, comment: "物品子类型（如武器类型、防具部位等）"),
                    base_quality = table.Column<int>(type: "int", nullable: false, comment: "基础品质 0-普通 1-精良 2-稀有 3-史诗 4-传说 5-神器"),
                    rarity = table.Column<int>(type: "int", nullable: false, comment: "稀有度 0-常见 1-少见 2-稀少 3-罕见 4-极其罕见 5-绝世珍稀"),
                    element = table.Column<int>(type: "int", nullable: false, comment: "五行属性 0-金 1-木 2-水 3-火 4-土 5-无"),
                    material_grade = table.Column<int>(type: "int", nullable: false, comment: "材料品阶 1-9级"),
                    level_require = table.Column<int>(type: "int", nullable: false, comment: "使用等级需求"),
                    realm_require = table.Column<int>(type: "int", nullable: false, comment: "境界等级需求"),
                    profession_limit = table.Column<string>(type: "varchar(50)", nullable: false, comment: "职业限制（逗号分隔的职业ID）"),
                    max_stack = table.Column<int>(type: "int", nullable: false, comment: "最大叠加数"),
                    drop_rate = table.Column<decimal>(type: "decimal(5,4)", nullable: false, comment: "基础掉落概率（0.0001-1.0000）"),
                    source_type = table.Column<int>(type: "int", nullable: false, comment: "出处类型 0-怪物掉落 1-采集获得 2-任务奖励 3-副本产出 4-商店购买 5-合成产出 6-活动奖励"),
                    source_detail = table.Column<string>(type: "nvarchar(500)", nullable: false, comment: "出处详情（JSON格式，包含怪物ID、地图ID、NPC ID等）"),
                    gather_type = table.Column<int>(type: "int", nullable: true, comment: "采集类型 0-矿物 1-草药 2-木材 3-兽皮 4-其他"),
                    base_attributes = table.Column<string>(type: "nvarchar(1000)", nullable: false, comment: "基础属性（JSON格式，包含属性类型和数值）"),
                    random_attributes = table.Column<string>(type: "nvarchar(2000)", nullable: false, comment: "随机属性池（JSON格式，定义可能出现的随机属性）"),
                    random_attr_count = table.Column<string>(type: "varchar(20)", nullable: false, comment: "随机属性数量范围（如1-3）"),
                    gem_slot_rates = table.Column<string>(type: "varchar(100)", nullable: false, comment: "宝石槽概率（JSON格式，定义0-5个槽的概率）"),
                    synthesis_recipe = table.Column<string>(type: "nvarchar(500)", nullable: false, comment: "合成配方（JSON格式，定义合成所需材料）"),
                    inherit_rate = table.Column<string>(type: "varchar(50)", nullable: false, comment: "属性继承率范围（如10-80）"),
                    element_bonus = table.Column<decimal>(type: "decimal(3,2)", nullable: false, comment: "五行相生加成系数"),
                    element_penalty = table.Column<decimal>(type: "decimal(3,2)", nullable: false, comment: "五行相克减益系数"),
                    set_id = table.Column<int>(type: "int", nullable: true, comment: "所属套装ID"),
                    bind_type = table.Column<int>(type: "int", nullable: false, comment: "绑定类型 0-不绑定 1-拾取绑定 2-装备绑定 3-使用绑定"),
                    sell_price = table.Column<int>(type: "int", nullable: false, comment: "出售价格（铜币）"),
                    buy_price = table.Column<int>(type: "int", nullable: false, comment: "购买价格（铜币）"),
                    icon_path = table.Column<string>(type: "varchar(200)", nullable: false, comment: "图标资源路径"),
                    model_path = table.Column<string>(type: "varchar(200)", nullable: false, comment: "3D模型资源路径"),
                    use_effect = table.Column<string>(type: "nvarchar(500)", nullable: false, comment: "使用效果（JSON格式，定义使用后的效果）"),
                    can_trade = table.Column<bool>(type: "bit", nullable: false, comment: "是否可交易"),
                    can_destroy = table.Column<bool>(type: "bit", nullable: false, comment: "是否可销毁"),
                    is_unique = table.Column<bool>(type: "bit", nullable: false, comment: "是否唯一物品"),
                    valid_days = table.Column<int>(type: "int", nullable: false, comment: "有效期（天数，0表示永久）"),
                    create_time = table.Column<DateTime>(type: "datetime", nullable: false, comment: "创建时间"),
                    update_time = table.Column<DateTime>(type: "datetime", nullable: false, comment: "更新时间"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_ItemTemplate", x => x.item_id);
                },
                comment: "物品模板表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_Material",
                columns: table => new
                {
                    material_uid = table.Column<long>(type: "bigint", nullable: false, comment: "材料唯一ID")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    material_id = table.Column<int>(type: "int", nullable: false),
                    owner_id = table.Column<long>(type: "bigint", nullable: false),
                    element = table.Column<int>(type: "int", nullable: false),
                    grade = table.Column<int>(type: "int", nullable: false),
                    rarity = table.Column<int>(type: "int", nullable: false),
                    quantity = table.Column<int>(type: "int", nullable: false),
                    is_bound = table.Column<bool>(type: "bit", nullable: false),
                    acquire_time = table.Column<DateTime>(type: "datetime2", nullable: false),
                    source_type = table.Column<int>(type: "int", nullable: false),
                    source_id = table.Column<int>(type: "int", nullable: true),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_Material", x => x.material_uid);
                },
                comment: "材料信息表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_MaterialSynthesisLog",
                columns: table => new
                {
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    character_id = table.Column<long>(type: "bigint", nullable: false),
                    recipe_id = table.Column<int>(type: "int", nullable: false),
                    result_item_id = table.Column<int>(type: "int", nullable: false),
                    result_quality = table.Column<int>(type: "int", nullable: false),
                    result_quantity = table.Column<int>(type: "int", nullable: false),
                    is_success = table.Column<bool>(type: "bit", nullable: false),
                    used_materials = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    inherited_attributes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    wuxing_bonus = table.Column<float>(type: "real", nullable: false),
                    synthesis_time = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_MaterialSynthesisLog", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_Server",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    zone_id = table.Column<int>(type: "int", nullable: false, comment: "分区ID"),
                    server_name = table.Column<string>(type: "varchar(255)", nullable: false, comment: "服务器名称"),
                    ip_address = table.Column<string>(type: "varchar(255)", nullable: false, comment: "IP地址"),
                    port = table.Column<int>(type: "int", nullable: false, comment: "端口"),
                    status = table.Column<string>(type: "varchar(50)", nullable: false, comment: "服务器状态"),
                    max_players = table.Column<int>(type: "int", nullable: false, comment: "最大玩家数"),
                    current_players = table.Column<int>(type: "int", nullable: false, comment: "当前玩家数"),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "创建时间"),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "更新时间")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_Server", x => x.Id);
                },
                comment: "游戏服务器信息表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_SetItem",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false, comment: "自增ID")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    character_id = table.Column<long>(type: "bigint", nullable: false, comment: "角色ID"),
                    set_id = table.Column<int>(type: "int", nullable: false, comment: "套装ID"),
                    set_name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, comment: "套装名称"),
                    equipped_count = table.Column<int>(type: "int", nullable: false, comment: "已装备件数"),
                    total_pieces = table.Column<int>(type: "int", nullable: false, comment: "套装总件数"),
                    set_element = table.Column<int>(type: "int", nullable: false, comment: "套装五行属性"),
                    set_quality = table.Column<int>(type: "int", nullable: false, comment: "套装品质"),
                    active_effects = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "激活的套装效果（JSON格式）"),
                    equipped_items = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "装备的物品ID列表（JSON格式）"),
                    update_time = table.Column<DateTime>(type: "datetime", nullable: false, comment: "更新时间"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_SetItem", x => x.id);
                },
                comment: "套装物品表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_SkillAdvancePath",
                columns: table => new
                {
                    path_id = table.Column<int>(type: "int", nullable: false, comment: "路径ID")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    base_skill_id = table.Column<int>(type: "int", nullable: false, comment: "基础技能ID"),
                    advance_skill_id = table.Column<int>(type: "int", nullable: false, comment: "进阶技能ID"),
                    path_name = table.Column<string>(type: "nvarchar(50)", nullable: false, comment: "路径名称"),
                    description = table.Column<string>(type: "nvarchar(500)", nullable: false, comment: "路径描述"),
                    advance_type = table.Column<int>(type: "int", nullable: false, comment: "进阶类型 0-正统进阶 1-变异进阶 2-融合进阶 3-顿悟进阶"),
                    skill_level_require = table.Column<int>(type: "int", nullable: false, comment: "基础技能等级要求"),
                    skill_realm_require = table.Column<int>(type: "int", nullable: false, comment: "技能境界要求"),
                    level_require = table.Column<int>(type: "int", nullable: false, comment: "角色等级要求"),
                    realm_require = table.Column<int>(type: "int", nullable: false, comment: "境界要求"),
                    comprehension_require = table.Column<int>(type: "int", nullable: false, comment: "悟性要求"),
                    material_require = table.Column<string>(type: "nvarchar(1000)", nullable: false, comment: "材料需求（JSON格式）"),
                    currency_cost = table.Column<string>(type: "nvarchar(500)", nullable: false, comment: "货币消耗（JSON格式）"),
                    success_rate = table.Column<double>(type: "float", nullable: false, comment: "基础成功率（0-100）"),
                    assist_skills = table.Column<string>(type: "varchar(200)", nullable: false, comment: "辅助技能ID（逗号分隔，可提高成功率）"),
                    special_condition = table.Column<string>(type: "nvarchar(500)", nullable: false, comment: "特殊条件（JSON格式）"),
                    fail_penalty = table.Column<string>(type: "nvarchar(500)", nullable: false, comment: "失败惩罚（JSON格式）"),
                    is_repeatable = table.Column<bool>(type: "bit", nullable: false, comment: "是否可重复进阶"),
                    create_time = table.Column<DateTime>(type: "datetime", nullable: false, comment: "创建时间"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_SkillAdvancePath", x => x.path_id);
                },
                comment: "技能进阶路径表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_SkillBook",
                columns: table => new
                {
                    book_id = table.Column<int>(type: "int", nullable: false, comment: "秘籍ID")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    book_name = table.Column<string>(type: "nvarchar(50)", nullable: false, comment: "秘籍名称"),
                    description = table.Column<string>(type: "nvarchar(500)", nullable: false, comment: "秘籍描述"),
                    skill_id = table.Column<int>(type: "int", nullable: false, comment: "对应技能ID"),
                    quality = table.Column<int>(type: "int", nullable: false, comment: "秘籍品质 0-普通 1-精良 2-稀有 3-史诗 4-传说 5-神话"),
                    book_type = table.Column<int>(type: "int", nullable: false, comment: "秘籍类型 0-完整秘籍 1-残卷 2-心得 3-传承玉简"),
                    level_require = table.Column<int>(type: "int", nullable: false, comment: "学习等级需求"),
                    comprehension_require = table.Column<int>(type: "int", nullable: false, comment: "悟性需求"),
                    learn_duration = table.Column<int>(type: "int", nullable: false, comment: "学习时长（分钟）"),
                    learn_success_rate = table.Column<double>(type: "float", nullable: false, comment: "基础学习成功率"),
                    max_skill_level = table.Column<int>(type: "int", nullable: false, comment: "可学习到的最高技能等级"),
                    use_limit = table.Column<int>(type: "int", nullable: false, comment: "使用次数限制（0为无限）"),
                    extra_effects = table.Column<string>(type: "nvarchar(500)", nullable: false, comment: "额外效果（JSON格式）"),
                    source_info = table.Column<string>(type: "nvarchar(500)", nullable: false, comment: "出处说明"),
                    create_time = table.Column<DateTime>(type: "datetime", nullable: false, comment: "创建时间"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_SkillBook", x => x.book_id);
                },
                comment: "技能秘籍表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_SkillCustomCreate",
                columns: table => new
                {
                    custom_skill_id = table.Column<long>(type: "bigint", nullable: false, comment: "自创技能ID")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    creator_id = table.Column<long>(type: "bigint", nullable: false, comment: "创建者角色ID"),
                    creator_name = table.Column<string>(type: "nvarchar(20)", nullable: false, comment: "创建者角色名"),
                    skill_name = table.Column<string>(type: "nvarchar(50)", nullable: false, comment: "自创技能名称"),
                    description = table.Column<string>(type: "nvarchar(500)", nullable: false, comment: "技能描述"),
                    base_skill_id = table.Column<int>(type: "int", nullable: false, comment: "基础技能模板ID"),
                    fusion_skills = table.Column<string>(type: "varchar(200)", nullable: false, comment: "融合技能ID列表（JSON格式）"),
                    create_type = table.Column<int>(type: "int", nullable: false, comment: "创造类型 0-改良 1-融合 2-顿悟 3-传承"),
                    skill_effects = table.Column<string>(type: "nvarchar(2000)", nullable: false, comment: "技能效果（JSON格式）"),
                    energy_cost = table.Column<int>(type: "int", nullable: false, comment: "内力消耗"),
                    cooldown = table.Column<int>(type: "int", nullable: false, comment: "冷却时间（毫秒）"),
                    power_factor = table.Column<double>(type: "float", nullable: false, comment: "威力系数"),
                    innovation_rate = table.Column<double>(type: "float", nullable: false, comment: "创新度评分（0-100）"),
                    completion_rate = table.Column<double>(type: "float", nullable: false, comment: "完成度（0-100）"),
                    inherit_count = table.Column<int>(type: "int", nullable: false, comment: "被传承次数"),
                    rating_score = table.Column<double>(type: "float", nullable: false, comment: "玩家评价分数"),
                    rating_count = table.Column<int>(type: "int", nullable: false, comment: "评价人数"),
                    is_public = table.Column<bool>(type: "bit", nullable: false, comment: "是否公开"),
                    inherit_price = table.Column<int>(type: "int", nullable: false, comment: "传承价格（元宝）"),
                    create_time = table.Column<DateTime>(type: "datetime", nullable: false, comment: "创建时间"),
                    update_time = table.Column<DateTime>(type: "datetime", nullable: false, comment: "更新时间"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_SkillCustomCreate", x => x.custom_skill_id);
                },
                comment: "自创技能表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_SkillTemplate",
                columns: table => new
                {
                    skill_id = table.Column<int>(type: "int", nullable: false, comment: "技能ID")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    skill_name = table.Column<string>(type: "nvarchar(50)", nullable: false, comment: "技能名称"),
                    description = table.Column<string>(type: "nvarchar(500)", nullable: false, comment: "技能描述"),
                    skill_type = table.Column<int>(type: "int", nullable: false, comment: "技能类型 0-主动攻击 1-主动辅助 2-被动增益 3-心法 4-轻功 5-内功 6-绝学"),
                    skill_school = table.Column<int>(type: "int", nullable: false, comment: "技能流派 0-外功 1-内功 2-医术 3-毒术 4-音律 5-机关"),
                    sect_id = table.Column<int>(type: "int", nullable: false, comment: "所属门派ID（0表示通用）"),
                    profession_limit = table.Column<string>(type: "varchar(50)", nullable: false, comment: "职业限制（逗号分隔的职业ID）"),
                    max_level = table.Column<int>(type: "int", nullable: false, comment: "最大等级"),
                    learn_level_require = table.Column<int>(type: "int", nullable: false, comment: "学习等级需求"),
                    learn_realm_require = table.Column<int>(type: "int", nullable: false, comment: "学习境界需求"),
                    pre_skill_id = table.Column<int>(type: "int", nullable: true, comment: "前置技能ID"),
                    pre_skill_level = table.Column<int>(type: "int", nullable: true, comment: "前置技能等级"),
                    learn_cost = table.Column<string>(type: "nvarchar(500)", nullable: false, comment: "学习消耗（JSON格式，包含货币、物品等）"),
                    comprehension_require = table.Column<int>(type: "int", nullable: false, comment: "悟性需求"),
                    energy_cost_formula = table.Column<string>(type: "varchar(200)", nullable: false, comment: "内力消耗公式"),
                    cooldown_formula = table.Column<string>(type: "varchar(200)", nullable: false, comment: "冷却时间公式（毫秒）"),
                    cast_time_formula = table.Column<string>(type: "varchar(200)", nullable: false, comment: "施法时间公式（毫秒）"),
                    attack_range = table.Column<double>(type: "float", nullable: false, comment: "攻击范围"),
                    target_type = table.Column<int>(type: "int", nullable: false, comment: "目标类型 0-自己 1-单体敌人 2-群体敌人 3-单体友方 4-群体友方 5-地面"),
                    effect_range = table.Column<double>(type: "float", nullable: false, comment: "效果范围（AOE技能）"),
                    max_targets = table.Column<int>(type: "int", nullable: false, comment: "最大目标数"),
                    skill_effects = table.Column<string>(type: "nvarchar(2000)", nullable: false, comment: "技能效果（JSON格式）"),
                    level_effects = table.Column<string>(type: "nvarchar(2000)", nullable: false, comment: "升级效果（JSON格式）"),
                    combo_skill_ids = table.Column<string>(type: "varchar(100)", nullable: false, comment: "连招技能ID（逗号分隔）"),
                    can_create = table.Column<bool>(type: "bit", nullable: false, comment: "是否可自创"),
                    can_advance = table.Column<bool>(type: "bit", nullable: false, comment: "是否可进阶"),
                    advance_skill_id = table.Column<int>(type: "int", nullable: true, comment: "进阶技能ID"),
                    icon_path = table.Column<string>(type: "varchar(200)", nullable: false, comment: "图标路径"),
                    effect_id = table.Column<int>(type: "int", nullable: false, comment: "特效ID"),
                    sound_id = table.Column<int>(type: "int", nullable: false, comment: "音效ID"),
                    create_time = table.Column<DateTime>(type: "datetime", nullable: false, comment: "创建时间"),
                    update_time = table.Column<DateTime>(type: "datetime", nullable: false, comment: "更新时间"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_SkillTemplate", x => x.skill_id);
                },
                comment: "技能模板表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_TradeLog",
                columns: table => new
                {
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    trade_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    seller_id = table.Column<long>(type: "bigint", nullable: false),
                    seller_name = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    buyer_id = table.Column<long>(type: "bigint", nullable: false),
                    buyer_name = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    trade_type = table.Column<int>(type: "int", nullable: false),
                    trade_items = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    currency_type = table.Column<int>(type: "int", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    tax = table.Column<long>(type: "bigint", nullable: false),
                    trade_time = table.Column<DateTime>(type: "datetime2", nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_TradeLog", x => x.trade_id);
                });

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_User",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false, comment: "用户ID")
                        .Annotation("SqlServer:Identity", "1, 1"),
                    account_name = table.Column<string>(type: "varchar(50)", nullable: false, comment: "账号名"),
                    password_hash = table.Column<string>(type: "varchar(256)", nullable: false, comment: "密码哈希"),
                    password_salt = table.Column<string>(type: "varchar(128)", nullable: false, comment: "密码盐"),
                    status = table.Column<int>(type: "int", nullable: false, comment: "账号状态 0-正常 1-冻结 2-封禁"),
                    activity_level = table.Column<int>(type: "int", nullable: false, comment: "活跃等级"),
                    activity_points = table.Column<long>(type: "bigint", nullable: false, comment: "活跃度积分"),
                    total_online_minutes = table.Column<int>(type: "int", nullable: false, comment: "累计在线时长（分钟）"),
                    consecutive_login_days = table.Column<int>(type: "int", nullable: false, comment: "连续登录天数"),
                    create_time = table.Column<DateTime>(type: "datetime", nullable: false, comment: "创建时间"),
                    last_login_time = table.Column<DateTime>(type: "datetime", nullable: true, comment: "最后登录时间"),
                    last_login_ip = table.Column<string>(type: "varchar(50)", nullable: false, comment: "最后登录IP"),
                    server_id = table.Column<int>(type: "int", nullable: false, comment: "服务器ID"),
                    platform_id = table.Column<string>(type: "varchar(50)", nullable: false, comment: "平台ID"),
                    device_id = table.Column<string>(type: "varchar(128)", nullable: false, comment: "设备ID"),
                    email = table.Column<string>(type: "varchar(100)", nullable: false, comment: "邮箱"),
                    phone = table.Column<string>(type: "varchar(20)", nullable: false, comment: "手机号"),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false, comment: "是否已删除"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    GameId = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    GameUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_User", x => x.user_id);
                },
                comment: "用户账号表");

            migrationBuilder.CreateTable(
                name: "Game_HunduShijie_Zone",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    game_id = table.Column<int>(type: "int", nullable: false, comment: "游戏Id"),
                    zone_name = table.Column<string>(type: "varchar(255)", nullable: false, comment: "分区名称"),
                    description = table.Column<string>(type: "varchar(500)", nullable: false, comment: "分区描述"),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "创建时间"),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "更新时间")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game_HunduShijie_Zone", x => x.Id);
                },
                comment: "游戏分区信息表");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Game_HunduShijie_Bag");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_Character");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_CharacterActivity");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_CharacterAttribute");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_CharacterSkill");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_CharacterTitle");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_ChatBlacklist");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_ChatChannelSetting");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_ChatMessage");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_ChatPrivateMessage");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_Currency");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_Game");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_Guild");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_Item");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_ItemAttribute");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_ItemGem");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_ItemTemplate");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_Material");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_MaterialSynthesisLog");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_Server");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_SetItem");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_SkillAdvancePath");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_SkillBook");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_SkillCustomCreate");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_SkillTemplate");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_TradeLog");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_User");

            migrationBuilder.DropTable(
                name: "Game_HunduShijie_Zone");
        }
    }
}
