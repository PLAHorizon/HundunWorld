-- 外观装备物品模板（与客户端 EquipmentDatabase.AppearanceXuanmo/Xuantie/Liuyun/Suxue 对齐）
-- item_type 新增 4 = 外观（EquipmentType.Appearance）；外观不提供战斗属性，base_attributes 为空。
-- 外观槽位固定为 15（EquipmentSlot.Appearance），由客户端穿戴命令携带；服务端只按槽位存储。
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
SET IDENTITY_INSERT dbo.Game_HunduShijie_ItemTemplate ON;

    IF NOT EXISTS (SELECT 1 FROM Game_HunduShijie_ItemTemplate WHERE item_id = 91001)
    INSERT INTO Game_HunduShijie_ItemTemplate (item_id, item_name, description, item_type, sub_type, base_quality, rarity, element, material_grade, level_require, realm_require, profession_limit, max_stack, drop_rate, source_type, source_detail, base_attributes, random_attributes, random_attr_count, gem_slot_rates, synthesis_recipe, inherit_rate, element_bonus, element_penalty, bind_type, sell_price, buy_price, icon_path, model_path, use_effect, can_trade, can_destroy, is_unique, valid_days, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId) VALUES (91001, N'玄墨劲装', N'整套外观：玄墨劲装，身形轻盈利落。仅改变角色外形，不提供战斗属性。', 4, 0, 2, 2, 0, 0, 1, 0, '', 1, 0, 0, N'', N'{}', N'', '', '', N'', '', 1.00, 1.00, 0, 0, 0, N'Content/Icons/Equipment/DefaultBody.flax', N'', N'', 1, 0, 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000');

    IF NOT EXISTS (SELECT 1 FROM Game_HunduShijie_ItemTemplate WHERE item_id = 91002)
    INSERT INTO Game_HunduShijie_ItemTemplate (item_id, item_name, description, item_type, sub_type, base_quality, rarity, element, material_grade, level_require, realm_require, profession_limit, max_stack, drop_rate, source_type, source_detail, base_attributes, random_attributes, random_attr_count, gem_slot_rates, synthesis_recipe, inherit_rate, element_bonus, element_penalty, bind_type, sell_price, buy_price, icon_path, model_path, use_effect, can_trade, can_destroy, is_unique, valid_days, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId) VALUES (91002, N'玄铁重铠', N'整套外观：玄铁重铠，身披重甲格外魁梧。仅改变角色外形，不提供战斗属性。', 4, 0, 3, 3, 0, 0, 1, 0, '', 1, 0, 0, N'', N'{}', N'', '', '', N'', '', 1.00, 1.00, 0, 0, 0, N'Content/Icons/Equipment/DefaultBody.flax', N'', N'', 1, 0, 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000');

    IF NOT EXISTS (SELECT 1 FROM Game_HunduShijie_ItemTemplate WHERE item_id = 91003)
    INSERT INTO Game_HunduShijie_ItemTemplate (item_id, item_name, description, item_type, sub_type, base_quality, rarity, element, material_grade, level_require, realm_require, profession_limit, max_stack, drop_rate, source_type, source_detail, base_attributes, random_attributes, random_attr_count, gem_slot_rates, synthesis_recipe, inherit_rate, element_bonus, element_penalty, bind_type, sell_price, buy_price, icon_path, model_path, use_effect, can_trade, can_destroy, is_unique, valid_days, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId) VALUES (91003, N'流云霓裳', N'整套外观：流云霓裳，衣袂随身形轻展。仅改变角色外形，不提供战斗属性。', 4, 0, 2, 2, 0, 0, 1, 0, '', 1, 0, 0, N'', N'{}', N'', '', '', N'', '', 1.00, 1.00, 0, 0, 0, N'Content/Icons/Equipment/DefaultBody.flax', N'', N'', 1, 0, 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000');

    IF NOT EXISTS (SELECT 1 FROM Game_HunduShijie_ItemTemplate WHERE item_id = 91004)
    INSERT INTO Game_HunduShijie_ItemTemplate (item_id, item_name, description, item_type, sub_type, base_quality, rarity, element, material_grade, level_require, realm_require, profession_limit, max_stack, drop_rate, source_type, source_detail, base_attributes, random_attributes, random_attr_count, gem_slot_rates, synthesis_recipe, inherit_rate, element_bonus, element_penalty, bind_type, sell_price, buy_price, icon_path, model_path, use_effect, can_trade, can_destroy, is_unique, valid_days, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId) VALUES (91004, N'素雪罗裙', N'整套外观：素雪罗裙，身形高挑。仅改变角色外形，不提供战斗属性。', 4, 0, 3, 3, 0, 0, 1, 0, '', 1, 0, 0, N'', N'{}', N'', '', '', N'', '', 1.00, 1.00, 0, 0, 0, N'Content/Icons/Equipment/DefaultBody.flax', N'', N'', 1, 0, 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000');

SET IDENTITY_INSERT dbo.Game_HunduShijie_ItemTemplate OFF;
COMMIT TRANSACTION;
