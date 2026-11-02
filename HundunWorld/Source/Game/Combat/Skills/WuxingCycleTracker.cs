using System;
using FlaxEngine;
using Game.Character.Attributes;

namespace HundunWorld.Game.Combat.Skills
{
    /// <summary>
    /// 五行循环节（周天）的客户端预测器。
    /// <para>
    /// 权威判定在服务端（<c>CombatCalculator</c> 的相生/相克乘算），这里只做<b>即时反馈</b>：
    /// 玩家按下技能栏的那一记是不是"顺生"、现在叠了几层、下一式打哪一行能续上 chain。
    /// 网络往返之内没有服务端回执，靠这份预测把"循环往复"手感连起来；一旦服务端下行
    /// 权威节数，客户端应改用下行值（本类只需被替换数据源，界面不用动）。
    /// </para>
    /// </summary>
    public sealed class WuxingCycleTracker
    {
        /// <summary>周天满层数：五行各打一式。</summary>
        public const int MaxStack = 5;

        /// <summary>顺生判定窗口（秒）：上一式之后多久内打出"子行"式才算连上。</summary>
        public const float WindowSeconds = 3.0f;

        /// <summary>每层节数的全系增伤（与服务端 §5.1 同值）。</summary>
        public const float BonusPerStack = 0.03f;

        private static WuxingCycleTracker _instance;
        public static WuxingCycleTracker Instance => _instance ??= new WuxingCycleTracker();

        /// <summary>当前节数 0..MaxStack。</summary>
        public int Stack { get; private set; }

        /// <summary>上一记有效施放的五行（None 表示还没打过）。</summary>
        public WuxingElement LastElement { get; private set; } = WuxingElement.None;

        /// <summary>本次施放是否构成顺生（用于飘字与指示器高亮）。</summary>
        public bool LastCastGenerated { get; private set; }

        /// <summary>周天圆满累计次数（演化门槛的计数来源）。</summary>
        public int CompletionCount { get; private set; }

        /// <summary>触发周天圆满的事件（层数归零并 +1 次），供 UI 播放一次性反馈。</summary>
        public event Action CycleCompleted;

        /// <summary>镇岳行（§5.3）。只由服务端回执写入——本地不预判"点了就算"。</summary>
        public WuxingElement GuardElement { get; private set; } = WuxingElement.None;

        /// <summary>镇岳切换冷却剩余秒数。>0 时 UI 应把这一排节点灰掉并倒计时。</summary>
        public float GuardCooldownRemaining { get; private set; }

        /// <summary>最近一次镇岳回执的说明文本（被拒原因就是这句，直接显示给玩家）。</summary>
        public string GuardStatusText { get; private set; } = "";

        /// <summary>镇岳状态变化（成功与被拒都触发），供指示器刷新。</summary>
        public event Action GuardChanged;

        /// <summary>
        /// 回写服务端镇岳结果。传进来的就是权威值：被拒时 <paramref name="guardElement"/>
        /// 是<b>原来</b>那一行，所以 UI 会自动弹回，不需要额外回滚逻辑。
        /// </summary>
        public void ApplyGuardResult(bool success, int guardElement, float nextChangeInSeconds, string message)
        {
            GuardElement = (WuxingElement)Math.Max(0, Math.Min(5, guardElement));
            GuardCooldownRemaining = Math.Max(0f, nextChangeInSeconds);
            GuardStatusText = (message ?? "") + (success ? "" : "（未生效）");
            try { GuardChanged?.Invoke(); }
            catch (Exception ex) { Debug.LogWarning($"[WuxingCycle] GuardChanged 订阅者异常: {ex.Message}"); }
        }

        /// <summary>开始一次镇岳请求的本地冷却占位，防止连点把同一请求发好几次。</summary>
        public void MarkGuardPending(float seconds)
        {
            GuardCooldownRemaining = Math.Max(GuardCooldownRemaining, seconds);
        }

        /// <summary>
        /// 走一秒镇岳倒计时。由常驻可见的指示器每帧驱动（与技能栏拖拽 Tick 同一做法：
        /// 不可见控件的 Update 不可靠，而这个指示器只要进游戏就在屏上）。
        /// </summary>
        public void TickGuardCooldown(float deltaTime)
        {
            if (GuardCooldownRemaining <= 0f) return;
            GuardCooldownRemaining = Math.Max(0f, GuardCooldownRemaining - deltaTime);
        }

        private float _lastCastAt = -999f;

        /// <summary>链是否已因超时断开。</summary>
        public bool IsChainExpired => Time.GameTime - _lastCastAt > WindowSeconds;

        /// <summary>当前节数对应的增伤系数。</summary>
        public float DamageMultiplier => 1f + BonusPerStack * Stack;

        /// <summary>
        /// 记下一记施放。规则与服务端 <c>CombatCalculator.StepWuxingCycle</c> 逐值一致：
        /// 超时清零；空链上的第一式自己占一层（否则五行各打一式只到 4 层，周天永远凑不满）；
        /// 顺生（上一行的所生之行）再加一层，满五层即周天圆满并清零；
        /// 同属与逆生不清零也不加层，只刷新时间戳并把链的"当前行"移过去。
        /// </summary>
        public void NoteCast(WuxingElement element)
        {
            if (element == WuxingElement.None) return;

            if (IsChainExpired) Stack = 0;

            LastCastGenerated = LastElement != WuxingElement.None
                && WuxingRules.GetGenerateElement(LastElement) == element;

            if (LastCastGenerated || Stack == 0)
            {
                Stack++;
                if (Stack >= MaxStack)
                {
                    Stack = 0;
                    CompletionCount++;
                    LastElement = element;
                    _lastCastAt = Time.GameTime;
                    try { CycleCompleted?.Invoke(); }
                    catch (Exception ex) { Debug.LogWarning($"[WuxingCycle] CycleCompleted 订阅者异常: {ex.Message}"); }
                    return;
                }
            }

            LastElement = element;
            _lastCastAt = Time.GameTime;
        }

        /// <summary>下一式打哪一行能续上 chain（None 表示从当前行出发即可起链）。</summary>
        public WuxingElement NextGenerateElement =>
            LastElement == WuxingElement.None ? WuxingElement.None : WuxingRules.GetGenerateElement(LastElement);

        /// <summary>
        /// 用服务端权威值校正本地预测。传 -1 表示该字段这条回执里没有，保持原值不动。
        /// <para>
        /// 只信"层数与周天次数"，不信时间窗口：链的方向（<see cref="LastElement"/>）仍由本地
        /// 施放记录维持，因为权威回执不带上一式是哪一行。
        /// </para>
        /// </summary>
        public void SyncFromAuthority(int stack, int completionCount, bool cycleCompleted)
        {
            bool isNewCompletion = completionCount > CompletionCount;

            if (stack >= 0) Stack = Math.Min(MaxStack, stack);
            if (completionCount >= 0) CompletionCount = completionCount;

            if (!isNewCompletion || !cycleCompleted) return;

            // 权威侧新发生的一次圆满（比如夺元把最后一层补齐）才补发一次反馈；
            // 本地已经数过的那次不再重复闪，否则指示器会连闪两下。
            try { CycleCompleted?.Invoke(); }
            catch (Exception ex) { Debug.LogWarning($"[WuxingCycle] CycleCompleted 订阅者异常: {ex.Message}"); }
        }

        public void Reset()
        {
            Stack = 0;
            LastElement = WuxingElement.None;
            LastCastGenerated = false;
            GuardElement = WuxingElement.None;
            GuardCooldownRemaining = 0f;
            GuardStatusText = "";
            _lastCastAt = -999f;
        }
    }
}
