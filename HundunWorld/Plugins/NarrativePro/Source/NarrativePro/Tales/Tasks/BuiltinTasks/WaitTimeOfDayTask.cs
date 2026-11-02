using System;
using NarrativePro.Core;
using NarrativePro.UnrealFramework;

namespace NarrativePro.Tales.Tasks.BuiltinTasks
{
    /// <summary>
    /// 等到某个时辰为止的任务。对应 UE5 蓝图里串接 <c>UAsyncAction_WaitTimeOfDay</c> 的任务节点
    /// （NarrativeGameState.h:23-46）：到达目标时辰即算完成。
    /// <para>
    /// UE5 的三个时辰异步节点（WaitTimeOfDay / WaitTimeRange / WaitSunsetAndSunrise）在原版只被
    /// 蓝图图调用；本端口用 JSON 注册表取代蓝图，所以它们的合法消费者就是这里的任务类型 ——
    /// 否则三个节点会退化成"实现了但没人调用"。
    /// </para>
    /// </summary>
    [Serializable]
    public class WaitTimeOfDayTask : NarrativeTask
    {
        /// <summary>目标时辰（0-2400，100 = 1 小时）。对应 UE5 <c>Time</c>。</summary>
        public float TargetTime = 0f;

        /// <summary>到达时辰前保留等待句柄，EndTask 时取消。</summary>
        [NonSerialized]
        private AsyncActionWaitTimeOfDay _action;

        protected override void OnBeginTask()
        {
            var gameState = NarrativeGameState.GetCurrent();
            if (gameState == null)
            {
                NarrativeLog.LogWarning("WaitTimeOfDayTask: 没有 NarrativeGameState，时辰任务直接按完成处理");
                CompleteTask();
                return;
            }

            _action = AsyncActionWaitTimeOfDay.WaitTimeOfDay(gameState, TargetTime);
            if (_action == null)
            {
                CompleteTask();
                return;
            }

            _action.OnTimeReached += OnTimeReached;
        }

        private void OnTimeReached(float eventTime, float actualTime, float timePassedDelta, bool bFiredFromAdvanceTime)
        {
            Detach();
            SetProgress(RequiredQuantity);
        }

        protected override void OnEndTask()
        {
            Detach();
        }

        private void Detach()
        {
            if (_action == null) return;
            _action.OnTimeReached -= OnTimeReached;
            _action = null;
        }

        public override string GetTaskDescription()
        {
            return $"等到 {FormatClock(TargetTime)}";
        }

        /// <summary>把 0-2400 的时辰值格式化成 HH:MM。</summary>
        protected static string FormatClock(float time)
        {
            int clamped = (int)(time < 0f ? 0f : (time > 2400f ? 2400f : time));
            return $"{clamped / 100:00}:{clamped % 100:00}";
        }
    }

    /// <summary>
    /// 等一个时间段开始的任务。对应 UE5 <c>UAsyncAction_WaitTimeRange</c> 的 OnTimeRangeStart 分支
    /// （NarrativeGameState.h:49-77）：进入时段即完成，可选地把时段的结束也作为进度上限。
    /// </summary>
    [Serializable]
    public class WaitTimeRangeStartTask : NarrativeTask
    {
        /// <summary>时段起点（0-2400）。</summary>
        public float TimeStart = 0f;

        /// <summary>时段终点（0-2400，小于起点表示跨午夜）。</summary>
        public float TimeEnd = 0f;

        [NonSerialized]
        private AsyncActionWaitTimeRange _action;

        protected override void OnBeginTask()
        {
            var gameState = NarrativeGameState.GetCurrent();
            if (gameState == null)
            {
                NarrativeLog.LogWarning("WaitTimeRangeStartTask: 没有 NarrativeGameState，任务直接按完成处理");
                CompleteTask();
                return;
            }

            _action = AsyncActionWaitTimeRange.WaitTimeRange(gameState, TimeStart, TimeEnd);
            if (_action == null)
            {
                CompleteTask();
                return;
            }

            _action.OnTimeRangeStart += OnStarted;
        }

        private void OnStarted(float eventTime, float actualTime, float timePassedDelta, bool bFiredFromAdvanceTime)
        {
            Detach();
            SetProgress(RequiredQuantity);
        }

        protected override void OnEndTask()
        {
            Detach();
        }

        private void Detach()
        {
            if (_action == null) return;
            _action.OnTimeRangeStart -= OnStarted;
            _action = null;
        }

        public override string GetTaskDescription()
        {
            return $"等待时段 {FormatClock(TimeStart)}-{FormatClock(TimeEnd)} 开始";
        }

        private static string FormatClock(float time)
        {
            int clamped = (int)(time < 0f ? 0f : (time > 2400f ? 2400f : time));
            return $"{clamped / 100:00}:{clamped % 100:00}";
        }
    }

    /// <summary>
    /// 等日落（或日出）的任务。对应 UE5 <c>UAsyncAction_WaitSunsetAndSunrise</c>
    /// （NarrativeGameState.h:80-100，注释口径：路灯一类环境物）。默认等日落，可改为等日出。
    /// </summary>
    [Serializable]
    public class WaitSunsetOrSunriseTask : NarrativeTask
    {
        /// <summary>true = 等日出，false = 等日落。</summary>
        public bool bWaitForSunrise = false;

        [NonSerialized]
        private AsyncActionWaitSunsetAndSunrise _action;

        protected override void OnBeginTask()
        {
            var gameState = NarrativeGameState.GetCurrent();
            if (gameState == null)
            {
                NarrativeLog.LogWarning("WaitSunsetOrSunriseTask: 没有 NarrativeGameState，任务直接按完成处理");
                CompleteTask();
                return;
            }

            _action = AsyncActionWaitSunsetAndSunrise.WaitSunsetAndRise(gameState);
            if (_action == null)
            {
                CompleteTask();
                return;
            }

            if (bWaitForSunrise)
            {
                _action.OnSunrise += OnHit;
            }
            else
            {
                _action.OnSunset += OnHit;
            }
        }

        private void OnHit(float eventTime, float actualTime, float timePassedDelta, bool bFiredFromAdvanceTime)
        {
            Detach();
            SetProgress(RequiredQuantity);
        }

        protected override void OnEndTask()
        {
            Detach();
        }

        private void Detach()
        {
            if (_action == null) return;
            _action.OnSunrise -= OnHit;
            _action.OnSunset -= OnHit;
            _action = null;
        }

        public override string GetTaskDescription()
        {
            return bWaitForSunrise ? "等待日出" : "等待日落";
        }
    }
}
