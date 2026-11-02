# GASP 动画系统方案与决策清单（2026-11-02）

参考实现：<https://github.com/MrWen33/GASP-Godot>（Epic GASP/UE 5.5 的 Godot 移植，含 Motion Matching、BlendStack、Traversal、FootPlacement/LegIK、IK Retargeter、相机）。
本工程宿主：Flax 1.13 + C#（`HundunWorld/Source/Game`），角色位移由 ECS 权威预测/网络同步，动画只做表现。

## 1. 目标与边界

最终目标是在本工程得到一套 **自己的 GASP**：角色移动由数据驱动选动画（Motion Matching），而不是手写状态机/混合参数；支持转向、停步、起跳/下落/落地，随后接入 Traversal（翻越/攀爬）、脚部贴地与 LegIK、相机 rig 与调试可视化。

非目标（第一版不做）：
- 不替换 ECS 权威移动/网络同步；GASP 只读 Actor 位姿并写骨骼。
- 不移植 UE 资产/内容；使用本工程已有的 ALS 动画（`Content/Animations/als/**`）与 Ada MetaHuman 骨架。
- MetaHuman 面部/手指的 GASP 化不在第一阶段；未覆盖骨骼继续由现有动画图提供。

## 2. 现状证据（决定架构的关键事实）

| 事实 | 证据 |
|---|---|
| 世界角色身体是 `Ada_BodyPreview4.flax`（343 节点，Y-up、厘米），头部另挂一个 AnimatedModel | `HundunWorldGame.cs`（`EnsureMetaHumanBody`）、`docs/MetaHuman_完成度审计_2026-10-22.md` |
| 本地位移由 ECS 预测 → `LocalPlayerActorSyncSystem` 写 Actor；远端由插值 → `FlaxActorSyncSystem` 写 Actor | `LocalPlayerActorSyncSystem.cs:118-151`、`FlaxActorSyncSystem.cs:956-1061` |
| 现有动画是 11 节点 Blend 图 + `LocomotionGraphDriver` 写参数（速度/转向/停步/空中） | `Character/LocomotionGraphDriver.cs`、`Tools/_animation_graph_make_locomotion.py` |
| ALS 剪辑以 Z-up 烘焙；`MetaHumanClipFrameFix` 每帧对整副姿态做 `R_x(-90)·M·R_x(+90)` 转正 | `Character/MetaHumanClipFrameFix.cs:15-16,69-78` |
| ALS 剪辑通道与 Ada 按名匹配 79/84（Run 目录实测） | 编辑器桥 `animation_inventory`（2026-11-01） |
| `AnimatedModel` 暴露 `GetCurrentPose/SetCurrentPose/SetNodeTransformation`，可在 C# 每帧写骨骼 | `Source/Engine/Level/Actors/AnimatedModel.h:245-313` |
| 动画资产 chunk0 是未压缩二进制曲线，可在离线工具里完整还原（v103：f64 时长/帧率 + 通道曲线） | `Source/Engine/Content/Assets/Animation.cpp:600-659`、`CurveSerialization.h:209-235` |
| 图节点 `Animation(9,2)` 的 box7=Start Position、box8=Animation Asset，配合 speed=0 可作为“任意时刻采样器” | `Source/Engine/Animations/Graph/AnimGroup.Animation.cpp:1081-1145` |

## 3. 架构决策

### D0. 落地首版的两个硬修正（2026-11-01 实测）
1. **Flax 二进制动画的关键帧时间是帧号，不是秒**：引擎在采样前执行 `animPos *= fps`（`AnimGroup.Animation.cpp:456`）。烘焙器最初按秒取值，导致四肢几乎静止（只有骨盆轻微变化）；修正后走/跑/冲刺的脚部位移恢复到合理量级。
2. **ALS 剪辑是原地动画**（骨盆位移 < 3cm），轨迹特征不能从骨盆位移得到。改为从“支撑脚速度”推断每个剪辑的标称移动速度：
   - 取整段最低脚高 +5cm 内的帧（支撑相），对双脚世界速度做分量中位数，再取反；
   - 实测标称：走 81–127、跑 60–164、冲刺 300 cm/s；
   - 同一剪辑的标称速度用于合成过去/未来轨迹特征，并在运行时把游戏目标速度按 `MoveSpeed` 的倍数归类到走/跑/冲刺后取该族标称值（`GaspAnimator.UseGaitCalibration`）。
3. **模型前向偏航**：Ada+ALS 经 `ClipFrameFix` 转正后，模型空间前向是 **−Z**（实测 `ForwardYaw ≈ π`）。查询轨迹必须先把 Actor 世界速度转到模型空间再检索；否则前进查询会命中反向剪辑。
4. 播放速率按 `实际速度 / 标称速度` 调整（0.6–1.8 倍），减少原地剪辑的滑步。

### D1. 动画数据离线烘焙为二进制库（不在运行时采样）
运行时 `UpdateAnimation()` 只是把模型排进动画更新队列（`AnimatedModel.cpp:305-322`），不能同步取样；逐帧等待采样 25 个剪辑需要几十秒。
因此由 Python 工具直接解析 `.flax` Animation chunk0（曲线格式已核实），按 30Hz 采样并写出紧凑二进制库。运行时只读库、做特征检索与姿势插值，**不依赖编辑器 API**。
- 产物：
  - 开发期：`HundunWorld/Content/Gasp/gasp_anims.bin`（Python 烘焙，文件读取）；
  - 分发期：`HundunWorld/Content/Gasp/gasp_anims.flax`（`RawDataAsset`，随 Content 打包，运行时 `Content.Load`，已实测 `db=asset:Content/Gasp/gasp_anims.flax`）。
  - 生成分发资产：先跑 `python Tools/GaspBakeAnimations.py`，再在编辑器 PIE 中放 `%APPDATA%/HundunWorld/gasp_asset_bake.on` 标记（夹具调用 `GaspDataAssetBaker`），随后 `content_database_rebuild`。
- 回退：脚本可重复生成；删除资产/文件时 GASP 组件自动停用并回落到现有动画图。

### D2. 姿势所有权：图提供未覆盖骨骼，GASP 覆盖匹配到的骨骼
每个动画帧：图先求值 → `MetaHumanClipFrameFix` 转正 → GASP 读取当前姿态，用离线库算出的局部变换覆盖“库中存在的通道”（79 根），再 `SetCurrentPose` 写回。
- 好处：面部/手指/未覆盖骨骼仍由现有图驱动；不需要重做 MetaHuman 全骨架映射。
- 顺序：GASP 订阅 `AnimatedModel.AnimationUpdated`，且必须晚于 `MetaHumanClipFrameFix` 注册；带重入保护。
- 回退：组件 `Enabled=false` 时完全不动骨骼，等同于现状。

### D3. 坐标与单位
- 离线库保存**剪辑原始局部变换**（UE/ALS 局部空间，厘米），与引擎 `AnimationData::Channels` 一致。
- 运行时先以 Ada 绑定姿态为底，叠加通道曲线，再对每个节点做与 `MetaHumanClipFrameFix` 相同的 `R_x(-90)·M·R_x(+90)`。
- 特征计算在**模型空间**：以 pelvis 为原点、模型 +Z 为前向；Actor 的 yaw 负责世界朝向。`Actor.Position` 是厘米，特征也统一厘米。

### D4. Motion Matching 特征（第一版）
- 轨迹：过去 `-0.3/-0.2/-0.1s`、未来 `0.1..0.8s` 的根骨位置（XZ）+ 朝向（sin/cos，yaw）。
- 姿势：`pelvis` 速度；`foot_l/foot_r` 位置与 XZ 速度；`head`、`hand_l/hand_r` 位置（模型空间）。
- 权重：轨迹位置/朝向高权重，脚部次之，上肢低权重。维度与权重集中定义在 `GaspFeatureSchema`（唯一真源）。
- 查询：过去来自 Actor 位置历史；未来按当前速度 + 输入方向做一阶预测；姿势取上一帧 GASP 输出。
- 检索：第一版暴力搜索（约 2k 帧 × ~40 维，每帧 <0.2ms）；预留 PCA-KD 树接口（对齐参考 mm.h 的 `PCAKDTree`）。

### D5b. 骨骼级混合 + 剖面通知（2026-11-02）
参考实现的 BlendStack 支持“bone-wise blending + profile notifications”，本轮补齐对应能力：

1. **骨骼掩码**：`GaspBoneMask.Subtree(skel, "spine_01")` 按层级截取子树（实测覆盖 274/343 节点：头/手/脊柱为 true，骨盆/脚为 false）；`GaspBoneMask.All` 为全骨骼。
2. **叠加层**：`GaspBlendStack.PlayOverlay(clip, t, loop, fadeIn, fadeOut, mask, profile)` 新增层但**不淡出**已有层；`Evaluate` 逐骨按“参与该骨的层权重”归一化——未被掩码覆盖的骨骼仍完全由基础层决定。`StopProfile(profile)` 只淡出指定层。
3. **剖面通知**：`BlendStarted/BlendCompleted/BlendFinished` 三个事件（参数为 profile 名），在权重到达 1 / 层被移除时触发。
4. **验证（离线，103/103）**：`mask.upperOnly`（骨盆/脚不在掩码、头/手在内）；`mask.lowerUntouched`（叠加层淡入后骨盆/脚与**同帧** walk 参考逐位一致）；`mask.upperApplied`（头部被叠加层改变）；`mask.profileEvents`（start/done 到达）；`mask.layerRemoved`（StopProfile 后头部回到 walk 参考、并收到 end 事件）。
5. **待接线**：目前游戏层还没有“瞄准/施法上半身”这类叠加动作消费者，API 处于“已被测试覆盖的库能力”状态；接入时应以 profile 名做生命周期管理，避免层泄漏。

### D5. 连续性与混合
- 续播偏置：候选若属于当前剪辑且时间与“当前时间+dt”接近，减一个偏置；离得越远偏置越小。
- 重选历史：0.3s 内选过的 (clip, pose) 加惩罚，避免抖动。
- 混合：`GaspBlendStack` 维护最多 4 个播放器，新选择淡入 0.2s、旧播放器淡出；逐骨线性/slerp。
- 暂不应用 root motion：位移权威在 ECS；库中根骨平移保留为表现（ALS 剪辑本身近似原地）。

### D6. 状态机边界（第一版）
- 地面：Idle / Walk / Run / Sprint / Turn / Stop 全部进入同一个 MM 库。
- 空中：Jump/Fall/Land 由简单状态机选择（起跳→JumpLoop，下落→FallLoop，落地→Land），不参与地面检索。
- 蹲伏/滑行：第一版沿用现有图参数（GASP 覆盖后可能视觉冲突，作为下一阶段）。

### D7. Traversal（第二阶段，核心已落地）
- **检测**：`FlaxTraversalProbe` 胸高前向射线找墙（拒绝过陡/过缓法线）→ 从墙内侧上方垂直向下找平台顶（法线朝上）→ 高度 45–220cm、水平距离 ≤200cm；输出 `GaspLedgeInfo`（墙点/顶点/法线/高度/手侧）。单位全部厘米。
- **选库**：`GaspTraversalSelector` 按高度选 `Mantle_1m` / `Mantle_2m`，1m 优先匹配 LH/RH；缺手侧回退同高度。
- **运动扭曲**：`GaspTraversalPlan` 起点=脚底、终点=平台顶向内 `LandingOffset`，位置按 smoothstep 分配并叠加中段小弧线；时长取剪辑长度。
- **状态机**：`GaspTraversalController` 管理开始/推进/结束/冷却/拒绝原因。
- **运行时**：`GaspTraversalDriver` 调用 `GaspAnimator.BeginTraversal` 暂停普通 MM、独占 Mantle 剪辑，结束后 `EndTraversal` 恢复检索；`AutoApplyMovement` 默认关闭，正式联机走 D10 的权威链路（本地预测 + 服务端裁决），绝不由客户端直接写 Actor。
- **验证**：离线断言覆盖选库/高度拒绝/弧线/终点/冷却；PIE 夹具用假探测器跑通 `start=(0,0,120) → end=(0,100,215)`、剪辑 `ALS_N_Mantle_1m_RH`。
- **已知限制**：PIE scenes 场景中全层射线连平台 BoxCollider 都打不到（疑似该播放模式无物理查询），真实射线路径需在真实游戏/PIE game 模式验证。

### D10. Traversal 联机权威（第四阶段，2026-11-01 落地）
**口径**：客户端只提交"计划参数"，逐帧位置由两端用同一份纯函数推进；服务端裁决计划并推进权威位置，客户端同曲线本地预测。

1. **共享数学层（唯一实现）**：`Horizon.Game.Sim.TraversalMath`
   - `Sample(plan, elapsed)`：水平/垂直 `smoothstep`，叠加 `4t(1-t)·ArcHeight` 抛物弧；端点钳制。
   - `SampleYaw(startYaw, plan, elapsed)`：最短弧插值到 `EndYaw`，与位置同一曲线。
   - `Validate(plan, fromX/Y/Z, limits)`：时长 0.25–2.0s、抬升 20–250cm、水平位移 (0, 250cm]、弧高 0–250cm、起点贴合 ≤75cm；拒绝原因 `TraversalRejectReason`（0=None…9=AuthorityUnavailable）。
   - **单位口径（踩过的坑）**：客户端 `pred.X/Y/Z` 与 Flax 世界坐标只差轴序、**不缩放**（`LocalPlayerActorSyncSystem` 写的是 `Actor.Position=(pred.X,pred.Z,pred.Y)`，世界单位=厘米）。限值最初按"米"写（0.2–2.5），结果 60cm 的合法计划被服务端/客户端预检判 `HeightOutOfRange`；实机日志 `limits minH=20 maxH=250` 才把口径钉住。客户端换算也不再 `/100`。
   - `TraversalLimits` 单一来源：`SimulationOptions.Traversal` / `MovementValidator.Options.Traversal`（服务端）与客户端生成侧共用。
2. **客户端预测**：`LocalSimulationSystem.BeginLocalTraversal(entityId, plan, startYaw)` 登记后，FixedUpdate 用 `MovementStepInput.TraversalActive` 分支推进 `PredictedTransformComponent`（忽略移动输入、Vz=0、跳跃计数清零、朝向按曲线插值），走完触发 `LocalTraversalFinished`。起点取**预测位置**而非渲染 Actor 位置——服务端比的是权威位置，插值延迟在高速移动时会天然超出 `StartTolerance`。
3. **协议 v17**：`TraversalRequestPacket`（client→server，起点/终点/时长/弧高/EndYaw/CurveVersion）与 `TraversalResultPacket`（server→client，Accepted/Status/权威终点回显）。包层用 `byte Status` 不引用 Sim.Contracts，数值契约由 `TraversalProtocolContractTests` 逐值钉住。
4. **服务端权威**：`SimWorld.TryBeginTraversal` 校验（未知实体/已在翻越/冷却/限值/起点贴合）→ `SimWorld.Step` 的翻越分支按曲线推进权威位置与朝向、结束时显式落点并清状态；翻越帧消费积压输入（推进 `LastProcessedClientTick` 以回 ack）但不回放、不产生 Correction。
5. **路由**：`SyncTraversalHandler` 先做会话绑定校验 → DS 权威会话转 DS（`DsDataPlaneHost` → `DsWorldSession.HandleTraversalRequestFromClient`，结果包直接排进该会话下行事件）→ 否则转 `IZoneShardGrain.SubmitTraversalRequestAsync`，由网关把结果包作为响应回给客户端。DS 与分片共用同一 `SimWorld`，不存在第二套规则。
6. **客户端联动**：`GaspTraversalDriver`（默认 F 键）探测 → 选剪辑 → 校验 → `BeginLocalTraversal` + 上行请求 + 开始 Mantle 动画；收到拒绝回执立刻撤销本地预测并 `EndTraversal`，位置回正交给权威快照/修正。拒绝原因、`LastAuthorityPlan` 可在驱动上读取。
7. **验证（离线，翻越相关 44/44；含步进/金轨迹回归 60/60）**：曲线端点/弧贡献/单调性/按位确定性；步进器翻越窗口（忽略输入、结束落点、零时长幂等）与非翻越帧对独立参照逐位一致；校验矩阵（每档拒绝 + 闭区间边界 + 自定义限值）；`SimWorld` 受理/冷却/不叠加/逐帧对曲线/落点/无 Correction/积压输入 ack；DS 会话请求→回执→权威推进→落点；协议编解码往返与状态词表；网关路由（DS 优先/回退分片/会话绑定/异常兜底）。
8. **实机取证（PIE game，2026-11-01）**：自动登录成功进入 World；`gasp_traversal_request.on + gasp_traversal_synthetic.on` 标记驱动一次 60cm 翻越：
   - `GaspAnimator READY clips=32`、`limits minH=20 maxH=250 maxD=250 tol=75`；
   - `start clip=ALS_N_Mantle_1m_LH height=60cm start=(743,-0,5831) end=(743,60,5931) mode=authority`；
   - 本地预测/选库/动画全部走通；**服务端未回执**（后端进程是改动前启动的旧版本，不认识 v17 包），客户端出现 `[SyncDiag] CorrectionStorm Entity=46`，即旧服务端把脚本位移当异常拉回——与"服务端没受理"一致。
   - 因此真机闭环只差**后端重建重启**（Gateway/Silo/WebApi/DsHost 需重新发布 v17）。
   - **第二轮（后端已重建重启）**：`authority accepted tick=104 serverTick=39731` —— 服务端受理并推进权威位置，角色被送到计划终点（Flax `(1094,0,5980)`，之后因脚下无真实平台回落）。**剩余收尾问题**：客户端驱动没有 `finished`/`EndTraversal` 日志，角色停在 Mantle 末帧（截图呈趴伏/拉伸）；已加驱动心跳（elapsed/dt/local 状态）与动画侧日志待复验，并计划加动画侧兜底（剪辑播完自动 `EndTraversal`）。
   - **阻塞**：本轮末尾另一个并行会话把 `CharacterPanelPage.cs` 改成未限定 `ItemInfo`（line 1303/1308），客户端编译失败、编辑器新实例 `Cannot load scene without game modules loaded`；该文件已于 22:57 由并行会话自行修好，客户端重编通过。
   - **基线对照（无翻越）**：自动登录进世界后角色站姿/地面贴合正常 ⇒ "炸开"不是资产或登录链路。
   - **翻越后的姿态异常（已定位并修复，见下方根因 A/B）**：当时心跳证实控制器在推进、服务端受理、`EndTraversal` 执行，但角色仍呈躺伏/拉伸状。离线健全性断言（局部平移 ≤96cm、scale=1、四元数归一、组件空间到骨盆 ≤110cm）全部通过 ⇒ 数据本身正常，问题被追到"剪辑未绑定 + 落地状态机"。
   - **已加动画侧兜底**：`GaspAnimator` 在主剪辑播到末尾（t ≥ len-0.02）时自动 `EndTraversal`；诊断（`trav diag` / `state`）只在 `DebugLog` 打开时输出，自测标记会自动打开。
   - **根因 A（躺姿/静止）**：`GaspMotionDatabase.Build` 只给 **Ground** 剪辑绑通道，Air/Traversal 剪辑的 1742 条通道全部 `NodeIndex=-1` → 播放时回落 bind 姿态（实机 Mantle 0/343 骨骼在动、骨盆恒在 (0,2,87)）。修复：构建时**绑定全部剪辑**；并在 `SampleClip` 里**剥离根骨平移**（Mantle 自带 99cm root motion，位移权威在 ECS，否则翻越会视觉双倍位移）。离线证据：Mantle 63/343 骨骼在动、骨盆 96→56→96（直立→蹲伏→直立），32 个剪辑无一静态（`clip.allClipsAnimate`）。
   - **根因 B（落地后卡蹲姿）**：`grounded` 为真时更新循环对任意 `phase != Ground` 都调 `EnterGround`，而 `EnterGround` 把 phase 设为 **Landing** → 落地期间每帧重置 `_landTimer`，永远回不到 Ground（不止翻越，普通跳跃落地同样中招）。修复：只在 `Rising/Falling` 进入 Landing。实机复验：翻越→Falling→Landing→**Ground/MM**（`ALS_N_Stop_R_Down` 末帧直立站姿），截图确认角色恢复直立。
   - **静止待机（已落地）**：库里没有可循环 Idle；实测 `ALS_N_SecondaryMotion` 是合格的循环待机（骨盆 96、首尾 0cm 闭合、Loop=true）。`GaspAnimator` 增加 rest-idle 状态：速度 <20 且当前非循环剪辑播完 → 切到该循环待机并跳过 MM，速度 >50 或离地时退出。实机日志 `mode=Idle clip=ALS_N_SecondaryMotion` 且 MainTime 0.67→1.68→0.04 循环。
   - **真实探针实机验证（`gasp_traversal_realbox.on`）**：运行时在玩家前方 1.5m 生成 120cm 高 `BoxCollider`，走**真实** `FlaxTraversalProbe`（胸高前向射线→顶面向下）→ `start clip=ALS_N_Mantle_1m_RH height=120cm side=1` → `authority accepted` → Mantle 动画（pelvis 95→69）→ `EndTraversal` → Ground/MM → 落地 → 循环待机，全链路一次跑通。
   - **矮台探测（已补）**：胸高 70cm 射线会从低于胸口的台面上方掠过。`FlaxTraversalProbe` 现在先射胸高、失败后再射膝高（`LowProbeHeight=30cm`），复用同一套墙法线/顶面/高度/距离过滤。实机（`gasp_traversal_realbox_low.on`，60cm 高碰撞盒）：`wall miss h=70` → `low-ledge fallback hit h=30cm` → `start clip=ALS_N_Mantle_1m_RH height=60cm` → 服务端受理 → 翻越 → 落地 → 循环待机。
9. **实机暴露并修复的两个客户端真 bug**：
   - **冷却永不衰减**：`GaspTraversalController.Tick` 才递减 cooldown，而 Driver 在 `!Active` 时直接 return → 一次 `Cancel()` 后冷却卡在 0.4s，后续 30 次重试全报"冷却中"（永久锁死翻越）。修法：Update 里先 Tick 再判断 Active。
   - **`Entity.Id == 0` 当"未找到"哨兵**：Arch 实体 Id 从 0 开始，本地玩家恰是 0 号 → 一直被拒"本地玩家实体未就绪"。修法：用独立 `found` 布尔。
   - 另加 **3s 无回执兜底**：超时取消本地预测并结束动画，避免"服务端不认包时无限预测 + 纠正风暴"。
10. **已知限制**：
   - 翻越期间若收到**翻越前在途**的修正包，`ReconciliationSystem` 的重放不含计划；实际表现是下一 FixedUpdate 被曲线绝对位置覆盖（自愈），但中途可能闪一帧。服务端在翻越窗口内不产生 Correction，正常网络下不可见。
   - Orleans（非 DS）路径的结果包依赖网关"请求-响应"回复通道，未做多客户端联调；当前生产主链路是 DS。
   - `CurveVersion` 不一致会被拒绝（防两端曲线不同步），升级时需同时发布两端。
   - **运行目录同步**：本次改了 `Horizon.Game.Message` / `.ECS.Arch` / `.Sim.Contracts` 三个共享程序集，
     Flax 的 C# 编译解析的是仓库 `bin/Debug/net10.0`（`Game.Build.cs` 优先取仓库路径），
     但**运行/PIE 从 `HundunWorld/Binaries/GameEditorTarget/Windows/x64/Debug/` 解析同名程序集**；
     后者不会自动刷新，需手工复制（否则 PIE 里翻越触发时会缺 `TraversalResultPacket`/`BeginLocalTraversal`）：
     ```
     cp -f Horizon.Game.Message/bin/Debug/net10.0/Horizon.Game.Message.dll \
           Horizon.Game.ECS.Arch/bin/Debug/net10.0/Horizon.Game.ECS.Arch.dll \
           Horizon.Game.Sim.Contracts/bin/Debug/net10.0/Horizon.Game.Sim.Contracts.dll \
           HundunWorld/Binaries/GameEditorTarget/Windows/x64/Debug/
     ```
     后续可考虑在 Game 构建后处理里自动同步（未做，避免改构建链）。
   - **后端常驻启动**：`Start-Process` 拉起的 Silo/Gateway/WebApi/DsHost 会随工具会话结束被作业对象回收；本轮改用 WMI `Win32_Process.Create` 隐藏启动 `%TEMP%\hzn-restart\start-backend.cmd`（含 WebApi 的 `ASPNETCORE_URLS=https://0.0.0.0:5101`），四端口 `5101/11111/19870/19871` 全部监听后才继续验收。

### D11. 特征空间 KD 树 + 稠密/稀疏 LOD（2026-11-01 晚落地）
参考实现用 PCA KD-tree 做 dense/sparse LOD；这里落地等价结构：

1. **`GaspFeatureKdTree`**：51 维特征、按“节点内方差最大维度 + 中位数”分割的经典 KD 树（叶 ≤8 点），查询为 K 近邻（平方欧氏距离，与 `GaspFeatureSchema.Distance` 同口径），带访问计数 `LastVisited`。
2. **候选集重排**：`GaspMotionDatabase.Search(..., candidateLimit)` —— `<=0` 走原全库暴力（行为不变）；`>0` 先用 KD 树取 top-K，再由同一套评分（步态惩罚 2000 / 续播负偏置 / 近期惩罚 150）重排；若续播帧不在候选内会补评一次，保证 `continuingCost` 始终可用。`LastCandidatesScored` 暴露实际打分数。
3. **LOD 档位**：`GaspAnimator.DenseCandidateLimit=128`（本地）、`SparseCandidateLimit=32`（`RemoteMode` 远端角色）——远端从“全库 962 帧”降到 32 个候选。**K 值经离线校准**：K16/24/32/48/64/96/128 各跑 120 查询，K=128 达到 120/120 最优，稠密档取 128；稀疏档在 32 附近取得代价/一致率平衡。
4. **验证（离线，103/103）**：
   - `kd.knnExact`：200 个扰动查询的 top-1 平方距离与**独立暴力参照**逐点一致（0 误差），单次查询只访问 263/962 点；
   - `kd.denseOptimal` / `kd.denseClipMatch`：200 个查询的稠密(128)结果代价**从不劣于**暴力（200/200），剪辑一致 170/200（其余为同类等价帧）；
   - `kd.sparseCostGap`：稀疏(32)最优率 171/200、剪辑一致 140/200、平均代价差 9.62、最大 122.1（候选被截断时才会劣于暴力，符合 LOD 预期）；
   - `kd.lodBudget`：稠密档实际打分 ≤128（全库 962）。
5. **已知边界**：本工程库仅 ~962 帧，KD 树主要是为多角色/大库与远端降级预留；叶大小/候选数是唯一旋钮，如需更严召回可调大 `DenseCandidateLimit`。

### D12. 转向扭曲（Steering Warping，2026-11-02）
参考实现的 Animation Warping（orientation/steering）在本工程落地“整姿态绕骨盆竖轴扭曲”：

1. **纯几何层** `GaspPoseWarp`：`RotateYAbout(comp, pelvis, rad)` 按行向量约定 `comp * T(-p) * Ry * T(p)` 施加（**先骨骼变换再绕枢轴旋转**，写反会让枢轴自己平移 π/2）；`ComputeSteer` = `wrap(desiredHeading - clipHeading)` 钳到 ±Max、按 `speed/walkSpeed` 缩放；`Approach` 按角速度上限逼近。
2. **方向换算**：剪辑标称方向（模型空间）→ 世界 = `RotateY(nominal, yaw + ForwardYaw)`，必须与查询构建的“世界→模型 `-yaw-ForwardYaw`”严格互逆；漏掉 `ForwardYaw≈π` 会得到恒定 180° 误差（实测被封顶成 ±30°）。
3. **动画器接线**：`UpdateGroundMM` 计算目标角（仅地面 MM 且速度>20cm/s），`OnAnimationUpdated` 在组件矩阵转正后、脚部 IK 前平滑施加；翻越/空中目标清零。`LastSteerWarpDegrees` 输出到调试日志。
4. **验证**：离线 103/103 —— `warp.pivotFixed`（骨盆位置 0 位移）、`warp.rigidDistances`（头/脚到骨盆距离不变）、`warp.angleApplied`（施加 30° 实测 30.00°）、`warp.steerClampScale`（60° 差满速→30°、半速→15°、静止→0）、`warp.approachRate`（90°/s×0.1s=9°）。
5. **实机**（`gasp_fixture.on` + `gasp_fixture_turn.on`，圆周半径 300cm/500cm/s，夹具按标记自动挂到本地玩家）：直线 `warp=1deg`（不误扭），圆周 `warp=30deg`（封顶补偿），`clip=ALS_N_Walk_RF`。
6. **已知边界**：目前只做全姿态刚体扭曲（无上下身反向补偿）；转向很急时会长期贴着 30° 上限，若观感需要可下调 `MaxSteerDegrees` 或按曲率自适应。

### D13. Foley 脚步事件（2026-11-02）

参考实现的 Foley 由 AnimNotify/落脚时刻驱动；本工程用“离线落脚检测 + 纯时间线 + 动画事件 + 消费端”实现：

1. **落脚检测**（`GaspMotionDatabase.BuildFootPlants`）：逐帧采样左右脚踝高度与竖直速度，以“接近剪辑内最低点 +4cm、竖直速度 < max(20% 峰值, 20cm/s)”为接触判据，取上升沿记录 `ClipPlantTimes/ClipPlantSides`。实测 Walk_F 落脚 `[0.067, 0.633]` 侧 `[L,R]`，Run_F `[0.033, 0.433]` 侧 `[L,R]`，SecondaryMotion 0 次；步距占剪辑长度 25%–75%（`foot.walkGap`）。
2. **时间线**（`GaspFootstepTimeline.Collect`）：判定一次主剪辑推进 `(from, to]` 跨过了哪些落脚索引；左开右闭避免同一帧重复触发，循环剪辑 `to < from` 自动切成 `(from, len] + [0, to]`，非循环回退不产生事件。
3. **动画器事件**：`GaspAnimator.Footstep(side, worldPos, speed)`，在 `_blend.Update` 之后调用 `TickFootsteps()`；切换主剪辑时重置游标（不会把新剪辑起点误判成跨点），世界坐标用脚骨组件位置经 `_model.Transform.LocalToWorld`。
4. **消费端**（`GaspFoley`）：约定 `Content/Audio/Foley/footstep_01..06.flax`；有资产时按速度调音量并播放，缺资产时进入“事件模式”（只计数/日志），让无美术资源阶段也能验收。本地玩家由 `HundunWorldGame.EnsureGaspAnimator` 自动挂载，测试夹具同样挂载。
5. **验证**：离线 103/103 —— `foot.walkCount/walkAlternates/walkGap/runCount/runAlternates/idleNone` + 时间线 6 条（正向、左开右闭、单步跨两点、空区间、循环回绕、非循环不回绕）。**PIE 实机**：`[GaspFoley] step#1 side=L speed=488 → step#2 side=R speed=500 → step#3 L → step#4 R`（左右交替），跑动相位 `speed=1006`，位置随角色前进；本地玩家与夹具各一条挂载记录。
6. **已知边界**：落地/起身等非位移剪辑若含落脚时刻也会触发（当前按速度记在事件参数里，消费端需要可自行按速度门控）；音频资产尚未制作，目前是事件模式。

### D14. 调试绘制（2026-11-02）

参考实现的 debug_draw 在本工程落地为 `GaspDebugDraw`（自测标记 `%APPDATA%/HundunWorld/gasp_debug.on` 才挂载，默认不影响正常游戏）：

1. **数据源**：`GaspQueryBuilder.LastWorldTrajectory` 保存最近一次查询的 11 个轨迹采样（世界坐标）；过去采样取历史插值（无历史时回退到输入位置），未来采样把模型空间预测偏移用 `-modelYawOffset` 转回世界。`GaspAnimator` 同时暴露 `LastPlanarVelocity`、`LastDesiredHeadingRad`、`LastClipHeadingRad`。
2. **绘制内容**：过去轨迹灰线、未来轨迹青线（含采样点）、剪辑朝向（红箭头）/期望朝向（绿箭头）、速度向量（黄）、最近 0.6s 落脚点（黄圈+竖线）与头顶文本（剪辑名/cost/warp/speed）。
3. **验证**：离线 103/103 —— `traj.pastUsesPosition`（无历史时过去采样=输入位置）、`traj.futureForward`（未来采样沿世界速度方向单调前进）、`traj.futurePlanar`（预测保持平面、不改变高度）。PIE：夹具与本地玩家均出现 `[GaspDebugDraw] attached` 与 `[GaspDebugDraw] first draw`，说明数据与绘制调用每帧执行。
4. **已知边界**：Flax 的调试绘制是编辑器视口叠加层，`game_screenshot` 桥抓的是游戏后处理画面，抓不到这些线（需要编辑器视口开启 Debug Draw 目视）；因此本项以“绘制调用已执行 + 轨迹数据离线对拍”为验收，而不是截图。

### D8. FootPlacement/LegIK（第三阶段，核心已落地）
在 GASP 输出局部姿态之后、写回之前做：
1. **姿态空间修正**：引擎 `GetCurrentPose/SetCurrentPose` 用的是**组件空间**矩阵（`AnimGraph.cpp:421-432` 用 `LocalToWorld` 铺平），不是局部矩阵。现在流程是：混合局部姿态 → 组合成组件矩阵 → 对每个组件矩阵做 `R_x(-90)·M·R_x(+90)`（与 `MetaHumanClipFrameFix` 一致）→ 写回。未覆盖骨骼沿用图的组件矩阵。
2. **脚部贴地**：`GaspAnimator.ApplyFootPlacement` 把左右脚踝组件位置经 `_model.Transform` 转到世界，向下 `RayCastAll`（可配 `FootLayerMask`，跳过自身/子 Actor），把命中点 +`AnkleHeight` 转回组件空间作为目标；目标高度做 ±限幅。
3. **骨盆补偿**：`GaspLegSolver.ComputePelvisDrop` 取双脚所需下沉量的较小值（≤0，限 `MaxPelvisDrop`），`OffsetPelvis` 平移骨盆并重铺整副骨架。
4. **双骨 IK**：`GaspLegSolver.SolveLegComponent` 在组件空间解析求解（骨长保持、膝盖极向量沿用当前弯曲方向、退化时朝模型前方 −Z），脚的世界朝向保持；实测四元数/矩阵乘法顺序为 mode 3（组件旋转与相对矩阵都右乘）。
5. **脚掌坡度对齐**：`GaspLegSolver.AlignFootToGround` 把脚底法线旋到地面法线（保留 yaw），绕脚自身原点旋转并重铺子树，超过 `MaxFootTiltDegrees`（默认 30°）的倾角被钳制。脚底法线取**脚骨局部 X 轴**：Ada 脚骨在组件空间里局部 Y 是横向（恒指 −X）、局部 Z 是脚背朝向，只有局部 X 朝上；用错轴会把脚拧到侧向约 90°（离线量到 `cX≈(0,1,0)`、`cY≈(−1,0,0)` 才定位到）。
6. 未做：Stops/Default 两套 Plant 参数、软 IK、扭转修正。

### D9. 验证
- 离线：`Tools/GaspHarness`（103 条断言）读取真实 `gasp_anims.bin` + Ada 真实骨架（`Tools/GaspSkeletonExport.py` 从 `.flax` 解出），覆盖：库结构、特征有限值、逐剪辑自洽检索、前/后/左/右方向查询、走/跑/冲刺查询、前向偏航与族标称速度校准、2 秒续播稳定性、BlendStack 淡入淡出/非循环钳制、骨骼掩码上/下半身隔离与剖面事件、组件空间双骨 IK、脚掌对齐（平地摆平、20° 目标精确到位、80° 目标按 30° 钳制、平移与子骨距离保持）、KD 树与暴力参照对拍/LOD 预算、落脚检测与时间线（左右交替、循环回绕）、转向扭曲（枢轴固定/刚体距不变/角度/钳制/逼近速率）、Traversal 选库/运动扭曲/冷却。
- 运行时：`GaspSelfTest`（`%APPDATA%/HundunWorld/gasp_selftest.on` 触发）断言库加载、特征维度、检索自洽、姿势覆盖、`PoseApplyCount > 0`。
- PIE：编辑器桥 `sim_play` + 日志/`actor_bone_probe` 取证；必要时截图。
- 不把“编译通过”当验收：必须看到 MM 选中的剪辑名与骨骼矩阵变化。

### D15. 参考实现覆盖对照（2026-11-02 收尾审计）

| 参考模块（README 功能表） | 参考内容 | 本工程实现 | 结论 |
|---|---|---|---|
| Motion Matching | PoseSearch：PCA-KD/暴力、Chooser Dense/Sparse、续播偏置、PoseReselectHistory | 51 维特征、方差 KD + 暴力、LOD 128/32、续播负偏置、0.3s 近期惩罚、步态/净偏航惩罚 | 等价实现（未做 PCA；962 帧下 raw KD top-1 仅访问 263/962） |
| BlendStack | 主栈 + 状态机栈、BlendProfile、通知过滤/触发权重 | 逐骨混合、骨骼掩码、剖面事件、overlay、非循环钳制；旧 `LocomotionGraphDriver` 可回退 | 适配实现（无状态机栈；脚步事件只从主剪辑发） |
| AnimationWarping | Orientation / Steering / OffsetRootBone / Lean / AimOffset 加性 | Steering：整姿态绕骨盆竖轴扭曲（±30°、360°/s） | 部分适配：游戏无瞄准/侧移输入，未做 orientation/lean/aim；根位移由 ECS 权威移动承担 |
| Traversal | 检测→Chooser→Montage+Warping，支持连续翻越 | 胸高 + 低台(30cm)探测、选库、共享曲线、v17 权威协议、客户端预测、DS/分片校验、真实台面验收 | 适配实现（单次翻越 + 0.35s 冷却，落地后可再次触发；蒙太奇中途接续未做） |
| FootPlacement + LegIK | 世界地面查询、Stops/Default 两套 Plant、弹簧 plant/unplant | 逐脚射线、骨盆下沉、解析双骨 IK、脚掌坡度对齐与 30° 钳制 | 核心适配（单一参数集；未做 plant/unplant 弹簧状态机与两套 Plant 参数） |
| IK Retargeter / PBIK | 完整移植、多角色切换 | 无跨骨架需求（所有玩家共用 MetaHuman 骨架）；脚部用解析 IK | 不适用/未做（无手部 IK 目标与多骨架资产） |
| 角色移动 | CharacterMovementComponent | 本工程 ECS 权威移动 `SimWorld`，GASP 读取速度/位姿 | 由现有系统承担 |
| 相机 | GameplayCameras 9 个 rig | 已有 `SpringArmCamera`/`ThirdPersonCamera`/`CameraShakeSystem`/`DynamicCameraAdjuster` | 由现有系统承担 |
| 其它 | Foley、实验状态机、UMG、CVar、调试绘制 | Foley（D13）、旧移动驱动回退、Ink UI、调试绘制（D14） | 完成/适配 |

**打包回归（M5）**：`GameTarget Development` 编译通过（含 GASP 新代码）；本机 Flax 安装只有 Editor、不提供独立游戏 exe，无法在本环境执行打包后运行验证，记录为环境限制（编辑器内 `Content.Load` RawDataAsset 已实测）。

## 4. 当前状态（2026-11-02）

- [x] 离线动画库烘焙（32 剪辑 / 1389 帧 / 3.1MB）+ 骨架导出工具。
- [x] C# 核心：动画库、骨架、特征 schema、逐帧特征库、暴力检索、续播偏航/迟滞、BlendStack、查询构建、步态标签、组件空间双骨 IK。
- [x] 角色接线：本地玩家挂 `GaspAnimator`；激活时停用旧 `LocomotionGraphDriver`、接管 `MetaHumanClipFrameFix` 并每帧覆盖骨骼。
- [x] **PIE 实机验证（测试场景）**：
  - 引擎崩溃根因已定位并修复：Ada 有 282 根骨，`PerBoneMotionBlur=true` 时首块骨骼缓冲需 282×48×2≈26KB，而引擎首块固定 16KB → `AnimatedModel.cpp:82` 断言。`PerBoneMotionBlur=false` 后测试 PIE 稳定。
  - `GaspAnimator READY clips=32 poses=962 skeleton=343`；`PoseApplyCount` 约 60/s 持续增长（每帧写回）。
  - 500 cm/s → Walk_F、1000 cm/s → Run_F（修正方向/步态校准后不再命中 Run_B/Sprint）；停止时回 Idle/Turn。
  - 公平对照：graph-only（只图 + ClipFrameFix）与 GASP 在同一空闲相位的肩部/躯干形变**完全一致** ⇒ 形变来自 Ada 预览身体 + SecondaryMotion 剪辑本身，不是 GASP 姿态/写回/IK 的缺陷。
  - 组件空间骨骼位置与引擎探针对账：Walk_F t=0 的 pelvis/foot_l/calf_l/hand_l/upperarm_l 逐骨一致。
- [x] **远端玩家接入**：`FlaxActorSyncSystem` 创建 Player 远端时挂 `GaspAnimator(RemoteMode=true)`；远端尊重网络 LOD（不强设 EveryUpdate/离屏更新）、关闭脚部射线，只读 Actor 插值位姿。夹具对照：`remote clip=ALS_N_Walk_F apply=1308 ready=True`，与本地选择一致。
- [x] **数据分发**：动画库已包成 `RawDataAsset`（`Content/Gasp/gasp_anims.flax`），运行时优先 `Content.Load`；PIE 实测 `READY ... db=asset:Content/Gasp/gasp_anims.flax`。bin 文件仅作开发期回退。
- [x] **多人共享缓存**：`GaspRuntimeCache` 按「动画资产 + 骨架」共享只读动画库与逐帧特征库；PIE 实测第二个角色 `cache=dbHit=1 dbLoad=1 motionHit=1 motionBuild=1`，354ms 特征构建只付一次。
- [x] 离线验证 **103/103** 通过（含前/后/左/右方向查询、BlendStack 淡入淡出/非循环钳制、步态约束、组件空间双骨 IK、脚掌坡度对齐与倾角钳制、Traversal 选库/运动扭曲/冷却、骨骼掩码分层、KD/LOD 对拍、脚步落脚与时间线、转向扭曲、调试轨迹世界采样）；Editor Debug/Release、Game Development 编译通过。
- [x] **Traversal 联机权威（D10）**：共享曲线/协议 v17/客户端预测/服务端裁决/DS 与分片路由/驱动与动画联动；网关与服务端测试 **60/60**。**实机 PIE 全链路已验收**：自动登录→翻越请求→`authority accepted`→权威位移→Mantle 动画（63 骨在动、骨盆 96→56→96）→Falling→Landing→Ground/MM 直立恢复（修复了“剪辑未绑定回落 bind 姿态”和“落地计时器被每帧重置”两个真 bug）。
- [x] **真实世界流程**：后端四服务经 WMI 常驻启动（WebApi 5100/5101 正常），PIE 自动登录→进世界→`LocalPlayer_46` GASP READY→真实台面翻越全链路通过；真实角色/网络链路不再依赖测试场景。
- [x] **2026-11-02 特征采样缺陷修复**：`GaspClip.SampleRate` 为 int，`f / SampleRate` 触发整数除法，使同一秒内的采样帧特征完全相同（等于把逐帧检索退化成逐秒）。改为 `(float)clip.SampleRate` 后重新校准步态速度（walk 138 / run 297 / sprint 548 cm/s）与 KD 的 K 值（128/32），并给停止查询加净偏航惩罚，避免站立时选中转身剪辑。
- [x] **收尾审计（D15）**：参考实现剩余模块逐项判定——相机 rig 由现有相机栈承担、IK Retargeter/PBIK 对单一 MetaHuman 骨架不适用、打包运行受本机缺少独立游戏 exe 限制；无未决的核心 GASP 功能项。

## 5. 数据结构（`gasp_anims.bin` v1）

```
magic  "GASPDB1\0"       8B
version u32              1
sampleRate f32           30
clipCount u32
per clip:
  nameLen u16 + UTF8 name
  pathLen u16 + UTF8 path
  guid 16B
  length f32, fps f32
  loop u8
  channelCount u32
  per channel:
    nameLen u16 + UTF8 node name
    flags u8   (1=pos, 2=rot, 4=scale)
  frameCount u32
  per frame, per channel（按 flags 顺序）:
    pos: f32×3   rot: f32×4 (x,y,z,w)   scale: f32×3
```
所有浮点为 little-endian。曲线在采样时按引擎语义：时间夹在 `[0, length]`，线性插值，四元数 slerp。

## 6. 代码落点

```
HundunWorld/Source/Game/Gasp/
  GaspAnimationDatabase.cs   二进制库读取/剪辑/姿势采样
  GaspSkeleton.cs            通道名→节点、层级、绑定姿态、局部/模型空间矩阵
  GaspPose.cs                绑定+通道合成、世界矩阵、逐骨混合
  GaspFeatureSchema.cs       特征通道与权重（唯一真源）
  GaspMotionDatabase.cs      标称速度推断、逐帧特征构建、暴力/KD 检索、落脚检测
  GaspFeatureKdTree.cs       方差分割 KD 树 + KNN（稠密/稀疏 LOD）
  GaspMotionMatcher.cs       查询构建（历史/预测/前向偏航）、续播/重选、选择结果
  GaspBlendStack.cs          多播放器混合、逐骨权重、骨骼掩码与剖面事件
  GaspBoneMask.cs            骨骼掩码（全骨骼/子树）
  GaspPoseWarp.cs            转向扭曲纯几何层（绕骨盆竖轴旋转/逼近）
  GaspLegSolver.cs           组件空间双骨 IK、骨盆补偿、子树重铺
  GaspFootstepTimeline.cs    落脚时间线（区间跨点/循环回绕）
  GaspFoley.cs               脚步事件消费端（音频资产可选，缺资产走事件模式）
  GaspDebugDraw.cs           调试绘制（查询轨迹/朝向/速度/落脚点/剪辑信息）
  GaspTraversal.cs           翻越结果/选库/运动扭曲计划/状态机
  GaspTraversalProbe.cs      Flax 射线探测器 + GaspTraversalDriver
  GaspDataAssetBaker.cs      编辑器内把 bin 包成 RawDataAsset（分发）
  GaspRuntimeCache.cs        多角色共享动画库/特征库（进程内缓存）
  GaspAnimator.cs            Script：挂到角色根，驱动 MM、脚部贴地、扭曲、脚步并写骨骼
  GaspSelfTest.cs            运行期自检（标记触发）
  GaspTestFixture.cs         运行期夹具（标记触发，不依赖登录）
Tools/GaspBakeAnimations.py  离线烘焙工具（可重复、带结构自检）
Tools/GaspSkeletonExport.py  SkinnedModel 骨架导出（离线验证用）
Tools/GaspHarness/           离线核心验证（103 条断言，真实库+真实骨架）
```

## 7. 里程碑

1. M1 地面 MM：Idle/Walk/Run/Sprint/Turn/Stop 数据 + 检索 + 混合 + 骨骼写回；PIE 可跑。**已完成**。
2. M2 空中状态机 + 远端玩家接入。**已完成**：空中状态机编码、远端 `RemoteMode` 接入并 PIE 对照一致；真实世界登录链路已打通。
3. M3 Traversal（Mantle）+ 运动扭曲。**已完成**：D10 联机权威（共享曲线 + v17 协议 + 客户端预测 + DS/分片服务端裁决，网关/服务端 60/60），真实台面（120cm/60cm）PIE 全链路验收；多客户端同时翻越的可见性尚未取证。
4. M4 FootPlacement + LegIK。**核心/写回路径已完成，含脚掌坡度对齐与倾角钳制；真实地面/坡度目视待补**。
4b. M4.1 表现层（掩码分层、KD/LOD、转向扭曲、Foley 脚步、剖面事件、调试绘制）。**已完成并离线/实机验证**；参考实现剩余：相机 rig、IK Retargeter/PBIK。
5. M5 网络远端降级模式（低帧率、仅表现）与打包数据分发。**远端 RemoteMode、RawDataAsset 分发、多人共享缓存与 GameTarget Development 编译已完成；本机 Flax 安装不含独立游戏 exe，打包运行验证无法执行（环境限制）**。

## 8. 决策清单（待用户确认的可选项）

- Q1 第一版是否保留现有 `LocomotionGraphDriver`？**建议保留但默认停用**（GASP 激活时自动禁用其写参数），便于随时回退。
- Q2 远端玩家是否也上 MM？**建议第二阶段**：先本地玩家取证，远端先沿用图。
- Q3 蹲伏/滑行/轻功是否并入 MM 库？**建议第二阶段**做 Crouch 库；轻功继续独立。
- Q4 数据文件是否入库？**建议不入库**（生成物），由 `Tools/GaspBakeAnimations.py` 重建；只在需要出包时拷贝。
