-- ============================================================================
-- 024 回填历史装备的 inventory_type（只生成；执行需用户点头）
--
-- 为什么需要：023 之前的历史行 inventory_type 全是 0（新增列的默认值）。
-- 编辑器打开这类装备能读，但保存会被校验拒绝"防具必须指定身体部位"；
-- 客户端导出也会把它们挡在门外（避免覆盖掉硬编码里可穿的条目）。
--
-- 映射来源：HundunWorld/Source/Game/Equipment/EquipmentDatabase.cs 的 Id + Slot
-- （EquipmentSlot 0..15 → inventory_type 1..16）。只更新 inventory_type 仍为 0 的行，
-- 已手工配过的行**不覆盖**。
--
-- 回滚：scripts/sql/revert_024_backfill_item_inventory_type.sql
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
    PRINT '跳过：inventory_type 列不存在（先执行 023）。';
    RETURN;
END

BEGIN TRANSACTION;

DECLARE @map TABLE (item_id int PRIMARY KEY, inventory_type int NOT NULL);
INSERT INTO @map (item_id, inventory_type) VALUES
    (10001, 5),   -- Body
    (20001, 9),   -- RightHand
    (30001, 1),   -- Head
    (10002, 2),   -- Neck
    (10003, 3),   -- Shoulder
    (10004, 4),   -- Back
    (10005, 6),   -- Waist
    (10006, 7),   -- Legs
    (10007, 8),   -- Feet
    (10008, 10),  -- LeftHand
    (10009, 11),  -- RightRing
    (10010, 12),  -- LeftRing
    (10011, 13),  -- RightWrist
    (10012, 14),  -- LeftWrist
    (10013, 15),  -- Face
    (91001, 16),  -- Appearance
    (91002, 16),
    (91003, 16),
    (91004, 16);

UPDATE t
SET t.inventory_type = m.inventory_type
FROM dbo.Game_HunduShijie_ItemTemplate t
INNER JOIN @map m ON m.item_id = t.item_id
WHERE t.inventory_type = 0;

SELECT 'updated', @@ROWCOUNT;

COMMIT TRANSACTION;
PRINT '024 回填完成（只填 inventory_type=0 的历史装备/外观）。';
