BEGIN TRANSACTION;
CREATE TABLE [Game_HunduShijie_NpcCraftRecipe] (
    [IsValid] bit NOT NULL,
    [recipe_id] int NOT NULL IDENTITY,
    [name] nvarchar(64) NOT NULL,
    [product_item_id] int NOT NULL,
    [materials_json] nvarchar(max) NOT NULL,
    [cost_gold] bigint NOT NULL,
    [station_npc_id] int NOT NULL,
    [skill_req] int NOT NULL,
    [quality_gap_caps_json] nvarchar(128) NOT NULL,
    [quality_chance_bps_json] nvarchar(128) NOT NULL,
    [learn_source_kind] tinyint NOT NULL,
    [publish_state] tinyint NOT NULL,
    [content_version] int NOT NULL,
    [GameId] int NOT NULL,
    [AreaId] int NOT NULL,
    [ServerId] int NOT NULL,
    [GameUserId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_Game_HunduShijie_NpcCraftRecipe] PRIMARY KEY ([recipe_id])
);
DECLARE @defaultSchema AS sysname;
SET @defaultSchema = SCHEMA_NAME();
DECLARE @description AS sql_variant;
SET @description = N'制作配方表';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_NpcCraftRecipe';
SET @description = N'配方编号';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_NpcCraftRecipe', 'COLUMN', N'recipe_id';
SET @description = N'配方名';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_NpcCraftRecipe', 'COLUMN', N'name';
SET @description = N'产物物品编号';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_NpcCraftRecipe', 'COLUMN', N'product_item_id';
SET @description = N'材料清单 JSON';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_NpcCraftRecipe', 'COLUMN', N'materials_json';
SET @description = N'制作工费';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_NpcCraftRecipe', 'COLUMN', N'cost_gold';
SET @description = N'设施 NPC 编号，≤0=随处';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_NpcCraftRecipe', 'COLUMN', N'station_npc_id';
SET @description = N'要求技能点数';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_NpcCraftRecipe', 'COLUMN', N'skill_req';
SET @description = N'成功档技能差上限 JSON';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_NpcCraftRecipe', 'COLUMN', N'quality_gap_caps_json';
SET @description = N'四档成功率 JSON（万分比）';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_NpcCraftRecipe', 'COLUMN', N'quality_chance_bps_json';
SET @description = N'学习来源 1训练师 2书 3任务 4掉落';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_NpcCraftRecipe', 'COLUMN', N'learn_source_kind';
SET @description = N'发布状态';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_NpcCraftRecipe', 'COLUMN', N'publish_state';
SET @description = N'内容版本';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_NpcCraftRecipe', 'COLUMN', N'content_version';

CREATE TABLE [Game_HunduShijie_NpcGossipOption] (
    [IsValid] bit NOT NULL,
    [option_id] int NOT NULL IDENTITY,
    [npc_id] int NOT NULL,
    [group_id] int NOT NULL,
    [seq] int NOT NULL,
    [text_id] int NOT NULL,
    [action_kind] tinyint NOT NULL,
    [action_arg] int NOT NULL,
    [req_min_level] int NOT NULL,
    [req_max_level] int NOT NULL,
    [enabled] tinyint NOT NULL,
    [GameId] int NOT NULL,
    [AreaId] int NOT NULL,
    [ServerId] int NOT NULL,
    [GameUserId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_Game_HunduShijie_NpcGossipOption] PRIMARY KEY ([option_id])
);
DECLARE @defaultSchema1 AS sysname;
SET @defaultSchema1 = SCHEMA_NAME();
DECLARE @description1 AS sql_variant;
SET @description1 = N'NPC 对话选项表';
EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', @defaultSchema1, 'TABLE', N'Game_HunduShijie_NpcGossipOption';
SET @description1 = N'选项编号';
EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', @defaultSchema1, 'TABLE', N'Game_HunduShijie_NpcGossipOption', 'COLUMN', N'option_id';
SET @description1 = N'NPC 编号';
EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', @defaultSchema1, 'TABLE', N'Game_HunduShijie_NpcGossipOption', 'COLUMN', N'npc_id';
SET @description1 = N'台词组';
EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', @defaultSchema1, 'TABLE', N'Game_HunduShijie_NpcGossipOption', 'COLUMN', N'group_id';
SET @description1 = N'窗口排序';
EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', @defaultSchema1, 'TABLE', N'Game_HunduShijie_NpcGossipOption', 'COLUMN', N'seq';
SET @description1 = N'文本编号';
EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', @defaultSchema1, 'TABLE', N'Game_HunduShijie_NpcGossipOption', 'COLUMN', N'text_id';
SET @description1 = N'动作种类';
EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', @defaultSchema1, 'TABLE', N'Game_HunduShijie_NpcGossipOption', 'COLUMN', N'action_kind';
SET @description1 = N'动作参数';
EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', @defaultSchema1, 'TABLE', N'Game_HunduShijie_NpcGossipOption', 'COLUMN', N'action_arg';
SET @description1 = N'可见等级下限';
EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', @defaultSchema1, 'TABLE', N'Game_HunduShijie_NpcGossipOption', 'COLUMN', N'req_min_level';
SET @description1 = N'可见等级上限，0=不限';
EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', @defaultSchema1, 'TABLE', N'Game_HunduShijie_NpcGossipOption', 'COLUMN', N'req_max_level';
SET @description1 = N'是否启用';
EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', @defaultSchema1, 'TABLE', N'Game_HunduShijie_NpcGossipOption', 'COLUMN', N'enabled';

CREATE TABLE [Game_HunduShijie_NpcQuestLink] (
    [IsValid] bit NOT NULL,
    [link_id] int NOT NULL IDENTITY,
    [npc_id] int NOT NULL,
    [quest_id] int NOT NULL,
    [role] tinyint NOT NULL,
    [order_seq] int NOT NULL,
    [GameId] int NOT NULL,
    [AreaId] int NOT NULL,
    [ServerId] int NOT NULL,
    [GameUserId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_Game_HunduShijie_NpcQuestLink] PRIMARY KEY ([link_id])
);
DECLARE @defaultSchema2 AS sysname;
SET @defaultSchema2 = SCHEMA_NAME();
DECLARE @description2 AS sql_variant;
SET @description2 = N'任务与 NPC 的发布/交付关系表';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_NpcQuestLink';
SET @description2 = N'关系编号';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_NpcQuestLink', 'COLUMN', N'link_id';
SET @description2 = N'NPC 编号';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_NpcQuestLink', 'COLUMN', N'npc_id';
SET @description2 = N'任务编号';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_NpcQuestLink', 'COLUMN', N'quest_id';
SET @description2 = N'角色 1发布 2交付';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_NpcQuestLink', 'COLUMN', N'role';
SET @description2 = N'窗口排序';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_NpcQuestLink', 'COLUMN', N'order_seq';

CREATE TABLE [Game_HunduShijie_NpcRecipeTaught] (
    [IsValid] bit NOT NULL,
    [link_id] int NOT NULL IDENTITY,
    [recipe_id] int NOT NULL,
    [npc_id] int NOT NULL,
    [cost_gold] bigint NOT NULL,
    [enabled] tinyint NOT NULL,
    [GameId] int NOT NULL,
    [AreaId] int NOT NULL,
    [ServerId] int NOT NULL,
    [GameUserId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_Game_HunduShijie_NpcRecipeTaught] PRIMARY KEY ([link_id])
);
DECLARE @defaultSchema3 AS sysname;
SET @defaultSchema3 = SCHEMA_NAME();
DECLARE @description3 AS sql_variant;
SET @description3 = N'配方 ↔ NPC 教学关系表';
EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_NpcRecipeTaught';
SET @description3 = N'关系编号';
EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_NpcRecipeTaught', 'COLUMN', N'link_id';
SET @description3 = N'配方编号';
EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_NpcRecipeTaught', 'COLUMN', N'recipe_id';
SET @description3 = N'NPC 编号';
EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_NpcRecipeTaught', 'COLUMN', N'npc_id';
SET @description3 = N'学费';
EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_NpcRecipeTaught', 'COLUMN', N'cost_gold';
SET @description3 = N'是否启用';
EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_NpcRecipeTaught', 'COLUMN', N'enabled';

CREATE TABLE [Game_HunduShijie_NpcScheduleSlot] (
    [IsValid] bit NOT NULL,
    [slot_id] int NOT NULL IDENTITY,
    [npc_id] int NOT NULL,
    [seq] int NOT NULL,
    [from_seconds] int NOT NULL,
    [to_seconds] int NOT NULL,
    [route_id] int NOT NULL,
    [posture] tinyint NOT NULL,
    [gossip_group_id] int NOT NULL,
    [GameId] int NOT NULL,
    [AreaId] int NOT NULL,
    [ServerId] int NOT NULL,
    [GameUserId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_Game_HunduShijie_NpcScheduleSlot] PRIMARY KEY ([slot_id])
);
DECLARE @defaultSchema4 AS sysname;
SET @defaultSchema4 = SCHEMA_NAME();
DECLARE @description4 AS sql_variant;
SET @description4 = N'NPC 作息表';
EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', @defaultSchema4, 'TABLE', N'Game_HunduShijie_NpcScheduleSlot';
SET @description4 = N'档编号';
EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', @defaultSchema4, 'TABLE', N'Game_HunduShijie_NpcScheduleSlot', 'COLUMN', N'slot_id';
SET @description4 = N'NPC 编号';
EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', @defaultSchema4, 'TABLE', N'Game_HunduShijie_NpcScheduleSlot', 'COLUMN', N'npc_id';
SET @description4 = N'同刻冲突时的优先序（小者优先）';
EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', @defaultSchema4, 'TABLE', N'Game_HunduShijie_NpcScheduleSlot', 'COLUMN', N'seq';
SET @description4 = N'起（含），周期内秒';
EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', @defaultSchema4, 'TABLE', N'Game_HunduShijie_NpcScheduleSlot', 'COLUMN', N'from_seconds';
SET @description4 = N'止（不含），周期内秒；等于起点=全天';
EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', @defaultSchema4, 'TABLE', N'Game_HunduShijie_NpcScheduleSlot', 'COLUMN', N'to_seconds';
SET @description4 = N'路线，≤0=不覆盖';
EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', @defaultSchema4, 'TABLE', N'Game_HunduShijie_NpcScheduleSlot', 'COLUMN', N'route_id';
SET @description4 = N'姿态';
EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', @defaultSchema4, 'TABLE', N'Game_HunduShijie_NpcScheduleSlot', 'COLUMN', N'posture';
SET @description4 = N'台词组覆盖，≤0=用模板';
EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', @defaultSchema4, 'TABLE', N'Game_HunduShijie_NpcScheduleSlot', 'COLUMN', N'gossip_group_id';

CREATE TABLE [Game_HunduShijie_NpcSpawnPoint] (
    [IsValid] bit NOT NULL,
    [spawn_point_id] int NOT NULL IDENTITY,
    [zone_id] int NOT NULL,
    [shard_id] bigint NOT NULL,
    [x] float NOT NULL,
    [y] float NOT NULL,
    [z] float NOT NULL,
    [yaw] float NOT NULL,
    [npc_id] int NOT NULL,
    [level_override] int NOT NULL,
    [max_alive] int NOT NULL,
    [respawn_seconds] float NOT NULL,
    [route_id] int NOT NULL,
    [facing_mode] tinyint NOT NULL,
    [enabled] tinyint NOT NULL,
    [publish_state] tinyint NOT NULL,
    [content_version] int NOT NULL,
    [GameId] int NOT NULL,
    [AreaId] int NOT NULL,
    [ServerId] int NOT NULL,
    [GameUserId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_Game_HunduShijie_NpcSpawnPoint] PRIMARY KEY ([spawn_point_id])
);
DECLARE @defaultSchema5 AS sysname;
SET @defaultSchema5 = SCHEMA_NAME();
DECLARE @description5 AS sql_variant;
SET @description5 = N'NPC 刷点表';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_NpcSpawnPoint';
SET @description5 = N'刷点编号';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_NpcSpawnPoint', 'COLUMN', N'spawn_point_id';
SET @description5 = N'区域编号';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_NpcSpawnPoint', 'COLUMN', N'zone_id';
SET @description5 = N'分片编号';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_NpcSpawnPoint', 'COLUMN', N'shard_id';
SET @description5 = N'X';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_NpcSpawnPoint', 'COLUMN', N'x';
SET @description5 = N'Y';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_NpcSpawnPoint', 'COLUMN', N'y';
SET @description5 = N'Z';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_NpcSpawnPoint', 'COLUMN', N'z';
SET @description5 = N'朝向（度）';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_NpcSpawnPoint', 'COLUMN', N'yaw';
SET @description5 = N'NPC 编号';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_NpcSpawnPoint', 'COLUMN', N'npc_id';
SET @description5 = N'等级覆盖，0=用模板';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_NpcSpawnPoint', 'COLUMN', N'level_override';
SET @description5 = N'同时存在数量';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_NpcSpawnPoint', 'COLUMN', N'max_alive';
SET @description5 = N'刷新秒，0=用默认';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_NpcSpawnPoint', 'COLUMN', N'respawn_seconds';
SET @description5 = N'路线覆盖，≤0=不覆盖';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_NpcSpawnPoint', 'COLUMN', N'route_id';
SET @description5 = N'朝向模式';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_NpcSpawnPoint', 'COLUMN', N'facing_mode';
SET @description5 = N'是否启用';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_NpcSpawnPoint', 'COLUMN', N'enabled';
SET @description5 = N'发布状态';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_NpcSpawnPoint', 'COLUMN', N'publish_state';
SET @description5 = N'内容版本';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_NpcSpawnPoint', 'COLUMN', N'content_version';

CREATE TABLE [Game_HunduShijie_NpcTemplate] (
    [npc_id] int NOT NULL,
    [name] nvarchar(64) NOT NULL,
    [title] nvarchar(64) NOT NULL,
    [model_key] nvarchar(64) NOT NULL,
    [prefab_guid] nvarchar(64) NOT NULL,
    [scale] float NOT NULL,
    [level] int NOT NULL,
    [level_variance] int NOT NULL,
    [reaction] tinyint NOT NULL,
    [faction_group] int NOT NULL,
    [wuxing_domain] int NOT NULL,
    [service_flags] int NOT NULL,
    [ai_tier] tinyint NOT NULL,
    [ai_bits_force_on] int NOT NULL,
    [ai_bits_force_off] int NOT NULL,
    [attackable_override] int NOT NULL,
    [speed_walk] float NOT NULL,
    [speed_run] float NOT NULL,
    [alert_radius_meters] int NOT NULL,
    [gossip_group_id] int NOT NULL,
    [rank] tinyint NOT NULL,
    [kill_credit_npc_id] int NOT NULL,
    [publish_state] tinyint NOT NULL,
    [content_version] int NOT NULL,
    [IsValid] bit NOT NULL,
    [GameId] int NOT NULL,
    [AreaId] int NOT NULL,
    [ServerId] int NOT NULL,
    [GameUserId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_Game_HunduShijie_NpcTemplate] PRIMARY KEY ([npc_id])
);
DECLARE @defaultSchema6 AS sysname;
SET @defaultSchema6 = SCHEMA_NAME();
DECLARE @description6 AS sql_variant;
SET @description6 = N'NPC 模板表（NPC 编辑器主表）';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_NpcTemplate';
SET @description6 = N'NPC 编号（号段即身份）';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_NpcTemplate', 'COLUMN', N'npc_id';
SET @description6 = N'显示名';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_NpcTemplate', 'COLUMN', N'name';
SET @description6 = N'头顶头衔';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_NpcTemplate', 'COLUMN', N'title';
SET @description6 = N'客户端模型键';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_NpcTemplate', 'COLUMN', N'model_key';
SET @description6 = N'prefab GUID（空=未配美术，客户端显式回退）';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_NpcTemplate', 'COLUMN', N'prefab_guid';
SET @description6 = N'体型倍数';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_NpcTemplate', 'COLUMN', N'scale';
SET @description6 = N'基础等级';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_NpcTemplate', 'COLUMN', N'level';
SET @description6 = N'等级浮动';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_NpcTemplate', 'COLUMN', N'level_variance';
SET @description6 = N'立场 0友好 1中立 2敌对';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_NpcTemplate', 'COLUMN', N'reaction';
SET @description6 = N'阵营组（暂无权威玩家阵营，未参与判据）';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_NpcTemplate', 'COLUMN', N'faction_group';
SET @description6 = N'五行域 1金2木3水4火5土 0=未定';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_NpcTemplate', 'COLUMN', N'wuxing_domain';
SET @description6 = N'服务能力位图（NpcServiceBits）';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_NpcTemplate', 'COLUMN', N'service_flags';
SET @description6 = N'AI 档位 0-7';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_NpcTemplate', 'COLUMN', N'ai_tier';
SET @description6 = N'能力位强制开';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_NpcTemplate', 'COLUMN', N'ai_bits_force_on';
SET @description6 = N'能力位强制关';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_NpcTemplate', 'COLUMN', N'ai_bits_force_off';
SET @description6 = N'可攻击覆盖 -1跟立场 0强制不可 1强制可';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_NpcTemplate', 'COLUMN', N'attackable_override';
SET @description6 = N'步行速度（米/秒）';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_NpcTemplate', 'COLUMN', N'speed_walk';
SET @description6 = N'奔跑速度（米/秒）';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_NpcTemplate', 'COLUMN', N'speed_run';
SET @description6 = N'呼叫增援半径（米）';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_NpcTemplate', 'COLUMN', N'alert_radius_meters';
SET @description6 = N'台词组 id';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_NpcTemplate', 'COLUMN', N'gossip_group_id';
SET @description6 = N'rank 0普通 1精英 2稀有精英 3首领 4世界BOSS';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_NpcTemplate', 'COLUMN', N'rank';
SET @description6 = N'击杀归属转移到的 NPC 编号，0=不转移';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_NpcTemplate', 'COLUMN', N'kill_credit_npc_id';
SET @description6 = N'发布状态 0草稿 1已发布 2已退役';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_NpcTemplate', 'COLUMN', N'publish_state';
SET @description6 = N'内容版本（乐观锁）';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_NpcTemplate', 'COLUMN', N'content_version';

CREATE TABLE [Game_HunduShijie_NpcTrainerEntry] (
    [IsValid] bit NOT NULL,
    [entry_id] int NOT NULL IDENTITY,
    [npc_id] int NOT NULL,
    [skill_id] int NOT NULL,
    [service_kind] tinyint NOT NULL,
    [cost_gold] bigint NOT NULL,
    [cost_item_id] int NOT NULL,
    [cost_item_count] int NOT NULL,
    [seq] int NOT NULL,
    [enabled] tinyint NOT NULL,
    [GameId] int NOT NULL,
    [AreaId] int NOT NULL,
    [ServerId] int NOT NULL,
    [GameUserId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_Game_HunduShijie_NpcTrainerEntry] PRIMARY KEY ([entry_id])
);
DECLARE @defaultSchema7 AS sysname;
SET @defaultSchema7 = SCHEMA_NAME();
DECLARE @description7 AS sql_variant;
SET @description7 = N'NPC 教学行表（技能与配方）';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_NpcTrainerEntry';
SET @description7 = N'行编号';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_NpcTrainerEntry', 'COLUMN', N'entry_id';
SET @description7 = N'NPC 编号';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_NpcTrainerEntry', 'COLUMN', N'npc_id';
SET @description7 = N'技能或配方编号';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_NpcTrainerEntry', 'COLUMN', N'skill_id';
SET @description7 = N'服务种类 1技能 2配方';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_NpcTrainerEntry', 'COLUMN', N'service_kind';
SET @description7 = N'学费（金币）';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_NpcTrainerEntry', 'COLUMN', N'cost_gold';
SET @description7 = N'材料物品编号，≤0=不要';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_NpcTrainerEntry', 'COLUMN', N'cost_item_id';
SET @description7 = N'材料件数';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_NpcTrainerEntry', 'COLUMN', N'cost_item_count';
SET @description7 = N'窗口排序';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_NpcTrainerEntry', 'COLUMN', N'seq';
SET @description7 = N'是否启用';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_NpcTrainerEntry', 'COLUMN', N'enabled';

CREATE TABLE [Game_HunduShijie_NpcVendorEntry] (
    [IsValid] bit NOT NULL,
    [entry_id] int NOT NULL IDENTITY,
    [npc_id] int NOT NULL,
    [slot] int NOT NULL,
    [item_id] int NOT NULL,
    [buy_price_override] bigint NOT NULL,
    [sell_price_override] bigint NOT NULL,
    [stock_max] int NOT NULL,
    [restock_seconds] int NOT NULL,
    [category] int NOT NULL,
    [enabled] tinyint NOT NULL,
    [GameId] int NOT NULL,
    [AreaId] int NOT NULL,
    [ServerId] int NOT NULL,
    [GameUserId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_Game_HunduShijie_NpcVendorEntry] PRIMARY KEY ([entry_id])
);
DECLARE @defaultSchema8 AS sysname;
SET @defaultSchema8 = SCHEMA_NAME();
DECLARE @description8 AS sql_variant;
SET @description8 = N'NPC 商人货架表';
EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_NpcVendorEntry';
SET @description8 = N'行编号';
EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_NpcVendorEntry', 'COLUMN', N'entry_id';
SET @description8 = N'NPC 编号';
EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_NpcVendorEntry', 'COLUMN', N'npc_id';
SET @description8 = N'槽位';
EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_NpcVendorEntry', 'COLUMN', N'slot';
SET @description8 = N'物品编号';
EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_NpcVendorEntry', 'COLUMN', N'item_id';
SET @description8 = N'买入价覆盖，≤0=用模板';
EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_NpcVendorEntry', 'COLUMN', N'buy_price_override';
SET @description8 = N'收购价覆盖，≤0=用模板';
EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_NpcVendorEntry', 'COLUMN', N'sell_price_override';
SET @description8 = N'库存上限，0=不限';
EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_NpcVendorEntry', 'COLUMN', N'stock_max';
SET @description8 = N'补货秒';
EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_NpcVendorEntry', 'COLUMN', N'restock_seconds';
SET @description8 = N'类别（仅显示分组）';
EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_NpcVendorEntry', 'COLUMN', N'category';
SET @description8 = N'是否启用';
EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_NpcVendorEntry', 'COLUMN', N'enabled';

CREATE TABLE [Game_HunduShijie_NpcWaypointNode] (
    [IsValid] bit NOT NULL,
    [node_id] int NOT NULL IDENTITY,
    [path_id] int NOT NULL,
    [seq] int NOT NULL,
    [x] float NOT NULL,
    [y] float NOT NULL,
    [z] float NOT NULL,
    [pause_seconds] float NOT NULL,
    [GameId] int NOT NULL,
    [AreaId] int NOT NULL,
    [ServerId] int NOT NULL,
    [GameUserId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_Game_HunduShijie_NpcWaypointNode] PRIMARY KEY ([node_id])
);
DECLARE @defaultSchema9 AS sysname;
SET @defaultSchema9 = SCHEMA_NAME();
DECLARE @description9 AS sql_variant;
SET @description9 = N'NPC 路线点表';
EXEC sp_addextendedproperty 'MS_Description', @description9, 'SCHEMA', @defaultSchema9, 'TABLE', N'Game_HunduShijie_NpcWaypointNode';
SET @description9 = N'点编号';
EXEC sp_addextendedproperty 'MS_Description', @description9, 'SCHEMA', @defaultSchema9, 'TABLE', N'Game_HunduShijie_NpcWaypointNode', 'COLUMN', N'node_id';
SET @description9 = N'所属路线';
EXEC sp_addextendedproperty 'MS_Description', @description9, 'SCHEMA', @defaultSchema9, 'TABLE', N'Game_HunduShijie_NpcWaypointNode', 'COLUMN', N'path_id';
SET @description9 = N'顺序';
EXEC sp_addextendedproperty 'MS_Description', @description9, 'SCHEMA', @defaultSchema9, 'TABLE', N'Game_HunduShijie_NpcWaypointNode', 'COLUMN', N'seq';
SET @description9 = N'X';
EXEC sp_addextendedproperty 'MS_Description', @description9, 'SCHEMA', @defaultSchema9, 'TABLE', N'Game_HunduShijie_NpcWaypointNode', 'COLUMN', N'x';
SET @description9 = N'Y';
EXEC sp_addextendedproperty 'MS_Description', @description9, 'SCHEMA', @defaultSchema9, 'TABLE', N'Game_HunduShijie_NpcWaypointNode', 'COLUMN', N'y';
SET @description9 = N'Z';
EXEC sp_addextendedproperty 'MS_Description', @description9, 'SCHEMA', @defaultSchema9, 'TABLE', N'Game_HunduShijie_NpcWaypointNode', 'COLUMN', N'z';
SET @description9 = N'本点停留秒（覆盖路线默认）';
EXEC sp_addextendedproperty 'MS_Description', @description9, 'SCHEMA', @defaultSchema9, 'TABLE', N'Game_HunduShijie_NpcWaypointNode', 'COLUMN', N'pause_seconds';

CREATE TABLE [Game_HunduShijie_NpcWaypointPath] (
    [IsValid] bit NOT NULL,
    [path_id] int NOT NULL IDENTITY,
    [name] nvarchar(64) NOT NULL,
    [mode] tinyint NOT NULL,
    [pause_at_point_seconds] float NOT NULL,
    [use_navmesh] tinyint NOT NULL,
    [point_count] int NOT NULL,
    [publish_state] tinyint NOT NULL,
    [GameId] int NOT NULL,
    [AreaId] int NOT NULL,
    [ServerId] int NOT NULL,
    [GameUserId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_Game_HunduShijie_NpcWaypointPath] PRIMARY KEY ([path_id])
);
DECLARE @defaultSchema10 AS sysname;
SET @defaultSchema10 = SCHEMA_NAME();
DECLARE @description10 AS sql_variant;
SET @description10 = N'NPC 路线表';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_NpcWaypointPath';
SET @description10 = N'路线编号';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_NpcWaypointPath', 'COLUMN', N'path_id';
SET @description10 = N'路线名';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_NpcWaypointPath', 'COLUMN', N'name';
SET @description10 = N'走法 0往返 1循环 2随机';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_NpcWaypointPath', 'COLUMN', N'mode';
SET @description10 = N'每点停留秒';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_NpcWaypointPath', 'COLUMN', N'pause_at_point_seconds';
SET @description10 = N'是否走图';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_NpcWaypointPath', 'COLUMN', N'use_navmesh';
SET @description10 = N'点数（冗余，供校验与进度显示）';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_NpcWaypointPath', 'COLUMN', N'point_count';
SET @description10 = N'发布状态';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_NpcWaypointPath', 'COLUMN', N'publish_state';

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261026160704_AddNpcEditorTables', N'10.0.2');

COMMIT;
GO

