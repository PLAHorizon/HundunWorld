-- 007_seed_skill_templates.sql
-- 把客户端技能定义落成服务端技能模板行，让权威结算知道"这个技能属哪一行五行"、消耗与冷却是多少。
--
-- 数据来源优先级：**运行时真正会施放的那一份**（各 SkillBase 子类在 OnAwake 里 new 的 SkillData），
-- 其余 id 才取 Combat/Skills/SkillDatabase.cs 的目录条目（同样带上它的能量/冷却常数，避免"模板存在但消耗为 0"的白嫖）。
-- wuxing_element 用战斗侧编号（0=无属性，1..5=金木水火土）。
-- skill_attack / skill_penetration 一律 0：技能自身的五行攻击与破防属数值设计，不由这次迁移编造；
-- 留 0 的含义是"技能只决定取哪一行与克制倍率，威力仍来自角色自身的五行攻击"。
-- energy/cooldown/cast_time 公式列写同值常数（常数是最简单的公式），与客户端声明同值——
-- 服务端按模板扣内力池，客户端报的消耗只在模板缺行时兜底，否则报 0 就能无限施法。
-- 可重复执行：已存在的行按同一份来源刷新。
--
-- ⚠ 已知数据冲突（10 个 id 在两套客户端表里指代不同技能/不同五行，这里一律取运行时那份）：
--     id 2001：运行时 SkillBase 是「青木藤缠」(2)，SkillDatabase 是「冰霜箭」(3)
--     id 2002：运行时 SkillBase 是「春回大地」(2)，SkillDatabase 是「寒冰领域」(3)
--     id 2003：运行时 SkillBase 是「万木森罗」(2)，SkillDatabase 是「冰封万里」(3)
--     id 3001：运行时 SkillBase 是「寒冰掌」(3)，SkillDatabase 是「火球术」(4)
--     id 3002：运行时 SkillBase 是「水愈术」(3)，SkillDatabase 是「烈焰旋风」(4)
--     id 3003：运行时 SkillBase 是「冰封千里」(3)，SkillDatabase 是「焚天灭地」(4)
--     id 4001：运行时 SkillBase 是「烈焰掌」(4)，SkillDatabase 是「春风化雨」(2)
--     id 4002：运行时 SkillBase 是「火球术」(4)，SkillDatabase 是「万物复苏」(2)
--     id 5001：运行时 SkillBase 是「岩甲术」(5)，SkillDatabase 是「磐石护体」(5)
--     id 5002：运行时 SkillBase 是「地刺术」(5)，SkillDatabase 是「地裂山崩」(5)

SET NOCOUNT ON;
-- skill_id 是 IDENTITY 列
SET IDENTITY_INSERT dbo.Game_HunduShijie_SkillTemplate ON;

IF EXISTS (SELECT 1 FROM Game_HunduShijie_SkillTemplate WHERE skill_id = 1001)
    UPDATE Game_HunduShijie_SkillTemplate SET skill_name = N'裂金斩', description = N'以刚猛内力凝于剑锋，劈出金色剑气。', wuxing_element = 1,
        energy_cost_formula = N'30', cooldown_formula = N'3', cast_time_formula = N'0.4', attack_range = 5,
        update_time = GETUTCDATE() WHERE skill_id = 1001;
ELSE
    INSERT INTO Game_HunduShijie_SkillTemplate (skill_id, skill_name, description, skill_type, skill_school, sect_id, profession_limit, max_level, learn_level_require, learn_realm_require, learn_cost, comprehension_require, energy_cost_formula, cooldown_formula, cast_time_formula, attack_range, target_type, effect_range, max_targets, skill_effects, level_effects, combo_skill_ids, can_create, can_advance, icon_path, effect_id, sound_id, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId, wuxing_element, skill_attack, skill_penetration) VALUES (1001, N'裂金斩', N'以刚猛内力凝于剑锋，劈出金色剑气。', 1, 0, 0, '', 10, 1, 0, '', 0, N'30', N'3', N'0.4', 5, 0, 0, 1, N'', N'', '', 0, 0, '', 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000', 1, 0, 0);

IF EXISTS (SELECT 1 FROM Game_HunduShijie_SkillTemplate WHERE skill_id = 1002)
    UPDATE Game_HunduShijie_SkillTemplate SET skill_name = N'断岳击', description = N'裂金斩后续招， upward 挑击将敌人击飞。', wuxing_element = 1,
        energy_cost_formula = N'25', cooldown_formula = N'0', cast_time_formula = N'0.3', attack_range = 4,
        update_time = GETUTCDATE() WHERE skill_id = 1002;
ELSE
    INSERT INTO Game_HunduShijie_SkillTemplate (skill_id, skill_name, description, skill_type, skill_school, sect_id, profession_limit, max_level, learn_level_require, learn_realm_require, learn_cost, comprehension_require, energy_cost_formula, cooldown_formula, cast_time_formula, attack_range, target_type, effect_range, max_targets, skill_effects, level_effects, combo_skill_ids, can_create, can_advance, icon_path, effect_id, sound_id, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId, wuxing_element, skill_attack, skill_penetration) VALUES (1002, N'断岳击', N'裂金斩后续招， upward 挑击将敌人击飞。', 1, 0, 0, '', 10, 1, 0, '', 0, N'25', N'0', N'0.3', 4, 0, 0, 1, N'', N'', '', 0, 0, '', 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000', 1, 0, 0);

IF EXISTS (SELECT 1 FROM Game_HunduShijie_SkillTemplate WHERE skill_id = 1003)
    UPDATE Game_HunduShijie_SkillTemplate SET skill_name = N'万剑归宗', description = N'连招终结技，内力爆发化为无数剑气横扫前方。', wuxing_element = 1,
        energy_cost_formula = N'60', cooldown_formula = N'15', cast_time_formula = N'0.8', attack_range = 12,
        update_time = GETUTCDATE() WHERE skill_id = 1003;
ELSE
    INSERT INTO Game_HunduShijie_SkillTemplate (skill_id, skill_name, description, skill_type, skill_school, sect_id, profession_limit, max_level, learn_level_require, learn_realm_require, learn_cost, comprehension_require, energy_cost_formula, cooldown_formula, cast_time_formula, attack_range, target_type, effect_range, max_targets, skill_effects, level_effects, combo_skill_ids, can_create, can_advance, icon_path, effect_id, sound_id, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId, wuxing_element, skill_attack, skill_penetration) VALUES (1003, N'万剑归宗', N'连招终结技，内力爆发化为无数剑气横扫前方。', 1, 0, 0, '', 10, 1, 0, '', 0, N'60', N'15', N'0.8', 12, 0, 0, 1, N'', N'', '', 0, 0, '', 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000', 1, 0, 0);

IF EXISTS (SELECT 1 FROM Game_HunduShijie_SkillTemplate WHERE skill_id = 2001)
    UPDATE Game_HunduShijie_SkillTemplate SET skill_name = N'青木藤缠', description = N'召唤藤蔓束缚敌人，造成持续伤害和定身效果', wuxing_element = 2,
        energy_cost_formula = N'70', cooldown_formula = N'10', cast_time_formula = N'0.3', attack_range = 15,
        update_time = GETUTCDATE() WHERE skill_id = 2001;
ELSE
    INSERT INTO Game_HunduShijie_SkillTemplate (skill_id, skill_name, description, skill_type, skill_school, sect_id, profession_limit, max_level, learn_level_require, learn_realm_require, learn_cost, comprehension_require, energy_cost_formula, cooldown_formula, cast_time_formula, attack_range, target_type, effect_range, max_targets, skill_effects, level_effects, combo_skill_ids, can_create, can_advance, icon_path, effect_id, sound_id, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId, wuxing_element, skill_attack, skill_penetration) VALUES (2001, N'青木藤缠', N'召唤藤蔓束缚敌人，造成持续伤害和定身效果', 1, 0, 0, '', 10, 1, 0, '', 0, N'70', N'10', N'0.3', 15, 0, 0, 1, N'', N'', '', 0, 0, '', 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000', 2, 0, 0);

IF EXISTS (SELECT 1 FROM Game_HunduShijie_SkillTemplate WHERE skill_id = 2002)
    UPDATE Game_HunduShijie_SkillTemplate SET skill_name = N'春回大地', description = N'范围治疗友军，恢复生命值并持续恢复', wuxing_element = 2,
        energy_cost_formula = N'120', cooldown_formula = N'30', cast_time_formula = N'1', attack_range = 0,
        update_time = GETUTCDATE() WHERE skill_id = 2002;
ELSE
    INSERT INTO Game_HunduShijie_SkillTemplate (skill_id, skill_name, description, skill_type, skill_school, sect_id, profession_limit, max_level, learn_level_require, learn_realm_require, learn_cost, comprehension_require, energy_cost_formula, cooldown_formula, cast_time_formula, attack_range, target_type, effect_range, max_targets, skill_effects, level_effects, combo_skill_ids, can_create, can_advance, icon_path, effect_id, sound_id, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId, wuxing_element, skill_attack, skill_penetration) VALUES (2002, N'春回大地', N'范围治疗友军，恢复生命值并持续恢复', 1, 0, 0, '', 10, 1, 0, '', 0, N'120', N'30', N'1', 0, 0, 0, 1, N'', N'', '', 0, 0, '', 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000', 2, 0, 0);

IF EXISTS (SELECT 1 FROM Game_HunduShijie_SkillTemplate WHERE skill_id = 2003)
    UPDATE Game_HunduShijie_SkillTemplate SET skill_name = N'万木森罗', description = N'召唤树木持续攻击范围内的敌人', wuxing_element = 2,
        energy_cost_formula = N'180', cooldown_formula = N'25', cast_time_formula = N'1.5', attack_range = 20,
        update_time = GETUTCDATE() WHERE skill_id = 2003;
ELSE
    INSERT INTO Game_HunduShijie_SkillTemplate (skill_id, skill_name, description, skill_type, skill_school, sect_id, profession_limit, max_level, learn_level_require, learn_realm_require, learn_cost, comprehension_require, energy_cost_formula, cooldown_formula, cast_time_formula, attack_range, target_type, effect_range, max_targets, skill_effects, level_effects, combo_skill_ids, can_create, can_advance, icon_path, effect_id, sound_id, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId, wuxing_element, skill_attack, skill_penetration) VALUES (2003, N'万木森罗', N'召唤树木持续攻击范围内的敌人', 1, 0, 0, '', 10, 1, 0, '', 0, N'180', N'25', N'1.5', 20, 0, 0, 1, N'', N'', '', 0, 0, '', 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000', 2, 0, 0);

IF EXISTS (SELECT 1 FROM Game_HunduShijie_SkillTemplate WHERE skill_id = 2004)
    UPDATE Game_HunduShijie_SkillTemplate SET skill_name = N'生生不息', description = N'激活生命之力，持续恢复生命和内力，并提升移动速度', wuxing_element = 2,
        energy_cost_formula = N'250', cooldown_formula = N'60', cast_time_formula = N'0.5', attack_range = 0,
        update_time = GETUTCDATE() WHERE skill_id = 2004;
ELSE
    INSERT INTO Game_HunduShijie_SkillTemplate (skill_id, skill_name, description, skill_type, skill_school, sect_id, profession_limit, max_level, learn_level_require, learn_realm_require, learn_cost, comprehension_require, energy_cost_formula, cooldown_formula, cast_time_formula, attack_range, target_type, effect_range, max_targets, skill_effects, level_effects, combo_skill_ids, can_create, can_advance, icon_path, effect_id, sound_id, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId, wuxing_element, skill_attack, skill_penetration) VALUES (2004, N'生生不息', N'激活生命之力，持续恢复生命和内力，并提升移动速度', 1, 0, 0, '', 10, 1, 0, '', 0, N'250', N'60', N'0.5', 0, 0, 0, 1, N'', N'', '', 0, 0, '', 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000', 2, 0, 0);

IF EXISTS (SELECT 1 FROM Game_HunduShijie_SkillTemplate WHERE skill_id = 3001)
    UPDATE Game_HunduShijie_SkillTemplate SET skill_name = N'寒冰掌', description = N'掌心凝聚寒冰之力，攻击敌人并减速', wuxing_element = 3,
        energy_cost_formula = N'60', cooldown_formula = N'6', cast_time_formula = N'0.3', attack_range = 8,
        update_time = GETUTCDATE() WHERE skill_id = 3001;
ELSE
    INSERT INTO Game_HunduShijie_SkillTemplate (skill_id, skill_name, description, skill_type, skill_school, sect_id, profession_limit, max_level, learn_level_require, learn_realm_require, learn_cost, comprehension_require, energy_cost_formula, cooldown_formula, cast_time_formula, attack_range, target_type, effect_range, max_targets, skill_effects, level_effects, combo_skill_ids, can_create, can_advance, icon_path, effect_id, sound_id, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId, wuxing_element, skill_attack, skill_penetration) VALUES (3001, N'寒冰掌', N'掌心凝聚寒冰之力，攻击敌人并减速', 1, 0, 0, '', 10, 1, 0, '', 0, N'60', N'6', N'0.3', 8, 0, 0, 1, N'', N'', '', 0, 0, '', 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000', 3, 0, 0);

IF EXISTS (SELECT 1 FROM Game_HunduShijie_SkillTemplate WHERE skill_id = 3002)
    UPDATE Game_HunduShijie_SkillTemplate SET skill_name = N'水愈术', description = N'引导水之灵力，快速恢复生命值', wuxing_element = 3,
        energy_cost_formula = N'100', cooldown_formula = N'15', cast_time_formula = N'0.8', attack_range = 15,
        update_time = GETUTCDATE() WHERE skill_id = 3002;
ELSE
    INSERT INTO Game_HunduShijie_SkillTemplate (skill_id, skill_name, description, skill_type, skill_school, sect_id, profession_limit, max_level, learn_level_require, learn_realm_require, learn_cost, comprehension_require, energy_cost_formula, cooldown_formula, cast_time_formula, attack_range, target_type, effect_range, max_targets, skill_effects, level_effects, combo_skill_ids, can_create, can_advance, icon_path, effect_id, sound_id, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId, wuxing_element, skill_attack, skill_penetration) VALUES (3002, N'水愈术', N'引导水之灵力，快速恢复生命值', 1, 0, 0, '', 10, 1, 0, '', 0, N'100', N'15', N'0.8', 15, 0, 0, 1, N'', N'', '', 0, 0, '', 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000', 3, 0, 0);

IF EXISTS (SELECT 1 FROM Game_HunduShijie_SkillTemplate WHERE skill_id = 3003)
    UPDATE Game_HunduShijie_SkillTemplate SET skill_name = N'冰封千里', description = N'大范围冰冻敌人，解冻后持续减速', wuxing_element = 3,
        energy_cost_formula = N'200', cooldown_formula = N'30', cast_time_formula = N'2', attack_range = 20,
        update_time = GETUTCDATE() WHERE skill_id = 3003;
ELSE
    INSERT INTO Game_HunduShijie_SkillTemplate (skill_id, skill_name, description, skill_type, skill_school, sect_id, profession_limit, max_level, learn_level_require, learn_realm_require, learn_cost, comprehension_require, energy_cost_formula, cooldown_formula, cast_time_formula, attack_range, target_type, effect_range, max_targets, skill_effects, level_effects, combo_skill_ids, can_create, can_advance, icon_path, effect_id, sound_id, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId, wuxing_element, skill_attack, skill_penetration) VALUES (3003, N'冰封千里', N'大范围冰冻敌人，解冻后持续减速', 1, 0, 0, '', 10, 1, 0, '', 0, N'200', N'30', N'2', 20, 0, 0, 1, N'', N'', '', 0, 0, '', 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000', 3, 0, 0);

IF EXISTS (SELECT 1 FROM Game_HunduShijie_SkillTemplate WHERE skill_id = 3004)
    UPDATE Game_HunduShijie_SkillTemplate SET skill_name = N'滔天巨浪', description = N'召唤巨大水浪向前冲击，击退范围内所有敌人', wuxing_element = 3,
        energy_cost_formula = N'180', cooldown_formula = N'25', cast_time_formula = N'1.2', attack_range = 0,
        update_time = GETUTCDATE() WHERE skill_id = 3004;
ELSE
    INSERT INTO Game_HunduShijie_SkillTemplate (skill_id, skill_name, description, skill_type, skill_school, sect_id, profession_limit, max_level, learn_level_require, learn_realm_require, learn_cost, comprehension_require, energy_cost_formula, cooldown_formula, cast_time_formula, attack_range, target_type, effect_range, max_targets, skill_effects, level_effects, combo_skill_ids, can_create, can_advance, icon_path, effect_id, sound_id, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId, wuxing_element, skill_attack, skill_penetration) VALUES (3004, N'滔天巨浪', N'召唤巨大水浪向前冲击，击退范围内所有敌人', 1, 0, 0, '', 10, 1, 0, '', 0, N'180', N'25', N'1.2', 0, 0, 0, 1, N'', N'', '', 0, 0, '', 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000', 3, 0, 0);

IF EXISTS (SELECT 1 FROM Game_HunduShijie_SkillTemplate WHERE skill_id = 3005)
    UPDATE Game_HunduShijie_SkillTemplate SET skill_name = N'水月镜花', description = N'创造水之幻影分身，提升闪避率并协同攻击', wuxing_element = 3,
        energy_cost_formula = N'150', cooldown_formula = N'35', cast_time_formula = N'1', attack_range = 0,
        update_time = GETUTCDATE() WHERE skill_id = 3005;
ELSE
    INSERT INTO Game_HunduShijie_SkillTemplate (skill_id, skill_name, description, skill_type, skill_school, sect_id, profession_limit, max_level, learn_level_require, learn_realm_require, learn_cost, comprehension_require, energy_cost_formula, cooldown_formula, cast_time_formula, attack_range, target_type, effect_range, max_targets, skill_effects, level_effects, combo_skill_ids, can_create, can_advance, icon_path, effect_id, sound_id, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId, wuxing_element, skill_attack, skill_penetration) VALUES (3005, N'水月镜花', N'创造水之幻影分身，提升闪避率并协同攻击', 1, 0, 0, '', 10, 1, 0, '', 0, N'150', N'35', N'1', 0, 0, 0, 1, N'', N'', '', 0, 0, '', 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000', 3, 0, 0);

IF EXISTS (SELECT 1 FROM Game_HunduShijie_SkillTemplate WHERE skill_id = 4001)
    UPDATE Game_HunduShijie_SkillTemplate SET skill_name = N'烈焰掌', description = N'掌心凝聚烈焰攻击敌人，造成燃烧效果', wuxing_element = 4,
        energy_cost_formula = N'75', cooldown_formula = N'7', cast_time_formula = N'0.3', attack_range = 8,
        update_time = GETUTCDATE() WHERE skill_id = 4001;
ELSE
    INSERT INTO Game_HunduShijie_SkillTemplate (skill_id, skill_name, description, skill_type, skill_school, sect_id, profession_limit, max_level, learn_level_require, learn_realm_require, learn_cost, comprehension_require, energy_cost_formula, cooldown_formula, cast_time_formula, attack_range, target_type, effect_range, max_targets, skill_effects, level_effects, combo_skill_ids, can_create, can_advance, icon_path, effect_id, sound_id, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId, wuxing_element, skill_attack, skill_penetration) VALUES (4001, N'烈焰掌', N'掌心凝聚烈焰攻击敌人，造成燃烧效果', 1, 0, 0, '', 10, 1, 0, '', 0, N'75', N'7', N'0.3', 8, 0, 0, 1, N'', N'', '', 0, 0, '', 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000', 4, 0, 0);

IF EXISTS (SELECT 1 FROM Game_HunduShijie_SkillTemplate WHERE skill_id = 4002)
    UPDATE Game_HunduShijie_SkillTemplate SET skill_name = N'火球术', description = N'投掷火球攻击目标位置，造成范围爆炸伤害', wuxing_element = 4,
        energy_cost_formula = N'110', cooldown_formula = N'10', cast_time_formula = N'0.8', attack_range = 25,
        update_time = GETUTCDATE() WHERE skill_id = 4002;
ELSE
    INSERT INTO Game_HunduShijie_SkillTemplate (skill_id, skill_name, description, skill_type, skill_school, sect_id, profession_limit, max_level, learn_level_require, learn_realm_require, learn_cost, comprehension_require, energy_cost_formula, cooldown_formula, cast_time_formula, attack_range, target_type, effect_range, max_targets, skill_effects, level_effects, combo_skill_ids, can_create, can_advance, icon_path, effect_id, sound_id, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId, wuxing_element, skill_attack, skill_penetration) VALUES (4002, N'火球术', N'投掷火球攻击目标位置，造成范围爆炸伤害', 1, 0, 0, '', 10, 1, 0, '', 0, N'110', N'10', N'0.8', 25, 0, 0, 1, N'', N'', '', 0, 0, '', 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000', 4, 0, 0);

IF EXISTS (SELECT 1 FROM Game_HunduShijie_SkillTemplate WHERE skill_id = 4003)
    UPDATE Game_HunduShijie_SkillTemplate SET skill_name = N'炎龙出海', description = N'释放炽热火龙直线冲击，穿透敌人造成伤害', wuxing_element = 4,
        energy_cost_formula = N'160', cooldown_formula = N'18', cast_time_formula = N'1', attack_range = 0,
        update_time = GETUTCDATE() WHERE skill_id = 4003;
ELSE
    INSERT INTO Game_HunduShijie_SkillTemplate (skill_id, skill_name, description, skill_type, skill_school, sect_id, profession_limit, max_level, learn_level_require, learn_realm_require, learn_cost, comprehension_require, energy_cost_formula, cooldown_formula, cast_time_formula, attack_range, target_type, effect_range, max_targets, skill_effects, level_effects, combo_skill_ids, can_create, can_advance, icon_path, effect_id, sound_id, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId, wuxing_element, skill_attack, skill_penetration) VALUES (4003, N'炎龙出海', N'释放炽热火龙直线冲击，穿透敌人造成伤害', 1, 0, 0, '', 10, 1, 0, '', 0, N'160', N'18', N'1', 0, 0, 0, 1, N'', N'', '', 0, 0, '', 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000', 4, 0, 0);

IF EXISTS (SELECT 1 FROM Game_HunduShijie_SkillTemplate WHERE skill_id = 4004)
    UPDATE Game_HunduShijie_SkillTemplate SET skill_name = N'焚天灭地', description = N'召唤天火降临，造成毁灭性的范围爆炸伤害', wuxing_element = 4,
        energy_cost_formula = N'300', cooldown_formula = N'40', cast_time_formula = N'2.5', attack_range = 30,
        update_time = GETUTCDATE() WHERE skill_id = 4004;
ELSE
    INSERT INTO Game_HunduShijie_SkillTemplate (skill_id, skill_name, description, skill_type, skill_school, sect_id, profession_limit, max_level, learn_level_require, learn_realm_require, learn_cost, comprehension_require, energy_cost_formula, cooldown_formula, cast_time_formula, attack_range, target_type, effect_range, max_targets, skill_effects, level_effects, combo_skill_ids, can_create, can_advance, icon_path, effect_id, sound_id, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId, wuxing_element, skill_attack, skill_penetration) VALUES (4004, N'焚天灭地', N'召唤天火降临，造成毁灭性的范围爆炸伤害', 1, 0, 0, '', 10, 1, 0, '', 0, N'300', N'40', N'2.5', 30, 0, 0, 1, N'', N'', '', 0, 0, '', 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000', 4, 0, 0);

IF EXISTS (SELECT 1 FROM Game_HunduShijie_SkillTemplate WHERE skill_id = 4005)
    UPDATE Game_HunduShijie_SkillTemplate SET skill_name = N'凤凰涅槃', description = N'死亡后化身火凤凰复活，满状态复活并对周围敌人造成伤害', wuxing_element = 4,
        energy_cost_formula = N'2000', cooldown_formula = N'300', cast_time_formula = N'0', attack_range = 0,
        update_time = GETUTCDATE() WHERE skill_id = 4005;
ELSE
    INSERT INTO Game_HunduShijie_SkillTemplate (skill_id, skill_name, description, skill_type, skill_school, sect_id, profession_limit, max_level, learn_level_require, learn_realm_require, learn_cost, comprehension_require, energy_cost_formula, cooldown_formula, cast_time_formula, attack_range, target_type, effect_range, max_targets, skill_effects, level_effects, combo_skill_ids, can_create, can_advance, icon_path, effect_id, sound_id, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId, wuxing_element, skill_attack, skill_penetration) VALUES (4005, N'凤凰涅槃', N'死亡后化身火凤凰复活，满状态复活并对周围敌人造成伤害', 1, 0, 0, '', 10, 1, 0, '', 0, N'2000', N'300', N'0', 0, 0, 0, 1, N'', N'', '', 0, 0, '', 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000', 4, 0, 0);

IF EXISTS (SELECT 1 FROM Game_HunduShijie_SkillTemplate WHERE skill_id = 5001)
    UPDATE Game_HunduShijie_SkillTemplate SET skill_name = N'岩甲术', description = N'凝聚岩石护甲，大幅提升防御力，但降低移动速度', wuxing_element = 5,
        energy_cost_formula = N'80', cooldown_formula = N'20', cast_time_formula = N'0.5', attack_range = 0,
        update_time = GETUTCDATE() WHERE skill_id = 5001;
ELSE
    INSERT INTO Game_HunduShijie_SkillTemplate (skill_id, skill_name, description, skill_type, skill_school, sect_id, profession_limit, max_level, learn_level_require, learn_realm_require, learn_cost, comprehension_require, energy_cost_formula, cooldown_formula, cast_time_formula, attack_range, target_type, effect_range, max_targets, skill_effects, level_effects, combo_skill_ids, can_create, can_advance, icon_path, effect_id, sound_id, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId, wuxing_element, skill_attack, skill_penetration) VALUES (5001, N'岩甲术', N'凝聚岩石护甲，大幅提升防御力，但降低移动速度', 1, 0, 0, '', 10, 1, 0, '', 0, N'80', N'20', N'0.5', 0, 0, 0, 1, N'', N'', '', 0, 0, '', 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000', 5, 0, 0);

IF EXISTS (SELECT 1 FROM Game_HunduShijie_SkillTemplate WHERE skill_id = 5002)
    UPDATE Game_HunduShijie_SkillTemplate SET skill_name = N'地刺术', description = N'从地面召唤岩石尖刺连续攻击敌人', wuxing_element = 5,
        energy_cost_formula = N'100', cooldown_formula = N'12', cast_time_formula = N'0.5', attack_range = 15,
        update_time = GETUTCDATE() WHERE skill_id = 5002;
ELSE
    INSERT INTO Game_HunduShijie_SkillTemplate (skill_id, skill_name, description, skill_type, skill_school, sect_id, profession_limit, max_level, learn_level_require, learn_realm_require, learn_cost, comprehension_require, energy_cost_formula, cooldown_formula, cast_time_formula, attack_range, target_type, effect_range, max_targets, skill_effects, level_effects, combo_skill_ids, can_create, can_advance, icon_path, effect_id, sound_id, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId, wuxing_element, skill_attack, skill_penetration) VALUES (5002, N'地刺术', N'从地面召唤岩石尖刺连续攻击敌人', 1, 0, 0, '', 10, 1, 0, '', 0, N'100', N'12', N'0.5', 15, 0, 0, 1, N'', N'', '', 0, 0, '', 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000', 5, 0, 0);

IF EXISTS (SELECT 1 FROM Game_HunduShijie_SkillTemplate WHERE skill_id = 5003)
    UPDATE Game_HunduShijie_SkillTemplate SET skill_name = N'山岳护盾', description = N'生成坚固护盾吸收伤害，护盾破碎时反伤周围敌人', wuxing_element = 5,
        energy_cost_formula = N'150', cooldown_formula = N'30', cast_time_formula = N'0.8', attack_range = 0,
        update_time = GETUTCDATE() WHERE skill_id = 5003;
ELSE
    INSERT INTO Game_HunduShijie_SkillTemplate (skill_id, skill_name, description, skill_type, skill_school, sect_id, profession_limit, max_level, learn_level_require, learn_realm_require, learn_cost, comprehension_require, energy_cost_formula, cooldown_formula, cast_time_formula, attack_range, target_type, effect_range, max_targets, skill_effects, level_effects, combo_skill_ids, can_create, can_advance, icon_path, effect_id, sound_id, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId, wuxing_element, skill_attack, skill_penetration) VALUES (5003, N'山岳护盾', N'生成坚固护盾吸收伤害，护盾破碎时反伤周围敌人', 1, 0, 0, '', 10, 1, 0, '', 0, N'150', N'30', N'0.8', 0, 0, 0, 1, N'', N'', '', 0, 0, '', 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000', 5, 0, 0);

IF EXISTS (SELECT 1 FROM Game_HunduShijie_SkillTemplate WHERE skill_id = 5004)
    UPDATE Game_HunduShijie_SkillTemplate SET skill_name = N'地动山摇', description = N'猛击地面引发地震，击飞并眩晕范围内所有敌人', wuxing_element = 5,
        energy_cost_formula = N'250', cooldown_formula = N'35', cast_time_formula = N'1.5', attack_range = 0,
        update_time = GETUTCDATE() WHERE skill_id = 5004;
ELSE
    INSERT INTO Game_HunduShijie_SkillTemplate (skill_id, skill_name, description, skill_type, skill_school, sect_id, profession_limit, max_level, learn_level_require, learn_realm_require, learn_cost, comprehension_require, energy_cost_formula, cooldown_formula, cast_time_formula, attack_range, target_type, effect_range, max_targets, skill_effects, level_effects, combo_skill_ids, can_create, can_advance, icon_path, effect_id, sound_id, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId, wuxing_element, skill_attack, skill_penetration) VALUES (5004, N'地动山摇', N'猛击地面引发地震，击飞并眩晕范围内所有敌人', 1, 0, 0, '', 10, 1, 0, '', 0, N'250', N'35', N'1.5', 0, 0, 0, 1, N'', N'', '', 0, 0, '', 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000', 5, 0, 0);

IF EXISTS (SELECT 1 FROM Game_HunduShijie_SkillTemplate WHERE skill_id = 5005)
    UPDATE Game_HunduShijie_SkillTemplate SET skill_name = N'泰山压顶', description = N'召唤巨大岩石从天而降，压制目标区域的敌人', wuxing_element = 5,
        energy_cost_formula = N'200', cooldown_formula = N'28', cast_time_formula = N'1', attack_range = 20,
        update_time = GETUTCDATE() WHERE skill_id = 5005;
ELSE
    INSERT INTO Game_HunduShijie_SkillTemplate (skill_id, skill_name, description, skill_type, skill_school, sect_id, profession_limit, max_level, learn_level_require, learn_realm_require, learn_cost, comprehension_require, energy_cost_formula, cooldown_formula, cast_time_formula, attack_range, target_type, effect_range, max_targets, skill_effects, level_effects, combo_skill_ids, can_create, can_advance, icon_path, effect_id, sound_id, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId, wuxing_element, skill_attack, skill_penetration) VALUES (5005, N'泰山压顶', N'召唤巨大岩石从天而降，压制目标区域的敌人', 1, 0, 0, '', 10, 1, 0, '', 0, N'200', N'28', N'1', 20, 0, 0, 1, N'', N'', '', 0, 0, '', 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000', 5, 0, 0);

IF EXISTS (SELECT 1 FROM Game_HunduShijie_SkillTemplate WHERE skill_id = 5006)
    UPDATE Game_HunduShijie_SkillTemplate SET skill_name = N'厚土载物', description = N'与大地融为一体，免疫控制，大幅提升防御并持续恢复生命', wuxing_element = 5,
        energy_cost_formula = N'500', cooldown_formula = N'120', cast_time_formula = N'1.5', attack_range = 0,
        update_time = GETUTCDATE() WHERE skill_id = 5006;
ELSE
    INSERT INTO Game_HunduShijie_SkillTemplate (skill_id, skill_name, description, skill_type, skill_school, sect_id, profession_limit, max_level, learn_level_require, learn_realm_require, learn_cost, comprehension_require, energy_cost_formula, cooldown_formula, cast_time_formula, attack_range, target_type, effect_range, max_targets, skill_effects, level_effects, combo_skill_ids, can_create, can_advance, icon_path, effect_id, sound_id, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId, wuxing_element, skill_attack, skill_penetration) VALUES (5006, N'厚土载物', N'与大地融为一体，免疫控制，大幅提升防御并持续恢复生命', 1, 0, 0, '', 10, 1, 0, '', 0, N'500', N'120', N'1.5', 0, 0, 0, 1, N'', N'', '', 0, 0, '', 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000', 5, 0, 0);

IF EXISTS (SELECT 1 FROM Game_HunduShijie_SkillTemplate WHERE skill_id = 6001)
    UPDATE Game_HunduShijie_SkillTemplate SET skill_name = N'凌波微步', description = N'以精妙身法瞬间位移到目标位置，短暂无敌。', wuxing_element = 3,
        energy_cost_formula = N'20', cooldown_formula = N'6', cast_time_formula = N'0', attack_range = 10,
        update_time = GETUTCDATE() WHERE skill_id = 6001;
ELSE
    INSERT INTO Game_HunduShijie_SkillTemplate (skill_id, skill_name, description, skill_type, skill_school, sect_id, profession_limit, max_level, learn_level_require, learn_realm_require, learn_cost, comprehension_require, energy_cost_formula, cooldown_formula, cast_time_formula, attack_range, target_type, effect_range, max_targets, skill_effects, level_effects, combo_skill_ids, can_create, can_advance, icon_path, effect_id, sound_id, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId, wuxing_element, skill_attack, skill_penetration) VALUES (6001, N'凌波微步', N'以精妙身法瞬间位移到目标位置，短暂无敌。', 1, 0, 0, '', 10, 1, 0, '', 0, N'20', N'6', N'0', 10, 0, 0, 1, N'', N'', '', 0, 0, '', 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000', 3, 0, 0);

IF EXISTS (SELECT 1 FROM Game_HunduShijie_SkillTemplate WHERE skill_id = 6002)
    UPDATE Game_HunduShijie_SkillTemplate SET skill_name = N'风驰电掣', description = N'化为疾风向前突进，路径上敌人受到切割伤害。', wuxing_element = 2,
        energy_cost_formula = N'25', cooldown_formula = N'5', cast_time_formula = N'0', attack_range = 12,
        update_time = GETUTCDATE() WHERE skill_id = 6002;
ELSE
    INSERT INTO Game_HunduShijie_SkillTemplate (skill_id, skill_name, description, skill_type, skill_school, sect_id, profession_limit, max_level, learn_level_require, learn_realm_require, learn_cost, comprehension_require, energy_cost_formula, cooldown_formula, cast_time_formula, attack_range, target_type, effect_range, max_targets, skill_effects, level_effects, combo_skill_ids, can_create, can_advance, icon_path, effect_id, sound_id, create_time, update_time, IsValid, GameId, AreaId, ServerId, GameUserId, wuxing_element, skill_attack, skill_penetration) VALUES (6002, N'风驰电掣', N'化为疾风向前突进，路径上敌人受到切割伤害。', 1, 0, 0, '', 10, 1, 0, '', 0, N'25', N'5', N'0', 12, 0, 0, 1, N'', N'', '', 0, 0, '', 0, 0, GETUTCDATE(), GETUTCDATE(), 1, 0, 0, 0, '00000000-0000-0000-0000-000000000000', 2, 0, 0);


SET IDENTITY_INSERT dbo.Game_HunduShijie_SkillTemplate OFF;
