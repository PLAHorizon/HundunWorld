-- revert_019_monster_editor_tables.sql —— 回滚 019 建的八张怪物表。
--
-- 为什么有这个文件：019 是"只生成不执行"的产物，一旦在生产库上执行过就得有一条**不靠记忆**的回退路。
-- 顺序按外键来（子表先走），最后删 __EFMigrationsHistory 里那一行，
-- 这样 `dotnet ef database update` 会把它当未应用迁移重新跑。
--
-- **刻意不碰**的东西（两条都写在字面上，别"顺手清理"）：
--   * Game_HunduShijie_Character 上那五列 meta_human_*：它们属于数字人那条线，
--     019 只是幂等地"缺则补"，回滚怪物表不该连带删掉别条线的列与数据；
--   * NPC / 宠物那两套表：与本脚本无关。
--
-- 演练记录（2026-10-27）：在一次性库上按 019 → 020 → 本脚本跑完，
-- 断言"怪物表数 == 0 且 history 里那一行没了"，跑完 DROP DATABASE。

SET NOCOUNT ON;
BEGIN TRANSACTION;

DECLARE @fkOrder TABLE (seq int identity(1,1), tbl sysname);
INSERT INTO @fkOrder (tbl) VALUES
    ('Game_HunduShijie_MonsterDropEntry'),
    ('Game_HunduShijie_MonsterLootBinding'),
    ('Game_HunduShijie_MonsterSkillMount'),
    ('Game_HunduShijie_MonsterPatrolPoint'),
    ('Game_HunduShijie_MonsterPatrolRoute'),
    ('Game_HunduShijie_MonsterSpawnPoint'),
    ('Game_HunduShijie_MonsterBehavior'),
    ('Game_HunduShijie_MonsterTemplate');

DECLARE @seq int = 1, @tbl sysname, @sql nvarchar(300);
WHILE @seq <= (SELECT COUNT(*) FROM @fkOrder)
BEGIN
    SELECT @tbl = tbl FROM @fkOrder WHERE seq = @seq;
    -- 只在存在时删：中途失败的半程回滚也要能重跑。
    SET @sql = N'IF OBJECT_ID(N''dbo.' + @tbl + N''', N''U'') IS NOT NULL DROP TABLE dbo.[' + @tbl + N'];';
    EXEC sp_executesql @sql;
    SET @seq = @seq + 1;
END

-- 迁移历史：删掉这一行，EF 才会把 019 当未应用。别的迁移一行都不许动。
DELETE FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261025123917_AddMonsterEditorTables';

COMMIT;

-- ---------- 自检：回滚后必须看到 0 张怪物表、history 里没有那一行 ----------
SELECT COUNT(*) AS remaining_monster_tables,
       CASE WHEN COUNT(*) = 0 THEN 'OK' ELSE 'FAIL: 还有表没删掉' END AS check_drop
FROM sys.tables WHERE name LIKE 'Game_HunduShijie_Monster%';

SELECT COUNT(*) AS history_rows_left,
       CASE WHEN COUNT(*) = 0 THEN 'OK' ELSE 'FAIL: 迁移历史没清，EF 会以为已应用' END AS check_history
FROM __EFMigrationsHistory WHERE MigrationId = N'20261025123917_AddMonsterEditorTables';

-- 数字人那五列必须**还在**（回滚怪物表不该带走别条线的列）
SELECT COUNT(*) AS metahuman_cols_kept,
       CASE WHEN COUNT(*) = 5 THEN 'OK' ELSE 'FAIL: 动了不属于本脚本的列' END AS check_meta_human
FROM sys.columns
WHERE object_id = OBJECT_ID(N'dbo.Game_HunduShijie_Character') AND name LIKE 'meta_human%';
GO
