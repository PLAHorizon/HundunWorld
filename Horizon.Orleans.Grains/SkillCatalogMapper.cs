using Horizon.Game.Message.Network;
using Horizon.Model.GameModel;
using System.Globalization;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 技能模板行 → 目录下行项（<see cref="SkillCatalogSyncMessage.Templates"/>）的纯映射。
    /// <para>
    /// 单独成类、做成纯函数有两个原因：① 客户端技能树的格子位置、封存与演化链、tooltip 上的倍率
    /// 全读这几列，映射漏一条就表现为"树上少了一式"这种很难归因的现象，必须能逐值回归；
    /// ② 库里新增了行而 <c>skills.json</c> 忘了重导时，客户端只能靠这份下行建行——
    /// 不带阶位的行会被树页当作"不在五行阶梯上"直接跳过，等于数据在库里有、界面上没有。
    /// </para>
    /// </summary>
    public static class SkillCatalogMapper
    {
        public static SkillTemplateEntryInfo ToEntry(SkillTemplateEntity t) => new()
        {
            SkillId = t.Id,
            SkillName = t.SkillName ?? "",
            Description = t.Description ?? "",
            Element = t.WuxingElement,
            SkillType = t.SkillType,
            EnergyCost = ParseFormula(t.EnergyCostFormula),
            Cooldown = ParseFormula(t.CooldownFormula),
            CastTime = ParseFormula(t.CastTimeFormula),
            AttackRange = t.AttackRange,
            MaxLevel = t.MaxLevel,
            LearnLevelRequire = t.LearnLevelRequire,
            SkillAttack = t.SkillAttack,
            SkillPenetration = t.SkillPenetration,
            SkillGrade = t.SkillGrade,
            GradeStage = t.GradeStage,
            SeriesId = t.SeriesId ?? "",
            DamageMultiplier = t.DamageMultiplier,
            PreSkillId = t.PreSkillId ?? 0,
            // 演化消耗与周天门槛就存这一列，原文透传：加一条新门槛不用再动契约。
            LearnCostJson = t.LearnCost ?? "",
        };

        /// <summary>
        /// 技能表的消耗/冷却列存的是公式串（现有内容一律是常数），下行前折成数值。
        /// 解析不了按 0 处理而不是抛：一个脏字符串不该拖垮整份目录。
        /// </summary>
        public static float ParseFormula(string formula)
            => float.TryParse((formula ?? string.Empty).Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var value) ? value : 0f;
    }
}
