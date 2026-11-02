/* ============================================================================
  017_seed_skill_level_effects.sql —— 给 91 式填 level_effects（#30 的"先给等级一点真收益"）

  为什么要有这一份（事实，不是设想）：
    · skill.Level 今天在**伤害结算里一个读者都没有**；91 行的 level_effects 全是空串。
    · 于是"精研到圆满"的净效果是负的：升级只顺手改了耗气(×1.1)与射程(+0.5)，倍率不涨。
    · 而 §6.2 的演化前置要求"源式练满"才允许演化——那条前置目前是**纯税**，交钱买不到战力。
    · 用户 2026-10-06 拍板：先给等级一点真收益，再谈精研成本。这一份就是收益侧的数据。

  读取侧契约（必须与实现同形，已由 WuxingStatusEffectPlanTests 钉住）：
    · 键住在**顶层**：{"damagePerLevel":0.02}
      —— WuxingSkillEffects.ReadNumber 只看根对象；写成 {"perLevel":{...}} 会安静地读成"没配"→ 每级 +0%。
    · 语义 = **每多一重 +damagePerLevel 的最终伤害**，第 1 重是基准不加；max_level=10 ⇒ 满重加成 = damagePerLevel × 9。
    · 实现侧不给默认值：没配就是 0（"没配 = 没收益"是真话，不许退化成全局 +2%）。

  **与既有客户端产物的关系（本轮实测发现，先写在这里免得下一个人再撞）**：
    · HundunWorld/Content/GameData/Skills/skills.json 的 91 行**早就带 damagePerLevel**，
      值是 0.11/0.12/0.13/0.14/0.15/0.16/0.17（按 skill_grade 递增一档加 0.01）。
    · 但 `SkillConfig.DamagePerLevel` 在客户端**零读者**（除 DTO 反序列化外无人用），服务端也不读 level_effects——
      所以那是一个谁都没结算过的承诺，不是既成事实。
    · 键名本轮已刻意与它对齐（都用顶层 `damagePerLevel`），**剩下的唯一分歧是幅度**：
      那套占位值是每重 +11%~+17%（满重 +99%~+153%），本脚本提的是 +2%/+1.5%/+1%/+0.8%。
      两者只能留一个：入库前请一并定幅度，然后 skills.json 与本脚本必须写同一个数
      （两端一致的既有纪律见 §14.7 G16 的"两份产物一致"闸门）。

  分档口径（阶位越高、每重加成越小：天阶/道阶的倍率本身已经很高，再给高斜率会让阶梯失稳）：
    白·黄·玄（grade 1/2/3）→ 0.020/重 → 满重 +18%   —— 45 行
    地·天（grade 4/5）      → 0.015/重 → 满重 +13.5% —— 30 行
    道阶（grade 6）         → 0.010/重 → 满重 +9%    —— 15 行
    混沌一炁（grade 7）     → 0.008/重 → 满重 +7.2%  ——  1 行

  id 谓词按编号契约反解，不用大范围 BETWEEN 扫（10101..50503 单看会误伤 10601 这类不存在的行）：
    基础式 = 行*10000 + 阶*100 + 境，行∈1..5、阶∈1..5、境∈1..3
    道阶   = 70000 + 对*100 + 境，对∈1..5、境∈1..3
    混沌   = 90001
  三条口径已由离线校验对 011 实测过：91 行分档 45/30/15/1、档间零重叠、零漏行。

  幂等：四条 UPDATE 各自只写自己那一档的字面量，重复执行结果不变。
  **本脚本只生成、不自动执行**；执行与否与时机由用户决定：
    sqlcmd -S <host> -d <db> -f 65001 -i scripts/sql/017_seed_skill_level_effects.sql

  结构说明（012 那次三重假安全的教训在这儿同样适用）：
    · 不用 `IF <条件>;` 这种裸语句——它会让整批中止而一行都不写，且不留痕迹；
    · 不套 BEGIN TRY：TRY 里的 RAISERROR(16) 会跳进 CATCH，把"已经回滚好了"这件事
      变成第二次报错 + 一句多余的 CATCH 消息；判据失败就在 ELSE 分支外的一支里
      ROLLBACK + RAISERROR，成功分支才 COMMIT；
    · 自检用**字面量全等**而不是 ISJSON/JSON_VALUE：别的行有过 JSON 文本非法导致
      Msg 13609 的先例，而这里要的正是"写进去的就是这四个串之一"，等值判据更硬也更快；
    · 自检失败必须**整批回滚**，不留"半档写了半档没写"的状态。
   ============================================================================ */

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

    /* --- 白 / 黄 / 玄：每重 +2% 最终伤害（45 行 = 5 行 × 3 阶 × 3 境） --- */
    UPDATE Game_HunduShijie_SkillTemplate
    SET level_effects = N'{"damagePerLevel":0.02}',
        update_time   = GETUTCDATE()
    WHERE IsValid = 1
      AND skill_id / 10000 BETWEEN 1 AND 5
      AND (skill_id % 10000) / 100 IN (1, 2, 3)
      AND skill_id % 100 BETWEEN 1 AND 3;

    /* --- 地 / 天：每重 +1.5%（30 行 = 5 行 × 2 阶 × 3 境） --- */
    UPDATE Game_HunduShijie_SkillTemplate
    SET level_effects = N'{"damagePerLevel":0.015}',
        update_time   = GETUTCDATE()
    WHERE IsValid = 1
      AND skill_id / 10000 BETWEEN 1 AND 5
      AND (skill_id % 10000) / 100 IN (4, 5)
      AND skill_id % 100 BETWEEN 1 AND 3;

    /* --- 道阶（超越天阶的相生演化式）：每重 +1%（15 行 = 5 对 × 3 境） --- */
    UPDATE Game_HunduShijie_SkillTemplate
    SET level_effects = N'{"damagePerLevel":0.01}',
        update_time   = GETUTCDATE()
    WHERE IsValid = 1
      AND skill_id / 10000 = 7
      AND (skill_id % 10000) / 100 BETWEEN 1 AND 5
      AND skill_id % 100 BETWEEN 1 AND 3;

    /* --- 混沌一炁：每重 +0.8%（1 行） --- */
    UPDATE Game_HunduShijie_SkillTemplate
    SET level_effects = N'{"damagePerLevel":0.008}',
        update_time   = GETUTCDATE()
    WHERE IsValid = 1
      AND skill_id = 90001;

/* --------------------------------------------------------------------------
   回读自检：先数形状（每档该有多少行），再数内容（是否正好是这四个字面量）。
   形状不对 = 编号契约被改过，本脚本的档划分就是错的，必须整批回滚而不是半写。
   -------------------------------------------------------------------------- */
DECLARE @n_low int = (SELECT COUNT(*) FROM Game_HunduShijie_SkillTemplate
                      WHERE skill_id / 10000 BETWEEN 1 AND 5
                        AND (skill_id % 10000) / 100 IN (1, 2, 3)
                        AND skill_id % 100 BETWEEN 1 AND 3);
DECLARE @n_mid int = (SELECT COUNT(*) FROM Game_HunduShijie_SkillTemplate
                      WHERE skill_id / 10000 BETWEEN 1 AND 5
                        AND (skill_id % 10000) / 100 IN (4, 5)
                        AND skill_id % 100 BETWEEN 1 AND 3);
DECLARE @n_dao int = (SELECT COUNT(*) FROM Game_HunduShijie_SkillTemplate
                      WHERE skill_id / 10000 = 7
                        AND (skill_id % 10000) / 100 BETWEEN 1 AND 5
                        AND skill_id % 100 BETWEEN 1 AND 3);
DECLARE @n_chaos int = (SELECT COUNT(*) FROM Game_HunduShijie_SkillTemplate
                        WHERE skill_id = 90001);

DECLARE @bad_low int = (SELECT COUNT(*) FROM Game_HunduShijie_SkillTemplate
                        WHERE skill_id / 10000 BETWEEN 1 AND 5
                          AND (skill_id % 10000) / 100 IN (1, 2, 3)
                          AND skill_id % 100 BETWEEN 1 AND 3
                          AND level_effects <> N'{"damagePerLevel":0.02}');
DECLARE @bad_mid int = (SELECT COUNT(*) FROM Game_HunduShijie_SkillTemplate
                        WHERE skill_id / 10000 BETWEEN 1 AND 5
                          AND (skill_id % 10000) / 100 IN (4, 5)
                          AND skill_id % 100 BETWEEN 1 AND 3
                          AND level_effects <> N'{"damagePerLevel":0.015}');
DECLARE @bad_dao int = (SELECT COUNT(*) FROM Game_HunduShijie_SkillTemplate
                        WHERE skill_id / 10000 = 7
                          AND (skill_id % 10000) / 100 BETWEEN 1 AND 5
                          AND skill_id % 100 BETWEEN 1 AND 3
                          AND level_effects <> N'{"damagePerLevel":0.01}');
DECLARE @bad_chaos int = (SELECT COUNT(*) FROM Game_HunduShijie_SkillTemplate
                          WHERE skill_id = 90001
                            AND level_effects <> N'{"damagePerLevel":0.008}');

IF @@TRANCOUNT > 0
BEGIN
    IF @n_low <> 45 OR @n_mid <> 30 OR @n_dao <> 15 OR @n_chaos <> 1
    BEGIN
        ROLLBACK TRANSACTION;
        RAISERROR(N'017 中止并已回滚：五行技能行的形状不是 45/30/15/1（实得 %d/%d/%d/%d）。编号契约（行*10000+阶*100+境）若被改过，本脚本的分档就是错的，不许半写。', 16, 1, @n_low, @n_mid, @n_dao, @n_chaos);
    END
    ELSE IF @bad_low + @bad_mid + @bad_dao + @bad_chaos <> 0
    BEGIN
        ROLLBACK TRANSACTION;
        RAISERROR(N'017 中止并已回滚：回读有 %d 行的 level_effects 不是本脚本写进去的字面量（多半与别的会话同批改过同一张表）。整批回滚，不留半态。', 16, 2, @bad_low + @bad_mid + @bad_dao + @bad_chaos);
    END
    ELSE
    BEGIN
        COMMIT TRANSACTION;

        SELECT N'017 完成' AS result,
               @n_low + @n_mid + @n_dao + @n_chaos AS rows_with_level_effects,
               @n_low AS grade_1_2_3, @n_mid AS grade_4_5, @n_dao AS dao, @n_chaos AS chaos;
    END
END

/* ---------------------------------------------------------------------------
   需要复位时手工执行（同样只生成、不自动跑）：

   UPDATE Game_HunduShijie_SkillTemplate
   SET level_effects = N''
   WHERE IsValid = 1
     AND level_effects IN (N'{"damagePerLevel":0.02}', N'{"damagePerLevel":0.015}',
                           N'{"damagePerLevel":0.01}',  N'{"damagePerLevel":0.008}');
   --------------------------------------------------------------------------- */
