// 模块关系说明：
// 本文件是 NarrativePro.TimeOfDay 的「时辰窗口纯接缝」：活动调度到点/出点的窗口判定、
// 跨午夜窗口、追赶时间差（catch-up delta）、以及时辰事件「这一 tick 是否跨过了事件时刻」的
// 判定全部住在这里，**不引用任何 FlaxEngine 类型**。
//
// 为什么要有它：调度规则原本长在 UnrealFramework.NarrativeGameState 的 ScheduledBehavior（要
// RegisterTimeOfDayEvent 到活的 GameState 实例上）与 FireEventsInRange（要活的时辰表）里，
// 进程外一条都跑不到；而恰恰是这几处承载了最容易写错的边界：
//   - UE5 的 IsTimeInRange 两端都是**闭区间**（>= 与 <=），不是「含起点不含终点」；
//   - start > end 时走的是「或」分支（跨午夜），start == end 走的是「且」分支（退化成一个点）；
//   - HandleStarted/HandleEnded 各自还有一道门，Ended 那道门的参数顺序是**刻意交换**的；
//   - bFireIfAlreadyStarted 要把「已经错过了多久」当 TimePassedDelta 传下去，供目标追赶。
// 线上通路（ScheduledBehavior.BindBehavior / DispatchHandleStarted / DispatchHandleEnded /
// FireEventsInRange）都改成调用本文件，harness 调同一份，测试不会退化成复刻。
//
// UE5 依据（只读参考工程 …/NarrativeArsenal）：
//   - Private/UnrealFramework/NarrativeGameState.cpp:20-44  BindBehavior（含 :35-41 的追赶 delta）
//   - Private/UnrealFramework/NarrativeGameState.cpp:52-66   DispatchHandleStarted / DispatchHandleEnded
//   - Private/ArsenalStatics.cpp:425-436                     IsTimeInRange
//   - Private/AI/Activities/NPCActivitySchedule.cpp:12-41     HandleStarted_Implementation

using System;

namespace NarrativePro.TimeOfDay
{
    /// <summary>
    /// 时辰窗口与派发裁决纯规则。
    /// </summary>
    public static class TimeOfDayWindowRules
    {
        /// <summary>一天的长度（0-2400）。与 <c>FTimeOfDay.RangeMax</c> / <c>FTimeOfDayRange.RangeMax</c> 同值，harness 有对账断言。</summary>
        public const float DayLength = 2400.0f;

        /// <summary>
        /// 时间是否落在窗口内。对应 UE5 <c>UArsenalStatics::IsTimeInRange</c>
        /// （ArsenalStatics.cpp:425-436）：<b>两端闭区间</b>（:430 的 <c>Time &gt;= RangeStart &amp;&amp; Time &lt;= RangeEnd</c>），
        /// <c>rangeStart &gt; rangeEnd</c> 时按跨午夜走「或」分支（:434）；
        /// <c>rangeStart == rangeEnd</c> 落在「且」分支，因此只有恰好等于该时刻才算在窗内。
        /// </summary>
        public static bool IsTimeInRange(float time, float rangeStart, float rangeEnd)
        {
            if (rangeStart <= rangeEnd)
            {
                return time >= rangeStart && time <= rangeEnd;
            }
            // 跨天
            return time >= rangeStart || time <= rangeEnd;
        }

        /// <summary>
        /// 绑定时刻「已经错过了多久」。对应 UE5 <c>BindBehavior</c> 的
        /// <c>Delta = TOD &lt; StartTime ? TOD + 2400 - StartTime : TOD - StartTime</c>
        /// （NarrativeGameState.cpp:40），它作为 TimePassedDelta 传给 HandleStarted，
        /// 让调度目标有机会 catchup（NPCActivitySchedule.cpp:29-30 把 IntendedTODStartTime 记下来正是为此）。
        /// </summary>
        public static float CatchUpDelta(float currentTime, float rangeStart)
        {
            return currentTime < rangeStart
                ? currentTime + DayLength - rangeStart
                : currentTime - rangeStart;
        }

        /// <summary>
        /// 到点派发要不要真的开始。对应 UE5 <c>DispatchHandleStarted</c> 的门
        /// （NarrativeGameState.cpp:54：<c>IsTimeInRange(ActualTime, StartTime, EndTime)</c>）。
        /// 时间被大步推进（AdvanceTimeOfDay）时，实际时刻可能已经走出窗口，此时 UE5 **不开始**。
        /// </summary>
        public static bool ShouldDispatchStarted(float actualTime, float startTime, float endTime)
        {
            return IsTimeInRange(actualTime, startTime, endTime);
        }

        /// <summary>
        /// 到点派发要不要真的结束。对应 UE5 <c>DispatchHandleEnded</c> 的门
        /// （NarrativeGameState.cpp:62：<c>IsTimeInRange(ActualTime, EndTime, StartTime)</c>）——
        /// 参数顺序刻意交换，判定的是「End→Start」这段窗外区间，UE5 原样保留。
        /// </summary>
        public static bool ShouldDispatchEnded(float actualTime, float startTime, float endTime)
        {
            return IsTimeInRange(actualTime, endTime, startTime);
        }

        /// <summary>
        /// 本 tick 是否要派发时辰事件。对应 UE5 <c>UpdateTimeOfDay</c> 的循环体
        /// （NarrativeGameState.cpp:148）：<c>IsTimeInRange(TODEvent.EventTime, TimeOfDayLastTick, TimeOfDay)</c>
        /// —— 直接复用同一份闭区间判定，所以「上一 tick 恰好等于事件时刻」时 UE5 **会再派发一次**
        /// （tick 起点含在内）。端口早前写成 <c>prev &lt; event</c> 的半开区间来避免重复派发，
        /// 那是与上游不一致的行为，现在按 UE5 原样对齐。
        /// 跨过 2400→0 回绕时（current &lt; prev）走 <c>IsTimeInRange</c> 的「或」分支，与 UE5 同一条式子。
        /// </summary>
        public static bool DidCrossEventTime(float prevTime, float currentTime, float eventTime)
        {
            return IsTimeInRange(eventTime, prevTime, currentTime);
        }

        /// <summary>
        /// 本 tick 跨过了几个整天。对应 UE5 <c>DaysPassed = Floor(AccumulatedTime / 2400) -
        /// Floor(AccumulatedTimeLastTick / 2400)</c>（NarrativeGameState.cpp:140）。
        /// 用累计时间（不是折回 0-2400 的 TimeOfDay）来算，回绕那一帧才不会算出负数天。
        /// </summary>
        public static int DaysPassedBetween(float accumulatedTime, float accumulatedTimeLastTick)
        {
            return (int)Math.Floor(accumulatedTime / DayLength) - (int)Math.Floor(accumulatedTimeLastTick / DayLength);
        }

        /// <summary>
        /// 派发给回调的「事件时刻已经过去多久」。对应 UE5 <c>TimePassedDelta =
        /// (TimeOfDay - EventTime) + DaysPassed * 2400</c>（NarrativeGameState.cpp:150）。
        /// 注意这是**迟到量**，不是本帧推进量：调度目标靠它做 catchup
        /// （NPCActivitySchedule.cpp:29-30 记下 IntendedTODStartTime 正是为了比对这段差值）。
        /// </summary>
        public static float EventTimePassedDelta(float currentTime, float eventTime, int daysPassed)
        {
            return currentTime - eventTime + daysPassed * DayLength;
        }

        /// <summary>把任意时间折回 0-2400 循环区间（对应 GameState 的归一化 while 循环）。</summary>
        public static float Normalize(float time)
        {
            float value = time;
            while (value >= DayLength) value -= DayLength;
            while (value < 0f) value += DayLength;
            return value;
        }
    }
}
