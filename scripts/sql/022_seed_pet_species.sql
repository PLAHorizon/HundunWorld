-- ============================================================================
-- 022_seed_pet_species.sql —— 宠物编辑器：把硬编码回落目录的 5 个物种搬进表
--
-- 本脚本**只生成、不执行**（迁移与写库由用户点头后再跑）。
-- 执行前提：021_pet_editor_tables.sql 已建表。
--
-- 数值出处（逐项可对账，不是凭记忆写的）：
--   物种号/名称/携带段/model_key/出身 = Horizon.Game.Sim/Pets/PetContent.cs 的 PetSpeciesCatalog.Rows
--   资质区间 [900,1500] = 同文件 Build()（硬编码回落目录）
--   槽位/技能/成长/捕捉/默契的默认值 = 各 core 的 Default（PetSlotRuleConfig/PetFollowConfig/PetBondValues）
--   行为档位：Divine→通灵(4) 其余→训练有素(2)，同 Build() 里 IntellectTier 的写法
--
-- 为什么种子必须逐字等于今天运行时的值：
--   "接入数据库"和"顺手改平衡"必须分开发生。否则上线后表现为"宠变弱了"，
--   没有人会把它归因到本脚本。
--
-- 与怪物侧 020 的一处**有意的**不同：每一行都显式给出全部 NOT NULL 列（含平台基列）。
--   019/020 的组合不给基列默认值却只插 6 个业务列，那种写法一执行就会整条失败；
--   本脚本不复制这个缺陷。
-- ============================================================================

BEGIN TRANSACTION;

MERGE INTO [Game_HunduShijie_PetTemplate] AS t
USING (VALUES
    (500001, N'墨麟幼崽', N'pet_mo_qilin_cub', NULL, NULL, 1.00, N'Idle', N'base', 0, 25, '1', N'0,1,2', 0, 0, 10, 5, 0, 1000, 1, 3000, 0, 50, 1, 1, N'与 PetSpeciesCatalog 硬编码回落目录逐字对齐', 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (500002, N'山鬼', N'pet_shan_gui', NULL, NULL, 1.00, N'Idle', N'base', 15, 40, '0', N'0,1,2', 0, 0, 10, 5, 0, 1000, 1, 3000, 0, 50, 1, 1, N'与 PetSpeciesCatalog 硬编码回落目录逐字对齐', 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (500003, N'赤瞳狼', N'pet_red_wolf', NULL, NULL, 1.00, N'Idle', N'base', 25, 50, '0', N'0,1,2', 0, 0, 10, 5, 0, 1000, 1, 3000, 0, 50, 1, 1, N'与 PetSpeciesCatalog 硬编码回落目录逐字对齐', 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (600001, N'变异·赤瞳狼', N'pet_red_wolf_mutant', NULL, NULL, 1.00, N'Idle', N'base', 25, 50, '3', N'0,1,2', 0, 0, 10, 5, 0, 1000, 1, 3000, 0, 0, 1, 1, N'与 PetSpeciesCatalog 硬编码回落目录逐字对齐', 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (650001, N'混沌遗蜕·守灵', N'pet_hundun_warden', NULL, NULL, 1.00, N'Idle', N'base', 175, 175, '5', N'0,1,2', 0, 0, 10, 5, 1, 1000, 4, 0, 0, 50, 1, 1, N'与 PetSpeciesCatalog 硬编码回落目录逐字对齐', 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000')
) AS s(species_id, name, model_key, prefab_path, prefab_guid, model_scale, stand_state, base_color_key, carry_level_min, carry_level_max, allowed_origins, allowed_growth_tiers, hatch_seconds, hatch_level, owner_level_slack, potential_per_level, immortal_lifespan, happiness_scale, capture_mode, capture_base_bps, capture_guidance_seconds, mutation_chance_bps, publish_state, content_version, remark, IsValid, GameId, AreaId, ServerId, GameUserId)
ON t.species_id = s.species_id
WHEN NOT MATCHED THEN INSERT ([species_id], [name], [model_key], [prefab_path], [prefab_guid], [model_scale], [stand_state], [base_color_key], [carry_level_min], [carry_level_max], [allowed_origins], [allowed_growth_tiers], [hatch_seconds], [hatch_level], [owner_level_slack], [potential_per_level], [immortal_lifespan], [happiness_scale], [capture_mode], [capture_base_bps], [capture_guidance_seconds], [mutation_chance_bps], [publish_state], [content_version], [remark], [IsValid], [GameId], [AreaId], [ServerId], [GameUserId]) VALUES (s.[species_id], s.[name], s.[model_key], s.[prefab_path], s.[prefab_guid], s.[model_scale], s.[stand_state], s.[base_color_key], s.[carry_level_min], s.[carry_level_max], s.[allowed_origins], s.[allowed_growth_tiers], s.[hatch_seconds], s.[hatch_level], s.[owner_level_slack], s.[potential_per_level], s.[immortal_lifespan], s.[happiness_scale], s.[capture_mode], s.[capture_base_bps], s.[capture_guidance_seconds], s.[mutation_chance_bps], s.[publish_state], s.[content_version], s.[remark], s.[IsValid], s.[GameId], s.[AreaId], s.[ServerId], s.[GameUserId])
WHEN MATCHED THEN UPDATE SET t.[name] = s.[name], t.[model_key] = s.[model_key], t.[carry_level_min] = s.[carry_level_min]
;

MERGE INTO [Game_HunduShijie_PetStatBand] AS t
USING (VALUES
    (500001, 1000, 1500, 1000, 1500, 1000, 1500, 1000, 1500, 900, 1500, 10000, 12000, 10, 10, 0, 8, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (500002, 1000, 1500, 1000, 1500, 1000, 1500, 1000, 1500, 900, 1500, 10000, 12000, 10, 10, 0, 8, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (500003, 1000, 1500, 1000, 1500, 1000, 1500, 1000, 1500, 900, 1500, 10000, 12000, 10, 10, 0, 8, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (600001, 1000, 1500, 1000, 1500, 1000, 1500, 1000, 1500, 900, 1500, 10000, 12000, 10, 10, 0, 8, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (650001, 1000, 1500, 1000, 1500, 1000, 1500, 1000, 1500, 900, 1500, 10000, 12000, 0, 0, 0, 8, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000')
) AS s(species_id, attack_min, attack_max, defense_min, defense_max, stamina_min, stamina_max, mana_min, mana_max, speed_min, speed_max, growth_min_bps, growth_max_bps, comprehension_max, spirit_max, super_spirit, mutation_tier_max, IsValid, GameId, AreaId, ServerId, GameUserId)
ON t.species_id = s.species_id
WHEN NOT MATCHED THEN INSERT ([species_id], [attack_min], [attack_max], [defense_min], [defense_max], [stamina_min], [stamina_max], [mana_min], [mana_max], [speed_min], [speed_max], [growth_min_bps], [growth_max_bps], [comprehension_max], [spirit_max], [super_spirit], [mutation_tier_max], [IsValid], [GameId], [AreaId], [ServerId], [GameUserId]) VALUES (s.[species_id], s.[attack_min], s.[attack_max], s.[defense_min], s.[defense_max], s.[stamina_min], s.[stamina_max], s.[mana_min], s.[mana_max], s.[speed_min], s.[speed_max], s.[growth_min_bps], s.[growth_max_bps], s.[comprehension_max], s.[spirit_max], s.[super_spirit], s.[mutation_tier_max], s.[IsValid], s.[GameId], s.[AreaId], s.[ServerId], s.[GameUserId])
;

MERGE INTO [Game_HunduShijie_PetSkillRule] AS t
USING (VALUES
    (500001, 4, 8, N'', 4, 7, 125, 3, 1500, 7000, 2, 6, 3, 1, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (500002, 4, 8, N'', 4, 7, 125, 3, 1500, 7000, 2, 6, 3, 1, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (500003, 4, 8, N'', 4, 7, 125, 3, 1500, 7000, 2, 6, 3, 1, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (600001, 4, 8, N'', 4, 7, 125, 3, 1500, 7000, 2, 6, 3, 1, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (650001, 4, 8, N'', 4, 7, 125, 3, 1500, 7000, 2, 6, 3, 1, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000')
) AS s(species_id, base_slots, absolute_max_slots, slots_by_grade, comprehension_unlock_a, comprehension_unlock_b, slot_grow_max_level, slot_grow_max_existing, slot_grow_chance_bps, overwrite_chance_bps, max_lockable_slots, learned_pool_max, equipped_max, inner_core_slots, IsValid, GameId, AreaId, ServerId, GameUserId)
ON t.species_id = s.species_id
WHEN NOT MATCHED THEN INSERT ([species_id], [base_slots], [absolute_max_slots], [slots_by_grade], [comprehension_unlock_a], [comprehension_unlock_b], [slot_grow_max_level], [slot_grow_max_existing], [slot_grow_chance_bps], [overwrite_chance_bps], [max_lockable_slots], [learned_pool_max], [equipped_max], [inner_core_slots], [IsValid], [GameId], [AreaId], [ServerId], [GameUserId]) VALUES (s.[species_id], s.[base_slots], s.[absolute_max_slots], s.[slots_by_grade], s.[comprehension_unlock_a], s.[comprehension_unlock_b], s.[slot_grow_max_level], s.[slot_grow_max_existing], s.[slot_grow_chance_bps], s.[overwrite_chance_bps], s.[max_lockable_slots], s.[learned_pool_max], s.[equipped_max], s.[inner_core_slots], s.[IsValid], s.[GameId], s.[AreaId], s.[ServerId], s.[GameUserId])
;

MERGE INTO [Game_HunduShijie_PetBehavior] AS t
USING (VALUES
    (500001, 2, NULL, NULL, NULL, NULL, NULL, 3, 1, N'0,3,4', 1000, 3, 12, 40, 10, 5.5, 0.6, 0, 4, 0, 300, 100, 700, 100, 0, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (500002, 2, NULL, NULL, NULL, NULL, NULL, 3, 1, N'0,3,4', 1000, 3, 12, 40, 10, 5.5, 0.6, 0, 4, 0, 300, 100, 700, 100, 0, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (500003, 2, NULL, NULL, NULL, NULL, NULL, 3, 1, N'0,3,4', 1000, 3, 12, 40, 10, 5.5, 0.6, 0, 4, 0, 300, 100, 700, 100, 0, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (600001, 2, NULL, NULL, NULL, NULL, NULL, 3, 1, N'0,3,4', 1000, 3, 12, 40, 10, 5.5, 0.6, 0, 4, 0, 300, 100, 700, 100, 0, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (650001, 4, NULL, NULL, NULL, NULL, NULL, 3, 1, N'0,3,4', 1000, 3, 12, 40, 10, 5.5, 0.6, 0, 4, 0, 300, 100, 700, 100, 0, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000')
) AS s(species_id, intellect_tier, flag_use_acquire, flag_use_autocast, flag_use_evade, flag_use_guard, flag_use_hold, default_stance, default_command, allowed_personalities, epiphany_chance_bps, follow_distance, leash_radius, teleport_beyond_radius, repath_interval_ticks, follow_speed, arrive_epsilon, corpse_seconds, revive_cast_seconds, summon_cooldown_seconds, follow_start_delay_ms, initial_loyalty, initial_happiness, initial_lifespan, initial_intimacy, IsValid, GameId, AreaId, ServerId, GameUserId)
ON t.species_id = s.species_id
WHEN NOT MATCHED THEN INSERT ([species_id], [intellect_tier], [flag_use_acquire], [flag_use_autocast], [flag_use_evade], [flag_use_guard], [flag_use_hold], [default_stance], [default_command], [allowed_personalities], [epiphany_chance_bps], [follow_distance], [leash_radius], [teleport_beyond_radius], [repath_interval_ticks], [follow_speed], [arrive_epsilon], [corpse_seconds], [revive_cast_seconds], [summon_cooldown_seconds], [follow_start_delay_ms], [initial_loyalty], [initial_happiness], [initial_lifespan], [initial_intimacy], [IsValid], [GameId], [AreaId], [ServerId], [GameUserId]) VALUES (s.[species_id], s.[intellect_tier], s.[flag_use_acquire], s.[flag_use_autocast], s.[flag_use_evade], s.[flag_use_guard], s.[flag_use_hold], s.[default_stance], s.[default_command], s.[allowed_personalities], s.[epiphany_chance_bps], s.[follow_distance], s.[leash_radius], s.[teleport_beyond_radius], s.[repath_interval_ticks], s.[follow_speed], s.[arrive_epsilon], s.[corpse_seconds], s.[revive_cast_seconds], s.[summon_cooldown_seconds], s.[follow_start_delay_ms], s.[initial_loyalty], s.[initial_happiness], s.[initial_lifespan], s.[initial_intimacy], s.[IsValid], s.[GameId], s.[AreaId], s.[ServerId], s.[GameUserId])
;

MERGE INTO [Game_HunduShijie_PetEquipRule] AS t
USING (VALUES
    (500001, N'0,1,2', 1, 5, 1, 0, 0, 3, 1, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (500002, N'0,1,2', 1, 5, 1, 0, 0, 3, 1, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (500003, N'0,1,2', 1, 5, 1, 0, 0, 3, 1, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (600001, N'0,1,2', 1, 5, 1, 0, 0, 3, 1, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (650001, N'0,1,2,3,4,5', 1, 7, 1, 1, 1, 3, 1, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000')
) AS s(species_id, allowed_slots, gem_allowed, max_star, set_family, appraisal_enabled, awakening_enabled, awakening_piece_count, ownership, IsValid, GameId, AreaId, ServerId, GameUserId)
ON t.species_id = s.species_id
WHEN NOT MATCHED THEN INSERT ([species_id], [allowed_slots], [gem_allowed], [max_star], [set_family], [appraisal_enabled], [awakening_enabled], [awakening_piece_count], [ownership], [IsValid], [GameId], [AreaId], [ServerId], [GameUserId]) VALUES (s.[species_id], s.[allowed_slots], s.[gem_allowed], s.[max_star], s.[set_family], s.[appraisal_enabled], s.[awakening_enabled], s.[awakening_piece_count], s.[ownership], s.[IsValid], s.[GameId], s.[AreaId], s.[ServerId], s.[GameUserId])
;

MERGE INTO [Game_HunduShijie_PetMergeRule] AS t
USING (VALUES
    (500001, 1, 1, 30, 6000, 7000, 11000, 9600, 10200, 8, 1, 5, 100, 1000, 2, 172800, 2, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (500002, 1, 1, 30, 6000, 7000, 11000, 9600, 10200, 8, 1, 5, 100, 1000, 2, 172800, 2, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (500003, 1, 1, 30, 6000, 7000, 11000, 9600, 10200, 8, 1, 5, 100, 1000, 2, 172800, 2, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (600001, 0, 0, 30, 6000, 7000, 11000, 9600, 10200, 8, 1, 5, 100, 1000, 2, 172800, 2, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (650001, 0, 0, 30, 6000, 7000, 11000, 9600, 10200, 8, 1, 5, 100, 1000, 2, 172800, 2, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000')
) AS s(species_id, fuse_enabled, breed_enabled, fuse_min_level, fuse_success_bps, fuse_quality_min_bps, fuse_quality_max_bps, fuse_growth_min_bps, fuse_growth_max_bps, fuse_max_inherited_skills, fuse_consumes_parents, breed_max_level_gap, breed_required_happiness, breed_required_lifespan, breed_offspring_count, breed_claim_window_seconds, breed_cost, IsValid, GameId, AreaId, ServerId, GameUserId)
ON t.species_id = s.species_id
WHEN NOT MATCHED THEN INSERT ([species_id], [fuse_enabled], [breed_enabled], [fuse_min_level], [fuse_success_bps], [fuse_quality_min_bps], [fuse_quality_max_bps], [fuse_growth_min_bps], [fuse_growth_max_bps], [fuse_max_inherited_skills], [fuse_consumes_parents], [breed_max_level_gap], [breed_required_happiness], [breed_required_lifespan], [breed_offspring_count], [breed_claim_window_seconds], [breed_cost], [IsValid], [GameId], [AreaId], [ServerId], [GameUserId]) VALUES (s.[species_id], s.[fuse_enabled], s.[breed_enabled], s.[fuse_min_level], s.[fuse_success_bps], s.[fuse_quality_min_bps], s.[fuse_quality_max_bps], s.[fuse_growth_min_bps], s.[fuse_growth_max_bps], s.[fuse_max_inherited_skills], s.[fuse_consumes_parents], s.[breed_max_level_gap], s.[breed_required_happiness], s.[breed_required_lifespan], s.[breed_offspring_count], s.[breed_claim_window_seconds], s.[breed_cost], s.[IsValid], s.[GameId], s.[AreaId], s.[ServerId], s.[GameUserId])
;

SET IDENTITY_INSERT [Game_HunduShijie_PetAssistRule] ON;

MERGE INTO [Game_HunduShijie_PetAssistRule] AS t
USING (VALUES
    (1, 500001, 800901, 0, 1, 0, 0, 1, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (2, 500001, 800913, 7, 1, 0, 0, 2, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (3, 500002, 800901, 0, 1, 0, 0, 3, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (4, 500002, 800913, 7, 1, 0, 0, 4, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (5, 500003, 800901, 0, 1, 0, 0, 5, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (6, 500003, 800913, 7, 1, 0, 0, 6, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (7, 600001, 800901, 0, 1, 0, 0, 7, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (8, 600001, 800913, 7, 1, 0, 0, 8, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (9, 650001, 800901, 0, 1, 0, 0, 9, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (10, 650001, 800913, 7, 1, 0, 0, 10, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000')
) AS s(entry_id, species_id, skill_id, assist_kind, requires_battle, possessable, coefficient_bps, seq, IsValid, GameId, AreaId, ServerId, GameUserId)
ON t.entry_id = s.entry_id
WHEN NOT MATCHED THEN INSERT ([entry_id], [species_id], [skill_id], [assist_kind], [requires_battle], [possessable], [coefficient_bps], [seq], [IsValid], [GameId], [AreaId], [ServerId], [GameUserId]) VALUES (s.[entry_id], s.[species_id], s.[skill_id], s.[assist_kind], s.[requires_battle], s.[possessable], s.[coefficient_bps], s.[seq], s.[IsValid], s.[GameId], s.[AreaId], s.[ServerId], s.[GameUserId])
;
SET IDENTITY_INSERT [Game_HunduShijie_PetAssistRule] OFF;

SET IDENTITY_INSERT [Game_HunduShijie_PetMutationRule] ON;

MERGE INTO [Game_HunduShijie_PetMutationRule] AS t
USING (VALUES
    (1, 500001, N'dye_common', 36000, 10500, N'gold', 0, 1, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (2, 500002, N'dye_common', 36000, 10500, N'gold', 0, 1, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (3, 500003, N'dye_common', 36000, 10500, N'gold', 0, 1, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (4, 600001, N'dye_common', 36000, 10500, N'gold', 0, 1, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000')
) AS s(entry_id, species_id, mutation_key, duration_ticks, quality_cap_bps, color_override, extra_skill_slots, enabled, IsValid, GameId, AreaId, ServerId, GameUserId)
ON t.entry_id = s.entry_id
WHEN NOT MATCHED THEN INSERT ([entry_id], [species_id], [mutation_key], [duration_ticks], [quality_cap_bps], [color_override], [extra_skill_slots], [enabled], [IsValid], [GameId], [AreaId], [ServerId], [GameUserId]) VALUES (s.[entry_id], s.[species_id], s.[mutation_key], s.[duration_ticks], s.[quality_cap_bps], s.[color_override], s.[extra_skill_slots], s.[enabled], s.[IsValid], s.[GameId], s.[AreaId], s.[ServerId], s.[GameUserId])
;
SET IDENTITY_INSERT [Game_HunduShijie_PetMutationRule] OFF;

SET IDENTITY_INSERT [Game_HunduShijie_PetSkillPool] ON;

MERGE INTO [Game_HunduShijie_PetSkillPool] AS t
USING (VALUES
    (1, 500001, 800901, 1, 0, 0, N'', 0, 1, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (2, 500001, 800903, 0, -1, 0, N'', 0, 2, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (3, 500001, 800913, 2, -1, 0, N'', 0, 3, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (4, 500002, 800901, 1, 0, 0, N'', 0, 4, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (5, 500002, 800903, 0, -1, 0, N'', 0, 5, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (6, 500002, 800913, 2, -1, 0, N'', 0, 6, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (7, 500003, 800901, 1, 0, 0, N'', 0, 7, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (8, 500003, 800903, 0, -1, 0, N'', 0, 8, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (9, 500003, 800913, 2, -1, 0, N'', 0, 9, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (10, 600001, 800901, 1, 0, 0, N'', 0, 10, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (11, 600001, 800903, 0, -1, 0, N'', 0, 11, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (12, 600001, 800913, 2, -1, 0, N'', 0, 12, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (13, 650001, 800901, 1, 0, 0, N'', 0, 13, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (14, 650001, 800903, 0, -1, 0, N'', 0, 14, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'),
    (15, 650001, 800913, 2, -1, 0, N'', 0, 15, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000')
) AS s(entry_id, species_id, skill_id, kind, innate_slot, min_level, conflicts, advanced, seq, IsValid, GameId, AreaId, ServerId, GameUserId)
ON t.entry_id = s.entry_id
WHEN NOT MATCHED THEN INSERT ([entry_id], [species_id], [skill_id], [kind], [innate_slot], [min_level], [conflicts], [advanced], [seq], [IsValid], [GameId], [AreaId], [ServerId], [GameUserId]) VALUES (s.[entry_id], s.[species_id], s.[skill_id], s.[kind], s.[innate_slot], s.[min_level], s.[conflicts], s.[advanced], s.[seq], s.[IsValid], s.[GameId], s.[AreaId], s.[ServerId], s.[GameUserId])
;
SET IDENTITY_INSERT [Game_HunduShijie_PetSkillPool] OFF;

COMMIT;
GO

-- ---------- 自检（跑完本脚本后立刻执行；任何一条对不上就说明种子被改坏了） ----------
-- 1) 物种行数必须是 5（与 PetSpeciesCatalog.Rows 的条目数同构），且号段全部合法。
SELECT COUNT(*) AS template_rows,
       CASE WHEN COUNT(*) = 5 THEN 'OK' ELSE 'FAIL: 期望 5 行' END AS check_templates
FROM [Game_HunduShijie_PetTemplate];

-- 2) 号段合法性：species_id 必须落在 500000-799999。零行才是对的；任何一行越界都说明
--    有人给编号列加了自增，或者手工插了不合法的号。
SELECT species_id, name FROM [Game_HunduShijie_PetTemplate]
WHERE species_id < 500000 OR species_id > 799999;

-- 3) 资质区间必须 min <= max（反了的话 roll 会静默返回 min，看起来像"这只宠资质最差"）。
SELECT species_id FROM [Game_HunduShijie_PetStatBand]
WHERE attack_min > attack_max OR defense_min > defense_max OR stamina_min > stamina_max
   OR mana_min > mana_max OR speed_min > speed_max OR growth_min_bps > growth_max_bps;

-- 4) 每个物种都必须有一行资质区间、槽位规则、行为、装备规则、合成规则（少一行 = 编辑器显示空白而
--    运行时期望值拿默认，两侧都不报错）。
SELECT t.species_id FROM [Game_HunduShijie_PetTemplate] t
LEFT JOIN [Game_HunduShijie_PetStatBand] b ON b.species_id = t.species_id
WHERE b.species_id IS NULL;

SELECT t.species_id FROM [Game_HunduShijie_PetTemplate] t
LEFT JOIN [Game_HunduShijie_PetSkillRule] r ON r.species_id = t.species_id
WHERE r.species_id IS NULL;

SELECT t.species_id FROM [Game_HunduShijie_PetTemplate] t
LEFT JOIN [Game_HunduShijie_PetBehavior] h ON h.species_id = t.species_id
WHERE h.species_id IS NULL;

SELECT t.species_id FROM [Game_HunduShijie_PetTemplate] t
LEFT JOIN [Game_HunduShijie_PetEquipRule] e ON e.species_id = t.species_id
WHERE e.species_id IS NULL;

SELECT t.species_id FROM [Game_HunduShijie_PetTemplate] t
LEFT JOIN [Game_HunduShijie_PetMergeRule] g ON g.species_id = t.species_id
WHERE g.species_id IS NULL;

-- 4b) 规则侧表各自非零（0 行说明种子那一段整块没执行，而不是"这个物种不需要"）。
SELECT 'Game_HunduShijie_PetStatBand' AS tbl, COUNT(*) AS rows_now,
       CASE WHEN COUNT(*) = 5 THEN 'OK' ELSE 'FAIL' END AS check_rows
FROM [Game_HunduShijie_PetStatBand];
GO
SELECT 'Game_HunduShijie_PetSkillRule' AS tbl, COUNT(*) AS rows_now,
       CASE WHEN COUNT(*) = 5 THEN 'OK' ELSE 'FAIL' END AS check_rows
FROM [Game_HunduShijie_PetSkillRule];
GO
SELECT 'Game_HunduShijie_PetBehavior' AS tbl, COUNT(*) AS rows_now,
       CASE WHEN COUNT(*) = 5 THEN 'OK' ELSE 'FAIL' END AS check_rows
FROM [Game_HunduShijie_PetBehavior];
GO
SELECT 'Game_HunduShijie_PetEquipRule' AS tbl, COUNT(*) AS rows_now,
       CASE WHEN COUNT(*) = 5 THEN 'OK' ELSE 'FAIL' END AS check_rows
FROM [Game_HunduShijie_PetEquipRule];
GO
SELECT 'Game_HunduShijie_PetMergeRule' AS tbl, COUNT(*) AS rows_now,
       CASE WHEN COUNT(*) = 5 THEN 'OK' ELSE 'FAIL' END AS check_rows
FROM [Game_HunduShijie_PetMergeRule];
GO
SELECT 'Game_HunduShijie_PetAssistRule' AS tbl, COUNT(*) AS rows_now,
       CASE WHEN COUNT(*) = 10 THEN 'OK' ELSE 'FAIL' END AS check_rows
FROM [Game_HunduShijie_PetAssistRule];
GO
SELECT 'Game_HunduShijie_PetMutationRule' AS tbl, COUNT(*) AS rows_now,
       CASE WHEN COUNT(*) = 4 THEN 'OK' ELSE 'FAIL' END AS check_rows
FROM [Game_HunduShijie_PetMutationRule];
GO
SELECT 'Game_HunduShijie_PetSkillPool' AS tbl, COUNT(*) AS rows_now,
       CASE WHEN COUNT(*) = 15 THEN 'OK' ELSE 'FAIL' END AS check_rows
FROM [Game_HunduShijie_PetSkillPool];
GO
-- 5) 技能池的天生技（kind=1）每个物种至多一条落在同一个 innate_slot 上；
--    重复会让"恢复天生技回第 1 格"变成非确定操作。
SELECT species_id, innate_slot, COUNT(*) AS dup FROM [Game_HunduShijie_PetSkillPool]
WHERE kind = 1 GROUP BY species_id, innate_slot HAVING COUNT(*) > 1;

-- 6) 两张运行时表本期**应当为空**（PetInstance 尚未接入 Orleans Grain，设计文档 §8 D8）。
--    若这里出现行，说明有人绕过内核直接往个体表里写了数据——个体编号必须由 PetIdentityCore 分配。
SELECT 'Game_HunduShijie_PetInstance' AS tbl, COUNT(*) AS rows_now FROM [Game_HunduShijie_PetInstance];
SELECT 'Game_HunduShijie_PetInstanceEvent' AS tbl, COUNT(*) AS rows_now FROM [Game_HunduShijie_PetInstanceEvent];
GO

-- 7) 个体表若将来启用，重复副本必须能被数出来：同一 (owner, species) 的多行是**正常状态**，
--    而 pet_instance_id 反解出的物种必须与 species_id 列一致（不一致 = 有人手填了编号）。
SELECT pet_instance_id, species_id FROM [Game_HunduShijie_PetInstance]
WHERE species_id < 500000 OR species_id > 799999
   OR pet_instance_id <> CAST(species_id AS bigint) * 100000 + (pet_instance_id % 100000);
