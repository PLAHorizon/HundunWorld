using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;
using NarrativePro.Settings;

namespace NarrativePro.UnrealFramework
{
    /// <summary>
    /// 阵营态度改变事件委托。对应 UE5 FOnFactionAttitudeChanged。
    /// </summary>
    /// <param name="faction">自身阵营。</param>
    /// <param name="otherFaction">对方阵营。</param>
    /// <param name="newAttitude">新态度。</param>
    public delegate void OnFactionAttitudeChangedDelegate(GameplayTag faction, GameplayTag otherFaction, ArsenalStatics.ETeamAttitude newAttitude);

    /// <summary>
    /// 时间事件委托。对应 UE5 FTimeOfDayEvent。
    /// </summary>
    /// <param name="eventTime">事件预定时间。</param>
    /// <param name="timeAtFire">实际触发时间。</param>
    /// <param name="timePassedDelta">自上次事件经过的时间。</param>
    /// <param name="bFiredFromAdvanceTime">是否由 AdvanceTime 触发。</param>
    public delegate void TimeOfDayEventDelegate(float eventTime, float timeAtFire, float timePassedDelta, bool bFiredFromAdvanceTime);

    /// <summary>
    /// 阵营态度数据。对应 UE5 FFactionAttitudeData。
    /// 记录一个阵营对其他阵营的态度映射。
    /// </summary>
    [Serializable]
    public class FactionAttitudeData
    {
        /// <summary>阵营 → 态度映射（Narrative.Factions 分类）。</summary>
        public Dictionary<string, ArsenalStatics.ETeamAttitude> AttitudeMap = new Dictionary<string, ArsenalStatics.ETeamAttitude>(StringComparer.Ordinal);
    }

    /// <summary>
    /// 活跃时间事件。对应 UE5 FActiveTimeOfDayEvent。
    /// 表示一个已注册的、在指定时间触发的事件。
    /// </summary>
    [Serializable]
    public class ActiveTimeOfDayEvent
    {
        /// <summary>句柄 ID。</summary>
        public int HandleID = 0;

        /// <summary>事件触发时间（0-2400）。</summary>
        public float EventTime = 0f;

        /// <summary>事件触发委托。</summary>
        [NonSerialized]
        public TimeOfDayEventDelegate EventDelegate;

        public ActiveTimeOfDayEvent() { }

        public ActiveTimeOfDayEvent(float inEventTime)
        {
            EventTime = inEventTime;
        }

        public override int GetHashCode()
        {
            return (int)(EventTime * 10000f);
        }

        public override bool Equals(object obj)
        {
            if (obj is ActiveTimeOfDayEvent other)
            {
                return EventTime == other.EventTime;
            }
            if (obj is float time)
            {
                return EventTime == time;
            }
            return false;
        }
    }

    /// <summary>
    /// Narrative 游戏状态。对应 UE5 ANarrativeGameState。
    /// UE5 中继承 AGameStateBase；Flax 无 GameState 基类，改为 Script。
    /// 每个场景应挂载一个此 Script，负责昼夜系统、阵营联盟管理等全局状态。
    /// 简化点：
    /// - 移除 UE5 复制/RPC（OnRep_Xxx），改为本地逻辑 + 事件回调
    /// - 移除 INarrativeSavableActor 接口（Flax-不兼容: UE5 INarrativeSavableActor 在 Flax 无对应物，保留占位）
    /// - TSet/TMap 转为 List/Dictionary
    /// - 移除 BlueprintAsyncActionBase（Flax 无对应，异步操作通过事件回调实现）
    /// </summary>
    public class NarrativeGameState : Script
    {
        // ===== 时间事件 =====

        /// <summary>所有已注册的时间事件列表。对应 UE5 TimeOfDayEvents。</summary>
        public List<ActiveTimeOfDayEvent> TimeOfDayEvents = new List<ActiveTimeOfDayEvent>();

        // ===== 阵营联盟 =====

        /// <summary>
        /// 阵营联盟映射（SaveGame）。对应 UE5 FactionAllianceMap。
        /// 键为阵营标签（Narrative.Factions），值是该阵营对其他阵营的态度数据。
        /// </summary>
        public Dictionary<string, FactionAttitudeData> FactionAllianceMap = new Dictionary<string, FactionAttitudeData>(StringComparer.Ordinal);

        /// <summary>阵营态度改变事件。对应 UE5 OnFactionAttitudeChanged。</summary>
        public event OnFactionAttitudeChangedDelegate OnFactionAttitudeChanged;

        // ===== 时间状态（SaveGame）=====

        /// <summary>上一帧的时间（SaveGame）。对应 UE5 TimeOfDayLastTick。</summary>
        public float TimeOfDayLastTick = 0f;

        /// <summary>上一帧的累计时间（SaveGame）。对应 UE5 AccumulatedTimeLastTick。</summary>
        public float AccumulatedTimeLastTick = 0f;

        /// <summary>当前时间（0-2400，SaveGame）。对应 UE5 TimeOfDay。</summary>
        public float TimeOfDay = 800f;

        /// <summary>累计时间（2400 为 1 天，SaveGame）。对应 UE5 AccumulatedTime。</summary>
        public float AccumulatedTime = 0f;

        /// <summary>本帧待推进的时间。对应 UE5 PendingAdvanceTime。</summary>
        [NonSerialized]
        protected float PendingAdvanceTime = 0f;

        /// <summary>时间设置（缓存的 NarrativeTimeOfDaySettings 引用）。对应 UE5 TimeSettings。</summary>
        [NonSerialized]
        protected NarrativeTimeOfDaySettings TimeSettings;

        // ===== 内部 =====

        private int _nextEventHandleId = 1;

        // ===== 生命周期 =====

        public override void OnEnable()
        {
            base.OnEnable();
            TimeSettings = NarrativeTimeOfDaySettings.Instance;
            if (TimeSettings != null)
            {
                if (TimeOfDay == 0f && TimeSettings.DefaultTimeOfDay.Time > 0f)
                {
                    TimeOfDay = TimeSettings.DefaultTimeOfDay.Time;
                }
            }
        }

        public override void OnDisable()
        {
            base.OnDisable();
        }

        public override void OnUpdate()
        {
            float dt = Time.DeltaTime;
            UpdateTimeOfDay(dt);
        }

        // ===== 时间系统 =====

        /// <summary>每帧更新时间。对应 UE5 UpdateTimeOfDay。</summary>
        /// <param name="deltaSeconds">帧时间。</param>
        public virtual void UpdateTimeOfDay(float deltaSeconds)
        {
            // 处理待推进时间
            if (PendingAdvanceTime > 0f)
            {
                float toAdvance = PendingAdvanceTime;
                PendingAdvanceTime = 0f;
                AdvanceTimeInternal(toAdvance, true);
            }

            // 动态时间推进
            if (TimeSettings != null && TimeSettings.bDynamicTimeOfDay)
            {
                // 计算每秒推进的时间
                bool isDay = IsDayTime();
                float lengthMinutes = isDay ? TimeSettings.DayLengthMinutes : TimeSettings.NightLengthMinutes;
                if (lengthMinutes > 0.01f)
                {
                    // 2400 时间单位 / (lengthMinutes * 60 秒) = 每秒推进的时间单位
                    float timePerSecond = 2400f / (lengthMinutes * 60f);
                    AdvanceTimeInternal(timePerSecond * deltaSeconds, false);
                }
            }
        }

        /// <summary>内部推进时间。</summary>
        /// <param name="timeToAdvance">要推进的时间。</param>
        /// <param name="bFromAdvanceTime">是否由 AdvanceTimeOfDay 显式调用。</param>
        protected virtual void AdvanceTimeInternal(float timeToAdvance, bool bFromAdvanceTime)
        {
            TimeOfDayLastTick = TimeOfDay;
            AccumulatedTimeLastTick = AccumulatedTime;

            float prevTime = TimeOfDay;
            TimeOfDay += timeToAdvance;
            AccumulatedTime += timeToAdvance;

            // 归一化到 0-2400
            while (TimeOfDay >= 2400f) TimeOfDay -= 2400f;
            while (TimeOfDay < 0f) TimeOfDay += 2400f;

            // 触发区间内的事件
            float deltaPassed = timeToAdvance;
            FireEventsInRange(prevTime, TimeOfDay, deltaPassed, bFromAdvanceTime);
        }

        /// <summary>触发指定时间区间内的事件。</summary>
        protected virtual void FireEventsInRange(float prevTime, float currentTime, float deltaPassed, bool bFromAdvanceTime)
        {
            if (TimeOfDayEvents == null || TimeOfDayEvents.Count == 0) return;

            // 简化版：遍历所有事件，检查是否在区间内
            // 处理跨午夜的情况
            bool wrapped = currentTime < prevTime;
            for (int i = TimeOfDayEvents.Count - 1; i >= 0; i--)
            {
                var evt = TimeOfDayEvents[i];
                if (evt == null) continue;
                float t = evt.EventTime;
                bool inRange;
                if (!wrapped)
                {
                    inRange = t > prevTime && t <= currentTime;
                }
                else
                {
                    inRange = t > prevTime || t <= currentTime;
                }
                if (inRange)
                {
                    evt.EventDelegate?.Invoke(t, currentTime, deltaPassed, bFromAdvanceTime);
                }
            }
        }

        /// <summary>获取指定时间的事件委托（用于注册回调）。对应 UE5 GetTimeOfDayEventDelegate。</summary>
        public virtual TimeOfDayEventDelegate GetTimeOfDayEventDelegate(float time)
        {
            foreach (var evt in TimeOfDayEvents)
            {
                if (evt != null && evt.EventTime == time)
                {
                    return evt.EventDelegate;
                }
            }
            // 不存在则创建新事件
            var newEvent = new ActiveTimeOfDayEvent(time)
            {
                HandleID = _nextEventHandleId++
            };
            TimeOfDayEvents.Add(newEvent);
            return newEvent.EventDelegate;
        }

        /// <summary>注册时间事件回调。</summary>
        /// <param name="time">触发时间（0-2400）。</param>
        /// <param name="callback">回调委托。</param>
        /// <returns>事件句柄 ID（0 表示失败）。</returns>
        public virtual int RegisterTimeOfDayEvent(float time, TimeOfDayEventDelegate callback)
        {
            if (callback == null) return 0;
            foreach (var evt in TimeOfDayEvents)
            {
                if (evt != null && evt.EventTime == time)
                {
                    evt.EventDelegate += callback;
                    return evt.HandleID;
                }
            }
            var newEvent = new ActiveTimeOfDayEvent(time)
            {
                HandleID = _nextEventHandleId++
            };
            newEvent.EventDelegate += callback;
            TimeOfDayEvents.Add(newEvent);
            return newEvent.HandleID;
        }

        /// <summary>取消注册时间事件回调。</summary>
        public virtual void UnregisterTimeOfDayEvent(int handleId, TimeOfDayEventDelegate callback)
        {
            foreach (var evt in TimeOfDayEvents)
            {
                if (evt != null && evt.HandleID == handleId)
                {
                    evt.EventDelegate -= callback;
                    break;
                }
            }
        }

        /// <summary>手动推进时间。对应 UE5 AdvanceTimeOfDay。
        /// 100 = 1 小时（如 500 = 推进 5 小时，4800 = 推进 2 天）。
        /// 实际时间变化发生在下一帧。</summary>
        /// <param name="timeToAdvance">要推进的时间。</param>
        public virtual void AdvanceTimeOfDay(float timeToAdvance)
        {
            PendingAdvanceTime += timeToAdvance;
        }

        /// <summary>推进到指定时间。对应 UE5 AdvanceToTimeOfDay。</summary>
        /// <param name="desiredTime">目标时间（0-2400）。</param>
        public virtual void AdvanceToTimeOfDay(float desiredTime)
        {
            float diff = desiredTime - TimeOfDay;
            if (diff < 0f) diff += 2400f;
            AdvanceTimeOfDay(diff);
        }

        /// <summary>获取当前时间（0-2400）。</summary>
        public virtual float GetTimeOfDay() => TimeOfDay;

        /// <summary>获取累计时间。</summary>
        public virtual float GetAccumulatedTime() => AccumulatedTime;

        /// <summary>是否为白天。对应 UE5 IsDayTime。</summary>
        public virtual bool IsDayTime()
        {
            if (TimeSettings == null) return true;
            return TimeOfDay >= TimeSettings.SunriseTime.Time && TimeOfDay < TimeSettings.SunsetTime.Time;
        }

        /// <summary>获取时间推进速度。对应 UE5 GetTimeOfDayAdvanceSpeed。</summary>
        public virtual float GetTimeOfDayAdvanceSpeed()
        {
            if (TimeSettings == null || !TimeSettings.bDynamicTimeOfDay) return 0f;
            bool isDay = IsDayTime();
            float lengthMinutes = isDay ? TimeSettings.DayLengthMinutes : TimeSettings.NightLengthMinutes;
            if (lengthMinutes <= 0.01f) return 0f;
            return 2400f / (lengthMinutes * 60f);
        }

        // ===== 阵营态度系统 =====

        /// <summary>获取一组阵营对另一组阵营的态度。对应 UE5 GetFactionsAttitudeTowardsFactions。
        /// 任一为敌对则返回敌对；无敌对且至少一个友好则返回友好；否则返回中立。</summary>
        public virtual ArsenalStatics.ETeamAttitude GetFactionsAttitudeTowardsFactions(GameplayTagContainer sourceFactions, GameplayTagContainer targetFactions)
        {
            if (sourceFactions == null || targetFactions == null) return ArsenalStatics.ETeamAttitude.Neutral;

            bool anyFriendly = false;
            foreach (var src in sourceFactions.GetTags())
            {
                foreach (var tgt in targetFactions.GetTags())
                {
                    var attitude = GetOneWayFactionAttitude(new GameplayTag(src), new GameplayTag(tgt));
                    if (attitude == ArsenalStatics.ETeamAttitude.Hostile) return ArsenalStatics.ETeamAttitude.Hostile;
                    if (attitude == ArsenalStatics.ETeamAttitude.Friendly) anyFriendly = true;
                }
            }
            return anyFriendly ? ArsenalStatics.ETeamAttitude.Friendly : ArsenalStatics.ETeamAttitude.Neutral;
        }

        /// <summary>查询单个阵营对另一阵营的态度。对应 UE5 GetFactionAttitudeTowardsFaction。
        /// 默认双向检查（任一方向为敌对则视为敌对）。</summary>
        public virtual ArsenalStatics.ETeamAttitude GetFactionAttitudeTowardsFaction(GameplayTag sourceFaction, GameplayTag targetFaction)
        {
            var oneWay = GetOneWayFactionAttitude(sourceFaction, targetFaction);
            if (oneWay == ArsenalStatics.ETeamAttitude.Hostile) return ArsenalStatics.ETeamAttitude.Hostile;
            var reverse = GetOneWayFactionAttitude(targetFaction, sourceFaction);
            if (reverse == ArsenalStatics.ETeamAttitude.Hostile) return ArsenalStatics.ETeamAttitude.Hostile;
            if (oneWay == ArsenalStatics.ETeamAttitude.Friendly || reverse == ArsenalStatics.ETeamAttitude.Friendly)
                return ArsenalStatics.ETeamAttitude.Friendly;
            return ArsenalStatics.ETeamAttitude.Neutral;
        }

        /// <summary>查询单向阵营态度。对应 UE5 GetOneWayFactionAttitude。</summary>
        public virtual ArsenalStatics.ETeamAttitude GetOneWayFactionAttitude(GameplayTag sourceFaction, GameplayTag targetFaction)
        {
            if (!sourceFaction.IsValid() || !targetFaction.IsValid()) return ArsenalStatics.ETeamAttitude.Neutral;
            if (sourceFaction == targetFaction) return ArsenalStatics.ETeamAttitude.Friendly;

            if (FactionAllianceMap.TryGetValue(sourceFaction.TagName, out var data) && data != null)
            {
                if (data.AttitudeMap.TryGetValue(targetFaction.TagName, out var attitude))
                {
                    return attitude;
                }
            }
            return ArsenalStatics.ETeamAttitude.Neutral;
        }

        /// <summary>设置阵营态度。对应 UE5 SetFactionAttitude。</summary>
        public virtual void SetFactionAttitude(GameplayTag sourceFaction, GameplayTag targetFaction, ArsenalStatics.ETeamAttitude newAttitude)
        {
            if (!sourceFaction.IsValid() || !targetFaction.IsValid()) return;

            if (!FactionAllianceMap.TryGetValue(sourceFaction.TagName, out var data))
            {
                data = new FactionAttitudeData();
                FactionAllianceMap[sourceFaction.TagName] = data;
            }
            data.AttitudeMap[targetFaction.TagName] = newAttitude;
            OnFactionAttitudeChanged?.Invoke(sourceFaction, targetFaction, newAttitude);
        }

        // ===== 存档（INarrativeSavableActor 等价）=====

        /// <summary>准备保存。对应 UE5 PrepareForSave_Implementation。</summary>
        public virtual void PrepareForSave()
        {
            // 时间和阵营数据已标记 SaveGame，由 Flax 存档系统序列化
        }

        /// <summary>加载存档。对应 UE5 Load_Implementation。</summary>
        public virtual void Load()
        {
            // TODO [需接入存档系统]: 从存档恢复时间和阵营数据
        }

        // ===== 静态访问 =====

        /// <summary>获取指定场景的 NarrativeGameState。</summary>
        public static NarrativeGameState Get(Scene scene)
        {
            if (scene == null) return null;
            return scene.GetScript<NarrativeGameState>();
        }

        /// <summary>获取当前激活场景的 NarrativeGameState。</summary>
        public static NarrativeGameState GetCurrent()
        {
            return Get(Level.GetScene(0));
        }
    }

    // ===== 异步时间事件辅助类（对应 UE5 UAsyncAction_WaitTimeOfDay 等）=====
    // 注：Flax 无 BlueprintAsyncActionBase 等价物，简化为普通类 + 事件回调。

    /// <summary>
    /// 等待指定时间的异步操作。对应 UE5 UAsyncAction_WaitTimeOfDay。
    /// 简化：通过 NarrativeGameState.RegisterTimeOfDayEvent 注册回调。
    /// </summary>
    public class AsyncActionWaitTimeOfDay
    {
        /// <summary>到达时间时触发。</summary>
        public event TimeOfDayEventDelegate OnTimeReached;

        /// <summary>已注册的事件句柄。</summary>
        public int TimeEventHandle;

        /// <summary>等待的时间。</summary>
        public float Time;

        /// <summary>关联的 GameState。</summary>
        [NonSerialized]
        protected NarrativeGameState GameState;

        /// <summary>创建并启动等待操作。对应 UE5 WaitTimeOfDay。</summary>
        public static AsyncActionWaitTimeOfDay WaitTimeOfDay(NarrativeGameState gameState, float time)
        {
            var action = new AsyncActionWaitTimeOfDay
            {
                GameState = gameState,
                Time = time
            };
            action.Activate();
            return action;
        }

        /// <summary>激活操作。对应 UE5 Activate。</summary>
        public virtual void Activate()
        {
            if (GameState == null) return;
            TimeEventHandle = GameState.RegisterTimeOfDayEvent(Time, OnHitTimeOfDay);
        }

        /// <summary>到达时间时调用。对应 UE5 OnHitTimeOfDay。</summary>
        public virtual void OnHitTimeOfDay(float eventTime, float actualTime, float timePassedDelta, bool bFiredFromAdvancedTime)
        {
            OnTimeReached?.Invoke(eventTime, actualTime, timePassedDelta, bFiredFromAdvancedTime);
            // 触发后取消注册
            if (GameState != null && TimeEventHandle != 0)
            {
                GameState.UnregisterTimeOfDayEvent(TimeEventHandle, OnHitTimeOfDay);
                TimeEventHandle = 0;
            }
        }
    }

    /// <summary>
    /// 等待指定时间范围的异步操作。对应 UE5 UAsyncAction_WaitTimeRange。
    /// </summary>
    public class AsyncActionWaitTimeRange
    {
        /// <summary>范围开始时触发。</summary>
        public event TimeOfDayEventDelegate OnTimeRangeStart;

        /// <summary>范围结束时触发。</summary>
        public event TimeOfDayEventDelegate OnTimeRangeEnd;

        /// <summary>范围起始时间。</summary>
        public float TimeStart;

        /// <summary>范围结束时间。</summary>
        public float TimeEnd;

        /// <summary>关联的 GameState。</summary>
        [NonSerialized]
        protected NarrativeGameState GameState;

        private int _startHandle;
        private int _endHandle;

        /// <summary>创建并启动等待操作。对应 UE5 WaitTimeRange。</summary>
        public static AsyncActionWaitTimeRange WaitTimeRange(NarrativeGameState gameState, float timeStart, float timeEnd)
        {
            var action = new AsyncActionWaitTimeRange
            {
                GameState = gameState,
                TimeStart = timeStart,
                TimeEnd = timeEnd
            };
            action.Activate();
            return action;
        }

        /// <summary>激活操作。</summary>
        public virtual void Activate()
        {
            if (GameState == null) return;
            _startHandle = GameState.RegisterTimeOfDayEvent(TimeStart, OnHitTimeStart);
            _endHandle = GameState.RegisterTimeOfDayEvent(TimeEnd, OnHitTimeEnd);
        }

        /// <summary>范围开始时调用。</summary>
        public virtual void OnHitTimeStart(float eventTime, float actualTime, float timePassedDelta, bool bFiredFromAdvancedTime)
        {
            OnTimeRangeStart?.Invoke(eventTime, actualTime, timePassedDelta, bFiredFromAdvancedTime);
        }

        /// <summary>范围结束时调用。</summary>
        public virtual void OnHitTimeEnd(float eventTime, float actualTime, float timePassedDelta, bool bFiredFromAdvancedTime)
        {
            OnTimeRangeEnd?.Invoke(eventTime, actualTime, timePassedDelta, bFiredFromAdvancedTime);
        }
    }

    /// <summary>
    /// 等待日出日落的异步操作。对应 UE5 UAsyncAction_WaitSunsetAndSunrise。
    /// </summary>
    public class AsyncActionWaitSunsetAndSunrise
    {
        /// <summary>日出时触发。</summary>
        public event TimeOfDayEventDelegate OnSunrise;

        /// <summary>日落时触发。</summary>
        public event TimeOfDayEventDelegate OnSunset;

        /// <summary>关联的 GameState。</summary>
        [NonSerialized]
        protected NarrativeGameState GameState;

        private int _sunriseHandle;
        private int _sunsetHandle;

        /// <summary>创建并启动等待操作。对应 UE5 WaitSunsetAndRise。</summary>
        public static AsyncActionWaitSunsetAndSunrise WaitSunsetAndRise(NarrativeGameState gameState)
        {
            var action = new AsyncActionWaitSunsetAndSunrise
            {
                GameState = gameState
            };
            action.Activate();
            return action;
        }

        /// <summary>激活操作。</summary>
        public virtual void Activate()
        {
            if (GameState == null) return;
            var settings = NarrativeTimeOfDaySettings.Instance;
            if (settings == null) return;

            _sunriseHandle = GameState.RegisterTimeOfDayEvent(settings.SunriseTime.Time, Sunrise);
            _sunsetHandle = GameState.RegisterTimeOfDayEvent(settings.SunsetTime.Time, Sunset);
        }

        /// <summary>日出时调用。</summary>
        public virtual void Sunrise(float eventTime, float actualTime, float timePassedDelta, bool bFiredFromAdvancedTime)
        {
            OnSunrise?.Invoke(eventTime, actualTime, timePassedDelta, bFiredFromAdvancedTime);
        }

        /// <summary>日落时调用。</summary>
        public virtual void Sunset(float eventTime, float actualTime, float timePassedDelta, bool bFiredFromAdvancedTime)
        {
            OnSunset?.Invoke(eventTime, actualTime, timePassedDelta, bFiredFromAdvancedTime);
        }
    }

    /// <summary>
    /// 计划行为基类。对应 UE5 UScheduledBehavior。
    /// 绑定到指定时间范围，在范围内/外时触发 HandleStarted/HandleEnded。
    /// 注：UE5 中为可蓝图继承的 UObject；Flax 中简化为 [Serializable] 普通类。
    /// </summary>
    [Serializable]
    public class ScheduledBehavior
    {
        /// <summary>是否禁用。</summary>
        public bool bDisabled = false;

        /// <summary>开始时间。</summary>
        public float StartTime = 0f;

        /// <summary>结束时间。</summary>
        public float EndTime = 0f;

        [NonSerialized]
        protected int _startHandle;

        [NonSerialized]
        protected int _endHandle;

        /// <summary>开始行为描述。对应 UE5 DescribeBehavior_Implementation。</summary>
        public virtual string DescribeBehavior()
        {
            return $"ScheduledBehavior [{StartTime}-{EndTime}]";
        }

        /// <summary>绑定行为。对应 UE5 BindBehavior。</summary>
        /// <param name="gameState">关联的 GameState。</param>
        /// <param name="bFireIfAlreadyStarted">若已在范围内，是否立即触发开始。</param>
        public virtual void BindBehavior(NarrativeGameState gameState, bool bFireIfAlreadyStarted = false)
        {
            if (gameState == null || bDisabled) return;

            _startHandle = gameState.RegisterTimeOfDayEvent(StartTime, DispatchHandleStarted);
            _endHandle = gameState.RegisterTimeOfDayEvent(EndTime, DispatchHandleEnded);

            if (bFireIfAlreadyStarted)
            {
                float currentTime = gameState.GetTimeOfDay();
                bool inRange;
                if (StartTime < EndTime)
                {
                    inRange = currentTime >= StartTime && currentTime < EndTime;
                }
                else
                {
                    // 跨午夜
                    inRange = currentTime >= StartTime || currentTime < EndTime;
                }
                if (inRange)
                {
                    DispatchHandleStarted(StartTime, currentTime, 0f, false);
                }
            }
        }

        /// <summary>开始时调用。对应 UE5 HandleStarted_Implementation。</summary>
        protected virtual void HandleStarted(float eventTime, float actualTime, float timePassedDelta, bool bFiredFromAdvancedTime)
        {
            // 子类重写
        }

        /// <summary>结束时调用。对应 UE5 HandleEnded_Implementation。</summary>
        protected virtual void HandleEnded(float eventTime, float actualTime, float timePassedDelta, bool bFiredFromAdvancedTime)
        {
            // 子类重写
        }

        /// <summary>分发开始事件。对应 UE5 DispatchHandleStarted。</summary>
        protected virtual void DispatchHandleStarted(float eventTime, float actualTime, float timePassedDelta, bool bFiredFromAdvancedTime)
        {
            HandleStarted(eventTime, actualTime, timePassedDelta, bFiredFromAdvancedTime);
        }

        /// <summary>分发结束事件。对应 UE5 DispatchHandleEnded。</summary>
        protected virtual void DispatchHandleEnded(float eventTime, float actualTime, float timePassedDelta, bool bFiredFromAdvancedTime)
        {
            HandleEnded(eventTime, actualTime, timePassedDelta, bFiredFromAdvancedTime);
        }
    }
}
