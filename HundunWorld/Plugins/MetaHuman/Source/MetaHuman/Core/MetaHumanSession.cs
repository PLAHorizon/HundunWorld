using System;
using System.Collections.Generic;

namespace MetaHuman
{
    /// <summary>
    /// 一次数字人自定义会话：把模板、数值、求解、回溯、发布串成唯一的入口。
    /// 写回端（Flax 侧）只订阅 OnSolutionChanged，UI 只调 Adjust/Commit/Undo/JumpTo/Publish，
    /// 两边都不碰映射表，避免又一处「改了数值没反应」的静默失效。
    /// </summary>
    public class MetaHumanSession
    {
        public MetaHumanTemplate Template { get; private set; }
        public MetaHumanState State { get; private set; }
        public MetaHumanHistory History { get; private set; }

        /// <summary>当前生效的解；写回端逐字段消费。</summary>
        public MetaHumanSolution Solution { get; private set; }

        /// <summary>第二个参数为 true 表示这次是全量重解（首帧、跳步、预设、随机之后）。</summary>
        public event Action<MetaHumanSolution, bool> OnSolutionChanged;

        public event Action OnSealed;

        private bool _pendingFull;
        private string _activePresetId;

        /// <summary>本会话已发出的最大修订号；每次保存/发布只增不减。</summary>
        private long _revision;

        public long Revision { get { return _revision; } }

        public bool IsSealed { get { return History.IsSealed; } }

        public int StepCount { get { return History.StepCount; } }

        public int Cursor { get { return History.Cursor; } }

        public bool CanUndo { get { return History.CanUndo; } }

        public bool CanRedo { get { return History.CanRedo; } }

        public MetaHumanSession(MetaHumanTemplate template)
        {
            if (template == null)
            {
                throw new ArgumentNullException("template");
            }

            List<string> errors = template.Validate();
            if (errors.Count > 0)
            {
                throw new ArgumentException("模板校验未通过：" + string.Join(" | ", errors.ToArray()));
            }

            Template = template;
            State = new MetaHumanState(template);
            Solution = new MetaHumanSolution();
            History = new MetaHumanHistory();
            History.OnChanged += OnHistoryChanged;

            _pendingFull = true;
            Rebuild();
            History.Initialize(MetaHumanLook.Capture(State, null));
        }

        private void OnHistoryChanged()
        {
            // 步骤数/游标变化的通知由调用方通过 OnSolutionChanged 之外的 History.OnChanged 自行订阅。
        }

        /// <summary>
        /// 封存即定格：发布之后任何编辑入口都不许改动当前状态。
        /// 只挡"记不记步"是不够的（真机实测：历史不记步、值却照改），
        /// 那会让本机这份与已发布的那份脱钩，下次上传必被服务端拒。
        /// </summary>
        private bool RejectEdit
        {
            get { return History.IsSealed; }
        }

        /// <summary>实时预览一次改动，不记步。已封存时原样返回当前值，不碰状态。</summary>
        public float Adjust(string paramId, float value)
        {
            if (RejectEdit)
            {
                return State.Get(paramId);
            }

            float applied = State.Set(paramId, value);
            Rebuild();
            return applied;
        }

        /// <summary>把当前状态记为一步。已封存时返回 false。</summary>
        public bool Commit(string label)
        {
            MetaHumanLook look = MetaHumanLook.Capture(State, _activePresetId);
            look.Revision = _revision + 1;
            if (!History.Commit(look, label))
            {
                return false;
            }

            // 只有真记进历史才推进修订号：封存被拒的提交不能留下"更新"的假象。
            _revision = look.Revision;
            return true;
        }

        /// <summary>改一个数值并立刻记一步（非滑杆类控件用这个，例如开关或点选）。</summary>
        public float AdjustAndCommit(string paramId, float value, string label)
        {
            float applied = Adjust(paramId, value);
            Commit(label);
            return applied;
        }

        /// <summary>逐轴模式变化会影响镜像写入的结果，因此也记一步。</summary>
        public bool SetLinkSymmetric(bool linked, string label)
        {
            if (State.LinkSymmetric == linked)
            {
                return true;
            }

            if (RejectEdit)
            {
                return false;
            }

            State.LinkSymmetric = linked;
            return Commit(label);
        }

        public bool ApplyPreset(string presetId, string label)
        {
            if (RejectEdit)
            {
                return false;
            }

            MetaHumanPreset preset = Template.FindPreset(presetId);
            if (preset == null)
            {
                return false;
            }

            _activePresetId = presetId;
            State.ApplyPreset(preset);
            _pendingFull = true;
            Rebuild();
            return Commit(label ?? ("预设 " + presetId));
        }

        /// <summary>两个预设混合成一个新状态并记步（对应 UE 的 BlendPresets）。</summary>
        public bool BlendPresets(string presetA, string presetB, float t, string label)
        {
            if (RejectEdit)
            {
                return false;
            }

            MetaHumanPreset a = Template.FindPreset(presetA);
            MetaHumanPreset b = Template.FindPreset(presetB);
            if (a == null || b == null)
            {
                return false;
            }

            State.BlendPresets(a, b, t);
            _pendingFull = true;
            Rebuild();
            return Commit(label ?? ("混合 " + presetA + "/" + presetB));
        }

        public bool Randomize(int seed, string label)
        {
            if (RejectEdit)
            {
                return false;
            }

            State.Randomize(seed);
            _pendingFull = true;
            Rebuild();
            return Commit(label ?? ("随机 " + seed));
        }

        /// <summary>
        /// 一键重置回到基线并记为一步（重置本身也可回溯）。已封存时不改状态、返回 false。
        /// <para>
        /// 基线 = 全默认 + **本次会话选中的那个面容预设**（<c>_activePresetId</c>）：玩家是从「面容预设」
        /// 页带着一张脸进来微调的，只归零不回预设等于把上一步选的脸弄丢了
        /// （2026-10-19 实测「重置后回不到预设」）。没选过预设时 `_activePresetId` 为空 ⇒ 就是中性脸。
        /// </para>
        /// </summary>
        public bool ResetToBaseline(string label)
        {
            // 这也是"会改状态的入口"：漏挡这一次，发布后点一下重置就把脸上全部改动抹回默认，
            // 而历史又确实不记步 ⇒ 本机那份与已发布那份彻底脱钩，下次上传必被服务端拒。
            if (RejectEdit)
            {
                return false;
            }

            State.Reset();

            if (!string.IsNullOrEmpty(_activePresetId))
            {
                MetaHumanPreset baseline = Template.FindPreset(_activePresetId);
                if (baseline != null)
                {
                    State.ApplyPreset(baseline);
                }
            }

            _pendingFull = true;
            Rebuild();
            return Commit(label ?? "重置");
        }

        public MetaHumanLook Undo()
        {
            MetaHumanLook look = History.Undo();
            if (look != null)
            {
                Restore(look);
            }

            return look;
        }

        public MetaHumanLook Redo()
        {
            MetaHumanLook look = History.Redo();
            if (look != null)
            {
                Restore(look);
            }

            return look;
        }

        /// <summary>回溯到任意步骤。</summary>
        public MetaHumanLook JumpTo(int index)
        {
            MetaHumanLook look = History.JumpTo(index);
            if (look != null)
            {
                Restore(look);
            }

            return look;
        }

        private void Restore(MetaHumanLook look)
        {
            _activePresetId = look.SourcePresetId;
            look.RestoreInto(State);
            _pendingFull = true;
            Rebuild();
        }

        /// <summary>保存：产出一个可写盘/可上传的快照，不结束会话。</summary>
        public MetaHumanLook Save()
        {
            Commit("保存");
            MetaHumanLook look = MetaHumanLook.Capture(State, _activePresetId);
            look.Revision = _revision;
            // 已封存的会话不能再"保存"出一份未发布的快照：
            // 封存位是随快照走的（LoadLook 按它恢复 Sealed），漏带就等于把已发布角色解了封。
            // 面板的「保存」在发布后依然可点（历史记不上，但快照照样落盘），所以这一格必须由写方自己兜住。
            if (History.IsSealed)
            {
                look.Published = true;
            }

            return look;
        }

        /// <summary>
        /// 发布：提交最终状态并封存历史。返回的快照带 IsSealed 之后的语义——
        /// 此后任何 Commit 都返回 false，回溯通道关闭。
        /// </summary>
        public MetaHumanLook Publish()
        {
            // 与当前步不一致才补记一步：Publish 不该凭空多出一个「发布」步骤，
            // 否则步数会在封存瞬间增长，回溯条目的编号也就和玩家看到的不符。
            if (!SameAsCurrentStep())
            {
                Commit("发布");
            }
            MetaHumanLook final = MetaHumanLook.Capture(State, _activePresetId);
            final.Published = true;
            final.Revision = _revision + 1;
            _revision = final.Revision;
            History.Seal();

            Action handler = OnSealed;
            if (handler != null)
            {
                handler();
            }

            return final;
        }

        /// <summary>从外部快照恢复整个会话（读档/其它端同步过来）。</summary>
        public void LoadLook(MetaHumanLook look)
        {
            if (look == null)
            {
                return;
            }

            List<string> problems = look.ValidateAgainst(Template);
            if (problems.Count > 0)
            {
                throw new ArgumentException("外观快照与模板不匹配：" + string.Join(" | ", problems.ToArray()));
            }

            Restore(look);
            History.Initialize(look);

            // 修订号只增不减：从盘上/别的端读回来的档可能是更新的，下一次保存必须超过它。
            if (look.Revision > _revision)
            {
                _revision = look.Revision;
            }
        }

        private void Rebuild()
        {
            bool full = _pendingFull || Solution.IsEmpty;
            if (full)
            {
                Solution = MetaHumanSolver.Solve(State);
                _pendingFull = false;
            }
            else
            {
                MetaHumanSolver.UpdateIncremental(State, Solution);
            }

            Action<MetaHumanSolution, bool> handler = OnSolutionChanged;
            if (handler != null)
            {
                handler(Solution, full);
            }
        }

        private bool SameAsCurrentStep()
        {
            MetaHumanLook current = History.Current;
            if (current == null)
            {
                return false;
            }

            if (current.Values.Count != State.ModifiedCount)
            {
                return false;
            }

            foreach (KeyValuePair<string, float> kv in State.ModifiedValues())
            {
                float stored;
                if (!current.Values.TryGetValue(kv.Key, out stored) || Math.Abs(stored - kv.Value) > MetaHumanParameter.Epsilon)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 诊断用：全量重解与当前解的差异，写回端可用它在 PIE 里自证增量没算错。
        /// **五个通道都要比**：增量通路写的是 morph / 骨骼 / 材质浮点 / 材质颜色 / 材质贴图，
        /// 只比 morph 时其余四档的残留与漏算永远查不出来（贴图那档还是"离散赋值 + 减档跳过"的特殊写法）。
        /// </summary>
        public List<string> AuditIncremental()
        {
            MetaHumanSolution reference = MetaHumanSolver.Solve(State);
            List<string> problems = new List<string>();

            foreach (KeyValuePair<string, float> kv in reference.MorphWeights)
            {
                float local;
                Solution.MorphWeights.TryGetValue(kv.Key, out local);
                if (Math.Abs(local - kv.Value) > 1e-4f)
                {
                    problems.Add("morph " + kv.Key + " 增量=" + local + " 全量=" + kv.Value);
                }
            }

            foreach (KeyValuePair<string, float> kv in Solution.MorphWeights)
            {
                if (!reference.MorphWeights.ContainsKey(kv.Key))
                {
                    problems.Add("morph " + kv.Key + " 只存在于增量解（残留）");
                }
            }

            foreach (KeyValuePair<string, MetaHumanBoneOp> kv in reference.Bones)
            {
                MetaHumanBoneOp local;
                if (!Solution.Bones.TryGetValue(kv.Key, out local))
                {
                    problems.Add("骨骼 " + kv.Key + " 只在全量解里（增量漏算）");
                    continue;
                }

                if (Math.Abs(local.Translate.X - kv.Value.Translate.X) > 1e-3f
                    || Math.Abs(local.Translate.Y - kv.Value.Translate.Y) > 1e-3f
                    || Math.Abs(local.Translate.Z - kv.Value.Translate.Z) > 1e-3f
                    || Math.Abs(local.ScaleDelta.X - kv.Value.ScaleDelta.X) > 1e-4f
                    || Math.Abs(local.ScaleDelta.Y - kv.Value.ScaleDelta.Y) > 1e-4f
                    || Math.Abs(local.ScaleDelta.Z - kv.Value.ScaleDelta.Z) > 1e-4f)
                {
                    problems.Add("骨骼 " + kv.Key + " 增量与全量不一致");
                }
            }

            foreach (KeyValuePair<string, MetaHumanBoneOp> kv in Solution.Bones)
            {
                if (!reference.Bones.ContainsKey(kv.Key))
                {
                    problems.Add("骨骼 " + kv.Key + " 只存在于增量解（残留）");
                }
            }

            foreach (KeyValuePair<string, float> kv in reference.MaterialFloats)
            {
                float local;
                Solution.MaterialFloats.TryGetValue(kv.Key, out local);
                if (Math.Abs(local - kv.Value) > 1e-4f)
                {
                    problems.Add("材质浮点 " + kv.Key + " 增量=" + local + " 全量=" + kv.Value);
                }
            }

            foreach (KeyValuePair<string, float> kv in Solution.MaterialFloats)
            {
                if (!reference.MaterialFloats.ContainsKey(kv.Key))
                {
                    problems.Add("材质浮点 " + kv.Key + " 只存在于增量解（残留）");
                }
            }

            foreach (KeyValuePair<string, MetaHumanColor> kv in reference.MaterialColors)
            {
                MetaHumanColor local;
                Solution.MaterialColors.TryGetValue(kv.Key, out local);
                if (Math.Abs(local.R - kv.Value.R) > 1e-4f || Math.Abs(local.G - kv.Value.G) > 1e-4f
                    || Math.Abs(local.B - kv.Value.B) > 1e-4f)
                {
                    problems.Add("材质颜色 " + kv.Key + " 增量与全量不一致");
                }
            }

            foreach (KeyValuePair<string, MetaHumanColor> kv in Solution.MaterialColors)
            {
                if (!reference.MaterialColors.ContainsKey(kv.Key))
                {
                    problems.Add("材质颜色 " + kv.Key + " 只存在于增量解（残留）");
                }
            }

            foreach (KeyValuePair<string, string> kv in reference.MaterialTextures)
            {
                string local;
                Solution.MaterialTextures.TryGetValue(kv.Key, out local);
                if (!string.Equals(local, kv.Value, StringComparison.Ordinal))
                {
                    problems.Add("材质贴图 " + kv.Key + " 增量=" + local + " 全量=" + kv.Value);
                }
            }

            foreach (KeyValuePair<string, string> kv in Solution.MaterialTextures)
            {
                if (!reference.MaterialTextures.ContainsKey(kv.Key))
                {
                    problems.Add("材质贴图 " + kv.Key + " 只存在于增量解（残留）");
                }
            }

            foreach (KeyValuePair<string, int> kv in reference.HairStyles)
            {
                int local;
                Solution.HairStyles.TryGetValue(kv.Key, out local);
                if (local != kv.Value)
                {
                    problems.Add("发型 " + kv.Key + " 增量=" + local + " 全量=" + kv.Value);
                }
            }

            foreach (KeyValuePair<string, int> kv in Solution.HairStyles)
            {
                if (!reference.HairStyles.ContainsKey(kv.Key))
                {
                    problems.Add("发型 " + kv.Key + " 只存在于增量解（残留）");
                }
            }

            return problems;
        }
    }
}
