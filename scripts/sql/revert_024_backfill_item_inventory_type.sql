-- ============================================================================
-- 024 回滚：把上面这批 id 的 inventory_type 清回 0。
-- 注意：只应在本脚本执行后、且这些行没有被编辑器重新配过部位时使用；
-- 若已手工配置过，回滚会把配置一并清掉（按 item_id 清，不区分来源）。
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.Game_HunduShijie_ItemTemplate', N'U') IS NULL
BEGIN
    PRINT '跳过：本库没有 Game_HunduShijie_ItemTemplate。';
    RETURN;
END
IF COL_LENGTH(N'dbo.Game_HunduShijie_ItemTemplate', N'inventory_type') IS NULL
BEGIN
    PRINT '跳过：inventory_type 列不存在。';
    RETURN;
END

BEGIN TRANSACTION;

UPDATE dbo.Game_HunduShijie_ItemTemplate
SET inventory_type = 0
WHERE item_id IN (10001, 20001, 30001, 10002, 10003, 10004, 10005, 10006, 10007,
                  10008, 10009, 10010, 10011, 10012, 10013,
                  91001, 91002, 91003, 91004);

SELECT 'reverted', @@ROWCOUNT;

COMMIT TRANSACTION;
PRINT '024 已回滚。';
