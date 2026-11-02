-- ============================================================================
-- 025 消耗品模板：50001 金创药 / 50002 回气丹（只生成；执行需用户点头）
--
-- 背景：这两个 id 一直只活在客户端硬编码里（ItemCatalog.EnsureConsumables），库里没有行，
-- 所以物品编辑器列不出、也改不了它们。补两行让编辑器可管理；use_effect 的形状由
-- ItemCore.ParseRestoreFromUseEffect 读取（hp/mp）。
-- 客户端覆盖层会把它们覆盖成同一组数值（300 血 / 200 气），行为不变。
--
-- 回滚：scripts/sql/revert_025_seed_consumable_templates.sql
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.Game_HunduShijie_ItemTemplate', N'U') IS NULL
BEGIN
    PRINT '跳过：本库没有 Game_HunduShijie_ItemTemplate。';
    RETURN;
END

BEGIN TRANSACTION;
SET IDENTITY_INSERT dbo.Game_HunduShijie_ItemTemplate ON;

IF NOT EXISTS (SELECT 1 FROM dbo.Game_HunduShijie_ItemTemplate WHERE item_id = 50001)
INSERT INTO dbo.Game_HunduShijie_ItemTemplate
(item_id, item_name, description, item_type, sub_type, base_quality, rarity, element, material_grade,
 level_require, realm_require, profession_limit, max_stack, drop_rate, source_type, source_detail,
 base_attributes, random_attributes, random_attr_count, gem_slot_rates, synthesis_recipe, inherit_rate,
 element_bonus, element_penalty, bind_type, sell_price, buy_price, icon_path, model_path, use_effect,
 can_trade, can_destroy, is_unique, valid_days, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId)
VALUES
(50001, N'金创药', N'外伤圣药，服用后恢复 300 点生命。', 3, 0, 2, 0, 5, 0,
 1, 0, '', 20, 0, 0, N'',
 N'{}', N'', '', '', N'', '',
 1.00, 1.00, 0, 10, 0, N'', N'', N'{"hp":300}',
 1, 1, 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000');

IF NOT EXISTS (SELECT 1 FROM dbo.Game_HunduShijie_ItemTemplate WHERE item_id = 50002)
INSERT INTO dbo.Game_HunduShijie_ItemTemplate
(item_id, item_name, description, item_type, sub_type, base_quality, rarity, element, material_grade,
 level_require, realm_require, profession_limit, max_stack, drop_rate, source_type, source_detail,
 base_attributes, random_attributes, random_attr_count, gem_slot_rates, synthesis_recipe, inherit_rate,
 element_bonus, element_penalty, bind_type, sell_price, buy_price, icon_path, model_path, use_effect,
 can_trade, can_destroy, is_unique, valid_days, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId)
VALUES
(50002, N'回气丹', N'培元固本，服用后恢复 200 点内力。', 3, 0, 2, 0, 5, 0,
 1, 0, '', 20, 0, 0, N'',
 N'{}', N'', '', '', N'', '',
 1.00, 1.00, 0, 10, 0, N'', N'', N'{"mp":200}',
 1, 1, 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000');

SET IDENTITY_INSERT dbo.Game_HunduShijie_ItemTemplate OFF;

SELECT 'consumables', COUNT(*) FROM dbo.Game_HunduShijie_ItemTemplate WHERE item_id IN (50001, 50002);
COMMIT TRANSACTION;
PRINT '025 消耗品模板已就绪。';
