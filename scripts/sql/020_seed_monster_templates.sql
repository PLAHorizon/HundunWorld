-- ============================================================================
-- 020_seed_monster_templates.sql —— 怪物编辑器：把今天硬编码的 11 只怪与开发刷怪点搬进表
--
-- 本脚本**只生成、不执行**（大功能的迁移与写库由用户点头后再跑；本地 sqlcmd -E 可直连）。
-- 执行前提：019_monster_editor_tables.sql 已建表。
--
-- 为什么要种子而不是"从空表开始配"：
--   1) 编号 1–11 是既有契约（脚本生成白名单 ScriptAuthorityCore、任务击杀目标、头顶信息卡断言
--      都按它们写过，MonsterTemplateCatalog.cs:49 明确"前三行 id/名称/等级都不动"）。
--   2) 数值口径必须**逐字等于今天运行时的值**，否则"接入数据库"这件事会顺带把平衡改掉，
--      而那种改变在日志里表现为"怪变硬了"，不会有人归因到本脚本。
--
-- 数值对账（本脚本的"不改平衡"承诺，逐项都有出处）：
--   MaxHp=100 / AttackPower=10 / MoveSpeed=4 / Aggro=10 / Leash=30 / Wander=6 /
--   IdleToPatrol=3 / Respawn=15 / MaxAlive=3 / SlotSpacing=8
--   出处 = SpawnPointConfig.Default（Horizon.Game.Sim/SpawnDirectorCore.cs:70）
--   与 DsHost 的 BuildSpawnPoints（Horizon.DsHost/Program.cs:372 起，沿 X 每 60m 一点）。
--   → 故 hp_base=100/hp_per_level=0、atk_base=10/atk_per_level=0（公式在任意等级都得今天的常数）。
--
-- 三处**有意的**行为差异（不是漂移，请在验收时按这三条看）：
--   A) ai_profile=0（笨档，五个覆盖位全 NULL）：威胁表/技能/走图/求援/战术全关。
--      这是为了"接入数据表"与"改 AI 行为"两件事分开发生；策划把档位调到 2/3 才会开启。
--   B) 击杀经验：此前**根本不存在**这条链路（DsWorldSession.HandleMonsterDeath 只发任务计数与好感）。
--      本次 xp_base=45/xp_per_level=5 是新增产出，不是数值变更；灰名断供保证大号不会在低级怪身上刷经验。
--   C) 世界 BOSS（rank=4）在属性公式里会再乘 rank 倍率（6×血量），而今天它们和普通怪一样 100 血。
--      这是"rank 有意义"的必然结果；若要保持一致请给 BOSS 行写 hp_override_bps。
-- ============================================================================

BEGIN TRANSACTION;

-- ---------- 模板（11 行，与 MonsterTemplateCatalog.Templates 逐字对齐） ----------
MERGE INTO [Game_HunduShijie_MonsterTemplate] AS t
USING (VALUES
    (1,  N'墨麒麟',   12, N'mo_qilin',        3, 0),
    (2,  N'石甲狰',   18, N'shi_jia_zheng',   5, 0),
    (3,  N'冥羽鸩',   26, N'ming_yu_zhen',    4, 0),
    (4,  N'银爪狸',    8, N'yin_zhua_li',     1, 0),
    (5,  N'青藤蟒',   21, N'qing_teng_mang',  2, 0),
    (6,  N'锐金夜叉',210, N'boss_jin_ye_cha', 1, 4),
    (7,  N'青梧蛟',  215, N'boss_qing_wu_jiao',2, 4),
    (8,  N'玄溟螭',  220, N'boss_xuan_ming_chi',3, 4),
    (9,  N'赤炎鹏',  225, N'boss_chi_yan_peng', 4, 4),
    (10, N'厚岩魁',  230, N'boss_hou_yan_kui',  5, 4),
    (11, N'混沌遗蜕',300, N'boss_hundun_yitui', 0, 4)
) AS s(monster_id, name, level, model_key, wuxing_domain, rank)
ON t.monster_id = s.monster_id
WHEN NOT MATCHED THEN
    INSERT (monster_id, name, rank, species, wuxing_domain, model_key, prefab_guid, model_scale,
            stand_state, level_base, level_min, level_max,
            hp_base, hp_per_level, atk_base, atk_per_level, def_base, def_per_level,
            hp_override_bps, atk_override_bps, xp_base, xp_per_level,
            respawn_base_seconds, respawn_per_level_seconds, corpse_seconds, aggro_say_id,
            publish_state, content_version, remark,
            xp_mod_bps, health_mod_bps, IsValid, GameId, AreaId, ServerId, GameUserId)
    VALUES (s.monster_id, s.name, s.rank,
            -- species 是占位（1野兽 / 4巨人 / 3龙类），等策划在编辑器里逐项确认；
            -- 留 0（人形）会让"同族求援"把所有人都判成同类，比占位值更糟。
            CASE WHEN s.rank = 0 THEN 1 WHEN s.wuxing_domain = 0 THEN 3 ELSE 4 END,
            s.wuxing_domain, s.model_key, NULL, 1.0,
            0, s.level, s.level, s.level,
            100, 0, 10, 0, 0, 0,
            0, 0, 45, 5,
            15.0, 0.0, 60.0, 0,
            1, 1, N'种子：由 MonsterTemplateCatalog 硬编码表迁入，数值对账见脚本头',
            10000, 10000, 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000')
WHEN MATCHED THEN
    UPDATE SET
        -- 只对齐"身份三要素 + 域"，**不动**数值列：数值列可能已被策划在编辑器里改过，
        -- 重跑种子不该把它们覆盖回默认值。
        t.name = s.name, t.rank = s.rank, t.wuxing_domain = s.wuxing_domain,
        t.model_key = s.model_key,
        t.level_base = s.level, t.level_min = s.level, t.level_max = s.level,
        t.xp_mod_bps = 10000, t.health_mod_bps = 10000, t.IsValid = 1;

-- ---------- 行为（11 行；五覆盖位全 NULL = 跟 ai_profile 预设） ----------
MERGE INTO [Game_HunduShijie_MonsterBehavior] AS t
USING (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11)) AS s(monster_id)
ON t.monster_id = s.monster_id
WHEN NOT MATCHED THEN
    INSERT (monster_id, ai_profile, flag_use_threat, flag_use_skills, flag_use_navmesh,
            flag_use_assist, flag_use_tactics, is_aggressive, aggro_radius, aggro_cone_degrees,
            leash_radius, evade_immunity_seconds, assist_radius, detect_mask, wander_radius,
            idle_to_patrol_seconds, walk_speed, move_speed, follow_type, melee_swing_seconds,
            ranged_min_distance, ranged_max_distance, flee_hp_percent, patrol_route_id,
            IsValid, GameId, AreaId, ServerId, GameUserId)
    VALUES (s.monster_id, 0, NULL, NULL, NULL, NULL, NULL,
            1, 10.0, 360.0, 30.0, 3.0, 10.0, 1, 6.0, 3.0, 2.5, 4.0, 0, 1.5, 0.0, 0.0, 0, 0,
            1, 0, 0, 0, '00000000-0000-0000-0000-000000000000')
WHEN MATCHED THEN
    UPDATE SET
        -- 覆盖位刻意**不回填**：种子脚本不该替策划把"强制关"写回"跟档位"。
        t.aggro_radius = 10.0, t.aggro_cone_degrees = 360.0, t.leash_radius = 30.0,
        t.move_speed = 4.0, t.walk_speed = 2.5, t.IsValid = 1;

-- ---------- 开发刷怪点（复刻 DsHost 的"沿 X 每 60m 一点、每点 3 只、模板 1+(i%5)"） ----------
MERGE INTO [Game_HunduShijie_MonsterSpawnPoint] AS t
USING (VALUES
    (1, 0, 20.0, 1), (2, 0, 80.0, 2), (3, 0, 140.0, 3),
    (4, 0, 200.0, 4), (5, 0, 260.0, 5), (6, 0, 320.0, 1)
) AS s(spawn_point_id, shard_id, x, monster_id)
ON t.spawn_point_id = s.spawn_point_id
WHEN NOT MATCHED THEN
    INSERT (spawn_point_id, zone_id, shard_id, x, y, z, yaw, monster_id, level_override,
            max_alive, slot_spacing, respawn_override_seconds, route_override_id,
            enabled, publish_state, content_version,
            IsValid, GameId, AreaId, ServerId, GameUserId)
    VALUES (s.spawn_point_id, 0, s.shard_id, s.x, 0.0, 0.0, 0.0, s.monster_id, NULL,
            3, 8.0, 0.0, 0, 1, 1, 1,
            1, 0, 0, 0, '00000000-0000-0000-0000-000000000000')
WHEN MATCHED THEN
    UPDATE SET t.x = s.x, t.monster_id = s.monster_id, t.enabled = 1, t.IsValid = 1;

COMMIT;

-- ---------- 自检（跑完本脚本后立刻执行；任何一条对不上就说明种子被改坏了） ----------
-- 1) 模板行数必须是 11，且 1–11 连续（号段契约的最低要求）。
SELECT COUNT(*) AS template_rows,
       CASE WHEN COUNT(*) = 11 THEN 'OK' ELSE 'FAIL: 期望 11 行' END AS check_templates
FROM [Game_HunduShijie_MonsterTemplate];

-- 2) 名称/等级/域必须与代码里的硬编码表逐字相同（迁移期间两处共存，这是唯一的对齐证据）。
SELECT m.monster_id, m.name, m.level_base, m.wuxing_domain, m.rank
FROM [Game_HunduShijie_MonsterTemplate] m
WHERE m.monster_id IN (1,2,3,4,5,6,7,8,9,10,11)
ORDER BY m.monster_id;

-- 3) 每个刷怪点引用的怪都必须存在且已发布（引用完整性：漏一条 = 那个点整点不刷怪且无日志）。
SELECT sp.spawn_point_id, sp.monster_id
FROM [Game_HunduShijie_MonsterSpawnPoint] sp
LEFT JOIN [Game_HunduShijie_MonsterTemplate] t
       ON t.monster_id = sp.monster_id AND t.publish_state = 1
WHERE sp.enabled = 1 AND t.monster_id IS NULL;

-- 4) 行为表必须与模板 1:1（少一行 = 那只怪运行时拿默认行为而编辑器里显示为空，两侧都不报错）。
SELECT t.monster_id
FROM [Game_HunduShijie_MonsterTemplate] t
LEFT JOIN [Game_HunduShijie_MonsterBehavior] b ON b.monster_id = t.monster_id
WHERE b.monster_id IS NULL;
