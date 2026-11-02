using System;

namespace HundunWorld.Game.Network
{
    /// <summary>
    /// 训练师上下文：记住"我现在是在哪个 NPC 面前开技能页的"。
    /// <para>
    /// <b>为什么需要这一层</b>：学技请求原本不带 NPC，服务端只能按"直接学"处理（不收费）。
    /// 有了它，技能页在训练师面前学习时会把实体 id 一起带上，服务端据此解析训练师、核对邻近并扣学费。
    /// </para>
    /// <para>
    /// <b>为什么是"会被清掉的软绑定"而不是一直挂着</b>：实体 id 只在玩家还站在那个 NPC 面前时有效。
    /// 绑定在三种情况下清空：与另一个 NPC 交互、学习失败（服务端已经把"人不在跟前"判掉了）、
    /// 页面显式调用 <see cref="Clear"/>。宁可让玩家多点一次 NPC，也不要让一次过期的绑定
    /// 把"学不了"变成默认结局。
    /// </para>
    /// </summary>
    public sealed class TrainerClientService
    {
        /// <summary>全局单例（与 <c>ShopClientService</c> 同一条路子）。</summary>
        public static TrainerClientService Instance { get; } = new TrainerClientService();

        private readonly object _gate = new object();
        private ulong _entityId;
        private int _npcId;

        private TrainerClientService() { }

        /// <summary>当前绑定的训练师实体 id（0 = 不在训练师面前）。</summary>
        public ulong EntityId { get { lock (_gate) return _entityId; } }

        /// <summary>
        /// 当前训练师的 NPC 编号（0 = 未知）。技能页据此查"这位教什么、多少钱"的展示表；
        /// 学费与能不能学仍由服务端按内容表判，这一列只影响显示。
        /// </summary>
        public int NpcId { get { lock (_gate) return _npcId; } }

        /// <summary>是否处于"向训练师学习"的语境（UI 据此显示收费提示）。</summary>
        public bool Active => EntityId != 0;

        /// <summary>与训练师交互时绑定；玩家换目标时由交互入口重新绑定或清空。</summary>
        public void Bind(ulong entityId, int npcId = 0)
        {
            lock (_gate)
            {
                _entityId = entityId;
                _npcId = npcId;
            }
        }

        /// <summary>清空绑定（换目标 / 学习失败 / 离开页面时调用）。</summary>
        public void Clear()
        {
            lock (_gate)
            {
                _entityId = 0;
                _npcId = 0;
            }
        }
    }
}
