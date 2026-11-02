using System;
using System.Collections.Generic;
using FlaxEngine;
using MetaHuman;

namespace MetaHuman.Runtime
{
    /// <summary>
    /// 把解出来的效果写到一个 AnimatedModel 上：blendshape 权重、骨骼偏移、材质参数。
    ///
    /// 三条来自引擎事实的约束（不是猜测，都在 Flax 1.12 源码里核过）：
    /// 1. SetBlendShapeWeight 是 CPU 变形，权重跨帧保持，无需每帧重刷；
    /// 2. SetNodeTransformation 写的是当帧姿势，动画一评估就被覆盖，
    ///    所以骨骼偏移必须挂在 AnimationUpdated 之后每帧重放；
    /// 3. 骨骼非均匀缩放是否被蒙皮着色器如实 honored 未证实，默认关闭该通道，
    ///    需要时显式打开并在 PIE 里目视确认。
    /// </summary>
    public sealed class MetaHumanAppearanceApplier
    {
        private readonly AnimatedModel _model;
        private readonly HashSet<string> _availableMorphs;
        private readonly Dictionary<string, int> _nodeCache;

        // 逐骨的"底稿"与"成品"快照，撤销不再用逆矩阵。
        // 2026-10-23 实测（MH_ScaleTest 夹具）：body.height 177.5→220→177.5、body.waist 同理，
        // 骨架不回落，形变永久留在身上。旧实现按"整姿逐位相等"判断姿势是否还是自己写的，
        // 引擎每帧重算无关节点的矩阵就会让它恒为 false ⇒ 旧位移/缩放从不撤销；而读回的姿势里
        // 其实还带着我们写的值。现在只比对**自己写过的骨**并给浮点容差：
        // 当前值≈上次成品 ⇒ 底稿就是撤销目标；不≈ ⇒ 引擎已重置，当前值就是新的底稿。
        private readonly Dictionary<string, Matrix> _basePose = new Dictionary<string, Matrix>(StringComparer.Ordinal);
        private readonly Dictionary<string, Matrix> _writtenPoseBone = new Dictionary<string, Matrix>(StringComparer.Ordinal);
        /// <summary>最近一次写入的位移目标值。**只用于自证/报表**（BoneProbe、OffsetSum），不参与撤销。</summary>
        private readonly Dictionary<string, MetaHumanVec3> _appliedTranslate = new Dictionary<string, MetaHumanVec3>(StringComparer.Ordinal);
        private readonly HashSet<string> _appliedMorphs;

        /// <summary>
        /// 上一次**真正写进引擎**的 morph 权重。引擎没有批量设权重的 API，而每次
        /// <c>SetBlendShapeWeight</c> 都要 <c>WaitForLoaded()</c> + 线性扫一遍已激活权重表 +
        /// 可能增删 deformer（AnimatedModel.cpp:460），所以一轮里把没变化的通道再写一遍是纯浪费。
        /// </summary>
        private readonly Dictionary<string, float> _writtenMorphs = new Dictionary<string, float>();

        private readonly Dictionary<string, float> _shapeMorphs = new Dictionary<string, float>();
        private readonly Dictionary<string, MetaHumanBoneOp> _shapeBones = new Dictionary<string, MetaHumanBoneOp>();
        private readonly Dictionary<string, float> _expressionMorphs = new Dictionary<string, float>();
        private readonly Dictionary<string, MetaHumanVec3> _expressionOffsets = new Dictionary<string, MetaHumanVec3>();

        /// <summary>表情骨骼旋转：骨名 → 模型空间旋转 + 支点骨。与位移分开存，合成时并进同一个 BoneOp。</summary>
        private readonly Dictionary<string, MetaHumanBoneRotation> _expressionRotations =
            new Dictionary<string, MetaHumanBoneRotation>();



        private MetaHumanSolution _composed;

        /// <summary>每次 ComposeAndWrite 自增；用来判断"这一帧要不要重放"。</summary>
        private int _composedVersion;

        /// <summary>上一次真正写进引擎时对应的 <see cref="_composedVersion"/>。</summary>
        private int _writtenVersion = -1;

        /// <summary>读姿势失败后的静默帧数：原生对象没了时每帧重试只会每帧抛一次空引用。</summary>
        private int _poseReadSkipFrames;

        /// <summary>读姿势失败是否已经报过（只报第一次，日志里留一条可归因的凭据）。</summary>
        private bool _poseReadFailureLogged;

        /// <summary>卸载（Clear）撞到已销毁模型时是否报过（同样只报第一次）。</summary>
        private bool _clearFailureLogged;

        private MaterialBase[] _materials;
        private bool _hooked;

        /// <summary>
        /// 面部骨（<c>FACIAL_*</c>）的位移增益。模板里每个语义参数对每根骨的位移量是 0.02/0.06cm，
        /// 不放大时肉眼不可辨。实测：增益 20 时眼 747 / 鼻 296 / 下颌 168 像素差，脸颊/额头约 48；
        /// 增益 40 时眼区出现明显拉丝、而脸颊/额头仍只有约 47/56（瓶颈是权重覆盖，不是增益）；
        /// 增益 60 眼球被拉成白条。故定在 20。身体骨（spine/thigh…）**不放大**，再乘会炸。
        /// </summary>
        public static float FaceBoneGain = 20f;

        /// <summary>骨骼缩放通道：未证实引擎如实 honored，默认关。</summary>
        public bool EnableBoneScaleChannel { get; set; }

        public int AppliedMorphCount { get; private set; }

        public int SkippedMorphCount { get; private set; }

        /// <summary>
        /// 经 morph→GUI 表翻译后才落到资产形状上的条数。
        /// 它与 SkippedMorphCount 分开计数：真头到位后这个数 >0 才说明"表在承重"，
        /// 只有 AppliedMorphCount 会分不清是同名直查命中还是翻译命中。
        /// </summary>
        public int BridgedMorphCount { get; private set; }

        /// <summary>本轮改过并准备整姿写回的骨骼根数。</summary>
        public int BoneWritesLastPass { get; private set; }

        /// <summary>本轮调用整姿写回的次数（批量之后只可能是 0 或 1）。</summary>
        public int PoseWritesLastPass { get; private set; }

        /// <summary>与上次实写相同而被跳过的 morph 写入次数（拖动时多数通道并没变）。</summary>
        public int MorphWritesSkippedUnchanged { get; private set; }

        /// <summary>每帧重放里"没人动过姿势、组合也没变"而整段跳过的次数（省的是全量骨骼求值）。</summary>
        public int BoneReplaysSkippedUnchanged { get; private set; }


        /// <summary>
        /// 每帧动画事件那条路上实际写了几次整姿（累计）。与 <see cref="PoseWritesLastPass"/> 分开记：
        /// 前者是"稳态每帧的固定开销"，后者是"一次 Adjust 干了多少活"，混在一个数里就没法分别归因。
        /// </summary>
        public int HookPoseWrites { get; private set; }

        private int _skippedMaterialTargets;

        /// <summary>因 SkinnedModel 为空而被挡在引擎断言之外的 morph 写入次数（必须可见，不静默）。</summary>
        public int MorphWritesBlockedNoModel { get; private set; }

        private bool _warnedNoSkinnedModel;

        /// <summary>被跳过的非实例材质目标数（写基础材质会污染共享资产，所以宁可不写）。</summary>
        public int SkippedMaterialTargets
        {
            get { return _skippedMaterialTargets; }
        }

        public int SkippedBoneCount { get; private set; }

        /// <summary>最近一次重放里因节点名解析不到而跳过的骨名（最多 16 个）：区分绑定缺失与权重太小。</summary>
        public readonly System.Collections.Generic.List<string> SkippedBoneNamesLastPass =
            new System.Collections.Generic.List<string>();

        /// <summary>这具模型是"头"（有形状通道）⇒ 只接受 FACIAL_* 骨骼位移，忽略身体骨。</summary>
        private bool _faceOnly;

        /// <summary>真正参与蒙皮的节点索引（SkeletonBone.NodeIndex）。写非蒙皮节点等于空操作。</summary>
        private HashSet<int> _skinnedNodes;

        /// <summary>装载期的一次性诊断（名字对不上资产），必须被看见而不是静默跳过。</summary>
        public readonly List<string> Warnings = new List<string>();

        public MetaHumanAppearanceApplier(AnimatedModel model)
        {
            if (model == null)
            {
                throw new ArgumentNullException("model");
            }

            _model = model;
            _availableMorphs = new HashSet<string>(StringComparer.Ordinal);
            _nodeCache = new Dictionary<string, int>(StringComparer.Ordinal);
            _appliedMorphs = new HashSet<string>(StringComparer.Ordinal);
            RefreshAssetCache();
        }

        /// <summary>换了模型资产后重建可用名录。</summary>
        public void RefreshAssetCache()
        {
            _availableMorphs.Clear();
            _nodeCache.Clear();
            // 换资产后节点索引与底稿全部作废：旧底稿写到新骨架上就是错位变形。
            _basePose.Clear();
            _writtenPoseBone.Clear();
            _appliedTranslate.Clear();

            SkinnedModel asset = _model.SkinnedModel;
            if (asset == null)
            {
                Warnings.Add("AnimatedModel 的 Model 为空，形状通道不会生效");
                return;
            }

            string[] blendShapes = asset.BlendShapes;
            if (blendShapes != null)
            {
                for (int i = 0; i < blendShapes.Length; i++)
                {
                    _availableMorphs.Add(blendShapes[i]);
                }
            }

            // 有形状通道的资产是"头"：它的骨架里也有 spine/pelvis 等同名节点，
            // 若不筛掉身体骨位移，身体页签调过的身高会被重放到头上（实测把颈肩撕成尖刺）。
            // 路径兜底：将来若有一具"没有形状通道的脸"，名字里带 Face 也按头处理。
            _faceOnly = (blendShapes != null && blendShapes.Length > 0)
                        || (!string.IsNullOrEmpty(asset.Path)
                            && asset.Path.IndexOf("Face", StringComparison.OrdinalIgnoreCase) >= 0);

            // 蒙皮关节集合：同名节点里优先选真正参与蒙皮的那个。glTF 的骨架常常夹带
            // control/IK/辅助节点，它们与骨骼同名时 FindNode 取到谁全看导入顺序。
            var bones = asset.Bones;
            _skinnedNodes = new HashSet<int>();
            if (bones != null)
            {
                for (int i = 0; i < bones.Length; i++)
                {
                    _skinnedNodes.Add(bones[i].NodeIndex);
                }
            }

            var lods = asset.LODs;
            _lodCount = lods == null ? 1 : Math.Max(1, lods.Length);
        }

        public void SetMaterials(MaterialBase[] materials)
        {
            _materials = materials;
        }

        public bool HasMorph(string name)
        {
            return _availableMorphs.Contains(name);
        }

        /// <summary>资产里的 LOD 档数（LOD 策略按它封顶）。</summary>
        public int AvailableLodCount
        {
            get { return _lodCount; }
        }

        /// <summary>
        /// 设定允许的最粗 LOD。Flax 的 blendshape 只在 LOD0 生效，
        /// 所以带形状定制的角色不能让它随意降档 —— 降了就是整张脸回到默认。
        /// </summary>
        public void SetMaxLod(int maxLod)
        {
            if (maxLod < 0)
            {
                maxLod = 0;
            }

            // ForcedLOD 是「强制到第几档」，不是上限；要的是不超过 maxLod，
            // 所以这里只在当前实际档位比 maxLod 更粗时把它按回去。
            _lodFloor = maxLod;
            if (_lodCount > 0 && maxLod >= _lodCount)
            {
                _lodFloor = _lodCount - 1;
            }

            // ForcedLOD 是「定到第几档」而不是上限，所以直接定到判定允许的那一档：
            // 需要保形状时是 0，其余情况把选择权交回引擎（-1 = 自动）。
            _model.ForcedLOD = _lodFloor < 0 ? -1 : _lodFloor;
        }

        private int _lodFloor = -1;

        /// <summary>有多少次骨骼查找是靠别名表对齐的（身体滑杆在 Humanoid 角色上生效就是靠它）。</summary>
        public int AliasedBoneCount;
        private int _lodCount;

        public int LodFloor
        {
            get { return _lodFloor; }
        }

        /// <summary>
        /// 写回目标是否还活着。Flax 在 <see cref="FlaxEngine.Object"/> 上重载了 <c>==</c>/隐式 <c>bool</c>
        /// 并按**原生指针**比较，所以 Actor 被销毁后这个引用就按"没有"算 —— 正是我们要的判据。
        /// </summary>
        public bool ModelAlive
        {
            get { return _model; }
        }

        public void Apply(MetaHumanSolution solution)
        {
            if (solution == null)
            {
                return;
            }

            if (!ModelAlive)
            {
                // 目标 Actor 已随页面销毁。继续往下写会在第一个骨骼读取上抛原生空引用
                // （AnimatedModel::GetNodeTransformation 自己有边界保护，抛 NRE 只可能是原生对象没了），
                // 而那个异常会把整条写回链一起打断 —— 2026-10-18 一次 PIE 里刷了 35 次，
                // 症状正是"数值改了、脸不动、界面读数也不更新"。这里改成一次可见的告警。
                if (Warnings.Count < 32 && !Warnings.Contains("写回目标已销毁，形状与骨骼无处可写"))
                {
                    Warnings.Add("写回目标已销毁，形状与骨骼无处可写");
                }

                return;
            }

            _shapeMorphs.Clear();
            foreach (KeyValuePair<string, float> kv in solution.MorphWeights)
            {
                _shapeMorphs[kv.Key] = kv.Value;
            }

            _shapeBones.Clear();
            foreach (KeyValuePair<string, MetaHumanBoneOp> kv in solution.Bones)
            {
                _shapeBones[kv.Key] = kv.Value;
            }

            ApplyMaterialParameters(solution);
            ComposeAndWrite();
        }

        /// <summary>
        /// 表情通道写入（动画/LiveLink 的解析结果）。
        /// 形状与表情会写同一个 blendshape 与同一根面部骨骼：两处各自直写就是「后写的静默盖掉前一个」，
        /// 所以表情只写到这里，最终值由 ComposeAndWrite 相加后一次落地。
        /// </summary>
        public void SetExpression(Dictionary<string, float> morphWeights, Dictionary<string, MetaHumanVec3> jointOffsets)
        {
            SetExpression(morphWeights, jointOffsets, null);
        }

        public void SetExpression(Dictionary<string, float> morphWeights, Dictionary<string, MetaHumanVec3> jointOffsets,
            Dictionary<string, MetaHumanBoneRotation> jointRotations)
        {
            _expressionMorphs.Clear();
            if (morphWeights != null)
            {
                foreach (KeyValuePair<string, float> kv in morphWeights)
                {
                    _expressionMorphs[kv.Key] = kv.Value;
                }
            }

            _expressionOffsets.Clear();
            if (jointOffsets != null)
            {
                foreach (KeyValuePair<string, MetaHumanVec3> kv in jointOffsets)
                {
                    _expressionOffsets[kv.Key] = kv.Value;
                }
            }

            _expressionRotations.Clear();
            if (jointRotations != null)
            {
                foreach (KeyValuePair<string, MetaHumanBoneRotation> kv in jointRotations)
                {
                    _expressionRotations[kv.Key] = kv.Value;
                }
            }

            ComposeAndWrite();
        }

        /// <summary>
        /// 形状写入开关。关掉时合成只输出表情通道；
        /// 先前写过的形状 morph 会被「本轮不再存在就归零」那段自动清掉，不会留半张脸。
        /// </summary>
        public bool ShapeWritingEnabled
        {
            get { return _shapeEnabled; }
            set
            {
                if (_shapeEnabled == value)
                {
                    return;
                }

                _shapeEnabled = value;
                ComposeAndWrite();
            }
        }

        private bool _shapeEnabled = true;

        private void ComposeAndWrite()
        {
            // 本轮计数在这里清零，而不是在 ReplayBoneOffsets 里：被"内容没变"提前 return 的 applier
            // 根本走不到重放那一步，若不清零就会把上一轮的 1 一直报成"本轮也写了一次"
            // （2026-10-19 实测 poseWrites=2 而其中一具其实被跳过了 —— 归因坏了，不是行为坏了）。
            BoneWritesLastPass = 0;
            PoseWritesLastPass = 0;

            MetaHumanSolution composed = new MetaHumanSolution();

            if (_shapeEnabled)
            {
                foreach (KeyValuePair<string, float> kv in _shapeMorphs)
                {
                    composed.MorphWeights[kv.Key] = kv.Value;
                }
            }

            foreach (KeyValuePair<string, float> kv in _expressionMorphs)
            {
                float existing;
                composed.MorphWeights.TryGetValue(kv.Key, out existing);
                composed.MorphWeights[kv.Key] = existing + kv.Value;
            }

            MetaHumanSolver.ClampMorphs(composed);

            if (_shapeEnabled)
            {
                foreach (KeyValuePair<string, MetaHumanBoneOp> kv in _shapeBones)
                {
                    if (_faceOnly && !kv.Key.StartsWith("FACIAL_", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    composed.Bones[kv.Key] = kv.Value;
                }
            }

            foreach (KeyValuePair<string, MetaHumanVec3> kv in _expressionOffsets)
            {
                if (_faceOnly && !kv.Key.StartsWith("FACIAL_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                MetaHumanBoneOp op;
                if (!composed.Bones.TryGetValue(kv.Key, out op))
                {
                    op = new MetaHumanBoneOp();
                }
                else
                {
                    // 克隆：composed.Bones 里可能是解算器复用的对象，就地 Add 会让表情量逐帧累加
                    op = op.Clone();
                }

                composed.Bones[kv.Key] = op;
                op.Translate.Add(kv.Value);
            }

            foreach (KeyValuePair<string, MetaHumanBoneRotation> kv in _expressionRotations)
            {
                if (_faceOnly && !kv.Key.StartsWith("FACIAL_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (kv.Value.Rotation.IsIdentity || string.IsNullOrEmpty(kv.Value.PivotBone))
                {
                    continue;
                }

                MetaHumanBoneOp op;
                if (!composed.Bones.TryGetValue(kv.Key, out op))
                {
                    op = new MetaHumanBoneOp();
                }
                else
                {
                    op = op.Clone();
                }

                composed.Bones[kv.Key] = op;

                // 同一根骨被多条表情同时写到时按四元数叠乘（角度相加的通用形式）。
                op.Rotation = MetaHumanQuat.Multiply(op.Rotation, kv.Value.Rotation);
                op.PivotBone = kv.Value.PivotBone;
            }

            // 这里**不加**"内容与上一轮相同就跳过"的短路：2026-10-19 试过，签名逐条比数值看似稳妥，
            // 实测却把唯一该写的头部 applier 每个事件都判成"没变"（compSkip 每事件 +1、bones=0），
            // 脸只是靠每帧的 AnimationUpdated 重放才跟上，而「重置」直接不回模型。
            // 原因是 composed.Bones 与解算器持有的 MetaHumanBoneOp 是同一批对象（见上面的 op.Translate.Add），
            // 签名读到的是"已经被就地改过的值"，判据本身不成立。要省这一遍写，得先让解算结果可比较。
            _composed = composed;
            _composedVersion++;

            // 先清掉上一轮写过、这一轮不再存在的 morph：引擎不会自己归零，
            // 残留权重会让「回溯到早期步骤」看起来没生效。
            foreach (string name in _appliedMorphs)
            {
                if (!composed.MorphWeights.ContainsKey(name))
                {
                    WriteMorph(name, 0f);
                }
            }

            _appliedMorphs.Clear();

            AppliedMorphCount = 0;
            SkippedMorphCount = 0;
            BridgedMorphCount = 0;
            MorphWritesSkippedUnchanged = 0;
            foreach (KeyValuePair<string, float> kv in composed.MorphWeights)
            {
                string target = kv.Key;
                if (!_availableMorphs.Contains(target))
                {
                    // 模板给的形状名与资产里的 blendshape 名不同源（.dna 族与 UE 网格族同名命中 0/37），
                    // 所以先按两张 morph→GUI 表翻译一次；两张都翻不出来才记 skipped，
                    // 绝不拿"看着像"的名字写权重——那会把错形变画进脸里还全绿。
                    string resolved = MetaHumanMorphBridge.ResolveAssetMorph(
                        target, _availableMorphs,
                        MetaHuman.Core.MetaHumanTemplateStore.MorphGui,
                        MetaHuman.Core.MetaHumanTemplateStore.UeMorphGui,
                        MetaHuman.Core.MetaHumanTemplateStore.WarpShapes);
                    if (resolved == null || !_availableMorphs.Contains(resolved))
                    {
                        SkippedMorphCount++;
                        if (Warnings.Count < 32 && !Warnings.Contains("morph 缺失:" + kv.Key))
                        {
                            Warnings.Add("morph 缺失:" + kv.Key);
                        }

                        continue;
                    }

                    target = resolved;
                    BridgedMorphCount++;
                    if (Warnings.Count < 32 && !Warnings.Contains("morph 经表翻译:" + kv.Key + " -> " + resolved))
                    {
                        Warnings.Add("morph 经表翻译:" + kv.Key + " -> " + resolved);
                    }
                }

                WriteMorph(target, kv.Value);
                _appliedMorphs.Add(target);
                AppliedMorphCount++;
            }

            HookAnimationUpdated(composed.Bones.Count > 0);
            ReplayBoneOffsets(true);
        }

        /// <summary>表情通道的骨骼偏移计数，供诊断报告用。</summary>
        public int ExpressionJointCount
        {
            get { return _expressionOffsets.Count; }
        }

        public int ExpressionMorphCount
        {
            get { return _expressionMorphs.Count; }
        }

        private void ApplyMaterialParameters(MetaHumanSolution solution)
        {
            if (_materials == null || _materials.Length == 0)
            {
                if (solution.MaterialFloats.Count + solution.MaterialColors.Count > 0 && Warnings.Count < 32)
                {
                    Warnings.Add("有 " + (solution.MaterialFloats.Count + solution.MaterialColors.Count)
                        + " 个材质参数待写，但没有分配材质实例（外观的肤色/发色通道不会生效）");
                }

                return;
            }

            // 只写材质实例：`Material`（基础资产）上的 SetParameterValue 会改到共享资产本身，
            // 一个玩家调肤色、全场所有角色跟着变，而且这改动还会被存回资产文件。
            var instances = new List<MaterialInstance>();
            for (int i = 0; i < _materials.Length; i++)
            {
                MaterialInstance instance = _materials[i] as MaterialInstance;
                if (instance != null)
                {
                    instances.Add(instance);
                }
            }

            int pendingParameters = solution.MaterialFloats.Count + solution.MaterialColors.Count
                + solution.MaterialTextures.Count;

            if (instances.Count == 0)
            {
                _skippedMaterialTargets = _materials.Length;
                if (pendingParameters > 0 && Warnings.Count < 32)
                {
                    Warnings.Add("材质写入目标里没有一个实例（基础材质不能改，会污染共享资产）："
                        + pendingParameters + " 个参数未生效");
                }

                return;
            }

            _skippedMaterialTargets = _materials.Length - instances.Count;

            foreach (KeyValuePair<string, float> kv in solution.MaterialFloats)
            {
                // 瞳色在 UE 里是 0..1 的离散色板指数，Flax 侧落成颜色乘色（EyeIrisTint）。
                // 色板必须与创角色板一致：CcCreationPages.cs 的 EyeColors。
                // 发色不走这里：头发是独立 StaticModel，不在这批实例里；它由 AppearanceInfo.HairColor 在建模时着色。
                if (kv.Key == "Eye_IrisColor")
                {
                    Color iris = Palettize(kv.Value, _irisPalette);
                    for (int i = 0; i < instances.Count; i++)
                    {
                        instances[i].SetParameterValue("EyeIrisTint", iris, false);
                    }

                    continue;
                }

                string parameterName;
                float parameterValue;
                MapMaterialFloat(kv.Key, kv.Value, out parameterName, out parameterValue);
                int typeMismatch = 0;
                for (int i = 0; i < instances.Count; i++)
                {
                    // 映射表可能把标量通道指到贴图参数上（区域掩码那一类）。引擎会丢掉这次写入，
                    // 但每个实例都刷一条 Error，通道本身照样没生效。这里先问参数声明的类型。
                    if (!IsScalarMaterialParameter(instances[i].GetParameter(parameterName)))
                    {
                        typeMismatch++;
                        continue;
                    }

                    instances[i].SetParameterValue(parameterName, parameterValue, false);
                }

                if (typeMismatch > 0 && Warnings.Count < 32)
                {
                    Warnings.Add("浮点通道 " + kv.Key + " 的落点 " + parameterName
                        + " 在材质里不是标量参数，" + typeMismatch + " 个实例未写入（该通道当前没有生效通路）");
                }
            }

            foreach (KeyValuePair<string, MetaHumanColor> kv in solution.MaterialColors)
            {
                Color color = new Color(kv.Value.R, kv.Value.G, kv.Value.B);
                for (int i = 0; i < instances.Count; i++)
                {
                    instances[i].SetParameterValue(kv.Key, color, false);
                }
            }

            // 贴图通道：UE 的 MetaHuman 头部肤色/痣/皱纹/妆是换贴图，不是改颜色。
            // 载不进来必须写进 Warnings —— "没换"和"换了"在画面上几乎无法分辨，静默吞掉就是死通道。
            int textureFailures = 0;
            foreach (KeyValuePair<string, string> kv in solution.MaterialTextures)
            {
                TextureBase texture = Content.Load<TextureBase>(kv.Value);
                if (texture == null)
                {
                    textureFailures++;
                    continue;
                }

                for (int i = 0; i < instances.Count; i++)
                {
                    instances[i].SetParameterValue(kv.Key, texture, false);
                }
            }

            if (textureFailures > 0 && Warnings.Count < 32)
            {
                Warnings.Add("有 " + textureFailures + " 张贴图载不进来，对应槽位保持原样");
            }
        }

        private void HookAnimationUpdated(bool need)
        {
            if (need && !_hooked)
            {
                _model.AnimationUpdated += ReplayFromAnimation;
                _hooked = true;
            }
            else if (!need && _hooked)
            {
                _model.AnimationUpdated -= ReplayFromAnimation;
                _hooked = false;
            }
        }

        private int NodeIndex(string boneName)
        {
            int index;
            if (_nodeCache.TryGetValue(boneName, out index))
            {
                return index;
            }

            index = ResolveNode(boneName);

            if (index < 0)
            {
                // 模板写的是 UE 骨骼名，而项目现成角色是 Unity Humanoid 命名
                // （身体模板 24 个目标同名命中 0/24，文档 §44）⇒ 按别名表再问一次。
                // 命中要计数并写进诊断：静默改名会让"这具资产本来就没这根骨头"
                // 和"我们靠别名救回来了"长成同一个样子。
                string[] aliases = MetaHumanBoneAliases.AliasesOf(boneName);
                for (int i = 0; i < aliases.Length; i++)
                {
                    int aliased = ResolveNode(aliases[i]);
                    if (aliased >= 0)
                    {
                        index = aliased;
                        AliasedBoneCount++;
                        if (Warnings.Count < 32 && !Warnings.Contains("骨骼经别名对齐:" + boneName + " -> " + aliases[i]))
                        {
                            Warnings.Add("骨骼经别名对齐:" + boneName + " -> " + aliases[i]);
                        }

                        break;
                    }
                }
            }

            _nodeCache[boneName] = index;
            return index;
        }

        /// <summary>
        /// 按名字解析节点索引，并处理三类映射隐患（都要留痕，不许静默选一个）：
        /// 重名节点优先取**蒙皮关节**；命中非蒙皮节点要告警；只有大小写不同才命中也要告警。
        /// </summary>
        private int ResolveNode(string name)
        {
            SkinnedModel asset = _model.SkinnedModel;
            if (asset == null || asset.Nodes == null || string.IsNullOrEmpty(name))
            {
                return -1;
            }

            var nodes = asset.Nodes;
            int exact = -1, exactSkinned = -1, exactCount = 0;
            int ci = -1, ciCount = 0;
            for (int i = 0; i < nodes.Length; i++)
            {
                string n = nodes[i].Name;
                if (string.IsNullOrEmpty(n))
                {
                    continue;
                }

                if (string.Equals(n, name, StringComparison.Ordinal))
                {
                    exactCount++;
                    if (exact < 0)
                    {
                        exact = i;
                    }
                    if (exactSkinned < 0 && _skinnedNodes != null && _skinnedNodes.Contains(i))
                    {
                        exactSkinned = i;
                    }
                }
                else if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                {
                    ciCount++;
                    if (ci < 0)
                    {
                        ci = i;
                    }
                }
            }

            if (exactCount > 0)
            {
                if (exactCount > 1 && Warnings.Count < 32)
                {
                    string w = "重名节点（" + exactCount + " 个）: " + name
                               + " ⇒ 取 " + (exactSkinned >= 0 ? "蒙皮关节 " : "第一个 ") + (exactSkinned >= 0 ? exactSkinned : exact);
                    if (!Warnings.Contains(w)) Warnings.Add(w);
                }

                if (exactSkinned >= 0)
                {
                    return exactSkinned;
                }

                if (_skinnedNodes != null && _skinnedNodes.Count > 0 && Warnings.Count < 32)
                {
                    string w = "节点不在蒙皮关节里（写入可能无效）: " + name + " -> node " + exact;
                    if (!Warnings.Contains(w)) Warnings.Add(w);
                }

                return exact;
            }

            if (ciCount == 1)
            {
                if (Warnings.Count < 32)
                {
                    string w = "节点名大小写不一致: " + name + " -> " + nodes[ci].Name;
                    if (!Warnings.Contains(w)) Warnings.Add(w);
                }

                return ci;
            }

            return -1;
        }

        /// <summary>
        /// 每帧重放骨骼偏移。做法是「读当前姿势 → 减掉上次加的 → 加这次要的」，
        /// 这样在没有动画驱动（姿势就是我们上次写的）时也不会逐帧累积漂移。
        /// </summary>
        /// <summary>
        /// 一行自证：这具模型本轮到底写没写、没写是因为哪一道闸门。
        /// 加它的理由：聚合计数把"两具模型"压成一个数之后，"该写的被跳过"和"跳过的那具本来就不用写"
        /// 在回执上长得一模一样（bones 从 373 变 0 时我无法分辨），只能逐具摊开。
        /// </summary>
        public string Diag()
        {
            string name = _model == null ? "<已销毁>" : _model.Name;
            return name + "{shape=" + (_shapeEnabled ? 1 : 0)
                + " bones=" + BoneWritesLastPass
                + " pose=" + PoseWritesLastPass
                + " replaySkip=" + BoneReplaysSkippedUnchanged
                + " hookW=" + HookPoseWrites
                + " morph=" + AppliedMorphCount
                + " morphSkipU=" + MorphWritesSkippedUnchanged
                + " noIndex=" + SkippedBoneCount
                + " composedBones=" + (_composed == null ? -1 : _composed.Bones.Count)
                // 真正写进去的骨骼偏移量之和（cm）：body.height 从低到高时它会跟着变，
                // 这才是不依赖渲染的"形体被改了"的数值证据。
                + " offSum=" + OffsetSum().ToString("F2")
                + " active=" + (_model != null && _model.IsActive ? 1 : 0)
                + " probeBone=" + BoneProbe()
                + " asset=" + (_model != null && _model.SkinnedModel != null
                    ? System.IO.Path.GetFileNameWithoutExtension(_model.SkinnedModel.Path) : "-") + "}";
        }

        /// <summary>
        /// 骨骼读回自证：取第一根"我们写过偏移"的面部骨，把它**当前**的局部变换读出来。
        /// 判据：写完之后读回应当带着我们加的位移；若读回是 0，说明姿势被别处覆盖或写到了别的实例。
        /// </summary>
        private string BoneProbe()
        {
            if (_model == null || _appliedTranslate.Count == 0)
            {
                return "-";
            }

            foreach (KeyValuePair<string, MetaHumanVec3> kv in _appliedTranslate)
            {
                int index = NodeIndex(kv.Key);
                if (index < 0)
                {
                    continue;
                }

                Matrix m;
                _model.GetNodeTransformation(index, out m, false);
                Vector3 t = m.TranslationVector;
                return string.Format("{0}:{1:F2},{2:F2},{3:F2}", kv.Key, t.X, t.Y, t.Z);
            }

            return "-";
        }

        /// <summary>把已写进骨架的骨骼偏移量（各轴绝对值之和）加起来 —— 不依赖渲染的"改到了没"证据。</summary>
        private float OffsetSum()
        {
            float sum = 0f;
            foreach (KeyValuePair<string, MetaHumanVec3> kv in _appliedTranslate)
            {
                float gain = kv.Key.StartsWith("FACIAL_", StringComparison.Ordinal) ? FaceBoneGain : 1f;
                sum += (Mathf.Abs(kv.Value.X) + Mathf.Abs(kv.Value.Y) + Mathf.Abs(kv.Value.Z)) * gain;
            }

            return sum;
        }

        /// <summary>引擎动画事件入口（每帧）：只重放，不计进"本轮 Adjust"的账。</summary>
        private void ReplayFromAnimation()
        {
            ReplayBoneOffsets(false);
        }

        private void RememberSkippedBone(string boneName)
        {
            if (SkippedBoneNamesLastPass.Count < 16 && !SkippedBoneNamesLastPass.Contains(boneName))
            {
                SkippedBoneNamesLastPass.Add(boneName);
            }
        }

        private void ReplayBoneOffsets(bool fromCompose)
        {
            if (_composed == null || _composed.Bones.Count == 0)
            {
                return;
            }

            // 目标原生对象已经销毁：包装还留着但任何一次原生调用都会抛空引用。
            if (!ModelAlive)
            {
                return;
            }

            if (_poseReadSkipFrames > 0)
            {
                _poseReadSkipFrames--;
                return;
            }

            // 一次读整姿、改完一次写回。逐根 SetNodeTransformation 每调用一次，引擎就完整跑一遍
            // OnAnimationUpdated（最终姿态 + skinning + socket + root motion，AnimatedModel.cpp:277），
            // 而一个头部参数往往带动十几根 FACIAL 关节 ⇒ 一次拖动 = 几十次全量骨骼求值；
            // 本函数还挂在 AnimationUpdated 上每帧来一遍，所以拖动时是"持续掉帧"而不只是"点一下慢"。
            // 批量版 SetCurrentPose 只在最后 OnAnimationUpdated 一次（AnimatedModel.cpp:218）。
            Matrix[] pose;
            try
            {
                _model.GetCurrentPose(out pose, false);
            }
            catch (Exception ex)
            {
                // 实测（2026-10-23）：页面重建/角色销毁后，残留的 applier 每帧调到这里，
                // 原生侧抛 NullReferenceException，几分钟就把日志刷到 2MB 上限。
                // 报一次带上模型名，之后每 120 帧才重试一次：模型真没了就安静，万一只是
                // 资产替换瞬间也能自己恢复。
                _poseReadSkipFrames = 120;
                if (!_poseReadFailureLogged)
                {
                    _poseReadFailureLogged = true;
                    Debug.LogWarning("[MetaHumanApplier] pose-read-failed: " + ex.GetType().Name
                        + " model=" + (_model ? _model.Name : "<destroyed>"));
                }

                return;
            }
            if (pose == null || pose.Length == 0)
            {
                return;
            }

            if (_writtenVersion == _composedVersion && WrittenBonesIntact(pose))
            {
                // 组合结果没变、而且我们写过的骨还是上次那份 ⇒ 跳过整段重放。
                // 只比"自己写过的骨"：引擎每帧会重算无关节点的矩阵，全局逐位比对会恒失配。
                BoneReplaysSkippedUnchanged++;
                return;
            }

            int written = 0;
            SkippedBoneNamesLastPass.Clear();

            // 已经从组合里消失、但引擎里可能还留着我们上次写的值（表情归零 / 参数回默认）：
            // 先按底稿还原。当前值不像上次成品 ⇒ 引擎已重置，直接丢记账。
            if (_writtenPoseBone.Count > 0)
            {
                List<string> stale = null;
                foreach (KeyValuePair<string, Matrix> kv in _writtenPoseBone)
                {
                    if (!_composed.Bones.ContainsKey(kv.Key))
                    {
                        (stale ??= new List<string>()).Add(kv.Key);
                    }
                }

                if (stale != null)
                {
                    for (int i = 0; i < stale.Count; i++)
                    {
                        string name = stale[i];
                        int index = NodeIndex(name);
                        Matrix lastWritten;
                        _writtenPoseBone.TryGetValue(name, out lastWritten);
                        if (index >= 0 && index < pose.Length && MatApproxEqual(ref pose[index], ref lastWritten))
                        {
                            pose[index] = _basePose[name];
                            written++;
                        }

                        _writtenPoseBone.Remove(name);
                        _basePose.Remove(name);
                        _appliedTranslate.Remove(name);
                    }
                }
            }

            foreach (KeyValuePair<string, MetaHumanBoneOp> kv in _composed.Bones)
            {
                string boneName = kv.Key;
                MetaHumanBoneOp op = kv.Value;
                int index = NodeIndex(boneName);
                if (index < 0)
                {
                    SkippedBoneCount++;
                    RememberSkippedBone(boneName);
                    continue;
                }

                if (index >= pose.Length)
                {
                    // 姿势数组比节点索引还短 = 模型刚换过/还没初始化完，写下去会越界（引擎那边是 CHECK 断言）。
                    SkippedBoneCount++;
                    RememberSkippedBone(boneName);
                    if (Warnings.Count < 32 && !Warnings.Contains("节点索引超出当前姿势数组，已跳过"))
                    {
                        Warnings.Add("节点索引超出当前姿势数组，已跳过");
                    }

                    continue;
                }

                Matrix current = pose[index];

                // 当前值还是我们上次写的吗？是 ⇒ 从底稿重建；否 ⇒ 引擎已重算，当前值就是新底稿。
                Matrix basePose = current;
                Matrix lastWrittenBone;
                Matrix storedBase;
                if (_writtenPoseBone.TryGetValue(boneName, out lastWrittenBone)
                    && MatApproxEqual(ref current, ref lastWrittenBone)
                    && _basePose.TryGetValue(boneName, out storedBase))
                {
                    basePose = storedBase;
                }

                // 全部按**绝对量**从底稿重建，不做"先撤旧值再加新值"的增量：
                // 增量撤销要求"姿势还是上次那份"的判据绝对可靠，实测不可靠（见字段注释）。
                // 右乘（Flax 行向量约定，平移在最后一行）：2026-10-13 实测左乘 ΔY≈0、右乘 ΔY=2.5。
                MetaHumanVec3 target = op.Translate;
                float gain = boneName.StartsWith("FACIAL_", StringComparison.Ordinal) ? FaceBoneGain : 1f;
                Matrix result = basePose * Matrix.Translation(
                    target.X * gain, target.Y * gain, target.Z * gain);

                if (EnableBoneScaleChannel)
                {
                    result = result * Matrix.Scaling(new Vector3(
                        1f + op.ScaleDelta.X,
                        1f + op.ScaleDelta.Y,
                        1f + op.ScaleDelta.Z));
                }

                if (!op.Rotation.IsIdentity && !string.IsNullOrEmpty(op.PivotBone))
                {
                    int pivotIndex = NodeIndex(op.PivotBone);
                    if (pivotIndex < 0 || pivotIndex >= pose.Length)
                    {
                        SkippedBoneCount++;
                        RememberSkippedBone(boneName);
                        if (Warnings.Count < 32 && !Warnings.Contains("表情旋转支点骨不可用：" + op.PivotBone))
                        {
                            Warnings.Add("表情旋转支点骨不可用：" + op.PivotBone);
                        }
                    }
                    else
                    {
                        // 模型空间绕支点转：p' = (p − P)·R + P，行向量约定写成
                        // Translation(−P) * Rotation * Translation(P)，右乘到当前位姿上。
                        Vector3 pivot = pose[pivotIndex].TranslationVector;
                        MetaHumanQuat q = op.Rotation;
                        Matrix aroundPivot = Matrix.Translation(-pivot)
                            * Matrix.RotationQuaternion(new Quaternion(q.X, q.Y, q.Z, q.W))
                            * Matrix.Translation(pivot);
                        result = result * aroundPivot;
                    }
                }

                pose[index] = result;
                _basePose[boneName] = basePose;
                _writtenPoseBone[boneName] = result;
                _appliedTranslate[boneName] = target;
                written++;
                if (fromCompose)
                {
                    BoneWritesLastPass++;
                }
            }

            if (written == 0)
            {
                // 一根都没改（索引全不可用）⇒ 不写整姿，免得白付一遍全量骨骼求值。
                return;
            }

            _model.SetCurrentPose(pose, false);
            _writtenVersion = _composedVersion;

            if (fromCompose)
            {
                PoseWritesLastPass++;
            }
            else
            {
                HookPoseWrites++;
            }
        }

        /// <summary>我们写过的每一根骨是否还保持上次写进去的值（只比这些骨，容差 1e-3）。</summary>
        private bool WrittenBonesIntact(Matrix[] pose)
        {
            foreach (KeyValuePair<string, Matrix> kv in _writtenPoseBone)
            {
                int index = NodeIndex(kv.Key);
                if (index < 0 || index >= pose.Length)
                {
                    return false;
                }

                Matrix last = kv.Value;
                if (!MatApproxEqual(ref pose[index], ref last))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>矩阵近似相等：引擎回读会对浮点做轻微重排，逐位相等会把"我们还留着"误判成"被重置"。</summary>
        private static bool MatApproxEqual(ref Matrix a, ref Matrix b)
        {
            const float eps = 1e-3f;
            return Mathf.Abs(a.M11 - b.M11) <= eps && Mathf.Abs(a.M12 - b.M12) <= eps
                && Mathf.Abs(a.M13 - b.M13) <= eps && Mathf.Abs(a.M14 - b.M14) <= eps
                && Mathf.Abs(a.M21 - b.M21) <= eps && Mathf.Abs(a.M22 - b.M22) <= eps
                && Mathf.Abs(a.M23 - b.M23) <= eps && Mathf.Abs(a.M24 - b.M24) <= eps
                && Mathf.Abs(a.M31 - b.M31) <= eps && Mathf.Abs(a.M32 - b.M32) <= eps
                && Mathf.Abs(a.M33 - b.M33) <= eps && Mathf.Abs(a.M34 - b.M34) <= eps
                && Mathf.Abs(a.M41 - b.M41) <= eps && Mathf.Abs(a.M42 - b.M42) <= eps
                && Mathf.Abs(a.M43 - b.M43) <= eps && Mathf.Abs(a.M44 - b.M44) <= eps;
        }


        /// <summary>
        /// 写一个 morph 权重，**先确认 SkinnedModel 真的在**。
        ///
        /// 为什么这一层要自己挡：引擎侧 `AnimatedModel::SetBlendShapeWeight` 开头就是
        /// `const auto* model = SkinnedModel.Get(); CHECK(model);`
        /// （Source/Engine/Level/Actors/AnimatedModel.cpp:462-463）。资产为空时它**不是**抛托管异常，
        /// 而是走引擎断言失败——弹模态框、把编辑器主线程卡住，于是这一进程上所有 MCP 调用一起超时
        /// （2026-10-15 实测：预制体实例的 SkinnedModel 为空，morph 写入直接把编辑器钉死）。
        /// 这种失败没法用 try/catch 兜，只能在调用前挡掉；挡了还必须**亮出来**，不能静默归零。
        /// </summary>
        private void WriteMorph(string name, float value)
        {
            float written;
            if (_writtenMorphs.TryGetValue(name, out written) && Mathf.Abs(written - value) < 1e-5f)
            {
                // 与上次实写一致 ⇒ 引擎那边已经是这个值了，跳过这次 WaitForLoaded + 全表扫描。
                MorphWritesSkippedUnchanged++;
                return;
            }

            if (_model == null || _model.SkinnedModel == null)
            {
                MorphWritesBlockedNoModel++;
                if (Warnings.Count < 32 && !_warnedNoSkinnedModel)
                {
                    _warnedNoSkinnedModel = true;
                    Warnings.Add("SkinnedModel 为空，morph 写入已在引擎断言之前挡下（本 Applier 累计 "
                                 + MorphWritesBlockedNoModel + " 次）");
                }

                return;
            }

            _model.SetBlendShapeWeight(name, value);
            // 只在**真的写进引擎之后**记账：挡在空模型分支里 return 的话记了也没写，
            // 下一轮就会因为"值没变"被跳过，等于把这条通道的形状永久静默丢掉。
            _writtenMorphs[name] = value;
        }

        /// <summary>卸载：写过的 morph 归零、偏移撤掉、事件摘掉。</summary>
        public void Clear()
        {
            // 2026-10-23 实测：从命名页「返回」重进捏脸页时，CollectModels 会对上一页那具
            // 已随页面销毁的 AnimatedModel 调 Clear；原生侧抛 NullReferenceException，
            // 异常冒到 CcFaceSculptPage 后 _character 归 null（本轮改动全部静默失效）。
            // 目标已经没了就只清本地记账：写不进去，也撤不出来。
            bool alive = ModelAlive;

            if (alive)
            {
                foreach (string name in _appliedMorphs)
                {
                    WriteMorph(name, 0f);
                }
            }

            _appliedMorphs.Clear();

            if (alive && _writtenPoseBone.Count > 0)
            {
                try
                {
                    // 逐骨还原底稿。当前值已被动画/引擎改过的骨直接跳过——我们的贡献本来就不在里面了。
                    foreach (KeyValuePair<string, Matrix> kv in _writtenPoseBone)
                    {
                        int index = NodeIndex(kv.Key);
                        if (index < 0)
                        {
                            continue;
                        }

                        Matrix current;
                        _model.GetNodeTransformation(index, out current, false);
                        Matrix last = kv.Value;
                        Matrix basePose;
                        if (!MatApproxEqual(ref current, ref last) || !_basePose.TryGetValue(kv.Key, out basePose))
                        {
                            continue;
                        }

                        _model.SetNodeTransformation(index, basePose, false);
                    }
                }
                catch (Exception ex)
                {
                    if (!_clearFailureLogged)
                    {
                        _clearFailureLogged = true;
                        Debug.LogWarning("[MetaHumanApplier] clear-failed: " + ex.GetType().Name
                            + " model=" + (_model ? _model.Name : "<destroyed>"));
                    }
                }
            }

            _writtenPoseBone.Clear();
            _basePose.Clear();
            _appliedTranslate.Clear();

            _shapeMorphs.Clear();
            _shapeBones.Clear();
            _expressionMorphs.Clear();
            _expressionOffsets.Clear();
            _composed = null;
            _writtenVersion = -1;

            HookAnimationUpdated(false);
        }

        /// <summary>
        /// 模板里的材质目标名是 UE 语义名，材质实例只认 Flax 参数名，必须在这层翻译。
        /// Melanin 额外做亮度映射：0..1 → tint 1.15..0.45（1.0 = 原贴图，越黑越小）。
        /// </summary>
        /// <summary>
        /// 瞳色色板：与创角色板 CcCreationPages.EyeColors 逐项一致（棕 / 近黑 / 琥珀 / 青绿 / 灰蓝）。
        /// 模板给 0..1 的指数，这里在相邻两色之间插值，避免离散切换时跳变。
        /// </summary>
        private static readonly Color[] _irisPalette =
        {
            new Color(0.32f, 0.20f, 0.12f),
            new Color(0.12f, 0.12f, 0.14f),
            new Color(0.62f, 0.42f, 0.16f),
            new Color(0.24f, 0.48f, 0.44f),
            new Color(0.32f, 0.40f, 0.54f),
        };

        private static Color Palettize(float t, Color[] palette)
        {
            if (t < 0f)
            {
                t = 0f;
            }
            else if (t > 1f)
            {
                t = 1f;
            }

            float x = t * (palette.Length - 1);
            int i = (int)x;
            if (i >= palette.Length - 1)
            {
                return palette[palette.Length - 1];
            }

            float f = x - i;
            Color a = palette[i];
            Color b = palette[i + 1];
            return new Color(
                a.R + (b.R - a.R) * f,
                a.G + (b.G - a.G) * f,
                a.B + (b.B - a.B) * f);
        }

        private static void MapMaterialFloat(string target, float value, out string parameterName, out float parameterValue)
        {
            parameterName = target;
            parameterValue = value;

            List<MetaHumanMaterialTargetEntry> entries = MetaHumanMaterialMap.Entries();
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Target != target)
                {
                    continue;
                }

                if (entries[i].State == MetaHumanMaterialTargetState.Mapped
                    && !string.IsNullOrEmpty(entries[i].FlaxParameter))
                {
                    parameterName = entries[i].FlaxParameter;
                }
                break;
            }

            if (parameterName == "SkinTint")
            {
                float t = value < 0f ? 0f : (value > 1f ? 1f : value);
                parameterValue = 1.15f - 0.7f * t;
            }
            else if (parameterName == "SkinRoughness")
            {
                float t = value < 0f ? 0f : (value > 1f ? 1f : value);
                parameterValue = 0.4f + 1.2f * t;
            }
            else if (parameterName == "SkinRedness")
            {
                // 掩码里大量像素接近 1；满值直接当 alpha 会整脸红透。钳到 0.15 才是"气色"。
                float t = value < 0f ? 0f : (value > 1f ? 1f : value);
                parameterValue = 0.15f * t;
            }
        }

        /// <summary>
        /// 判断材质参数能否接收浮点。查不到该参数时返回 true：保持原有的"写了没人应答也不报错"通路，
        /// 只有确实存在、但类型是贴图之类的才判为不可写。
        /// </summary>
        private static bool IsScalarMaterialParameter(MaterialParameter parameter)
        {
            if (parameter == null)
            {
                return true;
            }

            MaterialParameterType type = parameter.ParameterType;
            return type == MaterialParameterType.Bool
                || type == MaterialParameterType.Integer
                || type == MaterialParameterType.Float;
        }
    }
}
