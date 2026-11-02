-- ============================================================================
-- 021_pet_editor_tables.sql —— 宠物编辑器：物种模板九张 + 运行时个体两张
--
-- 本脚本**只生成、不执行**（迁移与写库由用户点头后再跑；本地 sqlcmd -E 可直连）。
-- 生成方式：python Tools/gen-pet-sql.py（逐列读自 Horizon.Model/GameModel/Pet*Entity.cs，
--           所以"实体有列而表没列"这类缺陷在生成时就不可能出现）。
--
-- 为什么编号列一律不用 IDENTITY：
--   species_id 住在 500000-799999 号段里，号段本身就是稀有度判据（PetIdentityCore.RarityTierOf）。
--   自增会让数据库发出 1/2/3 这种不合法号，而它不报错，只是让 rarity 判成 Unknown、
--   让个体编号算出来是 100001 而不是 50000100001。
--   pet_instance_id 同理：它是 物种×100000+序号，由内核分配、永不复用、放生也不回收。
--   只有关联表/流水表的代理主键（entry_id / event_id）用 IDENTITY。
--
-- 为什么这里十一张表而怪物侧八张：宠物多了"个体实例"与"个体事件"两张运行时表——
--   宠物被抓后永久存在，必须有独立于物种的身份行与可审计历史（设计文档 §2.3/§4）。
--   本期**只建表、不接 Orleans Grain**：读写通路见 docs/PET_EDITOR_PLUGIN_DESIGN.md §9。
--
-- 与本脚本配套：022_seed_pet_species.sql（种子逐字对齐 PetSpeciesCatalog.Rows）。
-- ============================================================================

BEGIN TRANSACTION;

CREATE TABLE [Game_HunduShijie_PetTemplate] (
    [species_id] INT NOT NULL,
    [name] NVARCHAR(64) NOT NULL,
    [model_key] NVARCHAR(64) NOT NULL,
    [prefab_path] NVARCHAR(256) NULL,
    [prefab_guid] NVARCHAR(64) NULL,
    [model_scale] DECIMAL(4,2) NOT NULL,
    [stand_state] NVARCHAR(32) NOT NULL,
    [base_color_key] NVARCHAR(32) NOT NULL,
    [carry_level_min] INT NOT NULL,
    [carry_level_max] INT NOT NULL,
    [allowed_origins] NVARCHAR(64) NOT NULL,
    [allowed_growth_tiers] NVARCHAR(32) NOT NULL,
    [hatch_seconds] INT NOT NULL,
    [hatch_level] INT NOT NULL,
    [owner_level_slack] INT NOT NULL,
    [potential_per_level] INT NOT NULL,
    [immortal_lifespan] BIT NOT NULL,
    [happiness_scale] INT NOT NULL,
    [capture_mode] TINYINT NOT NULL,
    [capture_base_bps] INT NOT NULL,
    [capture_guidance_seconds] INT NOT NULL,
    [mutation_chance_bps] INT NOT NULL,
    [publish_state] TINYINT NOT NULL,
    [content_version] INT NOT NULL,
    [remark] NVARCHAR(500) NOT NULL,
    [IsValid] BIT NOT NULL,
    [GameId] INT NOT NULL,
    [AreaId] INT NOT NULL,
    [ServerId] INT NOT NULL,
    [GameUserId] UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [PK_Game_HunduShijie_PetTemplate] PRIMARY KEY ([species_id])
);
DECLARE @defaultSchema AS sysname;
SET @defaultSchema = SCHEMA_NAME();
DECLARE @description AS sql_variant;
SET @description = N'宠物物种模板表（宠物编辑器主表）';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_PetTemplate';
SET @description = N'宠物编号（号段即身份）';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_PetTemplate', 'COLUMN', N'species_id';
SET @description = N'显示名';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_PetTemplate', 'COLUMN', N'name';
SET @description = N'客户端模型键';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_PetTemplate', 'COLUMN', N'model_key';
SET @description = N'预制体内容路径（客户端按路径解析 GUID）';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_PetTemplate', 'COLUMN', N'prefab_path';
SET @description = N'预制体资产 Guid（可空）';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_PetTemplate', 'COLUMN', N'prefab_guid';
SET @description = N'体型缩放';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_PetTemplate', 'COLUMN', N'model_scale';
SET @description = N'待机状态名';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_PetTemplate', 'COLUMN', N'stand_state';
SET @description = N'基色键';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_PetTemplate', 'COLUMN', N'base_color_key';
SET @description = N'可携带等级下界';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_PetTemplate', 'COLUMN', N'carry_level_min';
SET @description = N'可携带等级上界';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_PetTemplate', 'COLUMN', N'carry_level_max';
SET @description = N'允许出身 CSV';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_PetTemplate', 'COLUMN', N'allowed_origins';
SET @description = N'允许成长档 CSV';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_PetTemplate', 'COLUMN', N'allowed_growth_tiers';
SET @description = N'孵化秒数 0=捕捉即得';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_PetTemplate', 'COLUMN', N'hatch_seconds';
SET @description = N'出壳等级';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_PetTemplate', 'COLUMN', N'hatch_level';
SET @description = N'允许超出主人的级数';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_PetTemplate', 'COLUMN', N'owner_level_slack';
SET @description = N'每级潜能点';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_PetTemplate', 'COLUMN', N'potential_per_level';
SET @description = N'寿命无限（神兽）';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_PetTemplate', 'COLUMN', N'immortal_lifespan';
SET @description = N'快乐刻度 1000或100';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_PetTemplate', 'COLUMN', N'happiness_scale';
SET @description = N'捕捉方式 0瞬捕1残血2驯服3对战4不可捕';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_PetTemplate', 'COLUMN', N'capture_mode';
SET @description = N'捕捉基准概率 bps';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_PetTemplate', 'COLUMN', N'capture_base_bps';
SET @description = N'驯服引导秒';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_PetTemplate', 'COLUMN', N'capture_guidance_seconds';
SET @description = N'变异概率 bps';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_PetTemplate', 'COLUMN', N'mutation_chance_bps';
SET @description = N'发布状态 0草稿 1已发布 2已停用';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_PetTemplate', 'COLUMN', N'publish_state';
SET @description = N'内容版本';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_PetTemplate', 'COLUMN', N'content_version';
SET @description = N'策划备注';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_PetTemplate', 'COLUMN', N'remark';
SET @description = N'是否有效';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_PetTemplate', 'COLUMN', N'IsValid';
SET @description = N'游戏编号';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_PetTemplate', 'COLUMN', N'GameId';
SET @description = N'区域编号';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_PetTemplate', 'COLUMN', N'AreaId';
SET @description = N'服务器编号';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_PetTemplate', 'COLUMN', N'ServerId';
SET @description = N'归属用户';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_PetTemplate', 'COLUMN', N'GameUserId';

CREATE TABLE [Game_HunduShijie_PetStatBand] (
    [species_id] INT NOT NULL,
    [attack_min] INT NOT NULL,
    [attack_max] INT NOT NULL,
    [defense_min] INT NOT NULL,
    [defense_max] INT NOT NULL,
    [stamina_min] INT NOT NULL,
    [stamina_max] INT NOT NULL,
    [mana_min] INT NOT NULL,
    [mana_max] INT NOT NULL,
    [speed_min] INT NOT NULL,
    [speed_max] INT NOT NULL,
    [growth_min_bps] INT NOT NULL,
    [growth_max_bps] INT NOT NULL,
    [comprehension_max] INT NOT NULL,
    [spirit_max] INT NOT NULL,
    [super_spirit] BIT NOT NULL,
    [mutation_tier_max] INT NOT NULL,
    [IsValid] BIT NOT NULL,
    [GameId] INT NOT NULL,
    [AreaId] INT NOT NULL,
    [ServerId] INT NOT NULL,
    [GameUserId] UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [PK_Game_HunduShijie_PetStatBand] PRIMARY KEY ([species_id])
);
DECLARE @defaultSchema2 AS sysname;
SET @defaultSchema2 = SCHEMA_NAME();
DECLARE @description2 AS sql_variant;
SET @description2 = N'宠物资质区间表（与物种 1:1）';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_PetStatBand';
SET @description2 = N'物种编号';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_PetStatBand', 'COLUMN', N'species_id';
SET @description2 = N'攻击资质下界';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_PetStatBand', 'COLUMN', N'attack_min';
SET @description2 = N'攻击资质上界';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_PetStatBand', 'COLUMN', N'attack_max';
SET @description2 = N'防御资质下界';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_PetStatBand', 'COLUMN', N'defense_min';
SET @description2 = N'防御资质上界';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_PetStatBand', 'COLUMN', N'defense_max';
SET @description2 = N'体力资质下界';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_PetStatBand', 'COLUMN', N'stamina_min';
SET @description2 = N'体力资质上界';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_PetStatBand', 'COLUMN', N'stamina_max';
SET @description2 = N'法力资质下界';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_PetStatBand', 'COLUMN', N'mana_min';
SET @description2 = N'法力资质上界';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_PetStatBand', 'COLUMN', N'mana_max';
SET @description2 = N'速度资质下界';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_PetStatBand', 'COLUMN', N'speed_min';
SET @description2 = N'速度资质上界';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_PetStatBand', 'COLUMN', N'speed_max';
SET @description2 = N'成长下界 万分比';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_PetStatBand', 'COLUMN', N'growth_min_bps';
SET @description2 = N'成长上界 万分比';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_PetStatBand', 'COLUMN', N'growth_max_bps';
SET @description2 = N'悟性上限';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_PetStatBand', 'COLUMN', N'comprehension_max';
SET @description2 = N'灵性上限';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_PetStatBand', 'COLUMN', N'spirit_max';
SET @description2 = N'是否超灵档';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_PetStatBand', 'COLUMN', N'super_spirit';
SET @description2 = N'变异最高档';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_PetStatBand', 'COLUMN', N'mutation_tier_max';
SET @description2 = N'是否有效';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_PetStatBand', 'COLUMN', N'IsValid';
SET @description2 = N'游戏编号';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_PetStatBand', 'COLUMN', N'GameId';
SET @description2 = N'区域编号';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_PetStatBand', 'COLUMN', N'AreaId';
SET @description2 = N'服务器编号';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_PetStatBand', 'COLUMN', N'ServerId';
SET @description2 = N'归属用户';
EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_PetStatBand', 'COLUMN', N'GameUserId';

CREATE TABLE [Game_HunduShijie_PetSkillRule] (
    [species_id] INT NOT NULL,
    [base_slots] INT NOT NULL,
    [absolute_max_slots] INT NOT NULL,
    [slots_by_grade] NVARCHAR(64) NOT NULL,
    [comprehension_unlock_a] INT NOT NULL,
    [comprehension_unlock_b] INT NOT NULL,
    [slot_grow_max_level] INT NOT NULL,
    [slot_grow_max_existing] INT NOT NULL,
    [slot_grow_chance_bps] INT NOT NULL,
    [overwrite_chance_bps] INT NOT NULL,
    [max_lockable_slots] INT NOT NULL,
    [learned_pool_max] INT NOT NULL,
    [equipped_max] INT NOT NULL,
    [inner_core_slots] INT NOT NULL,
    [IsValid] BIT NOT NULL,
    [GameId] INT NOT NULL,
    [AreaId] INT NOT NULL,
    [ServerId] INT NOT NULL,
    [GameUserId] UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [PK_Game_HunduShijie_PetSkillRule] PRIMARY KEY ([species_id])
);
DECLARE @defaultSchema3 AS sysname;
SET @defaultSchema3 = SCHEMA_NAME();
DECLARE @description3 AS sql_variant;
SET @description3 = N'宠物技能槽位规则表（与物种 1:1）';
EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_PetSkillRule';
SET @description3 = N'物种编号';
EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_PetSkillRule', 'COLUMN', N'species_id';
SET @description3 = N'基础技能格数';
EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_PetSkillRule', 'COLUMN', N'base_slots';
SET @description3 = N'技能格绝对上限';
EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_PetSkillRule', 'COLUMN', N'absolute_max_slots';
SET @description3 = N'按品级格数 CSV（空=不分）';
EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_PetSkillRule', 'COLUMN', N'slots_by_grade';
SET @description3 = N'悟性开格甲';
EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_PetSkillRule', 'COLUMN', N'comprehension_unlock_a';
SET @description3 = N'悟性开格乙';
EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_PetSkillRule', 'COLUMN', N'comprehension_unlock_b';
SET @description3 = N'顶书开格等级门';
EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_PetSkillRule', 'COLUMN', N'slot_grow_max_level';
SET @description3 = N'顶书开格已有技能门';
EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_PetSkillRule', 'COLUMN', N'slot_grow_max_existing';
SET @description3 = N'顶书开格概率 bps';
EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_PetSkillRule', 'COLUMN', N'slot_grow_chance_bps';
SET @description3 = N'打书顶替概率 bps';
EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_PetSkillRule', 'COLUMN', N'overwrite_chance_bps';
SET @description3 = N'可锁定格数';
EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_PetSkillRule', 'COLUMN', N'max_lockable_slots';
SET @description3 = N'技能池上限';
EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_PetSkillRule', 'COLUMN', N'learned_pool_max';
SET @description3 = N'同时装配上限';
EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_PetSkillRule', 'COLUMN', N'equipped_max';
SET @description3 = N'内丹格数';
EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_PetSkillRule', 'COLUMN', N'inner_core_slots';
SET @description3 = N'是否有效';
EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_PetSkillRule', 'COLUMN', N'IsValid';
SET @description3 = N'游戏编号';
EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_PetSkillRule', 'COLUMN', N'GameId';
SET @description3 = N'区域编号';
EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_PetSkillRule', 'COLUMN', N'AreaId';
SET @description3 = N'服务器编号';
EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_PetSkillRule', 'COLUMN', N'ServerId';
SET @description3 = N'归属用户';
EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_PetSkillRule', 'COLUMN', N'GameUserId';

CREATE TABLE [Game_HunduShijie_PetSkillPool] (
    [entry_id] INT NOT NULL IDENTITY,
    [species_id] INT NOT NULL,
    [skill_id] INT NOT NULL,
    [kind] TINYINT NOT NULL,
    [innate_slot] INT NOT NULL,
    [min_level] INT NOT NULL,
    [conflicts] NVARCHAR(256) NOT NULL,
    [advanced] BIT NOT NULL,
    [seq] INT NOT NULL,
    [IsValid] BIT NOT NULL,
    [GameId] INT NOT NULL,
    [AreaId] INT NOT NULL,
    [ServerId] INT NOT NULL,
    [GameUserId] UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [PK_Game_HunduShijie_PetSkillPool] PRIMARY KEY ([entry_id])
);
DECLARE @defaultSchema4 AS sysname;
SET @defaultSchema4 = SCHEMA_NAME();
DECLARE @description4 AS sql_variant;
SET @description4 = N'宠物技能池表（物种 × 技能）';
EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', @defaultSchema4, 'TABLE', N'Game_HunduShijie_PetSkillPool';
SET @description4 = N'条目 id（自增）';
EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', @defaultSchema4, 'TABLE', N'Game_HunduShijie_PetSkillPool', 'COLUMN', N'entry_id';
SET @description4 = N'所属物种';
EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', @defaultSchema4, 'TABLE', N'Game_HunduShijie_PetSkillPool', 'COLUMN', N'species_id';
SET @description4 = N'技能编号';
EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', @defaultSchema4, 'TABLE', N'Game_HunduShijie_PetSkillPool', 'COLUMN', N'skill_id';
SET @description4 = N'0可学 1天生 2领悟候选';
EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', @defaultSchema4, 'TABLE', N'Game_HunduShijie_PetSkillPool', 'COLUMN', N'kind';
SET @description4 = N'天生技槽位（非天生为 -1）';
EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', @defaultSchema4, 'TABLE', N'Game_HunduShijie_PetSkillPool', 'COLUMN', N'innate_slot';
SET @description4 = N'最低学习等级';
EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', @defaultSchema4, 'TABLE', N'Game_HunduShijie_PetSkillPool', 'COLUMN', N'min_level';
SET @description4 = N'互斥技能 CSV';
EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', @defaultSchema4, 'TABLE', N'Game_HunduShijie_PetSkillPool', 'COLUMN', N'conflicts';
SET @description4 = N'是否高级版';
EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', @defaultSchema4, 'TABLE', N'Game_HunduShijie_PetSkillPool', 'COLUMN', N'advanced';
SET @description4 = N'条目顺序';
EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', @defaultSchema4, 'TABLE', N'Game_HunduShijie_PetSkillPool', 'COLUMN', N'seq';
SET @description4 = N'是否有效';
EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', @defaultSchema4, 'TABLE', N'Game_HunduShijie_PetSkillPool', 'COLUMN', N'IsValid';
SET @description4 = N'游戏编号';
EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', @defaultSchema4, 'TABLE', N'Game_HunduShijie_PetSkillPool', 'COLUMN', N'GameId';
SET @description4 = N'区域编号';
EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', @defaultSchema4, 'TABLE', N'Game_HunduShijie_PetSkillPool', 'COLUMN', N'AreaId';
SET @description4 = N'服务器编号';
EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', @defaultSchema4, 'TABLE', N'Game_HunduShijie_PetSkillPool', 'COLUMN', N'ServerId';
SET @description4 = N'归属用户';
EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', @defaultSchema4, 'TABLE', N'Game_HunduShijie_PetSkillPool', 'COLUMN', N'GameUserId';

CREATE TABLE [Game_HunduShijie_PetBehavior] (
    [species_id] INT NOT NULL,
    [intellect_tier] TINYINT NOT NULL,
    [flag_use_acquire] TINYINT NULL,
    [flag_use_autocast] TINYINT NULL,
    [flag_use_evade] TINYINT NULL,
    [flag_use_guard] TINYINT NULL,
    [flag_use_hold] TINYINT NULL,
    [default_stance] TINYINT NOT NULL,
    [default_command] TINYINT NOT NULL,
    [allowed_personalities] NVARCHAR(32) NOT NULL,
    [epiphany_chance_bps] INT NOT NULL,
    [follow_distance] FLOAT NOT NULL,
    [leash_radius] FLOAT NOT NULL,
    [teleport_beyond_radius] FLOAT NOT NULL,
    [repath_interval_ticks] INT NOT NULL,
    [follow_speed] FLOAT NOT NULL,
    [arrive_epsilon] FLOAT NOT NULL,
    [corpse_seconds] INT NOT NULL,
    [revive_cast_seconds] INT NOT NULL,
    [summon_cooldown_seconds] INT NOT NULL,
    [follow_start_delay_ms] INT NOT NULL,
    [initial_loyalty] INT NOT NULL,
    [initial_happiness] INT NOT NULL,
    [initial_lifespan] INT NOT NULL,
    [initial_intimacy] INT NOT NULL,
    [IsValid] BIT NOT NULL,
    [GameId] INT NOT NULL,
    [AreaId] INT NOT NULL,
    [ServerId] INT NOT NULL,
    [GameUserId] UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [PK_Game_HunduShijie_PetBehavior] PRIMARY KEY ([species_id])
);
DECLARE @defaultSchema5 AS sysname;
SET @defaultSchema5 = SCHEMA_NAME();
DECLARE @description5 AS sql_variant;
SET @description5 = N'宠物行为表（与物种 1:1）';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_PetBehavior';
SET @description5 = N'物种编号';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_PetBehavior', 'COLUMN', N'species_id';
SET @description5 = N'智能档位 0-4';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_PetBehavior', 'COLUMN', N'intellect_tier';
SET @description5 = N'自主索敌覆盖 NULL=跟档位';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_PetBehavior', 'COLUMN', N'flag_use_acquire';
SET @description5 = N'自动施法覆盖 NULL=跟档位';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_PetBehavior', 'COLUMN', N'flag_use_autocast';
SET @description5 = N'低血避让覆盖 NULL=跟档位';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_PetBehavior', 'COLUMN', N'flag_use_evade';
SET @description5 = N'护主覆盖 NULL=跟档位';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_PetBehavior', 'COLUMN', N'flag_use_guard';
SET @description5 = N'保留控制技覆盖 NULL=跟档位';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_PetBehavior', 'COLUMN', N'flag_use_hold';
SET @description5 = N'默认姿态 0-3';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_PetBehavior', 'COLUMN', N'default_stance';
SET @description5 = N'默认指令 0-4';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_PetBehavior', 'COLUMN', N'default_command';
SET @description5 = N'允许性格 CSV';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_PetBehavior', 'COLUMN', N'allowed_personalities';
SET @description5 = N'领悟概率 bps';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_PetBehavior', 'COLUMN', N'epiphany_chance_bps';
SET @description5 = N'理想跟随距离 米';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_PetBehavior', 'COLUMN', N'follow_distance';
SET @description5 = N'脱战 leash 半径 米';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_PetBehavior', 'COLUMN', N'leash_radius';
SET @description5 = N'超距传送半径 米';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_PetBehavior', 'COLUMN', N'teleport_beyond_radius';
SET @description5 = N'重算路径间隔 tick';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_PetBehavior', 'COLUMN', N'repath_interval_ticks';
SET @description5 = N'跟随移速 米每秒';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_PetBehavior', 'COLUMN', N'follow_speed';
SET @description5 = N'到点判定距离';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_PetBehavior', 'COLUMN', N'arrive_epsilon';
SET @description5 = N'尸体滞留秒 0=不留';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_PetBehavior', 'COLUMN', N'corpse_seconds';
SET @description5 = N'复活施法秒';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_PetBehavior', 'COLUMN', N'revive_cast_seconds';
SET @description5 = N'召唤收回冷却秒';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_PetBehavior', 'COLUMN', N'summon_cooldown_seconds';
SET @description5 = N'跟随起步延迟毫秒';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_PetBehavior', 'COLUMN', N'follow_start_delay_ms';
SET @description5 = N'初始忠诚 0-100';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_PetBehavior', 'COLUMN', N'initial_loyalty';
SET @description5 = N'初始快乐（刻度见物种表 happiness_scale）';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_PetBehavior', 'COLUMN', N'initial_happiness';
SET @description5 = N'初始寿命点数';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_PetBehavior', 'COLUMN', N'initial_lifespan';
SET @description5 = N'初始亲密度';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_PetBehavior', 'COLUMN', N'initial_intimacy';
SET @description5 = N'是否有效';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_PetBehavior', 'COLUMN', N'IsValid';
SET @description5 = N'游戏编号';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_PetBehavior', 'COLUMN', N'GameId';
SET @description5 = N'区域编号';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_PetBehavior', 'COLUMN', N'AreaId';
SET @description5 = N'服务器编号';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_PetBehavior', 'COLUMN', N'ServerId';
SET @description5 = N'归属用户';
EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_PetBehavior', 'COLUMN', N'GameUserId';

CREATE TABLE [Game_HunduShijie_PetEquipRule] (
    [species_id] INT NOT NULL,
    [allowed_slots] NVARCHAR(64) NOT NULL,
    [gem_allowed] BIT NOT NULL,
    [max_star] INT NOT NULL,
    [set_family] TINYINT NOT NULL,
    [appraisal_enabled] BIT NOT NULL,
    [awakening_enabled] BIT NOT NULL,
    [awakening_piece_count] INT NOT NULL,
    [ownership] TINYINT NOT NULL,
    [IsValid] BIT NOT NULL,
    [GameId] INT NOT NULL,
    [AreaId] INT NOT NULL,
    [ServerId] INT NOT NULL,
    [GameUserId] UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [PK_Game_HunduShijie_PetEquipRule] PRIMARY KEY ([species_id])
);
DECLARE @defaultSchema6 AS sysname;
SET @defaultSchema6 = SCHEMA_NAME();
DECLARE @description6 AS sql_variant;
SET @description6 = N'宠物装备规则表（与物种 1:1）';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_PetEquipRule';
SET @description6 = N'物种编号';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_PetEquipRule', 'COLUMN', N'species_id';
SET @description6 = N'开放部位 CSV 空=无装备';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_PetEquipRule', 'COLUMN', N'allowed_slots';
SET @description6 = N'是否允许灵石';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_PetEquipRule', 'COLUMN', N'gem_allowed';
SET @description6 = N'最高星级';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_PetEquipRule', 'COLUMN', N'max_star';
SET @description6 = N'套装族';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_PetEquipRule', 'COLUMN', N'set_family';
SET @description6 = N'是否启用鉴定';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_PetEquipRule', 'COLUMN', N'appraisal_enabled';
SET @description6 = N'是否启用觉醒格';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_PetEquipRule', 'COLUMN', N'awakening_enabled';
SET @description6 = N'觉醒所需件数';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_PetEquipRule', 'COLUMN', N'awakening_piece_count';
SET @description6 = N'装备归属 0绑宠1绑角色';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_PetEquipRule', 'COLUMN', N'ownership';
SET @description6 = N'是否有效';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_PetEquipRule', 'COLUMN', N'IsValid';
SET @description6 = N'游戏编号';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_PetEquipRule', 'COLUMN', N'GameId';
SET @description6 = N'区域编号';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_PetEquipRule', 'COLUMN', N'AreaId';
SET @description6 = N'服务器编号';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_PetEquipRule', 'COLUMN', N'ServerId';
SET @description6 = N'归属用户';
EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_PetEquipRule', 'COLUMN', N'GameUserId';

CREATE TABLE [Game_HunduShijie_PetAssistRule] (
    [entry_id] INT NOT NULL IDENTITY,
    [species_id] INT NOT NULL,
    [skill_id] INT NOT NULL,
    [assist_kind] TINYINT NOT NULL,
    [requires_battle] BIT NOT NULL,
    [possessable] BIT NOT NULL,
    [coefficient_bps] INT NOT NULL,
    [seq] INT NOT NULL,
    [IsValid] BIT NOT NULL,
    [GameId] INT NOT NULL,
    [AreaId] INT NOT NULL,
    [ServerId] INT NOT NULL,
    [GameUserId] UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [PK_Game_HunduShijie_PetAssistRule] PRIMARY KEY ([entry_id])
);
DECLARE @defaultSchema7 AS sysname;
SET @defaultSchema7 = SCHEMA_NAME();
DECLARE @description7 AS sql_variant;
SET @description7 = N'宠物辅助（反哺主人）规则表';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_PetAssistRule';
SET @description7 = N'条目 id（自增）';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_PetAssistRule', 'COLUMN', N'entry_id';
SET @description7 = N'所属物种';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_PetAssistRule', 'COLUMN', N'species_id';
SET @description7 = N'辅助技能编号';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_PetAssistRule', 'COLUMN', N'skill_id';
SET @description7 = N'辅助类型 0-9';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_PetAssistRule', 'COLUMN', N'assist_kind';
SET @description7 = N'是否出战才生效';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_PetAssistRule', 'COLUMN', N'requires_battle';
SET @description7 = N'是否可附体';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_PetAssistRule', 'COLUMN', N'possessable';
SET @description7 = N'系数覆盖 bps 0=默认';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_PetAssistRule', 'COLUMN', N'coefficient_bps';
SET @description7 = N'条目顺序';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_PetAssistRule', 'COLUMN', N'seq';
SET @description7 = N'是否有效';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_PetAssistRule', 'COLUMN', N'IsValid';
SET @description7 = N'游戏编号';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_PetAssistRule', 'COLUMN', N'GameId';
SET @description7 = N'区域编号';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_PetAssistRule', 'COLUMN', N'AreaId';
SET @description7 = N'服务器编号';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_PetAssistRule', 'COLUMN', N'ServerId';
SET @description7 = N'归属用户';
EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_PetAssistRule', 'COLUMN', N'GameUserId';

CREATE TABLE [Game_HunduShijie_PetMergeRule] (
    [species_id] INT NOT NULL,
    [fuse_enabled] BIT NOT NULL,
    [breed_enabled] BIT NOT NULL,
    [fuse_min_level] INT NOT NULL,
    [fuse_success_bps] INT NOT NULL,
    [fuse_quality_min_bps] INT NOT NULL,
    [fuse_quality_max_bps] INT NOT NULL,
    [fuse_growth_min_bps] INT NOT NULL,
    [fuse_growth_max_bps] INT NOT NULL,
    [fuse_max_inherited_skills] INT NOT NULL,
    [fuse_consumes_parents] BIT NOT NULL,
    [breed_max_level_gap] INT NOT NULL,
    [breed_required_happiness] INT NOT NULL,
    [breed_required_lifespan] INT NOT NULL,
    [breed_offspring_count] INT NOT NULL,
    [breed_claim_window_seconds] INT NOT NULL,
    [breed_cost] INT NOT NULL,
    [IsValid] BIT NOT NULL,
    [GameId] INT NOT NULL,
    [AreaId] INT NOT NULL,
    [ServerId] INT NOT NULL,
    [GameUserId] UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [PK_Game_HunduShijie_PetMergeRule] PRIMARY KEY ([species_id])
);
DECLARE @defaultSchema8 AS sysname;
SET @defaultSchema8 = SCHEMA_NAME();
DECLARE @description8 AS sql_variant;
SET @description8 = N'宠物合成/繁殖规则表（与物种 1:1）';
EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_PetMergeRule';
SET @description8 = N'物种编号';
EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_PetMergeRule', 'COLUMN', N'species_id';
SET @description8 = N'是否可炼妖';
EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_PetMergeRule', 'COLUMN', N'fuse_enabled';
SET @description8 = N'是否可繁殖';
EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_PetMergeRule', 'COLUMN', N'breed_enabled';
SET @description8 = N'炼妖最低等级';
EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_PetMergeRule', 'COLUMN', N'fuse_min_level';
SET @description8 = N'炼妖基准成功率 bps';
EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_PetMergeRule', 'COLUMN', N'fuse_success_bps';
SET @description8 = N'资质继承带下界 bps';
EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_PetMergeRule', 'COLUMN', N'fuse_quality_min_bps';
SET @description8 = N'资质继承带上界 bps';
EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_PetMergeRule', 'COLUMN', N'fuse_quality_max_bps';
SET @description8 = N'成长继承带下界 bps';
EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_PetMergeRule', 'COLUMN', N'fuse_growth_min_bps';
SET @description8 = N'成长继承带上界 bps';
EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_PetMergeRule', 'COLUMN', N'fuse_growth_max_bps';
SET @description8 = N'继承技能上限';
EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_PetMergeRule', 'COLUMN', N'fuse_max_inherited_skills';
SET @description8 = N'炼妖失败是否仍消耗材料';
EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_PetMergeRule', 'COLUMN', N'fuse_consumes_parents';
SET @description8 = N'繁殖等级差上限';
EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_PetMergeRule', 'COLUMN', N'breed_max_level_gap';
SET @description8 = N'繁殖要求快乐';
EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_PetMergeRule', 'COLUMN', N'breed_required_happiness';
SET @description8 = N'繁殖要求寿命';
EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_PetMergeRule', 'COLUMN', N'breed_required_lifespan';
SET @description8 = N'繁殖产仔数';
EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_PetMergeRule', 'COLUMN', N'breed_offspring_count';
SET @description8 = N'繁殖领取时限秒';
EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_PetMergeRule', 'COLUMN', N'breed_claim_window_seconds';
SET @description8 = N'繁殖货币门槛';
EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_PetMergeRule', 'COLUMN', N'breed_cost';
SET @description8 = N'是否有效';
EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_PetMergeRule', 'COLUMN', N'IsValid';
SET @description8 = N'游戏编号';
EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_PetMergeRule', 'COLUMN', N'GameId';
SET @description8 = N'区域编号';
EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_PetMergeRule', 'COLUMN', N'AreaId';
SET @description8 = N'服务器编号';
EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_PetMergeRule', 'COLUMN', N'ServerId';
SET @description8 = N'归属用户';
EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_PetMergeRule', 'COLUMN', N'GameUserId';

CREATE TABLE [Game_HunduShijie_PetMutationRule] (
    [entry_id] INT NOT NULL IDENTITY,
    [species_id] INT NOT NULL,
    [mutation_key] NVARCHAR(64) NOT NULL,
    [duration_ticks] INT NOT NULL,
    [quality_cap_bps] INT NOT NULL,
    [color_override] NVARCHAR(32) NULL,
    [extra_skill_slots] INT NOT NULL,
    [enabled] BIT NOT NULL,
    [IsValid] BIT NOT NULL,
    [GameId] INT NOT NULL,
    [AreaId] INT NOT NULL,
    [ServerId] INT NOT NULL,
    [GameUserId] UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [PK_Game_HunduShijie_PetMutationRule] PRIMARY KEY ([entry_id])
);
DECLARE @defaultSchema9 AS sysname;
SET @defaultSchema9 = SCHEMA_NAME();
DECLARE @description9 AS sql_variant;
SET @description9 = N'宠物临时变异模板表';
EXEC sp_addextendedproperty 'MS_Description', @description9, 'SCHEMA', @defaultSchema9, 'TABLE', N'Game_HunduShijie_PetMutationRule';
SET @description9 = N'条目 id（自增）';
EXEC sp_addextendedproperty 'MS_Description', @description9, 'SCHEMA', @defaultSchema9, 'TABLE', N'Game_HunduShijie_PetMutationRule', 'COLUMN', N'entry_id';
SET @description9 = N'所属物种（0=全物种通用）';
EXEC sp_addextendedproperty 'MS_Description', @description9, 'SCHEMA', @defaultSchema9, 'TABLE', N'Game_HunduShijie_PetMutationRule', 'COLUMN', N'species_id';
SET @description9 = N'临时变异键';
EXEC sp_addextendedproperty 'MS_Description', @description9, 'SCHEMA', @defaultSchema9, 'TABLE', N'Game_HunduShijie_PetMutationRule', 'COLUMN', N'mutation_key';
SET @description9 = N'持续 tick';
EXEC sp_addextendedproperty 'MS_Description', @description9, 'SCHEMA', @defaultSchema9, 'TABLE', N'Game_HunduShijie_PetMutationRule', 'COLUMN', N'duration_ticks';
SET @description9 = N'资质上限放大 bps';
EXEC sp_addextendedproperty 'MS_Description', @description9, 'SCHEMA', @defaultSchema9, 'TABLE', N'Game_HunduShijie_PetMutationRule', 'COLUMN', N'quality_cap_bps';
SET @description9 = N'覆盖色键';
EXEC sp_addextendedproperty 'MS_Description', @description9, 'SCHEMA', @defaultSchema9, 'TABLE', N'Game_HunduShijie_PetMutationRule', 'COLUMN', N'color_override';
SET @description9 = N'临时追加技能格';
EXEC sp_addextendedproperty 'MS_Description', @description9, 'SCHEMA', @defaultSchema9, 'TABLE', N'Game_HunduShijie_PetMutationRule', 'COLUMN', N'extra_skill_slots';
SET @description9 = N'是否启用';
EXEC sp_addextendedproperty 'MS_Description', @description9, 'SCHEMA', @defaultSchema9, 'TABLE', N'Game_HunduShijie_PetMutationRule', 'COLUMN', N'enabled';
SET @description9 = N'是否有效';
EXEC sp_addextendedproperty 'MS_Description', @description9, 'SCHEMA', @defaultSchema9, 'TABLE', N'Game_HunduShijie_PetMutationRule', 'COLUMN', N'IsValid';
SET @description9 = N'游戏编号';
EXEC sp_addextendedproperty 'MS_Description', @description9, 'SCHEMA', @defaultSchema9, 'TABLE', N'Game_HunduShijie_PetMutationRule', 'COLUMN', N'GameId';
SET @description9 = N'区域编号';
EXEC sp_addextendedproperty 'MS_Description', @description9, 'SCHEMA', @defaultSchema9, 'TABLE', N'Game_HunduShijie_PetMutationRule', 'COLUMN', N'AreaId';
SET @description9 = N'服务器编号';
EXEC sp_addextendedproperty 'MS_Description', @description9, 'SCHEMA', @defaultSchema9, 'TABLE', N'Game_HunduShijie_PetMutationRule', 'COLUMN', N'ServerId';
SET @description9 = N'归属用户';
EXEC sp_addextendedproperty 'MS_Description', @description9, 'SCHEMA', @defaultSchema9, 'TABLE', N'Game_HunduShijie_PetMutationRule', 'COLUMN', N'GameUserId';

CREATE TABLE [Game_HunduShijie_PetInstance] (
    [pet_instance_id] BIGINT NOT NULL,
    [species_id] INT NOT NULL,
    [owner_character_id] BIGINT NOT NULL,
    [nickname] NVARCHAR(64) NOT NULL,
    [generation] TINYINT NOT NULL,
    [origin] TINYINT NOT NULL,
    [state] TINYINT NOT NULL,
    [level] INT NOT NULL,
    [current_xp] BIGINT NOT NULL,
    [potential_points] INT NOT NULL,
    [alloc_strength] INT NOT NULL,
    [alloc_endurance] INT NOT NULL,
    [alloc_constitution] INT NOT NULL,
    [alloc_spirit] INT NOT NULL,
    [alloc_agility] INT NOT NULL,
    [attack_quality] INT NOT NULL,
    [defense_quality] INT NOT NULL,
    [stamina_quality] INT NOT NULL,
    [mana_quality] INT NOT NULL,
    [speed_quality] INT NOT NULL,
    [growth_bps] INT NOT NULL,
    [comprehension_level] INT NOT NULL,
    [spirit_level] INT NOT NULL,
    [fusion_level] INT NOT NULL,
    [loyalty] INT NOT NULL,
    [happiness] INT NOT NULL,
    [lifespan] INT NOT NULL,
    [intimacy] INT NOT NULL,
    [skill_slots_json] NVARCHAR(MAX) NOT NULL,
    [equipment_json] NVARCHAR(MAX) NOT NULL,
    [mutation_tier] INT NOT NULL,
    [mutation_color_key] NVARCHAR(32) NULL,
    [timed_mutation_json] NVARCHAR(MAX) NULL,
    [personality] TINYINT NOT NULL,
    [intellect_tier_override] TINYINT NULL,
    [hatch_progress_seconds] INT NOT NULL,
    [breeding_count] INT NOT NULL,
    [spouse_instance_id] BIGINT NOT NULL,
    [claim_deadline_tick] BIGINT NOT NULL,
    [capture_source] NVARCHAR(128) NOT NULL,
    [captured_at] DATETIME2 NOT NULL,
    [duplicate_fingerprint] BIGINT NOT NULL,
    [content_version] INT NOT NULL,
    [remark] NVARCHAR(500) NOT NULL,
    [IsValid] BIT NOT NULL,
    [GameId] INT NOT NULL,
    [AreaId] INT NOT NULL,
    [ServerId] INT NOT NULL,
    [GameUserId] UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [PK_Game_HunduShijie_PetInstance] PRIMARY KEY ([pet_instance_id])
);
DECLARE @defaultSchema10 AS sysname;
SET @defaultSchema10 = SCHEMA_NAME();
DECLARE @description10 AS sql_variant;
SET @description10 = N'宠物个体实例表（运行时权威；本期未接入 Grain）';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance';
SET @description10 = N'个体编号（物种×100000+序号；永不复用）';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'pet_instance_id';
SET @description10 = N'物种编号';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'species_id';
SET @description10 = N'主人角色 id（Game_HunduShijie_Character.Id；宠物归属角色，不跨角色共享）';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'owner_character_id';
SET @description10 = N'昵称（仅展示）';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'nickname';
SET @description10 = N'形态阶段 0-3';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'generation';
SET @description10 = N'出身 0-5';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'origin';
SET @description10 = N'个体状态 0-9';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'state';
SET @description10 = N'等级';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'level';
SET @description10 = N'本级经验';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'current_xp';
SET @description10 = N'未分配潜能点';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'potential_points';
SET @description10 = N'力量已分配';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'alloc_strength';
SET @description10 = N'耐力已分配';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'alloc_endurance';
SET @description10 = N'体质已分配';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'alloc_constitution';
SET @description10 = N'灵性已分配';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'alloc_spirit';
SET @description10 = N'敏捷已分配';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'alloc_agility';
SET @description10 = N'攻击资质（裸资）';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'attack_quality';
SET @description10 = N'防御资质（裸资）';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'defense_quality';
SET @description10 = N'体力资质（裸资）';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'stamina_quality';
SET @description10 = N'法力资质（裸资）';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'mana_quality';
SET @description10 = N'速度资质（裸资）';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'speed_quality';
SET @description10 = N'成长率 万分比';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'growth_bps';
SET @description10 = N'悟性 0-10';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'comprehension_level';
SET @description10 = N'灵性 0-10';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'spirit_level';
SET @description10 = N'融合度 0-10';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'fusion_level';
SET @description10 = N'忠诚 0-100';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'loyalty';
SET @description10 = N'快乐（刻度见物种表 happiness_scale）';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'happiness';
SET @description10 = N'寿命点数';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'lifespan';
SET @description10 = N'亲密度';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'intimacy';
SET @description10 = N'技能槽 JSON';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'skill_slots_json';
SET @description10 = N'穿戴清单 JSON';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'equipment_json';
SET @description10 = N'永久变异档 0=无';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'mutation_tier';
SET @description10 = N'永久变异色键';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'mutation_color_key';
SET @description10 = N'临时变异 JSON（空=无）';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'timed_mutation_json';
SET @description10 = N'性格 0-4';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'personality';
SET @description10 = N'智能档位个体覆盖 NULL=用物种';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'intellect_tier_override';
SET @description10 = N'孵化进度秒';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'hatch_progress_seconds';
SET @description10 = N'已繁殖次数';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'breeding_count';
SET @description10 = N'配偶个体编号 0=无';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'spouse_instance_id';
SET @description10 = N'领取期限 tick 0=无';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'claim_deadline_tick';
SET @description10 = N'捕捉来源';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'capture_source';
SET @description10 = N'获得时间';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'captured_at';
SET @description10 = N'重复副本指纹';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'duplicate_fingerprint';
SET @description10 = N'内容版本（乐观锁）';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'content_version';
SET @description10 = N'备注';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'remark';
SET @description10 = N'是否有效';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'IsValid';
SET @description10 = N'游戏编号';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'GameId';
SET @description10 = N'区域编号';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'AreaId';
SET @description10 = N'服务器编号';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'ServerId';
SET @description10 = N'归属用户';
EXEC sp_addextendedproperty 'MS_Description', @description10, 'SCHEMA', @defaultSchema10, 'TABLE', N'Game_HunduShijie_PetInstance', 'COLUMN', N'GameUserId';

CREATE TABLE [Game_HunduShijie_PetInstanceEvent] (
    [event_id] INT NOT NULL IDENTITY,
    [pet_instance_id] BIGINT NOT NULL,
    [event_kind] TINYINT NOT NULL,
    [occurred_at] DATETIME2 NOT NULL,
    [payload_json] NVARCHAR(MAX) NOT NULL,
    [seed] BIGINT NOT NULL,
    [operator_user_id] UNIQUEIDENTIFIER NULL,
    [IsValid] BIT NOT NULL,
    [GameId] INT NOT NULL,
    [AreaId] INT NOT NULL,
    [ServerId] INT NOT NULL,
    [GameUserId] UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [PK_Game_HunduShijie_PetInstanceEvent] PRIMARY KEY ([event_id])
);
DECLARE @defaultSchema11 AS sysname;
SET @defaultSchema11 = SCHEMA_NAME();
DECLARE @description11 AS sql_variant;
SET @description11 = N'宠物个体事件流水表';
EXEC sp_addextendedproperty 'MS_Description', @description11, 'SCHEMA', @defaultSchema11, 'TABLE', N'Game_HunduShijie_PetInstanceEvent';
SET @description11 = N'事件 id（自增）';
EXEC sp_addextendedproperty 'MS_Description', @description11, 'SCHEMA', @defaultSchema11, 'TABLE', N'Game_HunduShijie_PetInstanceEvent', 'COLUMN', N'event_id';
SET @description11 = N'个体编号';
EXEC sp_addextendedproperty 'MS_Description', @description11, 'SCHEMA', @defaultSchema11, 'TABLE', N'Game_HunduShijie_PetInstanceEvent', 'COLUMN', N'pet_instance_id';
SET @description11 = N'事件类型 0-16';
EXEC sp_addextendedproperty 'MS_Description', @description11, 'SCHEMA', @defaultSchema11, 'TABLE', N'Game_HunduShijie_PetInstanceEvent', 'COLUMN', N'event_kind';
SET @description11 = N'发生时间';
EXEC sp_addextendedproperty 'MS_Description', @description11, 'SCHEMA', @defaultSchema11, 'TABLE', N'Game_HunduShijie_PetInstanceEvent', 'COLUMN', N'occurred_at';
SET @description11 = N'结果明细 JSON';
EXEC sp_addextendedproperty 'MS_Description', @description11, 'SCHEMA', @defaultSchema11, 'TABLE', N'Game_HunduShijie_PetInstanceEvent', 'COLUMN', N'payload_json';
SET @description11 = N'确定性种子';
EXEC sp_addextendedproperty 'MS_Description', @description11, 'SCHEMA', @defaultSchema11, 'TABLE', N'Game_HunduShijie_PetInstanceEvent', 'COLUMN', N'seed';
SET @description11 = N'操作者（GM 操作时与主人不同）';
EXEC sp_addextendedproperty 'MS_Description', @description11, 'SCHEMA', @defaultSchema11, 'TABLE', N'Game_HunduShijie_PetInstanceEvent', 'COLUMN', N'operator_user_id';
SET @description11 = N'是否有效';
EXEC sp_addextendedproperty 'MS_Description', @description11, 'SCHEMA', @defaultSchema11, 'TABLE', N'Game_HunduShijie_PetInstanceEvent', 'COLUMN', N'IsValid';
SET @description11 = N'游戏编号';
EXEC sp_addextendedproperty 'MS_Description', @description11, 'SCHEMA', @defaultSchema11, 'TABLE', N'Game_HunduShijie_PetInstanceEvent', 'COLUMN', N'GameId';
SET @description11 = N'区域编号';
EXEC sp_addextendedproperty 'MS_Description', @description11, 'SCHEMA', @defaultSchema11, 'TABLE', N'Game_HunduShijie_PetInstanceEvent', 'COLUMN', N'AreaId';
SET @description11 = N'服务器编号';
EXEC sp_addextendedproperty 'MS_Description', @description11, 'SCHEMA', @defaultSchema11, 'TABLE', N'Game_HunduShijie_PetInstanceEvent', 'COLUMN', N'ServerId';
SET @description11 = N'归属用户';
EXEC sp_addextendedproperty 'MS_Description', @description11, 'SCHEMA', @defaultSchema11, 'TABLE', N'Game_HunduShijie_PetInstanceEvent', 'COLUMN', N'GameUserId';

-- 注意：这里**没有** __EFMigrationsHistory 的 INSERT。
-- 本脚本是手写的等价 DDL，不是某条 EF 迁移的渲染产物。真正配对的迁移类要在 NPC 编辑器的
-- 迁移先落库之后再生成——现在跑 dotnet ef migrations add 会把**别人尚未迁移的 NPC 十二张表**
-- 一起卷进宠物这条迁移，变成一份跨所有者的混合迁移。执行本脚本时如需记账，请手工补一行。

COMMIT;
GO
