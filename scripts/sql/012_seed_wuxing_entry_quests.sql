-- ============================================================================
-- 012 五行入门任务种子（方案 §14.2：初级技能书的"任务"产出通路）
--
-- 为什么要有这张表：习得门槛（§14 好感/材料/属性）要成立，前提是这些产出物有一条
-- **服务端说了算**的来源。此前 HandleAcceptQuestAsync 是空桩、库里也没有任务表，
-- 于是"通过任务获得初级技能书"只存在于代码与测试里。任务模板落在服务端，
-- 接受时客户端只报 questId，奖励一律读本表——否则客户端自己声明 rewards 就能凭空造书。
--
-- 幂等：每条先 IF EXISTS 再 UPDATE/INSERT。quest_id 是 IDENTITY 列，
-- 所以整段必须包在 SET IDENTITY_INSERT 之内（且一个会话只允许一表为 ON）。
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

SET IDENTITY_INSERT dbo.Game_HunduShijie_QuestTemplate ON;

-- 每域一条入门任务：发该域白阶三本秘籍 + 2 枚该域信物 + 20 点该域好感。
-- 20 点正好够"玄·初"（favorNeed.min = 20）；玄·中/极要靠信物与该域往来继续堆，
-- 这符合设计意图——中级技能的解锁是"与这一域的往来"，不是交一次任务就封顶。
-- 信物 id = 910010 + 域号（011 种子建的五行信物）；白阶书 id = 800000 + 域*10000 + 100 + 境。
-- 用临时表而不是 CTE：CTE 的作用域只到它所在的那一条语句，下面的 UPDATE 会认不出它。
IF OBJECT_ID('tempdb..#wuxing_domains') IS NOT NULL DROP TABLE #wuxing_domains;
CREATE TABLE #wuxing_domains (
    d INT NOT NULL,
    element_cn NVARCHAR(4) NOT NULL,
    quest_name NVARCHAR(50) NOT NULL,
    quest_desc NVARCHAR(200) NOT NULL,
);
INSERT INTO #wuxing_domains (d, element_cn, quest_name, quest_desc) VALUES
    (1, N'金', N'金行·入门「磨锋问刃」', N'金域的第一次往来：带回该域三式白阶心得，并留下可换好感的信物。'),
    (2, N'木', N'木行·入门「问脉寻梢」', N'木域的第一次往来：带回该域三式白阶心得，并留下可换好感的信物。'),
    (3, N'水', N'水行·入门「观流知源」', N'水域的第一次往来：带回该域三式白阶心得，并留下可换好感的信物。'),
    (4, N'火', N'火行·入门「听焚识焰」', N'火域的第一次往来：带回该域三式白阶心得，并留下可换好感的信物。'),
    (5, N'土', N'土行·入门「量土知重」', N'土域的第一次往来：带回该域三式白阶心得，并留下可换好感的信物。');

INSERT INTO dbo.Game_HunduShijie_QuestTemplate
    (quest_id, quest_name, description, quest_type, level_require, wuxing_domain,
     rewards, objectives, repeat_limit, IsValid, GameId, AreaId, ServerId, GameUserId)
SELECT
    90000 + d,
    quest_name,
    quest_desc,
    1,                                       -- 1 = 支线
    1,                                       -- 入门任务：1 级可接
    d,                                       -- 五行域 1金 2木 3水 4火 5土
    N'{"item:' + CAST(800000 + d * 10000 + 101 AS NVARCHAR(10)) + N'":1,"item:' + CAST(800000 + d * 10000 + 102 AS NVARCHAR(10)) + N'":1,"item:' + CAST(800000 + d * 10000 + 103 AS NVARCHAR(10)) + N'":1,"item:' + CAST(910010 + d AS NVARCHAR(10)) + N'":2,"favor:' + CAST(d AS NVARCHAR(2)) + N'":20,"exp":80}',
    N'[{"type":"kill_domain:' + CAST(d AS NVARCHAR(2)) + N'","desc":"猎杀 5 只' + element_cn + N'异兽","count":5}]',
                                             -- 该域击杀目标：任务真的要求做点什么，否则它就是个空转按钮
    0,                                       -- 0 = 一次性（状态机已禁止重复接受）
    1, 0, 0, 0, '00000000-0000-0000-0000-000000000000'
FROM #wuxing_domains AS src
WHERE NOT EXISTS (SELECT 1 FROM dbo.Game_HunduShijie_QuestTemplate t WHERE t.quest_id = 90000 + src.d);

-- 已存在的行也要跟着设计走：奖励/域/描述以本种子为准重写（幂等，重跑不漂移）。
UPDATE t
SET quest_name = src.quest_name,
    description = src.quest_desc,
    quest_type = 1,
    level_require = 1,
    wuxing_domain = src.d,
    rewards = N'{"item:' + CAST(800000 + src.d * 10000 + 101 AS NVARCHAR(10)) + N'":1,"item:' + CAST(800000 + src.d * 10000 + 102 AS NVARCHAR(10)) + N'":1,"item:' + CAST(800000 + src.d * 10000 + 103 AS NVARCHAR(10)) + N'":1,"item:' + CAST(910010 + src.d AS NVARCHAR(10)) + N'":2,"favor:' + CAST(src.d AS NVARCHAR(2)) + N'":20,"exp":80}',
    objectives = N'[{"type":"kill_domain:' + CAST(src.d AS NVARCHAR(2)) + N'","desc":"猎杀 5 只' + src.element_cn + N'异兽","count":5}]',
    repeat_limit = 0,
    IsValid = 1
FROM dbo.Game_HunduShijie_QuestTemplate AS t
JOIN #wuxing_domains AS src ON t.quest_id = 90000 + src.d;

SET IDENTITY_INSERT dbo.Game_HunduShijie_QuestTemplate OFF;

-- 自检：任务里引用的每一件物品都必须真的存在（否则现场表现是"交任务了但书没到手"，
-- 而发奖侧只会留一条 warning，玩家与策划都对不上账）。
IF EXISTS (
    SELECT 1
    FROM dbo.Game_HunduShijie_QuestTemplate q
    CROSS APPLY OPENJSON(q.rewards) AS j
    WHERE LEFT(j.[key], 5) = 'item:'
      AND NOT EXISTS (
          SELECT 1 FROM dbo.Game_HunduShijie_ItemTemplate i
          WHERE i.item_id = TRY_CAST(SUBSTRING(j.[key], 6, 10) AS INT)))
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR(N'012 种子：任务奖励引用了不存在的物品模板，整批回滚。', 16, 1) WITH NOWAIT;
END;

-- 自检：好感键的域号必须落在 1..5（0 或越界会静默加到一个没人读的域上）。
IF EXISTS (
    SELECT 1
    FROM dbo.Game_HunduShijie_QuestTemplate q
    CROSS APPLY OPENJSON(q.rewards) AS j
    WHERE LEFT(j.[key], 6) = 'favor:'
      AND TRY_CAST(SUBSTRING(j.[key], 7, 3) AS INT) NOT BETWEEN 1 AND 5)
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR(N'012 种子：favor 键的五行域号越界，整批回滚。', 16, 1) WITH NOWAIT;
END;

-- 自检 3：每条入门任务必须带一个"本域击杀"目标，且域号与 wuxing_domain 一致。
-- 目标缺失 = 接了就能交（产出通路空转）；域号写错 = 目标永远推不动（服务端把非法域号拒了），
-- 任务卡死在"进行中"而玩家什么提示都拿不到。两种都宁可不上线。
--
-- 【这段原先是三重假安全，2026-10-05 实跑才被 SQL Server 打回】：
-- ① `IF <条件>;` 的分号是语法错误（Msg 102），整批一个字节都没写进去；
-- ② 就算去掉分号，`IF @@ROWCOUNT > 0` 读的也不是"存不存在坏行"，这条永远不成立；
-- ③ 它写在 `COMMIT` 之后，真要触发时 `ROLLBACK` 只会报"没有对应 BEGIN"。
-- 现在把它挪到提交之前，用 IF/ELSE 决定提交还是回滚，并加 `@@TRANCOUNT` 护栏——
-- 前面几条自检的 ROLLBACK+RAISERROR 不会中断批处理，少了这个护栏就会在回滚后再次 COMMIT 报错。
IF @@TRANCOUNT > 0
BEGIN
    IF EXISTS (
        SELECT 1 FROM dbo.Game_HunduShijie_QuestTemplate AS q
        WHERE q.quest_id BETWEEN 90001 AND 90005
          AND (q.objectives IS NULL OR DATALENGTH(q.objectives) < 3
               OR NOT EXISTS (SELECT 1 FROM OPENJSON(q.objectives)))
    ) OR EXISTS (
        SELECT 1 FROM dbo.Game_HunduShijie_QuestTemplate AS q
        CROSS APPLY OPENJSON(q.objectives) AS j
        WHERE q.quest_id BETWEEN 90001 AND 90005
          AND (JSON_VALUE(j.value, '$.type') NOT LIKE 'kill_domain:%'
               OR TRY_CAST(REPLACE(JSON_VALUE(j.value, '$.type'), 'kill_domain:', '') AS INT) <> q.wuxing_domain
               OR TRY_CAST(JSON_VALUE(j.value, '$.count') AS INT) IS NULL
               OR TRY_CAST(JSON_VALUE(j.value, '$.count') AS INT) <= 0)
    )
    BEGIN
        ROLLBACK TRANSACTION;
        RAISERROR(N'012 种子：入门任务的击杀目标缺失或与五行域不一致，整批回滚。', 16, 1) WITH NOWAIT;
    END
    ELSE
    BEGIN
        COMMIT TRANSACTION;
    END
END;

SELECT COUNT(*) AS entry_quest_rows,
       SUM(CASE WHEN wuxing_domain BETWEEN 1 AND 5 THEN 1 ELSE 0 END) AS with_valid_domain,
       -- 只有回滚没发生，这里才会是 5；把它放在汇报里，跑完一眼就能看到"到底写进去没有"
       SUM(CASE WHEN DATALENGTH(objectives) >= 3 THEN 1 ELSE 0 END) AS with_objectives
FROM dbo.Game_HunduShijie_QuestTemplate
WHERE quest_id BETWEEN 90001 AND 90005;

DROP TABLE #wuxing_domains;
