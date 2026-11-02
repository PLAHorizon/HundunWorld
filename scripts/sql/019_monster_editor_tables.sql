-- 本文件由迁移 20261025123917_AddMonsterEditorTables 生成；改迁移请同步改这里（两处是两份清单）。
BEGIN TRANSACTION;
-- 数字人那五列归角色那条线，本库没有角色表时**大声跳过**而不是让批处理在第 4 行中止：
-- 实测全新库上单看 COL_LENGTH 会抛 Msg 4902「找不到对象」⇒ 整批回滚 ⇒ 8 张怪物表一张都不建。
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

CREATE TABLE [Game_HunduShijie_MonsterBehavior] (
    [monster_id] int NOT NULL,
    [ai_profile] tinyint NOT NULL,
    [flag_use_threat] tinyint NULL,
    [flag_use_skills] tinyint NULL,
    [flag_use_navmesh] tinyint NULL,
    [flag_use_assist] tinyint NULL,
    [flag_use_tactics] tinyint NULL,
    [is_aggressive] tinyint NOT NULL,
    [aggro_radius] float NOT NULL,
    [aggro_cone_degrees] float NOT NULL,
    [leash_radius] float NOT NULL,
    [evade_immunity_seconds] float NOT NULL,
    [assist_radius] float NOT NULL,
    [detect_mask] int NOT NULL,
    [wander_radius] float NOT NULL,
    [idle_to_patrol_seconds] float NOT NULL,
    [walk_speed] float NOT NULL,
    [move_speed] float NOT NULL,
    [follow_type] tinyint NOT NULL,
    [melee_swing_seconds] float NOT NULL,
    [ranged_min_distance] float NOT NULL,
    [ranged_max_distance] float NOT NULL,
    [flee_hp_percent] int NOT NULL,
    [patrol_route_id] int NOT NULL,
    [IsValid] bit NOT NULL,
    [GameId] int NOT NULL,
    [AreaId] int NOT NULL,
    [ServerId] int NOT NULL,
    [GameUserId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_Game_HunduShijie_MonsterBehavior] PRIMARY KEY ([monster_id])
);
DECLARE @defaultSchema AS sysname;
SET @defaultSchema = SCHEMA_NAME();
DECLARE @description AS sql_variant;
SET @description = N'怪物行为表（与模板 1:1）';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_MonsterBehavior';
SET @description = N'怪物编号';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_MonsterBehavior', 'COLUMN', N'monster_id';
SET @description = N'AI 档位 0笨1战2精3王';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_MonsterBehavior', 'COLUMN', N'ai_profile';
SET @description = N'威胁表覆盖 NULL跟档位';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_MonsterBehavior', 'COLUMN', N'flag_use_threat';
SET @description = N'技能覆盖 NULL跟档位';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_MonsterBehavior', 'COLUMN', N'flag_use_skills';
SET @description = N'导航图覆盖 NULL跟档位';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_MonsterBehavior', 'COLUMN', N'flag_use_navmesh';
SET @description = N'求援覆盖 NULL跟档位';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_MonsterBehavior', 'COLUMN', N'flag_use_assist';
SET @description = N'战术覆盖 NULL跟档位';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_MonsterBehavior', 'COLUMN', N'flag_use_tactics';
SET @description = N'是否主动索敌';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_MonsterBehavior', 'COLUMN', N'is_aggressive';
SET @description = N'索敌半径（米）';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_MonsterBehavior', 'COLUMN', N'aggro_radius';
SET @description = N'索敌朝向锥（度）';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_MonsterBehavior', 'COLUMN', N'aggro_cone_degrees';
SET @description = N'脱战半径（米）';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_MonsterBehavior', 'COLUMN', N'leash_radius';
SET @description = N'回位免疫新仇恨秒';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_MonsterBehavior', 'COLUMN', N'evade_immunity_seconds';
SET @description = N'同族求援半径（米）';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_MonsterBehavior', 'COLUMN', N'assist_radius';
SET @description = N'检测类型位掩码';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_MonsterBehavior', 'COLUMN', N'detect_mask';
SET @description = N'无路线时踱步半径（米）';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_MonsterBehavior', 'COLUMN', N'wander_radius';
SET @description = N'待机转巡逻秒';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_MonsterBehavior', 'COLUMN', N'idle_to_patrol_seconds';
SET @description = N'巡逻速度（米/秒）';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_MonsterBehavior', 'COLUMN', N'walk_speed';
SET @description = N'追击速度（米/秒）';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_MonsterBehavior', 'COLUMN', N'move_speed';
SET @description = N'移动方式 0步行1直线2飞行3站桩';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_MonsterBehavior', 'COLUMN', N'follow_type';
SET @description = N'近战挥击间隔秒';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_MonsterBehavior', 'COLUMN', N'melee_swing_seconds';
SET @description = N'远程保持距离下界（米）';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_MonsterBehavior', 'COLUMN', N'ranged_min_distance';
SET @description = N'远程保持距离上界（米）';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_MonsterBehavior', 'COLUMN', N'ranged_max_distance';
SET @description = N'残血逃跑阈值百分比';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_MonsterBehavior', 'COLUMN', N'flee_hp_percent';
SET @description = N'巡逻路线 id';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_MonsterBehavior', 'COLUMN', N'patrol_route_id';

CREATE TABLE [Game_HunduShijie_MonsterDropEntry] (
    [entry_id] int NOT NULL IDENTITY,
    [binding_id] int NOT NULL,
    [item_id] int NOT NULL,
    [chance_bps] int NOT NULL,
    [count_min] int NOT NULL,
    [count_max] int NOT NULL,
    [seq] int NOT NULL,
    [group_id] int NOT NULL,
    [condition_kind] int NOT NULL,
    [quest_id] int NOT NULL,
    [IsValid] bit NOT NULL,
    [GameId] int NOT NULL,
    [AreaId] int NOT NULL,
    [ServerId] int NOT NULL,
    [GameUserId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_Game_HunduShijie_MonsterDropEntry] PRIMARY KEY ([entry_id])
);
DECLARE @defaultSchema1 AS sysname;
SET @defaultSchema1 = SCHEMA_NAME();
DECLARE @description1 AS sql_variant;
SET @description1 = N'怪物逐件掉落表';
EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', @defaultSchema1, 'TABLE', N'Game_HunduShijie_MonsterDropEntry';
SET @description1 = N'条目 id（自增）';
EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', @defaultSchema1, 'TABLE', N'Game_HunduShijie_MonsterDropEntry', 'COLUMN', N'entry_id';
SET @description1 = N'所属绑定';
EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', @defaultSchema1, 'TABLE', N'Game_HunduShijie_MonsterDropEntry', 'COLUMN', N'binding_id';
SET @description1 = N'物品模板 id';
EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', @defaultSchema1, 'TABLE', N'Game_HunduShijie_MonsterDropEntry', 'COLUMN', N'item_id';
SET @description1 = N'该件独立概率基点';
EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', @defaultSchema1, 'TABLE', N'Game_HunduShijie_MonsterDropEntry', 'COLUMN', N'chance_bps';
SET @description1 = N'数量下界';
EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', @defaultSchema1, 'TABLE', N'Game_HunduShijie_MonsterDropEntry', 'COLUMN', N'count_min';
SET @description1 = N'数量上界';
EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', @defaultSchema1, 'TABLE', N'Game_HunduShijie_MonsterDropEntry', 'COLUMN', N'count_max';
SET @description1 = N'掷骰顺序';
EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', @defaultSchema1, 'TABLE', N'Game_HunduShijie_MonsterDropEntry', 'COLUMN', N'seq';
SET @description1 = N'掉落组号（0=逐件独立掷，>0=同组互斥抽一件）';
EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', @defaultSchema1, 'TABLE', N'Game_HunduShijie_MonsterDropEntry', 'COLUMN', N'group_id';
SET @description1 = N'逐件掉落条件（-1=继承绑定，0=无条件，1=任务持有者，2=组队，3=稀有度）';
EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', @defaultSchema1, 'TABLE', N'Game_HunduShijie_MonsterDropEntry', 'COLUMN', N'condition_kind';
SET @description1 = N'逐件任务条件 id（0=用绑定上的）';
EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', @defaultSchema1, 'TABLE', N'Game_HunduShijie_MonsterDropEntry', 'COLUMN', N'quest_id';

CREATE TABLE [Game_HunduShijie_MonsterLootBinding] (
    [binding_id] int NOT NULL IDENTITY,
    [monster_id] int NOT NULL,
    [loot_table_id] int NOT NULL,
    [roll_chance_bps] int NOT NULL,
    [min_count] int NOT NULL,
    [max_count] int NOT NULL,
    [condition_kind] tinyint NOT NULL,
    [quest_id] int NOT NULL,
    [money_min] int NOT NULL,
    [money_max] int NOT NULL,
    [IsValid] bit NOT NULL,
    [GameId] int NOT NULL,
    [AreaId] int NOT NULL,
    [ServerId] int NOT NULL,
    [GameUserId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_Game_HunduShijie_MonsterLootBinding] PRIMARY KEY ([binding_id])
);
DECLARE @defaultSchema2 AS sysname;
SET @defaultSchema2 = SCHEMA_NAME();
DECLARE @description2 AS sql_variant;
SET @description2 = N'怪物掉落表绑定表';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_MonsterLootBinding';
SET @description2 = N'绑定 id（自增）';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_MonsterLootBinding', 'COLUMN', N'binding_id';
SET @description2 = N'怪物编号';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_MonsterLootBinding', 'COLUMN', N'monster_id';
SET @description2 = N'掉落表 id';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_MonsterLootBinding', 'COLUMN', N'loot_table_id';
SET @description2 = N'本表触发概率基点（0=必触）';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_MonsterLootBinding', 'COLUMN', N'roll_chance_bps';
SET @description2 = N'抽取件数下界';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_MonsterLootBinding', 'COLUMN', N'min_count';
SET @description2 = N'抽取件数上界';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_MonsterLootBinding', 'COLUMN', N'max_count';
SET @description2 = N'条件类型';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_MonsterLootBinding', 'COLUMN', N'condition_kind';
SET @description2 = N'条件任务 id';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_MonsterLootBinding', 'COLUMN', N'quest_id';
SET @description2 = N'金币下界';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_MonsterLootBinding', 'COLUMN', N'money_min';
SET @description2 = N'金币上界';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_MonsterLootBinding', 'COLUMN', N'money_max';

CREATE TABLE [Game_HunduShijie_MonsterPatrolPoint] (
    [point_id] int NOT NULL IDENTITY,
    [route_id] int NOT NULL,
    [seq] int NOT NULL,
    [x] float NOT NULL,
    [y] float NOT NULL,
    [z] float NOT NULL,
    [pause_seconds] float NULL,
    [IsValid] bit NOT NULL,
    [GameId] int NOT NULL,
    [AreaId] int NOT NULL,
    [ServerId] int NOT NULL,
    [GameUserId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_Game_HunduShijie_MonsterPatrolPoint] PRIMARY KEY ([point_id])
);
DECLARE @defaultSchema3 AS sysname;
SET @defaultSchema3 = SCHEMA_NAME();
DECLARE @description3 AS sql_variant;
SET @description3 = N'怪物巡逻点表';
EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_MonsterPatrolPoint';
SET @description3 = N'点 id（自增）';
EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_MonsterPatrolPoint', 'COLUMN', N'point_id';
SET @description3 = N'所属路线';
EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_MonsterPatrolPoint', 'COLUMN', N'route_id';
SET @description3 = N'点顺序';
EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_MonsterPatrolPoint', 'COLUMN', N'seq';
SET @description3 = N'世界坐标 X';
EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_MonsterPatrolPoint', 'COLUMN', N'x';
SET @description3 = N'世界坐标 Y（水平面第二轴）';
EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_MonsterPatrolPoint', 'COLUMN', N'y';
SET @description3 = N'世界坐标 Z（高度）';
EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_MonsterPatrolPoint', 'COLUMN', N'z';
SET @description3 = N'该点停留秒（NULL=用默认）';
EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_MonsterPatrolPoint', 'COLUMN', N'pause_seconds';

CREATE TABLE [Game_HunduShijie_MonsterPatrolRoute] (
    [route_id] int NOT NULL,
    [name] nvarchar(64) NOT NULL,
    [mode] tinyint NOT NULL,
    [pause_at_point_seconds] float NOT NULL,
    [use_navmesh] tinyint NOT NULL,
    [point_count] int NOT NULL,
    [publish_state] tinyint NOT NULL,
    [IsValid] bit NOT NULL,
    [GameId] int NOT NULL,
    [AreaId] int NOT NULL,
    [ServerId] int NOT NULL,
    [GameUserId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_Game_HunduShijie_MonsterPatrolRoute] PRIMARY KEY ([route_id])
);
DECLARE @defaultSchema4 AS sysname;
SET @defaultSchema4 = SCHEMA_NAME();
DECLARE @description4 AS sql_variant;
SET @description4 = N'怪物巡逻路线表';
EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', @defaultSchema4, 'TABLE', N'Game_HunduShijie_MonsterPatrolRoute';
SET @description4 = N'路线 id（自增）';
EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', @defaultSchema4, 'TABLE', N'Game_HunduShijie_MonsterPatrolRoute', 'COLUMN', N'route_id';
SET @description4 = N'路线名';
EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', @defaultSchema4, 'TABLE', N'Game_HunduShijie_MonsterPatrolRoute', 'COLUMN', N'name';
SET @description4 = N'0往返 1循环 2随机';
EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', @defaultSchema4, 'TABLE', N'Game_HunduShijie_MonsterPatrolRoute', 'COLUMN', N'mode';
SET @description4 = N'每点默认停留秒';
EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', @defaultSchema4, 'TABLE', N'Game_HunduShijie_MonsterPatrolRoute', 'COLUMN', N'pause_at_point_seconds';
SET @description4 = N'是否走图连段';
EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', @defaultSchema4, 'TABLE', N'Game_HunduShijie_MonsterPatrolRoute', 'COLUMN', N'use_navmesh';
SET @description4 = N'点数（由 CLI 统计写入）';
EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', @defaultSchema4, 'TABLE', N'Game_HunduShijie_MonsterPatrolRoute', 'COLUMN', N'point_count';
SET @description4 = N'0草稿 1已发布 2停用';
EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', @defaultSchema4, 'TABLE', N'Game_HunduShijie_MonsterPatrolRoute', 'COLUMN', N'publish_state';

CREATE TABLE [Game_HunduShijie_MonsterSkillMount] (
    [mount_id] int NOT NULL IDENTITY,
    [monster_id] int NOT NULL,
    [skill_id] int NOT NULL,
    [slot_seq] int NOT NULL,
    [slot_type] tinyint NOT NULL,
    [chance_bps] int NOT NULL,
    [cast_ms] int NOT NULL,
    [cooldown_ms] int NOT NULL,
    [min_range] float NOT NULL,
    [max_range] float NOT NULL,
    [target_select] tinyint NOT NULL,
    [target_count] int NOT NULL,
    [aoe_radius] float NOT NULL,
    [school_mask] int NOT NULL,
    [interruptible] tinyint NOT NULL,
    [allow_movement] tinyint NOT NULL,
    [damage_scale_bps] int NOT NULL,
    [status_id] int NOT NULL,
    [status_duration_seconds] float NOT NULL,
    [start_emote] int NOT NULL,
    [emergency_hp_percent] int NOT NULL,
    [IsValid] bit NOT NULL,
    [GameId] int NOT NULL,
    [AreaId] int NOT NULL,
    [ServerId] int NOT NULL,
    [GameUserId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_Game_HunduShijie_MonsterSkillMount] PRIMARY KEY ([mount_id])
);
DECLARE @defaultSchema5 AS sysname;
SET @defaultSchema5 = SCHEMA_NAME();
DECLARE @description5 AS sql_variant;
SET @description5 = N'怪物技能挂载表';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_MonsterSkillMount';
SET @description5 = N'挂载 id（自增）';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_MonsterSkillMount', 'COLUMN', N'mount_id';
SET @description5 = N'怪物编号';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_MonsterSkillMount', 'COLUMN', N'monster_id';
SET @description5 = N'技能 id';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_MonsterSkillMount', 'COLUMN', N'skill_id';
SET @description5 = N'遍历顺序';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_MonsterSkillMount', 'COLUMN', N'slot_seq';
SET @description5 = N'时机 0战斗1非战斗2残血3受控';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_MonsterSkillMount', 'COLUMN', N'slot_type';
SET @description5 = N'释放概率基点 0..10000';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_MonsterSkillMount', 'COLUMN', N'chance_bps';
SET @description5 = N'吟唱时长毫秒';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_MonsterSkillMount', 'COLUMN', N'cast_ms';
SET @description5 = N'冷却毫秒';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_MonsterSkillMount', 'COLUMN', N'cooldown_ms';
SET @description5 = N'最小射程（米）';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_MonsterSkillMount', 'COLUMN', N'min_range';
SET @description5 = N'最大射程（米）';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_MonsterSkillMount', 'COLUMN', N'max_range';
SET @description5 = N'选敌方式';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_MonsterSkillMount', 'COLUMN', N'target_select';
SET @description5 = N'最大目标数';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_MonsterSkillMount', 'COLUMN', N'target_count';
SET @description5 = N'AOE 半径（米）';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_MonsterSkillMount', 'COLUMN', N'aoe_radius';
SET @description5 = N'系别位掩码';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_MonsterSkillMount', 'COLUMN', N'school_mask';
SET @description5 = N'是否可打断';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_MonsterSkillMount', 'COLUMN', N'interruptible';
SET @description5 = N'是否允许移动施法';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_MonsterSkillMount', 'COLUMN', N'allow_movement';
SET @description5 = N'伤害倍率基点';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_MonsterSkillMount', 'COLUMN', N'damage_scale_bps';
SET @description5 = N'附加状态 id';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_MonsterSkillMount', 'COLUMN', N'status_id';
SET @description5 = N'状态持续秒';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_MonsterSkillMount', 'COLUMN', N'status_duration_seconds';
SET @description5 = N'起手喊话/表情 id';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_MonsterSkillMount', 'COLUMN', N'start_emote';
SET @description5 = N'残血阈值百分比';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_MonsterSkillMount', 'COLUMN', N'emergency_hp_percent';

CREATE TABLE [Game_HunduShijie_MonsterSpawnPoint] (
    [spawn_point_id] int NOT NULL,
    [zone_id] int NOT NULL,
    [shard_id] bigint NOT NULL,
    [x] float NOT NULL,
    [y] float NOT NULL,
    [z] float NOT NULL,
    [yaw] float NOT NULL,
    [monster_id] int NOT NULL,
    [level_override] int NULL,
    [max_alive] int NOT NULL,
    [slot_spacing] float NOT NULL,
    [respawn_override_seconds] float NOT NULL,
    [route_override_id] int NOT NULL,
    [enabled] tinyint NOT NULL,
    [publish_state] tinyint NOT NULL,
    [content_version] int NOT NULL,
    [IsValid] bit NOT NULL,
    [GameId] int NOT NULL,
    [AreaId] int NOT NULL,
    [ServerId] int NOT NULL,
    [GameUserId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_Game_HunduShijie_MonsterSpawnPoint] PRIMARY KEY ([spawn_point_id])
);
DECLARE @defaultSchema6 AS sysname;
SET @defaultSchema6 = SCHEMA_NAME();
DECLARE @description6 AS sql_variant;
SET @description6 = N'怪物刷怪点表';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_MonsterSpawnPoint';
SET @description6 = N'刷怪点 id';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_MonsterSpawnPoint', 'COLUMN', N'spawn_point_id';
SET @description6 = N'区域 id';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_MonsterSpawnPoint', 'COLUMN', N'zone_id';
SET @description6 = N'分片 id';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_MonsterSpawnPoint', 'COLUMN', N'shard_id';
SET @description6 = N'中心 X';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_MonsterSpawnPoint', 'COLUMN', N'x';
SET @description6 = N'中心 Y';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_MonsterSpawnPoint', 'COLUMN', N'y';
SET @description6 = N'中心 Z';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_MonsterSpawnPoint', 'COLUMN', N'z';
SET @description6 = N'初始朝向（弧度）';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_MonsterSpawnPoint', 'COLUMN', N'yaw';
SET @description6 = N'怪物编号（必须已发布）';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_MonsterSpawnPoint', 'COLUMN', N'monster_id';
SET @description6 = N'等级覆盖（NULL=模板基准）';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_MonsterSpawnPoint', 'COLUMN', N'level_override';
SET @description6 = N'同时存活上限（=槽位数）';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_MonsterSpawnPoint', 'COLUMN', N'max_alive';
SET @description6 = N'槽位横向间距（米）';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_MonsterSpawnPoint', 'COLUMN', N'slot_spacing';
SET @description6 = N'刷新秒覆盖（0=用公式）';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_MonsterSpawnPoint', 'COLUMN', N'respawn_override_seconds';
SET @description6 = N'路线覆盖（0=用行为表）';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_MonsterSpawnPoint', 'COLUMN', N'route_override_id';
SET @description6 = N'是否启用';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_MonsterSpawnPoint', 'COLUMN', N'enabled';
SET @description6 = N'0草稿 1已发布 2停用';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_MonsterSpawnPoint', 'COLUMN', N'publish_state';
SET @description6 = N'内容版本';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_MonsterSpawnPoint', 'COLUMN', N'content_version';

CREATE TABLE [Game_HunduShijie_MonsterTemplate] (
    [monster_id] int NOT NULL,
    [name] nvarchar(64) NOT NULL,
    [rank] tinyint NOT NULL,
    [species] tinyint NOT NULL,
    [wuxing_domain] int NOT NULL,
    [model_key] nvarchar(64) NOT NULL,
    [prefab_guid] nvarchar(64) NULL,
    [model_scale] decimal(4,2) NOT NULL,
    [stand_state] tinyint NOT NULL,
    [level_base] int NOT NULL,
    [level_min] int NOT NULL,
    [level_max] int NOT NULL,
    [hp_base] int NOT NULL,
    [hp_per_level] int NOT NULL,
    [atk_base] int NOT NULL,
    [atk_per_level] int NOT NULL,
    [def_base] int NOT NULL,
    [def_per_level] int NOT NULL,
    [hp_override_bps] int NOT NULL,
    [atk_override_bps] int NOT NULL,
    [xp_base] int NOT NULL,
    [xp_per_level] int NOT NULL,
    [xp_mod_bps] int NOT NULL,
    [health_mod_bps] int NOT NULL,
    [respawn_base_seconds] float NOT NULL,
    [respawn_per_level_seconds] float NOT NULL,
    [corpse_seconds] float NOT NULL,
    [aggro_say_id] int NOT NULL,
    [publish_state] tinyint NOT NULL,
    [content_version] int NOT NULL,
    [remark] nvarchar(500) NOT NULL,
    [prefab_path] nvarchar(256) NULL,
    [IsValid] bit NOT NULL,
    [GameId] int NOT NULL,
    [AreaId] int NOT NULL,
    [ServerId] int NOT NULL,
    [GameUserId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_Game_HunduShijie_MonsterTemplate] PRIMARY KEY ([monster_id])
);
DECLARE @defaultSchema7 AS sysname;
SET @defaultSchema7 = SCHEMA_NAME();
DECLARE @description7 AS sql_variant;
SET @description7 = N'怪物模板表（怪物编辑器主表）';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_MonsterTemplate';
SET @description7 = N'怪物编号（号段即身份）';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_MonsterTemplate', 'COLUMN', N'monster_id';
SET @description7 = N'显示名';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_MonsterTemplate', 'COLUMN', N'name';
SET @description7 = N'rank 0普通 1精英 2稀有精英 3首领 4世界BOSS';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_MonsterTemplate', 'COLUMN', N'rank';
SET @description7 = N'种族编号';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_MonsterTemplate', 'COLUMN', N'species';
SET @description7 = N'五行域 1金2木3水4火5土 0=未定';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_MonsterTemplate', 'COLUMN', N'wuxing_domain';
SET @description7 = N'客户端模型键';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_MonsterTemplate', 'COLUMN', N'model_key';
SET @description7 = N'预制体资产 Guid（可空）';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_MonsterTemplate', 'COLUMN', N'prefab_guid';
SET @description7 = N'体型缩放';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_MonsterTemplate', 'COLUMN', N'model_scale';
SET @description7 = N'待机姿态 0站1坐2卧3潜水';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_MonsterTemplate', 'COLUMN', N'stand_state';
SET @description7 = N'基准等级';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_MonsterTemplate', 'COLUMN', N'level_base';
SET @description7 = N'等级下界';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_MonsterTemplate', 'COLUMN', N'level_min';
SET @description7 = N'等级上界';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_MonsterTemplate', 'COLUMN', N'level_max';
SET @description7 = N'生命公式截距';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_MonsterTemplate', 'COLUMN', N'hp_base';
SET @description7 = N'生命公式每级增量';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_MonsterTemplate', 'COLUMN', N'hp_per_level';
SET @description7 = N'攻击公式截距';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_MonsterTemplate', 'COLUMN', N'atk_base';
SET @description7 = N'攻击公式每级增量';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_MonsterTemplate', 'COLUMN', N'atk_per_level';
SET @description7 = N'防御公式截距';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_MonsterTemplate', 'COLUMN', N'def_base';
SET @description7 = N'防御公式每级增量';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_MonsterTemplate', 'COLUMN', N'def_per_level';
SET @description7 = N'血量覆盖基点 0=不覆盖';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_MonsterTemplate', 'COLUMN', N'hp_override_bps';
SET @description7 = N'攻击覆盖基点 0=不覆盖';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_MonsterTemplate', 'COLUMN', N'atk_override_bps';
SET @description7 = N'经验基准截距';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_MonsterTemplate', 'COLUMN', N'xp_base';
SET @description7 = N'经验每级增量';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_MonsterTemplate', 'COLUMN', N'xp_per_level';
SET @description7 = N'逐模板经验倍率基点（10000=×1）';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_MonsterTemplate', 'COLUMN', N'xp_mod_bps';
SET @description7 = N'血薄削减经验基点（只有 0..7500 生效，10000=不削减）';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_MonsterTemplate', 'COLUMN', N'health_mod_bps';
SET @description7 = N'刷新秒截距';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_MonsterTemplate', 'COLUMN', N'respawn_base_seconds';
SET @description7 = N'刷新秒每级增量';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_MonsterTemplate', 'COLUMN', N'respawn_per_level_seconds';
SET @description7 = N'尸体停留秒';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_MonsterTemplate', 'COLUMN', N'corpse_seconds';
SET @description7 = N'索敌喊话 id';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_MonsterTemplate', 'COLUMN', N'aggro_say_id';
SET @description7 = N'发布状态 0草稿 1已发布 2已停用';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_MonsterTemplate', 'COLUMN', N'publish_state';
SET @description7 = N'内容版本';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_MonsterTemplate', 'COLUMN', N'content_version';
SET @description7 = N'策划备注';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_MonsterTemplate', 'COLUMN', N'remark';
SET @description7 = N'预制体内容路径（客户端按路径解析 GUID）';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_MonsterTemplate', 'COLUMN', N'prefab_path';

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261025123917_AddMonsterEditorTables', N'10.0.2');

COMMIT;
GO

