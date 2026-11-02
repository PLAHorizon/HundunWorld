-- 010_move_metal_1003_to_jinzhongzhao.sql
-- 把 1003 归还给运行时真正实现的「金钟罩」，原 1003「万剑归宗」整体搬到新 id 1004。
--
-- 为什么这样切，而不是反过来：
--   金钟罩有两处实现证据——MetalSkills.JinZhongZhao（5 秒内减少 50% 所受伤害）与
--   SkillAnimationMapping 里 1003 的 SkillAnimationType.Defense 动画；
--   万剑归宗此前只有表里一行 + SkillDatabase 一条，没有任何运行时实现。
--   让"有实现的"占住原 id、"没实现的"挪到新号，才不会造出第二次错位
--   （1003 表里写着攻击终结技、运行时放出来却是减伤）。
--
-- 连招链同步改指：SkillDatabase 里 1002 的 ComboNextId 由 1003 改到 1004，
--   终结技仍然是万剑归宗，只是换了号。
--
-- 007 脚本头注释里记的那批 id 冲突，金系这一条由本脚本收尾。
-- 可重复执行（1004 已存在时只跳过搬迁，不重复插入）。
-- 回退：
--   DELETE FROM Game_HunduShijie_SkillTemplate WHERE skill_id = 1004;
--   然后按 007 里 1003 那一行的原文把 skill_name/description/公式/射程改回去。

SET NOCOUNT ON;

-- 1) 整行复制成 1004：用 SELECT * INTO 保住所有列，只改 id 与文案，避免漏抄列。
IF NOT EXISTS (SELECT 1 FROM Game_HunduShijie_SkillTemplate WHERE skill_id = 1004)
BEGIN
    -- 不能用 SELECT * INTO 再改 skill_id：identity 属性会被临时表继承，UPDATE 直接报 8102。
    -- 所以按列显式搬迁，需要改的列写死，其余列原样带过来。
    SET IDENTITY_INSERT Game_HunduShijie_SkillTemplate ON;
    INSERT INTO Game_HunduShijie_SkillTemplate (
           skill_id,
           skill_name,
           description,
           skill_type,
           skill_school,
           sect_id,
           profession_limit,
           max_level,
           learn_level_require,
           learn_realm_require,
           pre_skill_id,
           pre_skill_level,
           learn_cost,
           comprehension_require,
           energy_cost_formula,
           cooldown_formula,
           cast_time_formula,
           attack_range,
           target_type,
           effect_range,
           max_targets,
           skill_effects,
           level_effects,
           combo_skill_ids,
           can_create,
           can_advance,
           advance_skill_id,
           icon_path,
           effect_id,
           sound_id,
           create_time,
           update_time,
           IsValid,
           GameId,
           AreaId,
           ServerId,
           GameUserId,
           skill_attack,
           skill_penetration,
           wuxing_element)
    SELECT 1004 AS skill_id,
           N'万剑归宗' AS skill_name,
           N'连招终结技，内力爆发化为无数剑气横扫前方。' AS description,
           s.skill_type,
           s.skill_school,
           s.sect_id,
           s.profession_limit,
           s.max_level,
           5 AS learn_level_require,
           s.learn_realm_require,
           1002 AS pre_skill_id,
           s.pre_skill_level,
           s.learn_cost,
           s.comprehension_require,
           s.energy_cost_formula,
           s.cooldown_formula,
           s.cast_time_formula,
           s.attack_range,
           s.target_type,
           s.effect_range,
           s.max_targets,
           s.skill_effects,
           s.level_effects,
           s.combo_skill_ids,
           s.can_create,
           s.can_advance,
           s.advance_skill_id,
           s.icon_path,
           s.effect_id,
           s.sound_id,
           s.create_time,
           GETUTCDATE() AS update_time,
           s.IsValid,
           s.GameId,
           s.AreaId,
           s.ServerId,
           s.GameUserId,
           s.skill_attack,
           s.skill_penetration,
           1 AS wuxing_element
      FROM Game_HunduShijie_SkillTemplate s
     WHERE s.skill_id = 1003;
    SET IDENTITY_INSERT Game_HunduShijie_SkillTemplate OFF;
END

-- 2) 1003 改成金钟罩：数值取运行时 JinZhongZhao 的规格（5 秒减伤 50%），
--    内力/冷却/施法取同定位防御技 5001「岩甲术」那一档（80 / 20 / 0.5），不另立一档。
UPDATE Game_HunduShijie_SkillTemplate
   SET skill_name = N'金钟罩',
       description = N'运起金钟罩，5 秒内减少 50% 所受伤害。',
       energy_cost_formula = '80',
       cooldown_formula = '20',
       cast_time_formula = '0.5',
       attack_range = 0,
       effect_range = 0,
       max_targets = 1,
       pre_skill_id = NULL,
       wuxing_element = 1,
       update_time = GETUTCDATE()
 WHERE skill_id = 1003;

-- 3) 自检：金系应为 1001-1004 四行，且 1003=金钟罩 / 1004=万剑归宗。
SELECT skill_id,
       CASE skill_id WHEN 1003 THEN N'金钟罩' WHEN 1004 THEN N'万剑归宗' ELSE skill_name END AS expected,
       skill_name, wuxing_element, energy_cost_formula, cooldown_formula, pre_skill_id
  FROM Game_HunduShijie_SkillTemplate
 WHERE skill_id BETWEEN 1001 AND 1004
 ORDER BY skill_id;
