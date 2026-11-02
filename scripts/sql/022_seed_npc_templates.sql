-- ============================================================================
-- 022_seed_npc_templates.sql —— NPC 编辑器：把今天硬编码的 3 个 NPC 搬进表，并给两条样板
--
-- 本脚本**只生成、不执行**（迁移与写库由用户点头后再跑；本地可 sqlcmd -E 直连）。
-- 执行前提：021_npc_editor_tables.sql 已建表。
--
-- 为什么要种子而不是从空表开始配：
--   1) 编号 1/2/3 是既有契约：QuestTemplate.giver_template_id 里已经写了它们
--      （Horizon.Game.Sim/NpcTemplateCatalog.cs:37-42 的三行），改号会让存量任务的发布者指向空气。
--   2) 名字 / 头衔 / 模型键 / 五行域必须**逐字等于** NpcTemplateCatalog 里的硬编码值，
--      否则"接入数据库"这件事会顺带改掉头顶名牌与好感记账（域决定 favor 记到哪一支）。
--   3) 三条样板的用途：400001 演示"商人 + 货架 + 作息"（服务型号段），
--      500001 演示"敌对可击杀 NPC"（敌对号段，给经验、会呼叫增援）。
--
-- 口径说明（与内核判据对齐，别在别处另写一份）：
--   * service_flags 的位值由本工程定义：Gossip=1 QuestGiver=2 QuestTurnIn=4 Vendor=8
--     Trainer=16 CraftTrainer=32 CraftStation=64（见 NpcServiceCore.cs）。
--   * reaction：0 友好 1 中立 2 敌对（NpcReactionCore）。
--   * ai_tier：0 站桩 … 7 精英（NpcBehaviorCore.ForTier 的预设表）。
--   * attackable_override：-1 跟立场（**不是 0**；0 表示强制不可打，会把敌对 NPC 变成哑巴）。
--   * stock_max=0 表示不限库存；有上限却没周期（restock_seconds=0）会被装配器报成
--     vendor_stock_never_restocks —— 本脚本不写这种行。
--   * 作息秒数是**周期内秒**，默认一天 1200 秒（NpcScheduleCore.DefaultDayCycleSeconds）；
--     from == to 表示全天兜底档。
--   * 本次不写 NpcQuestLink：它是任务发布时由 QuestCli/发布流程同步生成的倒排索引，
--     手写一份会和任务表打架（这正是"两个真相"的典型来源）。
-- ============================================================================

BEGIN TRANSACTION;

-- ---------- 模板（3 条存量 + 2 条样板） ----------
MERGE INTO [Game_HunduShijie_NpcTemplate] AS t
USING (VALUES
    -- npc_id, name,     title,   model_key,        scale, level, reaction, faction, wuxing, service_flags, ai_tier, alert, gossip_group, rank, publish, IsValid, GameId, AreaId, ServerId, GameUserId
    (1,     N'铸剑师',  N'铁匠', N'npc_smith',      1.00,  60,    0,        0,       1,      121,           2,       20,    1,            0,    1,       1,       0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (2,     N'守门人',  N'城门', N'npc_gatekeeper', 1.00,  65,    0,        0,       5,      7,             6,       24,    2,            0,    1,       1,       0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (3,     N'游方商队',N'行商', N'npc_merchant',   1.00,  55,    0,        0,       2,      9,             3,       20,    3,            0,    1,       1,       0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (400001,N'杂货商',  N'货郎', N'npc_peddler',    1.00,  30,    0,        0,       2,      9,             3,       16,    4,            0,    1,       1,       0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (500001,N'山贼头目',N'山贼', N'npc_bandit_chief',1.10, 32,    2,        0,       2,      0,             7,       18,    2,            1,    1,       1,       0, 0, 0, '00000000-0000-0000-0000-000000000000')
) AS s (npc_id, name, title, model_key, scale, level, reaction, faction_group, wuxing_domain, service_flags, ai_tier, alert_radius_meters, gossip_group_id, rank, publish_state, IsValid, GameId, AreaId, ServerId, GameUserId)
ON t.npc_id = s.npc_id
WHEN MATCHED THEN
    UPDATE SET t.name = s.name, t.title = s.title, t.model_key = s.model_key, t.scale = s.scale,
               t.level = s.level, t.reaction = s.reaction, t.faction_group = s.faction_group,
               t.wuxing_domain = s.wuxing_domain, t.service_flags = s.service_flags, t.ai_tier = s.ai_tier,
               t.alert_radius_meters = s.alert_radius_meters, t.gossip_group_id = s.gossip_group_id,
               t.rank = s.rank, t.publish_state = s.publish_state, t.IsValid = 1, t.content_version = 1
WHEN NOT MATCHED THEN
    INSERT (npc_id, name, title, model_key, prefab_guid, scale, level, level_variance, reaction, faction_group,
            wuxing_domain, service_flags, ai_tier, ai_bits_force_on, ai_bits_force_off, attackable_override,
            speed_walk, speed_run, alert_radius_meters, gossip_group_id, rank, kill_credit_npc_id,
            publish_state, content_version, IsValid, GameId, AreaId, ServerId, GameUserId)
    VALUES (s.npc_id, s.name, s.title, s.model_key, N'', s.scale, s.level, 0, s.reaction, s.faction_group,
            s.wuxing_domain, s.service_flags, s.ai_tier, 0, 0, -1,
            1.6, 4.0, s.alert_radius_meters, s.gossip_group_id, s.rank, 0,
            s.publish_state, 1, s.IsValid, s.GameId, s.AreaId, s.ServerId, s.GameUserId);

-- ---------- 货架（顺序是数据：slot 决定玩家看到的格子） ----------
-- 铁匠卖两种锻造料（精铁 700006 / 玄铁精 700011）；货郎卖一件杂货（青竹 700002）；
-- 行商卖两件外观（91001/91002）。价格全部走"商人覆盖"，
-- 因为 item 模板的 sell_price/buy_price 在种子里是 0（= 内核判作不可成交，不是免费）。
-- 引用的 item_id 必须是**库里真有的行**：016 的 7500xx 特殊物品档尚未执行，
-- 拿它当种子会让自检 #4 报 vendor_item_missing（发得出、点开是空货架）。
SET IDENTITY_INSERT [Game_HunduShijie_NpcVendorEntry] ON;
MERGE INTO [Game_HunduShijie_NpcVendorEntry] AS t
USING (VALUES
    -- entry_id, npc_id, slot, item_id, buy_override, sell_override, stock_max, restock, category
    (1, 1,      0, 700006, 1200, 300, 0, 0,    0),
    (2, 1,      1, 700011, 2400, 600, 0, 0,    0),
    (3, 3,      0, 91001,  500,  100, 3, 3600, 1),
    (4, 3,      1, 91002,  900,  180, 0, 0,    1),
    (5, 400001, 0, 700002, 150,  30,  0, 0,    0)
) AS s (entry_id, npc_id, slot, item_id, buy_price_override, sell_price_override, stock_max, restock_seconds, category)
ON t.entry_id = s.entry_id
WHEN MATCHED THEN
    UPDATE SET t.npc_id = s.npc_id, t.slot = s.slot, t.item_id = s.item_id,
               t.buy_price_override = s.buy_price_override, t.sell_price_override = s.sell_price_override,
               t.stock_max = s.stock_max, t.restock_seconds = s.restock_seconds,
               t.category = s.category, t.enabled = 1, t.IsValid = 1
WHEN NOT MATCHED THEN
    INSERT (entry_id, npc_id, slot, item_id, buy_price_override, sell_price_override, stock_max, restock_seconds, category, enabled, IsValid, GameId, AreaId, ServerId, GameUserId)
    VALUES (s.entry_id, s.npc_id, s.slot, s.item_id, s.buy_price_override, s.sell_price_override, s.stock_max, s.restock_seconds, s.category, 1, 1, 0, 0, 0, N'00000000-0000-0000-0000-000000000000');
SET IDENTITY_INSERT [Game_HunduShijie_NpcVendorEntry] OFF;

-- ---------- 教学（技能条目 + 配方条目；**不带等级门槛**，那由 SkillGrain.CheckLearnGates 判） ----------
-- 铁匠教两式金系技能（1001 裂金斩 / 1002 断岳击，库里实有），外加本脚本产出的配方 900001。
SET IDENTITY_INSERT [Game_HunduShijie_NpcTrainerEntry] ON;
MERGE INTO [Game_HunduShijie_NpcTrainerEntry] AS t
USING (VALUES
    -- entry_id, npc_id, skill_or_recipe_id, service_kind(1技能 2配方), cost_gold, cost_item_id, cost_item_count, seq
    (1, 1, 1001,   1, 200,  0, 0, 0),
    (2, 1, 1002,   1, 500,  0, 0, 1),
    (3, 1, 900001, 2, 800,  0, 0, 2)
) AS s (entry_id, npc_id, skill_id, service_kind, cost_gold, cost_item_id, cost_item_count, seq)
ON t.entry_id = s.entry_id
WHEN MATCHED THEN
    UPDATE SET t.npc_id = s.npc_id, t.skill_id = s.skill_id, t.service_kind = s.service_kind,
               t.cost_gold = s.cost_gold, t.cost_item_id = s.cost_item_id, t.cost_item_count = s.cost_item_count,
               t.seq = s.seq, t.enabled = 1, t.IsValid = 1
WHEN NOT MATCHED THEN
    INSERT (entry_id, npc_id, skill_id, service_kind, cost_gold, cost_item_id, cost_item_count, seq, enabled, IsValid, GameId, AreaId, ServerId, GameUserId)
    VALUES (s.entry_id, s.npc_id, s.skill_id, s.service_kind, s.cost_gold, s.cost_item_id, s.cost_item_count, s.seq, 1, 1, 0, 0, 0, N'00000000-0000-0000-0000-000000000000');
SET IDENTITY_INSERT [Game_HunduShijie_NpcTrainerEntry] OFF;

-- ---------- 配方（产物 = 玄铁重铠 91002；材料 = 玄铁精 700011 ×2；只在铁匠的砧前可做） ----------
SET IDENTITY_INSERT [Game_HunduShijie_NpcCraftRecipe] ON;
MERGE INTO [Game_HunduShijie_NpcCraftRecipe] AS t
USING (VALUES
    -- recipe_id, name, product_item_id, materials_json, cost_gold, station_npc_id, skill_req,
    -- quality_gap_caps_json(橙黄绿灰，由易到难), quality_chance_bps_json, learn_source_kind, publish_state
    (900001, N'锻造·玄铁重铠', 91002, N'[{"item":700011,"count":2}]', 300, 1, 10,
     N'[5,10,15,60]', N'[10000,6000,3000,1000]', 1, 1)
) AS s (recipe_id, name, product_item_id, materials_json, cost_gold, station_npc_id, skill_req, quality_gap_caps_json, quality_chance_bps_json, learn_source_kind, publish_state)
ON t.recipe_id = s.recipe_id
WHEN MATCHED THEN
    UPDATE SET t.name = s.name, t.product_item_id = s.product_item_id, t.materials_json = s.materials_json,
               t.cost_gold = s.cost_gold, t.station_npc_id = s.station_npc_id, t.skill_req = s.skill_req,
               t.quality_gap_caps_json = s.quality_gap_caps_json, t.quality_chance_bps_json = s.quality_chance_bps_json,
               t.learn_source_kind = s.learn_source_kind, t.publish_state = s.publish_state,
               t.IsValid = 1, t.content_version = 1
WHEN NOT MATCHED THEN
    INSERT (recipe_id, name, product_item_id, materials_json, cost_gold, station_npc_id, skill_req,
            quality_gap_caps_json, quality_chance_bps_json, learn_source_kind, publish_state, content_version,
            IsValid, GameId, AreaId, ServerId, GameUserId)
    VALUES (s.recipe_id, s.name, s.product_item_id, s.materials_json, s.cost_gold, s.station_npc_id, s.skill_req,
            s.quality_gap_caps_json, s.quality_chance_bps_json, s.learn_source_kind, s.publish_state, 1,
            1, 0, 0, 0, N'00000000-0000-0000-0000-000000000000');
SET IDENTITY_INSERT [Game_HunduShijie_NpcCraftRecipe] OFF;

SET IDENTITY_INSERT [Game_HunduShijie_NpcRecipeTaught] ON;
MERGE INTO [Game_HunduShijie_NpcRecipeTaught] AS t
USING (VALUES
    (1, 900001, 1, 800)
) AS s (link_id, recipe_id, npc_id, cost_gold)
ON t.link_id = s.link_id
WHEN MATCHED THEN
    UPDATE SET t.recipe_id = s.recipe_id, t.npc_id = s.npc_id, t.cost_gold = s.cost_gold, t.enabled = 1, t.IsValid = 1
WHEN NOT MATCHED THEN
    INSERT (link_id, recipe_id, npc_id, cost_gold, enabled, IsValid, GameId, AreaId, ServerId, GameUserId)
    VALUES (s.link_id, s.recipe_id, s.npc_id, s.cost_gold, 1, 1, 0, 0, 0, N'00000000-0000-0000-0000-000000000000');
SET IDENTITY_INSERT [Game_HunduShijie_NpcRecipeTaught] OFF;

-- ---------- 对话菜单（动作位必须与 service_flags 对得上，装配器会逐条核） ----------
SET IDENTITY_INSERT [Game_HunduShijie_NpcGossipOption] ON;
MERGE INTO [Game_HunduShijie_NpcGossipOption] AS t
USING (VALUES
    -- option_id, npc_id, group_id, seq, text_id, action_kind(0文本 1商店 2训练 3制作 4交任务 5子菜单), action_arg, min_lv, max_lv
    (1, 1, 1, 0, 0, 0, 0, 0, 0),
    (2, 1, 1, 1, 0, 1, 0, 0, 0),
    (3, 1, 1, 2, 0, 3, 1, 0, 0),
    (4, 3, 3, 0, 0, 0, 0, 0, 0),
    (5, 3, 3, 1, 0, 1, 0, 0, 0),
    (6, 400001, 4, 0, 0, 0, 0, 0, 0),
    (7, 400001, 4, 1, 0, 1, 0, 0, 0)
) AS s (option_id, npc_id, group_id, seq, text_id, action_kind, action_arg, req_min_level, req_max_level)
ON t.option_id = s.option_id
WHEN MATCHED THEN
    UPDATE SET t.npc_id = s.npc_id, t.group_id = s.group_id, t.seq = s.seq, t.text_id = s.text_id,
               t.action_kind = s.action_kind, t.action_arg = s.action_arg,
               t.req_min_level = s.req_min_level, t.req_max_level = s.req_max_level, t.enabled = 1, t.IsValid = 1
WHEN NOT MATCHED THEN
    INSERT (option_id, npc_id, group_id, seq, text_id, action_kind, action_arg, req_min_level, req_max_level, enabled, IsValid, GameId, AreaId, ServerId, GameUserId)
    VALUES (s.option_id, s.npc_id, s.group_id, s.seq, s.text_id, s.action_kind, s.action_arg, s.req_min_level, s.req_max_level, 1, 1, 0, 0, 0, N'00000000-0000-0000-0000-000000000000');
SET IDENTITY_INSERT [Game_HunduShijie_NpcGossipOption] OFF;

-- ---------- 作息（周期 1200 秒；铁匠白天干活、夜里收摊；行商全天游走） ----------
SET IDENTITY_INSERT [Game_HunduShijie_NpcScheduleSlot] ON;
MERGE INTO [Game_HunduShijie_NpcScheduleSlot] AS t
USING (VALUES
    -- slot_id, npc_id, seq, from_s, to_s, route_id, posture(0站 1坐 2工作 3持械), gossip_group
    (1, 1,      0, 0,   600,  0, 2, 1),
    (2, 1,      1, 600, 1200, 0, 0, 1),
    (3, 3,      0, 0,   0,    1, 0, 3),
    (4, 400001, 0, 0,   0,    0, 0, 4),
    (5, 2,      0, 0,   0,    0, 3, 2)
) AS s (slot_id, npc_id, seq, from_seconds, to_seconds, route_id, posture, gossip_group_id)
ON t.slot_id = s.slot_id
WHEN MATCHED THEN
    UPDATE SET t.npc_id = s.npc_id, t.seq = s.seq, t.from_seconds = s.from_seconds, t.to_seconds = s.to_seconds,
               t.route_id = s.route_id, t.posture = s.posture, t.gossip_group_id = s.gossip_group_id, t.IsValid = 1
WHEN NOT MATCHED THEN
    INSERT (slot_id, npc_id, seq, from_seconds, to_seconds, route_id, posture, gossip_group_id, IsValid, GameId, AreaId, ServerId, GameUserId)
    VALUES (s.slot_id, s.npc_id, s.seq, s.from_seconds, s.to_seconds, s.route_id, s.posture, s.gossip_group_id, 1, 0, 0, 0, N'00000000-0000-0000-0000-000000000000');
SET IDENTITY_INSERT [Game_HunduShijie_NpcScheduleSlot] OFF;

-- ---------- 敌对 NPC 的掉落 ----------
-- 表与怪物共用：owner 列（monster_id）里放 npc_id，这是 D4 的"扩 owner 而不是另立一套掉落表"。
-- 500001 山贼头目：必掉一件杂货（青竹 700002，60%）、稀有掉一件外观（91001，15%），外加 5~25 铜。
-- 掉落绑定写在怪物那两张表里（D4：扩 owner）。若 019 尚未执行，这两张表还不存在——
-- 那时跳过这一节并打印原因，**不让整份种子失败**：NPC 模板/货架/教学/作息与掉落无依赖关系。
IF OBJECT_ID(N'[Game_HunduShijie_MonsterLootBinding]', N'U') IS NOT NULL
BEGIN
SET IDENTITY_INSERT [Game_HunduShijie_MonsterLootBinding] ON;
MERGE INTO [Game_HunduShijie_MonsterLootBinding] AS t
USING (VALUES
    (9001, 500001, 1, 10000, 1, 1, 0, 0, 5, 25)
) AS s (binding_id, monster_id, loot_table_id, roll_chance_bps, min_count, max_count, condition_kind, quest_id, money_min, money_max)
ON t.binding_id = s.binding_id
WHEN MATCHED THEN
    UPDATE SET t.monster_id = s.monster_id, t.loot_table_id = s.loot_table_id, t.roll_chance_bps = s.roll_chance_bps,
               t.min_count = s.min_count, t.max_count = s.max_count, t.condition_kind = s.condition_kind,
               t.quest_id = s.quest_id, t.money_min = s.money_min, t.money_max = s.money_max, t.IsValid = 1
WHEN NOT MATCHED THEN
    INSERT (binding_id, monster_id, loot_table_id, roll_chance_bps, min_count, max_count, condition_kind, quest_id, money_min, money_max, IsValid, GameId, AreaId, ServerId, GameUserId)
    VALUES (s.binding_id, s.monster_id, s.loot_table_id, s.roll_chance_bps, s.min_count, s.max_count, s.condition_kind, s.quest_id, s.money_min, s.money_max, 1, 0, 0, 0, N'00000000-0000-0000-0000-000000000000');
SET IDENTITY_INSERT [Game_HunduShijie_MonsterLootBinding] OFF;

SET IDENTITY_INSERT [Game_HunduShijie_MonsterDropEntry] ON;
MERGE INTO [Game_HunduShijie_MonsterDropEntry] AS t
USING (VALUES
    (9001, 9001, 700002, 6000, 1, 2, 0, 0, -1, 0),
    (9002, 9001, 91001,  1500, 1, 1, 1, 0, -1, 0)
) AS s (entry_id, binding_id, item_id, chance_bps, count_min, count_max, seq, group_id, condition_kind, quest_id)
ON t.entry_id = s.entry_id
WHEN MATCHED THEN
    UPDATE SET t.binding_id = s.binding_id, t.item_id = s.item_id, t.chance_bps = s.chance_bps,
               t.count_min = s.count_min, t.count_max = s.count_max, t.seq = s.seq, t.group_id = s.group_id,
               t.condition_kind = s.condition_kind, t.quest_id = s.quest_id, t.IsValid = 1
WHEN NOT MATCHED THEN
    INSERT (entry_id, binding_id, item_id, chance_bps, count_min, count_max, seq, group_id, condition_kind, quest_id, IsValid, GameId, AreaId, ServerId, GameUserId)
    VALUES (s.entry_id, s.binding_id, s.item_id, s.chance_bps, s.count_min, s.count_max, s.seq, s.group_id, s.condition_kind, s.quest_id, 1, 0, 0, 0, N'00000000-0000-0000-0000-000000000000');
SET IDENTITY_INSERT [Game_HunduShijie_MonsterDropEntry] OFF;
END
ELSE
    PRINT N'NPC 掉落种子跳过：怪物掉落表不存在（019 未执行）。NPC 模板/货架/教学/作息已照常种入。';

-- ---------- 刷点（开发世界原点附近；坐标是 ECS Z-up：x/y 水平面，z 是高度） ----------
-- 位置只给"玩家出生点附近一圈"，让 PIE 一进世界就能看见；运营用的真实点位由编辑器另配。
-- 敌对 NPC 500001 放在稍远处（18, -6），避免一进世界就被山贼贴上。
SET IDENTITY_INSERT [Game_HunduShijie_NpcSpawnPoint] ON;
MERGE INTO [Game_HunduShijie_NpcSpawnPoint] AS t
USING (VALUES
    -- spawn_point_id, npc_id, x,    y,   z, yaw, route_id, facing_mode, max_alive, respawn, level_override
    (1, 1,      6.0,  0.0, 0.0, 180.0, 0, 0, 1, 60.0, 0),
    (2, 2,     10.0,  2.0, 0.0, 200.0, 0, 0, 1, 60.0, 0),
    (3, 3,      8.0, -2.0, 0.0, 0.0,   1, 0, 1, 60.0, 0),
    (4, 400001,12.0,  4.0, 0.0, 220.0, 0, 0, 1, 60.0, 0),
    (5, 500001,18.0, -6.0, 0.0, 0.0,   0, 0, 1, 30.0, 0)
) AS s (spawn_point_id, npc_id, x, y, z, yaw, route_id, facing_mode, max_alive, respawn_seconds, level_override)
ON t.spawn_point_id = s.spawn_point_id
WHEN MATCHED THEN
    UPDATE SET t.npc_id = s.npc_id, t.x = s.x, t.y = s.y, t.z = s.z, t.yaw = s.yaw,
               t.route_id = s.route_id, t.facing_mode = s.facing_mode, t.max_alive = s.max_alive,
               t.respawn_seconds = s.respawn_seconds, t.level_override = s.level_override,
               t.enabled = 1, t.publish_state = 1, t.IsValid = 1
WHEN NOT MATCHED THEN
    INSERT (spawn_point_id, zone_id, shard_id, x, y, z, yaw, npc_id, level_override,
            max_alive, respawn_seconds, route_id, facing_mode, enabled, publish_state, content_version,
            IsValid, GameId, AreaId, ServerId, GameUserId)
    VALUES (s.spawn_point_id, 0, 0, s.x, s.y, s.z, s.yaw, s.npc_id, s.level_override,
            s.max_alive, s.respawn_seconds, s.route_id, s.facing_mode, 1, 1, 1,
            1, 0, 0, 0, N'00000000-0000-0000-0000-000000000000');
SET IDENTITY_INSERT [Game_HunduShijie_NpcSpawnPoint] OFF;

-- ---------- 路线（行商"全天游走"走这条；循环四点 = 摊位四周的方框） ----------
SET IDENTITY_INSERT [Game_HunduShijie_NpcWaypointPath] ON;
MERGE INTO [Game_HunduShijie_NpcWaypointPath] AS t
USING (VALUES
    -- path_id, name,        mode(0往返 1循环 2随机), pause_at_point, use_navmesh, point_count, publish
    (1, N'行商巡街路线', 1, 2.0, 0, 4, 1)
) AS s (path_id, name, mode, pause_at_point_seconds, use_navmesh, point_count, publish_state)
ON t.path_id = s.path_id
WHEN MATCHED THEN
    UPDATE SET t.name = s.name, t.mode = s.mode, t.pause_at_point_seconds = s.pause_at_point_seconds,
               t.use_navmesh = s.use_navmesh, t.point_count = s.point_count,
               t.publish_state = s.publish_state, t.IsValid = 1
WHEN NOT MATCHED THEN
    INSERT (path_id, name, mode, pause_at_point_seconds, use_navmesh, point_count, publish_state,
            IsValid, GameId, AreaId, ServerId, GameUserId)
    VALUES (s.path_id, s.name, s.mode, s.pause_at_point_seconds, s.use_navmesh, s.point_count, s.publish_state,
            1, 0, 0, 0, N'00000000-0000-0000-0000-000000000000');
SET IDENTITY_INSERT [Game_HunduShijie_NpcWaypointPath] OFF;

SET IDENTITY_INSERT [Game_HunduShijie_NpcWaypointNode] ON;
MERGE INTO [Game_HunduShijie_NpcWaypointNode] AS t
USING (VALUES
    -- node_id, path_id, seq, x,    y,    z, pause
    (1, 1, 0,  8.0, -5.0, 0.0, 0.0),
    (2, 1, 1, 13.0, -5.0, 0.0, 0.0),
    (3, 1, 2, 13.0,  2.0, 0.0, 0.0),
    (4, 1, 3,  8.0,  2.0, 0.0, 0.0)
) AS s (node_id, path_id, seq, x, y, z, pause_seconds)
ON t.node_id = s.node_id
WHEN MATCHED THEN
    UPDATE SET t.path_id = s.path_id, t.seq = s.seq, t.x = s.x, t.y = s.y, t.z = s.z,
               t.pause_seconds = s.pause_seconds, t.IsValid = 1
WHEN NOT MATCHED THEN
    INSERT (node_id, path_id, seq, x, y, z, pause_seconds, IsValid, GameId, AreaId, ServerId, GameUserId)
    VALUES (s.node_id, s.path_id, s.seq, s.x, s.y, s.z, s.pause_seconds, 1, 0, 0, 0, N'00000000-0000-0000-0000-000000000000');
SET IDENTITY_INSERT [Game_HunduShijie_NpcWaypointNode] OFF;

COMMIT;

-- ============================================================================
-- 自检（跑完立刻执行；任何一条对不上就说明种子被改坏了，别继续导内容）
-- ============================================================================

-- 1) 模板行数 = 5，且三条存量行仍在（号段契约的最低要求）。
SELECT COUNT(*) AS npc_rows,
       CASE WHEN COUNT(*) = 5 THEN 'OK' ELSE 'FAIL: 期望 5 行' END AS check_count
FROM [Game_HunduShijie_NpcTemplate] WHERE IsValid = 1;

-- 2) 三条存量 NPC 的 名字/头衔/模型键/五行域 必须与 NpcTemplateCatalog.cs:39-41 逐字相同。
--    （迁移期间两处共存，这是唯一的对齐证据；域错了会让好感记到别的五行支。）
SELECT npc_id, name, title, model_key, wuxing_domain
FROM [Game_HunduShijie_NpcTemplate]
WHERE npc_id IN (1,2,3)
ORDER BY npc_id;
-- 期望：
--   1 | 铸剑师   | 铁匠 | npc_smith      | 1
--   2 | 守门人   | 城门 | npc_gatekeeper | 5
--   3 | 游方商队 | 行商 | npc_merchant   | 2

-- 3) 任何一行货架/教学/对话动作，其 NPC 必须存在且已发布，且位图必须声明对应服务。
--    返回行数必须为 0。
SELECT 'vendor_missing_bit' AS problem, v.entry_id AS row_id
FROM [Game_HunduShijie_NpcVendorEntry] v
JOIN [Game_HunduShijie_NpcTemplate] t ON t.npc_id = v.npc_id
WHERE v.enabled = 1 AND (t.publish_state <> 1 OR (t.service_flags & 8) = 0)
UNION ALL
SELECT 'trainer_missing_bit', r.entry_id
FROM [Game_HunduShijie_NpcTrainerEntry] r
JOIN [Game_HunduShijie_NpcTemplate] t ON t.npc_id = r.npc_id
WHERE r.enabled = 1 AND (t.publish_state <> 1 OR (t.service_flags & 48) = 0)
UNION ALL
-- 动作 1=商店→Vendor(8)、2=训练→Trainer(16)、3=制作→CraftStation(64)、4=交任务→QuestTurnIn(4)
SELECT 'gossip_action_bit_off', g.option_id
FROM [Game_HunduShijie_NpcGossipOption] g
JOIN [Game_HunduShijie_NpcTemplate] t ON t.npc_id = g.npc_id
WHERE g.enabled = 1 AND (
        (g.action_kind = 1 AND (t.service_flags & 8)  = 0) OR
        (g.action_kind = 2 AND (t.service_flags & 16) = 0) OR
        (g.action_kind = 3 AND (t.service_flags & 64) = 0) OR
        (g.action_kind = 4 AND (t.service_flags & 4)  = 0));

-- 4) 货架库存口径：有上限却没有补货周期的行（永远补不了货）必须为 0 行；
--    引用不存在的 item 模板的行也必须为 0 行。
SELECT 'stock_never_restocks' AS problem, entry_id AS row_id
FROM [Game_HunduShijie_NpcVendorEntry]
WHERE enabled = 1 AND stock_max > 0 AND restock_seconds <= 0
UNION ALL
SELECT 'vendor_item_missing', v.entry_id
FROM [Game_HunduShijie_NpcVendorEntry] v
LEFT JOIN [Game_HunduShijie_ItemTemplate] i ON i.item_id = v.item_id
WHERE v.enabled = 1 AND i.item_id IS NULL;

-- 5) 教学引用的技能/配方必须存在（返回 0 行）。
--    注意括号：`A AND B OR C AND D` 会被 SQL 解析成 `(A AND B) OR (C AND D)`，
--    恰好不是"技能类查技能表、配方类查配方表"的意思。
SELECT 'trainer_skill_missing' AS problem, r.entry_id AS row_id
FROM [Game_HunduShijie_NpcTrainerEntry] r
LEFT JOIN [Game_HunduShijie_SkillTemplate] s ON s.skill_id = r.skill_id
WHERE r.enabled = 1 AND r.service_kind = 1 AND s.skill_id IS NULL
UNION ALL
SELECT 'trainer_recipe_missing', r.entry_id
FROM [Game_HunduShijie_NpcTrainerEntry] r
LEFT JOIN [Game_HunduShijie_NpcCraftRecipe] c ON c.recipe_id = r.skill_id
WHERE r.enabled = 1 AND r.service_kind = 2 AND c.recipe_id IS NULL;

-- 6) 敌对 NPC 的掉落绑定必须挂在存在的 NPC 上（返回 0 行）。
--    怪物掉落表不存在时如实报"未检查"，别让缺失的表把这条检查静默吞掉。
IF OBJECT_ID(N'[Game_HunduShijie_MonsterLootBinding]', N'U') IS NOT NULL
BEGIN
    SELECT 'loot_binding_unknown_npc' AS problem, b.binding_id AS row_id
    FROM [Game_HunduShijie_MonsterLootBinding] b
    LEFT JOIN [Game_HunduShijie_NpcTemplate] t ON t.npc_id = b.monster_id
    WHERE b.IsValid = 1 AND b.monster_id >= 400000 AND t.npc_id IS NULL
    UNION ALL
    -- 掉落引用的物品也必须实有（只查 NPC 号段 owner 的绑定，不替怪物侧数据背书）。
    SELECT 'loot_item_missing', e.entry_id
    FROM [Game_HunduShijie_MonsterDropEntry] e
    JOIN [Game_HunduShijie_MonsterLootBinding] b ON b.binding_id = e.binding_id
    LEFT JOIN [Game_HunduShijie_ItemTemplate] i ON i.item_id = e.item_id
    WHERE e.IsValid = 1 AND b.monster_id >= 400000 AND i.item_id IS NULL;
END
ELSE
    SELECT 'SKIPPED_loot_tables_missing' AS problem, 0 AS row_id;

-- 7) 刷点必须挂在已发布 NPC 上；带路线的刷点其路线必须存在，且 point_count 要与节点数一致。
--    （point_count 是冗余列：写错不会报错，只会让进度显示与实际路线不符。）
SELECT 'spawn_unknown_npc' AS problem, sp.spawn_point_id AS row_id
FROM [Game_HunduShijie_NpcSpawnPoint] sp
LEFT JOIN [Game_HunduShijie_NpcTemplate] t ON t.npc_id = sp.npc_id AND t.publish_state = 1
WHERE sp.enabled = 1 AND t.npc_id IS NULL
UNION ALL
SELECT 'spawn_route_missing', sp.spawn_point_id
FROM [Game_HunduShijie_NpcSpawnPoint] sp
LEFT JOIN [Game_HunduShijie_NpcWaypointPath] p ON p.path_id = sp.route_id AND p.publish_state = 1
WHERE sp.enabled = 1 AND sp.route_id > 0 AND p.path_id IS NULL
UNION ALL
SELECT 'spawn_route_too_short', sp.spawn_point_id
FROM [Game_HunduShijie_NpcSpawnPoint] sp
WHERE sp.enabled = 1 AND sp.route_id > 0
  AND (SELECT COUNT(*) FROM [Game_HunduShijie_NpcWaypointNode] n
       WHERE n.path_id = sp.route_id AND n.IsValid = 1) < 2
UNION ALL
SELECT 'route_point_count_mismatch', p.path_id
FROM [Game_HunduShijie_NpcWaypointPath] p
WHERE p.publish_state = 1
  AND p.point_count <> (SELECT COUNT(*) FROM [Game_HunduShijie_NpcWaypointNode] n
                        WHERE n.path_id = p.path_id AND n.IsValid = 1);
