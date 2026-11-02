using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 分域好感度（方案 §14）。Grain 键与 SkillGrain 同法：<c>GrainKeys.ForEntity(characterId)</c>。
    /// <para>
    /// 项目原先只有角色级一个 <c>Reputation</c> 标量，问不出"这个人在金域攒了多少心意"，
    /// 而玄阶以上的习得条件要的正是后者，所以按五行域分桶单独存一份。
    /// </para>
    /// <para>
    /// 本 Grain 只记账，不参与习得判定：判定输入由调用方装配成 <see cref="LearnGateContext"/>
    /// 交给 SkillGrain。<b>SkillGrain 绝不反过来调这里</b> —— 研读通道已经是
    /// InventoryGrain → SkillGrain，再加一条 SkillGrain → 别人的边就会凑成 Grain 环，
    /// 非重入 Grain 会当场互等死锁。
    /// </para>
    /// </summary>
    public class FavorabilityGrain : Grain, IFavorabilityGrain
    {
        /// <summary>
        /// 单一来源（某个 NPC、某类怪）默认累计上限。
        /// 没有这一条，"和金域一个 NPC 反复对话"就能刷满整域，好感度门槛当场形同虚设。
        /// </summary>
        public const int DefaultPerTargetCap = 20;

        /// <summary>域内总上限。现有门槛最高 110（混沌终式），这里留足余量但不放开到无限。</summary>
        public const int DomainCap = 1000;

        private readonly ILogger<FavorabilityGrain> _logger;
        private readonly IPersistentState<FavorabilityState> _state;

        public FavorabilityGrain(
            ILogger<FavorabilityGrain> logger,
            [PersistentState("favorability", "GameStore")] IPersistentState<FavorabilityState> state)
        {
            _logger = logger;
            _state = state;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _state.State.ByDomain ??= new Dictionary<int, int>();
            _state.State.ByTarget ??= new Dictionary<string, int>();
            await base.OnActivateAsync(cancellationToken);
        }

        public Task<Dictionary<int, int>> GetByDomainAsync()
            => Task.FromResult(new Dictionary<int, int>(_state.State.ByDomain));

        public async Task<int> AddAsync(int domain, int delta, string? targetKey = null, int perTargetCap = 0)
        {
            if (domain < 1 || domain > 5)
            {
                _logger.LogWarning("好感度入账被忽略：域号越界 domain={Domain}", domain);
                return 0;
            }
            if (delta == 0) return Current(domain);

            var state = _state.State;
            int actual = delta;

            if (!string.IsNullOrWhiteSpace(targetKey))
            {
                state.ByTarget.TryGetValue(targetKey, out var given);

                if (delta > 0)
                {
                    int cap = perTargetCap > 0 ? perTargetCap : DefaultPerTargetCap;
                    actual = Math.Min(delta, Math.Max(0, cap - given));
                    if (actual <= 0)
                    {
                        // 说清楚"这个来源已经给满了"，否则内容组只会看到"加了好感但数值没动"
                        _logger.LogInformation("来源 {Target} 对第 {Domain} 域的好感已达上限 {Cap}，本次 {Delta} 未计入",
                            targetKey, domain, cap, delta);
                        return Current(domain);
                    }
                }

                state.ByTarget[targetKey] = given + actual;
            }

            state.ByDomain[domain] = Math.Clamp(Current(domain) + actual, 0, DomainCap);
            state.UpdatedAt = DateTime.UtcNow;
            await _state.WriteStateAsync();
            return state.ByDomain[domain];
        }

        private int Current(int domain) => _state.State.ByDomain.TryGetValue(domain, out var value) ? value : 0;
    }
}
