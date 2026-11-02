BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929063321_AddSkillWuxingColumns'
)
BEGIN
    ALTER TABLE [Game_HunduShijie_SkillTemplate] ADD [skill_attack] int NOT NULL DEFAULT 0;
    DECLARE @defaultSchema AS sysname;
    SET @defaultSchema = SCHEMA_NAME();
    DECLARE @description AS sql_variant;
    SET @description = N'技能附加的该行五行攻击力（求和侧）';
    EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'Game_HunduShijie_SkillTemplate', 'COLUMN', N'skill_attack';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929063321_AddSkillWuxingColumns'
)
BEGIN
    ALTER TABLE [Game_HunduShijie_SkillTemplate] ADD [skill_penetration] int NOT NULL DEFAULT 0;
    DECLARE @defaultSchema1 AS sysname;
    SET @defaultSchema1 = SCHEMA_NAME();
    DECLARE @description1 AS sql_variant;
    SET @description1 = N'技能附加的该行五行破防（取高侧）';
    EXEC sp_addextendedproperty 'MS_Description', @description1, 'SCHEMA', @defaultSchema1, 'TABLE', N'Game_HunduShijie_SkillTemplate', 'COLUMN', N'skill_penetration';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929063321_AddSkillWuxingColumns'
)
BEGIN
    ALTER TABLE [Game_HunduShijie_SkillTemplate] ADD [wuxing_element] int NOT NULL DEFAULT 0;
    DECLARE @defaultSchema2 AS sysname;
    SET @defaultSchema2 = SCHEMA_NAME();
    DECLARE @description2 AS sql_variant;
    SET @description2 = N'技能五行（0=无属性，1金2木3水4火5土）';
    EXEC sp_addextendedproperty 'MS_Description', @description2, 'SCHEMA', @defaultSchema2, 'TABLE', N'Game_HunduShijie_SkillTemplate', 'COLUMN', N'wuxing_element';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929063321_AddSkillWuxingColumns'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260929063321_AddSkillWuxingColumns', N'10.0.2');
END;

COMMIT;
GO

