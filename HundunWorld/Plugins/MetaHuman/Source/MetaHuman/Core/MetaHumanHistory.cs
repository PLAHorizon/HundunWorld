using System;
using System.Collections.Generic;
using System.Reflection;

namespace MetaHuman
{
    /// <summary>
    /// 自定义过程的快照日志，支撑「回溯到任意步骤」与「发布后停止回溯」。
    ///
    /// 存储是**差分**的（方案文档 §116，用户 2026-10-14 拍板"差分存储，无限回溯"）：
    /// 一步一次提交，步骤永不丢弃 —— 旧实现每超 <c>MaxDepth</c> 步就把最旧的永久裁掉，
    /// 而实测一次完整捏脸要 150 步（脸 120 根语义滑杆 + 身 30 根），64 的上限与"任意步骤"直接冲突。
    ///
    /// 每步要么是关键帧（整份参数表），要么只存"相对上一步的变化 + 被删掉的键"；
    /// 每隔 <see cref="KeyframeInterval"/> 步打一个关键帧，把任意跳转的重放长度封顶。
    /// 标量格（TemplateId/Published/Checksum…）每步照抄整份，因此新增字段不需要改这里就能被带上。
    /// </summary>
    [Serializable]
    public class MetaHumanHistory
    {
        /// <summary>一条步骤：关键帧时 <see cref="Look"/> 的 Values 是整份，差分时是"变化项"。</summary>
        private class Step
        {
            public MetaHumanLook Look;
            public bool IsKeyframe;
            public List<string> Removals;   // 差分步：上一步有、这一步没有的键
            public string Label;
            public double Time;
        }

        private readonly List<Step> _steps = new List<Step>();
        private int _cursor = -1;

        // 最近一次重放出来的整份参数表，只为让连续 Undo/Redo 不必从头重放
        private int _cachedIndex = -1;
        private Dictionary<string, float> _cachedValues;

        /// <summary>每隔多少步打一个关键帧（封顶任意跳转的重放长度）。必须 ≥1。</summary>
        public int KeyframeInterval { get; set; }

        /// <summary>同一步内的连续拖动合并窗口（秒）：标签相同且间隔小于它则覆盖当前步。</summary>
        public float CoalesceSeconds { get; set; }

        /// <summary>发布后为 true：不再接受任何新步骤。</summary>
        public bool Sealed { get; private set; }

        /// <summary>时间源可注入，离线断言要用可控时钟而不是真实时间。</summary>
        public Func<double> Clock;

        public event Action OnChanged;

        public MetaHumanHistory()
        {
            KeyframeInterval = 32;
            CoalesceSeconds = 0.45f;
            Clock = DefaultClock;
        }

        private static double DefaultClock()
        {
            return DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerSecond;
        }

        public int StepCount { get { return _steps.Count; } }

        public int Cursor { get { return _cursor; } }

        /// <summary>关键帧条数（内存遥测：应约为 StepCount / KeyframeInterval）。</summary>
        public int KeyframeCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _steps.Count; i++)
                {
                    if (_steps[i].IsKeyframe)
                    {
                        n++;
                    }
                }
                return n;
            }
        }

        /// <summary>差分步里累计的"变化项 + 删除项"总数（遥测：证明步与步之间确实是小的）。</summary>
        public int DeltaEntryCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _steps.Count; i++)
                {
                    if (_steps[i].IsKeyframe)
                    {
                        continue;
                    }
                    if (_steps[i].Look != null && _steps[i].Look.Values != null)
                    {
                        n += _steps[i].Look.Values.Count;
                    }
                    if (_steps[i].Removals != null)
                    {
                        n += _steps[i].Removals.Count;
                    }
                }
                return n;
            }
        }

        // 封存（发布）之后不再给回退余地：UI 的回溯入口只看这两个标志，
        // 原来只看游标位置，于是 Seal() 之后按钮照样能点，与"发布后不再可回溯"冲突。
        public bool CanUndo { get { return !Sealed && _cursor > 0; } }

        public bool CanRedo { get { return !Sealed && _cursor >= 0 && _cursor < _steps.Count - 1; } }

        public bool IsSealed { get { return Sealed; } }

        public MetaHumanLook Current
        {
            get
            {
                return _cursor >= 0 && _cursor < _steps.Count ? Snapshot(_cursor) : null;
            }
        }

        public MetaHumanLook EntryAt(int index)
        {
            if (index < 0 || index >= _steps.Count)
            {
                return null;
            }

            return Snapshot(index);
        }

        public string LabelAt(int index)
        {
            if (index < 0 || index >= _steps.Count)
            {
                return string.Empty;
            }

            return _steps[index].Label ?? string.Empty;
        }

        public List<string> Labels()
        {
            List<string> labels = new List<string>(_steps.Count);
            for (int i = 0; i < _steps.Count; i++)
            {
                labels.Add(_steps[i].Label ?? string.Empty);
            }
            return labels;
        }

        /// <summary>基线条目：进入会话时的状态，占据第 0 步，任何回溯都能落到它。</summary>
        public void Initialize(MetaHumanLook baseline)
        {
            _steps.Clear();
            InvalidateCache();

            Step step = new Step();
            step.IsKeyframe = true;
            step.Label = "初始";
            step.Time = Clock != null ? Clock() : DefaultClock();
            step.Look = NormalizeValues(baseline);
            _steps.Add(step);

            _cursor = 0;
            // 基线快照自己就带"已发布"这个事实（MetaHumanLook.Published 是序列化进去的一格）。
            // 以前这里无条件 false ⇒ 重登后本地会话以为还没封存，撤销/跳步/继续记步全开着，
            // 玩家一路操作到服务端才被拒——"发布后不再可回溯"这条只在第一次会话里成立。
            Sealed = baseline != null && baseline.Published;
            Raise();
        }

        /// <summary>
        /// 提交一步。返回 false 表示被拒（已封存），调用方据此在 UI 上明示「发布后不可回溯」。
        /// 步骤永不上限裁剪：这是"可回溯任意步骤"的实现前提。
        /// </summary>
        public bool Commit(MetaHumanLook look, string label)
        {
            if (Sealed || look == null)
            {
                return false;
            }

            double now = Clock != null ? Clock() : DefaultClock();

            if (_cursor >= 0 && _cursor < _steps.Count
                && LabelAt(_cursor) == label
                && ShouldCoalesce(now))
            {
                ReplaceStep(_cursor, look, label, now);
                Raise();
                return true;
            }

            // 从历史中间再编辑：未来的步骤被丢弃，与主流编辑器的分支语义一致。
            while (_steps.Count > _cursor + 1)
            {
                _steps.RemoveAt(_steps.Count - 1);
            }
            InvalidateCache();

            NormalizeValues(look);
            Step step = new Step();
            step.Label = label ?? string.Empty;
            step.Time = now;
            step.Look = look;
            step.IsKeyframe = (_steps.Count % SafeKeyframeInterval()) == 0;

            if (step.IsKeyframe)
            {
                step.Removals = null;
            }
            else
            {
                // 差分项必须写进一份新字典：原地改 look.Values 等于"边遍历 next 边写 next"，
                // 那会当场抛枚举异常或写出半个差分。
                Dictionary<string, float> previous = ValuesAt(_steps.Count - 1);
                Dictionary<string, float> changes = new Dictionary<string, float>(StringComparer.Ordinal);
                step.Removals = new List<string>();
                DiffValues(previous, look.Values, changes, step.Removals);
                step.Look.Values = changes;
            }

            _steps.Add(step);
            _cursor = _steps.Count - 1;
            Raise();
            return true;
        }

        public MetaHumanLook Undo()
        {
            if (Sealed || !CanUndo)
            {
                return null;
            }

            _cursor--;
            Raise();
            return Current;
        }

        public MetaHumanLook Redo()
        {
            if (Sealed || !CanRedo)
            {
                return null;
            }

            _cursor++;
            Raise();
            return Current;
        }

        /// <summary>任意步骤直达。</summary>
        public MetaHumanLook JumpTo(int index)
        {
            if (Sealed || index < 0 || index >= _steps.Count)
            {
                return null;
            }

            _cursor = index;
            Raise();
            return Current;
        }

        /// <summary>发布：封存历史。步骤仍留在日志里作为记录，但撤销/重做/跳步一律拒绝。</summary>
        public MetaHumanLook Seal()
        {
            Sealed = true;
            Raise();
            return Current;
        }

        /// <summary>显式清空（新会话复用实例时用；封存态下不允许清空，防止绕过限制）。</summary>
        public bool Reset()
        {
            if (Sealed)
            {
                return false;
            }

            _steps.Clear();
            InvalidateCache();
            _cursor = -1;
            Raise();
            return true;
        }

        private int SafeKeyframeInterval()
        {
            return KeyframeInterval < 1 ? 1 : KeyframeInterval;
        }

        private bool ShouldCoalesce(double now)
        {
            if (CoalesceSeconds <= 0f)
            {
                return false;
            }

            return now - _steps[_cursor].Time < CoalesceSeconds;
        }

        /// <summary>
        /// 覆盖当前步（同一次拖动的连续提交合并成一步）。必须按"该步原来是不是关键帧"重建差分项，
        /// 否则会把一条差分步错当成关键帧留下，回退时凭空多出/少掉参数。
        /// </summary>
        private void ReplaceStep(int index, MetaHumanLook look, string label, double now)
        {
            Step existing = _steps[index];
            NormalizeValues(look);

            Step replaced = new Step();
            replaced.Label = label ?? string.Empty;
            replaced.Time = now;
            replaced.Look = look;
            replaced.IsKeyframe = existing.IsKeyframe;

            if (replaced.IsKeyframe)
            {
                replaced.Removals = null;
            }
            else
            {
                Dictionary<string, float> previous = ValuesAt(index - 1);
                Dictionary<string, float> changes = new Dictionary<string, float>(StringComparer.Ordinal);
                replaced.Removals = new List<string>();
                DiffValues(previous, look.Values, changes, replaced.Removals);
                replaced.Look.Values = changes;
            }

            _steps[index] = replaced;
            InvalidateCache();
        }

        private static MetaHumanLook NormalizeValues(MetaHumanLook look)
        {
            if (look != null && look.Values == null)
            {
                look.Values = new Dictionary<string, float>(StringComparer.Ordinal);
            }
            return look;
        }

        /// <summary>
        /// 把 <paramref name="previous"/> 与 <paramref name="next"/> 的差写进 <paramref name="changes"/>/<paramref name="removals"/>。
        /// 只比 Values：其余标量格整份存，不在这儿处理（所以新增标量字段不会在这里被漏掉）。
        /// </summary>
        private static void DiffValues(Dictionary<string, float> previous,
                                       Dictionary<string, float> next,
                                       Dictionary<string, float> changes,
                                       List<string> removals)
        {
            if (next != null)
            {
                foreach (KeyValuePair<string, float> kv in next)
                {
                    float old;
                    if (previous == null || !previous.TryGetValue(kv.Key, out old) || old != kv.Value)
                    {
                        changes[kv.Key] = kv.Value;
                    }
                }
            }

            if (previous != null)
            {
                foreach (string key in previous.Keys)
                {
                    if (next == null || !next.ContainsKey(key))
                    {
                        removals.Add(key);
                    }
                }
            }
        }

        /// <summary>
        /// 上一次 <see cref="ValuesAt"/> 真正重放了多少步（命中缓存时为 0）。
        /// 这是"跳任意一步的代价被关键帧间隔封顶"这条承诺的观测量；没有它，那句話只是注释。
        /// 记在同一条走法里，不另写一份等价代码——等价的两份实现会一起错。
        /// </summary>
        public int LastReplayLength { get; private set; }

        /// <summary>重放出第 index 步的整份参数表（从最近的关键帧往前推，长度被 KeyframeInterval 封顶）。</summary>
        private Dictionary<string, float> ValuesAt(int index)
        {
            if (index < 0)
            {
                LastReplayLength = 0;
                return new Dictionary<string, float>(StringComparer.Ordinal);
            }

            if (_cachedIndex == index && _cachedValues != null)
            {
                LastReplayLength = 0;
                return new Dictionary<string, float>(_cachedValues, StringComparer.Ordinal);
            }

            int anchor = index;
            while (anchor > 0 && !_steps[anchor].IsKeyframe)
            {
                anchor--;
            }

            LastReplayLength = index - anchor;

            Step anchorStep = _steps[anchor];
            Dictionary<string, float> values = anchorStep.Look != null && anchorStep.Look.Values != null
                ? new Dictionary<string, float>(anchorStep.Look.Values, StringComparer.Ordinal)
                : new Dictionary<string, float>(StringComparer.Ordinal);

            for (int k = anchor + 1; k <= index; k++)
            {
                Step step = _steps[k];
                if (step.IsKeyframe)
                {
                    values = step.Look != null && step.Look.Values != null
                        ? new Dictionary<string, float>(step.Look.Values, StringComparer.Ordinal)
                        : new Dictionary<string, float>(StringComparer.Ordinal);
                    continue;
                }

                if (step.Removals != null)
                {
                    for (int r = 0; r < step.Removals.Count; r++)
                    {
                        values.Remove(step.Removals[r]);
                    }
                }

                if (step.Look != null && step.Look.Values != null)
                {
                    foreach (KeyValuePair<string, float> kv in step.Look.Values)
                    {
                        values[kv.Key] = kv.Value;
                    }
                }
            }

            _cachedIndex = index;
            _cachedValues = new Dictionary<string, float>(values, StringComparer.Ordinal);
            return values;
        }

        /// <summary>把某一步还原成完整快照（标量格取自该步自己那份，Values 用重放结果）。</summary>
        private MetaHumanLook Snapshot(int index)
        {
            Step step = _steps[index];
            MetaHumanLook look = CloneScalars(step.Look);
            look.Values = ValuesAt(index);
            return look;
        }

        /// <summary>
        /// 反射拷贝标量格、跳过 Values：这样"以后给 MetaHumanLook 加一格"不需要回来改这里，
        /// 也不会出现"加了字段但历史层没带上"的静默丢值（本项目栽过同类）。
        /// </summary>
        private static MetaHumanLook CloneScalars(MetaHumanLook source)
        {
            MetaHumanLook copy = new MetaHumanLook();
            if (source == null)
            {
                return copy;
            }

            PropertyInfo[] properties = typeof(MetaHumanLook).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < properties.Length; i++)
            {
                PropertyInfo property = properties[i];
                if (property.Name == "Values" || !property.CanRead || !property.CanWrite)
                {
                    continue;
                }

                Type type = property.PropertyType;
                if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal))
                {
                    property.SetValue(copy, property.GetValue(source, null), null);
                }
            }

            return copy;
        }

        private void InvalidateCache()
        {
            _cachedIndex = -1;
            _cachedValues = null;
        }

        private void Raise()
        {
            Action handler = OnChanged;
            if (handler != null)
            {
                handler();
            }
        }
    }
}
