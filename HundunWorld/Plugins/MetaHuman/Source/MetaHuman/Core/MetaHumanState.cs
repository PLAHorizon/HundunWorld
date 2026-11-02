using System;
using System.Collections.Generic;

namespace MetaHuman
{
    /// <summary>
    /// 一次自定义会话的参数值集合。只存「偏离默认值」的项，因此快照天然紧凑、
    /// 且模板加参数不会让旧存档错位（缺失的键按默认值解释）。
    /// </summary>
    [Serializable]
    public class MetaHumanState
    {
        private readonly MetaHumanTemplate _template;
        private readonly Dictionary<string, float> _values = new Dictionary<string, float>();

        /// <summary>左右联动：改一侧时同对岸参数写入同一值。</summary>
        public bool LinkSymmetric { get; set; }

        /// <summary>本帧被改动的参数：Id → 改动前的值。增量求解后由 SolveIncremental 清空。</summary>
        private readonly Dictionary<string, float> _dirty = new Dictionary<string, float>();

        public MetaHumanTemplate Template { get { return _template; } }

        public MetaHumanState(MetaHumanTemplate template)
        {
            if (template == null)
            {
                throw new ArgumentNullException("template");
            }

            _template = template;
            _template.EnsureIndexed();
            LinkSymmetric = true;
        }

        public int ModifiedCount { get { return _values.Count; } }

        public float Get(string paramId)
        {
            MetaHumanParameter p = _template.Find(paramId);
            if (p == null)
            {
                return 0f;
            }

            float v;
            return _values.TryGetValue(paramId, out v) ? v : p.Default;
        }

        public bool IsModified(string paramId)
        {
            return _values.ContainsKey(paramId);
        }

        public IEnumerable<KeyValuePair<string, float>> ModifiedValues()
        {
            return _values;
        }

        public IReadOnlyDictionary<string, float> Dirty { get { return _dirty; } }

        /// <summary>
        /// 设置一个数值。返回实际生效（钳位后）的值；未知 Id 返回 0 且不产生任何副作用。
        /// </summary>
        public float Set(string paramId, float value)
        {
            MetaHumanParameter p = _template.Find(paramId);
            if (p == null)
            {
                return 0f;
            }

            float applied = p.Clamp(value);
            ApplyOne(p, applied);

            if (LinkSymmetric && p.Side != MetaHumanSide.None && !string.IsNullOrEmpty(p.PairId))
            {
                MetaHumanParameter pair = _template.Find(p.PairId);
                if (pair != null)
                {
                    ApplyOne(pair, pair.Clamp(value));
                }
            }

            EnforceConstraints();
            return applied;
        }

        private void ApplyOne(MetaHumanParameter p, float value)
        {
            float previous = Get(p.Id);
            if (p.IsDefault(value))
            {
                _values.Remove(p.Id);
            }
            else
            {
                _values[p.Id] = value;
            }

            if (!_dirty.ContainsKey(p.Id))
            {
                _dirty[p.Id] = previous;
            }
        }

        /// <summary>写入但不记脏（用于整包恢复快照）。</summary>
        public void SetRaw(string paramId, float value)
        {
            MetaHumanParameter p = _template.Find(paramId);
            if (p == null)
            {
                return;
            }

            float clamped = p.Clamp(value);
            if (p.IsDefault(clamped))
            {
                _values.Remove(paramId);
            }
            else
            {
                _values[paramId] = clamped;
            }
        }

        public void ClearDirty()
        {
            _dirty.Clear();
        }

        public void Reset()
        {
            foreach (KeyValuePair<string, float> kv in _values)
            {
                _dirty[kv.Key] = kv.Value;
            }

            _values.Clear();
        }

        public void ApplyPreset(MetaHumanPreset preset)
        {
            if (preset == null)
            {
                return;
            }

            // "回默认"型预设必须真的清干净：只逐条写 Values 的话，Values 为空的预设点下去
            // 什么都没发生，而界面照样显示"已应用"（face.neutral 就是这么一份死预设）。
            if (preset.ResetToDefault)
            {
                Reset();
            }

            foreach (KeyValuePair<string, float> kv in preset.Values)
            {
                MetaHumanParameter p = _template.Find(kv.Key);
                if (p != null)
                {
                    ApplyOne(p, kv.Value);
                }
            }

            EnforceConstraints();
        }

        /// <summary>两个预设线性混合，对应 UE 的 BlendPresets。</summary>
        public void BlendPresets(MetaHumanPreset a, MetaHumanPreset b, float t)
        {
            if (a == null || b == null)
            {
                return;
            }

            if (t < 0f)
            {
                t = 0f;
            }
            else if (t > 1f)
            {
                t = 1f;
            }

            HashSet<string> ids = new HashSet<string>();
            foreach (string key in a.Values.Keys)
            {
                ids.Add(key);
            }

            foreach (string key in b.Values.Keys)
            {
                ids.Add(key);
            }

            foreach (string id in ids)
            {
                MetaHumanParameter p = _template.Find(id);
                if (p == null)
                {
                    continue;
                }

                float va = a.Values.ContainsKey(id) ? a.Values[id] : Get(id);
                float vb = b.Values.ContainsKey(id) ? b.Values[id] : Get(id);
                ApplyOne(p, va + (vb - va) * t);
            }

            EnforceConstraints();
        }

        /// <summary>
        /// 在合法区间内随机一个整张脸/全身，对应 UE 的 Randomize。
        /// 用显式种子：离线断言要可复现，不能依赖时钟。
        /// </summary>
        public void Randomize(int seed)
        {
            Random random = new Random(seed);
            foreach (MetaHumanParameter p in _template.All())
            {
                if (p.Kind == MetaHumanParamKind.SkinTone || p.Kind == MetaHumanParamKind.SkinDetail)
                {
                    // 肤色类不参与随机：随机出来的种族特征不是玩家想要的「换个脸」，
                    // 且会把 melanin 通道的物理范围搞成噪声。
                    continue;
                }

                ApplyOne(p, p.Min + (float)random.NextDouble() * (p.Max - p.Min));
            }

            EnforceConstraints();
        }

        /// <summary>
        /// 约束求解：一组参数的偏离量之和越界时，按「同侧总偏离」等比例回退。
        /// 只在被违反时动手，不做迭代投影（UE 侧也是按区间钳位而非全局优化）。
        /// </summary>
        public void EnforceConstraints()
        {
            for (int c = 0; c < _template.Constraints.Count; c++)
            {
                MetaHumanConstraint constraint = _template.Constraints[c];
                if (constraint.Kind != MetaHumanConstraintKind.Sum)
                {
                    continue;
                }

                float total = 0f;
                for (int i = 0; i < constraint.ParamIds.Count; i++)
                {
                    MetaHumanParameter p = _template.Find(constraint.ParamIds[i]);
                    if (p != null)
                    {
                        total += Get(p.Id) - p.Default;
                    }
                }

                float excess = total > constraint.Max ? total - constraint.Max : 0f;
                float deficit = total < constraint.Min ? total - constraint.Min : 0f;
                if (excess <= 0f && deficit >= 0f)
                {
                    continue;
                }

                // 正值超额只回退正偏离项，负值不足只回退负偏离项，避免把符号翻过来。
                float basis = 0f;
                for (int i = 0; i < constraint.ParamIds.Count; i++)
                {
                    MetaHumanParameter p = _template.Find(constraint.ParamIds[i]);
                    if (p == null)
                    {
                        continue;
                    }

                    float delta = Get(p.Id) - p.Default;
                    if ((excess > 0f && delta > 0f) || (deficit < 0f && delta < 0f))
                    {
                        basis += delta;
                    }
                }

                if (Math.Abs(basis) < 1e-6f)
                {
                    continue;
                }

                float ratio = excess > 0f ? (basis - excess) / basis : (basis - deficit) / basis;
                for (int i = 0; i < constraint.ParamIds.Count; i++)
                {
                    MetaHumanParameter p = _template.Find(constraint.ParamIds[i]);
                    if (p == null)
                    {
                        continue;
                    }

                    float delta = Get(p.Id) - p.Default;
                    if ((excess > 0f && delta > 0f) || (deficit < 0f && delta < 0f))
                    {
                        ApplyOne(p, p.Default + delta * ratio);
                    }
                }
            }
        }

        public int ModifiedParameterCount()
        {
            return _values.Count;
        }

        public List<string> ModifiedParamIds()
        {
            List<string> ids = new List<string>(_values.Keys);
            ids.Sort(StringComparer.Ordinal);
            return ids;
        }
    }
}
