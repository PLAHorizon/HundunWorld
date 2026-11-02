# MetaHuman 插件完成度审计（2026-10-21）

> 目的：把"目标四项"逐条映射到**可复现的证据**，并明确仍未覆盖的部分。
> 本文件只写结论与复现入口，不重复实现细节（细节见《MetaHuman对标UE5.8_方案与决策清单.md》§131 等）。

## 1. 四项要求 → 证据

| 要求 | 现状 | 证据（可复现） |
|---|---|---|
| ① 完整适配 UE5 MetaHuman 资源、实现全部功能 | **主体完成，未全量** | 头：821 shapes / 552 骨骼 / 9 网格（`model_inspect Content/Character/Models/head_mh/Ada_FaceMesh_conformhead4.flax`）；UE 头部材质函数图规格在 `docs/metahuman/ue_runtime/`；WM 法线链 `Normal←Divide(442)`（443 节点/698 连线）；41/41 权重由 morph 的 MaterialFloat 驱动；隔离像素差 38,164 px（>30: 32,071，max 223） |
| ② 编辑器内用数字人模板制作基础角色、自动绑骨、骨骼动画 | **完成** | `metahuman_fixture`：876 骨骼 / 821 shapes / 2 模板；引擎自检 109/0/1；动画重定向 8/8 与 9/9（`AnimationPlaybackProbe`） |
| ③ 发布后玩家改模板数值自定义（重点头部） | **完成（身体极端值有已知残片）** | 598 头部参数；滑杆提交回执 `[SculptDrag] foundInTemplate=True`；morph drive ↔ 撤消；材质实例 `head_wm1_normal_head_wm1_blink_L` 1↔0；预览头加载 `headModel=head_mh / 552/598`。身体：177.5 正常、220 下右颈肩小碎片+左侧浅腰缝（`Tools/_v220_on.png`），权重侧五种修法全部证伪（见 §3 #60）；其他身体参数 drive/撤销正常 |
| ④ 自定义可回溯任意步骤、发布后不可回溯 | **完成** | 差分历史任意步回跳（材质权重同步归零）；服务端封存闸门测试 12/12；真集群外观通道 **22/0**（character 999001，Redis 9379；含 v2 blob `Revision=7` 回环、发布位保留）；两份已发布外观的完整决策链（解码→7>5 采用服务端／8>7 保留本机／校验和一致判无冲突） |

## 2. 关键复现命令

```bash
# 离线纯核 + 模板契约（1531 断言）
dotnet run --project Tools/MetaHumanVerify/MetaHumanVerify.csproj -p:BaseOutputPath=bin/Verify/

# 客户端编译门
dotnet build Tools/MetaHumanGameGate/MetaHumanGameGate.csproj --no-incremental -p:BaseOutputPath=bin/Verify/

# 服务端外观回归（12 条）
dotnet test Horizon.Game.Gateway.Tests/Horizon.Game.Gateway.Tests.csproj \
  --filter "FullyQualifiedName~MetaHumanAppearanceSealTests" \
  -p:BaseOutputPath=bin/VerifyTests/

# 真集群外观通道（需 Silo + Redis 9379；character 30 未发布）
dotnet Tools/MhAppearanceVerify/bin/VerifyMh/Debug/net10.0/MhAppearanceVerify.dll 999001 127.0.0.1:9379
```

引擎内（编辑器桥 21901/mcp）：

```
metahuman_fixture {"model":"Content/Character/Models/head_mh/Ada_FaceMesh_conformhead4.flax"}
metahuman_selftest
metahuman_drive {"actor":"MH_Fixture","param":"morph.head_wm1_blink.l","value":1}
actor_material_param {"actor":"MH_Fixture","param":"head_wm1_normal_head_wm1_blink_L"}
metahuman_drive {"actor":"MH_Fixture","param":"morph.head_wm1_blink.l","value":1,"jumpTo":0}
```

## 3. 未完成 / 未覆盖

1. **#64 仍差一项**：两份不同已发布副本的**登录态客户端联调**（需角色上下文）；"是否拒绝倒退的 Revision"是产品判定项，当前服务端只存不判。真集群已证 blob 修订号回环（22/0）。
2. **#60/#61 肩缝**：运行时与烘焙侧修法均排除，需 UE/DCC 按 4 影响重导身体。2026-10-21 复看：默认姿态头肩框内**看不到缝**；把 `body.height` 从 177.5 拉到 220 后同框 bbox/像素数不变（异常，见下）。
   - **已定因并复现**：头肩框里看得见的"肩"来自 head_mh，身体在头页不可见 ⇒ 头页调身高像素差为 0 是取景问题。切到**身体页签**后：`body.height 177.5→220` 产生 **3,994 px 差（max 187）**，且右肩/腋下出现明显破面（黑洞与碎片）⇒ **#60 肩缝在极端身高下可稳定复现**。
   - **已排除一个候选修法**：把预览身体换成 `Ada_BodyPreview4s.flax`（同 32,334 顶点、权重 top-4 覆盖均 1.0）后，220 身高下躯干裂成多块、手臂分离，**明显更差**，已回退到 `Ada_BodyPreview4.flax` 并重建（0 CS error）。
   - **第二个候选也排除**：对权重做 1-ring 平滑（2 轮，top-4 归一化，和恒 255）后重导为 `Ada_BodyPreview4w.flax`，220 身高下出现**大量尖刺**（肩、胸、腹），比原资产更差；已回退到 `Ada_BodyPreview4.flax` 并重建（0 CS error）。4w 资产保留但不再被引用。
   - 结论：破面与"4 路权重被裁"无关（两份 GLB 的 top-4 覆盖率都是 1.0），简单平滑也会破坏体积守恒；更像**大位移骨矩阵下的 LBS 分离**。可行方向只剩 UE/DCC 侧按更合理的比例驱动/重绑，或给躯干参数设安全范围做产品兜底。
   - **权重侧读数（只读分析 Ada_BodyPreview4.glb）**：肩部（Y 最高 3%、x>0）479 个顶点、仅 1 个单影响顶点；主导组合集中在 `clavicle_l / clavicle_scap_l / clavicle_pec_l / spine_04 / upperarm_*`。
   - **裂缝的精确形状找到了**：肩带区域（y>1.15、|x|>0.05）共 2300 个顶点，只有 **2 条硬边**（L1 权重差 >0.9），且两边都在**同坐标的复制顶点**上：一份偏 `upperarm_twistCor_01_*`，另一份偏 `clavicle_* + spine_04`。静止位姿重合所以看不见；身高拉到极端时两条骨链位移不同 ⇒ 复制顶点分离成缝/尖刺。这也解释了为什么"全权重平滑"会把体表拉出尖刺（它把不相干的邻域也混在一起）。
   - **登录阻塞机制（2026-10-21 复核）**：客户端登录已迁到 WebApi `/Account/signin`；本机保存账号 `101125808` 仅剩失效 Token、无密码回退，故 PIE 恒停登录页，`open-sculpt.on` 不触发。没有有效凭据时无法自行进入捏脸/身体页，4x 的受支持路径复测只能等一次有效登录。
   - **第四个候选（骨盆 Y 缩放）被契约断言拦下，未进入生产**：离线 LBS 探针显示"脊柱平移"会让边长比出现大量 >2x 的尖峰，而"骨盆 Y 缩放"最大只有 1.24（连续）；但模板契约测试明确要求「身高 +10cm ⇒ 骨骼链位移合计 ≈10cm」，缩放方案的位移合计为 0，harness 报红。已还原模板并重烘内嵌表，**1531/0/0** 恢复。若要走缩放方案，必须先改产品契约（身高值的物理含义），不是能顺手改的测试。
   - **缝合候选（`Ada_BodyPreview4x`）结论：静止不回归、极端身高明显更差**。取得有效测试账号并通过 `HorizonGame.ini` 自动登录后，PIE 进入身体页签实测：静止 177.5 下 4x 与 4 只差 **7 px（>10 阈值）**，即此前的"静止裂开"是换模型当帧的脏帧；但 `body.height=220` 下捕获 `Tools/_v_tall_4x.png`，胸/肩板片整体分离、右腋下开口，像素差 **414,246 px（>20）** ⇒ **焊接复制顶点不能修复 #60，且显著放大破坏面**。
   - **定向焊接（只修肩带复制顶点）也被排除**：`Tools/_body_weld_shoulder.py` 在肩带（竖直顶部 25%、|x|>3% 跨度）里选中 **0 组**——肩带内的复制顶点权重完全一致；全局焊接造成的胸腹板片来自**无关区域的复制组**。⇒ 残片不是"复制顶点分离"。
   - **硬权重边定位**：`Tools/_body_smooth_hard_edges.py` 按位置归并顶点后统计肩带内边 18,178 条，L1 分位 **p50=0.133 / p90=0.251 / p99=0.478 / max=0.933**；99 分位阈值下命中 **190 条边 / 245 组 / 268 顶点**，其余 32,066 顶点逐字节不变、受影响顶点权重和恒 255。
   - **一次失败尝试（已留档）**：首版脚本按**权重槽位**平均却配代表顶点的骨骼 id，产出 `Ada_BodyPreview4s2.flax`。220 下截图**看着干净**（`Tools/_v220_s2.png`），但随后回 177.5 的静止帧（`Tools/_v_rest_s2.png`）胸腹/髋部浮出板片 ⇒ **单帧判据会骗人，换模型后必须先拍静止帧**。按骨骼 id 归并的修正版产出 `Ada_BodyPreview4s3.flax`（已导入、SkinnedModel），第二版（s3）**已在运行态双帧验证并证伪**：静止 177.5 干净（`Tools/_v_rest_s3.png`），但 220 下胸部整块板片分离（`Tools/_v220_s3.png`），破坏面与 4x 同型且远大于原资产 ⇒ **肩带硬边平滑同样不能修 #60**。生产仍为 `Ada_BodyPreview4.flax`。另：捏脸页在该工程要等约 5 分钟才加载（`CcFaceSculptPage` 日志），此前"PIE 卡世界场景"是等待不足。
   - **骨架继承关系已排除**：`clavicle_l/r ← spine_05 ← spine_04 ← …`、`upperarm_correctiveRoot ← upperarm ← clavicle`、`head ← neck_02 ← neck_01 ← spine_05`，肩带骨全部在脊柱链下，不是"矫正骨挂 root 导致位移不继承"。⇒ 残片来自 LBS 混合本身（硬边两侧位移差），不是骨架层级错误。
   - **离线 LBS 探针的读数已判定不可用于选型**：`Tools/_body_lbs_probe.py` 在 190 档就报 `translate:current p99=12.699 / max=27.347`，而运行时 190 档实测只有约 5,024 px 的正常变化、无明显破面 ⇒ 探针把每根骨的位移独立施加，没有建模"子骨继承父骨平移"；它的 `pelvis Y scale` 读数（190–220 均为 1.0–1.24 连续）同样不能作为修法依据。**根因已定位**：写回端是 `pose[index] * Matrix.Translation(...)`（`MetaHumanAppearanceApplier.cs:813`，在**骨骼自身局部轴**叠加，2026-10-13 引擎实测左乘无效才改右乘），而探针改的是节点局部矩阵的平移分量（**父空间**）；带旋转的骨上两者轴向不同，所以探针读数失真。已按此把探针改成局部轴右乘（`Tools/_body_lbs_probe.py`），**190/220 读数逐位不变**（p99 仍 12.699 / 42.305）⇒ 轴向空间不是差异来源。更可能的原因是**运行时并没有把这 5 个 height 目标全部解析并写回**（骨骼名/别名未命中时会静默跳过，见 applier 的 `SkippedBoneCount`），所以实际形变远小于探针假设的全量施加。**幅度公式已核实**：`MetaHumanSolver.cs:184` 对 BoneTranslate 用 `Offset + Gain*value`（如 spine_01：-39.05 + 0.22×220 = 9.35cm），与探针假设一致 ⇒ 差异不在幅度。因此只剩两种可能：①运行时骨骼目标实际未全命中（要看 `SkippedBoneCount`/写回条数）；②探针的蒙皮/层级计算与 Flax 实际求值仍有差异。夹具在本次会话不可实例化（`AddScript` NRE，需重启编辑器），下次用捏脸页或重启后的夹具读这两组计数。
   - **200/210 档位测量未取到**：同一天两次 PIE 分别在 5 分钟、8 分钟后仍未出现 `MetaHumanSculptHost`（捏脸页加载时机不稳定，非登录问题），故"安全上限落在哪一档"仍无像素证据，不要据此收窄 `body.height` 上限。
   - **定性修正（2026-10-21 夜，运行态）**：碎片**不是身高专属**。从历史 0 的干净基线做 `height 0→220→177.5` 往返：220 只有右颈肩小碎片+左侧浅缝（`Tools/_r220.png`），回到 177.5 **完全干净**（`Tools/_rback.png`）⇒ 写回端撤销正常。另测 `body.across_shoulder=54`（BoneScale clavicle XZ）与 `body.shoulder_height=192`（BoneTranslate spine_03/04+clavicle）各自都会撕开同一块胸肩区域（`Tools/_p_as54.png`、`Tools/_p_sh192.png`）。此前加载的持久外观（height=207.4、历史 23 步）在任何身高都带大片碎片，说明是**某个非默认身体参数**造成的，不是 220 特有。
   - **`EnableBoneScaleChannel` 默认 false ⇒ 所有 BoneScale 参数是空操作**（bust / bust_span / neck_base / across_shoulder 等）。实测：默认外观下 `body.bust=160`、`bust_span=36` 画面**完全不变**（`Tools/_b160.png`、`Tools/_bspan36.png`）。这是"身体参数看似可调但部分无效"的真实功能缺口。
   - **打开缩放通道后的原型结论**：把 height 改成 pelvis Y 缩放（gain=1/177.5）后，135/220 两档**躯干都平滑**（`Tools/_s_135.png`、`Tools/_s_220.png`），但画面出现巨大 Y 向长条（来源疑似非骨盆链几何/缩放叠加），⇒ 缩放方向有前途但未闭环。已全部回退（模板恢复备份、通道默认恢复 false、重烘内嵌表、热重载成功），harness **1531/0/0**。
   - **#65 已修并验证**：`MetaHumanAppearanceApplier` 新增 `_appliedScale`，每轮写回前按"先撤缩放、再撤平移"撤销上次缩放（`Clear` 同样撤）——此前缩放会逐帧累乘，拖动几帧就出现数米长的 Y 向条带。打开 `EnableBoneScaleChannel`（默认 true）后：`bust=140` 形态真实改变（3,428 px），仅右颈小碎片；`bust_span=30` 干净；`bust=160` 极值仍小碎片。**骨盆 Y 缩放不能驱动身高**：135/177.5/220 三档轮廓高度都量到 897px（缩放不传播到子骨偏移）⇒ 身高继续用原 5 条 BoneTranslate。harness **1531/0/0**。
   - **缩放开通后的身体参数覆盖面（PIE 像素扫描，`Tools/_sw_*.png`）**：bicep +12.5 单位 → 4,488 px；hip +14.5 → 708；waist +12.5 → 304；neck_base +8 → 163；masculine_feminine +1.5 → 59；muscularity +0.3 → 40；fat +0.3 → 19；**thigh +13 → 0、neck +6.5 → 0**。归因：`neck_01/neck_02` 在本身体网格上权重质量=0（颈部归头部网格），`thigh_fwd/bck` 虽有 23k/17.6k 权重但 13% 缩放只改变轮廓内部；头页单独测 `body.neck=50` 也只有 4 px ⇒ 这两个参数当前近乎无效，需改绑定到真正承重的骨或换形体 morph。
   - **身体参数完整矩阵（24 参数，缩放通道开启，PIE 像素 ≥20）**：有效——bicep 4,488 / shoulder_to_apex 897 / front_interscye 794 / high_hip 706 / upper_arm_length 616 / rise 552 / forearm 457 / hand_circumference 452 / wrist 451 / across_shoulder 380 / elbow 348 / lower_arm_length 311 / waist 304 / neck_to_waist 161 / neck_base 163 / underbust 149 / hip 708 / bust 3,428（140 档）/ bust_span 368（30 档）/ muscularity 40 / masculine_feminine 59 / fat 19；**无效（0 px）——calf / inseam / knee / neck_length / thigh / neck**。无效项的目标骨在 GLB 里权重为 0（neck_01/02）或只有低质量（calf_knee 42k），也可能在 288 骨骨架里未解析；`model_inspect` 不吐骨名，无法离线判定 NodeIndex 命中 ⇒ 下一步给 `metahuman_drive`/`inspect` 暴露 `SkippedBoneCount` 与 `BoneWritesLastPass`。
   - **编辑器编译失败的真凶是 PCG，不是 MetaHuman（2026-10-22 00:0x）**：`code_build_status` 连续报 `lastCompilationFailed=true`，但把 PCG 的 `PCG.CSharp.response` 手工交给 csc 后得到两条 **CS0117: `Globals` 不含 `EngineContentFolder`**（引擎更新后托管 API 移除了该名）。修法：`PCGSelfTest` 的默认夹具路径改为 `EngineContentFolderBestEffort()`（从 `Globals.StartupFolder` 向上找带 `Editor` 子目录的 `Content`，找不到返回空串 ⇒ 原语义仍是 skipped）。修后 `Cache/Intermediate/Log.txt` 以 `Done! / End.` 结束且无 `: error`，PIE 冒烟通过（捏脸页 `characters=1`、`body.bicep 41.5→50` 提交成功）。**教训**：桥的 `code_build_status` 只给布尔且可能是中途/陈旧状态；二进制文件 `HundunWorld/Cache/Intermediate/Log.txt` 才是编译结果与错误明细的权威来源。
   - **写回计数插桩尝试（已回退）**：给 `metahuman_drive` 回执加 `boneWrites/skippedBones/applierWarnings/skippedBoneNames`，并给 Applier 加"本轮跳过骨名"名单。首次两个字段版本编译通过，后续版本编辑器持续报 `lastCompilationFailed=true` 但输出无 CS 明细（`code_build_status` 只给布尔、`log_errors` 扫到的是游戏日志）；已全部回退到本轮可编译基线。已有读数：`boneWrites=387`、`skippedBones` 每次驱动增加约 4,356，跳过名单前 8 个全是头部 `FACIAL_*`（被头部 applier 占满，身体 applier 的名单看不到）⇒ **身体参数的目标骨名解析情况仍未判定**。下一步要么单独输出身体 applier 的跳过名单，要么重启编辑器后重编再看。
   - **勘误：#66 的“骨无蒙皮”结论作废**。`model_skin_probe` 的 `boxDelta` 是**全局包围盒**变化：小腿/颈部骨移动不会改变整体极值，0 不代表无蒙皮（正例 `upperarm_bicep_l` 也只是因为手臂接近顶部才改变 bbox）。#66 仍以像素实验为准（calf 13%/100%/改绑 twist/三轴等比均 0 px；neck 0 px；thigh 0 px），根因未定，下一步需要按局部区域/逐顶点的度量。
   - **原记录（勿再引用其结论）**：`model_skin_probe` 逐骨驱动（单骨平移 50cm / 批量 100cm 的包围盒 ΔY）——`upperarm_bicep_l` 15.85/40.85、`thigh_fwd_l` 4.22/29.22、`thigh_bck_l` 3.20/28.20、`thigh_twistCor_01` 0/19.78、`calf_knee_l` 0/6.34、`calf_twist_01_l` 0/0、`calf_l` 0/0、`thigh_l` 0/0、`neck_01/02` 与 `head` 0/0 ⇒ 腿部/颈部几乎无承重骨；模板改绑（calf→twist）与 gain×8、三轴等比都无效。6 个无效参数（calf/inseam/knee/neck_length/thigh/neck）必须靠重做身体蒙皮/形体解决。
   - **2026-10-22 资产修复（已验证）**：编辑器/引擎更新后，多个 .flax 出现“注册表 ID ≠ 头部/子资产引用”损坏：
     - 身体 `Ada_BodyPreview4.flax`：`asset_import` 同一 GLB（Type=SkinnedModel）后蒙皮恢复（bicep 8,533 px）；
     - 头部 `head_mh/Ada_FaceMesh_conformhead4.flax`：必须用 `ImportBlendShapes=true, LODCount=4, ImportMaterials=true, MergeMeshes=false` 重导（9 网格 / 9 材质槽 / **821 shapes**），再把 `MH_Head_New` 指到全部 9 槽；否则只剩 2 槽、眼睛全白。修后脸部/虹膜/嘴唇正常，眨眼 morph **18,902 px**。
   - **判断“测的是不是 Ada 身体”**：截图顶部 15% 若出现头部像素或橙色关节 ⇒ 页面已回退 `Character_1` 备胎，读数作废；必须先有 `bicep` 正例像素差，再谈其它参数。
   - **身高分布改版（已验证，harness 仍 1531/0/0）**：把 height 的位移摊到 `spine_01..05 + neck_01/02 + head`（gains 0.14×5 + 0.10×3 = 1.0）。破面像素：190 从 22,305 降到 **18,153**；220 从 61,495 降到 **48,929**。板片仍在 ⇒ 属资产蒙皮/形体上限。
   - **重要前提：#66 的四组“修法实验”作废（模板缓存）**。哨兵法：把 `body.calf` 临时改成“平移 pelvis 70cm”，驱动 50 后画面仍 0 px；`metahuman_selftest` 也不清缓存（`MetaHumanTemplateStore.ClearCache()` 只被模板编辑窗口调用）⇒ 同一编辑器会话内改模板 JSON 后，新 PIE **不会重读**。故 gain×8、450%、改绑 `calf_twist_01`、三轴等比四组实验均未真正生效。仍成立的是：**原始模板**下 calf/neck/thigh 驱动 0 px。下一步必须先重启编辑器，再单项验证并打印模板效果确认已重读。
   - **世界角色外观缺口（2026-10-22 定位）**：`CreateLocalPlayerActor` 只做 `EnsureBodyModel`（Character_1），**不挂 MetaHumanCharacter**；`MetaHumanCharacterRenderer` 只被展示场景使用。已落地最小原型：本地玩家创建后挂 `MetaHumanCharacter`（`TargetModels=本角色 AnimatedModel`、`PersistenceCharacterId=characterId`、`ApplyOnStart=true`），代码已通过客户端编译门；但本轮两次进世界尝试分别被“退出捏脸的确认框 / 登录页未跳转”挡住，**运行态尚未验证**。验证步骤：标准流程进世界 → 日志里查 `已挂 MetaHumanCharacter … loaded=?, diag=?` → `metahuman_inspect` 应列出 `LocalPlayer_<id>` → `metahuman_drive body.height=190` 后 `game_screenshot` 有像素差。
   - **回归自检（2026-10-22，PIE 内带角色）**：`metahuman_selftest` **105 通过 / 1 失败 / 3 跳过**。失败项是“骨架名唯一性”组：**Character_1.flax 自身节点 1109 / 唯一名 558 / 重名 551**（root×2、DEF-spine×2…），模板目标 `head` 在其中重名（head×2）⇒ 写回可能落到另一根。这是我们修复的 head_mh / Ada 身体之外的老资产问题；跳过 3 项均因“当前场景无 blendshape 资产/无 warp 头资产”。head_mh + Ada 身体路径未出现此失败。
   - **新修法也证伪：位移离群定向权重转移（2026-10-22）**。`Tools/_body_fix_height_outliers.py` 在 220 位姿下找“与 1-ring 邻居位移中位数差 >8cm”的胸区顶点，从位移正常邻居复制权重（3 轮，共改 2,182 顶点，权重和恒 255），导入为 `Ada_BodyPreview4h.flax` 后运行态 220 仍 220：**48,512 px vs 原 48,929 px，板片原样**。⇒ 撕开不是“少数离群顶点权重错”，而是整片胸区在大位移下的 LBS 形态问题；模板/权重两侧都已到上限。
   - **勘误：#66 的“腿部参数无效”是取景裁剪（2026-10-22）**。预览默认只拍到大腿中部，小腿/脚不在画面内；滚轮拉远（`ui_input action=wheel delta=-2` ×8）后，在 Ada 身体（无头）且 bicep 正例 8,513 px 的可靠通道下复测：**calf 1,304 / knee 1,341 / inseam 1,969 px 全部有效**，thigh 2,263 px。真正无效仅剩 `neck / neck_length`（neck_01/02 在身体与头部网格权重质量均为 0）。**判据**：测腿参数前必须确认脚在画面内。
   - **calf 的像素级定论（2026-10-22）**：`body.calf` 在预览里不可驱动——13%、100%（gain×8）、约 450%（gain 0.5，`Tools/_cx_50.png` 对 `_cx_base.png` 仍 0 px）三次放大均无变化；改绑 `calf_twist_01` 与三轴等比同样 0 px。模板已恢复、内嵌表重烘、harness **1531/0/0**。
   - **calf 类参数的决定性实验（2026-10-22）**：`body.calf` 在预览里对任何骨都不承重——gain×8（13%→100% 量级）仍 0 px；改绑 `calf_twist_01_l/r`（GLB 权重 191k）仍 0 px；三轴等比缩放仍 0 px ⇒ 不是轴/增益/名字，而是该区域在预览装配（288 骨 skeleton + top-4 权重）里没有可驱动顶点。模板已恢复、内嵌表重烘、harness **1531/0/0**。已在 Applier 加"本轮跳过骨名"并接进 `BuildDiagnosticReport`（重启编辑器后生效）。
   - **`shoulder_height` 尖刺来源已缩小**：去掉 clavicle_l/r 两项后 192 仍尖刺 ⇒ 主因是 spine_03/04 的**逐段平移累加**（权重跨段混合时相邻顶点差 14cm），不是 clavicle 额外项。
   - **捏脸页入口已提速**：PIE 起在登录面板时，点 `LoginHoverButton`（328×50 @36,326）即可直接进捏脸页（`characters=1`），不必再等约 5 分钟；`ui_click_control` 建议带 `canvas:"InkWashUICanvas"`，否则会点到隐藏的登录按钮。
   - **生产资产在 220 的真实上限（2026-10-21 重测，`Tools/_v220_on.png`）**：同页签、同取景（body/whole）下，`Ada_BodyPreview4` 的 220 身高躯干整体平滑，只剩**右肩颈几片小碎片 + 左侧腰一条浅缝**，远小于 4x 的整块板片。把 `CharacterPreview3D_SkinnedBody` 的 `IsActive` 设为 false 后整帧全黑（`Tools/_v220_off.png`）⇒ 身体页签下可见几何**全部来自蒙皮身体**，板片与 head_mh/模板头无关（页面本身已 `SetMetaHumanHeadVisible(!body)`）。
   - **针对性修法产出记录（已证伪）**：`Tools/_body_weld_weights.py` 只对"坐标完全相同的顶点组"做权重平均（1857 组 / 3736 顶点，组内一致性断言通过），产出 `Output/mhbaked/Ada_BodyPreview4x.glb`（32,334 顶点不变，权重和恒 255）。资产导入成功（显式 `Type=SkinnedModel`），上条运行态复看已判定无效；生产常量保持 `Ada_BodyPreview4.flax`（`EquipmentDatabase.cs:185`），运行时换模型只影响该次 PIE 会话。
3. **#34 毛发卡片不透明度**：UE python 烘焙路线判死（DynamicMesh 未绑定）。
4. **UE 法线链的其余分支**：微法线/Normal_BAKED/细节法线的完整混合、移动档回退未实现。
5. **像素证据范围**：只覆盖"接/断 Normal"的静态帧差异；滑杆级像素幅度未逐参数建表。

## 4. 部署/环境注意

- `Horizon.Game.Message` 已回到 8 槽（修订号在 blob v2），跨版本双向兼容实测通过；运行中的编辑器需重启一次才能卸载旧 9 槽装配。
- 改契约后要同步 `HundunWorld/Output/Windows` 与全部 `Binaries/*/Horizon.Game.Message.dll`。
- 群 Redis 端口 **9379**；旧测试号（40 等）grain 状态已发布，会跳过发布前通路。
- 编辑器重启后若资产 `knownToContentDatabase=false`，先重启重扫；头部加载已有 Guid 重载兜底。
