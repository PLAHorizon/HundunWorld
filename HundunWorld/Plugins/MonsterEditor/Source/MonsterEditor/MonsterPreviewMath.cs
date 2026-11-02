using System;

namespace MonsterEditor
{
    /// <summary>
    /// 编辑器侧的难度与经验曲线预览数学（<c>Horizon.Game.Sim.Monsters.MonsterDifficultyCore / MonsterXpCore</c> 的独立镜像）。
    /// <para>
    /// <b>为什么这里"再写一遍"而不是引用</b>：Flax 脚本域不能引用仓库工程；
    /// 而曲线视图必须能即时重画（每拖一次滑杆就跑一次），不能等子进程回一轮。
    /// </para>
    /// <para>
    /// <b>风险与闸门</b>：两份实现会同错或会漂移。因此
    /// ① 本文件按"独立参照"写（分段表逐字抄、循环求和而不是查表推导），
    /// ② 本文件**不许引用引擎类型**——它被离线闸门原样编进 .NET 测试工程，
    ///    <c>MonsterEditorContractTests</c> 把同一批等级/倍率**真的调用这里的函数**与内核逐点比对
    ///    （不是按文本抠常量：抠常量只能证明常量相同，证明不了这条式子跑得出同一个数）；
    /// ③ 窗口在每次拉取 <c>preview-xp</c> 回执时再做一次实际比对，不一致就在控制台**红字自证**。
    /// </para>
    /// </summary>
    public static class MonsterPreviewMath
    {
        /// <summary>境界起始等级（与内核 <c>StageStartLevels</c> 同序同值）。</summary>
        public static readonly int[] StageStartLevels = { 1, 1, 31, 76, 131, 201, 300 };

        /// <summary>灰名等级差（按境界）。</summary>
        public static readonly int[] GrayGapByStage = { 5, 5, 6, 7, 9, 10, 12 };

        /// <summary>零差值系数 ZD（按境界）。</summary>
        public static readonly int[] ZeroDifferenceByStage = { 5, 6, 8, 11, 15, 20, 24 };

        /// <summary>rank 经验倍率。</summary>
        public static readonly double[] RankXpScale = { 1.0, 2.0, 2.5, 4.0, 4.0 };

        /// <summary>
        /// 系别下拉的取值表（与 <see cref="SchoolNames"/> **一一对应**，7 项）：
        /// 0 = 未定系、1 = 物理，之后 <b>位号 == 域号</b>（2 金、4 木、8 水、16 火、32 土）。
        /// 与服务端 <c>MonsterSchoolBits</c> 的六个位逐值相同，由 <c>MonsterEditorContractTests</c> 钉住；
        /// 开头那个 0 是"未定系"这个**选项**，不是位。
        /// </summary>
        public static readonly int[] SchoolMaskOptions = { 0, 1, 2, 4, 8, 16, 32 };

        /// <summary>系别下拉的显示名（下标 0 = 未定系，其后依次是物理、金、木、水、火、土）。</summary>
        public static readonly string[] SchoolNames = { "未定系", "物理", "金", "木", "水", "火", "土" };

        /// <summary>施法条配色：下标 0 = 未定/物理（中性），1..5 = 金木水火土。</summary>
        /// <remarks>
        /// 这份副本存在的理由是"脚本域与游戏域互相引用不到"。它与客户端
        /// <c>InkWashTheme.Element*</c> 必须逐值相同——颜色整体错位没有任何一层会报错，
        /// 所以 <c>MonsterEditorContractTests</c> 直接把两边的数字抠出来对账。
        /// </remarks>
        public static readonly float[][] SchoolRgb =
        {
            new[] { 0.72f, 0.74f, 0.78f },                 // 未定/物理：中性，不假装是某个五行色
            new[] { 212f / 255f, 196f / 255f, 160f / 255f },   // 金
            new[] { 107f / 255f, 142f / 255f, 90f / 255f },    // 木
            new[] { 74f / 255f, 110f / 255f, 138f / 255f },    // 水
            new[] { 184f / 255f, 86f / 255f, 56f / 255f },     // 火
            new[] { 138f / 255f, 123f / 255f, 90f / 255f },    // 土
        };

        /// <summary>取掩码里最低的五行位对应的域号（1..5）；纯物理或 0 返回 0（与客户端同一规则）。</summary>
        public static int FirstDomainOf(int schoolMask)
        {
            for (var domain = 1; domain <= 5; domain++)
            {
                if ((schoolMask & (1 << domain)) != 0) return domain;
            }
            return 0;
        }

        /// <summary>该系别的条子颜色（越界回中性，绝不抛）。</summary>
        public static float[] SchoolColor(int schoolMask)
        {
            var domain = FirstDomainOf(schoolMask);
            if (domain < 0 || domain >= SchoolRgb.Length) return SchoolRgb[0];
            return SchoolRgb[domain];
        }

        /// <summary>
        /// 按**域号**索引的系别名（0 = 未定/物理那一档，1..5 = 金木水火土）。
        /// <para>与 <see cref="SchoolNames"/>（按下拉框序号，7 项）刻意分开：
        /// 一个是"域号 → 名字"，一个是"下拉第几行 → 名字"，混用会差一位——
        /// 而差一位的表现是"水系怪显示成物理"这种没人会去核对的错。</para>
        /// </summary>
        public static readonly string[] SchoolDomainLabels = { "未定系", "金", "木", "水", "火", "土" };

        /// <summary>这个掩码该念成什么系：有五行位就念那一系，否则物理位就念"物理"，全空就念"未定系"。</summary>
        public static string SchoolLabel(int schoolMask)
        {
            var domain = FirstDomainOf(schoolMask);
            if (domain > 0 && domain < SchoolDomainLabels.Length) return SchoolDomainLabels[domain];
            return (schoolMask & 1) != 0 ? "物理" : SchoolDomainLabels[0];
        }

        public const int MaxPlayerLevel = 300;
        public const int HighBonusCapLevels = 4;
        public const double HighBonusRate = 0.05;

        /// <summary>难度档（与内核枚举同序，值可直接当色表下标）。</summary>
        public enum Difficulty
        {
            Gray = 0,
            Green = 1,
            Yellow = 2,
            Orange = 3,
            Red = 4,
            Skull = 5,
        }

        public static int StageIndexOf(int level)
        {
            if (level < 1) return 0;
            var stage = 0;
            // 与内核一样用"线性扫全表取最后一个满足项"，而不是二分：
            // 两份实现要能在**算法层**也对得上，否则漂移会伪装成"边界处理不同"。
            for (var i = 0; i < StageStartLevels.Length; i++)
            {
                if (level >= StageStartLevels[i]) stage = i;
            }
            return stage;
        }

        public static int GrayLevel(int playerLevel)
        {
            if (playerLevel <= 5) return 0;
            if (playerLevel > MaxPlayerLevel) playerLevel = MaxPlayerLevel;
            var gap = GrayGapByStage[StageIndexOf(playerLevel)];
            var gray = playerLevel - gap;
            return gray < 0 ? 0 : gray;
        }

        public static int Zd(int playerLevel)
        {
            if (playerLevel < 1) playerLevel = 1;
            if (playerLevel > MaxPlayerLevel) playerLevel = MaxPlayerLevel;
            return ZeroDifferenceByStage[StageIndexOf(playerLevel)];
        }

        public static Difficulty Of(int monsterLevel, int playerLevel, int rank)
        {
            if (rank >= 3) return Difficulty.Skull;
            if (playerLevel < 1) playerLevel = 1;
            var diff = monsterLevel - playerLevel;
            if (diff >= 10) return Difficulty.Skull;
            if (diff >= 5) return Difficulty.Red;
            if (diff >= 3) return Difficulty.Orange;
            if (diff >= -2) return Difficulty.Yellow;
            if (monsterLevel <= GrayLevel(playerLevel)) return Difficulty.Gray;
            return Difficulty.Green;
        }

        /// <summary>让分系数（灰名断供为 0）。</summary>
        public static double Concession(int monsterLevel, int playerLevel, int rank)
        {
            if (Of(monsterLevel, playerLevel, rank) == Difficulty.Gray) return 0.0;
            if (monsterLevel > playerLevel)
            {
                var over = monsterLevel - playerLevel;
                if (over > HighBonusCapLevels) over = HighBonusCapLevels;
                return 1.0 + HighBonusRate * over;
            }
            var gap = playerLevel - monsterLevel;
            if (gap <= 0) return 1.0;
            var zd = Zd(playerLevel);
            if (zd <= 0) return 0.0;
            var f = 1.0 - (double)gap / zd;
            if (f < 0.0) f = 0.0;
            return f > 1.0 ? 1.0 : f;
        }

        /// <summary>
        /// "血薄削减"生效上界（基点）——与内核 <c>MonsterXpCore.HealthModEffectiveCapBps</c> 同值。
        /// 镜像副本里所有与内核同义的常数都必须成对改；本文件被离线闸门原样编进 .NET 工程，
        /// 所以这条上界与下面那段式子都是**被真的调用过**再对账的，不是按文本抠常量。
        /// </summary>
        public const int HealthModEffectiveCapBps = 7500;

        /// <summary>
        /// 本地镜像的经验曲线（内核 v3 的镜像：两段截断顺序也照抄）。
        /// <para>两个倍率缺省 10000 = 全不开，此时结果与加这两格之前逐字相同。
        /// 这条曲线只是即时预览；**对拍**走服务端的 <c>preview-xp</c>，两份不一致时窗口会点名。</para>
        /// </summary>
        public static long Xp(int monsterLevel, int playerLevel, int rank, int xpBase, int xpPerLevel,
                              int xpModBps = 10000, int healthModBps = 10000)
        {
            if (xpBase <= 0 && xpPerLevel <= 0) return 0;
            var baseXp = (double)xpBase + (double)xpPerLevel * Math.Max(1, monsterLevel);
            var scale = rank >= 0 && rank < RankXpScale.Length ? RankXpScale[rank] : 1.0;
            var xpMod = xpModBps < 0 ? 1.0 : xpModBps / 10000.0;   // 负数=坏值，按"没配"处理
            var raw = baseXp * Concession(monsterLevel, playerLevel, rank) * scale * xpMod;
            if (double.IsNaN(raw) || raw < 0.0) return 0;
            var firstStage = (long)Math.Round(raw, MidpointRounding.AwayFromZero);
            // 第二段：只有 0..7500 才削减，且这次是**截断**（与 AC 的 uint32 一致），不是四舍五入。
            if (healthModBps >= 0 && healthModBps <= HealthModEffectiveCapBps)
                return (long)(firstStage * (healthModBps / 10000.0));
            return firstStage;
        }

        /// <summary>AI 档位展开（与内核 <c>MonsterAiFlags.Resolve</c> 同判据，供"能力位预览"显示）。</summary>
        public static void ExpandProfile(int profile, int? threat, int? skills, int? nav, int? assist, int? tactics,
                                         out bool oThreat, out bool oSkills, out bool oNav, out bool oAssist, out bool oTactics)
        {
            bool pThreat, pSkills, pNav, pAssist, pTactics;
            switch (profile)
            {
                case 0: pThreat = false; pSkills = false; pNav = false; pAssist = false; pTactics = false; break;
                case 1: pThreat = true; pSkills = false; pNav = false; pAssist = false; pTactics = false; break;
                case 2: pThreat = true; pSkills = true; pNav = true; pAssist = true; pTactics = false; break;
                case 3: pThreat = true; pSkills = true; pNav = true; pAssist = true; pTactics = true; break;
                // 越界档位（含负数）与内核 ForProfile 的兜底一致：按"战"展开。
                // 这一档以前写的是"全开"，于是编辑器预览会把一只内核其实不会技能的怪画成五档全亮。
                default: pThreat = true; pSkills = false; pNav = false; pAssist = false; pTactics = false; break;
            }
            oThreat = threat.HasValue ? threat.Value != 0 : pThreat;
            oSkills = skills.HasValue ? skills.Value != 0 : pSkills;
            oNav = nav.HasValue ? nav.Value != 0 : pNav;
            oAssist = assist.HasValue ? assist.Value != 0 : pAssist;
            oTactics = tactics.HasValue ? tactics.Value != 0 : pTactics;
        }
    }
}
