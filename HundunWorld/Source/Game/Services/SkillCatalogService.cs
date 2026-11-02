using FlaxEngine;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Combat.Skills;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.Services
{
    /// <summary>
    /// 技能目录的客户端缓存：模板表走服务端下行（覆盖 <see cref="SkillDatabase"/> 的硬编码兜底），
    /// 已学技能同样以服务端为准。UI 只读这里，不再自己拼技能数据。
    /// <para>
    /// 下行失败时<b>什么都不动</b>：本地兜底表继续用。把"查询失败"当成"服务端确认你没有技能"
    /// 会让技能栏整屏清空，那比数值旧一点严重得多。
    /// </para>
    /// </summary>
    public class SkillCatalogService
    {
        private static SkillCatalogService _instance;
        public static SkillCatalogService Instance => _instance ??= new SkillCatalogService();

        private readonly Dictionary<int, SkillInfo> _learned = new Dictionary<int, SkillInfo>();

        /// <summary>目录或已学列表发生变化，UI 应整体重取。</summary>
        public event Action Changed;

        /// <summary>是否收到过服务端权威目录（false 时一切来自本地兜底表）。</summary>
        public bool HasServerData { get; private set; }

        public IReadOnlyDictionary<int, SkillInfo> Learned => _learned;

        private SkillCatalogService() { }

        public bool IsLearned(int skillId) => _learned.ContainsKey(skillId);

        /// <summary>
        /// 分域好感快照（1金…5土 → 当前值）。null 表示服务端这次没给到（读失败或旧帧），
        /// UI 只能退回"只说要什么"——把 null 当 0 会让一个本来达标的号显示成"好感 0/60"。
        /// </summary>
        public IReadOnlyDictionary<int, int> FavorByDomain { get; private set; }

        public int FavorOf(int domain)
            => FavorByDomain != null && FavorByDomain.TryGetValue(domain, out var value) ? value : 0;

        /// <summary>已学等级；未学返回 0。</summary>
        public int LevelOf(int skillId) => _learned.TryGetValue(skillId, out var s) ? s.Level : 0;

        /// <summary>消费一次下行回包（须在主线程调用）。</summary>
        public void Apply(SkillCatalogSyncMessage message)
        {
            if (message == null || !message.Success)
            {
                Debug.LogWarning($"[SkillCatalog] 服务端未返回技能目录，沿用本地兜底表：{message?.Message}");
                return;
            }

            var (updated, added) = SkillDatabase.ApplyServerTemplates(message.Templates);

            _learned.Clear();
            if (message.Learned != null)
            {
                foreach (var skill in message.Learned)
                    if (skill != null) _learned[skill.SkillId] = skill;
            }
            HasServerData = true;
            // 原样接收，不做"null 当空表"的合并：这一帧没给到就要让 UI 闭嘴，别拿上一帧的旧好感充数。
            FavorByDomain = message.FavorByDomain;

            // 周天与镇岳的权威快照：指示器不该靠"事后累积每一次施放的回执"来猜自己有几层。
            // 重登、换端、或刚被破势夺走一层之后，只有服务端能给得出这一帧；
            // cycleCompleted 一律传 false——这一次下行不是"刚刚圆满"，不该让指示器多闪一下。
            WuxingCycleTracker.Instance.SyncFromAuthority(message.LoopStack, message.LoopCompletionCount, false);
            WuxingCycleTracker.Instance.ApplyGuardResult(true, message.GuardElement, 0f, "");

            Debug.Log($"[SkillCatalog] 模板 {message.Templates.Count} 条（改写 {updated}、新增 {added}），已学 {_learned.Count} 条");
            Changed?.Invoke();
        }
    }
}
