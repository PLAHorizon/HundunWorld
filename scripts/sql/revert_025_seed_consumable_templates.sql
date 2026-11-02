-- ============================================================================
-- 025 回滚：删除 50001/50002 两行消耗品模板。
-- 注意：按 id 删，不区分来源；若之后在编辑器里改过这两行，也会一并删除。
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.Game_HunduShijie_ItemTemplate', N'U') IS NULL
BEGIN
    PRINT '跳过：本库没有 Game_HunduShijie_ItemTemplate。';
    RETURN;
END

BEGIN TRANSACTION;
DELETE FROM dbo.Game_HunduShijie_ItemTemplate WHERE item_id IN (50001, 50002);
SELECT 'deleted', @@ROWCOUNT;
COMMIT TRANSACTION;
PRINT '025 已回滚。';
