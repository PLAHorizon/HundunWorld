using System.Collections.Generic;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Sim.Monsters;

namespace Horizon.MonsterCli;

/// <summary>
/// 导出内容包那份回执的算术——抽成纯函数是为了能被离线断言。
/// <para>
/// 判据本体不是"包里今天有几行挂载"，而是<b>"库里有、包里没有"这种形状必须在这行字上读得出来</b>：
/// 回执以前只报模板/刷怪点/路线/掉落绑定四格，技能挂载整段不在其中；装配器若哪天漏带挂载，
/// 回执照旧是一句"已导出 rev=… 模板 12 …"，没人会去数包里的第 12 个模板有没有 skills。
/// 与切片 39 那条"writtenRows 会自己撒谎"同族，所以数一次要留两个数：
/// <see cref="SkillMounts"/> 是行数，<see cref="SkillCapableTemplates"/> 是"真能施法的怪有几只"——
/// 只数行数会把"挂了但档位关着"（死数据）报成有内容。
/// </para>
/// </summary>
public static class MonsterExportReceipt
{
    public readonly record struct Counts(int SkillMounts, int SkillCapableTemplates);

    /// <summary>
    /// 能力位仍问唯一主人 <see cref="MonsterAiFlags.Resolve"/>（档位 + 逐位覆盖），
    /// 这里不许出现第二张"profile≥2 就算会施法"的表——那正是内核与回执静默分叉的起点。
    /// </summary>
    public static Counts Of(IEnumerable<MonsterTemplateSnapshot>? templates)
    {
        if (templates is null) return new Counts(0, 0);
        int mounts = 0, casters = 0;
        foreach (var t in templates)
        {
            var n = t.Skills?.Count ?? 0;
            mounts += n;
            var b = t.Behavior;
            if (n == 0 || b is null) continue;
            var flags = MonsterAiFlags.Resolve(
                (MonsterAiProfile)System.Math.Clamp(b.AiProfile, 0, 3),
                b.FlagUseThreat, b.FlagUseSkills, b.FlagUseNavMesh, b.FlagUseAssist, b.FlagUseTactics);
            if (flags.UseSkills) casters++;
        }
        return new Counts(mounts, casters);
    }
}
