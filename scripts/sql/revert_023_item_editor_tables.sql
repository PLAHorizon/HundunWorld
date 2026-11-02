-- ============================================================================
-- 023 回滚：删除物品编辑器的从表与扩展列
-- 注意：会**丢弃**所有物品属性/伤害/插槽/词缀/套装/出处/多语言数据。
-- 执行前先备份：SELECT * INTO ..._bak_20261027 FROM ...（逐表）。
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.Game_HunduShijie_ItemLocalization', N'U') IS NOT NULL DROP TABLE dbo.Game_HunduShijie_ItemLocalization;
IF OBJECT_ID(N'dbo.Game_HunduShijie_ItemSource', N'U') IS NOT NULL DROP TABLE dbo.Game_HunduShijie_ItemSource;
IF OBJECT_ID(N'dbo.Game_HunduShijie_ItemSetBonusStat', N'U') IS NOT NULL DROP TABLE dbo.Game_HunduShijie_ItemSetBonusStat;
IF OBJECT_ID(N'dbo.Game_HunduShijie_ItemSetBonus', N'U') IS NOT NULL DROP TABLE dbo.Game_HunduShijie_ItemSetBonus;
IF OBJECT_ID(N'dbo.Game_HunduShijie_ItemSet', N'U') IS NOT NULL DROP TABLE dbo.Game_HunduShijie_ItemSet;
IF OBJECT_ID(N'dbo.Game_HunduShijie_ItemAffixStat', N'U') IS NOT NULL DROP TABLE dbo.Game_HunduShijie_ItemAffixStat;
IF OBJECT_ID(N'dbo.Game_HunduShijie_ItemAffix', N'U') IS NOT NULL DROP TABLE dbo.Game_HunduShijie_ItemAffix;
IF OBJECT_ID(N'dbo.Game_HunduShijie_ItemSocket', N'U') IS NOT NULL DROP TABLE dbo.Game_HunduShijie_ItemSocket;
IF OBJECT_ID(N'dbo.Game_HunduShijie_ItemSpell', N'U') IS NOT NULL DROP TABLE dbo.Game_HunduShijie_ItemSpell;
IF OBJECT_ID(N'dbo.Game_HunduShijie_ItemDamage', N'U') IS NOT NULL DROP TABLE dbo.Game_HunduShijie_ItemDamage;
IF OBJECT_ID(N'dbo.Game_HunduShijie_ItemStat', N'U') IS NOT NULL DROP TABLE dbo.Game_HunduShijie_ItemStat;

IF OBJECT_ID(N'dbo.Game_HunduShijie_ItemTemplate', N'U') IS NOT NULL
BEGIN
    DECLARE @cols TABLE (name sysname);
    INSERT INTO @cols (name) VALUES
        ('inventory_type'), ('item_level'), ('item_flags'), ('max_count'), ('container_slots'),
        ('attack_delay_ms'), ('armor'), ('block_value'), ('resistance_element'), ('resistance_value'),
        ('durability'), ('required_skill'), ('required_skill_rank'), ('allowable_class'), ('allowable_race'),
        ('socket_bonus'), ('gem_properties'), ('random_property'), ('random_suffix'), ('disenchant_id'),
        ('bag_family'), ('food_type'), ('duration_seconds'), ('start_quest'), ('page_text'),
        ('language_id'), ('min_money_loot'), ('max_money_loot'), ('publish_state'), ('content_version'), ('remark');

    DECLARE @name sysname, @sql nvarchar(400), @defaultName sysname;
    DECLARE cur CURSOR LOCAL FAST_FORWARD FOR SELECT name FROM @cols;
    OPEN cur;
    FETCH NEXT FROM cur INTO @name;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF COL_LENGTH(N'dbo.Game_HunduShijie_ItemTemplate', @name) IS NOT NULL
        BEGIN
            -- 023 给每列都建了具名 DEFAULT 约束；不先删约束，DROP COLUMN 会报 Msg 4922
            -- 并把整批回滚（实测：11 张从表一张也没删掉，事务是原子的）。
            SET @defaultName = (
                SELECT dc.name
                FROM sys.default_constraints dc
                INNER JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
                WHERE dc.parent_object_id = OBJECT_ID(N'dbo.Game_HunduShijie_ItemTemplate')
                  AND c.name = @name);
            IF @defaultName IS NOT NULL
            BEGIN
                SET @sql = N'ALTER TABLE dbo.Game_HunduShijie_ItemTemplate DROP CONSTRAINT ' + QUOTENAME(@defaultName) + N';';
                EXEC sp_executesql @sql;
            END

            SET @sql = N'ALTER TABLE dbo.Game_HunduShijie_ItemTemplate DROP COLUMN ' + QUOTENAME(@name) + N';';
            EXEC sp_executesql @sql;
        END
        FETCH NEXT FROM cur INTO @name;
    END
    CLOSE cur;
    DEALLOCATE cur;
END

COMMIT TRANSACTION;
PRINT '023 已回滚（从表 11 张 + 扩展列 31 个）。';
