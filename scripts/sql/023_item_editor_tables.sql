-- ============================================================================
-- 023 物品编辑器：ItemTemplate 扩展列 + 11 张从表
-- 设计文档：docs/ITEM_EDITOR_PLUGIN_DESIGN.md §3
--
-- 约定（照抄 019 怪物编辑器踩过的两条）：
--   1. **幂等**：迁移/脚本在"已经手工加过列"的库上必须当无事发生，不能整批中止。
--   2. **先问对象在不在**：COL_LENGTH 对不存在的表返回错误（Msg 4902），
--      空库上会把整批事务带走。所以先 OBJECT_ID 判表。
--
-- 本脚本**只生成、不执行**：执行时机等用户点头（本仓库既有规矩）。
-- 回滚脚本：scripts/sql/revert_023_item_editor_tables.sql
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.Game_HunduShijie_ItemTemplate', N'U') IS NULL
BEGIN
    PRINT '跳过：本库还没有 Game_HunduShijie_ItemTemplate（先跑 006/009/015/016/018 种子脚本建表）。';
    RETURN;
END

BEGIN TRANSACTION;

-- ── 1. ItemTemplate 扩展列（全部 NOT NULL + 默认值，老数据零回填也能读）──
IF COL_LENGTH(N'dbo.Game_HunduShijie_ItemTemplate', N'inventory_type') IS NULL
    ALTER TABLE dbo.Game_HunduShijie_ItemTemplate ADD inventory_type int NOT NULL CONSTRAINT DF_ItemTemplate_inventory_type DEFAULT (0);
IF COL_LENGTH(N'dbo.Game_HunduShijie_ItemTemplate', N'item_level') IS NULL
    ALTER TABLE dbo.Game_HunduShijie_ItemTemplate ADD item_level int NOT NULL CONSTRAINT DF_ItemTemplate_item_level DEFAULT (0);
IF COL_LENGTH(N'dbo.Game_HunduShijie_ItemTemplate', N'item_flags') IS NULL
    ALTER TABLE dbo.Game_HunduShijie_ItemTemplate ADD item_flags int NOT NULL CONSTRAINT DF_ItemTemplate_item_flags DEFAULT (0);
IF COL_LENGTH(N'dbo.Game_HunduShijie_ItemTemplate', N'max_count') IS NULL
    ALTER TABLE dbo.Game_HunduShijie_ItemTemplate ADD max_count int NOT NULL CONSTRAINT DF_ItemTemplate_max_count DEFAULT (0);
IF COL_LENGTH(N'dbo.Game_HunduShijie_ItemTemplate', N'container_slots') IS NULL
    ALTER TABLE dbo.Game_HunduShijie_ItemTemplate ADD container_slots int NOT NULL CONSTRAINT DF_ItemTemplate_container_slots DEFAULT (0);
IF COL_LENGTH(N'dbo.Game_HunduShijie_ItemTemplate', N'attack_delay_ms') IS NULL
    ALTER TABLE dbo.Game_HunduShijie_ItemTemplate ADD attack_delay_ms int NOT NULL CONSTRAINT DF_ItemTemplate_attack_delay_ms DEFAULT (0);
IF COL_LENGTH(N'dbo.Game_HunduShijie_ItemTemplate', N'armor') IS NULL
    ALTER TABLE dbo.Game_HunduShijie_ItemTemplate ADD armor int NOT NULL CONSTRAINT DF_ItemTemplate_armor DEFAULT (0);
IF COL_LENGTH(N'dbo.Game_HunduShijie_ItemTemplate', N'block_value') IS NULL
    ALTER TABLE dbo.Game_HunduShijie_ItemTemplate ADD block_value int NOT NULL CONSTRAINT DF_ItemTemplate_block_value DEFAULT (0);
IF COL_LENGTH(N'dbo.Game_HunduShijie_ItemTemplate', N'resistance_element') IS NULL
    ALTER TABLE dbo.Game_HunduShijie_ItemTemplate ADD resistance_element int NOT NULL CONSTRAINT DF_ItemTemplate_resistance_element DEFAULT (5);
IF COL_LENGTH(N'dbo.Game_HunduShijie_ItemTemplate', N'resistance_value') IS NULL
    ALTER TABLE dbo.Game_HunduShijie_ItemTemplate ADD resistance_value int NOT NULL CONSTRAINT DF_ItemTemplate_resistance_value DEFAULT (0);
IF COL_LENGTH(N'dbo.Game_HunduShijie_ItemTemplate', N'durability') IS NULL
    ALTER TABLE dbo.Game_HunduShijie_ItemTemplate ADD durability int NOT NULL CONSTRAINT DF_ItemTemplate_durability DEFAULT (0);
IF COL_LENGTH(N'dbo.Game_HunduShijie_ItemTemplate', N'required_skill') IS NULL
    ALTER TABLE dbo.Game_HunduShijie_ItemTemplate ADD required_skill int NOT NULL CONSTRAINT DF_ItemTemplate_required_skill DEFAULT (0);
IF COL_LENGTH(N'dbo.Game_HunduShijie_ItemTemplate', N'required_skill_rank') IS NULL
    ALTER TABLE dbo.Game_HunduShijie_ItemTemplate ADD required_skill_rank int NOT NULL CONSTRAINT DF_ItemTemplate_required_skill_rank DEFAULT (0);
IF COL_LENGTH(N'dbo.Game_HunduShijie_ItemTemplate', N'allowable_class') IS NULL
    ALTER TABLE dbo.Game_HunduShijie_ItemTemplate ADD allowable_class int NOT NULL CONSTRAINT DF_ItemTemplate_allowable_class DEFAULT (0);
IF COL_LENGTH(N'dbo.Game_HunduShijie_ItemTemplate', N'allowable_race') IS NULL
    ALTER TABLE dbo.Game_HunduShijie_ItemTemplate ADD allowable_race int NOT NULL CONSTRAINT DF_ItemTemplate_allowable_race DEFAULT (0);
IF COL_LENGTH(N'dbo.Game_HunduShijie_ItemTemplate', N'socket_bonus') IS NULL
    ALTER TABLE dbo.Game_HunduShijie_ItemTemplate ADD socket_bonus int NOT NULL CONSTRAINT DF_ItemTemplate_socket_bonus DEFAULT (0);
IF COL_LENGTH(N'dbo.Game_HunduShijie_ItemTemplate', N'gem_properties') IS NULL
    ALTER TABLE dbo.Game_HunduShijie_ItemTemplate ADD gem_properties int NOT NULL CONSTRAINT DF_ItemTemplate_gem_properties DEFAULT (0);
IF COL_LENGTH(N'dbo.Game_HunduShijie_ItemTemplate', N'random_property') IS NULL
    ALTER TABLE dbo.Game_HunduShijie_ItemTemplate ADD random_property int NOT NULL CONSTRAINT DF_ItemTemplate_random_property DEFAULT (0);
IF COL_LENGTH(N'dbo.Game_HunduShijie_ItemTemplate', N'random_suffix') IS NULL
    ALTER TABLE dbo.Game_HunduShijie_ItemTemplate ADD random_suffix int NOT NULL CONSTRAINT DF_ItemTemplate_random_suffix DEFAULT (0);
IF COL_LENGTH(N'dbo.Game_HunduShijie_ItemTemplate', N'disenchant_id') IS NULL
    ALTER TABLE dbo.Game_HunduShijie_ItemTemplate ADD disenchant_id int NOT NULL CONSTRAINT DF_ItemTemplate_disenchant_id DEFAULT (0);
IF COL_LENGTH(N'dbo.Game_HunduShijie_ItemTemplate', N'bag_family') IS NULL
    ALTER TABLE dbo.Game_HunduShijie_ItemTemplate ADD bag_family int NOT NULL CONSTRAINT DF_ItemTemplate_bag_family DEFAULT (0);
IF COL_LENGTH(N'dbo.Game_HunduShijie_ItemTemplate', N'food_type') IS NULL
    ALTER TABLE dbo.Game_HunduShijie_ItemTemplate ADD food_type int NOT NULL CONSTRAINT DF_ItemTemplate_food_type DEFAULT (0);
IF COL_LENGTH(N'dbo.Game_HunduShijie_ItemTemplate', N'duration_seconds') IS NULL
    ALTER TABLE dbo.Game_HunduShijie_ItemTemplate ADD duration_seconds int NOT NULL CONSTRAINT DF_ItemTemplate_duration_seconds DEFAULT (0);
IF COL_LENGTH(N'dbo.Game_HunduShijie_ItemTemplate', N'start_quest') IS NULL
    ALTER TABLE dbo.Game_HunduShijie_ItemTemplate ADD start_quest int NOT NULL CONSTRAINT DF_ItemTemplate_start_quest DEFAULT (0);
IF COL_LENGTH(N'dbo.Game_HunduShijie_ItemTemplate', N'page_text') IS NULL
    ALTER TABLE dbo.Game_HunduShijie_ItemTemplate ADD page_text int NOT NULL CONSTRAINT DF_ItemTemplate_page_text DEFAULT (0);
IF COL_LENGTH(N'dbo.Game_HunduShijie_ItemTemplate', N'language_id') IS NULL
    ALTER TABLE dbo.Game_HunduShijie_ItemTemplate ADD language_id int NOT NULL CONSTRAINT DF_ItemTemplate_language_id DEFAULT (0);
IF COL_LENGTH(N'dbo.Game_HunduShijie_ItemTemplate', N'min_money_loot') IS NULL
    ALTER TABLE dbo.Game_HunduShijie_ItemTemplate ADD min_money_loot int NOT NULL CONSTRAINT DF_ItemTemplate_min_money_loot DEFAULT (0);
IF COL_LENGTH(N'dbo.Game_HunduShijie_ItemTemplate', N'max_money_loot') IS NULL
    ALTER TABLE dbo.Game_HunduShijie_ItemTemplate ADD max_money_loot int NOT NULL CONSTRAINT DF_ItemTemplate_max_money_loot DEFAULT (0);
IF COL_LENGTH(N'dbo.Game_HunduShijie_ItemTemplate', N'publish_state') IS NULL
    ALTER TABLE dbo.Game_HunduShijie_ItemTemplate ADD publish_state tinyint NOT NULL CONSTRAINT DF_ItemTemplate_publish_state DEFAULT (1);
IF COL_LENGTH(N'dbo.Game_HunduShijie_ItemTemplate', N'content_version') IS NULL
    ALTER TABLE dbo.Game_HunduShijie_ItemTemplate ADD content_version int NOT NULL CONSTRAINT DF_ItemTemplate_content_version DEFAULT (1);
IF COL_LENGTH(N'dbo.Game_HunduShijie_ItemTemplate', N'remark') IS NULL
    ALTER TABLE dbo.Game_HunduShijie_ItemTemplate ADD remark nvarchar(200) NULL;

-- 已存在的老物品一律视为"已发布"：否则导出客户端内容表时会一件都导不出来，
-- 而表现是"编辑器保存成功、游戏里还是旧名字"。

-- ── 2. 从表（11 张）──
-- 统一带 BaseGameModel 的四列 + IsValid：EF 读写这些表时会 SELECT/INSERT 它们。
IF OBJECT_ID(N'dbo.Game_HunduShijie_ItemStat', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Game_HunduShijie_ItemStat (
        entry_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ItemStat PRIMARY KEY,
        item_id int NOT NULL,
        seq int NOT NULL CONSTRAINT DF_ItemStat_seq DEFAULT (0),
        stat_type int NOT NULL,
        stat_value int NOT NULL,
        IsValid bit NOT NULL CONSTRAINT DF_ItemStat_IsValid DEFAULT (1),
        GameId int NOT NULL CONSTRAINT DF_ItemStat_GameId DEFAULT (0),
        AreaId int NOT NULL CONSTRAINT DF_ItemStat_AreaId DEFAULT (0),
        ServerId int NOT NULL CONSTRAINT DF_ItemStat_ServerId DEFAULT (0),
        GameUserId uniqueidentifier NOT NULL CONSTRAINT DF_ItemStat_GameUserId DEFAULT ('00000000-0000-0000-0000-000000000000')
    );
    CREATE INDEX IX_ItemStat_item ON dbo.Game_HunduShijie_ItemStat (item_id, seq);
END

IF OBJECT_ID(N'dbo.Game_HunduShijie_ItemDamage', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Game_HunduShijie_ItemDamage (
        entry_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ItemDamage PRIMARY KEY,
        item_id int NOT NULL,
        seq int NOT NULL CONSTRAINT DF_ItemDamage_seq DEFAULT (0),
        damage_type int NOT NULL CONSTRAINT DF_ItemDamage_type DEFAULT (0),
        damage_min int NOT NULL CONSTRAINT DF_ItemDamage_min DEFAULT (0),
        damage_max int NOT NULL CONSTRAINT DF_ItemDamage_max DEFAULT (0),
        IsValid bit NOT NULL CONSTRAINT DF_ItemDamage_IsValid DEFAULT (1),
        GameId int NOT NULL CONSTRAINT DF_ItemDamage_GameId DEFAULT (0),
        AreaId int NOT NULL CONSTRAINT DF_ItemDamage_AreaId DEFAULT (0),
        ServerId int NOT NULL CONSTRAINT DF_ItemDamage_ServerId DEFAULT (0),
        GameUserId uniqueidentifier NOT NULL CONSTRAINT DF_ItemDamage_GameUserId DEFAULT ('00000000-0000-0000-0000-000000000000')
    );
    CREATE INDEX IX_ItemDamage_item ON dbo.Game_HunduShijie_ItemDamage (item_id, seq);
END

IF OBJECT_ID(N'dbo.Game_HunduShijie_ItemSpell', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Game_HunduShijie_ItemSpell (
        entry_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ItemSpell PRIMARY KEY,
        item_id int NOT NULL,
        seq int NOT NULL CONSTRAINT DF_ItemSpell_seq DEFAULT (0),
        spell_id int NOT NULL,
        spell_trigger int NOT NULL CONSTRAINT DF_ItemSpell_trigger DEFAULT (0),
        charges int NOT NULL CONSTRAINT DF_ItemSpell_charges DEFAULT (0),
        ppm_rate int NOT NULL CONSTRAINT DF_ItemSpell_ppm DEFAULT (0),
        cooldown_ms int NOT NULL CONSTRAINT DF_ItemSpell_cd DEFAULT (0),
        category_cooldown_ms int NOT NULL CONSTRAINT DF_ItemSpell_catcd DEFAULT (0),
        IsValid bit NOT NULL CONSTRAINT DF_ItemSpell_IsValid DEFAULT (1),
        GameId int NOT NULL CONSTRAINT DF_ItemSpell_GameId DEFAULT (0),
        AreaId int NOT NULL CONSTRAINT DF_ItemSpell_AreaId DEFAULT (0),
        ServerId int NOT NULL CONSTRAINT DF_ItemSpell_ServerId DEFAULT (0),
        GameUserId uniqueidentifier NOT NULL CONSTRAINT DF_ItemSpell_GameUserId DEFAULT ('00000000-0000-0000-0000-000000000000')
    );
    CREATE INDEX IX_ItemSpell_item ON dbo.Game_HunduShijie_ItemSpell (item_id, seq);
END

IF OBJECT_ID(N'dbo.Game_HunduShijie_ItemSocket', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Game_HunduShijie_ItemSocket (
        entry_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ItemSocket PRIMARY KEY,
        item_id int NOT NULL,
        seq int NOT NULL CONSTRAINT DF_ItemSocket_seq DEFAULT (0),
        socket_color int NOT NULL CONSTRAINT DF_ItemSocket_color DEFAULT (4),
        socket_bonus int NOT NULL CONSTRAINT DF_ItemSocket_bonus DEFAULT (0),
        IsValid bit NOT NULL CONSTRAINT DF_ItemSocket_IsValid DEFAULT (1),
        GameId int NOT NULL CONSTRAINT DF_ItemSocket_GameId DEFAULT (0),
        AreaId int NOT NULL CONSTRAINT DF_ItemSocket_AreaId DEFAULT (0),
        ServerId int NOT NULL CONSTRAINT DF_ItemSocket_ServerId DEFAULT (0),
        GameUserId uniqueidentifier NOT NULL CONSTRAINT DF_ItemSocket_GameUserId DEFAULT ('00000000-0000-0000-0000-000000000000')
    );
    CREATE INDEX IX_ItemSocket_item ON dbo.Game_HunduShijie_ItemSocket (item_id, seq);
END

IF OBJECT_ID(N'dbo.Game_HunduShijie_ItemAffix', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Game_HunduShijie_ItemAffix (
        affix_id int NOT NULL CONSTRAINT PK_ItemAffix PRIMARY KEY,
        affix_kind int NOT NULL CONSTRAINT DF_ItemAffix_kind DEFAULT (0),
        affix_name nvarchar(50) NOT NULL CONSTRAINT DF_ItemAffix_name DEFAULT (N''),
        description nvarchar(200) NOT NULL CONSTRAINT DF_ItemAffix_desc DEFAULT (N''),
        value_min int NOT NULL CONSTRAINT DF_ItemAffix_vmin DEFAULT (80),
        value_max int NOT NULL CONSTRAINT DF_ItemAffix_vmax DEFAULT (120),
        publish_state tinyint NOT NULL CONSTRAINT DF_ItemAffix_state DEFAULT (0),
        IsValid bit NOT NULL CONSTRAINT DF_ItemAffix_IsValid DEFAULT (1),
        GameId int NOT NULL CONSTRAINT DF_ItemAffix_GameId DEFAULT (0),
        AreaId int NOT NULL CONSTRAINT DF_ItemAffix_AreaId DEFAULT (0),
        ServerId int NOT NULL CONSTRAINT DF_ItemAffix_ServerId DEFAULT (0),
        GameUserId uniqueidentifier NOT NULL CONSTRAINT DF_ItemAffix_GameUserId DEFAULT ('00000000-0000-0000-0000-000000000000')
    );
END

IF OBJECT_ID(N'dbo.Game_HunduShijie_ItemAffixStat', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Game_HunduShijie_ItemAffixStat (
        entry_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ItemAffixStat PRIMARY KEY,
        affix_id int NOT NULL,
        seq int NOT NULL CONSTRAINT DF_ItemAffixStat_seq DEFAULT (0),
        stat_type int NOT NULL,
        stat_value int NOT NULL,
        IsValid bit NOT NULL CONSTRAINT DF_ItemAffixStat_IsValid DEFAULT (1),
        GameId int NOT NULL CONSTRAINT DF_ItemAffixStat_GameId DEFAULT (0),
        AreaId int NOT NULL CONSTRAINT DF_ItemAffixStat_AreaId DEFAULT (0),
        ServerId int NOT NULL CONSTRAINT DF_ItemAffixStat_ServerId DEFAULT (0),
        GameUserId uniqueidentifier NOT NULL CONSTRAINT DF_ItemAffixStat_GameUserId DEFAULT ('00000000-0000-0000-0000-000000000000')
    );
    CREATE INDEX IX_ItemAffixStat_affix ON dbo.Game_HunduShijie_ItemAffixStat (affix_id, seq);
END

IF OBJECT_ID(N'dbo.Game_HunduShijie_ItemSet', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Game_HunduShijie_ItemSet (
        set_id int NOT NULL CONSTRAINT PK_ItemSet PRIMARY KEY,
        set_name nvarchar(50) NOT NULL CONSTRAINT DF_ItemSet_name DEFAULT (N''),
        description nvarchar(300) NOT NULL CONSTRAINT DF_ItemSet_desc DEFAULT (N''),
        item_level int NOT NULL CONSTRAINT DF_ItemSet_ilvl DEFAULT (0),
        publish_state tinyint NOT NULL CONSTRAINT DF_ItemSet_state DEFAULT (0),
        IsValid bit NOT NULL CONSTRAINT DF_ItemSet_IsValid DEFAULT (1),
        GameId int NOT NULL CONSTRAINT DF_ItemSet_GameId DEFAULT (0),
        AreaId int NOT NULL CONSTRAINT DF_ItemSet_AreaId DEFAULT (0),
        ServerId int NOT NULL CONSTRAINT DF_ItemSet_ServerId DEFAULT (0),
        GameUserId uniqueidentifier NOT NULL CONSTRAINT DF_ItemSet_GameUserId DEFAULT ('00000000-0000-0000-0000-000000000000')
    );
END

IF OBJECT_ID(N'dbo.Game_HunduShijie_ItemSetBonus', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Game_HunduShijie_ItemSetBonus (
        entry_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ItemSetBonus PRIMARY KEY,
        set_id int NOT NULL,
        pieces int NOT NULL,
        bonus_description nvarchar(200) NOT NULL CONSTRAINT DF_ItemSetBonus_desc DEFAULT (N''),
        spell_id int NOT NULL CONSTRAINT DF_ItemSetBonus_spell DEFAULT (0),
        IsValid bit NOT NULL CONSTRAINT DF_ItemSetBonus_IsValid DEFAULT (1),
        GameId int NOT NULL CONSTRAINT DF_ItemSetBonus_GameId DEFAULT (0),
        AreaId int NOT NULL CONSTRAINT DF_ItemSetBonus_AreaId DEFAULT (0),
        ServerId int NOT NULL CONSTRAINT DF_ItemSetBonus_ServerId DEFAULT (0),
        GameUserId uniqueidentifier NOT NULL CONSTRAINT DF_ItemSetBonus_GameUserId DEFAULT ('00000000-0000-0000-0000-000000000000')
    );
    CREATE INDEX IX_ItemSetBonus_set ON dbo.Game_HunduShijie_ItemSetBonus (set_id, pieces);
END

IF OBJECT_ID(N'dbo.Game_HunduShijie_ItemSetBonusStat', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Game_HunduShijie_ItemSetBonusStat (
        entry_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ItemSetBonusStat PRIMARY KEY,
        set_id int NOT NULL,
        pieces int NOT NULL,
        stat_type int NOT NULL,
        stat_value int NOT NULL,
        IsValid bit NOT NULL CONSTRAINT DF_ItemSetBonusStat_IsValid DEFAULT (1),
        GameId int NOT NULL CONSTRAINT DF_ItemSetBonusStat_GameId DEFAULT (0),
        AreaId int NOT NULL CONSTRAINT DF_ItemSetBonusStat_AreaId DEFAULT (0),
        ServerId int NOT NULL CONSTRAINT DF_ItemSetBonusStat_ServerId DEFAULT (0),
        GameUserId uniqueidentifier NOT NULL CONSTRAINT DF_ItemSetBonusStat_GameUserId DEFAULT ('00000000-0000-0000-0000-000000000000')
    );
    CREATE INDEX IX_ItemSetBonusStat_set ON dbo.Game_HunduShijie_ItemSetBonusStat (set_id, pieces);
END

IF OBJECT_ID(N'dbo.Game_HunduShijie_ItemSource', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Game_HunduShijie_ItemSource (
        entry_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ItemSource PRIMARY KEY,
        item_id int NOT NULL,
        seq int NOT NULL CONSTRAINT DF_ItemSource_seq DEFAULT (0),
        source_type int NOT NULL CONSTRAINT DF_ItemSource_type DEFAULT (0),
        ref_type int NOT NULL CONSTRAINT DF_ItemSource_refType DEFAULT (0),
        ref_id int NOT NULL CONSTRAINT DF_ItemSource_refId DEFAULT (0),
        note nvarchar(200) NOT NULL CONSTRAINT DF_ItemSource_note DEFAULT (N''),
        IsValid bit NOT NULL CONSTRAINT DF_ItemSource_IsValid DEFAULT (1),
        GameId int NOT NULL CONSTRAINT DF_ItemSource_GameId DEFAULT (0),
        AreaId int NOT NULL CONSTRAINT DF_ItemSource_AreaId DEFAULT (0),
        ServerId int NOT NULL CONSTRAINT DF_ItemSource_ServerId DEFAULT (0),
        GameUserId uniqueidentifier NOT NULL CONSTRAINT DF_ItemSource_GameUserId DEFAULT ('00000000-0000-0000-0000-000000000000')
    );
    CREATE INDEX IX_ItemSource_item ON dbo.Game_HunduShijie_ItemSource (item_id, seq);
    CREATE INDEX IX_ItemSource_ref ON dbo.Game_HunduShijie_ItemSource (ref_type, ref_id);
END

IF OBJECT_ID(N'dbo.Game_HunduShijie_ItemLocalization', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Game_HunduShijie_ItemLocalization (
        entry_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ItemLocalization PRIMARY KEY,
        item_id int NOT NULL,
        locale varchar(10) NOT NULL CONSTRAINT DF_ItemLocalization_locale DEFAULT ('zhCN'),
        item_name nvarchar(50) NOT NULL CONSTRAINT DF_ItemLocalization_name DEFAULT (N''),
        description nvarchar(500) NOT NULL CONSTRAINT DF_ItemLocalization_desc DEFAULT (N''),
        IsValid bit NOT NULL CONSTRAINT DF_ItemLocalization_IsValid DEFAULT (1),
        GameId int NOT NULL CONSTRAINT DF_ItemLocalization_GameId DEFAULT (0),
        AreaId int NOT NULL CONSTRAINT DF_ItemLocalization_AreaId DEFAULT (0),
        ServerId int NOT NULL CONSTRAINT DF_ItemLocalization_ServerId DEFAULT (0),
        GameUserId uniqueidentifier NOT NULL CONSTRAINT DF_ItemLocalization_GameUserId DEFAULT ('00000000-0000-0000-0000-000000000000')
    );
    CREATE UNIQUE INDEX UX_ItemLocalization_item_locale ON dbo.Game_HunduShijie_ItemLocalization (item_id, locale);
END

COMMIT TRANSACTION;
PRINT '023 物品编辑器表结构已就绪（扩展列 31、从表 11）。';
