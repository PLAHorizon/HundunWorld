using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.Equipment;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.UI.Ink.Components
{
    /// <summary>
    /// 3D 角色预览控件（水墨主题）。
    /// <para>
    /// 使用独立 <see cref="GPUTexture"/> + 离屏 <see cref="Camera"/> + 子 <see cref="Actor"/>
    /// 挂载 <see cref="AnimatedModel"/> 实现 UI 内嵌 3D 角色预览。通过
    /// <see cref="SceneRenderTask"/> 的 <c>ActorsSource = CustomActors</c> 仅渲染指定 Actor，
    /// 不污染主场景渲染输出。
    /// </para>
    /// <para>
    /// 默认模型取自 <see cref="EquipmentDatabase.GetBodyModel"/>（按性别，0=男 1=女）；动画图引用
    /// HundunWorldGame 中预加载使用的 AnimationGraph 资产。
    /// </para>
    /// <para>
    /// 支持鼠标左键水平拖拽旋转角色（约 0.01 弧度/像素）。
    /// 实现 <see cref="IInkPage"/> 接口，由外部布局系统调用 <see cref="RefreshLayout"/>。
    /// </para>
    /// </summary>
    public class CharacterPreview3D : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局 / 相机常量
        // =======================================================================

        /// <summary>默认控件宽度</summary>
        private const float DefaultWidth = 440f;

        /// <summary>默认控件高度</summary>
        private const float DefaultHeight = 600f;

        /// <summary>
        /// 取景完成前的基线相机距离（cm）。模型就绪后由 <c>TryAutoFrame</c> 按实际包围盒重算，
        /// 所以换不同身高的身体资产不需要改这里。
        /// </summary>
        private const float CameraDistance = 250f;

        /// <summary>
        /// 预览台在世界空间中的落点（远离原点）。
        /// <para>
        /// 预览用的 Camera/AnimatedModel 是**生成进宿主场景**的普通 Actor，
        /// 而创角/选角场景补建的主相机就在 <c>(0,150,-300)</c> 正对原点；预览控件又是透明底
        /// （透出主视口），于是主相机会把同一具模型再画一遍——表现为预览区里一圈巨大的
        /// 半透明重影（离主相机最近的那份还被近平面裁成方块）。挪到主相机远平面之外即可：
        /// 预览自己的相机与模型同步平移，取景、拖拽、扶正都不受影响。
        /// 1 千米远是刻意取的量级——本工程所有相机（含未显式设置 FarPlane 的默认值 2 万厘米）
        /// 都不可能把它画进画面，而相对几何仍是米级，不触碰浮点精度下限。
        /// </para>
        /// </summary>
        private static readonly Vector3 StageOffset = new Vector3(0f, -100000f, 0f);

        /// <summary>预览相机的注视点：舞台点 + 取景中心高度（由 TryAutoFrame 按模型实测）</summary>
        private Vector3 StageFocus => StageOffset + new Vector3(0f, _fitCenterY, 0f);

        /// <summary>相机高度偏移（cm，略高于角色中心，轻微俯视更自然）</summary>
        private const float CameraHeight = 120f;

        /// <summary>相机注视点高度（角色中心约 90cm，而非脚底）</summary>
        private const float LookAtHeight = 90f;

        /// <summary>相机视野角度（度）</summary>
        private const float CameraFieldOfView = 45f;

        /// <summary>相机近裁剪面</summary>
        private const float CameraNearPlane = 1f;

        /// <summary>相机远裁剪面</summary>
        private const float CameraFarPlane = 5000f;

        /// <summary>水平拖拽旋转灵敏度（弧度/像素，约 0.01）</summary>
        private const float DragYawSensitivity = 0.01f;

        /// <summary>垂直拖拽灵敏度（度/像素）——控制相机绕"角色左右轴"的俯仰查看角</summary>
        private const float DragPitchSensitivity = 0.12f;

        /// <summary>轮询通道起转阈值（像素）：按下后位移超过它才算拖拽，避免点击被当成旋转</summary>
        private const float DragArmPx = 4f;

        /// <summary>相机俯仰查看角下限（度）：禁止从下往上仰视（防穿帮/全身出框）</summary>
        private const float CameraPitchMinDeg = -18f;

        /// <summary>相机俯仰查看角上限（度）：限制俯视角度，保证全身完整入画</summary>
        private const float CameraPitchMaxDeg = 26f;

        /// <summary>
        /// 相机离台面的最小净空（cm）：再低就会钻进台面里（穿模）。
        /// 该值由 <see cref="UpdateCameraTransform"/> 反解成仰角下限，与俯仰/拉远联动。
        /// </summary>
        private const float CameraFloorClearance = 12f;

        /// <summary>
        /// 模型俯仰校正（度，绕 X 轴），与 <see cref="ModelYawCorrection"/> 组合使用。
        /// 实测（恒等姿态下截图）：现役身体资产（Character_1/2，UE 系 FBX 重导入）原始为
        /// "上下颠倒站立、正面朝 -Z"，即头朝 -Y、脚朝 +Y。Rx(180°) 把头翻正的同时把 -Z 翻成 +Z，
        /// 因此单独使用即可正对相机。
        /// 若换骨架/换资产后出现躺倒或背面朝向，按截图二分：躺倒→调本值，背面→调 <see cref="ModelYawCorrection"/>。
        /// </summary>
        private const float ModelPitchCorrection = 180f;

        /// <summary>
        /// 正面朝向校正（度，绕垂直轴）。
        /// <para>
        /// 历史值 180° 是给已删除的 <c>skm_uefn_mannequin</c> 标定的（那具资产原始正面朝 +Z，
        /// 需要 Ry(180)·Rx(180)=Rz(180) 才能"头翻正且面部不变"）。
        /// 2026-09-27 换用 Character_1/2 后截图显示角色直立但**背对相机**：
        /// Rz(180) 不改变 Z 轴，说明该资产原始正面本就朝 -Z，于是绕垂直轴的额外 180° 是多余的，
        /// 归零后由 <see cref="ModelPitchCorrection"/> 单独完成扶正 + 转向。
        /// </para>
        /// </summary>
        private const float ModelYawCorrection = 0f;

        /// <summary>角色模型资产 GUID（来自 HundunWorldGame.cs）</summary>
        /// <remarks>
        /// 2026-09 修正：原写死的 ceded67f4bb2623f40b4dcb493b0d419 是**错误的 GUID**
        /// （Content/Character/Models/Animation Graph.flax 头部实际 ID 为
        /// ceded67f623f4bb2b4dcb44019d4b093），会导致 Content.Load 报
        /// "Invalid or missing asset (... AnimationGraph)"。现与资产真实 GUID 对齐。
        /// </remarks>
        private static readonly Guid CharacterAnimGraphGuid = new Guid("ceded67f623f4bb2b4dcb44019d4b093");

        /// <summary>角色 AnimationGraph 资产路径</summary>
        private const string CharacterAnimGraphPath = "Content/Character/Models/Animation Graph.flax";

        // ===================================================================
        // 渲染资源
        // =======================================================================

        /// <summary>离屏渲染目标纹理</summary>
        private GPUTexture _renderTexture;

        /// <summary>用于将 RenderTexture 绘制到 UI 的画笔</summary>
        private GPUTextureBrush _textureBrush;

        /// <summary>离屏渲染任务（仅渲染 CustomActors）</summary>
        private SceneRenderTask _renderTask;

        // ===================================================================
        // 场景 Actor
        // =======================================================================

        /// <summary>承载 Camera 的根 Actor（便于统一清理）</summary>
        private EmptyActor _cameraRoot;

        /// <summary>离屏相机</summary>
        private Camera _camera;

        /// <summary>承载 AnimatedModel 的根 Actor</summary>
        private EmptyActor _modelRoot;

        /// <summary>角色蒙皮模型组件</summary>
        private AnimatedModel _animatedModel;

        /// <summary>
        /// 头部蒙皮模型组件（MetaHuman 头，独立一具 Actor）。
        /// <para>
        /// 头与身体是**两套骨架**（身体 = UE mannequin 系；头 = MetaHuman FACIAL 系），
        /// 不能把脸当子网格蒙皮到身体上。在加它之前，创角预览里只有身体 ⇒
        /// "头部 120 项"滑杆没有可见对象（用户实测 + 代码核实，见方案文档 §170）。
        /// </para>
        /// <para>
        /// 它**不参与**身体的换绑/幂等链（`_appliedBodyModel` 那套）：那是身体专属的竞态保护，
        /// 把头混进去会让"换性别"和"装头"互相误判成对方的重复请求。
        /// </para>
        /// </summary>
        private AnimatedModel _headModel;

        /// <summary>预览用头发卡片：挂在 Ada 头下、零变换，与世界侧同一条约定（HundunWorldGame 世界头发段）。</summary>
        private StaticModel _previewHair;
        private MaterialInstance _previewHairMat;
        private StaticModel _previewBrows;
        private MaterialInstance _previewBrowMat;
        private int _previewHairColorIndex;

        /// <summary>已装上的头部模型（幂等判定用）</summary>
        private SkinnedModel _appliedHeadModel;

        /// <summary>头的骨骼对齐是否已做过（换绑头/身模型时重置）。</summary>
        private bool _headAligned;

        /// <summary>几何自证日志是否已打过（一次性的诊断，查完可删）。</summary>
        private bool _geometryReported;

        /// <summary>预览里的头部组件（供创角捏脸页把 MetaHumanCharacter 绑到"真正会被渲染的那具头"上）。</summary>
        public AnimatedModel HeadModel => _headModel;

        /// <summary>
        /// 预览的离屏渲染任务（<c>Output</c> 是那张 RenderTexture）。
        /// 暴露给编辑器桥接做"把预览画面存成 PNG"——不然自动化验证拿不到这几具模型的像素
        /// （<c>game_screenshot</c> 只截游戏视口，不含 UI 画布与离屏预览）。
        /// </summary>
        public SceneRenderTask PreviewRenderTask => _renderTask;

        /// <summary>
        /// 把本预览当前真正在渲染的蒙皮模型（身体 + 头）收进 <paramref name="into"/>，捏脸写回目标就用这一批。
        /// <para>
        /// 不要在场景里按名字搜：2026-10-19 实测那样收进来四具，其中两具已销毁、一具骨架完全对不上
        /// （每轮有 1500 个骨骼名查不到），结果是"身体参数写到头上、脸反而不动"。组件自己知道它建了谁，
        /// 这是唯一不会收错的来源。
        /// </para>
        /// </summary>
        public void CollectSkinnedModels(List<AnimatedModel> into)
        {
            if (into == null)
            {
                return;
            }

            // 用 Flax 的 != ：已销毁的 Actor 按"没有"算；SkinnedModel 为空的不收（写了也只会被断言挡下）。
            // 蒙皮模板身体就位时，**不要**再把游戏角色身体收进来：它是另一套骨架（实测只解析出 12 根骨、
            // 每轮 1500+ 个骨骼名查不到），白付一次整姿求值还写不准确。
            bool skinnedTemplateReady = _skinnedBody != null && _skinnedBody.SkinnedModel != null
                                        && _appliedSkinnedBody != null;
            if (!skinnedTemplateReady && _animatedModel != null && _animatedModel.SkinnedModel != null
                && !into.Contains(_animatedModel))
            {
                into.Add(_animatedModel);
            }

            // 只有蒙皮身体真正显示时才是写回目标：隐藏时不写，免得把参数写进一具看不见的模型。
            if (_skinnedBodyVisible && _skinnedBody != null && _skinnedBody.SkinnedModel != null
                && !into.Contains(_skinnedBody))
            {
                into.Add(_skinnedBody);
            }

            if (_headModel != null && _headModel.SkinnedModel != null && !into.Contains(_headModel))
            {
                into.Add(_headModel);
            }
        }

        /// <summary>
        /// 基础模板身体组件（conform topology，静态网格、无骨架）。
        /// <para>
        /// 与 <see cref="_animatedModel"/> 是**互斥的两条身体来源**：模板身体开启时把它隐掉，
        /// 因为 `Character_1/2` 自带脑袋，会遮住 MetaHuman 的脸（见方案文档 §170）。
        /// </para>
        /// </summary>
        private StaticModel _templateBody;

        /// <summary>
        /// 模板身体的**蒙皮版**（<c>Ada_BodyMesh.flax</c>：211 根骨、13,164 顶点、94.9cm）。
        /// 静态模板身体是 <c>Model</c>，对骨骼/形状参数毫无反应 —— 身体那 30 项参数要真正生效，
        /// 必须有一具带 MetaHuman 骨架的蒙皮身体（2026-10-19，实测静态身体承载 0/598）。
        /// </summary>
        private AnimatedModel _skinnedBody;

        private SkinnedModel _appliedSkinnedBody;

        /// <summary>是否显示蒙皮身体（身体页签）。头页签必须关掉它：它自带一颗头，会把脸挡住。</summary>
        private bool _skinnedBodyVisible;

        /// <summary>已装上的模板身体模型（幂等判定用）</summary>
        private Model _appliedTemplateBody;

        /// <summary>
        /// 模板头部组件（与 <see cref="_templateBody"/> 同代同度量系 ⇒ 同一变换即无缝）。
        /// 它是静态网格，所以只能走 StaticModel；此时 Ada 脸（<see cref="_headModel"/>）让位。
        /// </summary>
        private StaticModel _templateHead;

        /// <summary>已装上的模板头模型（幂等判定用）</summary>
        private Model _appliedTemplateHead;

        /// <summary>是否用模板身体取代 <see cref="_animatedModel"/> 作为可见身体（由调用页决定）。</summary>
        private bool _templateBodyVisible;

        /// <summary>是否用模板头取代 <see cref="_headModel"/>（Ada 脸）。默认关，见 <see cref="SetTemplateHeadVisible"/>。</summary>
        private bool _templateHeadVisible;

        /// <summary>
        /// 是否允许显示按性别取的完整角色身体（<see cref="_animatedModel"/>）。
        /// 头页签要关掉：它自带一颗头，会和 Ada 脸叠在一起。
        /// </summary>
        private bool _animatedBodyVisible = true;

        /// <summary>是否允许显示 Ada 脸（<see cref="_headModel"/>）。身体页签要关掉，只留按性别的完整身体。</summary>
        private bool _metaHumanHeadVisible = true;

        /// <summary>
        /// 完整 MetaHuman 外观模式：蒙皮身体与 Ada 头**同屏**，头用身体的姿态（SetMasterPoseModel）。
        /// 默认关：身体页签按下述老规则把 Ada 头收起（蒙皮身体没头，头会像灰蛋浮在颈口）。
        /// </summary>
        private bool _metaHumanLookMode;
        private bool _headMasterPosed;

        /// <summary>灯光根 Actor（承载三点光照，不随模型旋转）</summary>
        private EmptyActor _lightRoot;

        /// <summary>主光源（方向光）</summary>
        private DirectionalLight _keyLight;

        /// <summary>补光（方向光）</summary>
        private DirectionalLight _fillLight;

        /// <summary>轮廓光（方向光）</summary>
        private DirectionalLight _rimLight;

        // ===================================================================
        // 宿主场景与背景板
        // =======================================================================

        /// <summary>预览 Actor 所属的宿主场景（场景卸载断链判定与背景板定位用）</summary>
        private FlaxEngine.Scene _hostScene;

        /// <summary>舞台根（天幕等环境 Actor 的父节点，独立于人物转盘，不随拖拽旋转）。</summary>
        private EmptyActor _stageRoot;

        /// <summary>天幕的整块模型与唯一一份材质实例（进程级共享）。</summary>
        private static Model _stageSkyModelCache;
        private static MaterialInstance _stageSkyMatCache;

        /// <summary>
        /// 取景距离系数。1 = 自动取景（全身完整入画、留 18% 边距）；
        /// 小于 1 把相机推近做头肩特写。默认 1，对既有调用方完全等价于原行为。
        /// </summary>
        public float FramingScale = 1f;

        /// <summary>
        /// 取景瞄准高度占身高的比例。0.5 = 躯干中央（全身构图）；约 0.78 对准头肩。
        /// 仅在 <see cref="FramingScale"/> 推近后才明显影响构图。默认 0.5 即原行为。
        /// </summary>
        public float FramingHeightFrac = 0.5f;

        /// <summary>
        /// 取景俯仰偏移（度），叠加在相机基线仰角上：只改"看地面的角度"，不改变焦点在画面里的位置。
        /// 全身页用它把地平线压到脚踝附近（截图的构图是"大片暗天 + 低地平线"）。
        /// </summary>
        public float FramingPitchDeg = 0f;

        /// <summary>
        /// 水平取景偏移（相对视口宽度的比例）：正值把角色让到视口中心右侧。
        /// 全屏铺满的选角 / 创角页用它把立绘摆在右半，左半留给操作栏。默认 0 = 居中。
        /// </summary>
        public float FramingOffsetX = 0f;

        // ===================================================================
        // 交互状态
        // =======================================================================

        /// <summary>鼠标左键是否正在拖拽</summary>
        private bool _isDragging;

        /// <summary>是否由<b>轮询通道</b>接管拖拽（控件事件被压暗层/UI 路由挡掉时的兜底）</summary>
        private bool _isPollDragging;

        /// <summary>轮询按下后的起点：位移超过 <see cref="DragArmPx"/> 才真正开始转，纯点击不转</summary>
        private Float2 _pollDragStart;

        /// <summary>本次按下是否已经越过起转阈值</summary>
        private bool _pollDragArmed;

        /// <summary>上一帧鼠标位置（用于计算水平增量）</summary>
        private Float2 _lastMousePosition;

        /// <summary>最近一次 UI 鼠标移动事件时间（秒）——用于判定事件通道是否存活，决定轮询是否接管</summary>
        private float _lastEventMoveTime;

        /// <summary>诊断日志：拖拽启动是否已记录（仅一次）</summary>
        private bool _loggedDragStart;

        /// <summary>诊断日志：左右旋转生效是否已记录（仅一次）</summary>
        private bool _loggedYawApplied;

        /// <summary>骨骼基础姿态缓存（骨骼名 → 绑定姿态矩阵）：每帧在基准上叠加微小动态，避免累积漂移</summary>
        private readonly System.Collections.Generic.Dictionary<string, Matrix> _idleBasePose = new System.Collections.Generic.Dictionary<string, Matrix>();

        /// <summary>基础姿态是否已捕获</summary>
        private bool _idlePoseCaptured;

        /// <summary>诊断日志：待机动画已实际应用是否已记录（仅一次）</summary>
        private bool _loggedIdleApplied;

        /// <summary>
        /// 待机动画的骨骼槽位：每槽列出多套骨架的候选名（Rigify / UE 人偶 / Rigged Human / Bip001）。
        /// <para>
        /// 之所以按槽解析而非写死一组名字：换身体资产时最常见的失败是"模型正常显示、
        /// 微动画静默消失"，因为原实现只认 UE 命名（<c>spine_01</c>…），
        /// 命中不到就直接跳过，没有任何提示。解析发生在捕获基准姿态时，一次性的。
        /// </para>
        /// <para>
        /// 现役身体资产（Character_2）是 Blender **Rigify** 骨架，形变骨统一带 <c>DEF-</c> 前缀，
        /// 脊柱按 <c>DEF-spine</c>(骨盆)→<c>.001/.002/.003</c>(胸)→<c>.004</c>(上胸)→
        /// <c>.005</c>(颈)→<c>.006</c>(头) 编号。Rigify 同时导出大量非形变的控制骨
        /// （<c>spine</c>、<c>chest</c>、<c>head</c>、<c>*_fk.*</c>、<c>ORG-</c>、<c>MCH-</c>…），
        /// 它们也在 <c>SkinnedModel.Nodes</c> 里，驱动它们不会让网格动一根手指——所以 <c>DEF-</c>
        /// 候选必须排在前面，否则会被同名控制骨抢先命中（实测 7 槽只命中 3 槽，预览近乎静止）。
        /// </para>
        /// <para>
        /// <c>Character_1</c> 的形变骨已在 2026-09-30 被 <c>skeleton_rename</c> 工具重命名为
        /// 动画剪辑的通道名（<c>Hips/Spine1/Spine2/Neck/Head/LeftArm/RightArm/LeftHand…</c>），
        /// 因此这里必须同时带上这组 Mixamo 命名候选；两具身体共用一套槽位，缺一组就另一组失效。
        /// </para>
        /// </summary>
        private static readonly string[][] IdleBoneSlots =
        {
            new[] { "DEF-spine", "hips", "spine_01", "pelvis", "bip001 spine", "mixamorig:spine" },
            new[] { "DEF-spine.002", "spine1", "spine_02", "bip001 spine1", "mixamorig:spine1" },
            new[] { "DEF-spine.003", "spine2", "spine_03", "bip001 spine2", "mixamorig:spine2" },
            new[] { "DEF-spine.005", "neck", "neck_01", "bip001 neck", "mixamorig:neck" },
            new[] { "DEF-spine.006", "head", "bip001 head", "mixamorig:head" },
            new[] { "DEF-upper_arm.L", "leftarm", "upperarm_l", "left_upperarm", "bip001 l upperarm", "mixamorig:leftupperarm" },
            new[] { "DEF-upper_arm.R", "rightarm", "upperarm_r", "right_upperarm", "bip001 r upperarm", "mixamorig:rightupperarm" },
        };

        /// <summary>各槽位的摆动频率（秒^-1 量纲，与 <see cref="IdleBoneSlots"/> 一一对应）</summary>
        private static readonly float[] IdleSlotFrequency = { 0.35f, 0.35f, 0.35f, 0.22f, 0.22f, 0.30f, 0.30f };

        /// <summary>各槽位的摆幅（度）</summary>
        private static readonly float[] IdleSlotAmplitude = { 0.60f, 1.10f, 0.70f, 0.90f, 1.40f, 2.20f, 2.20f };

        /// <summary>各槽位的相位（让脊椎逐节错开、双臂反相，避免整身同步摆动像木偶）</summary>
        private static readonly float[] IdleSlotPhase = { 0f, 0f, 0.942f, 1.571f, 5.341f, 0f, 3.142f };

        /// <summary>槽位 → 当前骨架实际命中的骨骼名（null 表示这根骨骼不存在）</summary>
        private readonly string[] _idleResolvedBones = new string[IdleBoneSlots.Length];

        /// <summary>当前模型 Yaw 旋转（弧度）</summary>
        private float _modelYaw;

        /// <summary>
        /// 相机俯仰查看角（度）——相机绕"角色左右轴"（过注视点的水平左右方向轴）旋转的偏移角，
        /// 限制在 [<see cref="CameraPitchMinDeg"/> , <see cref="CameraPitchMaxDeg"/>]，保证全身完整入画。
        /// </summary>
        private float _cameraPitchDeg;

        /// <summary>站立巡检计时（秒）——用于发现"模型躺倒"异常并自愈</summary>
        private float _uprightCheckTimer;

        /// <summary>是否已完成站立巡检（只自愈一次）</summary>
        private bool _uprightVerified;

        /// <summary>是否已完成自动取景（依据模型实际包围盒；未取景时使用原基线）</summary>
        private bool _framed;

        /// <summary>临时诊断计数（虚影定位用，查完即删）：Update 帧数 / 存活实例数 / 实际换绑次数</summary>
        private int _diagFrames;
        private static int _liveInstances;
        private int _rebindCount;

        /// <summary>自动取景计时（秒）——等待绑定姿态生效后再测量包围盒</summary>
        private float _fitTimer;

        /// <summary>资产 LOD0 包围盒取不到时只报一次警告（见 <see cref="SkinnedWorldBox"/>）。</summary>
        private bool _lodBoxWarned;

        /// <summary>取景半径（取景中心到相机的距离）——自动取景前为原基线 251.8</summary>
        private float _fitDistance = Mathf.Sqrt((CameraHeight - LookAtHeight) * (CameraHeight - LookAtHeight) + CameraDistance * CameraDistance);

        /// <summary>
        /// 取景中心相对舞台的高度偏移（世界 +Y）。
        /// **注意方向**：预览的离屏 RenderTexture 竖直翻转出图 ⇒ 世界 +Y 在屏幕上是往下，
        /// 扶正过的模型脚落在世界包围盒的**最大 Y**那一端。所以这个量由
        /// <see cref="TryAutoFrame"/> 按"离脚底多高、朝屏幕上方"反算出来，不是直接往上加。
        /// </summary>
        private float _fitCenterY = LookAtHeight;

        /// <summary>自动取景算出的基准相机距离；滚轮缩放围绕它上下钳制，重取景时重置。</summary>
        private float _baseFitDistance;

        /// <summary>自动取景时量到的可见竖向高度（cm）：分区取景按它的比例算焦点高度。</summary>
        private float _visibleHeight;

        /// <summary>分区取景的目标距离与焦点高度（平滑过渡用，<see cref="Update"/> 每帧朝它们靠）。</summary>
        private float _targetFitDistance;
        private float _targetCenterY;

        private bool _focusAnimating;

        /// <summary>分区取景的一个档：焦点高度占可见高度的比例 + 取景距离倍数。</summary>
        private struct FocusPreset
        {
            public float HeightFrac;
            public float Zoom;
        }

        /// <summary>滚轮缩放的下限/上限（相对 <see cref="_baseFitDistance"/> 的倍数）。</summary>
        private const float ZoomMinScale = 0.35f;
        private const float ZoomMaxScale = 2.6f;

        /// <summary>滚轮缩放只自证一次（首次生效时打一行日志，供"事件到底到没到控件"取证）。</summary>
        private bool _loggedWheel;

        /// <summary>相机基线仰角（度）——略高于角色中心，轻微俯视更自然</summary>
        private const float CameraBaseElevationDeg = 6.8f;

        /// <summary>最近一次取景所用的模型资源（用于资源切换后自动重取景）</summary>
        private SkinnedModel _lastFramedModel;

        /// <summary>
        /// 模型局部居中偏移（水平居中 + 脚底贴地）。
        /// 关键：必须施加在**模型自身**（_animatedModel.Position）而非父节点——
        /// 父节点 _modelRoot 是 Yaw 自转的载体，其原点即旋转轴，须恰好穿过角色自身垂直中轴线。
        /// </summary>
        private Vector3 _modelLocalOffset;

        // ===================================================================
        // 水墨微粒（少量环境粒子）
        // =======================================================================

        /// <summary>水墨微粒（无状态：位置/透明度由绝对时间推导，无需逐帧累积更新）</summary>
        private struct InkMote
        {
            public float XFrac;      // 基准水平位置（0~1，相对宽度）
            public float YFrac0;     // 初始垂直位置（0~1，相对高度）
            public float RiseSpeed;  // 上升漂移速度（每秒高度的比例）
            public float SwayAmp;    // 水平摆动幅度（像素）
            public float SwayFreq;   // 摆动频率（Hz）
            public float Phase;      // 随机相位
            public float Size;       // 粒子半径（像素）
            public float BaseAlpha;  // 基准不透明度
            public Color Tint;       // 粒子颜色
        }

        /// <summary>水墨微粒数组（少量，约 16 个）</summary>
        private InkMote[] _motes;

        /// <summary>微粒随机源（固定种子，多次打开观感一致）</summary>
        private readonly System.Random _moteRandom = new System.Random(20260723);

        /// <summary>是否已初始化渲染资源（避免重复初始化）</summary>
        private bool _initialized;

        /// <summary>透明背景模式开关（嵌入宿主容器时使用）</summary>
        private bool _transparentBackground;

        /// <summary>待应用的角色组件（场景/Actor 未就绪时暂存，初始化完成后重放）</summary>
        private CharacterAttributesComponent _pendingCharacter;

        /// <summary>待应用的性别（同上，供创角/选角在 Actor 就绪前设置）</summary>
        private int _pendingGender;

        /// <summary>是否有一次暂存的性别绑定等待重放</summary>
        private bool _hasPendingGender;

        /// <summary>当前预览所使用的身体性别（0=男，1=女）；无角色 Actor 时用它取默认模型</summary>
        private int _bodyGender;

        /// <summary>已经绑到 <see cref="_animatedModel"/> 上的身体模型（幂等判定用）</summary>
        private SkinnedModel _appliedBodyModel;

        /// <summary>已应用身体所对应的性别；-2 表示"不是按性别来的"（角色面板直接绑了世界 Actor 的模型）</summary>
        private int _appliedBodyGender = -2;

        /// <summary>在途的身体模型请求（非阻塞轮询；null 表示没有在途）</summary>
        private SkinnedModel _pendingBodyModel;

        /// <summary>在途请求发起时的性别，用于丢弃被更新请求覆盖的迟到结果</summary>
        private int _pendingBodyGender;

        /// <summary>在途请求已等待的秒数（超时后放弃，避免永远挂着一个不就绪的资产）</summary>
        private float _pendingBodyElapsed;

        /// <summary>身体模型异步加载的等待上限（秒）</summary>
        private const float BodyLoadTimeoutSeconds = 20f;

        /// <summary>
        /// 透明背景模式：跳过自身背景填充与描边，由宿主容器（如 ModelStage）
        /// 提供底色与边框，渲染纹理的透明区域透出宿主背景。
        /// </summary>
        public bool TransparentBackground
        {
            get => _transparentBackground;
            set
            {
                _transparentBackground = value;
                BackgroundColor = value ? Color.Transparent : InkWashTheme.BaseDefault;
            }
        }

        /// <summary>
        /// 是否已就绪（渲染资源与角色模型均已建立）。
        /// 宿主容器可据此隐藏 2D 占位提示，切换为真实 3D 预览。
        /// </summary>
        public bool IsReady => _initialized
                               && _renderTexture != null
                               && _animatedModel != null
                               && _animatedModel.SkinnedModel != null;

        // ===================================================================
        // 构造函数
        // =======================================================================

        /// <summary>
        /// 构造函数：默认尺寸 440x600，深墨黑背景，金色描边。
        /// 在构造时尝试初始化离屏渲染管线；若场景未就绪则推迟到 <see cref="RefreshLayout"/>。
        /// </summary>
        public CharacterPreview3D()
        {
            _liveInstances++; // 临时诊断（虚影定位，查完即删）
            Size = new Float2(DefaultWidth, DefaultHeight);
            BackgroundColor = InkWashTheme.BaseDefault;
            ClipChildren = true;
            AutoFocus = false;

            try
            {
                InitializeRenderTarget();
                InitializeActors();
                // 仅当 Actor 真正创建成功时才标记为已初始化，否则交由 RefreshLayout 重试
                _initialized = _cameraRoot != null && _modelRoot != null;
                FlaxEngine.Debug.Log($"[CharacterPreview3D] 初始化完成，_initialized={_initialized}");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CharacterPreview3D] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // IInkPage 实现
        // =======================================================================

        /// <summary>
        /// 基于当前控件实际尺寸刷新布局。
        /// 重建 <see cref="GPUTexture"/> 分辨率（匹配控件尺寸），
        /// 重新计算相机位置（保持角色居中、距离约 200、FOV 45 度）。
        /// </summary>
        public void RefreshLayout()
        {
            try
            {
                // 若未初始化或 Actor 未创建，且场景已就绪，则重试 InitializeActors
                if (!_initialized || _cameraRoot == null || _modelRoot == null)
                {
                    var scene = GetTargetScene();
                    if (scene != null)
                    {
                        FlaxEngine.Debug.Log("[CharacterPreview3D] RefreshLayout 触发 InitializeActors 重试");
                        InitializeActors();
                        if (_cameraRoot != null && _modelRoot != null)
                        {
                            _initialized = true;
                            FlaxEngine.Debug.Log("[CharacterPreview3D] RefreshLayout 重试 InitializeActors 成功");
                        }
                        else
                        {
                            FlaxEngine.Debug.LogWarning("[CharacterPreview3D] RefreshLayout 重试后 Actor 仍为 null");
                        }
                    }
                    else
                    {
                        FlaxEngine.Debug.LogWarning("[CharacterPreview3D] RefreshLayout 场景仍未就绪，跳过初始化");
                    }
                }

                RebuildRenderTarget();
                UpdateCameraTransform();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CharacterPreview3D] RefreshLayout 失败: {ex.Message}");
            }
        }

        // ===================================================================
        // 公共 API
        // =======================================================================

        /// <summary>
        /// 绑定真实角色数据。读取组件所属 Actor 上的 <see cref="AnimatedModel"/>
        /// 的 <see cref="AnimatedModel.SkinnedModel"/> / <see cref="AnimatedModel.AnimationGraph"/>；
        /// 若组件为 null 或无可用模型，则使用默认模型。
        /// </summary>
        /// <param name="component">角色属性组件，可为 null</param>
        public void SetCharacter(CharacterAttributesComponent component)
        {
            try
            {
                // Actor 尚未创建（场景未就绪）：暂存角色数据，待 InitializeActors 完成后重放
                if (_animatedModel == null)
                {
                    _pendingCharacter = component;
                    FlaxEngine.Debug.Log("[CharacterPreview3D] SetCharacter：Actor 未就绪，已暂存角色数据待重放");
                    return;
                }

                SkinnedModel targetSkinnedModel = null;
                AnimationGraph targetAnimGraph = null;
                Vector3 targetScale = Vector3.One;

                if (component != null && component.Actor != null)
                {
                    var source = component.Actor.GetChild<AnimatedModel>();
                    if (source != null)
                    {
                        if (source.SkinnedModel != null && source.SkinnedModel.IsLoaded)
                            targetSkinnedModel = source.SkinnedModel;
                        if (source.AnimationGraph != null && source.AnimationGraph.IsLoaded)
                            targetAnimGraph = source.AnimationGraph;
                        // 外观装备通过整体缩放改变身形；预览必须跟着世界 Actor 走，
                        // 否则面板里还是默认比例，看起来像"外观没生效"。
                        targetScale = source.LocalScale;
                    }
                }

                ApplyModelResources(targetSkinnedModel, targetAnimGraph);

                if (_animatedModel != null && targetScale != Vector3.Zero)
                    _animatedModel.LocalScale = targetScale;

                // 这份模型来自世界 Actor（装备/外观驱动），不是"按性别取的身体资产"：
                // 标记成 -2，之后 SetBodyGender 才不会因为幂等判定而跳过重新绑定。
                if (targetSkinnedModel != null)
                    _appliedBodyGender = -2;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CharacterPreview3D] SetCharacter 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 按性别绑定身体模型。创角 / 选角流程里还没有运行时角色 Actor（本地玩家尚未生成），
        /// <see cref="SetCharacter"/> 拿不到任何模型，因此这条路径直接按性别取身体资产。
        /// </summary>
        /// <param name="gender">0 = 男，1 = 女（与 <c>CharacterInfo.Gender</c> 约定一致）</param>
        public void SetBodyGender(int gender)
        {
            try
            {
                int normalized = gender == 1 ? 1 : 0;
                _bodyGender = normalized;

                if (_animatedModel == null)
                {
                    _pendingGender = normalized;
                    _hasPendingGender = true;
                    return;
                }

                // 幂等：性别没变、身体模型也已经在渲染，就直接返回。
                // 选角列表里多个角色同性别是常态，每次点一行都重跑一遍换肤流程 = 白闪一次。
                if (normalized == _appliedBodyGender
                    && _appliedBodyModel != null
                    && _animatedModel.IsActive)
                {
                    return;
                }

                ApplyModelResources(null, null);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CharacterPreview3D] SetBodyGender({gender}) 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 在途身体模型就绪后再真正换绑（由 <see cref="Update"/> 每帧轮询）。
        /// <para>
        /// 绝不在 <c>IsLoaded == false</c> 的资产上换绑：那会儿几何/贴图还在后台线程上传，
        /// 拿半份资源去 <c>SetupSkinningData</c> 就是"糊面 + 叠影"的直接来源。
        /// </para>
        /// </summary>
        private void TryApplyPendingBodyModel(float deltaTime)
        {
            var asset = _pendingBodyModel;
            if (asset == null) return;

            _pendingBodyElapsed += deltaTime;

            if (asset.LastLoadFailed || _pendingBodyElapsed > BodyLoadTimeoutSeconds)
            {
                FlaxEngine.Debug.LogWarning($"[CharacterPreview3D] 身体模型未就绪" +
                                            $"（失败={asset.LastLoadFailed}, 等待={_pendingBodyElapsed:F1}s），保持当前模型");
                _pendingBodyModel = null;
                return;
            }

            if (!asset.IsLoaded) return;

            // 请求已被更新的性别覆盖：丢弃这次结果，让新请求自己完成（后到的旧结果不许覆盖新选择）
            if (_pendingBodyGender != _bodyGender)
            {
                _pendingBodyModel = null;
                ApplyModelResources(null, null);
                return;
            }

            _pendingBodyModel = null;
            ApplyModelResources(asset, null);
        }

        // ===================================================================
        // 初始化
        // =======================================================================

        /// <summary>
        /// 创建 <see cref="GPUTexture"/> 渲染目标与 <see cref="SceneRenderTask"/>。
        /// 初始分辨率匹配控件当前尺寸。
        /// </summary>
        private void InitializeRenderTarget()
        {
            int width = Mathf.Max(1, (int)Width);
            int height = Mathf.Max(1, (int)Height);

            var desc = GPUTextureDescription.New2D(width, height, PixelFormat.R8G8B8A8_UNorm);
            _renderTexture = new GPUTexture();
            // 注意：GPUTexture.Init 返回 true 表示失败（0=成功），下方 ! 判断是正确的
            if (!_renderTexture.Init(ref desc))
            {
                _textureBrush = new GPUTextureBrush(_renderTexture);

                _renderTask = new SceneRenderTask
                {
                    Order = -100,
                    Output = _renderTexture,
                    Enabled = false,
                };
                FlaxEngine.Debug.Log($"[CharacterPreview3D] RenderTexture 初始化成功 ({width}x{height})");
            }
            else
            {
                FlaxEngine.Debug.LogError($"[CharacterPreview3D] RenderTexture 初始化失败 (Init 返回非 0, {width}x{height})");
            }
        }

        /// <summary>
        /// 重建 RenderTexture 分辨率以匹配控件当前尺寸。
        /// 仅在尺寸有效且发生变化时执行。
        /// </summary>
        private void RebuildRenderTarget()
        {
            if (_renderTexture == null)
            {
                InitializeRenderTarget();
                return;
            }

            int width = Mathf.Max(1, (int)Width);
            int height = Mathf.Max(1, (int)Height);

            if (_renderTexture.Width == width && _renderTexture.Height == height)
                return;

            // Resize 要求纹理已创建
            _renderTexture.Resize(width, height, PixelFormat.Unknown);
        }

        /// <summary>
        /// 创建 Camera Actor 与 AnimatedModel Actor 并加入主场景，
        /// 通过 <see cref="ActorsSources.CustomActors"/> 仅渲染 <see cref="_modelRoot"/>。
        /// </summary>
        private void InitializeActors()
        {
            var scene = GetTargetScene();
            if (scene == null)
            {
                FlaxEngine.Debug.LogWarning("[CharacterPreview3D] 无可用场景，Actor 创建推迟");
                return;
            }

            FlaxEngine.Debug.Log($"[CharacterPreview3D] 场景已就绪: {scene.Name}，开始创建 Actor");

            // ── 相机 Actor ──
            _cameraRoot = new EmptyActor { Name = "CharacterPreview3D_Camera" };
            Level.SpawnActor(_cameraRoot, scene);

            _camera = _cameraRoot.AddChild<Camera>();
            _camera.UsePerspective = true;
            _camera.FieldOfView = CameraFieldOfView;
            _camera.NearPlane = CameraNearPlane;
            _camera.FarPlane = CameraFarPlane;
            // 不启用音频监听，避免覆盖主相机
            // 注：Camera 类未公开 AudioListener 开关，依靠 CustomActors 渲染源隔离

            // ── 模型 Actor ──
            _modelRoot = new EmptyActor { Name = "CharacterPreview3D_Model" };
            Level.SpawnActor(_modelRoot, scene);
            _modelRoot.Position = StageOffset;

            _animatedModel = _modelRoot.AddChild<AnimatedModel>();
            _animatedModel.SkinnedModel = null;
            _animatedModel.AnimationGraph = null;
            // 动画更新保障：离屏预览中模型不被主相机可见，必须开启"离屏也更新动画"，否则姿态不刷新
            _animatedModel.UpdateWhenOffscreen = true;
            // 关掉逐骨骼运动矢量：本工程后处理全局开着 MotionBlur（Graphics Settings.json
            // MotionBlur.Enabled=true, SampleCount=10）。预览相机是静止的，唯一会产出运动矢量的
            // 就是程序化待机的骨骼微动——开着 PerBoneMotionBlur 时，每根动过的骨骼都会被引擎
            // 沿自己的运动方向拖影，双臂与头部被拉成一片半透明的"翅膀"，看着就是人物边缘重影。
            _animatedModel.PerBoneMotionBlur = false;
            // 程序化待机动画：订阅引擎动画求值完成事件（在其后叠加骨骼级微小动态，保证不被覆盖）
            _animatedModel.AnimationUpdated += ApplyProceduralIdle;
            // 姿态强制（扶正 + 正面朝向 + 居中偏移）：统一由 ApplyModelUpright 施加；
            // 与父节点 _modelRoot 上的转盘 Yaw 互不干扰（先扶正/朝向，再绕自身垂直轴旋转）。
            ApplyModelUpright();

            // ── 头部 Actor（MetaHuman 头，独立骨架） ──
            // 挂同一个父节点（_modelRoot）：这样转盘 Yaw 与居中偏移对头和身体一致，
            // 不会再出现"身体转了、头没转"这种两具骨架各自为政的画面。
            _headModel = _modelRoot.AddChild<AnimatedModel>();
            // 名字是**约定**：创角捏脸页按 "CharacterPreview3D_Head" 找它，把 MetaHumanCharacter 绑到
            // 真正会被渲染的那具头上（改这里要同步 CcCreationPages.FindPreviewModelActor）。
            _headModel.Name = "CharacterPreview3D_Head";
            _headModel.SkinnedModel = null;
            _headModel.AnimationGraph = null;
            _headModel.UpdateWhenOffscreen = true;
            _headModel.PerBoneMotionBlur = false;
            ApplyHeadUpright();

            // 外部面部追踪入口（Live Link 等价）：创角预览里也能被追踪驱动。
            // 默认不监听；动画器由本组件按父级链自动找（MetaHumanCharacter 会挂 FaceAnimator）。
            if (_headModel.GetScript<MetaHuman.Runtime.MetaHumanFaceTrackingReceiver>() == null)
            {
                _headModel.AddScript<MetaHuman.Runtime.MetaHumanFaceTrackingReceiver>();
            }

            // 头发：世界侧就是 head 的子 StaticModel、零变换（见 HundunWorldGame 世界头发段），
            // 创角预览此前没有这一步 ⇒ 捏脸全程光头、发色要进世界才看得到。这里复用同一资产与材质参数。
            _previewHair = _headModel.AddChild<StaticModel>();
            _previewHair.Name = "CharacterPreview3D_Hair";
            Model hairAsset = Content.Load<Model>(
                HundunWorld.Game.Equipment.EquipmentDatabase.HairCardsModelPath, 10000.0);
            if (hairAsset != null && hairAsset.IsLoaded)
            {
                _previewHair.Model = hairAsset;
                _previewHair.LocalPosition = Vector3.Zero;
                _previewHair.LocalScale = Vector3.One;
                MaterialBase hairBase = _previewHair.GetMaterial(0);
                if (hairBase != null && hairBase.CreateVirtualInstance() is MaterialInstance hairInst)
                {
                    _previewHairMat = hairInst;
                    _previewHair.SetMaterial(0, hairInst);
                }
                ApplyHairColor();
            }
            else
            {
                Debug.LogWarning("[CharacterPreview3D] 预览头发模型未就绪: "
                                 + HundunWorld.Game.Equipment.EquipmentDatabase.HairCardsModelPath);
            }

            // ── 基础模板身体 Actor（conform topology，静态网格、无骨架） ──
            // 与 _animatedModel 是互斥的两条身体来源：默认不显示模板身体（保持既有行为），
            // 由创角"面容/捏脸"页调 SetTemplateBodyVisible(true) 切过去——那两页看的是脸，
            // 而 _animatedModel 那具角色自带脑袋，会把 MetaHuman 的脸罩住。
            _templateBody = _modelRoot.AddChild<StaticModel>();
            _templateBody.Name = "CharacterPreview3D_TemplateBody";
            _templateBody.Model = null;
            _templateBody.IsActive = false;

            _skinnedBody = _modelRoot.AddChild<AnimatedModel>();
            _skinnedBody.Name = "CharacterPreview3D_SkinnedBody";
            _skinnedBody.SkinnedModel = null;
            _skinnedBody.IsActive = false;
            // 与另外两具同口径：全局 MotionBlur 开着时，大位移的骨骼会被逐骨拖成竖条。
            _skinnedBody.UpdateWhenOffscreen = true;
            _skinnedBody.PerBoneMotionBlur = false;

            // ── 模板头部 Actor（与模板身体同代 ⇒ 严丝合缝；静态网格，故用 StaticModel） ──
            _templateHead = _modelRoot.AddChild<StaticModel>();
            _templateHead.Name = "CharacterPreview3D_TemplateHead";
            _templateHead.Model = null;
            _templateHead.IsActive = false;

            ApplyModelResources(null, null);

            // ── 灯光 Actor（三点光照，保证孤立渲染下模型可见） ──
            CreateLightingRig(scene);

            // ── 舞台根节点与天幕（地平线雾带） ──
            CreateStageRoot(scene);
            CreateStageBackdrop();

            // ── 绑定渲染任务 ──
            _renderTask.Camera = _camera;
            _renderTask.ActorsSource = ActorsSources.CustomActors;
            // 关键修复：CustomActors 孤立渲染不包含场景原有灯光，
            // 必须将灯光组与天幕一并加入渲染列表，否则无光源导致渲染结果全黑 / 天幕不出现
            _renderTask.CustomActors = new Actor[] { _modelRoot, _lightRoot, _headModel, _templateBody, _templateHead, _stageRoot };
            // 背景不再借用场景 Sky：天空来源取决于"此刻加载着哪些场景"，
            // 进了游戏世界就会拿到白天天空，UI 罩子怎么叠都压不住那股蓝。
            // 舞台是"天幕雾光 + 角色"，不再铺地台（用户 2026-10-28 要求移除地面）。
            // 关掉镜头光晕类后处理：场景高光会在天幕上打出一大团鬼影（实测画面上方一片 3 倍亮度的光斑），
            // 预览底图只要干净的雾光，不要镜头感。CameraArtifacts 留着——里面的抖动（dither）
            // 正是压掉大块渐变上 1 级色阶横纹的关键。
            _renderTask.ViewFlags &= ~(ViewFlags.Bloom | ViewFlags.LensFlares | ViewFlags.EyeAdaptation);
            _renderTask.Enabled = true;

            // 宿主场景断链防护：场景卸载（退世界/切场景/PIE停止）会销毁预览 Actor，
            // 而 SceneRenderTask 白名单里的原生指针不会自动清除，下一帧 DrawAll 解引用
            // 野指针即崩溃（Array.h Last() 断言 / 0xe06d7363）。见 OnLevelSceneUnloading。
            _hostScene = scene;
            Level.SceneUnloading -= OnLevelSceneUnloading; // InitializeActors 可能重试，防重复订阅
            Level.SceneUnloading += OnLevelSceneUnloading;

            UpdateCameraTransform();

            // 重放场景就绪前暂存的角色绑定
            if (_pendingCharacter != null)
            {
                var pending = _pendingCharacter;
                _pendingCharacter = null;
                SetCharacter(pending);
            }
            else if (_hasPendingGender)
            {
                var gender = _pendingGender;
                _hasPendingGender = false;
                SetBodyGender(gender);
            }

            FlaxEngine.Debug.Log("[CharacterPreview3D] InitializeActors 成功");
        }

        /// <summary>
        /// 创建三点光照组并加入目标场景。
        /// <para>
        /// CustomActors 孤立渲染模式下，场景原有灯光不参与渲染，
        /// 必须将自建光源一并加入 <see cref="SceneRenderTask.CustomActors"/>，否则渲染结果一片漆黑。
        /// </para>
        /// <para>
        /// 光照口径改为与工作台场景同源：主光就是场景里的 Sun
        /// （Orientation=Euler(-50,20,0)、暖白 (1,0.97,0.9)、Brightness=1.3）。
        /// 场景里角色还有 SkyLight(2.5)+天空环境光托底，而离屏孤立渲染没有 GI/SkyLight，
        /// 直接照搬 1.3 会让人物几乎全黑 —— 所以主光按比例放大，并用一盏冷调"天光"补正面，
        /// 主辅比与场景的 暖主光 : 冷天光 一致。数值由截图实测标定（见 3d-2 记忆的光照标定法）。
        /// </para>
        /// </summary>
        /// <param name="scene">目标场景</param>
        private void CreateLightingRig(FlaxEngine.Scene scene)
        {
            _lightRoot = new EmptyActor { Name = "CharacterPreview3D_Lights" };
            Level.SpawnActor(_lightRoot, scene);

            // 光向约定：DirectionalLight 沿自身前向（+Z）照射；相机在 +Z 侧、角色正面朝 +Z，
            // 因此 yaw≈180° 的光照射角色正面，yaw≈0° 的光照背面。

            // 主光：相机前上方偏左（yaw 160°、俯角 -45°）—— 正面主体受光。
            // 色温取工作台 Sun 的暖白，强度按孤立渲染（无 GI/SkyLight）实测标定：
            // 纯背光位（Euler(-50,20,0)）下 MetaHuman 脸只有 1~7/255，捏脸页不可用，故主光留在正面。
            _keyLight = _lightRoot.AddChild<DirectionalLight>();
            _keyLight.Name = "PreviewKeyLight";
            _keyLight.Orientation = Quaternion.Euler(-45f, 160f, 0f);
            _keyLight.Color = new Color(1f, 0.97f, 0.90f);
            _keyLight.Brightness = 7.0f;

            // 天光（代替场景 SkyLight 2.5）：正面偏右、低角度，冷灰蓝，压住主光的硬阴影
            _fillLight = _lightRoot.AddChild<DirectionalLight>();
            _fillLight.Name = "PreviewFillLight";
            _fillLight.Orientation = Quaternion.Euler(-15f, 200f, 0f);
            _fillLight.Color = new Color(0.80f, 0.85f, 0.92f);
            _fillLight.Brightness = 1.8f;

            // 轮廓光：背后上方，勾肩线/头顶，把人物从暗天里剥出来
            _rimLight = _lightRoot.AddChild<DirectionalLight>();
            _rimLight.Name = "PreviewRimLight";
            _rimLight.Orientation = Quaternion.Euler(-30f, 0f, 0f);
            _rimLight.Brightness = 0.8f;
            _rimLight.Color = new Color(0.90f, 0.92f, 1f);

            FlaxEngine.Debug.Log("[PreviewStage] workbench-v5 lighting ready: key=7.0 sky=1.8 rim=0.8");
        }

        // ===================================================================
        // 工作台舞台（取自 Content/SceneData/_monster_prefab_workbench.scene）
        // =======================================================================

        /// <summary>
        /// 舞台根节点：天幕挂在这下面，相机贴地限位也以它的高度为"地面"基准。
        /// <para>
        /// 这里<b>不再铺地台</b>（2026-10-28 用户要求移除地面）：角色直接立在天幕雾光前。
        /// 地台当年是为了工作台截图的"台面"观感，移除后台面投影也一并消失，画面只剩角色 + 雾。
        /// </para>
        /// </summary>
        private void CreateStageRoot(FlaxEngine.Scene scene)
        {
            _stageRoot = new EmptyActor { Name = "CharacterPreview3D_Stage" };
            Level.SpawnActor(_stageRoot, scene);
            _stageRoot.Position = StageOffset;
        }

        /// <summary>
        /// 舞台天幕：地平线雾带的渐变（工作台截图里台面边缘上方那条 雾灰→近黑 的过渡）。
        /// <para>
        /// 为什么不是真雾：离屏任务用 CustomActors 孤立渲染，场景里的 <c>ExponentialHeightFog</c>
        /// 无论是否加进渲染列表都不生效（2026-10-27 两轮实测：地平线仍是硬边、地面亮度剖面完全不变）。
        /// 也不能用 2D 叠色：雾带横穿人物腰部，会把人物一起洗灰。
        /// </para>
        /// <para>
        /// 做法：<b>一整块</b>竖直片，渐变在材质里按 UV.y 逐像素算（exp 曲线），不再是"一带一块"。
        /// 旧做法每 30cm 一块单色片（96 个 Actor + 96 个材质实例），一是片与片之间是硬台阶，
        /// 大片暗天上读成一条条横纹（2026-10-28 用户报），二是这么多实例跑两三分钟就把 DX11
        /// 的缓冲池耗尽（日志连续两轮 GPUBufferDX11.cpp:181 CHECK）。
        /// </para>
        /// </summary>
        private void CreateStageBackdrop()
        {
            if (!_stageSkyModelCache)
                _stageSkyModelCache = BuildStageSkyQuadModel();
            if (!_stageSkyModelCache)
                return;

            if (_stageSkyMatCache == null)
                _stageSkyMatCache = BuildStageSkyMaterial();
            if (_stageSkyMatCache == null)
                return;

            var sky = _stageRoot.AddChild<StaticModel>();
            sky.Name = "CharacterPreview3D_StageSky";
            sky.Model = _stageSkyModelCache;
            sky.LocalPosition = new Vector3(0f, 0f, -StageSkyDistance);
            ApplySkyMaterial(sky, _stageSkyMatCache);
        }

        /// <summary>给天幕片挂材质。</summary>
        private static void ApplySkyMaterial(StaticModel sky, MaterialInstance material)
        {
            var entries = sky.Entries;
            if (entries == null || entries.Length == 0)
                return;

            // 天幕不投影，否则会在台面上投出一排横线
            entries[0].ShadowsMode = ShadowsCastingMode.None;
            if (material)
                entries[0].Material = material;
            sky.Entries = entries;
        }

        /// <summary>
        /// 天幕材质：<b>一个</b>实例，颜色由材质按 UV.y 逐像素算（见 CreateStageBackdrop 的说明）。
        /// <para>
        /// 指数而非线性：工作台截图的雾光是一条<b>长尾</b>——地平线处最亮，往上迅速降一档后
        /// 拖出很长的余晖（逐 5% 行采样：39→27→21→16→12→10），线性渐变只有短短的十来个百分点
        /// 就黑透，读起来是"一条硬横带"。exp(-k·h/H) 与实测剖面吻合（k≈2.8）。
        /// </para>
        /// <para>
        /// 基座用**Unlit**（Content/Materials/PreviewStageSky.flax）：天幕是雾，不是被照亮的墙。
        /// 用受光材质时，场景环境贴图在竖直面上镜面反射，会在天上打出一团随视角漂移的光斑
        /// （实测上方亮度 14→44 的横向不均），而且颜色还要再乘一遍三点光的增益——同一组颜色
        /// 换个光照就变样。Unlit 下颜色即所见，雾带只跟取景有关。
        /// </para>
        /// </summary>
        private static MaterialInstance BuildStageSkyMaterial()
        {
            var baseMat = Content.Load<MaterialBase>(StageSkyMaterialPath, 10000.0);
            if (baseMat == null)
            {
                // 退路：老的单色基座（受光，会偏亮且带环境反光），至少天幕不会整块消失
                Debug.LogError($"[CharacterPreview3D] 天幕材质 {StageSkyMaterialPath} 加载失败，退回单色基座");
                baseMat = Content.Load<MaterialBase>("Content/Materials/Material.flax", 10000.0);
                if (baseMat == null)
                    return null;
                if (baseMat.CreateVirtualInstance() is MaterialInstance flat)
                {
                    flat.SetParameterValue("Color", StageSkyBottomColor, true);
                    return flat;
                }
                return null;
            }

            if (baseMat.CreateVirtualInstance() is not MaterialInstance inst)
            {
                Debug.LogWarning("[CharacterPreview3D] 天幕：材质实例创建失败");
                return null;
            }

            inst.SetParameterValue("ColorTop", StageSkyTopColor, true);
            inst.SetParameterValue("ColorBottom", StageSkyBottomColor, true);
            // 指数衰减折到 UV 上：v 从地平线(0)到堆顶(1) 对应高度 h = v·StackHeight，
            // 材质里算 pow(e, FalloffExp·v) = e^(−k·h/GradientHeight)。
            inst.SetParameterValue("FalloffExp",
                -StageSkyFalloff * StageSkyStackHeight / StageSkyGradientHeight, true);
            return inst;
        }

        /// <summary>
        /// 天幕网格：一整块竖直片（单位厘米，不再按 Actor 缩放）。
        /// y 向下为正：y=+StageSkyBottom 是雾墙底、y=0 是地平线、y=−StackHeight 是堆顶；
        /// UV.y 只在地平线以上生长（雾墙整段都用 v=0 的底部色），材质按 v 算指数雾光。
        /// </summary>
        private static Model BuildStageSkyQuadModel()
        {
            var model = Content.CreateVirtualAsset<Model>();
            if (model == null)
            {
                Debug.LogError("[CharacterPreview3D] 天幕：虚拟 Model 创建失败");
                return null;
            }

            // Flax 约定：返回 true = 失败
            if (model.SetupLODs(new[] { 1 }))
            {
                Debug.LogError("[CharacterPreview3D] 天幕：SetupLODs 失败");
                return null;
            }

            var lod = model.LODs != null && model.LODs.Length > 0 ? model.LODs[0] : null;
            var mesh = lod?.GetMesh(0) as Mesh;
            if (mesh == null)
            {
                Debug.LogError("[CharacterPreview3D] 天幕：虚拟网格取不到");
                return null;
            }

            float halfW = StageSkyWidth * 0.5f;
            var vertices = new[]
            {
                new Float3(-halfW, StageSkyBottom, 0f), new Float3(halfW, StageSkyBottom, 0f),
                new Float3(-halfW, 0f, 0f),            new Float3(halfW, 0f, 0f),
                new Float3(-halfW, -StageSkyStackHeight, 0f), new Float3(halfW, -StageSkyStackHeight, 0f),
            };
            var indices = new[] { 0, 1, 3, 0, 3, 2, 2, 3, 5, 2, 5, 4 };
            var normal = new Float3(0f, 0f, 1f);
            var tangent = new Float3(1f, 0f, 0f);
            var normals = new[] { normal, normal, normal, normal, normal, normal };
            var tangents = new[] { tangent, tangent, tangent, tangent, tangent, tangent };
            var uvs = new[]
            {
                new Float2(0f, 0f), new Float2(1f, 0f),
                new Float2(0f, 0f), new Float2(1f, 0f),
                new Float2(0f, 1f), new Float2(1f, 1f),
            };
            var white = Color32.White;
            var colors = new[] { white, white, white, white, white, white };

            mesh.UpdateMesh(vertices, indices, normals, tangents, uvs, colors);
            return model;
        }

        /// <summary>天幕宽度（厘米）：要横向盖满取景画面。</summary>
        private const float StageSkyWidth = 8000f;

        /// <summary>
        /// 雾光长尾的高度（厘米）：亮度指数衰减的尺度，t = 高度 / 该值。
        /// <para>
        /// 与取景一起标定：当前镜头（45° 视场、俯仰 -15.5°、天幕距 1400）下，
        /// 地平线落在画面 68.5%、参考图的雾光可读到约 27%，两者夹角换算约 1000cm。
        /// </para>
        /// </summary>
        private const float StageSkyGradientHeight = 1000f;

        /// <summary>
        /// 天幕堆的总高度（厘米）：必须高到顶出画幅（画幅顶约 1250cm），
        /// 否则堆顶会在天上留一条纯黑硬边（实测 12.8% 处一条横缝）。
        /// </summary>
        private const float StageSkyStackHeight = 3000f;

        /// <summary>雾光衰减指数：亮度 = 顶部基准 + (底部−顶部)·e^(−k·h/H)，h 为自地平线往上的高度。</summary>
        private const float StageSkyFalloff = 2.8f;

        /// <summary>天幕材质（Unlit + 双面）：颜色参数即最终所见，不再乘光照。</summary>
        private const string StageSkyMaterialPath = "Content/Materials/PreviewStageSky.flax";

        /// <summary>地平线以下雾墙的高度（厘米）：填住小台面远边与天幕之间的空档（整段用 v=0 的底部色）。</summary>
        private const float StageSkyBottom = 1400f;

        /// <summary>天幕离舞台的深度（厘米）——落在台面远边（800cm）之后。</summary>
        private const float StageSkyDistance = 1400f;

        /// <summary>
        /// 雾带底部颜色（Unlit ⇒ 参数即出图，但引擎的色调映射会把暗部再压一档：
        /// 实测参数 0.141 → 出图 22/255、0.037 → 2/255，反解出 ≈2.6·p^1.78，
        /// 于是要出参考图的 36/255 得给 0.193。色相沿用截图地平线的 (1, 1.07, 1.13)）。
        /// </summary>
        private static readonly Color StageSkyBottomColor = new Color(0.212f, 0.227f, 0.240f, 1f);

        /// <summary>雾带顶部颜色：目标出图 9/255（截图天顶实测），同样按色调映射反解。</summary>
        private static readonly Color StageSkyTopColor = new Color(0.088f, 0.094f, 0.099f, 1f);

        /// <summary>
        /// 预览 rig 的宿主场景：<b>固定为 RootScene</b>（即 <see cref="GameSceneManager"/> 所在场景）。
        /// <para>
        /// 原实现取"最后加载的非过渡场景"，结果随切换时机在 Character / RootScene 之间摇摆：
        /// 一旦落在会被卸载的场景上，每次页面导航都要重建整套 rig（相机+灯光+模型+RenderTexture），
        /// 实测 1 分钟内重建 4 次，并伴随"宿主场景正在卸载，断开离屏渲染任务"的抖动。
        /// 水墨 Canvas 本来就挂在 RootScene 下，预览 rig 跟着它同源同寿命才对。
        /// </para>
        /// </summary>
        /// <returns>可用的宿主场景，无则返回 null</returns>
        private static FlaxEngine.Scene GetTargetScene()
        {
            var host = GameSceneManager.Instance;
            if (host != null && host.Actor != null && host.Actor.Scene != null)
                return host.Actor.Scene;

            // 兜底：GameSceneManager 还没就绪时（脚本启动顺序），退回旧的选择策略，
            // 但排除过渡场景，避免 rig 建在马上要被卸载的过渡场景里。
            for (int i = Level.ScenesCount - 1; i >= 0; i--)
            {
                var scene = Level.GetScene(i);
                if (scene != null && scene.Name != "TransitionScene")
                    return scene;
            }
            return null;
        }


        /// <summary>
        /// 安全应用 SkinnedModel 到 <see cref="_animatedModel"/>。
        /// 若传入为 null，则回退到默认模型；预览视图**不启用动画图**（仅展示绑定姿态站立）。
        /// 遵循竞态安全协议：先停用组件、清空 AnimationGraph，再修改 SkinnedModel。
        /// </summary>
        /// <param name="skinnedModel">指定的 SkinnedModel，为 null 则使用默认</param>
        /// <param name="animGraph">指定的 AnimationGraph，为 null 则使用默认</param>
        private void ApplyModelResources(SkinnedModel skinnedModel, AnimationGraph animGraph)
        {
            if (_animatedModel == null)
            {
                FlaxEngine.Debug.LogWarning("[CharacterPreview3D] ApplyModelResources: _animatedModel 为 null");
                return;
            }

            // 回退到默认资产（非阻塞：未就绪时登记在途请求，本轮直接返回，不碰当前渲染）
            if (skinnedModel == null)
            {
                var requested = EquipmentDatabase.RequestBodyModel(_bodyGender);
                if (requested == null)
                {
                    FlaxEngine.Debug.LogWarning("[CharacterPreview3D] 无法获取 SkinnedModel，禁用 AnimatedModel");
                    // 只在状态真的要变时才写 IsActive：Actor 的启用位在脚本 tick 中被反复写，
                    // 会让引擎一次次销毁/重建托管脚本对象，实测会以
                    // ScriptingObject::ChangeID ← ManagedScriptingObject::OnScriptingDispose 的空句柄崩溃收场。
                    if (_animatedModel.IsActive)
                    {
                        _animatedModel.IsActive = false;
                    }
                    return;
                }

                if (!requested.IsLoaded)
                {
                    _pendingBodyModel = requested;
                    _pendingBodyGender = _bodyGender;
                    _pendingBodyElapsed = 0f;
                    return;
                }

                skinnedModel = requested;
            }

            // 幂等：同一具身体模型已经在渲染，就绝不再拆一次。
            // 下面的换绑流程会短暂 IsActive=false / UpdateMode=Never / 清 AnimationGraph，
            // 而渲染任务每帧照旧出图 —— 于是"每点一次选角行就闪一次半具身体"，
            // 连点时多帧交错正是用户看到的虚影。
            // 用 ReferenceEquals 而不是比 SkinnedModel 属性：后者的 getter 可能返回另一个托管包装，
            // 而 _appliedBodyModel 与 EquipmentDatabase 缓存是同一个对象，身份判定才可靠。
            if (ReferenceEquals(skinnedModel, _appliedBodyModel) && _animatedModel.IsActive)
            {
                _appliedBodyGender = _bodyGender;
                return;
            }

            // 预览视图：不启用动画图——只展示骨骼绑定姿态（自然站立、肢体完整），
            // 避免动画根姿态/缺失动画导致肢体错位、部件飞散与包围盒失真（2026-09 视觉修复）。
            animGraph = null;

            FlaxEngine.Debug.Log($"[CharacterPreview3D] ApplyModelResources: SkinnedModel={(skinnedModel != null ? skinnedModel.Path : "null")}, AnimationGraph={(animGraph != null ? animGraph.Path : "null")}");
            _rebindCount++; // 临时诊断（虚影定位，查完即删）：走到这里就是真换绑了一次

            if (skinnedModel == null)
            {
                FlaxEngine.Debug.LogWarning("[CharacterPreview3D] 无法获取 SkinnedModel，禁用 AnimatedModel");
                if (_animatedModel.IsActive)
                {
                    _animatedModel.IsActive = false;
                }
                return;
            }

            // 换绑会清空 GraphInstance.NodesPose，而待机动画的闸门是 _idlePoseCaptured：
            // 基准不作废的话，每帧仍拿上一具模型的 7 根骨骼去 SetNodeTransformation，
            // 引擎在 AnimatedModel.cpp 的 nodeIndex < NodesPose.Count() 断言上逐帧刷错
            //（2026-10-22 实测一次换绑 = 228 条 Check failed）。
            // 下面的取景流程会在姿态稳定后重新 CaptureIdleBasePose 并把闸门再打开。
            _idlePoseCaptured = false;

            // 竞态安全切换
            var originalUpdateMode = _animatedModel.UpdateMode;
            _animatedModel.UpdateMode = AnimatedModel.AnimationUpdateMode.Never;
            _animatedModel.IsActive = false;
            _animatedModel.AnimationGraph = null;

            if (_animatedModel.SkinnedModel != skinnedModel)
                _animatedModel.SkinnedModel = skinnedModel;

            _animatedModel.AnimationGraph = animGraph;

            try
            {
                _animatedModel.SetupSkinningData();
                _animatedModel.ResetAnimation();
                _animatedModel.UpdateAnimation();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"[CharacterPreview3D] 刷新 AnimatedModel 失败: {ex.Message}");
            }

            _animatedModel.UpdateMode = originalUpdateMode;

            // 换绑成功才记账：幂等判定与"后到的旧请求不许覆盖"都依赖这两个字段
            _appliedBodyModel = skinnedModel;
            _appliedBodyGender = _bodyGender;
            _pendingBodyModel = null;
            _headAligned = false;   // 换了身体模型 ⇒ 头要重新对着新骨架标定

            // 身体成了就顺手把"头"也装上（独立一条路，见 ApplyHeadResources）。
            // 放在这里而不是 InitializeActors：InitializeActors 时资产可能还没加载完。
            ApplyHeadResources();

            // 启用位由唯一这道闸门落：模板身体模式开启时它会把 _animatedModel 压回不可见
            //（首次初始化时原状态为 false，所以这里必须显式激活，不能保留 wasActive 原值）。
            ApplyBodyVisibility();

            FlaxEngine.Debug.Log("[CharacterPreview3D] AnimatedModel 已激活");
        }

        /// <summary>
        /// 加载默认 AnimationGraph（来自 HundunWorldGame 中的资产引用）。
        /// </summary>
        /// <returns>已加载的 AnimationGraph，或 null</returns>
        private static AnimationGraph LoadDefaultAnimationGraph()
        {
            try
            {
                var asset = Content.Load<AnimationGraph>(CharacterAnimGraphGuid);
                if (asset != null && asset.IsLoaded)
                {
                    FlaxEngine.Debug.Log($"[CharacterPreview3D] AnimationGraph 已加载 (GUID): {asset.Path}");
                    return asset;
                }
                if (asset != null)
                {
                    asset.WaitForLoaded(30000.0);
                    if (asset.IsLoaded)
                    {
                        FlaxEngine.Debug.Log($"[CharacterPreview3D] AnimationGraph 等待后加载完成 (GUID): {asset.Path}");
                        return asset;
                    }
                }

                var pathAsset = Content.LoadAsync<AnimationGraph>(CharacterAnimGraphPath);
                if (pathAsset != null && pathAsset.WaitForLoaded(30000.0) && pathAsset.IsLoaded)
                {
                    FlaxEngine.Debug.Log($"[CharacterPreview3D] AnimationGraph 已加载 (路径): {pathAsset.Path}");
                    return pathAsset;
                }
                FlaxEngine.Debug.LogWarning("[CharacterPreview3D] 默认 AnimationGraph 加载失败：GUID 与路径均未就绪");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CharacterPreview3D] 加载默认 AnimationGraph 失败: {ex.Message}");
            }
            return null;
        }

        // ===================================================================
        // 相机变换
        // =======================================================================

        /// <summary>
        /// 重新计算相机位置：围绕角色取景中心（<see cref="_fitCenterY"/>）按 <see cref="_fitDistance"/> 半径
        /// 在 <see cref="_cameraPitchDeg"/> 限制范围内俯仰环视——角色全身完整入画、比例协调。
        /// 取景参数由 <see cref="TryAutoFrame"/> 依据模型实际包围盒计算（默认值等价于原基线）。
        /// </summary>
        private void UpdateCameraTransform()
        {
            if (_cameraRoot == null || _camera == null)
                return;

            float elevation = Mathf.DegreesToRadians * (CameraBaseElevationDeg + FramingPitchDeg + _cameraPitchDeg);
            var focus = StageFocus;
            // 水平构图偏移：注视点左移 = 角色在画面里右移（出图只翻转竖直方向，X 不翻）。
            // 全屏铺满的选角/创角页靠这一项把立绘让到右半。
            // 系数 ×2：半视宽 = tan(fov/2)*dist*aspect，而 FramingOffsetX 是"整幅宽度"的比例。
            if (Mathf.Abs(FramingOffsetX) > 0.0001f)
            {
                float aspect = Width > 4f && Height > 4f ? Width / Height : 1f;
                float halfTan = Mathf.Max(Mathf.Tan(Mathf.DegreesToRadians * CameraFieldOfView * 0.5f), 0.01f);
                focus += new Vector3(-2f * FramingOffsetX * _fitDistance * halfTan * aspect, 0f, 0f);
            }
            // 仰角那一项要往**世界 −Y** 走。出图是竖直翻转的（世界 +Y 在屏幕上是往下），
            // 所以"相机在角色上方俯视"在 world 里是负 Y；旧写法把相机放到世界 +Y ⇒ 注释里的
            // "轻微俯视更自然"实际渲染成仰视 6.8°（与 §173 那两处取景反号同源，同一个翻转口径）。
            // 顺带把竖直拖拽也带正了：向下拖 = pitch 减小 = 相机在屏幕上往下走。
            //
            // 台面限位：竖直拖拽 + 滚轮拉远都会把相机往下带（−sinθ 随距离线性放大），
            // 越过台面后整块台面会挡在镜头前——就是用户说的"穿模"（角色面板台面 1600，
            // 相机水平位置就落在台面范围内，钻下去直接看到台底）。所以按几何反解一个仰角下限：
            // 相机高度 = focus.y − d·sinθ，要求不高于台面 − 余量。
            if (_fitDistance > 0.01f && _stageRoot != null)
            {
                float floorY = _stageRoot.Position.Y;
                float minSin = Mathf.Clamp(
                    (focus.Y - floorY + CameraFloorClearance) / _fitDistance, -0.98f, 0.98f);
                float minElevation = (float)System.Math.Asin(minSin);
                if (elevation < minElevation)
                    elevation = minElevation;
            }

            _cameraRoot.Position = focus + new Vector3(
                0f,
                -_fitDistance * Mathf.Sin(elevation),
                _fitDistance * Mathf.Cos(elevation));
            _cameraRoot.LookAt(focus);
        }

        // ===================================================================
        // 渲染
        // =======================================================================

        /// <inheritdoc />
        public override void Draw()
        {
            base.Draw();

            var bounds = new Rectangle(0, 0, Width, Height);

            // 1. 离屏纹理尚未就绪时先铺一层墨底，避免第一帧透出宿主页面
            if (!_transparentBackground)
                Render2D.FillRectangle(bounds, InkWashTheme.BaseDefault);

            // 2. 绘制离屏 RenderTexture（若可用）
            if (_textureBrush != null && _renderTexture != null && _renderTexture.IsAllocated)
            {
                _textureBrush.Draw(bounds, Color.White);
            }

            // 3. 压边罩：底色由 3D 舞台给出，这里只做很淡的中性近黑渐变压住上下边缘
            //    （灰紫罩会把冷灰的地平线染成紫，已随工作台舞台一起换成中性色）。
            DrawInkVeil(bounds);

            // 4. 水墨微粒（漂浮于罩层之上，形成景深）
            DrawInkMotes();

            // 5. 描边（金色，嵌入模式下跳过，由宿主容器提供边框）
            if (!_transparentBackground)
                Render2D.DrawRectangle(bounds, InkWashTheme.BorderGold, 1f);
        }

        /// <summary>压边罩的分带缓存（每带 2 设备像素 + 有序抖动，尺寸不变就不重算）。</summary>
        private Color[] _veilBands;
        private float _veilBandHeight;
        private float _veilBandsForHeight = -1f;

        /// <summary>
        /// 压边罩在归一化高度 t 处的颜色：中性近黑、上下略重、中线几乎不加。
        /// <para>
        /// 刻意用**单条余弦曲线**而不是多段线性插值：分段插值在拼接处导数不连续，
        /// 那条"折点"会被眼睛直接读成一条硬边（实测 28 段 + 三段线性 = 满屏横条纹）。
        /// </para>
        /// </summary>
        private static Color VeilAt(float t)
        {
            // 到画面中线的归一化距离：0 = 中线，1 = 顶/底
            float edge = Mathf.Abs(t - 0.5f) * 2f;
            float alpha = Mathf.Lerp(0.02f, 0.16f, edge * edge);
            return new Color(0.012f, 0.014f, 0.018f, Mathf.Clamp(alpha, 0.01f, 0.9f));
        }

        /// <summary>
        /// 绘制灰紫罩：2 像素一带，并对相邻带做 ±半步 的有序抖动，
        /// 把量化误差打散成高频噪点，避免肉眼可辨的阶梯。
        /// </summary>
        private void DrawInkVeil(Rectangle bounds)
        {
            if (bounds.Width <= 0f || bounds.Height <= 0f)
                return;

            const float bandH = 2f;
            int steps = Mathf.Max(2, (int)(bounds.Height / bandH));

            if (_veilBands == null || _veilBands.Length != steps || _veilBandsForHeight != bounds.Height)
            {
                _veilBands = new Color[steps];
                _veilBandHeight = bounds.Height / steps;
                _veilBandsForHeight = bounds.Height;

                for (int i = 0; i < steps; i++)
                {
                    var c = VeilAt((i + 0.5f) / steps);
                    // 有序抖动：交替把 alpha 推高/拉低半个量化步长
                    float step = Mathf.Abs(VeilAt((i + 1f) / steps).A - c.A);
                    c.A += ((i & 1) == 0 ? -0.5f : 0.5f) * step;
                    _veilBands[i] = c;
                }
            }

            for (int i = 0; i < _veilBands.Length; i++)
            {
                Render2D.FillRectangle(
                    new Rectangle(bounds.X, bounds.Y + i * _veilBandHeight, bounds.Width, _veilBandHeight),
                    _veilBands[i]);
            }
        }

        // ===================================================================
        // 水墨微粒渲染
        // =======================================================================

        /// <summary>
        /// 初始化水墨微粒场：34 粒，分近/远两层（近层大而慢、亮，远层小而快、暗），
        /// 颜色取月白 / 淡紫 / 淡金，压在灰紫罩上能读出景深。
        /// </summary>
        private void InitializeMotes()
        {
            const int count = 34;
            _motes = new InkMote[count];

            var tints = new[]
            {
                new Color(0.86f, 0.88f, 0.95f, 1f),          // 月白
                new Color(0.74f, 0.64f, 0.96f, 1f),          // 淡紫
                new Color(0.94f, 0.88f, 0.72f, 1f),          // 淡金
            };

            for (int i = 0; i < count; i++)
            {
                bool near = (i & 1) == 0;   // 一半近景、一半远景
                double r = _moteRandom.NextDouble();

                _motes[i] = new InkMote
                {
                    XFrac = (float)_moteRandom.NextDouble(),
                    YFrac0 = (float)_moteRandom.NextDouble(),
                    // 近层缓浮、远层快掠：两层速度差是景深的主要来源
                    RiseSpeed = near ? (0.016f + (float)r * 0.022f) : (0.040f + (float)r * 0.050f),
                    SwayAmp = near ? 12f + (float)r * 22f : 5f + (float)r * 10f,
                    SwayFreq = near ? 0.10f + (float)r * 0.18f : 0.22f + (float)r * 0.38f,
                    Phase = (float)(_moteRandom.NextDouble() * Mathf.TwoPi),
                    Size = near ? 3.4f + (float)r * 3.2f : 1.4f + (float)r * 1.8f,
                    BaseAlpha = near ? 0.46f + (float)r * 0.22f : 0.24f + (float)r * 0.16f,
                    Tint = tints[i % tints.Length],
                };
            }
        }

        /// <summary>
        /// 绘制水墨微粒：缓慢上升并左右摇曳、透明度呼吸，
        /// 超出顶部后回绕到底部，形成自然的水墨氛围。
        /// </summary>
        private void DrawInkMotes()
        {
            if (_motes == null)
                InitializeMotes();

            float t = Time.GameTime;
            for (int i = 0; i < _motes.Length; i++)
            {
                var m = _motes[i];

                // 垂直：随时间上升，归一化到 [0,1) 循环（超顶后回到底部）
                float yFrac = m.YFrac0 - m.RiseSpeed * t;
                yFrac -= Mathf.Floor(yFrac);

                // 水平：基准位置 + 正弦摇曳
                float x = m.XFrac * Width + Mathf.Sin(t * m.SwayFreq * Mathf.TwoPi + m.Phase) * m.SwayAmp;
                float y = yFrac * Height;

                // 透明度呼吸（脉动幅度加大，让粒子"活"起来而不是静止的噪点）
                float alpha = m.BaseAlpha * (0.35f + 0.65f * Mathf.Sin(t * 1.6f + m.Phase));
                if (alpha <= 0.02f)
                    continue;

                var core = new Color(m.Tint.R, m.Tint.G, m.Tint.B, alpha);
                InkRenderHelper.FillRadialGradient(new Float2(x, y), m.Size * 3.0f, core, Color.Transparent, 7);

                // 近层再点一枚高亮核，做出"光点"而非"雾团"的层次
                if (m.Size > 3f)
                {
                    var hot = new Color(m.Tint.R, m.Tint.G, m.Tint.B, alpha * 0.9f);
                    InkRenderHelper.FillRadialGradient(new Float2(x, y), m.Size * 0.9f, hot, Color.Transparent, 4);
                }
            }
        }

        // ===================================================================
        // 鼠标交互（拖拽旋转）
        // =======================================================================

        /// <summary>
        /// 应用拖拽位移（UI 事件与输入轮询共用，同帧自动去重）：
        /// 水平拖拽 = 角色绕自身垂直轴左右旋转；垂直拖拽 = 相机小幅俯仰查看。
        /// </summary>
        private void ApplyDragMove(Float2 local)
        {
            float deltaX = local.X - _lastMousePosition.X;
            float deltaY = local.Y - _lastMousePosition.Y;
            _lastMousePosition = local;
            ApplyDragDelta(deltaX, deltaY);
        }

        /// <summary>拖拽增量 → 角色自转 + 相机俯仰（事件通道与轮询通道共用）。</summary>
        private void ApplyDragDelta(float deltaX, float deltaY)
        {
            // 水平拖拽：角色绕自身垂直（左右对称）轴自转——仅角色旋转、保持站立，
            // 相机固定于正面框架，任意角度均完整显示全身。
            if (Mathf.Abs(deltaX) > 0.01f)
            {
                _modelYaw += deltaX * DragYawSensitivity;
                _modelYaw = WrapAngle(_modelYaw);
                ApplyModelYaw();
                if (!_loggedYawApplied)
                {
                    _loggedYawApplied = true;
                    FlaxEngine.Debug.Log($"[CharacterPreview3D] 左右旋转生效：dx={deltaX:F1}px, yaw={_modelYaw * Mathf.RadiansToDegrees:F1}°");
                }
            }

            // 垂直拖拽：相机绕"角色身体左右轴"（过注视点的水平轴）俯仰查看，
            // 角度限制在 [CameraPitchMinDeg, CameraPitchMaxDeg]，确保全身完整可见（不仰视、不俯穿）。
            if (Mathf.Abs(deltaY) > 0.01f)
            {
                _cameraPitchDeg = Mathf.Clamp(
                    _cameraPitchDeg - deltaY * DragPitchSensitivity,
                    CameraPitchMinDeg, CameraPitchMaxDeg);
                UpdateCameraTransform();
            }
        }

        /// <inheritdoc />
        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            base.OnMouseDown(location, button);

            if (button == MouseButton.Left)
            {
                _isDragging = true;
                _isPollDragging = false;   // 事件通道接管，轮询让位
                _lastMousePosition = location;
                _lastEventMoveTime = Time.GameTime;
                if (!_loggedDragStart)
                {
                    _loggedDragStart = true;
                    FlaxEngine.Debug.Log("[CharacterPreview3D] 拖拽开始（UI 事件通道）");
                }
                return true;
            }

            return false;
        }

        /// <inheritdoc />
        public override bool OnMouseUp(Float2 location, MouseButton button)
        {
            base.OnMouseUp(location, button);

            if (button == MouseButton.Left)
            {
                _isDragging = false;
                return true;
            }

            return false;
        }

        /// <inheritdoc />
        public override void OnMouseMove(Float2 location)
        {
            base.OnMouseMove(location);

            _lastEventMoveTime = Time.GameTime; // 标记事件通道存活

            if (!_isDragging)
                return;

            ApplyDragMove(location);
        }

        /// <summary>
        /// 左键拖拽的轮询兜底：舞台预览铺满整页、上面还压着 CcStageScrim 与页面路由，
        /// 鼠标事件到不了这个控件（2026-10-27 实测：注入拖拽落到 InkPageRouter，日志里
        /// 连"拖拽开始"都没有），所以直接读原始输入状态。只在按下点落进热区时接管，
        /// 列表/确认块/键位提示的点击不受影响。
        /// </summary>
        private void PollDragInput()
        {
            if (_modelRoot == null || !VisibleInHierarchy)
            {
                _isPollDragging = false;
                return;
            }

            bool down = Input.GetMouseButton(MouseButton.Left);
            if (!down)
            {
                _isPollDragging = false;
                return;
            }

            var local = PointFromScreen(Input.MousePosition);

            if (!_isPollDragging)
            {
                if (_isDragging)   // 事件通道已经在拖：别重复接管
                    return;
                if (local.X < 0f || local.Y < 0f || local.X > Width || local.Y > Height)
                    return;

                _isPollDragging = true;
                _pollDragStart = local;
                _pollDragArmed = false;
                _lastMousePosition = local;
                return;
            }

            // 先攒位移再起转：纯点击（不改选/不误转）不该把角色转歪，
            // 也就省掉了"热区里不能有按钮"这种和布局耦合的硬约束。
            if (!_pollDragArmed)
            {
                float dx = local.X - _pollDragStart.X;
                float dy = local.Y - _pollDragStart.Y;
                if (Mathf.Abs(dx) + Mathf.Abs(dy) < DragArmPx)
                    return;
                _pollDragArmed = true;
                _lastMousePosition = _pollDragStart;   // 从按下点补上这段位移
            }

            ApplyDragMove(local);
        }

        /// <inheritdoc />
        public override bool OnMouseWheel(Float2 location, float delta)
        {
            if (base.OnMouseWheel(location, delta))
                return true;

            if (Mathf.Abs(delta) < 0.0001f || _baseFitDistance <= 0.001f)
                return false;

            // 引擎口径：delta>0 表示滚轮向前（远离用户）⇒ 拉近。
            // 步长按比例而不是固定值，否则同一档手感在全身页与头肩页完全不同。
            float step = delta > 0f ? 0.88f : 1f / 0.88f;
            float next = Mathf.Clamp(_fitDistance * step,
                _baseFitDistance * ZoomMinScale, _baseFitDistance * ZoomMaxScale);
            // 玩家自己滚轮缩放 ⇒ 取消分区取景的过渡，否则镜头会把手动缩放任掉。
            _focusAnimating = false;
            _targetFitDistance = next;
            _targetCenterY = _fitCenterY;
            if (Mathf.Abs(next - _fitDistance) < 1e-4f)
                return true;   // 已经顶到上下限，仍然把事件吃掉，别让滚轮去滚旁边的列表

            _fitDistance = next;
            UpdateCameraTransform();
            if (!_loggedWheel)
            {
                _loggedWheel = true;
                FlaxEngine.Debug.Log(string.Format(
                    "[CharacterPreview3D] 滚轮缩放生效：delta={0:F2} 基准={1:F1} 当前={2:F1}",
                    delta, _baseFitDistance, _fitDistance));
            }

            return true;
        }

        /// <summary>将弧度角包裹到 (-π, π]，避免无限累积导致浮点精度损失。</summary>
        private static float WrapAngle(float radians)
        {
            while (radians > Mathf.Pi)
                radians -= Mathf.TwoPi;
            while (radians < -Mathf.Pi)
                radians += Mathf.TwoPi;
            return radians;
        }

        /// <inheritdoc />
        public override void OnEndMouseCapture()
        {
            _isDragging = false;
            base.OnEndMouseCapture();
        }

        /// <summary>
        /// 将当前 <see cref="_modelYaw"/> 应用到 <see cref="_modelRoot"/>。
        /// </summary>
        private void ApplyModelYaw()
        {
            if (_modelRoot != null)
                _modelRoot.Orientation = Quaternion.Euler(0f, _modelYaw * Mathf.RadiansToDegrees, 0f);
        }

        /// <inheritdoc />
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            TickFocusAnimation(deltaTime);

            // 左键拖拽兜底：舞台铺满整页时鼠标事件被压暗层/路由吃掉，这里读原始输入。
            PollDragInput();

            // ── 临时诊断（定位"角色信息页预览虚影"用，查完即删）────────────────
            // 要区分两种成因：① 同一控件在反复换绑（每换一次就闪一帧"半具身体"，
            // 连帧叠起来就是虚影）；② 多个预览控件同时活着（创角页的预览没随页面隐藏，
            // 与角色面板的预览叠在同一块屏幕上）。
            _diagFrames++;
            if (_diagFrames % 120 == 1)
            {
                FlaxEngine.Debug.Log($"[PreviewDiag] #{_diagFrames} host={GetHashCode():X8} " +
                                     $"live={_liveInstances} rebinding={_rebindCount} " +
                                     $"visible={VisibleInHierarchy} taskOn={_renderTask != null && _renderTask.Enabled} " +
                                     $"size={Width:F0}x{Height:F0} loc=({Location.X:F0},{Location.Y:F0}) " +
                                     $"model={(_animatedModel?.SkinnedModel != null ? System.IO.Path.GetFileName(_animatedModel.SkinnedModel.Path) : "null")} " +
                                     $"pos={_modelRoot?.Position.ToString() ?? "null"}");
            }

            // 0. 身体模型异步加载的推进点：请求非阻塞、就绪才换绑。
            //    没有这一步，UI 点击处理器里的 WaitForLoaded 会把游戏线程钉住，
            //    并让换绑发生在资产还没上传完的帧上（糊面 + 叠影）。
            TryApplyPendingBodyModel(deltaTime);

            // 头资源的兜底：正常路径是"身体换绑成功后顺手装头"；直进捏脸页等场景里
            // 身体那步没走到时头会一直是空的（资产=<无>、可服务 0/598、预览全黑）。
            TryApplyHeadResources(deltaTime);

            // 0b. 舞台地台在 InitializeActors 里建好（真实网格，跟随场景渲染），这里无需每帧重画。

            // 2. 姿态强制（每帧重申）：角色必须保持站立——
            //    仅允许绕自身垂直（左右对称）轴的 Yaw 自转；扶正与自转姿态在此强制生效，
            //    任何外部（动画根运动/残留拖拽/资产轴向）造成的倾倒都会被立即纠正为站立。
            ApplyModelUpright();

            // 2b. 程序化待机动画（每帧驱动）：关键——无动画图时 AnimationUpdated 事件不会触发，
            //     必须在此直接驱动骨骼（基于捕获的基准姿态 × 微小动态，无漂移）。
            ApplyProceduralIdle();

            // 3. 站立巡检自愈：若动画状态的根姿态把角色放倒（高度小于前后厚度即判定躺倒），
            //    禁用动画图回退到站立绑定姿态（工装姿态仍为站立 T-pose），并告警一次。
            if (!_uprightVerified && _animatedModel != null)
            {
                _uprightCheckTimer += deltaTime;
                if (_uprightCheckTimer >= 1f)
                {
                    _uprightVerified = true;
                    if (_animatedModel.AnimationGraph != null)
                    {
                        var box = _animatedModel.Box;
                        if (box.Size.Y < box.Size.Z)
                        {
                            FlaxEngine.Debug.LogWarning(
                                $"[CharacterPreview3D] 检测到角色已躺倒（Y={box.Size.Y:F2} < Z={box.Size.Z:F2}），" +
                                "禁用动画图以回退站立姿态（保持全身可见与站立展示）。");
                            _animatedModel.AnimationGraph = null;
                        }
                    }
                }
            }

            // 4. 自动取景：模型就绪后按实际包围盒适配相机（全身完整、比例协调、永不裁切）；
            //    模型资源切换时自动重取景。
            if (_framed && _animatedModel != null && !ReferenceEquals(_animatedModel.SkinnedModel, _lastFramedModel))
            {
                _framed = false;
                _fitTimer = 0f;
            }
            TryAutoFrame(deltaTime);
        }

        /// <summary>
        /// 姿态强制：模型按常量校正组合（当前资产为恒等——原始导入即站立且正面朝玩家）
        /// + 居中偏移（施加在模型自身，保证旋转轴穿过自身中轴）
        /// + 自身垂直轴 Yaw 自转（左右拖动 = 左右旋转查看）。每帧重申，确保任意时刻姿态稳定、轴心正确。
        /// </summary>
        private void ApplyModelUpright()
        {
            if (_animatedModel != null)
            {
                // 校正组合：绕 X 轴 ModelPitchCorrection 扶正，再绕垂直轴 ModelYawCorrection 定正面朝向。
                var upright = Quaternion.RotationY(Mathf.DegreesToRadians * ModelYawCorrection)
                              * Quaternion.RotationX(Mathf.DegreesToRadians * ModelPitchCorrection);
                // 居中偏移施加在模型自身的**局部**坐标上：父节点（转盘）固定在舞台点，
                // 于是 Yaw 旋转轴正好是角色自身的垂直中轴线（原地自转、不绕摆）。
                _animatedModel.Orientation = upright;
                _animatedModel.LocalPosition = _modelLocalOffset;
            }
            ApplyTemplateBodyPose();
            ApplyHeadUpright();
            ApplyModelYaw();
        }

        /// <summary>
        /// conform topology 那两具 FBX 资产（模板身体 / 模板头）的**正面朝向**补一个 180° 绕垂直轴。
        /// <para>
        /// 依据（2026-10-17 用户截图定案）：只给 Rx(180) 时，画面是"**身体背面 + 正脸**"——
        /// 头（Ada GLB 那具）朝向对，模板身体的正面却背对相机。两具资产不同源：
        /// 身体来自 <c>body.fbx</c>（其 <c>GlobalSettings.FrontAxis=2/+1</c> 说 +Z 是前），
        /// 脸来自 UE 的 glTF 导出，两者"前"落在相反两侧 ⇒ 只能各给各的朝向。
        /// </para>
        /// <para>
        /// 注意这**不影响颈缝**：颈圈截面近似圆，绕垂直轴转 180° 不改变它落在哪儿；
        /// 也不影响转盘拖拽（Yaw 加在 Actor 自身，父节点转盘对头与身体一起转）。
        /// </para>
        /// </summary>
        private const float TemplateFbxYawCorrection = 180f;

        /// 用方法而不是静态字段：`TemplateUpright` 声明在本文件更靠后的位置，
        /// 静态字段初始化按声明顺序跑，写成字段会拿到"还没初始化"的零四元数。
        /// </summary>
        private static Quaternion TemplateFbxPose()
        {
            return Quaternion.RotationY(Mathf.DegreesToRadians * TemplateFbxYawCorrection) * TemplateUpright;
        }

        /// <summary>
        /// 模板身体的姿态：**与角色身体同一套扶正 + 同一个居中偏移**。
        /// <para>
        /// 为什么不是恒等（2026-10-16 走过弯路，别再改回恒等）：Flax 的 **FBX 导入会把 up 轴翻掉**。
        /// 对照实测 —— 源 <c>Character_1.fbx</c> 沿 z 分档：z=0 处横向半幅 0.152（脚）、
        /// z=1.35–1.58 处 0.959（T 型臂展）、z=1.58–1.81 收到 0.095（头顶）⇒ 源里 z=0 是脚；
        /// 而本文件里 <see cref="ModelPitchCorrection"/> 的注释记着该资产在 Flax 里"头朝 -Y、脚朝 +Y"。
        /// 两侧一对照，翻 up 的是导入器，不是某个资产的作者习惯。
        /// </para>
        /// <para>
        /// 模板身体与 <c>Character_1/2</c> **同为 FBX 导入** ⇒ 共用同一套校正。
        /// 而 <see cref="_headModel"/> 来自 **GLB**（glTF 不翻 up：它在 Flax 里实测
        /// <c>head</c> 骨 y=143.36 高于 <c>neck_01</c> 的 132.99）⇒ 头必须用恒等，
        /// **两具的校正天生不同，谁都不能继承谁**。
        /// </para>
        /// <para>
        /// 教训：包围盒的 y 区间与符号无关，**判不出上下**。要判朝向只能量
        /// "按 up 轴分档的横向展幅"或找硬锚点（眼睛的 y 在牙齿之上 / 头骨在颈骨之上）。
        /// </para>
        /// </summary>
        private void ApplyTemplateBodyPose()
        {
            if (_templateBody == null)
                return;

            // 与角色身体**同一套**扶正。为什么必须同一套（2026-10-17 用户截图定案）：
            // 预览的离屏 RenderTexture 是**竖直翻转**出图的 —— 世界 +Y 在屏幕上是往下。
            // 证据：回执里头骨世界 y(-99843.80) 明明高于身体顶(-99857.65)，画面上头却在脚下方。
            // 所以 `ModelPitchCorrection=180` 不是"某具资产作者习惯"，而是**对这条翻转出图链的补偿**；
            // 我按世界坐标判"恒等即正立"（§171.10）判的是世界系不是屏幕系，那条结论已作废。
            _templateBody.Orientation = TemplateFbxPose();
            // 居中/贴地偏移也必须给：TryAutoFrame 量的是"当前可见身体"的包围盒，
            // 只把偏移写到 _animatedModel 上，模板身体就会停在父节点原点 ⇒ 画面里整个人偏到一边。
            _templateBody.LocalPosition = _modelLocalOffset;

            if (_skinnedBody != null)
            {
                // 蒙皮身体现在是 UE glTF 导出的 preview 资产（与 Ada 头同源），朝向走头那一套
                // （TemplateUpright，无额外 Ry180）；旧的 FBX 模板身体才需要 TemplateFbxPose。
                _skinnedBody.Orientation = TemplateUpright;
                _skinnedBody.LocalPosition = _modelLocalOffset;
            }

            // 模板头与模板身体**完全同一个变换**（同代同度量系 ⇒ 同姿态同位置即天然无缝），
            // 所以它跟着身体吃同一个 Ry(180)：两具是同一个 FBX 包里出来的，"前"在同一侧。
            if (_templateHead != null)
            {
                _templateHead.Orientation = TemplateFbxPose();
                _templateHead.LocalPosition = _modelLocalOffset;
            }
        }

        /// <summary>
        /// 头的姿态：与身体**同一套校正 + 同一个居中偏移**。
        /// <para>
        /// 依据：两具资产走的是同一条 FBX 导入管线，所以"身体需要的那套扶正/朝向/居中"对它同样适用；
        /// 而脸网格的骨骼本就在自身空间的头高处，因此**与身体同变换**即落在颈部位置。
        /// </para>
        /// <para>
        /// 这条"同变换即对齐"是**待目视确认的假设**（头与身体两套骨架不同源）——
        /// 若目视发现头偏高/偏低/尺度不符，改这里（按头自身骨架标定），别去动身体的校正。
        /// 见方案文档 §170。
        /// </para>
        /// </summary>
        private void ApplyHeadUpright()
        {
            if (_headModel == null || _animatedModel == null)
                return;

            if (TemplateBodyInUse)
            {
                // 与模板身体**完全同一个变换**（同一个补偿姿态 + 同一个居中偏移，零额外补偿）。
                // 依据一：两具资产同在 MetaHuman 度量系（身体 0→142.35、Ada 脸 124.26→159.14，单位 cm），
                //        同一变换即天然同框，头该落在脖子上。
                // 依据二：预览的离屏 RenderTexture 是**竖直翻转**出图的（世界 +Y 在屏幕上朝下），
                //        所以三具 Actor 必须共用同一个补偿姿态 `TemplateUpright`；
                //        分别用恒等 / Ry180 会让它们在屏幕上各朝各的（2026-10-17 用户截图定案）。
                // 依据三：Rx(180) 同时翻 Y 和 Z ⇒ 既把画面翻正、又把该资产 -Z 侧的脸转到相机那侧
                //        （判据E 实测眼骨模型 Δz=-9.15，即脸在资产 -Z 侧）。所以**不需要再单独 Ry180**。
                // 依据四（2026-10-17 逐点对应实测，覆盖原先"同一变换即无缝"那句推断）：
                //        Ada 脸的绑定空间与模板差 1.096 倍再加一段平移，见
                //        EquipmentDatabase.AdaToTemplateScale / AdaToTemplateOffset（ICP 残差中位 0.125cm）。
                //        平移写在**父节点局部系**里，所以要先把模板系的 t 旋到父系（R 与等比缩放可交换）。
                _headModel.Orientation = TemplateUpright;
                _headModel.Scale = _animatedModel.Scale * HundunWorld.Game.Equipment.EquipmentDatabase.AdaToTemplateScale;
                _headModel.LocalPosition = _modelLocalOffset + Vector3.Transform(
                    HundunWorld.Game.Equipment.EquipmentDatabase.AdaToTemplateOffset, TemplateUpright);
                return;
            }

            _headModel.Orientation = _animatedModel.Orientation;
            _headModel.Scale = _animatedModel.Scale;
            _headModel.LocalPosition = _animatedModel.LocalPosition;

            // 再把"头"按**骨骼**对到身体的头骨上（同变换只是初值）。
            AlignHeadToBodyByBone();
        }

        /// <summary>头骨候选名（与 <see cref="IdleBoneSlots"/> 里那格同一族）。</summary>
        private static readonly string[] HeadBoneCandidates =
            { "DEF-spine.006", "head", "bip001 head", "mixamorig:head" };

        /// <summary>颈骨候选名（用于按"头到颈的距离"推出两具骨架的尺度比）。</summary>
        private static readonly string[] NeckBoneCandidates =
            { "DEF-spine.005", "neck", "neck_01", "bip001 neck", "mixamorig:neck" };

        /// <summary>
        /// 头的**骨骼对齐**（2026-10-16 实测后补上，替换原先"与身体同变换即落在颈部"那条假设）。
        /// <para>
        /// 为什么必须做：`model_box_probe` 量出两具资产的包围盒根本不成比例 ——
        /// 头 `Ada_FaceMesh_slotpacked` = 63×**249**×36、中心 y=79.6；
        /// 身 `Character_1` = 345×**325**×54、中心 y=90.3。
        /// 一个头不该有身体的 77% 高、也不该骑在躯干中心 ⇒ 照"同变换"放，脸会落在**躯干内部**，
        /// 于是目视"没有任何改观"（用户实测反馈）。
        /// </para>
        /// <para>
        /// 做法：取两具模型各自的**头骨**（局部空间位置），把头 Actor 平移过去使两个头骨重合；
        /// 再用**头到颈的距离比**给出尺度（两具骨架不同源，尺度不一定一致；颈骨缺失就退回 1）。
        /// 骨名缺失时**只记警告并保持同变换**（绝不静默地把头放到某个魔数位置上）。
        /// </para>
        /// </summary>
        private void AlignHeadToBodyByBone()
        {
            // **只对一次**（模型换绑时重置）：上一版把它挂在每帧都会跑的 ApplyModelUpright 上，
            // 实测刷出 4760 行日志，而且身体在放待机动画、骨位每帧都在动 ⇒ 头会跟着抖。
            // 对齐是"装载时的一次性标定"，不是每帧的事。
            if (_headAligned)
                return;

            var bodyModel = _animatedModel?.SkinnedModel;
            var headModel = _headModel?.SkinnedModel;
            if (bodyModel == null || headModel == null || !bodyModel.IsLoaded || !headModel.IsLoaded)
                return;

            string bodyHead = FindBoneName(bodyModel, HeadBoneCandidates);
            string headHead = FindBoneName(headModel, HeadBoneCandidates);
            if (bodyHead == null || headHead == null)
            {
                FlaxEngine.Debug.LogWarning(
                    $"[CharacterPreview3D] 头骨缺失（身={bodyHead ?? "<无>"} 头={headHead ?? "<无>"}）⇒ 头保持同变换");
                return;
            }

            // 注意：`AnimatedModel.GetNodeTransformation` 返回 **void**（不是 bool），
            // 所以只能调用后自己校验矩阵（骨名不存在时它不会通过返回值告诉你）。
            Matrix bodyHeadM, headHeadM;
            _animatedModel.GetNodeTransformation(bodyHead, out bodyHeadM, false);
            _headModel.GetNodeTransformation(headHead, out headHeadM, false);
            if (!IsFiniteMatrix(bodyHeadM) || !IsFiniteMatrix(headHeadM))
            {
                FlaxEngine.Debug.LogWarning("[CharacterPreview3D] 头骨变换取到非有限值 ⇒ 头保持同变换");
                return;
            }

            var bodyHeadPos = bodyHeadM.TranslationVector;
            var headHeadPos = headHeadM.TranslationVector;
            // 尺度：用"头骨到颈骨的距离"比（两具骨架不同源，不能假定同尺度）
            float scale = 1f;
            string bodyNeck = FindBoneName(bodyModel, NeckBoneCandidates);
            string headNeck = FindBoneName(headModel, NeckBoneCandidates);
            if (bodyNeck != null && headNeck != null)
            {
                Matrix bodyNeckM, headNeckM;
                _animatedModel.GetNodeTransformation(bodyNeck, out bodyNeckM, false);
                _headModel.GetNodeTransformation(headNeck, out headNeckM, false);
                if (IsFiniteMatrix(bodyNeckM) && IsFiniteMatrix(headNeckM))
                {
                    float bodyLen = (bodyHeadPos - bodyNeckM.TranslationVector).Length;
                    float headLen = (headHeadPos - headNeckM.TranslationVector).Length;
                    if (headLen > 1e-4f && bodyLen > 1e-4f)
                        scale = bodyLen / headLen;
                }
            }

            _headModel.Scale = _animatedModel.Scale * scale;
            // 平移：让两个头骨重合（先按缩放折算头骨自身的局部位置）
            _headModel.LocalPosition = _animatedModel.LocalPosition
                                       + bodyHeadPos - headHeadPos * scale;

            FlaxEngine.Debug.Log($"[CharacterPreview3D] 头按骨骼对齐: 身头骨={bodyHeadPos} 头头骨={headHeadPos} "
                + $"尺度={scale:F4} ⇒ 头局部位置={_headModel.LocalPosition}");
            _headAligned = true;
        }

        /// <summary>矩阵是否可用的轻量校验（骨名缺失时引擎给的可能是 NaN/零矩阵）。</summary>
        private static bool IsFiniteMatrix(Matrix m)
        {
            var t = m.TranslationVector;
            return !float.IsNaN(t.X) && !float.IsNaN(t.Y) && !float.IsNaN(t.Z)
                   && !float.IsInfinity(t.X) && !float.IsInfinity(t.Y) && !float.IsInfinity(t.Z)
                   && !float.IsNaN(m.M11);
        }

        private float _headRetryCooldown;
        private int _headLoadRetries;

        /// <summary>头部资源的低频兜底：每秒一次、最多 30 次；成功即停，失败也不刷屏。</summary>
        private void TryApplyHeadResources(float deltaTime)
        {
            if (_headModel == null || _appliedHeadModel != null || !Visible || !Enabled || _headLoadRetries >= 30)
            {
                return;
            }

            _headRetryCooldown -= deltaTime;
            if (_headRetryCooldown > 0f)
            {
                return;
            }

            _headRetryCooldown = 1f;
            _headLoadRetries++;
            ApplyHeadResources();
        }

        /// <summary>
        /// 给"预览里的头"装资源。**与身体分开一条路**：头是独立骨架，不参与身体那套
        /// `_appliedBodyModel` 幂等/竞态保护（混进去会让"换性别"与"装头"互相误判成对方的重复请求）。
        /// 加载失败只记警告并停用它 ⇒ 预览退化成"只有身体"，与加头之前的行为一致，不会把身体那条链带崩。
        /// </summary>
        private void ApplyHeadResources()
        {
            if (_headModel == null)
                return;

            var path = HundunWorld.Game.Equipment.EquipmentDatabase.HeadModelPath;
            SkinnedModel head = Content.Load<SkinnedModel>(path, 10000.0);
            if (head == null || !head.IsLoaded)
            {
                // 路径加载在“注册表 ID 与文件头不一致”时会失败（实测重启/重建库后出现）；
                // 再按资产库 ID 试一次 Guid 重载，仍不行才退化成只有身体。
                AssetInfo info;
                Content.GetAssetInfo(path, out info);
                if (info.ID != Guid.Empty)
                {
                    head = Content.Load<SkinnedModel>(info.ID, 10000.0);
                }
            }

            if (head == null || !head.IsLoaded)
            {
                FlaxEngine.Debug.LogWarning($"[CharacterPreview3D] 头部模型未就绪，预览暂时只有身体: {path}");
                ApplyBodyVisibility();
                return;
            }

            if (!ReferenceEquals(_appliedHeadModel, head))
            {
                try
                {
                    _headModel.SkinnedModel = head;
                    _headModel.SetupSkinningData();
                    _headModel.ResetAnimation();
                    _headModel.UpdateAnimation();
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogWarning($"[CharacterPreview3D] 刷新头部 AnimatedModel 失败: {ex.Message}");
                    return;
                }

                _appliedHeadModel = head;
                _headAligned = false;   // 换了头模型 ⇒ 重新标定
                ApplyHeadUpright();
                AttachPreviewBrows();
                ApplyBodyVisibility();

                // 一次性诊断（查完可删）：把"头上挂的模型/槽 0 材质"**和两具模型的包围盒**一起读回来。
                // 为什么要读包围盒：目视只能看出"有个头"，分不清那是人偶自带的头还是 MetaHuman 的脸；
                // 而"脸有没有用 MH_Head 渲染、尺寸对不对、是否被人偶自带的头罩住"这三件事，
                // 靠 center/size 两个数就能判。
                var slots = _headModel.MaterialSlots;
                var slot0Mat = _headModel.GetMaterial(0);
                string slot0 = slot0Mat != null ? slot0Mat.Path : "<空>";
                var hb = _headModel.Box;
                var bb = _animatedModel.Box;
                FlaxEngine.Debug.Log($"[CharacterPreview3D] 头部模型已装载: {path} | 模型={_headModel.SkinnedModel?.Path} | 槽数={slots?.Length ?? 0} | 槽0={slot0}");
                FlaxEngine.Debug.Log($"[CharacterPreview3D] 头 Box: center={hb.Center} size={hb.Size}");
                FlaxEngine.Debug.Log($"[CharacterPreview3D] 身 Box: center={bb.Center} size={bb.Size}");
                return;
            }

            ApplyHeadUpright();
            ApplyBodyVisibility();
        }

        /// <summary>
        /// 对"离屏 RenderTexture 竖直翻转出图"的补偿姿态 —— 与角色身体用的是同一个量，
        /// 模板身体与 MetaHuman 头也必须用同一个，否则三者在屏幕上各朝各的。
        /// </summary>
        private static readonly Quaternion TemplateUpright =
            Quaternion.RotationY(Mathf.DegreesToRadians * ModelYawCorrection)
            * Quaternion.RotationX(Mathf.DegreesToRadians * ModelPitchCorrection);

        /// <summary>
        /// 切换"可见身体"的来源：模板身体（<c>MetaHuman_Conform_Topology/body.flax</c>，
        /// 脖子以下、灰模、无骨架）还是 <see cref="_animatedModel"/>（按性别取的完整角色）。
        /// <para>
        /// 谁需要它：创角"面容 / 捏脸"页——那两页看的是 MetaHuman 的脸，
        /// 而按性别取的身体**自带脑袋**，会把脸整颗罩住（用户实测"没有任何改观"，方案文档 §170）。
        /// 模板身体只到颈顶（引擎实测包围盒 y=142.35）⇒ 不遮挡。
        /// </para>
        /// <para>
        /// 只切可见性，不销毁任何 Actor；模型装不上时**退回按性别的身体**并记警告，
        /// 绝不让预览变成只剩一颗头或全空。
        /// </para>
        /// </summary>
        /// <summary>
        /// 分区取景：把镜头**平滑**推到某个部位。<c>HeightFrac</c> 沿用 <see cref="FramingHeightFrac"/> 的约定
        /// （负号 = 从脚底朝屏幕上方量），<c>Zoom</c> 是相对自动取景基准距离的倍数。
        /// <para>
        /// 这张表是按解剖比例的一版估法，只影响镜头、不影响写回；目视觉得不合适就改数值。
        /// </para>
        /// </summary>
        public void FocusOn(string sectionId)
        {
            if (_visibleHeight <= 0.001f || _baseFitDistance <= 0.001f || sectionId == null)
            {
                return;
            }

            FocusPreset preset;
            switch (sectionId)
            {
                case "head":
                    preset = new FocusPreset { HeightFrac = 0.95f, Zoom = 0.55f };
                    break;
                case "eyes":
                    preset = new FocusPreset { HeightFrac = 0.98f, Zoom = 0.42f };
                    break;
                case "cheeks":
                    preset = new FocusPreset { HeightFrac = 0.94f, Zoom = 0.48f };
                    break;
                case "chin":
                    preset = new FocusPreset { HeightFrac = 0.90f, Zoom = 0.50f };
                    break;
                case "neck":
                    preset = new FocusPreset { HeightFrac = 0.82f, Zoom = 0.72f };
                    break;
                case "torso":
                    preset = new FocusPreset { HeightFrac = 0.72f, Zoom = 1.45f };
                    break;
                case "limbs":
                    preset = new FocusPreset { HeightFrac = 0.42f, Zoom = 1.90f };
                    break;
                case "proportions":
                    preset = new FocusPreset { HeightFrac = 0.55f, Zoom = 2.20f };
                    break;
                default:
                    preset = new FocusPreset { HeightFrac = 0.62f, Zoom = 2.20f };
                    break;
            }

            _targetCenterY = -_visibleHeight * preset.HeightFrac;
            _targetFitDistance = _baseFitDistance * preset.Zoom;
            _focusAnimating = true;
        }

        /// <summary>分区取景的平滑过渡：距离与焦点都朝目标靠（约 0.25 秒收敛），每帧一次。</summary>
        private void TickFocusAnimation(float deltaTime)
        {
            if (!_focusAnimating)
            {
                return;
            }

            float t = Mathf.Clamp(deltaTime * 10f, 0f, 1f);
            _fitDistance = Mathf.Lerp(_fitDistance, _targetFitDistance, t);
            _fitCenterY = Mathf.Lerp(_fitCenterY, _targetCenterY, t);

            if (Mathf.Abs(_fitDistance - _targetFitDistance) < 0.05f
                && Mathf.Abs(_fitCenterY - _targetCenterY) < 0.05f)
            {
                _fitDistance = _targetFitDistance;
                _fitCenterY = _targetCenterY;
                _focusAnimating = false;
            }

            UpdateCameraTransform();
        }

        /// <summary>
        /// 是否显示**蒙皮**身体（<see cref="TemplateSkinnedBodyModelPath"/>）。
        /// 身体参数只能写在这具上；但它自带一颗头，头页签必须关掉，否则脸被它挡住（2026-10-19 截图实证）。
        /// </summary>
        public void SetSkinnedBodyVisible(bool visible)
        {
            if (_skinnedBodyVisible == visible)
            {
                return;
            }

            _skinnedBodyVisible = visible;
            if (visible)
                ApplySkinnedBodyResources();
            _framed = false;   // 可见身体换了 ⇒ 重新取景
            ApplyBodyVisibility();
            UpdateCameraTransform();
        }

        /// <summary>
        /// 选角页的「完整 MetaHuman 外观」模式：蒙皮身体 + Ada 头同屏，头用身体姿态对齐。
        /// 进模式会顺带打开蒙皮身体与 Ada 头；退出恢复旧规则（身体可见时头收起）。
        /// </summary>
        public void SetMetaHumanLookMode(bool enabled)
        {
            if (_metaHumanLookMode == enabled)
            {
                return;
            }

            _metaHumanLookMode = enabled;
            if (enabled)
            {
                _metaHumanHeadVisible = true;
                _skinnedBodyVisible = true;
                _headMasterPosed = false;
                ApplySkinnedBodyResources();
            }
            else
            {
                _headMasterPosed = false;
            }

            _framed = false;
            ApplyBodyVisibility();
            UpdateCameraTransform();
        }

        /// <summary>是否用模板身体（静态网格）取代按性别取的完整角色身体。</summary>
        public void SetTemplateBodyVisible(bool visible)
        {
            _templateBodyVisible = visible;
            try
            {
                // 可见的身体换了 ⇒ 必须重新取景。取景的"重新算"只挂在"身体 SkinnedModel 变了"这条判据上
                //（见 Update 里 _lastFramedModel 那段），而切模板身体并不改 _animatedModel.SkinnedModel，
                // 不手动清 _framed 就会拿着旧身体的包围盒参数去框新身体（人很小 / 头顶被切）。
                _framed = false;
                if (visible)
                    ApplyTemplateBodyResources();
                ApplyBodyVisibility();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CharacterPreview3D] SetTemplateBodyVisible({visible}) 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 是否允许显示按性别取的完整角色身体（<see cref="_animatedModel"/>）。
        /// 头页签关掉它，避免它自带的头与 Ada 脸重叠；身体页签打开它，身体参数才有可见的写回目标。
        /// </summary>
        public void SetAnimatedBodyVisible(bool visible)
        {
            if (_animatedBodyVisible == visible)
                return;
            _animatedBodyVisible = visible;
            _framed = false;
            ApplyBodyVisibility();
            UpdateCameraTransform();
        }

        /// <summary>
        /// 是否允许显示 Ada 脸（<see cref="_headModel"/>）。身体页签关掉它，只留完整身体。
        /// </summary>
        private int _hairStyleIndex;

        /// <summary>
        /// 预览里的发型下标（模板参数 hair.style）。MetaHumanCharacter 挂在常驻宿主上，
        /// 不在预览 Actor 的父级链里，所以由创角页在 OnApplied 时推过来，而不是挂绑定器自己找宿主。
        /// 换模会重置材质槽，换完把发色虚拟实例指回去。
        /// </summary>
        public void SetHairStyle(int index)
        {
            if (_previewHair == null)
            {
                return;
            }

            index = HundunWorld.Game.Character.HairStyleCatalog.Clamp(index);
            if (index == _hairStyleIndex && _previewHair.Model != null)
            {
                return;
            }

            Model asset = Content.Load<Model>(HundunWorld.Game.Character.HairStyleCatalog.PathFor(index), 10000.0);
            if (asset == null || !asset.IsLoaded)
            {
                FlaxEngine.Debug.LogWarning("[CharacterPreview3D] 发型模型未就绪: "
                    + HundunWorld.Game.Character.HairStyleCatalog.PathFor(index));
                return;
            }

            _previewHair.Model = asset;
            if (_previewHairMat != null)
            {
                _previewHair.SetMaterial(0, _previewHairMat);

                // 卡片切边靠材质参数 MaskTex（RootUVSeedCoverage 的 alpha）；换发型不换掩码会用错形状。
                Texture mask = Content.Load<Texture>(
                    HundunWorld.Game.Character.HairStyleCatalog.MaskPathFor(index), 10000.0);
                if (mask != null && mask.IsLoaded)
                {
                    _previewHairMat.SetParameterValue("MaskTex", mask);
                }
            }

            _hairStyleIndex = index;
            FlaxEngine.Debug.Log("[CharacterPreview3D] 预览发型 -> "
                + HundunWorld.Game.Character.HairStyleCatalog.NameFor(index));
        }

        /// <summary>
        /// 预览里的发色下标（与 <c>CcFacePresets.HairColors</c>、世界侧 <c>MH_Hair</c> 的 HairColor 参数同一调色板）。
        /// </summary>
        public void SetHairColorIndex(int index)
        {
            _previewHairColorIndex = index;
            ApplyHairColor();
        }

        private void ApplyHairColor()
        {
            Color color = CurrentHairColor();
            if (_previewHairMat != null)
            {
                _previewHairMat.SetParameterValue("HairColor", color);
            }

            // 眉毛跟随发色（UE 的眉毛 groom 也是同一套发色语义），避免"头发银白、眉毛仍深棕"。
            if (_previewBrowMat != null)
            {
                _previewBrowMat.SetParameterValue(
                    "HairColor", HundunWorld.Game.Character.MetaHumanEyebrowBinder.DarkenForBrows(color));
            }
        }

        private Color CurrentHairColor()
        {
            Color[] palette = HundunWorld.Game.UI.Ink.Pages.CharacterCreation.CcFacePresets.HairColors;
            return palette.Length > 0
                ? palette[Mathf.Clamp(_previewHairColorIndex, 0, palette.Length - 1)]
                : new Color(0.10f, 0.06f, 0.035f);
        }

        /// <summary>把眉毛卡片挂到头部；眉骨/眉毛是头模型同空间的静态卡片，零变换即可。</summary>
        private void AttachPreviewBrows()
        {
            if (_previewBrows != null)
            {
                return;
            }

            _previewBrows = HundunWorld.Game.Character.MetaHumanEyebrowBinder.Attach(
                _headModel, CurrentHairColor(), "CharacterPreview3D_Brows", out _previewBrowMat);
        }

        public void SetMetaHumanHeadVisible(bool visible)
        {
            if (_metaHumanHeadVisible == visible)
                return;
            _metaHumanHeadVisible = visible;
            ApplyBodyVisibility();
        }

        /// <summary>强制重新自动取景（页签切换后可见身体变了，但可能没有哪个开关真的从 true 变到 false）。</summary>
        public void Reframe()
        {
            _framed = false;
            _fitTimer = 0f;
        }

        /// <summary>按角度旋转模型（正数向右转），供页面的左旋/右旋按钮调用。</summary>
        public void RotateByDegrees(float degrees)
        {
            _modelYaw = WrapAngle(_modelYaw + Mathf.DegreesToRadians * degrees);
            ApplyModelYaw();
        }

        /// <summary>按比例缩放（&lt;1 拉近、&gt;1 拉远），供页面的放大/缩小按钮调用。</summary>
        public void ZoomBy(float factor)
        {
            if (factor <= 0f || _baseFitDistance <= 0.001f)
                return;

            float next = Mathf.Clamp(_fitDistance * factor,
                _baseFitDistance * ZoomMinScale, _baseFitDistance * ZoomMaxScale);
            _focusAnimating = false;
            _targetFitDistance = next;
            _targetCenterY = _fitCenterY;
            _fitDistance = next;
            UpdateCameraTransform();
        }

        /// <summary>重置视角：模型朝向与俯仰回零，相机回到自动取景距离。</summary>
        public void ResetView()
        {
            _modelYaw = 0f;
            _cameraPitchDeg = 0f;
            ApplyModelYaw();

            if (_baseFitDistance > 0.001f)
            {
                _focusAnimating = false;
                _targetFitDistance = _baseFitDistance;
                _fitDistance = _baseFitDistance;
                _targetCenterY = _fitCenterY;
                UpdateCameraTransform();
            }
        }

        /// <summary>把模板身体模型装上。失败只记警告（<see cref="ApplyBodyVisibility"/> 会自动退回原身体）。</summary>
        private void ApplyTemplateBodyResources()
        {
            if (_templateBody == null)
                return;

            var path = HundunWorld.Game.Equipment.EquipmentDatabase.TemplateBodyModelPath;
            var model = Content.Load<Model>(path, 10000.0);
            if (model == null || !model.IsLoaded)
            {
                FlaxEngine.Debug.LogWarning($"[CharacterPreview3D] 模板身体未就绪，退回按性别取的身体: {path}");
                return;
            }

            if (!ReferenceEquals(_appliedTemplateBody, model))
            {
                _templateBody.Model = model;
                _appliedTemplateBody = model;
                var mb = _templateBody.Box;
                FlaxEngine.Debug.Log($"[CharacterPreview3D] 模板身体已装载: {path}"
                    + $" | 包围盒 center={mb.Center} size={mb.Size}");
            }

            // 蒙皮身体：身体参数的唯一可写目标。装载失败只告警，退回静态模板（看得见但改不动）。
            // 只有身体页签真正要显示它时才装：头页签装了它会让 CollectSkinnedModels 把按性别身体排除掉。
            if (_skinnedBodyVisible)
            {
                ApplySkinnedBodyResources();
            }

            ApplyHeadUpright();

            // 一次性几何自证：肉眼读截图判不出"手在上还是脚在上"，让运行态自己报数。
            ReportPreviewGeometryOnce();
        }

        /// <summary>
        /// 装载蒙皮身体资产。身体页签显示它时才走这条（模板身体与它互相独立，别因为模板身体关着就不装）。
        /// </summary>
        private void ApplySkinnedBodyResources()
        {
            if (_skinnedBody == null)
                return;

            string skinnedPath = HundunWorld.Game.Equipment.EquipmentDatabase.TemplateSkinnedBodyModelPath;
            var skinned = Content.Load<SkinnedModel>(MetaHuman.Runtime.MetaHumanAssetUtil.ResolveProjectPath(skinnedPath), 10000.0);
            if (skinned != null && skinned.IsLoaded)
            {
                if (!ReferenceEquals(_appliedSkinnedBody, skinned))
                {
                    _skinnedBody.SkinnedModel = skinned;
                    _appliedSkinnedBody = skinned;
                    FlaxEngine.Debug.Log($"[CharacterPreview3D] 蒙皮身体已装载: {skinnedPath}");
                }
            }
            else
            {
                FlaxEngine.Debug.LogWarning($"[CharacterPreview3D] 蒙皮身体未就绪（身体滑杆将无可写目标）: {skinnedPath}");
            }
        }

        /// <summary>
        /// 把模板头（<c>MetaHuman_Conform_Topology/head.flax</c>）装上。失败只记警告，
        /// <see cref="ApplyBodyVisibility"/> 会退回 Ada 脸（那条才有 821 个可写形状）。
        /// </summary>
        private void ApplyTemplateHeadResources()
        {
            if (_templateHead == null)
                return;

            var path = HundunWorld.Game.Equipment.EquipmentDatabase.TemplateHeadModelPath;
            var model = Content.Load<Model>(path, 10000.0);
            if (model == null || !model.IsLoaded)
            {
                FlaxEngine.Debug.LogWarning($"[CharacterPreview3D] 模板头未就绪，退回 Ada 脸: {path}");
                return;
            }

            if (!ReferenceEquals(_appliedTemplateHead, model))
            {
                _templateHead.Model = model;
                _appliedTemplateHead = model;
                var hb = _templateHead.Box;
                FlaxEngine.Debug.Log($"[CharacterPreview3D] 模板头已装载: {path}"
                    + $" | 包围盒 center={hb.Center} size={hb.Size}");
            }
        }

        /// <summary>
        /// 是否用模板头取代 Ada 脸。默认 **关**：模板头是静态网格、源 FBX 里连
        /// <c>Deformer</c>/<c>BlendShapeChannel</c> 节点都没有（<c>Tools/_fbx_has_blendshapes.py</c>
        /// 实测 6 类节点全 0）⇒ 换上它，捏脸通道就无处可写。
        /// 它唯一的价值是"与模板身体同代同度量系 ⇒ 颈缝天然为零"，所以留成显式开关供目视对照。
        /// </summary>
        public void SetTemplateHeadVisible(bool visible)
        {
            _templateHeadVisible = visible;
            try
            {
                if (visible)
                    ApplyTemplateHeadResources();
                ApplyBodyVisibility();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CharacterPreview3D] SetTemplateHeadVisible({visible}) 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 一次性把"预览里这两具 Actor 到底怎么摆的"打进日志（世界系，含 Ada 头的头骨/颈骨世界 Y）。
        /// <para>
        /// 为什么需要：2026-10-16 用户实测"模型倒立"，而我按资产包围盒与源 FBX 分档展幅推出的结论
        /// （两具都正立）与画面矛盾 —— 包围盒的 y 区间与符号无关，**判不出朝向**，不能再靠读图猜。
        /// 这条自证给的是可判定的关系：头的世界 Y 该高于身体世界 Y；`head` 骨的世界 Y 该高于 `neck_01`。
        /// 任何一条反了，就直接指出反的是哪一具。
        /// </para>
        /// </summary>
        private void ReportPreviewGeometryOnce()
        {
            if (_geometryReported || _templateBody == null || _headModel == null || _animatedModel == null)
                return;
            if (!TemplateBodyInUse || _headModel.SkinnedModel == null)
                return;

            _geometryReported = true;

            var bodyBox = _templateBody.Box;
            var headBox = _headModel.Box;
            var bodyEuler = _templateBody.EulerAngles;
            var headEuler = _headModel.EulerAngles;

            string boneLine = "头骨/颈骨未取到";
            string headBone = FindBoneName(_headModel.SkinnedModel, HeadBoneCandidates);
            string neckBone = FindBoneName(_headModel.SkinnedModel, NeckBoneCandidates);
            if (headBone != null && neckBone != null)
            {
                Matrix hm, nm;
                _headModel.GetNodeTransformation(headBone, out hm, true);
                _headModel.GetNodeTransformation(neckBone, out nm, true);
                boneLine = string.Format("{0}世界y={1:F2} , {2}世界y={3:F2} , 头-颈={4:+0.00;-0.00} ⇒ {5}",
                    headBone, hm.TranslationVector.Y, neckBone, nm.TranslationVector.Y,
                    hm.TranslationVector.Y - nm.TranslationVector.Y,
                    (hm.TranslationVector.Y - nm.TranslationVector.Y) > 0f ? "头骨在颈骨之上(正立)" : "**头骨在颈骨之下(倒立)**");
            }

            FlaxEngine.Debug.Log(string.Format(
                "[PreviewGeom] 模板身体 世界box center={0} size={1} 欧拉={2}",
                bodyBox.Center, bodyBox.Size, bodyEuler));
            FlaxEngine.Debug.Log(string.Format(
                "[PreviewGeom] Ada头   世界box center={0} size={1} 欧拉={2}",
                headBox.Center, headBox.Size, headEuler));
            FlaxEngine.Debug.Log(string.Format(
                "[PreviewGeom] 判据A 头中心y={0:F2} vs 身中心y={1:F2} , 差={2:+0.00;-0.00} ⇒ {3}",
                headBox.Center.Y, bodyBox.Center.Y, headBox.Center.Y - bodyBox.Center.Y,
                (headBox.Center.Y - bodyBox.Center.Y) > 0f ? "头在身体之上(组合朝向一致)" : "**头在身体中心之下(两具朝向相反或头落错格)**"));
            FlaxEngine.Debug.Log(string.Format(
                "[PreviewGeom] 判据B 身box y∈[{0:F2},{1:F2}] 高={2:F2} ; 头box y∈[{3:F2},{4:F2}] 高={5:F2}",
                bodyBox.Minimum.Y, bodyBox.Maximum.Y, bodyBox.Size.Y,
                headBox.Minimum.Y, headBox.Maximum.Y, headBox.Size.Y));
            FlaxEngine.Debug.Log("[PreviewGeom] 判据C " + boneLine);

            // 判据E：脸的**朝向**用眼睛当锚点（眼睛在脸的正面）。
            // 要报两份：模型空间 Δ 是"这具资产固有的脸在哪一侧"，世界空间 Δ 才含 Actor 自己的姿态 ——
            // 只报模型空间的那份自证不了"我已经把它转回来了"（2026-10-16 就是这么漏判的）。
            // 相机在 focus 的 +Z 侧回望 ⇒ 世界 Δz > 0 才是正对相机。
            string faceLine = "未找到 eye 骨，朝向无法自证";
            string eyeBone = FindBoneByName(_headModel.SkinnedModel, "eye");
            if (eyeBone != null && headBone != null)
            {
                Matrix em, hbm, emW, hbmW;
                _headModel.GetNodeTransformation(eyeBone, out em, false);
                _headModel.GetNodeTransformation(headBone, out hbm, false);
                _headModel.GetNodeTransformation(eyeBone, out emW, true);
                _headModel.GetNodeTransformation(headBone, out hbmW, true);
                float dzModel = em.TranslationVector.Z - hbm.TranslationVector.Z;
                float dzWorld = emW.TranslationVector.Z - hbmW.TranslationVector.Z;
                float dyWorld = emW.TranslationVector.Y - hbmW.TranslationVector.Y;
                faceLine = string.Format(
                    "眼骨={0} 模型Δz={1:+0.00;-0.00} 世界Δz={2:+0.00;-0.00} 世界Δy={3:+0.00;-0.00} ⇒ {4}",
                    eyeBone, dzModel, dzWorld, dyWorld,
                    System.Math.Abs(dzWorld) >= System.Math.Abs(dyWorld)
                        ? (dzWorld > 0f ? "脸朝 +Z（正对相机）√" : "**脸朝 -Z（背对相机）**")
                        : "世界Δz 与 Δy 同量级，锚点不足以判（换鼻梁/鼻尖）");
            }
            FlaxEngine.Debug.Log("[PreviewGeom] 判据E " + faceLine);
            FlaxEngine.Debug.Log(string.Format(
                "[PreviewGeom] 判据D 角色身体(隐藏中) 欧拉={0} ; 转盘父节点 欧拉={1}",
                _animatedModel.EulerAngles, _modelRoot != null ? _modelRoot.EulerAngles.ToString() : "<无>"));

            // 判据F（#57 捏脸无效果的定罪）：捏脸只能写在"有形状通道的头"上，
            // 所以这条同时报三件事——预览头挂的是哪份资产、它到底有多少个形状通道、
            // 这具 Actor 上有没有 MetaHumanCharacter 脚本、脚本收到的写回目标是不是它本身。
            // 只看画面永远分不清"没写"和"写到了别处"。
            var shapes = _headModel.SkinnedModel.BlendShapes;
            var mh = _headModel.GetScript<MetaHuman.Runtime.MetaHumanCharacter>();
            FlaxEngine.Debug.Log(string.Format(
                "[PreviewGeom] 判据F Ada头资产={0} 形状通道={1} 本Actor上的MetaHumanCharacter={2} Actor启用={3}",
                _headModel.SkinnedModel.Path, shapes == null ? "<null>" : shapes.Length.ToString(),
                mh == null ? "无（页面绑到了别处）" : "有", _headModel.IsActive));
        }

        /// <summary>
        /// 唯一改 <see cref="_templateBody"/> / <see cref="_animatedModel"/> 启用位的地方
        /// （两者互斥：模板身体能上，角色身体就隐）。
        /// <para>
        /// 必须"值真的变了才写"：脚本 tick 里反复写 Actor 启用位会让引擎一次次销毁/重建托管脚本对象，
        /// 实测以 <c>ScriptingObject::ChangeID ← OnScriptingDispose</c> 的空句柄崩溃收场。
        /// </para>
        /// </summary>
        private void ApplyBodyVisibility()
        {
            bool templateReady = _templateBodyVisible && _templateBody != null
                                 && _templateBody.Model != null && _templateBody.Model.IsLoaded;

            // 有蒙皮身体就优先显示蒙皮那具：静态那具改不动，看得见反而误导。
            bool skinnedReady = _skinnedBodyVisible && _skinnedBody != null
                                && _skinnedBody.SkinnedModel != null && _appliedSkinnedBody != null;
            if (_skinnedBody != null && _skinnedBody.IsActive != skinnedReady)
                _skinnedBody.IsActive = skinnedReady;
            if (skinnedReady)
                templateReady = false;

            if (_templateBody != null && _templateBody.IsActive != templateReady)
                _templateBody.IsActive = templateReady;

            if (_animatedModel != null)
            {
                bool wantAnimatedBody = !templateReady && _animatedBodyVisible && _appliedBodyModel != null;
                if (_animatedModel.IsActive != wantAnimatedBody)
                    _animatedModel.IsActive = wantAnimatedBody;
            }

            // 头这一侧同样是"两具互斥"：模板头能上，Ada 脸就隐（它带 821 形状，是捏脸的唯一目标，
            // 所以模板头默认不开，只在这里等显式开关）。判据用 _appliedTemplateHead 而不是
            // 每次都读 _templateHead.Model —— Actor 可能被宿主场景销毁，读它会以异常代替报告。
            bool templateHeadReady = _templateHeadVisible && _templateHead != null
                                     && _appliedTemplateHead != null;
            if (_templateHead != null && _templateHead.IsActive != templateHeadReady)
                _templateHead.IsActive = templateHeadReady;

            if (_headModel != null)
            {
                // 身体页签显示蒙皮身体时把 Ada 头也藏掉：这具身体没有头，Ada 头会浮在颈口上像一个灰蛋
                // （2026-10-19 截图实证）。脸只在头页签看。
                // 例外：完整外观模式（选角页）两具同屏，头由 master pose 跟随身体姿态对齐。
                bool wantAdaHead = _metaHumanHeadVisible && !templateHeadReady
                                   && (_metaHumanLookMode || !skinnedReady) && _appliedHeadModel != null;
                if (_headModel.IsActive != wantAdaHead)
                    _headModel.IsActive = wantAdaHead;

                if (wantAdaHead && _metaHumanLookMode && _skinnedBody != null && _skinnedBody.IsActive
                    && !_headMasterPosed)
                {
                    try
                    {
                        // 头与蒙皮身体同源（都是 Ada 厘米资产）：Actor 用同一变换，骨骼姿态交给 master pose。
                        // 不能复用 AlignHeadToBodyByBone（它拿的是占位身体 _animatedModel，尺度比会算错）。
                        _headModel.Orientation = _skinnedBody.Orientation;
                        _headModel.Scale = _skinnedBody.Scale;
                        _headModel.LocalPosition = _skinnedBody.LocalPosition;
                        _headModel.SetMasterPoseModel(_skinnedBody);
                        _headMasterPosed = true;
                        FlaxEngine.Debug.Log("[CharacterPreview3D] MetaHuman 外观模式：头已跟随身体姿态");
                    }
                    catch (Exception ex)
                    {
                        FlaxEngine.Debug.LogWarning("[CharacterPreview3D] SetMasterPoseModel 失败: " + ex.Message);
                    }
                }

                // 头发是头的子 Actor，但 IsActive 不随父级传播：头藏掉时必须单独收，
                // 否则身体页签会留下一团浮在空中的头发。
                if (_previewHair != null && _previewHair.IsActive != wantAdaHead)
                    _previewHair.IsActive = wantAdaHead;

                // 眉毛同理：头藏掉时单独收，避免身体页签留下两条浮空的眉毛。
                if (_previewBrows != null && _previewBrows.IsActive != wantAdaHead)
                    _previewBrows.IsActive = wantAdaHead;
            }
        }

        /// <summary>
        /// 当前真正会被渲染的那具身体是否是模板身体。
        /// 取景（<see cref="TryAutoFrame"/>）必须按**可见的那具**量包围盒：
        /// 用隐形的角色身体去取景，画面里就会出现"人很小、留一大片空"。
        /// </summary>
        private bool TemplateBodyInUse
        {
            get
            {
                return _templateBodyVisible && _templateBody != null
                       && _templateBody.Model != null && _templateBody.Model.IsLoaded;
            }
        }

        /// <summary>
        /// 程序化待机动画（基础角色动画）：在绑定姿态基础上叠加极小幅度的呼吸/摆动，
        /// 通过骨骼级 SetNodeTransformation 直接驱动（不依赖动画图资产）；
        /// 在 AnimationUpdated 事件中应用（引擎动画求值完成后），确保不被覆盖。
        /// </summary>
        private void ApplyProceduralIdle()
        {
            if (!_idlePoseCaptured || _animatedModel == null)
                return;

            float t = Time.GameTime;
            // 呼吸（脊椎三节微起仄）、头部轻摆、双臂微晃（左右反相）
            for (int slot = 0; slot < _idleResolvedBones.Length; slot++)
            {
                var bone = _idleResolvedBones[slot];
                if (bone == null) continue;

                ApplyBoneIdle(bone, t, IdleSlotFrequency[slot], IdleSlotAmplitude[slot], IdleSlotPhase[slot]);
            }

            if (!_loggedIdleApplied)
            {
                _loggedIdleApplied = true;
                FlaxEngine.Debug.Log($"[CharacterPreview3D] 待机动画已应用（骨骼 {_idleBasePose.Count} 根）");
            }
        }

        /// <summary>对单根骨骼应用微小摆动：基准矩阵 × 局部小旋转（绕骨骼自身原点，不会位移）。</summary>
        private void ApplyBoneIdle(string boneName, float t, float freq, float ampDeg, float phase)
        {
            if (!_idleBasePose.TryGetValue(boneName, out var baseM))
                return;

            float angle = Mathf.Sin(t * freq * Mathf.TwoPi + phase) * ampDeg;
            var delta = Quaternion.Euler(angle * 0.5f, angle, angle * 0.35f);
            _animatedModel.SetNodeTransformation(boneName, baseM * Matrix.RotationQuaternion(delta), false);
        }

        /// <summary>捕获基础骨骼姿态（模型就绪且绑定姿态稳定后执行一次；使待机动画每帧基于同一基准无漂移）。</summary>
        private void CaptureIdleBasePose()
        {
            if (_animatedModel == null || _animatedModel.SkinnedModel == null)
                return;

            var model = _animatedModel.SkinnedModel;
            for (int i = 0; i < _idleResolvedBones.Length; i++)
                _idleResolvedBones[i] = null;
            _idleBasePose.Clear();

            for (int slot = 0; slot < IdleBoneSlots.Length; slot++)
            {
                var bone = FindBoneName(model, IdleBoneSlots[slot]);
                if (bone == null) continue;

                _animatedModel.GetNodeTransformation(bone, out Matrix m, false);
                // 全零矩阵 = 骨骼不存在（跳过）
                if (m.M11 == 0f && m.M44 == 0f) continue;

                _idleResolvedBones[slot] = bone;
                _idleBasePose[bone] = m;
            }

            _idlePoseCaptured = _idleBasePose.Count > 0;

            if (_idleBasePose.Count == 0)
            {
                // 一根都没命中时必须说出来：否则表现是"模型在、但完全不动"，
                // 只改模型路径永远查不到这一层
                FlaxEngine.Debug.LogWarning($"[CharacterPreview3D] 待机动画未命中任何骨骼" +
                                            $"（骨架 {model.Nodes?.Length ?? 0} 根节点，命名与 " +
                                            $"{IdleBoneSlots[0][0]}/{IdleBoneSlots[5][0]} 等候选都不符）→ 预览只会静止站立");
                return;
            }

            FlaxEngine.Debug.Log($"[CharacterPreview3D] 待机动画骨骼捕获：{_idleBasePose.Count}/{IdleBoneSlots.Length}");
        }

        /// <summary>
        /// 在骨架节点里按候选名顺序找第一根命中的骨骼。大小写不敏感——
        /// FBX 不同导出工具链的命名大小写并不统一（<c>LeftUpperArm</c> / <c>leftupperarm</c>）。
        /// </summary>
        private static string FindBoneName(SkinnedModel model, string[] candidates)
        {
            var nodes = model.Nodes;
            if (nodes == null) return null;

            foreach (var candidate in candidates)
            {
                for (int i = 0; i < nodes.Length; i++)
                {
                    // 返回资产里的真实拼写：SetNodeTransformation/GetNodeTransformation 按名字查节点
                    if (string.Equals(nodes[i].Name, candidate, StringComparison.OrdinalIgnoreCase))
                        return nodes[i].Name;
                }
            }

            return null;
        }

        /// <summary>
        /// 按**子串**找第一根骨骼（大小写不敏感）。
        /// 只给一次性朝向自证用：眼骨的真实拼写在 FBX/glTF 两条工具链间不统一
        /// （<c>eyeLeft</c> / <c>DEF-eye_l</c> / <c>eye_01_l</c>），按候选名穷举不如按子串捞。
        /// </summary>
        private static string FindBoneByName(SkinnedModel model, string substring)
        {
            var nodes = model.Nodes;
            if (nodes == null || string.IsNullOrEmpty(substring))
                return null;

            for (int i = 0; i < nodes.Length; i++)
            {
                string name = nodes[i].Name;
                if (!string.IsNullOrEmpty(name) && name.IndexOf(substring, StringComparison.OrdinalIgnoreCase) >= 0)
                    return name;
            }

            return null;
        }

        /// <summary>
        /// 自动取景：模型就绪后，依据实际包围盒：
        /// ① 水平居中 + 脚底贴地（使 Yaw 旋转轴落在角色自身垂直中轴线上）；
        /// ② 垂直/水平双向适配计算取景距离（含 18% 边距）——全身完整入画、比例协调、永不裁切；
        /// ③ 同步近/远裁剪面（适配从厘米到米级的不同资产尺度）。
        /// 只在模型就绪后执行一次；资源切换时由 Update 重置后重取景。
        /// </summary>
        private void TryAutoFrame(float deltaTime)
        {
            if (_framed || _animatedModel == null)
                return;

            // 按"当前可见的那具身体"取景：模板身体模式下 _animatedModel 是隐的，
            // 拿它的包围盒去取景会得到"人很小 + 一大片空"。
            bool useTemplate = TemplateBodyInUse;
            bool useSkinned = _skinnedBodyVisible && _skinnedBody != null && _appliedSkinnedBody != null;
            // 头页签（只剩 Ada 脸）按**头自己的包围盒**取景：拿隐藏的整身去取景会把头缩成一个小点。
            bool useHead = !useTemplate && !useSkinned && !_animatedBodyVisible && _metaHumanHeadVisible
                           && _headModel != null && _headModel.SkinnedModel != null && _appliedHeadModel != null;
            if (!useTemplate && !useSkinned && !useHead
                && (_animatedModel.SkinnedModel == null || !_animatedModel.SkinnedModel.IsLoaded))
                return;

            _fitTimer += deltaTime;
            if (_fitTimer < 0.5f)
                return; // 等待绑定姿态（扶正/动画初始状态）生效后再测量

            // **先摆姿态，再量盒子**。`_templateBody` 是在 `InitializeActors` 里 `ApplyModelUpright()`
            // 之后才创建的，所以它创建时没人给它 Rx(180)；若在这里量完才摆，量到的是"扶正前"的盒子，
            // 贴地锚点（世界最大 Y = 脚）会取到颈顶那一端，整具人差一整个身高（2026-10-17 回执实测：
            // 一次性几何自证里模板身体欧拉还是 (0,0,0)）。摆姿态是幂等的，多调一次没有副作用。
            ApplyModelUpright();

            // 蒙皮身体用"资产 LOD0 变换到世界"的盒子，不用 Actor.Box（后者把整副骨架都算进去，见 helper）。
            var box = useTemplate
                ? _templateBody.Box
                : (useSkinned ? SkinnedWorldBox(_skinnedBody) : SkinnedWorldBox(_animatedModel));
            // 身高取哪一格：模板身体只到**颈顶**（142.35），而 FramingHeightFrac 那套比例
            // （0.78 = 头肩）是按"到**头顶**的整身高"标定的 —— 直接拿 142 去算，取景中心会落到胸口、
            // 把头顶切掉。模板模式下改用模板头的顶点高度（171.98），标定口径就与既有页一致。
            // 头页签同理：Ada 头按隐藏的整身盒子定位/对齐，取景高度仍用整身高，否则会怼到脸上。
            float h = (useTemplate || useHead || _metaHumanLookMode)
                ? HundunWorld.Game.Equipment.EquipmentDatabase.TemplateHeadCrownY
                : box.Size.Y;
            if (h <= 0.001f)
                return; // 包围盒尚未就绪，下帧再试

            // ① 水平居中 + 脚底贴地：偏移施加在**模型自身**上，
            //    父节点（Yaw 旋转载体）保持在原点——旋转轴恰好穿过角色自身垂直中轴（原地自转、不绕摆）。
            _modelRoot.Position = StageOffset;
            // 两条与"离屏 RenderTexture 竖直翻转出图"绑在一起的口径（2026-10-17 用户截图"整屏只有腿"逼出来）：
            //   · 世界 +Y 在屏幕上是**往下** ⇒ 扶正过的模型，脚在世界包围盒的**最大 Y**那一端，
            //     所以"贴地"要贴 Maximum 而不是 Minimum（旧写法把整套人往下挪了整整一个身高）。
            //   · 新偏移 = 旧偏移 + (目标 − 实测) 才幂等。旧写法 `−(实测 − 舞台)` 只在"旧偏移为 0"
            //     的第一次取景成立，换性别/换资产重取景会整段漂掉一个旧偏移。
            _modelLocalOffset += new Vector3(
                StageOffset.X - box.Center.X,
                StageOffset.Y - box.Maximum.Y,
                StageOffset.Z - box.Center.Z);
            ApplyModelUpright();

            // ② 双向适配取景距离（垂直 FOV，水平按控件宽高比换算；留 18% 边距）
            float fovRad = Mathf.DegreesToRadians * CameraFieldOfView;
            float halfTan = Mathf.Max(Mathf.Tan(fovRad * 0.5f), 0.01f);
            float aspect = Width > 4f && Height > 4f ? Width / Height : 1f;
            float horizontalHalf = Mathf.Max(box.Size.X, box.Size.Z) * 0.5f;
            float distV = (h * 0.5f) / halfTan;
            float distH = horizontalHalf / (halfTan * aspect);
            // 取景系数默认 1 / 0.5，此时与原始公式逐项相等（既有调用方行为不变）
            float framingScale = FramingScale <= 0f ? 1f : FramingScale;
            _fitDistance = Mathf.Max(distV, distH) * 1.18f * framingScale;
            _baseFitDistance = _fitDistance;   // 滚轮缩放围绕这条基准上下钳制
            // 取景中心要**从脚底往屏幕上方**量。上面刚把脚贴到舞台面（世界 StageOffset.Y），
            // 而"屏幕上方"= 世界 −Y（出图翻转），所以这里必须是负号：
            // 旧写法 `+frac*h` 在 frac=0.5 时与正确值只差一个整体平移（相机跟着焦点走 ⇒ 全身页看不出问题），
            // 但 0.78 的头肩特写会被甩到脚踝 —— 2026-10-17 用户截图"整屏只有腿"就是这么来的。
            _fitCenterY = -h * Mathf.Clamp(FramingHeightFrac, 0.05f, 0.98f);

            // 分区取景按"可见高度的比例"换算焦点，基准就是这里量到的 h；
            // 同时把过渡目标对齐到当前值，避免重取景后镜头自己再飘一段。
            _visibleHeight = h;
            _targetFitDistance = _fitDistance;
            _targetCenterY = _fitCenterY;
            _focusAnimating = false;

            // ③ 裁剪面随取景距离自适应（防近平面裁切/远平面丢失）
            if (_camera != null)
            {
                _camera.NearPlane = Mathf.Max(0.01f, _fitDistance * 0.005f);
                _camera.FarPlane = _fitDistance * 20f;
            }

            _framed = true;
            _lastFramedModel = _animatedModel.SkinnedModel;
            UpdateCameraTransform();
            CaptureIdleBasePose(); // 基础姿态稳定后捕获（供程序化待机动画使用）

            FlaxEngine.Debug.Log(
                $"[CharacterPreview3D] 自动取景完成：身高={h:F2}, 宽={box.Size.X:F2}, 深={box.Size.Z:F2}, " +
                $"取景距离={_fitDistance:F1}, 视口={Width:F0}x{Height:F0}");
            // 把"离脚底多高"与"世界偏移"两个数一起报出来：出图是竖直翻转的，
            // 只看其中一个没法判断取景中心到底落在脸上还是脚踝（2026-10-17 就是这么误判过一次）。
            FlaxEngine.Debug.Log(string.Format(
                "[CharacterPreview3D] 取景中心: 离脚底={0:F2}cm (frac={1}) ; 世界偏移={2:F2}cm ; 可见竖向={3:F1}cm",
                h * Mathf.Clamp(FramingHeightFrac, 0.05f, 0.98f), FramingHeightFrac, _fitCenterY,
                2f * _fitDistance * halfTan));
        }

        /// <summary>
        /// 蒙皮 Actor 上"看得见的身体"的世界包围盒：拿**资产 LOD0**（绑定空间）乘 Actor 世界矩阵算出来。
        /// <para>
        /// 为什么不用 <c>Actor.Box</c>：它把整副骨架一起算进去。实测 <c>Character_1</c> 的 Actor.Box 是
        /// 345.18 × <b>325.06</b> × 54.50，而同一资产的 LOD0 只有 191.77 × <b>180.60</b> × 30.28
        /// （180.6cm 才是这个人的真实身高）。拿 325 当身高，0.78 的取景中心就落到"离脚底 253cm"——
        /// 比头顶还高 80cm ⇒ 人贴到框底、上面一大片空（2026-10-17 捏脸页回执实测）。
        /// 头那具更夸张：876 根面部骨骼把 Actor.Box 撑成资产实测值的 7.1 倍。
        /// </para>
        /// <para>取不到 LOD0 就退回 <c>Actor.Box</c> 并点名报一次：宁可框得松，不要静默算错。</para>
        /// </summary>
        private BoundingBox SkinnedWorldBox(AnimatedModel model)
        {
            var actorBox = model.Box;
            var asset = model.SkinnedModel;
            if (asset == null || !asset.IsLoaded || asset.LODsCount <= 0)
                return actorBox;

            try
            {
                var lod = asset.GetLOD(0);
                if (lod == null)
                    return actorBox;

                model.GetLocalToWorldMatrix(out var toWorld);
                return BoundingBox.Transform(lod.Box, toWorld);
            }
            catch (Exception ex)
            {
                if (!_lodBoxWarned)
                {
                    _lodBoxWarned = true;
                    FlaxEngine.Debug.LogWarning(
                        $"[CharacterPreview3D] 资产 LOD0 包围盒取不到，取景退回 Actor.Box: {ex.Message}");
                }

                return actorBox;
            }
        }

        // ===================================================================
        // 生命周期 & 清理
        // =======================================================================

        /// <summary>
        /// 宿主场景卸载防护：在 Actor 真正被销毁前把离屏渲染任务断链并降级为"待重建"。
        /// <para>
        /// 没有本守卫时，场景卸载销毁 _modelRoot/_lightRoot/_cameraRoot 后，
        /// Enabled 的 SceneRenderTask 仍持有这些原生指针，下一帧 RenderTask::DrawAll 在
        /// CollectPostFxVolumes(dynamic_cast) / OnCollectDrawCalls(AddActor) 处解引用野指针，
        /// 表现为 Array.h Last() 断言弹窗或 0xe06d7363 未处理异常崩溃。
        /// </para>
        /// </summary>
        private void OnLevelSceneUnloading(FlaxEngine.Scene scene, Guid sceneId)
        {
            if (_renderTask == null || scene == null || scene != _hostScene)
                return;

            FlaxEngine.Debug.Log("[CharacterPreview3D] 宿主场景正在卸载，断开离屏渲染任务以防野指针崩溃");

            _renderTask.Enabled = false;
            _renderTask.CustomActors = null;
            _renderTask.Camera = null;
            Level.SceneUnloading -= OnLevelSceneUnloading;

            // Actor 随场景销毁：清空全部引用， OnDestroy 的兜底销毁会自动跳过，避免二次销毁失效句柄
            _animatedModel = null;
            _camera = null;
            _keyLight = null;
            _fillLight = null;
            _rimLight = null;
            _modelRoot = null;
            _lightRoot = null;
            _cameraRoot = null;
            _stageRoot = null;
            _hostScene = null;
            _framed = false;
            _idlePoseCaptured = false;
            _uprightVerified = false;
            _initialized = false; // RefreshLayout 在新场景就绪后走重试路径重建
        }

        /// <inheritdoc />
        public override void OnDestroy()
        {
            _liveInstances--; // 临时诊断（虚影定位，查完即删）
            try
            {
                Level.SceneUnloading -= OnLevelSceneUnloading;

                if (_renderTask != null)
                {
                    _renderTask.Enabled = false;
                    _renderTask.CustomActors = null;
                    _renderTask.Camera = null;
                    FlaxEngine.Object.Destroy(_renderTask);
                    _renderTask = null;
                }

                _textureBrush = null;

                if (_renderTexture != null)
                {
                    _renderTexture.ReleaseGPU();
                    FlaxEngine.Object.Destroy(_renderTexture);
                    _renderTexture = null;
                }

                if (_modelRoot != null)
                {
                    FlaxEngine.Object.Destroy(_modelRoot);
                    _modelRoot = null;
                }

                if (_lightRoot != null)
                {
                    FlaxEngine.Object.Destroy(_lightRoot);
                    _lightRoot = null;
                }

                if (_cameraRoot != null)
                {
                    FlaxEngine.Object.Destroy(_cameraRoot);
                    _cameraRoot = null;
                }

                if (_stageRoot != null)
                {
                    FlaxEngine.Object.Destroy(_stageRoot);
                    _stageRoot = null;
                }

                if (_animatedModel != null)
                    _animatedModel.AnimationUpdated -= ApplyProceduralIdle;

                _animatedModel = null;
                _camera = null;
                _keyLight = null;
                _fillLight = null;
                _rimLight = null;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CharacterPreview3D] OnDestroy 清理失败: {ex.Message}");
            }

            base.OnDestroy();
        }
    }
}
