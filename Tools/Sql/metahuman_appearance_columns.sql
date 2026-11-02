-- MetaHuman appearance columns on the character table (idempotent, additive, reversible).
--
-- Why: the grain state ("character" / GameStore) lives in Redis. If Redis is flushed or evicts
-- the key, "published look survives forever" has no backstop, so the authoritative copy must
-- also live in SQL Server.
--
-- Rollback: each column is a nullable addition; DROP COLUMN undoes it. The column list before
-- this ran is snapshotted under Tools/SqlBackups/.

IF OBJECT_ID(N'dbo.Game_HunduShijie_Character', N'U') IS NULL
    RAISERROR(N'Character table missing - is this the Game database?', 16, 1);
GO

IF COL_LENGTH('dbo.Game_HunduShijie_Character', 'meta_human_look') IS NULL
BEGIN
    ALTER TABLE dbo.Game_HunduShijie_Character ADD meta_human_look VARBINARY(MAX) NULL;
END
GO
IF COL_LENGTH('dbo.Game_HunduShijie_Character', 'meta_human_template_id') IS NULL
BEGIN
    ALTER TABLE dbo.Game_HunduShijie_Character ADD meta_human_template_id NVARCHAR(128) NULL;
END
GO
IF COL_LENGTH('dbo.Game_HunduShijie_Character', 'meta_human_checksum') IS NULL
BEGIN
    ALTER TABLE dbo.Game_HunduShijie_Character ADD meta_human_checksum INT NULL;
END
GO
IF COL_LENGTH('dbo.Game_HunduShijie_Character', 'meta_human_revision') IS NULL
BEGIN
    ALTER TABLE dbo.Game_HunduShijie_Character ADD meta_human_revision INT NULL;
END
GO
IF COL_LENGTH('dbo.Game_HunduShijie_Character', 'meta_human_published') IS NULL
BEGIN
    ALTER TABLE dbo.Game_HunduShijie_Character ADD meta_human_published BIT NOT NULL DEFAULT(0);
END
GO

SELECT name, TYPE_NAME(user_type_id) AS type_name, max_length, is_nullable
FROM sys.columns
WHERE object_id = OBJECT_ID(N'dbo.Game_HunduShijie_Character')
  AND name LIKE 'meta_human%'
ORDER BY column_id;
GO
