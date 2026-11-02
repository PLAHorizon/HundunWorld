/*
  Scripted world spawn (DS monster/NPC/event) grant authority table.
  Idempotent: safe to re-run. Written in ASCII so sqlcmd reads it regardless of BOM handling.

  Apply (project convention: EF design-time does NOT expand ${VAR} placeholders, so run DDL directly):
      sqlcmd -S <server> -d <db> -i scripts/sql/gm_script_grant.sql

  Why a hand-written DDL instead of `dotnet ef migrations add`:
  the model snapshot is currently being edited by another workstream; adding a migration here
  would collide with their pending migration. This script is additive and can later be folded
  into a migration without changing the table shape.
*/

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'gm_script_grant')
BEGIN
    CREATE TABLE gm_script_grant (
        id                   bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
        operator_character_id bigint              NOT NULL,
        flags                bigint              NOT NULL CONSTRAINT df_gm_script_grant_flags DEFAULT (0),
        granted_by           bigint              NOT NULL CONSTRAINT df_gm_script_grant_by DEFAULT (0),
        granted_at_utc       datetime2           NOT NULL CONSTRAINT df_gm_script_grant_at DEFAULT (SYSUTCDATETIME()),
        expires_at_utc       datetime2           NOT NULL CONSTRAINT df_gm_script_grant_exp DEFAULT (SYSUTCDATETIME()),
        revoked_at_utc       datetime2           NULL,
        reason               nvarchar(200)       NULL,
        IsValid              bit                 NOT NULL CONSTRAINT df_gm_script_grant_valid DEFAULT (1)
    );
END
GO

/* One live row per operator: the grain looks up by operator_character_id. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_gm_script_grant_operator')
BEGIN
    CREATE INDEX ix_gm_script_grant_operator
        ON gm_script_grant (operator_character_id, revoked_at_utc);
END
GO
