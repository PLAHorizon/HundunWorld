BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005131122_AddQuestEditorColumns'
)
BEGIN
    ALTER TABLE [Game_HunduShijie_QuestTemplate] ADD [content_version] int NOT NULL DEFAULT 0;
    DECLARE @defaultSchema AS sysname;
    SET @defaultSchema = SCHEMA_NAME();
    DECLARE @description AS sql_variant;
    SET @description = N'内容版本号 每次入库自增';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_QuestTemplate', 'COLUMN', N'content_version';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005131122_AddQuestEditorColumns'
)
BEGIN
    ALTER TABLE [Game_HunduShijie_QuestTemplate] ADD [giver_kind] int NOT NULL DEFAULT 0;
    DECLARE @defaultSchema1 AS sysname;
    SET @defaultSchema1 = SCHEMA_NAME();
    DECLARE @description1 AS sql_variant;
    SET @description1 = N'发布者类别 1NPC 2怪物 3物品 4物件 5区域';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', @defaultSchema1, 'TABLE', N'Game_HunduShijie_QuestTemplate', 'COLUMN', N'giver_kind';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005131122_AddQuestEditorColumns'
)
BEGIN
    ALTER TABLE [Game_HunduShijie_QuestTemplate] ADD [giver_spawn_point_id] int NOT NULL DEFAULT 0;
    DECLARE @defaultSchema2 AS sysname;
    SET @defaultSchema2 = SCHEMA_NAME();
    DECLARE @description2 AS sql_variant;
    SET @description2 = N'发布者限定生成点 0=不限';
    EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_QuestTemplate', 'COLUMN', N'giver_spawn_point_id';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005131122_AddQuestEditorColumns'
)
BEGIN
    ALTER TABLE [Game_HunduShijie_QuestTemplate] ADD [giver_template_id] int NOT NULL DEFAULT 0;
    DECLARE @defaultSchema3 AS sysname;
    SET @defaultSchema3 = SCHEMA_NAME();
    DECLARE @description3 AS sql_variant;
    SET @description3 = N'发布者模板ID 0=未配置';
    EXEC sp_addextendedproperty 'MS_Description', @description3, 'SCHEMA', @defaultSchema3, 'TABLE', N'Game_HunduShijie_QuestTemplate', 'COLUMN', N'giver_template_id';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005131122_AddQuestEditorColumns'
)
BEGIN
    ALTER TABLE [Game_HunduShijie_QuestTemplate] ADD [giver_zone_id] int NOT NULL DEFAULT 0;
    DECLARE @defaultSchema4 AS sysname;
    SET @defaultSchema4 = SCHEMA_NAME();
    DECLARE @description4 AS sql_variant;
    SET @description4 = N'发布者限定区域 0=不限';
    EXEC sp_addextendedproperty 'MS_Description', @description4, 'SCHEMA', @defaultSchema4, 'TABLE', N'Game_HunduShijie_QuestTemplate', 'COLUMN', N'giver_zone_id';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005131122_AddQuestEditorColumns'
)
BEGIN
    ALTER TABLE [Game_HunduShijie_QuestTemplate] ADD [prerequisite_quests] varchar(200) NOT NULL DEFAULT '';
    DECLARE @defaultSchema5 AS sysname;
    SET @defaultSchema5 = SCHEMA_NAME();
    DECLARE @description5 AS sql_variant;
    SET @description5 = N'前置任务ID 逗号分隔';
    EXEC sp_addextendedproperty 'MS_Description', @description5, 'SCHEMA', @defaultSchema5, 'TABLE', N'Game_HunduShijie_QuestTemplate', 'COLUMN', N'prerequisite_quests';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005131122_AddQuestEditorColumns'
)
BEGIN
    ALTER TABLE [Game_HunduShijie_QuestTemplate] ADD [publish_state] int NOT NULL DEFAULT 0;
    DECLARE @defaultSchema6 AS sysname;
    SET @defaultSchema6 = SCHEMA_NAME();
    DECLARE @description6 AS sql_variant;
    SET @description6 = N'发布状态 0下线 1已发布（实时开关，不影响已接实例）';
    EXEC sp_addextendedproperty 'MS_Description', @description6, 'SCHEMA', @defaultSchema6, 'TABLE', N'Game_HunduShijie_QuestTemplate', 'COLUMN', N'publish_state';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005131122_AddQuestEditorColumns'
)
BEGIN
    ALTER TABLE [Game_HunduShijie_QuestTemplate] ADD [turn_in_kind] int NOT NULL DEFAULT 0;
    DECLARE @defaultSchema7 AS sysname;
    SET @defaultSchema7 = SCHEMA_NAME();
    DECLARE @description7 AS sql_variant;
    SET @description7 = N'交付者类别 0=沿用发布者';
    EXEC sp_addextendedproperty 'MS_Description', @description7, 'SCHEMA', @defaultSchema7, 'TABLE', N'Game_HunduShijie_QuestTemplate', 'COLUMN', N'turn_in_kind';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005131122_AddQuestEditorColumns'
)
BEGIN
    ALTER TABLE [Game_HunduShijie_QuestTemplate] ADD [turn_in_template_id] int NOT NULL DEFAULT 0;
    DECLARE @defaultSchema8 AS sysname;
    SET @defaultSchema8 = SCHEMA_NAME();
    DECLARE @description8 AS sql_variant;
    SET @description8 = N'交付者模板ID 0=沿用发布者';
    EXEC sp_addextendedproperty 'MS_Description', @description8, 'SCHEMA', @defaultSchema8, 'TABLE', N'Game_HunduShijie_QuestTemplate', 'COLUMN', N'turn_in_template_id';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005131122_AddQuestEditorColumns'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261005131122_AddQuestEditorColumns', N'10.0.2');
END;

COMMIT;
GO

