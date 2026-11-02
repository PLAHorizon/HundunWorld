using System;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.UnrealFramework;

namespace NarrativePro.Tales
{
    /// <summary>
    /// 时辰触发器。对应 UE5 在蓝图里覆写 Initialize 绑定「时间段」委托的 NarrativeTrigger 子类
    /// （NarrativeTrigger.h:56-59 的注释口径：让 NPC 在一天中的特定时段做特定活动，如睡觉）。
    /// <para>
    /// UE5 本体没有 C++ 时间触发器（Initialize 是 BlueprintNativeEvent，时段绑定在 BP 资产里），
    /// 端口采用 Flax 侧已有的时辰事件通路：NarrativeGameState.RegisterTimeOfDayEvent
    /// （NarrativeGameState.cs:263），与 ScheduledBehavior.BindBehavior
    /// （NarrativeGameState.cs:644-650）完全同构。到 Start 点 Activate、到 End 点 Deactivate，
    /// 由基类把 TriggerEvents 按 EEventRuntime 分派执行。
    /// </para>
    /// </summary>
    [Serializable]
    public class TimeOfDayNarrativeTrigger : NarrativeTrigger
    {
        /// <summary>激活窗口开始时辰（0-2400，100=1 小时）。对应 UE5 ScheduledBehavior.StartTime 口径。</summary>
        public float StartTime = 0f;

        /// <summary>激活窗口结束时辰（0-2400；小于 StartTime 时视为跨午夜窗口）。</summary>
        public float EndTime = 0f;

        /// <summary>初始化时若已处于窗口内，是否立即激活。对应 BindBehavior 的 bFireIfAlreadyStarted。</summary>
        public bool bFireIfAlreadyStarted = true;

        [NonSerialized]
        private int _startHandle;

        [NonSerialized]
        private int _endHandle;

        /// <summary>绑定时辰事件。对应 UE5 Initialize_Implementation 里绑定激活/停用委托。</summary>
        public override void Initialize()
        {
            base.Initialize();

            var gameState = NarrativeGameState.GetCurrent();
            if (gameState == null)
            {
                NarrativeLog.LogWarning(
                    $"TimeOfDayNarrativeTrigger: 未找到 NarrativeGameState，触发器不会按时辰激活（{GetDescription()}，宿主 {OwnerCharacter?.Name}）");
                return;
            }

            _startHandle = gameState.RegisterTimeOfDayEvent(StartTime, OnTimeStarted);
            _endHandle = gameState.RegisterTimeOfDayEvent(EndTime, OnTimeEnded);

            if (bFireIfAlreadyStarted && IsTimeInRange(gameState.GetTimeOfDay()))
            {
                Activate();
            }
        }

        /// <summary>复制模板并绑定宿主（对应 UE5 DuplicateObject&lt;UNarrativeTrigger&gt;(Template, this)，
        /// NarrativeCharacter.cpp:1921-1935）。副本不继承句柄，需各自 Initialize 重新绑定。</summary>
        public override NarrativeTrigger CopyForOwner(Actor ownerCharacter)
        {
            var copy = (TimeOfDayNarrativeTrigger)base.CopyForOwner(ownerCharacter);
            copy._startHandle = 0;
            copy._endHandle = 0;
            return copy;
        }

        /// <summary>反注册时辰事件（对应 UE5 触发器随宿主销毁、委托自动失效；Flax 需显式反注册）。</summary>
        public override void Unbind()
        {
            var gameState = NarrativeGameState.GetCurrent();
            if (gameState != null)
            {
                // 委托按「目标+方法」相等移除，重新构造的等价委托可以命中注册时那份
                if (_startHandle != 0) gameState.UnregisterTimeOfDayEvent(_startHandle, new TimeOfDayEventDelegate(OnTimeStarted));
                if (_endHandle != 0) gameState.UnregisterTimeOfDayEvent(_endHandle, new TimeOfDayEventDelegate(OnTimeEnded));
            }
            _startHandle = 0;
            _endHandle = 0;
        }

        public override string GetDescription()
        {
            return $"{base.GetDescription()} [{StartTime:0}-{EndTime:0}]";
        }

        private void OnTimeStarted(float eventTime, float timeAtFire, float timePassedDelta, bool bFiredFromAdvanceTime)
        {
            Activate();
        }

        private void OnTimeEnded(float eventTime, float timeAtFire, float timePassedDelta, bool bFiredFromAdvanceTime)
        {
            Deactivate();
        }

        /// <summary>当前时辰是否落在激活窗口内（含跨午夜）。判定与 NarrativeGameState.cs:654-666 的
        /// BindBehavior bFireIfAlreadyStarted 分支一致。</summary>
        private bool IsTimeInRange(float currentTime)
        {
            if (StartTime < EndTime)
            {
                return currentTime >= StartTime && currentTime < EndTime;
            }
            // 跨午夜窗口（如 2200-600）
            return currentTime >= StartTime || currentTime < EndTime;
        }
    }
}
