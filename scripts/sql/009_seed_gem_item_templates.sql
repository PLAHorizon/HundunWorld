-- 009_seed_gem_item_templates.sql
-- 补上五颗五行宝石的物品模板行（60001..60005），让"已镶宝石的五行亲和"在服务端也读得到。
--
-- 症状：客户端把宝石亲和算进五行属性值（ItemCatalog.GemAffinityPerGem = 20/颗，
-- 经 EquipmentAttributeBridge 累加进 MetalAffinity…），而服务端物品表里根本没有这五行，
-- `CombatEquipmentSource` 按 gemTemplateId 解析模板时拿到 null → 该件装备的宝石贡献整体为 0。
-- 在"五行属性值兜底三行"的新口径下，这不再只是雷达图上的差异：亲和是兜底地板的输入，
-- 两端亲和不同 → 客户端算出的三行与服务端结算的三行必然不同。
--
-- 数值全部对齐客户端 ItemCatalog：一颗宝石只给本行亲和 +20，不给三行、不给通用攻防。
-- item_type 沿用 006 的 EquipmentType 编号（0身体/1配饰/2武器/3其他）；宝石不是可穿戴装备，
-- 服务端没有任何读者依赖这一列，写 3 只作分类占位。
--
-- 可重复执行。回退：DELETE FROM Game_HunduShijie_ItemTemplate WHERE item_id BETWEEN 60001 AND 60005;

SET NOCOUNT ON;
SET IDENTITY_INSERT dbo.Game_HunduShijie_ItemTemplate ON;

IF EXISTS (SELECT 1 FROM Game_HunduShijie_ItemTemplate WHERE item_id = 60001)
    UPDATE Game_HunduShijie_ItemTemplate SET base_attributes = N'{"Metal": 20}', update_time = GETUTCDATE() WHERE item_id = 60001;
ELSE
    INSERT INTO Game_HunduShijie_ItemTemplate (item_id, item_name, description, item_type, sub_type, base_quality, rarity, element, material_grade, level_require, realm_require, profession_limit, max_stack, drop_rate, source_type, source_detail, base_attributes, random_attributes, random_attr_count, gem_slot_rates, synthesis_recipe, inherit_rate, element_bonus, element_penalty, bind_type, sell_price, buy_price, icon_path, model_path, use_effect, can_trade, can_destroy, is_unique, valid_days, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId) VALUES (60001, N'金锐珠', N'五行属金的宝石，镶嵌后提升五行亲和 20 点。', 3, 0, 3, 3, 0, 0, 1, 0, '', 99, 0, 0, N'', N'{"Metal": 20}', N'', '', '', N'', '', 1.00, 1.00, 0, 0, 0, N'', N'', N'', 1, 0, 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000');

IF EXISTS (SELECT 1 FROM Game_HunduShijie_ItemTemplate WHERE item_id = 60002)
    UPDATE Game_HunduShijie_ItemTemplate SET base_attributes = N'{"Wood": 20}', update_time = GETUTCDATE() WHERE item_id = 60002;
ELSE
    INSERT INTO Game_HunduShijie_ItemTemplate (item_id, item_name, description, item_type, sub_type, base_quality, rarity, element, material_grade, level_require, realm_require, profession_limit, max_stack, drop_rate, source_type, source_detail, base_attributes, random_attributes, random_attr_count, gem_slot_rates, synthesis_recipe, inherit_rate, element_bonus, element_penalty, bind_type, sell_price, buy_price, icon_path, model_path, use_effect, can_trade, can_destroy, is_unique, valid_days, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId) VALUES (60002, N'木灵珠', N'五行属木的宝石，镶嵌后提升五行亲和 20 点。', 3, 0, 3, 3, 0, 0, 1, 0, '', 99, 0, 0, N'', N'{"Wood": 20}', N'', '', '', N'', '', 1.00, 1.00, 0, 0, 0, N'', N'', N'', 1, 0, 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000');

IF EXISTS (SELECT 1 FROM Game_HunduShijie_ItemTemplate WHERE item_id = 60003)
    UPDATE Game_HunduShijie_ItemTemplate SET base_attributes = N'{"Water": 20}', update_time = GETUTCDATE() WHERE item_id = 60003;
ELSE
    INSERT INTO Game_HunduShijie_ItemTemplate (item_id, item_name, description, item_type, sub_type, base_quality, rarity, element, material_grade, level_require, realm_require, profession_limit, max_stack, drop_rate, source_type, source_detail, base_attributes, random_attributes, random_attr_count, gem_slot_rates, synthesis_recipe, inherit_rate, element_bonus, element_penalty, bind_type, sell_price, buy_price, icon_path, model_path, use_effect, can_trade, can_destroy, is_unique, valid_days, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId) VALUES (60003, N'水华珠', N'五行属水的宝石，镶嵌后提升五行亲和 20 点。', 3, 0, 3, 3, 0, 0, 1, 0, '', 99, 0, 0, N'', N'{"Water": 20}', N'', '', '', N'', '', 1.00, 1.00, 0, 0, 0, N'', N'', N'', 1, 0, 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000');

IF EXISTS (SELECT 1 FROM Game_HunduShijie_ItemTemplate WHERE item_id = 60004)
    UPDATE Game_HunduShijie_ItemTemplate SET base_attributes = N'{"Fire": 20}', update_time = GETUTCDATE() WHERE item_id = 60004;
ELSE
    INSERT INTO Game_HunduShijie_ItemTemplate (item_id, item_name, description, item_type, sub_type, base_quality, rarity, element, material_grade, level_require, realm_require, profession_limit, max_stack, drop_rate, source_type, source_detail, base_attributes, random_attributes, random_attr_count, gem_slot_rates, synthesis_recipe, inherit_rate, element_bonus, element_penalty, bind_type, sell_price, buy_price, icon_path, model_path, use_effect, can_trade, can_destroy, is_unique, valid_days, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId) VALUES (60004, N'赤焰珠', N'五行属火的宝石，镶嵌后提升五行亲和 20 点。', 3, 0, 3, 3, 0, 0, 1, 0, '', 99, 0, 0, N'', N'{"Fire": 20}', N'', '', '', N'', '', 1.00, 1.00, 0, 0, 0, N'', N'', N'', 1, 0, 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000');

IF EXISTS (SELECT 1 FROM Game_HunduShijie_ItemTemplate WHERE item_id = 60005)
    UPDATE Game_HunduShijie_ItemTemplate SET base_attributes = N'{"Earth": 20}', update_time = GETUTCDATE() WHERE item_id = 60005;
ELSE
    INSERT INTO Game_HunduShijie_ItemTemplate (item_id, item_name, description, item_type, sub_type, base_quality, rarity, element, material_grade, level_require, realm_require, profession_limit, max_stack, drop_rate, source_type, source_detail, base_attributes, random_attributes, random_attr_count, gem_slot_rates, synthesis_recipe, inherit_rate, element_bonus, element_penalty, bind_type, sell_price, buy_price, icon_path, model_path, use_effect, can_trade, can_destroy, is_unique, valid_days, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId) VALUES (60005, N'厚土珠', N'五行属土的宝石，镶嵌后提升五行亲和 20 点。', 3, 0, 3, 3, 0, 0, 1, 0, '', 99, 0, 0, N'', N'{"Earth": 20}', N'', '', '', N'', '', 1.00, 1.00, 0, 0, 0, N'', N'', N'', 1, 0, 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000');

SET IDENTITY_INSERT dbo.Game_HunduShijie_ItemTemplate OFF;
GO
