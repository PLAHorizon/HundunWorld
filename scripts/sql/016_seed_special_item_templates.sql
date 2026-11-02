-- ============================================================================
-- 016 特殊物品入物品模板表：任务物品 / 配方 / 宝箱（9 行，item_type = 5 / 7 / 8）
--
-- 为什么需要这张种子：目标写的是"奖励物品关联**所有**游戏物品（如装备、技能书、材料、**特殊物品**）"。
-- 装备/防具/饰品/消耗品/材料/秘籍在库里都有行（现共 171 行，档位 0/1/2/3/4/6），
-- 唯独 item_type = 5（任务物品）、7（配方）、8（宝箱）**一行都没有**——于是任务编辑器的奖励下拉里
-- 根本没有"特殊物品"可选，那句话在数据上不成立。本文件就是把这三档补齐（先各给几件能用的）。
--
-- 顺带纠正一处历史错位：`神秘钥匙`（700041）语义上是特殊物品，却因为这三档没有行而被塞进"材料"（item_type=4）。
-- 本文件不改它（改了要连掉落表与客户端一起动），只是说明为什么会那样——**没有档位可填，就有人来凑**。
--
-- 号段选择（与"段即语义"的既有做法同构：材料 700000+、秘籍 800000+）：
--   750001..  = item_type 5 任务物品
--   760001..  = item_type 7 配方
--   770001..  = item_type 8 宝箱
-- **必须全部 < 800000**：服务端与客户端都按"id ≥ 800000 就是秘籍"分流（TECH_DEBT T9），
-- 落进那一段的非秘籍物品会被拿去"研读"、显示成"无名残页"。自检 4 就是钉这条的。
--
-- 已知会有的样子（本文件**不解决**，都在 TECH_DEBT 里挂着）：
--   · 显示：客户端 ItemCatalog 没有这三档的定义，物品名会落成占位"神秘物品"。真正的解法是
--     "物品名/类别随背包与任务奖励下行"（T9③），不打算靠往客户端硬编码表里再抄 9 行来糊过去
--     （那正是本仓反复踩的"两份真相源"）。作者侧不受影响：CLI 目录走的是整张表，能选到、能校验。
--   · 使用：`InventoryGrain` 对 id < 800000 的物品走普通消耗品分支，宝箱**没有开箱逻辑**（全仓没有），
--     所以现在发一把"行商木匣"只能占格子、点不出东西；配方同理没有学习入口。
--     → 要么本文件先只入 `item_type=5`（任务物品，最贴近任务系统），要么等开箱/学习接上再入 7/8。
--   · max_stack 全仓无人读（T9/T12），这里写 1/99 只是把意图留在数据里。
--
-- 幂等：每条先 IF EXISTS 再 UPDATE/INSERT，UPDATE 分支写全**内容列**（015 那次就是 UPDATE 漏列导致
-- 静默丢数据）；同一条 UPDATE 里一列只赋值一次（重复会 Msg 264，整批回滚而 sqlcmd 仍可能报 0）。
-- UPDATE 有意不碰：item_id（定位）、create_time（现场事实）、GameId/AreaId/ServerId/GameUserId（多租户归属，
-- 被别的租户占了就该让自检暴露，而不是被 UPDATE 抹平）。
--
-- ⚠️ 本文件**只生成，未执行**（本仓口径：迁移/写库等用户点头）。执行方式与前置检查：
--     python scripts/verify-quest-special-item-seed.py                    # 只读结构核验，不碰库
--     sqlcmd -S . -d Game -E -C -f 65001 -i scripts/sql/016_seed_special_item_templates.sql
--   （`-f 65001` 不能省：中文脚本没有它可能静默不执行。回退见文件末尾。）
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

SET IDENTITY_INSERT dbo.Game_HunduShijie_ItemTemplate ON;

-- 750001 玄冰令（任务信物）
IF EXISTS (SELECT 1 FROM dbo.Game_HunduShijie_ItemTemplate WHERE item_id = 750001)
    UPDATE dbo.Game_HunduShijie_ItemTemplate SET item_name = N'玄冰令', description = N'寒山派调阅门中弟子的信物，凭此令可向执事换取一次庇护', item_type = 5, sub_type = 0, base_quality = 2, rarity = 2, element = 2, material_grade = 0, level_require = 1, realm_require = 0, profession_limit = '', max_stack = 1, drop_rate = 0, source_type = 1, source_detail = '', base_attributes = N'', random_attributes = N'', random_attr_count = '', gem_slot_rates = '', synthesis_recipe = N'', inherit_rate = '', element_bonus = 1.00, element_penalty = 1.00, bind_type = 2, sell_price = 0, buy_price = 0, icon_path = N'', model_path = N'', use_effect = N'', can_trade = 0, can_destroy = 0, is_unique = 0, valid_days = 0, update_time = GETUTCDATE(), IsValid = 1 WHERE item_id = 750001;
ELSE
    INSERT INTO dbo.Game_HunduShijie_ItemTemplate (item_id, item_name, description, item_type, sub_type, base_quality, rarity, element, material_grade, level_require, realm_require, profession_limit, max_stack, drop_rate, source_type, source_detail, base_attributes, random_attributes, random_attr_count, gem_slot_rates, synthesis_recipe, inherit_rate, element_bonus, element_penalty, bind_type, sell_price, buy_price, icon_path, model_path, use_effect, can_trade, can_destroy, is_unique, valid_days, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId)
    VALUES (750001, N'玄冰令', N'寒山派调阅门中弟子的信物，凭此令可向执事换取一次庇护', 5, 0, 2, 2, 2, 0, 1, 0, '', 1, 0, 1, '', N'', N'', '', '', N'', '', 1.00, 1.00, 2, 0, 0, N'', N'', N'', 0, 0, 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000');

-- 750002 商队通行符
IF EXISTS (SELECT 1 FROM dbo.Game_HunduShijie_ItemTemplate WHERE item_id = 750002)
    UPDATE dbo.Game_HunduShijie_ItemTemplate SET item_name = N'商队通行符', description = N'游方商队的路引，持有者可随队穿行于诸城之间', item_type = 5, sub_type = 0, base_quality = 1, rarity = 1, element = 1, material_grade = 0, level_require = 1, realm_require = 0, profession_limit = '', max_stack = 20, drop_rate = 0, source_type = 1, source_detail = '', base_attributes = N'', random_attributes = N'', random_attr_count = '', gem_slot_rates = '', synthesis_recipe = N'', inherit_rate = '', element_bonus = 1.00, element_penalty = 1.00, bind_type = 1, sell_price = 10, buy_price = 0, icon_path = N'', model_path = N'', use_effect = N'', can_trade = 1, can_destroy = 1, is_unique = 0, valid_days = 0, update_time = GETUTCDATE(), IsValid = 1 WHERE item_id = 750002;
ELSE
    INSERT INTO dbo.Game_HunduShijie_ItemTemplate (item_id, item_name, description, item_type, sub_type, base_quality, rarity, element, material_grade, level_require, realm_require, profession_limit, max_stack, drop_rate, source_type, source_detail, base_attributes, random_attributes, random_attr_count, gem_slot_rates, synthesis_recipe, inherit_rate, element_bonus, element_penalty, bind_type, sell_price, buy_price, icon_path, model_path, use_effect, can_trade, can_destroy, is_unique, valid_days, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId)
    VALUES (750002, N'商队通行符', N'游方商队的路引，持有者可随队穿行于诸城之间', 5, 0, 1, 1, 1, 0, 1, 0, '', 20, 0, 1, '', N'', N'', '', '', N'', '', 1.00, 1.00, 1, 10, 0, N'', N'', N'', 1, 1, 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000');

-- 750003 师门荐书
IF EXISTS (SELECT 1 FROM dbo.Game_HunduShijie_ItemTemplate WHERE item_id = 750003)
    UPDATE dbo.Game_HunduShijie_ItemTemplate SET item_name = N'师门荐书', description = N'一封举荐后辈的书信，交到大师傅手上可换一次指点', item_type = 5, sub_type = 0, base_quality = 2, rarity = 2, element = 0, material_grade = 0, level_require = 1, realm_require = 0, profession_limit = '', max_stack = 5, drop_rate = 0, source_type = 1, source_detail = '', base_attributes = N'', random_attributes = N'', random_attr_count = '', gem_slot_rates = '', synthesis_recipe = N'', inherit_rate = '', element_bonus = 1.00, element_penalty = 1.00, bind_type = 2, sell_price = 0, buy_price = 0, icon_path = N'', model_path = N'', use_effect = N'', can_trade = 0, can_destroy = 1, is_unique = 0, valid_days = 0, update_time = GETUTCDATE(), IsValid = 1 WHERE item_id = 750003;
ELSE
    INSERT INTO dbo.Game_HunduShijie_ItemTemplate (item_id, item_name, description, item_type, sub_type, base_quality, rarity, element, material_grade, level_require, realm_require, profession_limit, max_stack, drop_rate, source_type, source_detail, base_attributes, random_attributes, random_attr_count, gem_slot_rates, synthesis_recipe, inherit_rate, element_bonus, element_penalty, bind_type, sell_price, buy_price, icon_path, model_path, use_effect, can_trade, can_destroy, is_unique, valid_days, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId)
    VALUES (750003, N'师门荐书', N'一封举荐后辈的书信，交到大师傅手上可换一次指点', 5, 0, 2, 2, 0, 0, 1, 0, '', 5, 0, 1, '', N'', N'', '', '', N'', '', 1.00, 1.00, 2, 0, 0, N'', N'', N'', 0, 1, 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000');

-- 750004 古剑残页
IF EXISTS (SELECT 1 FROM dbo.Game_HunduShijie_ItemTemplate WHERE item_id = 750004)
    UPDATE dbo.Game_HunduShijie_ItemTemplate SET item_name = N'古剑残页', description = N'残缺的图谱页，攒齐或许能拼出一路剑式', item_type = 5, sub_type = 0, base_quality = 3, rarity = 3, element = 0, material_grade = 0, level_require = 1, realm_require = 0, profession_limit = '', max_stack = 9, drop_rate = 0, source_type = 1, source_detail = '', base_attributes = N'', random_attributes = N'', random_attr_count = '', gem_slot_rates = '', synthesis_recipe = N'', inherit_rate = '', element_bonus = 1.00, element_penalty = 1.00, bind_type = 1, sell_price = 50, buy_price = 0, icon_path = N'', model_path = N'', use_effect = N'', can_trade = 1, can_destroy = 1, is_unique = 0, valid_days = 0, update_time = GETUTCDATE(), IsValid = 1 WHERE item_id = 750004;
ELSE
    INSERT INTO dbo.Game_HunduShijie_ItemTemplate (item_id, item_name, description, item_type, sub_type, base_quality, rarity, element, material_grade, level_require, realm_require, profession_limit, max_stack, drop_rate, source_type, source_detail, base_attributes, random_attributes, random_attr_count, gem_slot_rates, synthesis_recipe, inherit_rate, element_bonus, element_penalty, bind_type, sell_price, buy_price, icon_path, model_path, use_effect, can_trade, can_destroy, is_unique, valid_days, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId)
    VALUES (750004, N'古剑残页', N'残缺的图谱页，攒齐或许能拼出一路剑式', 5, 0, 3, 3, 0, 0, 1, 0, '', 9, 0, 1, '', N'', N'', '', '', N'', '', 1.00, 1.00, 1, 50, 0, N'', N'', N'', 1, 1, 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000');

-- 750005 祭祀玉璧
IF EXISTS (SELECT 1 FROM dbo.Game_HunduShijie_ItemTemplate WHERE item_id = 750005)
    UPDATE dbo.Game_HunduShijie_ItemTemplate SET item_name = N'祭祀玉璧', description = N'献给山神的玉璧，可作为供奉凭证换取一域好感', item_type = 5, sub_type = 0, base_quality = 3, rarity = 4, element = 4, material_grade = 0, level_require = 10, realm_require = 0, profession_limit = '', max_stack = 1, drop_rate = 0, source_type = 1, source_detail = '', base_attributes = N'', random_attributes = N'', random_attr_count = '', gem_slot_rates = '', synthesis_recipe = N'', inherit_rate = '', element_bonus = 1.00, element_penalty = 1.00, bind_type = 2, sell_price = 0, buy_price = 0, icon_path = N'', model_path = N'', use_effect = N'', can_trade = 0, can_destroy = 1, is_unique = 1, valid_days = 0, update_time = GETUTCDATE(), IsValid = 1 WHERE item_id = 750005;
ELSE
    INSERT INTO dbo.Game_HunduShijie_ItemTemplate (item_id, item_name, description, item_type, sub_type, base_quality, rarity, element, material_grade, level_require, realm_require, profession_limit, max_stack, drop_rate, source_type, source_detail, base_attributes, random_attributes, random_attr_count, gem_slot_rates, synthesis_recipe, inherit_rate, element_bonus, element_penalty, bind_type, sell_price, buy_price, icon_path, model_path, use_effect, can_trade, can_destroy, is_unique, valid_days, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId)
    VALUES (750005, N'祭祀玉璧', N'献给山神的玉璧，可作为供奉凭证换取一域好感', 5, 0, 3, 4, 4, 0, 10, 0, '', 1, 0, 1, '', N'', N'', '', '', N'', '', 1.00, 1.00, 2, 0, 0, N'', N'', N'', 0, 1, 1, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000');

-- 760001 粗布袍配方
IF EXISTS (SELECT 1 FROM dbo.Game_HunduShijie_ItemTemplate WHERE item_id = 760001)
    UPDATE dbo.Game_HunduShijie_ItemTemplate SET item_name = N'粗布袍·图谱', description = N'缝制粗布袍的尺寸图样（学习入口未接，先作为可发放的任务奖励存在）', item_type = 7, sub_type = 0, base_quality = 1, rarity = 1, element = 0, material_grade = 0, level_require = 1, realm_require = 0, profession_limit = '', max_stack = 10, drop_rate = 0, source_type = 1, source_detail = '', base_attributes = N'', random_attributes = N'', random_attr_count = '', gem_slot_rates = '', synthesis_recipe = N'', inherit_rate = '', element_bonus = 1.00, element_penalty = 1.00, bind_type = 1, sell_price = 30, buy_price = 0, icon_path = N'', model_path = N'', use_effect = N'', can_trade = 1, can_destroy = 1, is_unique = 0, valid_days = 0, update_time = GETUTCDATE(), IsValid = 1 WHERE item_id = 760001;
ELSE
    INSERT INTO dbo.Game_HunduShijie_ItemTemplate (item_id, item_name, description, item_type, sub_type, base_quality, rarity, element, material_grade, level_require, realm_require, profession_limit, max_stack, drop_rate, source_type, source_detail, base_attributes, random_attributes, random_attr_count, gem_slot_rates, synthesis_recipe, inherit_rate, element_bonus, element_penalty, bind_type, sell_price, buy_price, icon_path, model_path, use_effect, can_trade, can_destroy, is_unique, valid_days, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId)
    VALUES (760001, N'粗布袍·图谱', N'缝制粗布袍的尺寸图样（学习入口未接，先作为可发放的任务奖励存在）', 7, 0, 1, 1, 0, 0, 1, 0, '', 10, 0, 1, '', N'', N'', '', '', N'', '', 1.00, 1.00, 1, 30, 0, N'', N'', N'', 1, 1, 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000');

-- 760002 铁甲配方
IF EXISTS (SELECT 1 FROM dbo.Game_HunduShijie_ItemTemplate WHERE item_id = 760002)
    UPDATE dbo.Game_HunduShijie_ItemTemplate SET item_name = N'铁甲·图谱', description = N'锻打铁甲的炉温与配比图样（学习入口未接）', item_type = 7, sub_type = 0, base_quality = 2, rarity = 2, element = 0, material_grade = 0, level_require = 10, realm_require = 0, profession_limit = '', max_stack = 10, drop_rate = 0, source_type = 1, source_detail = '', base_attributes = N'', random_attributes = N'', random_attr_count = '', gem_slot_rates = '', synthesis_recipe = N'', inherit_rate = '', element_bonus = 1.00, element_penalty = 1.00, bind_type = 1, sell_price = 120, buy_price = 0, icon_path = N'', model_path = N'', use_effect = N'', can_trade = 1, can_destroy = 1, is_unique = 0, valid_days = 0, update_time = GETUTCDATE(), IsValid = 1 WHERE item_id = 760002;
ELSE
    INSERT INTO dbo.Game_HunduShijie_ItemTemplate (item_id, item_name, description, item_type, sub_type, base_quality, rarity, element, material_grade, level_require, realm_require, profession_limit, max_stack, drop_rate, source_type, source_detail, base_attributes, random_attributes, random_attr_count, gem_slot_rates, synthesis_recipe, inherit_rate, element_bonus, element_penalty, bind_type, sell_price, buy_price, icon_path, model_path, use_effect, can_trade, can_destroy, is_unique, valid_days, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId)
    VALUES (760002, N'铁甲·图谱', N'锻打铁甲的炉温与配比图样（学习入口未接）', 7, 0, 2, 2, 0, 0, 10, 0, '', 10, 0, 1, '', N'', N'', '', '', N'', '', 1.00, 1.00, 1, 120, 0, N'', N'', N'', 1, 1, 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000');

-- 770001 行商木匣
IF EXISTS (SELECT 1 FROM dbo.Game_HunduShijie_ItemTemplate WHERE item_id = 770001)
    UPDATE dbo.Game_HunduShijie_ItemTemplate SET item_name = N'行商木匣', description = N'商队随身的木匣（开箱逻辑未接，暂只作任务奖励的载体与占位）', item_type = 8, sub_type = 0, base_quality = 2, rarity = 2, element = 0, material_grade = 0, level_require = 1, realm_require = 0, profession_limit = '', max_stack = 5, drop_rate = 0, source_type = 1, source_detail = '', base_attributes = N'', random_attributes = N'', random_attr_count = '', gem_slot_rates = '', synthesis_recipe = N'', inherit_rate = '', element_bonus = 1.00, element_penalty = 1.00, bind_type = 1, sell_price = 0, buy_price = 0, icon_path = N'', model_path = N'', use_effect = N'', can_trade = 1, can_destroy = 1, is_unique = 0, valid_days = 0, update_time = GETUTCDATE(), IsValid = 1 WHERE item_id = 770001;
ELSE
    INSERT INTO dbo.Game_HunduShijie_ItemTemplate (item_id, item_name, description, item_type, sub_type, base_quality, rarity, element, material_grade, level_require, realm_require, profession_limit, max_stack, drop_rate, source_type, source_detail, base_attributes, random_attributes, random_attr_count, gem_slot_rates, synthesis_recipe, inherit_rate, element_bonus, element_penalty, bind_type, sell_price, buy_price, icon_path, model_path, use_effect, can_trade, can_destroy, is_unique, valid_days, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId)
    VALUES (770001, N'行商木匣', N'商队随身的木匣（开箱逻辑未接，暂只作任务奖励的载体与占位）', 8, 0, 2, 2, 0, 0, 1, 0, '', 5, 0, 1, '', N'', N'', '', '', N'', '', 1.00, 1.00, 1, 0, 0, N'', N'', N'', 1, 1, 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000');

-- 770002 武者石匣
IF EXISTS (SELECT 1 FROM dbo.Game_HunduShijie_ItemTemplate WHERE item_id = 770002)
    UPDATE dbo.Game_HunduShijie_ItemTemplate SET item_name = N'武者石匣', description = N'封存较久的石匣，据说是前人试炼的遗物（开箱逻辑未接）', item_type = 8, sub_type = 0, base_quality = 3, rarity = 3, element = 0, material_grade = 0, level_require = 20, realm_require = 0, profession_limit = '', max_stack = 5, drop_rate = 0, source_type = 1, source_detail = '', base_attributes = N'', random_attributes = N'', random_attr_count = '', gem_slot_rates = '', synthesis_recipe = N'', inherit_rate = '', element_bonus = 1.00, element_penalty = 1.00, bind_type = 1, sell_price = 0, buy_price = 0, icon_path = N'', model_path = N'', use_effect = N'', can_trade = 1, can_destroy = 1, is_unique = 0, valid_days = 0, update_time = GETUTCDATE(), IsValid = 1 WHERE item_id = 770002;
ELSE
    INSERT INTO dbo.Game_HunduShijie_ItemTemplate (item_id, item_name, description, item_type, sub_type, base_quality, rarity, element, material_grade, level_require, realm_require, profession_limit, max_stack, drop_rate, source_type, source_detail, base_attributes, random_attributes, random_attr_count, gem_slot_rates, synthesis_recipe, inherit_rate, element_bonus, element_penalty, bind_type, sell_price, buy_price, icon_path, model_path, use_effect, can_trade, can_destroy, is_unique, valid_days, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId)
    VALUES (770002, N'武者石匣', N'封存较久的石匣，据说是前人试炼的遗物（开箱逻辑未接）', 8, 0, 3, 3, 0, 0, 20, 0, '', 5, 0, 1, '', N'', N'', '', '', N'', '', 1.00, 1.00, 1, 0, 0, N'', N'', N'', 1, 1, 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000');

SET IDENTITY_INSERT dbo.Game_HunduShijie_ItemTemplate OFF;

-- 自检 1：三档各 5 / 2 / 2 行，合计 9。少一条就是解析/替换漏了。
IF (SELECT COUNT(*) FROM dbo.Game_HunduShijie_ItemTemplate WHERE item_type = 5) <> 5
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR(N'016 种子：item_type=5（任务物品）应为 5 行，整批回滚。', 16, 1) WITH NOWAIT;
    RETURN;
END;
IF (SELECT COUNT(*) FROM dbo.Game_HunduShijie_ItemTemplate WHERE item_type = 7) <> 2
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR(N'016 种子：item_type=7（配方）应为 2 行，整批回滚。', 16, 1) WITH NOWAIT;
    RETURN;
END;
IF (SELECT COUNT(*) FROM dbo.Game_HunduShijie_ItemTemplate WHERE item_type = 8) <> 2
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR(N'016 种子：item_type=8（宝箱）应为 2 行，整批回滚。', 16, 1) WITH NOWAIT;
    RETURN;
END;

-- 自检 2：本种子覆盖的 id 必须**还是这三个档位**——若哪个 id 已被别的类型占了（例如别的系统先建了 750001），
-- 说明号段撞车，UPDATE 分支会把人家的物品悄悄改成任务物品。这条要暴露，不许抹平。
IF EXISTS (SELECT 1 FROM dbo.Game_HunduShijie_ItemTemplate
           WHERE item_id BETWEEN 750001 AND 750005 AND item_type <> 5
              OR item_id BETWEEN 760001 AND 760002 AND item_type <> 7
              OR item_id BETWEEN 770001 AND 770002 AND item_type <> 8)
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR(N'016 种子：750001..770002 段内有 id 属于别的档位（号段撞车），整批回滚。', 16, 1) WITH NOWAIT;
    RETURN;
END;

-- 自检 3：三档内 id 不许重复（整表唯一性兜底）。
IF EXISTS (SELECT item_id FROM dbo.Game_HunduShijie_ItemTemplate GROUP BY item_id HAVING COUNT(*) > 1)
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR(N'016 种子：物品表出现重复 item_id，整批回滚。', 16, 1) WITH NOWAIT;
    RETURN;
END;

-- 自检 4：**这三档一件都不许落进秘籍号段**。id ≥ 800000 会被服务端与客户端的
-- "段即秘籍"启发式吞掉（好感/开箱效果不触发、显示成"无名残页"，见 TECH_DEBT T9）。
IF EXISTS (SELECT 1 FROM dbo.Game_HunduShijie_ItemTemplate WHERE item_id >= 800000 AND item_type IN (5, 7, 8))
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR(N'016 种子：任务物品/配方/宝箱落进了秘籍号段，整批回滚。', 16, 1) WITH NOWAIT;
    RETURN;
END;

-- 自检 5：名字非空且**在本三档内唯一**——重名会让"按名字填 id"的解析层歧义（QuestCatalogLookup）。
IF EXISTS (SELECT 1 FROM dbo.Game_HunduShijie_ItemTemplate WHERE item_type IN (5, 7, 8) AND LTRIM(RTRIM(item_name)) = '')
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR(N'016 种子：有物品没有名字，整批回滚。', 16, 1) WITH NOWAIT;
    RETURN;
END;
IF EXISTS (SELECT item_name FROM dbo.Game_HunduShijie_ItemTemplate WHERE item_type IN (5, 7, 8)
           GROUP BY item_name HAVING COUNT(*) > 1)
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR(N'016 种子：三档内出现重名物品（名字反查 id 会歧义），整批回滚。', 16, 1) WITH NOWAIT;
    RETURN;
END;

COMMIT TRANSACTION;

-- 对账（跑完自己看一眼，别只信 exit code）
SELECT item_type, COUNT(*) AS rows_count, MIN(item_id) AS min_id, MAX(item_id) AS max_id
FROM dbo.Game_HunduShijie_ItemTemplate WHERE item_type IN (5, 7, 8) GROUP BY item_type ORDER BY item_type;

-- 回退（本批只新增/刷新这 9 行，删掉即可回到执行前）：
--   DELETE FROM dbo.Game_HunduShijie_ItemTemplate
--   WHERE item_id IN (750001,750002,750003,750004,750005,760001,760002,770001,770002);
-- ============================================================================
