using System;
using System.Collections.Generic;
using FlaxEngine;
using MetaHuman;
using MetaHuman.Core;

namespace MetaHuman.Runtime
{
    /// <summary>
    /// 数字人角色组件：模板 + 会话 + 写回端的唯一接入点。
    /// 玩家界面只与本组件打交道（调数值、记步、回溯、发布、存/读快照），不接触映射表，也不直接碰 blendshape。
    /// </summary>
    public class MetaHumanCharacter : Script
    {
        [Tooltip("参与合成的模板 Id，按顺序合并（脸、身体、毛发…）")]
        [Serialize]
        public string[] TemplateIds = new string[0];

        [Tooltip("OnEnable 时自动装载并应用。远程角色可关掉，等快照到了再调 Load()")]
        [Serialize]
        public bool ApplyOnStart = true;

        [Tooltip("左右联动：改一侧形状时对岸一起改")]
        [Serialize]
        public bool LinkSymmetric = true;

        [Tooltip("骨骼缩放通道。引擎是否如实 honored 未经 PIE 证实，默认只用骨骼平移")]
        [Serialize]
        public bool EnableBoneScaleChannel = true;

        [Tooltip("留空则自动收集本 Actor 及子 Actor 上的所有 AnimatedModel")]
        [Serialize]
        public AnimatedModel[] TargetModels = new AnimatedModel[0];

        [Tooltip("材质参数写入目标。留空则肤色/发色等材质通道只算不写，并在诊断里点名")]
        [Serialize]
        public MaterialBase[] MaterialTargets = new MaterialBase[0];

        [Tooltip("角色身份：local / editing / preview 会锁在能承载形状的 LOD；remote 按画质与距离降档")]
        [Serialize]
        public string CharacterRole = "remote";

        [Tooltip("渲染档位，决定远端角色最多能粗到哪一档 LOD")]
        [Serialize]
        public MetaHumanRenderQuality RenderQuality = MetaHumanRenderQuality.High;

        [Tooltip("按与主相机的距离自动降 LOD；关掉就只按画质与身份定")]
        [Serialize]
        public bool UseDistanceLod = true;

        [Tooltip("LOD 复核周期（秒）")]
        [Serialize]
        public float LodCheckInterval = 0.25f;

        [Tooltip("本地外观仓库的键（角色 Id）。留空则不自动读存；设了就在 Load() 后恢复最近一版")]
        [Serialize]
        public string PersistenceCharacterId = string.Empty;

        [Tooltip("恢复时优先用已发布的那一版（发布后不可回溯，默认就按终版渲染）")]
        [Serialize]
        public bool PreferPublishedLook = true;

        [Tooltip("差分历史的整份快照打点间隔（步）。回溯步数本身没有上限（方案文档 §116），"
            + "这个数只决定每隔几步存一份全量以封顶任意跳转的重放长度；填 1 就是每步都存全量。")]
        [Serialize]
        public int HistoryKeyframeInterval = 32;

        [Tooltip("自动挂上表情动画驱动（MetaHumanFaceAnimator）：外部面部追踪（ARKit 通道）与表情剪辑的入口。"
            + "它没有控制值在动时一帧都不写，所以挂着是安全的；关掉就等于这条能力链没有宿主。")]
        [Serialize]
        public bool EnableFaceAnimator = true;

        public MetaHumanSession Session { get; private set; }

        public bool IsLoaded { get; private set; }

        public bool IsSealed { get { return Session != null && Session.IsSealed; } }

        public readonly List<string> LoadDiagnostics = new List<string>();

        /// <summary>
        /// 装载期量出来的「这（几）具模型能承载多少条头部参数」，逐模型一行（文档 §36）。
        /// 玩家调头部滑杆没反应时，这一行就是答案，不该要人去猜是代码坏了还是资产不带脸骨。
        /// </summary>
        public string HeadCoverageReport = string.Empty;

        /// <summary>所有模型里最好的那个的全可承载条数；0 表示这具角色改不动头部形状。</summary>
        public int ServableHeadParameterCount;

        /// <summary>参与统计的头部参数条数（不含 Body 区域）。</summary>
        public int HeadParameterCount;

        public event Action<MetaHumanSolution, bool> OnApplied;

        private readonly List<MetaHumanAppearanceApplier> _appliers = new List<MetaHumanAppearanceApplier>();
        private bool _subscribed;

        /// <summary>模板声明的材质写入目标（浮点/颜色/贴图三档都算）；装载前为空。</summary>
        public HashSet<string> DeclaredMaterialTargets
        {
            get { return _declaredMaterialTargets; }
        }

        private HashSet<string> _declaredMaterialTargets = new HashSet<string>();

        public override void OnEnable()
        {
            if (EnableFaceAnimator)
            {
                EnsureFaceAnimator();
            }

            if (ApplyOnStart && !IsLoaded)
            {
                Load();
            }
        }

        public override void OnDisable()
        {
            Unbind();
        }

        /// <summary>装载模板并建立会话。失败原因写进 LoadDiagnostics，不静默。</summary>
        public bool Load()
        {
            Unbind();
            LoadDiagnostics.Clear();

            if (TemplateIds == null || TemplateIds.Length == 0)
            {
                LoadDiagnostics.Add("没有配置 TemplateIds");
                return false;
            }

            List<MetaHumanTemplate> parts = new List<MetaHumanTemplate>();
            for (int i = 0; i < TemplateIds.Length; i++)
            {
                MetaHumanTemplate part = MetaHumanTemplateStore.Get(TemplateIds[i]);
                if (part == null)
                {
                    LoadDiagnostics.Add("模板装载失败：" + TemplateIds[i]);
                }
                else
                {
                    parts.Add(part);
                }
            }

            if (parts.Count == 0)
            {
                return false;
            }

            MetaHumanTemplate merged = MetaHumanTemplate.Merge(parts);
            List<string> errors = merged.Validate();
            if (errors.Count > 0)
            {
                LoadDiagnostics.Add("合成模板校验失败：" + string.Join(" | ", errors.ToArray()));
                return false;
            }

            try
            {
                Session = new MetaHumanSession(merged);
            }
            catch (Exception exception)
            {
                LoadDiagnostics.Add("会话建立失败：" + exception.Message);
                return false;
            }

            Session.State.LinkSymmetric = LinkSymmetric;
            Session.History.KeyframeInterval = HistoryKeyframeInterval;

            // 材质需求以模板声明为准（MaterialTargets() 现在算齐浮点/颜色/贴图三档），
            // 这样"要不要给这具身体准备材质实例"只有一个口径，不由各写方各自数一遍。
            _declaredMaterialTargets = Session.Template != null
                ? Session.Template.MaterialTargets()
                : new HashSet<string>();

            CollectModels();
            if (_appliers.Count == 0)
            {
                LoadDiagnostics.Add("没有可用的 AnimatedModel，形状通道无处可写");
            }

            Session.OnSolutionChanged += HandleSolutionChanged;
            _subscribed = true;
            IsLoaded = true;
            RestoreFromStore();
            ApplyNow();
            return true;
        }

        private void CollectModels()
        {
            // 旧 applier 必须先 Clear：它挂在模型的 AnimationUpdated 上，只 Clear 列表会让旧目标
            // 继续每帧重放姿势（页签每次切换都会 RetargetModels，订阅会越积越多）。
            for (int i = 0; i < _appliers.Count; i++)
            {
                _appliers[i].Clear();
            }

            _appliers.Clear();

            List<AnimatedModel> models = new List<AnimatedModel>();
            if (TargetModels != null)
            {
                for (int i = 0; i < TargetModels.Length; i++)
                {
                    if (TargetModels[i] != null)
                    {
                        models.Add(TargetModels[i]);
                    }
                }
            }

            if (models.Count == 0 && Actor != null)
            {
                // 真实 MetaHuman 是「一个根 Actor 下面挂 head/teeth/eyes/tongue/eyeshell…几十个子网格」，
                // 层级还能再套一层（Face 组下面才是网格）。只扫直接子级会只绑到一两个模型，
                // 于是牙齿眼睛不动而没人报错，所以这里整棵树递归找。
                AnimatedModel self = Actor as AnimatedModel;
                if (self != null)
                {
                    models.Add(self);
                }
                else
                {
                    CollectModelsRecursive(Actor, models, 0);
                }
            }

            for (int i = 0; i < models.Count; i++)
            {
                MetaHumanAppearanceApplier applier = new MetaHumanAppearanceApplier(models[i]);
                applier.EnableBoneScaleChannel = EnableBoneScaleChannel;
                if (MaterialTargets != null && MaterialTargets.Length > 0)
                {
                    applier.SetMaterials(MaterialTargets);
                }
                else if (_declaredMaterialTargets.Count > 0)
                {
                    // 模板确实要写材质才去就地取实例：纯形状模板不该白白造一批虚拟实例
                    applier.SetMaterials(CollectMaterialInstances(models[i]));
                }
                else
                {
                    applier.SetMaterials(new MaterialBase[0]);
                }
                _appliers.Add(applier);
            }

            if (_declaredMaterialTargets.Count > 0 && MaterialTargets != null && MaterialTargets.Length == 0)
            {
                LoadDiagnostics.Add("模板声明 " + _declaredMaterialTargets.Count
                    + " 个材质目标（浮点/颜色/贴图），未手配材质 → 已按槽位自动取材质实例");
            }

            MeasureHeadCoverage(models);
        }

        /// <summary>
        /// 装载期问一句：**这些模型到底能承载多少条头部参数**。
        ///
        /// 判据单位是参数而不是名字（一条参数缺一个骨骼目标是"半个动作"，按名计数说不了）；
        /// 承载不了不算错，是资产缺口 ⇒ 只报告、不拦写回，也不静默。
        /// 名字只能在引擎里取：字节扫描 .flax 看不见 SkinnedModel 的节点名（文档 §34 的判别实验）。
        /// 多网格角色（head/teeth/eyes…）逐具各报一行，Actor 级取"最好的那具"，不假装跨网格求并集。
        /// </summary>
        private void MeasureHeadCoverage(List<AnimatedModel> models)
        {
            HeadCoverageReport = string.Empty;
            ServableHeadParameterCount = 0;
            HeadParameterCount = 0;

            MetaHumanTemplate template = Session != null ? Session.Template : null;
            if (template == null || models == null || models.Count == 0)
            {
                return;
            }

            int best = 0;
            for (int i = 0; i < models.Count; i++)
            {
                SkinnedModel asset = models[i] == null ? null : models[i].SkinnedModel;
                if (asset == null)
                {
                    continue;
                }

                // 取名字与承载量的唯一实现（探针）：见 MetaHumanAssetProbe
                // 这里刻意调 4 参版而不是 2 参 Measure：探针返回的"经别名对齐"条数
                // 若被丢掉，就等于"探得到但没人说"——直接命中与靠别名救回来必须在读数里分开。
                HashSet<string> bones;
                HashSet<string> morphs;
                int aliasHits = MetaHuman.MetaHumanAssetProbe.CollectNames(asset, template, out bones, out morphs);
                MetaHumanBindingReport report = MetaHumanBinding.Bind(template, bones, morphs);
                HeadParameterCount = report.HeadParams;

                if (HeadCoverageReport.Length > 0)
                {
                    HeadCoverageReport += " ; ";
                }

                HeadCoverageReport += asset.Path + " 全可承载 " + report.ServableParams + "/" + report.HeadParams
                    + "、部分 " + report.PartiallyServableParams
                    + "、写不出来 " + report.UnservableParams.Count
                    + "、只挂材质 " + report.NoTargetParams.Count
                    + "、骨架目标经别名对齐 " + aliasHits;

                if (report.ServableParams > best)
                {
                    best = report.ServableParams;
                }
            }

            ServableHeadParameterCount = best;
            if (HeadCoverageReport.Length == 0)
            {
                return;
            }

            LoadDiagnostics.Add("头部承载量：" + HeadCoverageReport);
            if (best == 0 && HeadParameterCount > 0)
            {
                LoadDiagnostics.Add("这些模型承载不了任何头部形状参数（头部形状的主载体是 FACIAL_ 那套脸骨）"
                    + " ⇒ 玩家动头部滑杆不会有效果，需要带脸骨的 MetaHuman 头资产，不是代码问题");
            }
        }

        /// <summary>
        /// 没手配材质目标时，就地把这具模型的每个槽位换成它自己的材质实例。
        /// 不这样做的话肤色/发色那 9 个材质通道永远"只算不写"，而配错成基础材质又会污染共享资产。
        /// </summary>
        private static MaterialBase[] CollectMaterialInstances(AnimatedModel model)
        {
            if (model == null)
            {
                return new MaterialBase[0];
            }

            MaterialSlot[] slots = model.MaterialSlots;
            if (slots == null || slots.Length == 0)
            {
                return new MaterialBase[0];
            }

            MaterialBase[] targets = new MaterialBase[slots.Length];
            int made = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                MaterialBase slotMaterial = slots[i] != null ? slots[i].Material : null;
                if (slotMaterial == null)
                {
                    continue;
                }

                MaterialInstance instance = slotMaterial as MaterialInstance;
                if (instance == null)
                {
                    // 基础材质一律不当写入目标：直接写它会把同一个材质资产上的所有角色一起改掉。
                    // 这里给这个 Actor 的单槽位换成它自己的虚拟实例，参数只覆盖在本 Actor 上。
                    MaterialBase virtualInstance = slotMaterial.CreateVirtualInstance();
                    instance = virtualInstance as MaterialInstance;
                    if (instance == null)
                    {
                        continue;
                    }

                    model.SetMaterial(i, instance);
                }

                targets[i] = instance;
                made++;
            }

            if (made == 0)
            {
                FlaxEngine.Debug.LogWarning("[MetaHuman] 取不到任何材质实例，肤色/发色通道不会生效");
            }

            return targets;
        }

        private void CollectModelsRecursive(Actor actor, List<AnimatedModel> found, int depth)
        {
            if (actor == null || depth > 8)
            {
                return;
            }

            AnimatedModel model = actor as AnimatedModel;
            if (model != null)
            {
                found.Add(model);
            }

            Actor[] children = actor.Children;
            for (int i = 0; i < children.Length; i++)
            {
                CollectModelsRecursive(children[i], found, depth + 1);
            }
        }

        /// <summary>
        /// 只换「写回目标」、不动会话：把形状/材质重新指到新一批 <see cref="AnimatedModel"/> 并立刻写入当前 Solution。
        /// <para>
        /// 存在的理由：创角预览的 Actor 会随页面销毁重建（页面每次导航都新建），而捏脸会话要跨页留着。
        /// 走 <see cref="Load"/> 换目标会连 <c>Session</c> 一起重建 ⇒ 已调好的数值与历史全丢
        /// （2026-10-18 实测「捏脸 → 命名 → 返回，数据无保留」正是这条）。
        /// </para>
        /// </summary>
        /// <returns>至少接上一个可写模型才算成功。</returns>
        public bool RetargetModels(AnimatedModel[] models)
        {
            if (Session == null)
            {
                return false;
            }

            TargetModels = models;
            CollectModels();
            if (_appliers.Count == 0)
            {
                LoadDiagnostics.Add("重设写回目标后没有可用的 AnimatedModel，形状无处可写");
                return false;
            }

            ApplyNow();
            return true;
        }

        /// <summary>
        /// 逐具写回目标的自证串（见 <c>MetaHumanAppearanceApplier.Diag</c>）。
        /// 聚合数会把"该写的被跳过"和"本来就不用写"压成同一个读数，这时只能逐具摊开。
        /// </summary>
        public string AppliersDiag()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < _appliers.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(' ');
                }

                sb.Append(_appliers[i].Diag());
            }

            return sb.Length == 0 ? "<无 applier>" : sb.ToString();
        }

        /// <summary>
        /// 至少还有一个活着的写回目标吗？全死了就该让宿主（创角页面）重新指认当前被渲染的那颗头，
        /// 而不是把数值写进一具已经不存在的 Actor。
        /// </summary>
        public bool HasLiveWriteTarget
        {
            get
            {
                for (int i = 0; i < _appliers.Count; i++)
                {
                    if (_appliers[i].ModelAlive)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private void Unbind()
        {
            if (Session != null && _subscribed)
            {
                Session.OnSolutionChanged -= HandleSolutionChanged;
                _subscribed = false;
            }

            for (int i = 0; i < _appliers.Count; i++)
            {
                _appliers[i].Clear();
            }

            _appliers.Clear();
            IsLoaded = false;
        }

        private void HandleSolutionChanged(MetaHumanSolution solution, bool full)
        {
            ApplyNow();
            Action<MetaHumanSolution, bool> handler = OnApplied;
            if (handler != null)
            {
                handler(solution, full);
            }
        }

        /// <summary>写回端列表，供表情动画器把解析结果推给同一批 applier。</summary>
        public IReadOnlyList<MetaHumanAppearanceApplier> Appliers { get { return _appliers; } }

        private MetaHumanFaceAnimator _faceAnimator;

        /// <summary>表情动画驱动（外部面部追踪与剪辑轨道的宿主）。没挂时为 null。</summary>
        public MetaHumanFaceAnimator FaceAnimator { get { return _faceAnimator; } }

        /// <summary>
        /// 挂上表情动画驱动；已经有就复用那一份。
        /// 判"有没有"要问 Actor 上的脚本清单而不是自己那个字段：域重载或资产里本来就挂着时字段是空的，
        /// 只比字段会再 AddScript 一次，于是同一个角色上跑两遍表情写入。
        /// </summary>
        public MetaHumanFaceAnimator EnsureFaceAnimator()
        {
            if (Actor == null)
            {
                return null;
            }

            MetaHumanFaceAnimator[] found = Actor.GetScripts<MetaHumanFaceAnimator>();
            if (found != null && found.Length > 0)
            {
                _faceAnimator = found[0];
            }
            else
            {
                _faceAnimator = Actor.AddScript<MetaHumanFaceAnimator>();
            }

            if (_faceAnimator != null)
            {
                _faceAnimator.Owner = this;
            }

            return _faceAnimator;
        }

        /// <summary>
        /// 表情通道入口：把动画解析出的 blendshape 权重与面部骨骼偏移交给写回端，
        /// 与形状通道相加后一次落地（两个通道不能各自直写，否则后写的静默覆盖前一个）。
        /// </summary>
        public void PushExpression(Dictionary<string, float> morphWeights, Dictionary<string, MetaHumanVec3> jointOffsets)
        {
            PushExpression(morphWeights, jointOffsets, null);
        }

        public void PushExpression(Dictionary<string, float> morphWeights, Dictionary<string, MetaHumanVec3> jointOffsets,
            Dictionary<string, MetaHumanBoneRotation> jointRotations)
        {
            for (int i = 0; i < _appliers.Count; i++)
            {
                _appliers[i].SetExpression(morphWeights, jointOffsets, jointRotations);
            }
        }

        /// <summary>
        /// 最近一次 <see cref="ApplyNow"/> 的耗时（毫秒）。卡顿要按环节量出来，不能靠"感觉慢"：
        /// 这个数与 UI 侧"一次 Adjust 的总耗时"之差就是纯数据核（Rebuild）的开销。
        /// </summary>
        public double LastApplyMilliseconds { get; private set; }

        /// <summary>本轮改过的骨骼根数（所有 applier 之和）。</summary>
        public int LastApplyBoneWrites { get; private set; }

        /// <summary>本轮整姿写回次数（批量之后每具模型最多 1 次；改回逐根写时它会等于骨骼根数）。</summary>
        public int LastApplyPoseWrites { get; private set; }

        /// <summary>本轮因"与上次实写相同"而跳过的 morph 写入次数。</summary>
        public int LastApplyMorphSkips { get; private set; }

        /// <summary>
        /// 各 applier 累计的"整帧跳过重放"次数之和。这是**累计值**而不是本轮值：
        /// 它发生在挂在 AnimationUpdated 上的每帧重放里，本轮计数在拖动那一刻永远是 0，
        /// 拿两条相隔一秒的回执相减才是"每秒省掉了多少次全量骨骼求值"。
        /// </summary>
        public int TotalBoneReplaySkips { get; private set; }

        /// <summary>各 applier 累计的"每帧动画事件里真的写了整姿"次数之和 —— 稳态帧开销看这个数。</summary>
        public int TotalHookPoseWrites { get; private set; }

        private readonly System.Diagnostics.Stopwatch _applyWatch = new System.Diagnostics.Stopwatch();

        /// <summary>
        /// 当前发型下标（模板参数 hair.style 解出来的离散值；没有这条参数/值时为 0）。
        /// 宿主（预览/世界/远端）的 MetaHumanHairBinder 读它来换发卡模型——
        /// 发型不是材质也不是骨骼，写回端管不到，只能由宿主在模型层落地。
        /// </summary>
        public int HairStyle { get; private set; }

        private void ApplyNow()
        {
            if (Session == null)
            {
                return;
            }

            int hairStyle;
            HairStyle = Session.Solution.HairStyles.TryGetValue("hair", out hairStyle) ? hairStyle : 0;

            bool applyShape = LastLodDecision == null || LastLodDecision.ApplyShape;
            _applyWatch.Restart();
            int bones = 0;
            int poses = 0;
            int skips = 0;
            for (int i = 0; i < _appliers.Count; i++)
            {
                _appliers[i].ShapeWritingEnabled = applyShape;
                _appliers[i].Apply(Session.Solution);
                bones += _appliers[i].BoneWritesLastPass;
                poses += _appliers[i].PoseWritesLastPass;
                skips += _appliers[i].MorphWritesSkippedUnchanged;
            }

            _applyWatch.Stop();
            LastApplyMilliseconds = _applyWatch.Elapsed.TotalMilliseconds;
            LastApplyBoneWrites = bones;
            LastApplyPoseWrites = poses;
            LastApplyMorphSkips = skips;
            int replaySkips = 0;
            int hookWrites = 0;
            for (int i = 0; i < _appliers.Count; i++)
            {
                replaySkips += _appliers[i].BoneReplaysSkippedUnchanged;
                hookWrites += _appliers[i].HookPoseWrites;
            }

            TotalBoneReplaySkips = replaySkips;
            TotalHookPoseWrites = hookWrites;
        }

        /// <summary>
        /// LOD 复核。Flax 的 blendshape 只在 LOD0 生效，远端角色一降档整张捏脸就没了；
        /// 所以要么保住形状档，要么整体不写形状，不接受半张脸有半张脸没有。
        /// </summary>
        public override void OnUpdate()
        {
            if (!IsLoaded || Session == null || _appliers.Count == 0)
            {
                return;
            }

            _lodTimer += Time.DeltaTime;
            if (_lodTimer < LodCheckInterval)
            {
                return;
            }

            _lodTimer = 0f;

            float distance = 0f;
            if (UseDistanceLod && Actor != null)
            {
                // 量的是「角色到相机」，不是相机自己走了多远
                distance = Vector3.Distance(Actor.Position, LodAnchor());
            }

            int lods = 1;
            for (int i = 0; i < _appliers.Count; i++)
            {
                if (_appliers[i].AvailableLodCount > lods)
                {
                    lods = _appliers[i].AvailableLodCount;
                }
            }

            MetaHumanLodDecision decision = MetaHumanLodPolicy.Decide(
                CharacterRole, RenderQuality, distance, Session.Solution.MorphWeights.Count, lods);

            bool changed = LastLodDecision == null
                || LastLodDecision.ApplyShape != decision.ApplyShape
                || LastLodDecision.MaxLod != decision.MaxLod;
            LastLodDecision = decision;

            if (!changed)
            {
                return;
            }

            for (int i = 0; i < _appliers.Count; i++)
            {
                _appliers[i].SetMaxLod(decision.MaxLod);
            }

            ApplyNow();
        }

        /// <summary>距离参照：优先主相机——玩家眼里的屏幕大小才是该降 LOD 的理由。</summary>
        private Vector3 LodAnchor()
        {
            Camera camera = Camera.MainCamera;
            if (camera != null && camera.IsActive)
            {
                return camera.Position;
            }

            return Actor != null ? Actor.Position : Vector3.Zero;
        }

        private float _lodTimer;

        /// <summary>最近一次 LOD 判定，供诊断与调试面板读。</summary>
        public MetaHumanLodDecision LastLodDecision { get; private set; }

        /// <summary>滑杆拖动中：实时预览，不记步。</summary>
        public float Adjust(string paramId, float value)
        {
            if (Session == null)
            {
                return 0f;
            }

            float applied = Session.Adjust(paramId, value);
            // **必须在这里落地**。原先 Adjust 只改会话，`ApplyNow()` 只有三处会跑：
            // Load、历史回溯、以及 OnUpdate 里"LOD 判定发生变化"那一次 —— 拖滑杆不改判定，
            // 所以数值在会话里变了、脸上一个字节都没写（2026-10-17 用户实测"参数变化没实际效果"）。
            ApplyNow();
            LogShapeWriteOnce();
            return applied;
        }

        /// <summary>松手/点定：记一步。已发布（封存）时返回 false。</summary>
        public bool Commit(string label)
        {
            if (Session == null)
            {
                return false;
            }

            bool recorded = Session.Commit(label);
            if (recorded)
            {
                ApplyNow();
            }

            return recorded;
        }

        public float AdjustAndCommit(string paramId, float value, string label)
        {
            if (Session == null)
            {
                return 0f;
            }

            float applied = Session.AdjustAndCommit(paramId, value, label);
            ApplyNow();
            LogShapeWriteOnce();
            return applied;
        }

        private bool _shapeWriteLogged;

        /// <summary>
        /// 形状落地回执，只打一次：把"写回端报的写/翻译/缺失"与"LOD 判定是否允许写形状"放在一起。
        /// 这两个数以前分开看得见，合不上时就只能猜是"没送"还是"被 LOD 压住"。
        /// </summary>
        private void LogShapeWriteOnce()
        {
            if (_shapeWriteLogged)
            {
                return;
            }

            _shapeWriteLogged = true;
            FlaxEngine.Debug.Log("[MetaHuman] 形状落地首回执: " + MorphReceiptText()
                + " | LOD判定=" + (LastLodDecision != null ? LastLodDecision.ToString() : "<未判>")
                + " | 身份=" + CharacterRole
                + " | 写回端=" + _appliers.Count);
        }

        public MetaHumanParameter FindParameter(string paramId)
        {
            return Session != null ? Session.Template.Find(paramId) : null;
        }

        public List<MetaHumanParameter> ParametersInRegion(MetaHumanRegion region)
        {
            return Session != null ? Session.Template.InRegion(region) : new List<MetaHumanParameter>();
        }

        public bool ApplyPreset(string presetId, string label)
        {
            return Session != null && Session.ApplyPreset(presetId, label);
        }

        public bool Randomize(int seed, string label)
        {
            return Session != null && Session.Randomize(seed, label);
        }

        public bool CanUndo { get { return Session != null && Session.CanUndo; } }

        public bool CanRedo { get { return Session != null && Session.CanRedo; } }

        public int HistoryStepCount { get { return Session != null ? Session.StepCount : 0; } }

        public int HistoryCursor { get { return Session != null ? Session.Cursor : -1; } }

        public MetaHumanLook Undo()
        {
            return Session != null ? Session.Undo() : null;
        }

        public MetaHumanLook Redo()
        {
            return Session != null ? Session.Redo() : null;
        }

        /// <summary>回溯到任意一步（界面步骤条直接用它）。</summary>
        public bool JumpToStep(int index)
        {
            return Session != null && Session.JumpTo(index) != null;
        }

        public List<string> HistoryLabels()
        {
            return Session != null ? Session.History.Labels() : new List<string>();
        }

        /// <summary>保存快照（不结束会话，仍可继续回溯）。</summary>
        public MetaHumanLook Save()
        {
            return Session != null ? Session.Save() : null;
        }

        /// <summary>发布：快照封存，此后回溯通道关闭；带角色 Id 时顺手落本地仓库。</summary>
        public MetaHumanLook Publish()
        {
            MetaHumanLook look = Session != null ? Session.Publish() : null;
            if (look != null)
            {
                // 还没有角色 Id 的场合（创角流程）留一份待认领，回执到了再落到具体角色名下
                if (string.IsNullOrEmpty(PersistenceCharacterId))
                {
                    MetaHumanLookStore.SetPendingLook(look);
                }

                SaveToStore(look);
            }

            return look;
        }

        /// <summary>落一版本地快照（未设 PersistenceCharacterId 时返回 null）。</summary>
        public string SaveToStore(MetaHumanLook look)
        {
            if (string.IsNullOrEmpty(PersistenceCharacterId) || look == null)
            {
                return null;
            }

            string error;
            string path = MetaHumanLookStore.Save(PersistenceCharacterId, look, out error);
            if (path == null)
            {
                LoadDiagnostics.Add("本地外观落盘失败：" + error);
                Debug.LogError("MetaHuman 外观落盘失败：" + error);
            }

            return path;
        }

        /// <summary>保存快照并落本地仓库，返回快照（未设 Id 时只返回快照）。</summary>
        public MetaHumanLook SaveAndStore()
        {
            MetaHumanLook look = Save();
            if (look != null)
            {
                SaveToStore(look);
            }

            return look;
        }

        private void RestoreFromStore()
        {
            if (Session == null || string.IsNullOrEmpty(PersistenceCharacterId))
            {
                return;
            }

            List<MetaHumanLook> history = MetaHumanLookStore.ReadAll(PersistenceCharacterId);
            if (history.Count == 0)
            {
                return;
            }

            // 选哪一份交给纯核的静态判据（离线门钉得住），这里只把它翻成会话动作。
            int chosenIndex = MetaHumanLook.SelectRestoreIndex(history, PreferPublishedLook);
            MetaHumanLook chosen = chosenIndex < 0 ? null : history[chosenIndex];

            if (chosen == null)
            {
                return;
            }

            if (chosen.Published)
            {
                // 读档即终版：回溯通道不该对已发布的角色敞开
                Session.LoadLook(chosen);
                Session.Publish();
                return;
            }

            Session.LoadLook(chosen);
        }

        public bool LoadLook(MetaHumanLook look)
        {
            if (Session == null || look == null)
            {
                return false;
            }

            try
            {
                Session.LoadLook(look);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError("MetaHuman 快照装载失败：" + exception.Message);
                return false;
            }
        }

        public bool LoadLookJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }

            try
            {
                return LoadLook(MetaHumanLook.FromJson(json));
            }
            catch (Exception exception)
            {
                Debug.LogError("MetaHuman 快照 JSON 解析失败：" + exception.Message);
                return false;
            }
        }

        public string ExportLookJson()
        {
            MetaHumanLook look = Save();
            return look == null ? null : look.ToJson();
        }

        /// <summary>诊断汇总：装载问题 + 写回端跳过计数 + 钳位与材质冲突。PIE 里直接看这个。</summary>
        /// <summary>
        /// 写回端形状回执的三个数（各网格相加；形状通道与表情通道在落地前已合成，这里无法再分）。
        /// 动画器报告也取它：动画器只负责"送出去"，落没落到脸上只有写回端知道，
        /// 两个口径放一处才不会把"送出了"读成"画上了"。
        /// </summary>
        public void CollectMorphReceipt(out int applied, out int bridged, out int skipped)
        {
            applied = 0;
            bridged = 0;
            skipped = 0;
            for (int i = 0; i < _appliers.Count; i++)
            {
                applied += _appliers[i].AppliedMorphCount;
                bridged += _appliers[i].BridgedMorphCount;
                skipped += _appliers[i].SkippedMorphCount;
            }
        }

        public string MorphReceiptText()
        {
            int applied, bridged, skipped;
            CollectMorphReceipt(out applied, out bridged, out skipped);
            return "写=" + applied + " 经表翻译=" + bridged + " 缺失=" + skipped;
        }

        public string BuildDiagnosticReport()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("MetaHumanCharacter[").Append(Actor != null ? Actor.Name : "<无>").Append("] 模板=")
              .Append(TemplateIds != null ? TemplateIds.Length : 0)
              .Append(" 写回端=").Append(_appliers.Count)
              .Append(" 回溯步=").Append(HistoryStepCount)
              .Append("/游标=").Append(HistoryCursor);

            if (LoadDiagnostics.Count > 0)
            {
                sb.Append(" | 装载问题: ").Append(string.Join(" ; ", LoadDiagnostics.ToArray()));
            }

            // 节点映射隐患（重名/大小写/非蒙皮节点/别名）必须出现在诊断里：
            // 它们都表现为"参数写了但画面不动"，只数跳过条数分不出是资产没有还是映射选错了。
            var mappingNotes = new List<string>();
            for (int i = 0; i < _appliers.Count && mappingNotes.Count < 8; i++)
            {
                List<string> warnings = _appliers[i].Warnings;
                for (int k = 0; k < warnings.Count && mappingNotes.Count < 8; k++)
                {
                    string w = warnings[k];
                    if (w.StartsWith("重名", StringComparison.Ordinal)
                        || w.StartsWith("节点", StringComparison.Ordinal)
                        || w.StartsWith("骨骼经别名", StringComparison.Ordinal))
                    {
                        mappingNotes.Add("目标" + i + ": " + w);
                    }
                }
            }

            if (mappingNotes.Count > 0)
            {
                sb.Append(" | 节点映射: ").Append(string.Join(" ; ", mappingNotes.ToArray()));
            }
            // 每个 applier 本轮因名字未解析而跳过的骨（最多 16 个）：参数"拖了不动"时用它区分
            // 是绑定缺失还是权重太小；头部 applier 会先占满 FACIAL_*，所以必须按 applier 分开打。
            for (int i = 0; i < _appliers.Count; i++)
            {
                var skippedBones = _appliers[i].SkippedBoneNamesLastPass;
                if (skippedBones.Count > 0)
                {
                    sb.Append(" | 跳过骨").Append(i).Append("=").Append(string.Join(",", skippedBones.ToArray()));
                }
            }


            int morphs = 0;
            int bridged = 0;
            int skipped = 0;
            CollectMorphReceipt(out morphs, out bridged, out skipped);

            sb.Append(" | morph 已写=").Append(morphs).Append(" 跳过=").Append(skipped)
              .Append(" 经表翻译=").Append(bridged);

            if (Session != null)
            {
                sb.Append(" | 钳位=").Append(Session.Solution.ClampedMorphCount)
                  .Append(" 材质冲突=").Append(Session.Solution.Conflicts.Count)
                  .Append(" 参数=").Append(Session.Template.Parameters.Count);
            }

            return sb.ToString();
        }
    }
}
