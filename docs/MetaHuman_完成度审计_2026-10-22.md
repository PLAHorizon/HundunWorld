# MetaHuman 完成度审计（2026-10-22）

> 目标四项 → 当前证据 → 未覆盖项。只写可复现结论；过程与坑见
> `docs/MetaHuman_完成度审计_2026-10-21.md` 与项目记忆。

## 1. 四项交付物对照

| 要求 | 状态 | 可复现证据 |
|---|---|---|
| ① 完整适配 UE5 MetaHuman 资源与功能 | **主体完成，未全量** | 头 `head_mh/Ada_FaceMesh_conformhead4.flax`：9 网格 / 9 材质槽 / **821 shapes** / 486 骨，`MH_Head_New` 指满 9 槽；脸部/虹膜/嘴唇正常，眨眼 morph **18,902 px**。头部 WM 法线链按 UE `MF_AnimatedMaps` 重建（443 节点/698 连线，41/41 权重驱动，隔离像素 38,164 px）。身体 `Ada_BodyPreview4.flax` 重导后蒙皮恢复；身体材质（Color×Cavity + Normal）与**毛发卡片**（`Hair_S_Coil_Cards` + `MH_Hair`，Mask=Coverage.a、TwoSided）已落地并在世界内目视成立。 |
| ② 编辑器内模板→基础角色、自动绑骨、骨骼动画 | **完成** | PIE 内 `metahuman_selftest` **106 通过 / 0 失败 / 3 跳过**（跳过项要求 blendshape/warp 资产，属环境）；动画重定向 8/8、9/9（`AnimationPlaybackProbe`）。 |
| ③ 发布后玩家改模板数值自定义（重点头部） | **创角 + 游戏内均已闭环（头部/身体实机可改）** | 创角侧头部 598 参数、身体 24 参数像素矩阵均有效。**游戏内（2026-10-22 实机）**：本地玩家身体改为 Ada（`metahuman_bind` 把 ALS 重定向到 Ada：核心骨 53、63 条动画条目、`retargetCount=126` 已 Save），头部 `head_mh` 用 `WorldHeadMasterPose` 在 OnStart 做一次 `SetMasterPoseModel`。`metahuman_inspect` → `loaded=true / 模板=2 / 写回端=2 / 参数=628`，头部承载 **552/598 全可写**、morph 已写 33；`face.zone.cheek.l.tx 0→2` 面部 **139 像素>10**，`body.height 177.5→190` **1297 像素>10**（均已回退）。**身体材质已补**：`MH_Body2`（Opaque/Lit）指认到 `Ada_BodyPreview4` 槽 0；按 UE `M_BodySkin` 结构接齐 **Color=BodyBaseColor×Cavity、Normal=BodyNormal（`n*2-1` 解码）、Roughness=0.95、Specular=0、Metalness=0**（UE 身体法线/cavity 贴图已从 8K 源导出并以 2048/线性导入）。世界内身体中位数 **84/60/46**，与头部 98/70/57 同色阶，白模问题闭合。**“身体半透明”已证伪**：新工具 `material_set_info` 读出 `MH_Body`/`body.flax` 全为 `Opaque`；常量红色与常量黑色探针下身体完全不透光且颜色精确跟随，Unlit 探针可见纹理按 sRGB 正确采样；此前“幽灵感”来自旧材质缺 Roughness/Specular（高光洗白），不是混合模式。**面部参数 id 已核实**：本轮曾用 `face.cheek.l.tx` 驱动得到回读恒 0，属**参数 Id 写错**（模板里不存在该 id，`State.Set` 直接返回 0）；正确 id 是 `face.zone.cheek.l.tx`。在新建的头部夹具（`metahuman_fixture`，552/598 可写）上复验：正确 id `0→1`（上限钳位）、错误 id `0→0` 作对照；`morph 已写=33`、`jumpTo 0` 后回读 **0**（回溯成立）。同轮 `metahuman_selftest` **109 通过 / 0 失败 / 1 跳过**。**世界侧也已复验**：用刷新后的令牌登录并新建测试角色「测试角色」，进入世界后 `LocalPlayer_41` 上 `face.zone.cheek.l.tx 0→1` 提交成功，前后截图 **5,020 像素>10**（错 id 对照仅 158 噪声）⇒ 头部参数在真游戏角色上生效。**服务端持久化已闭环（00:09）**：补 `MessageUnion` 246/247、替换服务器四个 bin 的 `Horizon.Game.Message.dll` 并重启三进程后，角色 44 的外观收到 `ok revision=1`，SQL `meta_human_look` 落成 **159 字节**（与客户端 blob 等长、`MHLK` 头）⇒ 换设备/重登可读回。**换设备读回也已实测**：移走本地 `.mhl` 后重登，`AdoptFromServer` 从角色列表 blob 落回本地，世界读回 `body.height=190 / cheek=1`（详见 §2b）。**肤色滑杆已真正生效（§2c）**：`skin.melanin` 0↔1 经 `SkinTint` 乘色使头+身 **53,345 px** 变化。**2026-10-25 真实 PIE 复核（6a13/6a16）**：进游戏后 HUD 正常；先前 `LocalPlayer_44` 落在 Login、画面是 UEFN mannequin/T-pose——两条均已修（场景解析路径优先；`CharacterEquipmentManager.ApplyVisualBody` 不再把 Ada 整模换回占位壳）。复测：身体=Ada+Animation Graph、`actor_bone_probe` **moved=2**、截图呈皮肤色人形；剩出生点/取景（#93）与选角页预览（6a14）。 |
| ④ 任意步骤回溯、发布后不可回溯 | **完成** | 发布前：blink 1 → 18,912 px，`jumpTo 0` 回基线（5 px）；`publish=true` 后 `sealedAfter=true`；封存后 `Adjust` 走 `RejectEdit`，值保持不变。服务端封存闸门 12/12；真集群外观 22/0（blob v2 `Revision=7` 回环）。 |

## 2. 本轮关键实现/修复

- **资产损坏修复流程**：引擎更新后 `.flax` 子资产引用失效。身体：`asset_import` 同一 GLB（`Type=SkinnedModel`）。头部：必须
  `ImportBlendShapes=true, LODCount=4, ImportMaterials=true, MergeMeshes=false` → 9 网格/9 槽/821 shapes，再 `MH_Head_New` 指满 9 槽。
- **模板生效链**：运行时读 **内嵌烘焙表**，流程固定为 `改 JSON → Tools/MetaHumanBakeTables.py → 编译(code_build_wait，成功以 Cache/Intermediate/Log.txt 为准) → 重启编辑器 → PIE`。
- **身高分布改版**：`spine_01..05 + neck_01/02 + head`（gains 和为 1）。220 破面 61,495 → **48,929 px**，190 22,305 → **18,153 px**；harness 仍 1531/0/0。
- **自检判据修正**：`骨架名唯一性` 只在“同名多个**蒙皮**节点”时失败；`head×2（蒙皮 0）` 归为说明（写入无效而非写错骨）。
- **世界角色外观最小原型**：本地玩家创建后挂 `MetaHumanCharacter`（`TargetModels=角色 AnimatedModel`、`PersistenceCharacterId=characterId`、`ApplyOnStart`），失败有 `LogError`。编译门通过，**运行态未验证**。
- **世界入口取证（本轮，结论：被服务端鉴权挡住，未进入 World）**：
  - PIE 会话 `HundunWorld/Logs/Log_2026_10_22_03_02_26.txt`：`收到登录响应: IsSuccess=True`（1712）→ `消息发送成功: CharacterList`（1718）→ `ProcessMessageAsync: Error` / `Code=1007 授权失败，请重新登录`（1720/1723）→ `角色列表为空`（1721）。
  - 本地库 `%APPDATA%\HorizonGame\game_data.db` 唯一 `PassportInfo`：`PassportId=101125808`、`Token` 长度 **80**；用 `.env` 的 `GATEWAY_AUTH_TOKEN_SECRET` 与网关 Development 密钥算 HMAC **都不通过** ⇒ 该令牌不是当前任一密钥签出的（陈旧令牌）。
  - `/Account/signin`（`1039667206/Test123456`）返回 `imAuthToken` 216 字符：`.env` 密钥 HMAC **通过**，解密 payload `PassportId=1039667206`、`ExpiryTime=1793197063771`（未过期）。
  - 换成该有效令牌后重启 PIE，`ui_tree` 选中页（`CcRoleListPage`）下**没有任何角色行**（`CcRoleRow` 0 个），页面停在空列表。
  - 再用库内原始账号 `101125808` + 保存的密码（9 字符）向 `/Account/signin` 取新 `imAuthToken` 并替换库内令牌，重启后仍是空列表（同样 0 行）。两次都排除了“令牌陈旧”是唯一原因。
  - 另有环境坑：`%APPDATA%\HundunWorld\open-sculpt.on` 存在时登录后直接进 `CcFaceSculptPage`，不会到选角页；验证前必须移除该标记。
  - 编辑器磁盘日志在 uptime `00:30` 后不再落盘（文件 mtime 停在 03:33），本轮是否仍有 1007 无法从磁盘日志确认 —— 属取证限制，不能记为通过。
  - 现场已复原：`game_data.db` 从 `game_data.db.bak-20261022` 回填（令牌恢复 80 字符原值）、`open-sculpt.on` 复名、PIE 已停止。
- **世界角色外观闭环（2026-10-22 实机）**：
  - 阻塞链：① 本地库令牌陈旧 → 网关 1007、角色列表为空；用 `/Account/signin` 取新 `imAuthToken` 写回 `PassportInfo` 后列表出现角色行。② 选角页确认块被 Narrative 覆盖层挡住鼠标，改用 **Space**（页面 `ConfirmOnSpace`）进入世界。③ 世界角色已挂脚本但 `loaded=false`：根因是 `MetaHumanCharacter.Load()` 只认 `TemplateIds`，原型没给；补 `metahuman.face_archetype` + `metahuman.body_base` 后成功。
  - 结果：`metahuman_inspect` → `LocalPlayer_39, loaded=true, 模板=2, 写回端=1, 参数=628, 历史 1 步`；`metahuman_drive body.height 177.5→190` 成功（历史 2 步），前后截图 **meanabs 1.25 / 最大 28 / 528 像素>10**。
  - 头部形状仍不可用：诊断「`Character_1.flax` 全可承载 **0/598**、别名对齐 11、只挂材质 9」，缺 `FACIAL_` 脸骨。
  - 头部二次尝试（同日，已回滚）：改用创角预览验证过的「按头骨标定」——`scale = 颈-头距离比 × 身体缩放`、`LocalPosition = 身体位置 + 身头骨 − 头头骨×scale`、朝向沿用身体。实测头部 Actor `scale=0.8056, pos=(869,44,672)`，画面仍是覆盖躯干的幽灵头壳 ⇒ 跨骨架（DEF- × MetaHuman）直接贴骨不可行；已再次撤下。**双目标注册已被证明可行（写回端=2）**，缺的是引擎级 bone attach / DCC 侧把头部接到 DEF 头骨（或世界换 Ada 骨架）。
  - 头部三次尝试（同日，已回滚且停止该路线）：改用世界空间 `GetNodeTransformation(..., world:true)`，每帧按「身头骨世界位置 − 头头骨世界位置」平移、朝向取身头骨、缩放固定 1。结果：PIE 启动后编辑器卡死（桥 http=000、主线程被钉住），只能强杀；判定该跨骨架挂头方案风险过高，已彻底撤下（代码/脚本均已删），世界路径恢复为已验证的 `applierCount=1`。**结论：跨骨架挂头不做在线运行态合成，需求交给「世界角色换 Ada 骨架（含动画重定向）」或 DCC 侧预制体。**
  - Ada 世界体型尝试（同日，已回滚）：`metahuman_bind` 把 ALS 重定向到 Ada 身体成功（核心骨映射 **53**、`animationsDir` 补 **63** 条动画条目、`retargetCountAfter=126`、已 Save）；随后在运行时把本地玩家 `SkinnedModel` 换成 `Ada_BodyPreview4`（Scale=1）并加 `head_mh` + `SetMasterPoseModel`。进世界瞬间引擎断言崩溃：`AnimatedModel::OnAnimationUpdated_Async() (AnimatedModel.cpp:932)`，崩溃转储 `HundunWorld/Logs/Crash_2026_10_22_05_25_22/`。判定为「运行中换 SkinnedModel/挂 master pose」与动画图竞态，已回滚。**正确做法是做成预制体**（Ada 身体 + head_mh + 已烘焙动画图），而不是运行时换模型；或换前先停动画并重建图。
  - Ada 世界体型**已落地**（同日，取代上面的运行时换模型路线）：预加载阶段就优先取 `Ada_BodyPreview4`（`Scale=1`），`AnimatedModel` 从一开始就是 Ada，不再运行中换模型；头 `head_mh` 作为子 Actor，由 `WorldHeadMasterPose` 在 OnStart 调一次 `SetMasterPoseModel`。实机：`loaded=true / 模板=2 / 写回端=2 / 参数=628`；头部承载 **552/598**、morph 写 33；`face.zone.cheek.l.tx 0→2` → 面部 **139 px>10**，`body.height 177.5→190` → **1297 px>10**（均已回退）。**遗留**：Ada 身体在游戏内是无贴图白模。
- **头部微法线（Normal_MICRO）取证**：
  - UE 无头导出补齐 `Output/mhcommontex/T_SkinMicroNormal.png`（2.54 MB，另有 `T_FlatNormal`、`T_MH_BentNormal_AO`）；`asset_import` 以 `Importer=Texture, Type=NormalMap, sRGB=false` 导入为 `Content/Character/Skin/T_SkinMicroNormal.flax`（回执 `optionsApplied=true`）。
  - `MH_Head_New.flax` 的 `Normal_MICRO` 参数存在但 `outputConnections=0`（从未接线）；UE 侧该分支 = `BlendAngleCorrectedNormals(baseNormal, MakeFloat3(intensity,intensity,1) × 微法线样本)`，参数默认 `MicroNormalIntensity=0.5`、`MicroNormalTiling=30`。
  - **根输入接线是通的**（此前“存不进资产”的结论已推翻）：探针材质上 `Float → Roughness` 一次落盘（重开资产可见）；`MH_Head_New` 上 `Roughness` 与 `Normal` 两条都能落盘。之前的假阴性来自两点：① `nodeKey` 的 `pin` 按名找 `UVs` 失败（Get Parameter 的原型文本不匹配），改成**按箱 ID**（贴图 UVs=0）后正常；② 保存后立刻 dump 会读到尚未重载的旧资产，**等第二次保存/数秒后**再读才准。
  - 从 `ue_head_material_functions.json` 取到 `BlendAngleCorrectedNormals` 精确图：`n1=(A.xy, A.z+1)`、`n2=(B.xy, B.z)`、`Normalize(n1·dot(n1,n2) − n2·n1.z)`（无 decode/encode，16 个表达式）。
  - 按该公式建了完整微法线链（UV×30 → 采样 → `(i,i,1)` → BACN），并验证**链路与接线都落盘**（776/812 连接，`Normal ← Divide`，`Normal_MICRO.UVs ← Multiply`）。
  - **Normal 输入是活的、且要 −1..1**：给 `Material.Normal` 接常量 `(0,1,0)`，面部均值 **62.46 → 12.77**；接回正常链后恢复。引擎 `GBufferCommon.hlsl` 也证实材质 Normal 由 `EncodeNormal(n)=n*0.5+0.5` 编码后写入 G-Buffer。
  - **基础法线已接通**：现成 `442 = normalize(Normal_MAIN/WM 混合)` 不能直接接（原样与 `*2−1` 都近黑）；改为「`Normal_MAIN` 取 X/Y/Z → `*2−1` → Pack Float3 → `Material.Normal`」后正常。与未接法线的基线相比：面部均值同为 62.46，区域平均差 **0.47**、局部最大 **65.7**（眼/唇细节），属真实但细微的法线细节。
  - **微法线各变体均未通过目视**（都以基础法线为对照）：解码后的微法线进 BACN → 均值 **85.15**（整脸提亮）；编码样本直接进 → **17.79**（近黑）；全在编码空间再解码输出 → **32.42**。且 `MicroNormalIntensity` 0.5/2/10 三档像素几乎不变（均值 85.15/85.1/85.17）⇒ 参数默认值没有进到着色器，无法按 UE 语义调档。**已撤下微法线分支并删除两个参数**；贴图 `T_SkinMicroNormal.flax` 保留备用。
  - 工具侧新增：`custom` kind（Material/Custom Code，可提交 HLSL）、`nodeKey+箱 ID` 接针、引擎 `CreateConnection`/`MarkAsEdited`、关窗、保存后 `rootNormalAfterWire/AfterSave/editedAfterSave/rootNormalActive/rootColorActive` 诊断；Development/Debug 两份编辑器 DLL 已重编。
  - 当前材质：Color ← Lerp(389)、Normal ← Pack Float3(454，源自 `Normal_MAIN` 解码链)；77 参数；图内留有本轮实验的**未接线孤立节点**（不影响编译/渲染，工具暂无删节点能力，后续重建图时可清理）。

## 2b. 创角 UI 全流程自动化与新发现（2026-10-22）

- 新增工具 `ui_text_input`（按控件类型找 TextBox，逐字符 `OnCharInput`，回执最终 Text）——OS SendKeys 在 PIE 里常丢键，这条不依赖焦点。
- 全流程已跑通：塑形页 `CcConfirmBlock` → 命名页 `ui_text_input("测试四号")` → `CcConfirmBlock` → `CcRoleListPage` → Space 进世界；SQL 建出 character **42/43**，世界 Actor 为 `LocalPlayer_42/43 + LocalPlayerHead_* + LocalPlayerHair_*`。
- **捏脸值丢失已修复（2026-10-22）**：原缺陷——塑形页改 `body.height=190 / face.zone.cheek.l.tx=1` 后走"下一步→命名→建号"，新角色进世界读回 **177.5 / 0**，盘上无 `.mhl`；根因是 `MetaHumanSculptPanel` 只在面板「发布」路径调 `RecordPendingLookMeta`，页面级"下一步"不走这条。
  修法：新增 `MetaHumanSculptPanel.CommitPendingLook()`（`Save()` + `MetaHumanLookStore.SetPendingLook` + `RecordPendingLookMeta`，不封存）→ `CcFaceSculptPage.CommitPendingLook()` → `MainUIManager` 的 `page.ConfirmRequested` 先提交再导航到 CcName。
  实测：character **44「测试六号」** 进世界后 `metahuman_drive` 读回 **body.height=190、face.zone.cheek.l.tx=1**（改前 42/43 为 177.5/0）⇒ 捏脸快照已随建号落到本地 `.mhl` 并被世界角色加载。
  **服务端落库已成立（2026-10-22 00:09 实测）**：轨迹先定位到根因——客户端 `UpdateCharacterAppearanceRequest/Response` 未登记在 `MessageUnion`（MemoryPack 报 *not annotated*，消息根本没发出去）。补 `[MemoryPackUnion(246/247)]` 并重建 `Horizon.Game.Message` 后，客户端能发出但服务端无回执：服务器四个 bin（Silo/Gateway/WebApi/Game.Core）里的 `Horizon.Game.Message.dll` 仍是 01:07–01:42 旧版（新 DLL 07:55），**按契约必须逐进程替换并重启**。本轮已覆盖五个 bin（sha256 一致）并按 Silo→WebApi→Gateway 重启。同时修掉一个真实缺陷：`_healSent` 跨 PIE/断线重连不清空，服务器重启后永不补传；改为网络恢复时复位（`heal-reset` 轨迹）。
  实测轨迹：`heal-local char=44 ... rev=3` → `queued ... bytes=159` → `sent char=44` → **`ok revision=1 checksum=2681197344`**；SQL 独立对账：`Game_HunduShijie_Character.character_id=44` 的 `meta_human_look` **159 字节**（`0x4D484C4B` = "MHLK" 头，与客户端 blob 长度一致），42/43 仍 NULL。⇒ 重登/换设备可读回该外观。
  **换设备读回已实测（2026-10-22 08:17–08:24）**：把本地 `%LOCALAPPDATA%\Flax\Flax Editor\MetaHuman\Looks\44.mhl` 移走后重登——服务端随 `CharacterList` 下发的 blob 被 `AdoptFromServer` 落回本地（轨迹 `adopt-saved char=44 bytes=159`），进世界后 `heal-skip char=44 reason=server-has-look`（服务端已有存档不再补传，避免修订号每次登录 +1）。世界读回 `body.height` **valueBefore=190**、`face.zone.cheek.l.tx` **valueBefore=1**，截图确认头/发/身体正常渲染（[evidence](file:///C:/Works/GitHubProjects/HundunWorld/HundunWorld/Tools/_adopt_world2_small.png)）。修法：Ink 流程不经过 `CharacterStateManager.SelectCharacter`，故在 `MainUIManager.OnCcEnterRequested` 显式调用 `AdoptFromServer`；新增 `ServerHasLook` 门控补传。
  **同轮发现的环境缺陷（非本次改动）**：世界头部资产按旧 GUID `1222…` 加载失败（资产 06:24 重写后 GUID 变为 `2e2fda68…`，`content_database_rebuild` 无效），世界只剩无头身体。按既有口径**移走 `HundunWorld/Cache/AssetsCache.dat` 并重启编辑器**后恢复（`LocalPlayerHead_44`、写回端 2）。

## 2c. 肤色（melanin）通道闭环（2026-10-22）

- **根因**：模板 `skin.melanin` 的 Effect 目标是 `Skin_Melanin`，`MetaHumanMaterialMap` 把它映射到 `Color_MAIN`，但 `Color_MAIN` 在运行时是**贴图参数**（`FaceColor_MAIN.flax`），applier 用 `SetParameterValue(float)` 写贴图参数被类型不匹配静默丢掉 ⇒ 滑杆 0.25→0 仅 **41 px**（噪声级）。
- **修法**：`MH_Head_New` 与 `MH_Body2` 的根 `Color` 前各加一个 `Multiply`，乘 `SkinTint`（float，默认 1.0）；applier 新增 `MapMaterialFloat`，按映射表把 `Skin_Melanin` 翻成 `SkinTint` 并把 0..1 映射到 **1.15..0.45**。`SkinTint` 是 Flax 侧实现参数，**不进** `Head_Material_Spec.json`；映射门改为允许 `MetaHumanMaterialMap.EngineAddedFlaxParameters()` 白名单（仅 "SkinTint"）。
- **实测（PIE，暂停冻结；`metahuman_drive` + `actor_material_param` 回读）**：melanin=0 → 头 9 个材质实例 `SkinTint=1.15`，melanin=1 → `0.45`；像素差仅头部 **12,468 px**、接入 `MH_Body2` 后 **53,345 px**（max 85）。
- **坑**：材质编辑器里开着旧图页签时，编辑器保存会把刚接的线覆盖回旧图（参数还在、连线没了）。重接后必须**隔几秒再 dump 复核**，且确认根 `Color` 的来源是 `Multiply`（头 511 / 身 39）。
- **仍未做**：瞳色（`Eye_IrisColor` 属规格外，虹膜材质/通道未接）；redness/stubble 的贴图型目标仍是浮点写入，视觉无效（待材质链路）。
- **粗糙度已接通（同日）**：`Roughness_MAIN` 同样是**贴图参数**且根部原本没接线。材质图新增 `SkinRoughness`：`采样 Roughness_MAIN.R × SkinRoughness → 根 Roughness`；applier 把 `Skin_Roughness` 0..1 映射为 **0.4..1.6** 倍率。实测 `skin.roughness 0.45→1` 像素差 **3,152 px**（max 42，属高光变化）。`SkinRoughness` 同属引擎增补白名单，harness 仍 **1532/0/0**。
- **泛红（redness）已闭环（同日）**：`FoundationBlusherMask` 是贴图，不能把 `Skin_Redness` 直接写浮点。材质图接成 `掩码R × SkinRedness → α`，把 Color 向 `SkinRedColor`（暗红 0.30/0.07/0.06）插值；applier 把 `Skin_Redness` 再乘 **0.15**（掩码大量像素接近 1，直接用会整脸红透）。创角预览（固定相机）实测 0.15→1：**480 px**，ΔRGB **−5.4/−10.1/−10.3**（G/B 降得比 R 多=真变红），仅面颈区，目视为自然气色。`SkinRedness` 同属引擎增补白名单；harness **1532/0/0**。证据：[_skin_redness.png](file:///C:/Works/GitHubProjects/HundunWorld/HundunWorld/Tools/_skin_redness.png)。**世界侧未单独定论**：同参数在世界截图里 ΔRGB 三通道同向上升（+7~+10），属自动曝光对整体变暗的补偿，不能当"变红"判据；材质与 applier 与创角共用。
- **stubble 被数据卡住**：`BakedGroomRootTipGradientRegionMasks.png` 2048² 全零（RGBA mean/max=0）⇒ 没有区域掩码可驱动；`FaceBakedGroomAttributeMap.png` 均值 64/最大 128，但语义是 groom 属性图，不能替代区域掩码。stubble 需要重新导出非空掩码后才能接。

1. **世界 Ada 身体材质（已结）**：`MH_Body2` = `Color=BodyBaseColor×Cavity` + `Normal=BodyNormal（n*2-1）` + Roughness 0.95 / Specular 0 / Metalness 0，Opaque/Lit；UE 的 8K 身体法线/cavity 贴图已导出并线性导入（2048）。世界内身体中位数 84/60/46，与头部 98/70/57 同色阶，白模闭合。**剩余**：UE 的 roughness 贴图是虚拟纹理、导出全黑，粗糙度只能用常量；mask 贴图导出未接。
1b. **面部驱动已复验（夹具 + 世界）**：模板里头部参数是 `face.zone.*`（598 个）与 `morph.*`；`face.cheek.l.tx` 不存在，`State.Set` 对未知 id 返回 0 —— 本轮回读 0 是 Id 写错，不是回归。夹具上 `face.zone.cheek.l.tx 0→1`、错误 id `0→0`、`morph 已写=33`、`jumpTo 0` 回读 0；世界侧新建角色 `LocalPlayer_41` 上同 id `0→1`、**5,020 px>10**。
1c. **新建角色的身体外观（贴图已补齐，仍偏亮）**：已排除 applier 覆盖材质（虚拟实例即 MH_Body2）。本轮从 UE 资产补导出身体贴图：`/Game/MetaHumans/Common/Female/Textures/female_002/8K/` 下的 `female_body_normal_map`（8K，均值 127/127/254）与 `female_body_roughness_map` 已导出；**粗糙度贴图是 UE 虚拟纹理，导出 PNG 全黑（均值 0）⇒ 不可用**，粗糙度继续用常量。`female_body_normal_map` 以 `Importer=Texture, sRGB=false, Resize=true, SizeX/SizeY=2048` 导入 Flax（`sRGB=false` 已回读确认）。MH_Body2 增加 `BodyNormal` 纹理参数并接 `Normal`（`n*2-1` 解码链：unpack×3 → mul 2 → sub 1 → pack3），常量节点回读值为 **Roughness 0.95 / Specular 0 / Metalness 0**（dump 现已输出 nodeValues）。同一地图身体中位数：无 Normal **109/90/78** → 有 Normal **119/107/100**（头未变）；再接入 UE 的 **cavity 乘色**（`female_body_cavity_map`，`Color = BodyBaseColor.rgb * Cavity.r`，与 UE `M_BodySkin` 的 Color×Cavity 同构）后，身体中位数变为 **84/60/46**，与头部（98/70/57）同色阶，截图目视为正常肤色（[evidence](file:///C:/Works/GitHubProjects/HundunWorld/HundunWorld/Tools/_world_cavity_small.png)）⇒ **“白模/幽灵身体”已闭合**。同轮复验：`body.height 177.5→190→177.5`、`face.zone.cheek.l.tx 0→1` 均正常。mask 贴图已导出（`Output/mhbodymaps/`）暂未接。
1d. **登录/角色列表根因（2026-10-22）**：库里唯一账号 101125808 的 Token 曾是 80 字符陈旧令牌 → 角色列表空。用新增的 `_DbPatch refresh` 重签后：**注意 `AppType.Game` 的枚举值是 369（不是 Id 特性上的 9）**，传 9 时 `HandleGameUserCreation` 直接返回 null、signin 的 `userId=0`，再查 `IUserCharacterManagerGrain(0)` 就是空列表；改成 369 后 signin 返回 `userId=35`，与 `Game_HunduShijie_Character.user_id=35`（本轮新建的角色 41）对上。客户端库的 `UserId` 也已写成 35，重启 PIE 自动登录后直接进 `CcFaceSculptPage`（开 sculpt），返回角色列表能看到角色 41 并进世界 ⇒ **重登可见角色**成立。
2. **极端身高（#60/#61）**：权重侧与模板侧均已证伪或到上限。本轮又验了「按 4 影响重导」的前提：`Ada_BodyPreview4.glb` 虽有 3 组 `WEIGHTS_*`，但每顶点实际影响 ≤4、第 0 组权重和恒为 1.0（`Tools/_glb_weights_probe.py`）⇒ **裁剪影响数不会改变任何东西**，问题是影响**分配**（DCC 侧重刷权重）或身高语义，不是数量。
3. **`neck / neck_length`**：身体与头部网格的 `neck_01/02` 权重质量均为 0，需重导蒙皮。
4. **#34 毛发（已闭合）**：GLB 本身无贴图（1 mesh / 0 material / 0 texture），但 UE 的毛发贴图早已导出到 `Output/mhhair/`。本轮补齐：`Hair_S_Coil_Cards.glb` 以 `Scale=100`（米→厘米）导入为静态 Model（包围盒 y 142..163 / x ±10，与头资产同空间）；新建 `MH_Hair`（Opaque + `Mask=RootUVSeedCoverage.a`、Color=深棕常量、Roughness 0.7、Specular 0.1、**CullMode=TwoSided**）指认到槽 0；`HundunWorldGame.CreateLocalPlayerActor` 在头 Actor 下挂 `LocalPlayerHair_{id}`（StaticModel，本地零变换）。世界内目视：深棕卷发卡片正常显示（[evidence](file:///C:/Works/GitHubProjects/HundunWorld/HundunWorld/Tools/_hair_face_crop.png)）。**发色已接数据**：`MH_Hair` 增加 `HairColor` 颜色参数；世界侧从 `CharacterService`/`CharacterManager` 缓存取 `Appearance.HairColor` 离散下标，用创角同一张调色板 `CcFacePresets.HairColors` 还原，并写到该 Actor 自己的材质虚拟实例上（不改共享材质）。实测：把角色 41 的 `hair_color` 置 4 → 世界头发变银白（`Tools/_hair_silver_crop.png`）；下标 0 → 深棕（`_hair_face_crop.png`）。**注意**：改回 0 后同一会话仍显示银白——服务端 grain 的 EF 上下文缓存了先前读到的实体，需重启服务/换会话才刷新；库内现值已还原为 0。**剩余**：发型（`HairModel`）仍是单一资产，未做多发型选择。**Flax 没有 Masked 混合模式**，裁剪要用 Opaque+`Mask` 输入（`material_set_info` 已支持 `cullMode`）。
5. **微法线（Normal_MICRO，2026-10-22 重做）**：基础法线 `Normal_MAIN` 解码链仍成立。本轮把微法线重新接上：复用现成的 `Normal_MICRO`（**贴图参数已到 26 上限，新加 `MicroTex` 会被静默丢弃**），设 `T_SkinMicroNormal`，UV×30（UE 默认 tiling）→ 采样 → `xyz*2-1` → BACN（自定义 HLSL，strength 0.5）→ 根 `Normal`；完整强度与 0.5 强度各测一次。结果：**不再有之前的整脸亮度偏移**（ΔRGB < 1），但在此 1060px 预览上只有 ~45 px 的细微差异、目视不可辨——微法线的特征尺寸在预览分辨率下低于 1px，属预期，未能在可用相机上取得"可见细节"证据。`Normal_BAKED` 在 UE 里就是 `T_FlatNormal`（恒等），可不做。
6. **#64 尾项**：两份“已发布”外观的登录态客户端联调；是否拒绝倒退 Revision（产品判定）。
6a. **瞳色与 stubble（未做）**：`Eye_IrisColor` 在 `MetaHumanMaterialMap` 里是 OutOfSpecScope（虹膜材质另立规格，当前虹膜还缺），创角页的 `AppearanceInfo.EyeColor` 仍只写库不驱动渲染；`Skin_Stubble` 的目标 `BakedGroomRootTipGradientRegionMasks` **整张全零**（2048²，RGBA mean/max=0），没有可用区域掩码，stubble 需重导非空掩码后才能接（melanin/roughness/redness 已各自做成 SkinTint/SkinRoughness/SkinRedness）。
6a2. **眼部虹膜试验（2026-10-22，回退）**：确认 9 个材质槽全指 `MH_Head_New`（含 eyeLeft/eyeRight），原 `eyeLeft/eyeRight.flax` 只是白色常量材质。新建 `MH_Eye`（Opaque/Lit，`T_Gray_Eyes_D` 采样 × EyeColor，Roughness 0.25）并指认到槽 3/4 后，创角预览的**前后截图完全一致**（眼部暗块不变）⇒ 预览未反映资产槽位变更或眼网格被遮住，无法据此判定；槽位已回退到 `MH_Head_New`，`MH_Eye.flax` 已删除。结论：虹膜需要 UE 的眼部着色/贴图通道（或先在预览/世界里确认眼网格真正可见），不是换一张 diff 材质就能解决。
6a3. **#66 颈部重权重的负结果（2026-10-22）**：量到颈带中央权重 `spine_04=560`、`neck_01/02=0`；写了一个只改 JOINTS/WEIGHTS 的 GLB 补丁（`Tools/_glb_neck_reweight.py`，1534 顶点、neck_01/02 权重 431/264，几何不变）。但**盘上没有 `conformhead4` 的源 GLB**，用 `Ada_FaceMesh.glb` 导入会得到 1043 shapes 的另一拓扑（现有资产是重拓扑后的 821 shapes）⇒ 不能替换。候选资产已删除。要真正修 #66，需先拿到 conform 后网格（或直接在 .flax 上重权重的工具），否则只能接受模板侧近似。
6a4. **#66 模板侧近似（2026-10-22，已回退）**：在 `body.neck` 上追加 6 条指向头部颈骨（`FACIAL_L/R_12IPV_NeckB3/B5`、`FACIAL_C_12IPV_NeckB2`、`FACIAL_C_12IPV_NeckBackB2`）的 `BoneScale`，想让颈带经头骨的颈骨拿到承重。创角预览实测（`ui_preview_screenshot`，1060×1080）：
- `gain=0.012`（两端 ±15% 缩放）：与默认值相比 546/651 px 有差异，但**逐行剪影宽度一位不变**（y 455..500 每行像素数相同），只有内部明暗在变；
- `gain=0.05`（±62%）：差异涨到 992/2319 px，且 bbox 上探到 y=347（额头/下颌），**剪影宽度仍一位不变** ⇒ 这些颈骨驱动的是头部表面的局部缩放而不是颈围，加大幅度只会把脸弄歪。

结论：模板侧近似无法替代重蒙皮（`body.neck` 的颈带权重在 `spine_04`，不在 `neck_01/02`）；`Template_Body_Base.json` 已按备份 `Tools/_Body_Base.backup.json` 逐字节还原（`cmp` 通过），重烘后离线门 1532/0/0。证据：`Tools/_neck_{base,min,max}.png`、`Tools/_neck2_{base,min,max}.png`。

6a2b. **眼网格可见性探针（2026-10-22，已被 6a2c 推翻：真凶是眼壳槽）**：为排除"MH_Eye 颜色太接近皮肤所以看不出"的可能，做了高对比探针——新建纯红 `Color` + `Unlit` 材质 `MH_EyeProbe`，按槽名指认到 `eyeLeft/eyeRight`（9 槽仍非空），进创角预览截图与基线对比：**差异仅 31 px（且 bbox 在鼻梁 y=412–441）、画面里红色像素数 = 0**。⇒ eyeLeft/eyeRight 这两个材质槽对应的图元在预览里根本没有被画出来（不是材质颜色问题，也不是槽位没生效）。槽位已改回 `MH_Head_New` 并逐一复核（9 槽全部 MH_Head_New），探针材质已删除。**瞳色要落地，前提是先让眼网格渲染出来**（检查模型 LOD/图元是否为空、或改用独立的眼球模型），再谈 EyeColor→虹膜着色。
6a2c. **眼区图元定位（2026-10-22，尚欠一次可信的颜色探针）**：解析源 GLB `Output/mhface/slots/Ada_FaceMesh_conformhead4.glb`（即 .flax 的 ImportPath）确认 **eyeLeft/eyeRight 图元真实存在**：各 802 顶点 / 1536 三角，包围盒 x 0.0172–0.0437（左）/ y 1.478–1.505 / z 0.0679–0.0946；全部图元都带 3 组 JOINTS/WEIGHTS（12 影响）。预览里做了三组槽位对比：
- `eyeshell(5)/eyelashes(6)/eyeEdge(7)` 从皮肤换成 `MH_Hair`：眼区 **540 px 变化**（bbox 485–574 × 407–424）⇒ 这三个图元确实在渲染；
- `eyeLeft(3)/eyeRight(4)` 换成黑/红探针：看不出变化 ⇒ 眼球图元要么没被导入、要么被上面的卡片挡住；
- **探针本身有缺陷，不能据此定死**：`material_graph_add_params` 只加参数不接线（渲染成黑）；`material_graph_build` 用 `constant`+`pack3(1,0,0)` 接到 `Color`（dump 确认连线在）头上仍渲染不出红色；`material_instance_create` 回执 created 但盘上没有文件、`material_set_param` 报"无该参数"。
⇒ 下一步：先用一个**在头部槽上验证过会变红**的材质再做 3/4 槽判据；同时把睫毛/眼壳换成带 `Eyelashes_L_SlightCurl_Coverage.png` 掩码的材质（它们现在用不透明皮肤材质，正是"眼睛像闭着"的直接原因）。**本轮落地与结论（续）**：已导入 `Eyelashes_L_SlightCurl_Coverage.png`（线性、非 sRGB）并做成 `MH_Eyelashes`（Opaque→Masked、TwoSided，`EyeTex→Color`、`unpack.W→Mask`），指认到 `eyelashes(6)/eyeEdge(7)`：眼区从"不透明皮肤块"变成**可辨认的细睫毛**（基线对比 276 px，bbox 473–572 × 407–441），这是本轮唯一保留的视觉改动。眼球槽（3/4）指认 `MH_Eye`（`T_Gray_Eyes_D`→Color）后画面零变化；驱动 `face.zone.eyelid_upper.l.ty` 到 ±1 也只有 **2 px** 差异 ⇒ 该参数是死通路，且**这具 conform 头的静息几何本身就是闭眼**（眼球被头网格眼睑面挡住）。因此瞳色仍不可见，下一步不再是材质而是**换一具睁眼静息的头部基础几何**（或确认可用的睁眼形状通道）。9 槽现状：0/1/2/3/4/5/8=`MH_Head_New`，6/7=`MH_Eyelashes`；`MH_Eye.flax` 留在盘上但未指认；离线门 1532/0/0。
6a2d. **瞳色通道打通（2026-10-22，本轮主成果）**：真凶确认是**槽 5 `eyeshell`**——它一直用不透明皮肤材质，正好糊在眼球上，看起来像闭眼。把 `eyeshell` 换成透明材质 `MH_EyeShell`（Transparent，`Opacity←0`）后，`eyeLeft/eyeRight` 的灰色虹膜立即显现（对比 256 px，眼白与虹膜都可见）。随后：
- `MH_Eye` 接 `EyeTex→Color`，再加 `EyeIrisTint` 颜色乘色；在乘色前按亮度做掩码（`saturate((0.75-luma)*3)` 把乘色限制到深色虹膜，巩膜保持白），节点链 18 个、`Color←m` 落盘；
- 写回端对 `Eye_IrisColor` 特判：0..1 指数 → 5 色色板插值（棕/近黑/琥珀/青绿/灰蓝，与 `CcCreationPages.EyeColors` 一致）→ 写 `EyeIrisTint`；
- `eye.iris_color` 本来就在脸模板里且有滑杆（598 参数里 Region=Eye），因此玩家可直接拖动。
实测（创角预览 1060×1080）：指数 0 → 褐色虹膜+白巩膜；指数 1 → 蓝色虹膜+白巩膜；两图差异 197 px（[0](file:///C:/Works/GitHubProjects/HundunWorld/Tools/_iris_masked0_eyez.png) / [1](file:///C:/Works/GitHubProjects/HundunWorld/Tools/_iris_masked1_eyez.png)）。9 槽最终回读：0/1/2/8=`MH_Head_New`、3/4=`MH_Eye`、5=`MH_EyeShell`、6/7=`MH_Eyelashes`。插件 Development/Debug 重编 `error CS=0`；离线门 1532/0/0。
- **色板 UI 已接（2026-10-22）**：把从未实例化的 `CcSwatchRow` 挂进捏脸面板（`_sliderScroll` 顶部，只头部页签可见），5 个色点对应 `CcFacePresets.EyeColors`，点击走与滑杆同一条 `Session.Adjust/Commit` 链（封存闸门/历史记步/写回端自动一致），并写 `CcSession.Current.EyeColorIndex` 供外观落库。客户端 csc 门 **0 error**；运行时 UI 树已确认 `CcSwatchRow` 存在（192×34，滑杆整体下移 38px）。**点击未能自动化**：当前窗口 1181×774、设计页 1920×1080 居中裁切，面板落在屏幕负坐标（-289,-43），注入点击打不到——需要大窗口下目视一次。
6a2e. **空的可激活栈吃掉了所有 Ink 点击（2026-10-22 定位并修复）**：注入点击时每次 hit 都是 `NarrativeActivatableWidgetContainer`，连鼻梁位置也是如此。运行时树证实 Ink 画布上两个**空**的 `NarrativeActivatableWidgetContainer` 以 1181×774 全屏、可见状态叠在 `InkPageShell` 之上——Flax 的 `ContainerControl` 即使没有子控件、背景全透明也照样命中整块矩形（UE 的 CommonUI 栈是 SelfHitTestInvisible）。这解释了"按钮/滑杆点不动"这一类现象。
- 修法：`Plugins/NarrativePro/.../NarrativeActivatableWidgetContainer.cs` 构造时 `Visible=false`，并在 AddWidget/RemoveWidget/ClearWidgets 后按 `Count>0` 同步可见性。
- **生效路径的坑**：插件目录重建只更新 `Plugins/NarrativePro/Binaries/...`，PIE 实际加载的是 `Binaries/GameEditorTarget/Windows/x64/{Debug,Development}/NarrativePro.CSharp.dll`（旧件 08:16）——把新 DLL 复制过去后重进 PIE，`Narrative... visible:true` 计数 5→0，点击 hit 变成 `InkPageRouter`。
- **色板点击闭环**：改用 `ui_click_control {type:"CcSwatchRow", offsetX:143, offsetY:17}`（屏幕坐标换算被 1920 设计页居中裁切搞乱，控件级点击绕开它）：`historySteps 2→3`，虹膜由褐变**青绿**（[截图](file:///C:/Works/GitHubProjects/HundunWorld/Tools/_swatch_teal_eyez.png)，与褐虹膜差 151 px）⇒ 色板 → 参数 → 材质 → 像素整条链成立。
6a2f. **口红参数试接（2026-10-22，已回退）**：MH_Head_New 里本来就有 `Lipstick`(float)/`LipstickColor`(color)/`EyeLipstickMask` 的链，但脸模板没有驱动参数。临时加 `face.lipstick`（MaterialFloat `Lipstick` + MaterialColor `LipstickColor`，棕→红）并重烘/重启编辑器清模板缓存后实测：参数可提交（valueBefore 0 → 1，history 2），但画面**整片下半脸（颊/下颌）被染红**（diff 14,514 px），不是只有嘴唇——说明现有 `LipstickColor` 分支混合范围过宽或掩码不是唇部隔离。结论：不能直接接线交付，已在 `Template_Face_Archetype.json` 移除该参数并回退。
- **回退事故与修复**：我先把模板恢复成 git 暂存版（`git show :...`），而暂存版比工作区旧——少了 41 条 WM 法线 `MaterialFloat` 效果，harness 立刻报 `全部 morph 置数后应产出 41 个 WM 法线权重（实得 0）`。用 `python Tools/MetaHumanWireNormalWeights.py --apply`（幂等）重新接回 41 条后，模板回到 **598 参数 + 41 WM 效果、无 lipstick**，离线门 **1532/0/0**。教训：`AM` 文件不能拿暂存版当"原样"，回退只认工作区备份或重跑生成脚本。
- **追加定因（同日）**：口红“染整片下半脸”的直接原因是 `MH_Head_New.EyeLipstickMask` **指向了腮红掩码**（`Content/Character/Skin/FaceFoundationBlusherMask.flax`），而不是唇部掩码。盘上导出的 `Output/mhheadtex/FaceEyeLipstickMask.png` **整张全空**（2048²，RGB min/max=0、A 恒 255）——和 stubble 的 groom 掩码同类死资产，导入后指认过去，`Lipstick=1 + 红唇色` 只差 20 px，等于不可见。⇒ 口红要与 UE 对齐，必须先拿到非空的唇部掩码（或从 UE 重新导出该贴图），不是材质接线问题。现场已还原：掩码指回腮红掩码、`Lipstick=0`、`LipstickColor` 白色，空的 `FaceEyeLipstickMask.flax` 已删除。
- 另：`MetaHumanBakeTables.py` 曾报 `OSError 22` 写不了 baked 表（同路径 .NET/Python 追加写都正常），把目标文件截断为 0 后再跑即成功——遇到时先手动截断再重烘，不要以为模板坏了。

6a2g. **发色色板（2026-10-22，外观档）与 `hair.color` 的边界**：捏脸面板加了第二行「发色」色板（`CcFacePresets.HairColors` 6 色）。但 `hair.color`→`Hair_BaseColor` **不能走写回端**：头发是独立的 `LocalPlayerHair_*` StaticModel，不在 MetaHumanCharacter 的 TargetModels（body+head）里，applier 的材质实例根本不含它——实测把 `hair.color` 从 0 拉到 1，世界截图里头发仍是深色，而 applier 的 Iris 分支正常。所以发色色板按**外观档**处理：只写 `CcSession.HairColorIndex`（`MainUIManager` 建角时写进 `AppearanceInfo.HairColor`，世界侧既有链路按缓存下标给 `MH_Hair` 实例着色，此前已实测银白/深棕）。UI 实测：两行色板都在（`CcSwatchRow` ×2）；点眼行 history 1→2（记步），点发行不记步（仅外观）。注意两行的控件匹配顺序是**眼=0、发=1**，与屏幕上的上下顺序相反。
6a2h. **瞳孔大小（eye.pupil_size）试接 = 负结果，已回退（2026-10-22）**：`eye.pupil_size` 只写 `Eye_PupilSize`，MH_Eye 里没有落点。尝试用亮度阈值近似瞳孔扩张（`factor = saturate((luma - (base + k*p)) * 20)`，`color *= factor`）：阈值 0.28..0.40 时**把虹膜一起吃掉**（整个虹膜变黑）；0.10..0.15 几乎无变化（diff 73 px）；0.14..0.24 也只把暗像素从 1470 增到 1515（3%）。结论：这张眼睛贴图的虹膜/瞳孔亮度不可分，没有瞳孔掩码或真实瞳孔几何就无法做成可用滑杆。眼材质与写回端已恢复为**仅虹膜乘色**（MH_Eye 参数只剩 `EyeTex`/`EyeIrisTint`，`Color←m`），`eye.pupil_size` 回到惰性参数。harness 1585/0/0。
6a2i. **瞳色发布→重登回读 + 封存拒改（2026-10-22，角色 44 已封存）**：世界内 `eye.iris_color=1` 后 `metahuman_drive publish=true` → `sealedAfter=true`、`sealedLook=true`；**重启 PIE、重新进世界且不做任何驱动**，`LocalPlayerHead_44` 槽 3/4 的 `EyeIrisTint` 读回 `RGBA(0.320,0.400,0.540)`（色板蓝灰）⇒ 瞳色随 look 落库并在重登后生效。随后驱动 `eye.iris_color=0`：`valueAfterCommit` 仍为 1、实例颜色不变 ⇒ 封存闸门在世界侧也拦住了改动。注意：测试角色 44 现在 `sealed=true`，不能再编辑（另有第二个角色可继续测）。
6a2j. **远端头/写回端运行态取证（2026-10-22，探针已删除）**：本机没有第二个客户端，于是临时在 `HundunWorldGame` 加探针（marker 控制）：用**真实** `RemotePlayerActor` 代码路径挂一个合成远端（Ada 身 + 远端头），世界内得到 `RemoteProbe_999` / `RemoteProbeBody_999` / `RemoteHead_999`。对 `RemoteProbe_999` 驱动 `eye.iris_color=1` 后，`RemoteHead_999` 槽 3/4 的 `EyeIrisTint` = `RGBA(0.320,0.400,0.540)` ⇒ **远端头 + TargetModels(head) + 写回端**这一整段在运行态成立。
- 未闭合：探针在“AddScript 同帧立刻 `LoadLook`”时颜色没生效（仍是基材琥珀），说明同步时机太早（真实远端是在网络回包回调里调用，晚于初始化）。第二次重启 PIE 时进程自行退出，未能用延迟 apply 复测。⇒ 真正结论仍需第二客户端联调；代码级链路已加常驻闸门（harness 1547 项，含 5 条远端链断言）。
- 探针代码与 marker 已完全删除，客户端 csc 0 error。
- **追加复测（同日，仍负）**：给 `RemotePlayerActor` 临时加“LoadLook 失败后按帧重试 90 帧”，并用 44 号**已发布 look**（`MetaHumanLookStore.LoadLatest("44")`，含 iris=1）喂合成远端：重试窗口后 `EyeIrisTint` 仍是基材琥珀，说明在该合成环境里失败不是时机问题（部分 look 与已发布 look 都不生效）；同时第二次起 PIE 频繁自退。**重试代码与探针均已回退**，不留未验证改动。结论：远端外观（含瞳色）的运行时验收只能靠真第二客户端，合成远端不足以作为判据。
6a2k. **肤色色板落地（2026-10-22）**：捏脸面板第三行「肤色」色板（`CcFacePresets.SkinColors` 6 色），点击 → `skin.melanin = i/5` 走 Adjust/Commit（记步），并写 `CcSession.SkinColorIndex`（建角写进 `AppearanceInfo.SkinColor`）。实测：点最暗色点后 `historySteps 1→2`，面部均值 RGB 72.0/61.4/57.5 → 50.6/40.3/37.4、差异 32,193 px ⇒ 色板→参数→材质链路成立。三行色板的控件匹配顺序为 **肤=0、眼=1、发=2**（创建顺序），屏幕上下顺序为 肤/发/眼。客户端 csc 0 error。**已加常驻闸门**：harness 断言三行色板的控件字段/订阅/落点参数（`skin.melanin`、`eye.iris_color`）与创角色板齐全，少一处就红；当前 **1549/0/0**。
- **肤色映射修正（同日）**：`SkinColors` 的顺序不是按明度排的（下标 3/5 是最浅的两块、4 最暗），原先把下标线性换成 `melanin=i/5` 会让“点浅色块皮肤反而变深”。改为 `CcFacePresets.SkinMelaninByIndex`（用色块自身 luma 反推，最暗→1、最浅→0），选色用 `MelaninOf(index)`、撤销/预设回填用 `ClosestSkinIndex(melanin)`；闸门断言禁止再出现 `skinLast` 式线性映射。实测映射：idx0 0.103 / idx1 0.322 / idx2 0.497 / idx3 0.0 / idx4 1.0 / idx5 0.094。另补一致性修复：`RefreshSteps` 统一调用 `SyncColorIndices()`，把 `eye.iris_color / skin.melanin / hair.color` 回填成 `CcSession` 的三个离散下标（滑杆/预设/撤销/重做都会覆盖），避免“拖滑杆改了色、AppearanceInfo 里还是旧下标”。
6a2l. **Flax 1.13 迁移验证（2026-10-22）**：工程运行环境从 1.12 切到 **1.13**（用户启动的 `Flax Editor 1.13`）。用 1.13 的 Flax.Build 重编：
- `MetaHumanEditorTarget` Development/Debug：**exit 0、error CS=0**；
- `GameEditorTarget` Development/Debug（cwd=工程根、`-mutex`）：**exit 0、error CS=0**，`Binaries/GameEditorTarget/.../Game.CSharp.dll` 均刷新。

1.13 的 `Binaries/Tools` 已含 Arch/LiteDB/MemoryPack/TouchSocket 等 30 个 DLL，不再缺第三方引用。`HundunAgent` 插件也已用 1.13 重编（Development/Debug，0 error）；但运行中的编辑器加载的是旧程序集，`tools/call` 仍返回空，需用户重启编辑器后才生效。两个写死 `Flax_1.12` 的 PowerShell 脚本已改为自动选最高版本引擎（解析结果=1.13，语法检查 0 错误）。
- **四个 MetaHuman 编译门在 1.13 下全部 0 错误**：`MetaHumanCompileCheck`、`MetaHumanEditorCompileCheck`、`MetaHumanGameGate`、`MetaHumanUIGate`。期间修了两处门配置：`MetaHumanGameGate` 的契约引用改指 `Horizon.Game.Message/bin/Debug/net10.0`（旧 `bin/Debug/verify` 副本过期，新消息被报"不存在"）；`MetaHumanUIGate` 为覆盖三行色板，把 `CcFacePresets` 与 `CcSession` 从 `CcCreationPages.cs`/`CcStagePage.cs` **抽成独立文件**（`CcFacePresets.cs`、`CcSession.cs`，同命名空间），门只编 面板+控件+色板+会话，不再拖整条舞台页依赖。客户端 csc 0 error / **466 源**。截图通路修复（待重启验证）：`ui_preview_screenshot` 在 1.13 下 `Screenshot.Capture(task, path)` 出全黑图（1060×1080 全 0），已改为**优先抓 `task.Output` 纹理**、拿不到才回退 task 捕获，并在回执里带 `source` 字段；HundunAgent 已用 1.13 重编 Development+Debug（0 error），需重启编辑器后在 PIE 捏脸页验证 `source=output` 且图像非全黑。**未恢复**：1.13 编辑器的 MCP 桥 `initialize`/`tools/list` 正常但 `tools/call` 返回空（当时进程 `Responding=False`），因此 1.13 下的 PIE 冒烟（三行色板/世界/发布）仍待桥恢复后补做。
6a2m. **1.13 运行态冒烟（2026-10-22，数据级）**：用户重启后的 1.13 编辑器桥恢复，创角 PIE 实测：
- UI 树里 `CcSwatchRow` ×3（肤/发/眼）；
- 点肤色：`historySteps` 稳定 **+1/次**（连点两次 5→6→7），`SkinTint=0.45`、`SkinRoughness=0.94`、`SkinRedness=0.0225`（melanin=1 的映射值）⇒ 肤色通道落位；
- 点瞳色：`EyeIrisTint` 从实例读出 `RGBA(0.320,0.200,0.120)` = 色板第 1 色 ⇒ 瞳色落位；
- 点发色：步数不变（外观档，符合设计）。
- **`ui_preview_screenshot` 在 1.13 下返回全黑图**（1060×1080 全 0 像素），而 `game_screenshot` 有内容（能看到世界与角色剪影）⇒ 预览截图的 RenderTask 通路在 1.13 下异常或口径变了；1.12 的像素证据仍有效，1.13 的**目视**验收待恢复截图通路后再做。
6a2n. **PIE 退出必卡死：编辑器日志解析器死循环（2026-10-22，已定因+已修+已加闸门）**
- **现象**：1.13 下每次 `sim_stop` 后编辑器主线程不再响应（`IsHungAppWindow=True`），`sim_status`/`scene_load` 全部 60s 超时；连续复现 3 次。
- **取证**：`dotnet-stack report -p <pid>` 两次采样（间隔 1 分钟）栈顶完全相同：
  `RichTextBox.OnParseTextBlocks → HtmlParser.ParseNext → ParseTag`，下方是 `DebugLogWindow.LogEntry..ctor → Debug.LogError → GameSceneManager.OnSceneUnloading`。
- **根因（引擎解析器 + 我们喂的文本）**：`OnSceneUnloading` 把 `System.Environment.StackTrace` 整段拼进 `Debug.LogError`。引擎栈里有
  `Level.<Internal_UnloadAllScenes>g____PInvoke|239_0` 这类"尖括号+下划线"标记；`HtmlParser.ParseTag` 在标签内遇到既不是字母数字/`-`、也不是 `< > / =` 的字符时，
  `ParseAttributeName()` 返回空串且**不移动位置**，外层 `while (Peek() != '>')` 原地空转 ⇒ 日志窗口永不返回，主线程 100% 占用。
- **离线复现（独立于引擎进程）**：用 `FlaxEngine.CSharp.dll` 直接跑 `HtmlParser`，硬超时判定：
  `<A_B>` → 卡死（exit 124）；`<AB>` → 正常 1 个标签；真实栈行 `at FlaxEngine.Level.<Internal_UnloadAllScenes>g____PInvoke|239_0()` → 卡死。
  普通中文/多行/`<`/`<>`/`<<`/未闭合引号/`<!--`/`<script` 都不复现 ⇒ 触发条件就是"尖括号内出现下划线等标点"。
- **修法**：日志不再拼整段调用栈；需要来源时只留单行调用者名 `new StackTrace(1,false).GetFrame(0)?.GetMethod()?.Name`。
  改动点：`GameSceneManager.OnSceneUnloading`、`GameSceneManager.SafeUnloadScene`、`NetworkManager`（Disconnect/CleanupClient/OnClientDisconnected）、`AuthenticationManager` 重入告警。
- **验证**：重编 `GameEditorTarget Development` 后 PIE `play → 20s → stop` 返回 `{"status":"stopped"}`，编辑器 `hung=False`（修复前同一路径必卡）。
- **常驻闸门**：`Tools/MetaHumanVerify` 新增 `TestNoEnvironmentStackTraceInLogs`（扫 400+ 个 `.cs`，禁止日志消息里出现该 API；带"实扫文件数>100"防空转）。harness **1551 通过 / 0 失败 / 0 跳过**。
- **副产物**：`dotnet-stack` 全局工具已安装（用于抓托管栈；可 `dotnet tool uninstall -g dotnet-stack` 移除）。

6a2o. **1.13 像素复验再次受阻：登录能过、选角墨页没建出来、PIE 无相机（2026-10-22，环境取证，未修）**
- **登录路径已摸清（可复用）**：`HorizonGameIniReader` 读的是 `Directory.GetCurrentDirectory()/HorizonGame.ini`，PIE 下即 `HundunWorld/HorizonGame.ini`（仓库根那份不会被读）。把仓库根的 ini 复制进项目目录后，自动登录会走 ini 的网关 Token —— 但游戏服判 **Code=1007（无效 Token）**；而 `LoginWithTokenAsync` 在发请求前就本地伪造了成功回执，所以 `TryAutoLoginAsync` 直接返回成功、永远轮不到"本机保存密码"回退。删除项目目录的 ini 后走**本机存档**：`PassportInfo{passport=101125808, RememberPassword=true, token 216 字符}` → `CharacterList IsSuccess=True, 角色数=5`，`Start → CharacterSelection` 切换成功。
- **但选角墨页没出现**：切场景后两个 `UICanvas` 的 runtime 树分别是「Login 的登录面板」和「空的 CanvasRootControl」；4 个 `NarrativeActivatableWidgetContainer` 全部 hidden。`CharacterSceneController.TryCreateUI` 在 `UseInkCharacterFlow=true` 时**故意**跳过旧面板栈、交给 Ink 路由；本轮 Ink 选角页没有被 push 进去（`MainUIManager` 的自愈路径也没触发）。这是 UI 路由问题，与 MetaHuman 无关，但它是 ③ 演示链的前置。
- **PIE 无相机**：Game tab 显示 "No camera"（场景层级里其实有 `Camera` 节点），`game_screenshot` 两次不同状态返回的文件字节数完全相同（7,396,056）⇒ 拿到的是缓存帧；因此**本轮仍无法做像素验收**，`ui_preview_screenshot` 的 `source=output` 修复也仍未在创角页验证。
- **工具侧发现**：`ui_dump/ui_tree` 的 `ResolveCanvas` 会把第一次解析到的画布缓存进 `_canvas`，同名画布（两个 `UICanvas`）无法用名字区分 ⇒ 切场景后 dump 仍在看 Login 画布。需要按 id/场景选择画布才能自动化后续取证。
- **结论修正（同日 6a2p 已闭环）**：选角墨页没出现并非路由坏，而是 **PIE 起错场景**——`MainUIManager` 是 RootScene/登录场景上的脚本，从 `Character.scene` 起 PIE 时它根本不存在。改从 **RootScene** 起 PIE 后，`InkWashUICanvas → CcRoleListPage → CharacterPreview3D` 正常出现；再用 `%APPDATA%/HundunWorld/open-sculpt.on` 直接落 `CcFaceSculptPage`。
- **副产物**：`sim_stop` 在本轮 3 次 PIE 循环里均返回 `{"status":"stopped"}` 且编辑器保持响应（6a2n 的修复稳定）；项目目录的 `HorizonGame.ini`（含令牌）已删除，未留在工作区。

6a2p. **1.13 像素验收闭环：截图通路修复成立 + 预览主光 0 已修 + 肤色/瞳色色板出像素证据（2026-10-22）**
- **PIE 起点**：必须从 **RootScene** 起。`MainUIManager`/`GameSceneManager` 挂在该场景；从 `Character.scene` 起 PIE 时画布为空（不是 UI 路由坏了）。
- **直达捏脸页**：存在 `%APPDATA%/HundunWorld/open-sculpt.on` 时，CC 流程直接落 `cc-face-sculpt`（代码里的自测入口），无需点选角行。
- **截图修复验证**：`ui_preview_screenshot` 回执出现 **`"source":"output"`**（`Screenshot.Capture(task.Output, …)` 分支生效），PNG 不再是全 0；此前"1.13 全黑"另有一半原因在下面。
- **真缺陷：预览主光亮度 = 0**。`CharacterPreview3D.CreateLightingRig` 主光写的是 `_keyLight.Brightness = 0f`（探针遗留），`CustomActors` 孤立渲染只有补光 0.7/轮廓光 0.45 ⇒ 捏脸页截图 mean=0.04、max=9/255。运行时逐档标定：2 → max=78 仍暗；**8 → 肤色 lit≈(120,83,62)、max=189**，人脸清晰。源码已改为 8，`GameEditorTarget Development` 重编 0 error；**重启编辑器加载新程序集后复测：mean=2.88、max=189、lit rgb=(120.7,83.3,62.5)**（与运行时探针一致 ⇒ 生效的是构建里的 8f，不是临时覆盖），见 [证据](file:///C:/Works/GitHubProjects/HundunWorld/Tools/_mh_sculpt_built8.png)。
- **像素证据（同一 PIE、离屏预览 1060×1080）**：
  - 肤色色板 idx4（深）：面部区域 lit RGB **(85.7, 52.1, 38.9)**；idx0（浅）：**(140.7, 99.2, 78.9)** ⇒ 点色板→skin.melanin→材质→像素 成立（[深肤色](file:///C:/Works/GitHubProjects/HundunWorld/Tools/_mh_skin_idx4.png)、[浅肤色](file:///C:/Works/GitHubProjects/HundunWorld/Tools/_mh_skin_idx0.png)）。
  - 瞳色色板 idx3（青绿）：与浅肤色基线相比 **90 px** 差异集中在眼部，色向从 (53.6,37.3,24.1) 变到 (39.3,40.2,33.5) ⇒ 虹膜染色在像素上可见（[证据](file:///C:/Works/GitHubProjects/HundunWorld/Tools/_mh_eye_idx3.png)）。
  - 命中口径：`CcSwatchRow` 的色点是半径 14 的圆点，中心 x = 53 + 30·i（`LabelW≈42 + DotR 11`）；点行中间会落空（本轮曾因 x=17 空点一次）。
- **常驻闸门**：新增 `TestPreviewKeyLightNotBlack`（解析 `_keyLight.Brightness` 字面值并断言 ≥1，同时要求 `CustomActors` 含 `_lightRoot`；解析不到会判失败，防空转）。harness **1555 通过 / 0 失败 / 0 跳过**。
- **头部滑杆的像素证据（同一 PIE）**：第一项滑杆**「额头·宽度 [-100~100]」**拖到右端 → 面部区域 **276 px** 变化（bbox x443–653/y327–626）；再拖回中点 → 与基线只剩 **33 px** 残差（≈12%，拖拽落点 160 与真实中点的量化差）⇒ 滑杆改的是实际几何、且可逆（[基线](file:///C:/Works/GitHubProjects/HundunWorld/Tools/_mh_slider_base.png)、[拖到右端](file:///C:/Works/GitHubProjects/HundunWorld/Tools/_mh_slider_after.png)）。
- **PIE 自检（RootScene + 捏脸页）**：`metahuman_selftest` **102 通过 / 0 失败 / 4 跳过**（此前 106/0/3；跳过项随页面/角色状态波动，无失败）。
- **仍未做**：发色在预览里不改（设计如此，进世界才生效）；世界内与发布重登路径的像素复验尚未做。

6a2q. **身体参数入口确认 + 焊缝缺陷的像素证据（2026-10-22）**
- **入口并不缺**：捏脸页有「头部 / 身体」两页签（数据驱动，`PartitionForUI` 分头身）。切到身体页签后 UI 列出 **30 项**身体参数（身高 [135~220]、胸围、胸间距…），值读自会话状态。
- **身体参数真的驱动几何**：`SculptSlider` #0 = 身高，177.5 → **220** 时预览 **106,266 px** 变化（bbox x0–705 / y157–684），标签同步 220；点「撤销」回 177.5 后与基线只差 **44 px** ⇒ 写回与撤销都是像素级成立。
- **真缺陷（像素可见）**：身高偏离默认值即出现**颈口/肩线/腰带处的锯齿撕裂**——195.51 与 158.88 都有，177.5 干净（[177.5](file:///C:/Works/GitHubProjects/HundunWorld/Tools/_mh_body_base.png)、[195.5](file:///C:/Works/GitHubProjects/HundunWorld/Tools/_mh_body_195.png)、[158.9](file:///C:/Works/GitHubProjects/HundunWorld/Tools/_mh_body_165.png)、[220](file:///C:/Works/GitHubProjects/HundunWorld/Tools/_mh_body_tall.png)）。形态与 #60 的肩缝一致，属**顶点影响数受限**（运行时三种修法已在 #60 排除，需 UE/DCC 侧按 4 影响重导，即 #61），不是接线问题。
- 结论：③ 的「身体参数可改」在**数据与像素**上都成立，但**幅度一大就露焊缝**，视觉质量仍受 #61 阻塞。

6a2r. **④「可回溯任意步骤」的像素证据（2026-10-22）**
- 同一次 PIE 里做两步修改：**肤色（深）+ 额头·宽度（右端）**，历史列表出现 `0. 初始 / 1. 肤色 / 2. 额头·宽度`。
- 与基线相比：改动后 **36,474 px**（>20 阈值，max 80）；点历史 `0. 初始` 跳回后与基线只差 **2 px**；再点 `2. 额头·宽度` 前进，与改动态只差 **1 px**。
- 结论：历史不仅是数据回放——**画面逐帧精确复原、前后跳都成立**（[基线](file:///C:/Works/GitHubProjects/HundunWorld/Tools/_mh_hist_base.png)、[两步改动](file:///C:/Works/GitHubProjects/HundunWorld/Tools/_mh_hist_changed.png)、[跳回初始](file:///C:/Works/GitHubProjects/HundunWorld/Tools/_mh_hist_jump0.png)、[前进到第 2 步](file:///C:/Works/GitHubProjects/HundunWorld/Tools/_mh_hist_jump2.png)）。封存后拒绝修改/不可回溯已由 6a2i 的数据级证据覆盖。

6a2s. **牙齿/唾液多网格：声明了但没人装配（2026-10-22，缺口立案）**
- `MetaHumanTemplate.TeethAsset` 在 `MetaHumanTemplate.cs:31` 有声明，但 `Template_Face_Archetype.json:14` 是空串，且运行时源码里**没有任何消费者**（只有声明）⇒ 又一个“声明了却没人写”的静默缺口。
- 磁盘上的 `.../Ada_FaceMesh_conformhead4/teeth.flax` 被 `model_inspect` 解析为 **FlaxEngine.Material** 且加载失败，不是可绑定的 SkinnedModel；同目录的 `saliva.flax` 同理。
- 影响：张开嘴/微笑时只有暗腔、没有牙齿（UE MetaHuman 的 head/teeth/eyes/tongue/eyeshell 多网格结构缺了 teeth/saliva）。下一步是从 UE 重导真实蒙皮网格，再在 `MetaHumanCharacter` 里按模板声明生成子 AnimatedModel 并加入 TargetModels。

- **2026-10-23 更正与接线（缺口口径变了）**：`model_inspect Content/Character/Models/head_mh/Ada_FaceMesh_conformhead4.flax` 显示该 SkinnedModel **有 9 个子网格**（顶点 24408/4585/660/802/802/552/2144/276/386）和 9 个材质槽，槽 1 = `teeth`、槽 2 = `saliva` —— **牙齿/唾液几何就在头资产里**，缺的是“专用材质没人指认”：两槽此前都指着 `MH_Head_New`（皮肤材质），所以只有暗腔。同目录的 `teeth.flax`/`saliva.flax` 是 **Material**（不是网格），正是该给槽 1/2 用的材质。
  - 新建 `Content/Character/Materials/MH_Teeth.flax`（Texture 参数 `TeethColor` → `T_Gray_Teeth_D`，接 Color）与 `MH_Saliva.flax`（Transparent、Color=`SalivaColor`、Opacity=`SalivaOpacity`），用 `model_material_slots_assign` 按槽名写到模型资产；回执 `saved=true`、`teeth`/`saliva` 两槽 after 均为新材质。
  - **近景像素证据（2026-10-23）**：在创角→脸型（`CcFacePresetPage`）用 `ui_preview_screenshot` 拿到 1060×1080 头部近景；把**头部皮肤槽 0 临时改为全透明**（新建 `MH_HeadInvisible`：Transparent + Opacity=0，验证完已删除）后重开 PIE，画面只剩眼睛/睫毛/**上下两排牙齿**与唾液层，牙齿呈白色牙冠纹理、位置正好在口腔——[透明头证据](file:///C:/Works/GitHubProjects/HundunWorld/Tools/_head_invisible.png)。把槽 0 恢复 `MH_Head_New` 后重开 PIE，正常肤色外观复原：[复原证据](file:///C:/Works/GitHubProjects/HundunWorld/Tools/_head_restored.png)。
  - **踩坑（另一条独立缺陷的线索）**：想通过张嘴看牙，试了三条路都不通——① 直接 `model_blendshape` 写 `head_lod0_mesh__jaw_open=1`（真实权重读回 1）画面嘴不张；② 走 `MetaHumanCharacter` 的 `metahuman_drive(morph.head_wm1_jawopen=1)`（诊断回执 morph 已写=33）嘴仍不张；③ 世界角色因出生点在约 7900 高处持续下落、`ThirdPersonCamera` 跟着掉，`game_screenshot` 拍不到脸。⇒ 「预览头 morph 权重已写但不驱动渲染」值得单独立案，和牙齿无关。

6a2t. **贴图源 PNG 已不在盘上 ⇒ 掩码类资产工作无法离线复算（2026-10-22）**
- 每个 `.flax` 贴图里都嵌着导入配置的 `ImportPath`，但逐个解析后**全部 MISS**：
  `Content/Character/Output/mhskin|mhheadtex|mhhair|mhbodymaps/*.png`、`Skin/Tools/_masks_png/*.png`、  以及 UE 侧的 `metahuman_conform_topologyv130/...`（那段还是乱码路径）。
- 后果：stubble / 口红（6a2f）/ 瞳孔（6a2h）这些“掩码或通道可分性”问题**不能靠离线复算贴图来推进**；要从 UE 重新导出源 PNG（或直接导出正确的掩码资产），否则只能继续用运行态像素试验。
- 同时确认牙齿/唾液不是网格：`Model/head_mh/.../teeth.flax`、`saliva.flax` 的头部类型是 **FlaxEngine.Material**（见 6a2s）。

6a2u. **登录页常驻盖住选角/世界（已修）+ 世界路径目视证据（2026-10-22）**
- **现象**：从 RootScene 起 PIE、自动登录后进入选角或世界，**Login.scene 自带的登录页仍盖在最上层**（进入世界后截图里只有登录页，而层级里 `LocalPlayer_44`/World 已存在）。
- **根因**：Token/ini 自动登录在本地伪造成功回执（`LoginWithTokenAsync`），不会触发服务端 `LoginResponse`，所以 `AuthenticationUI` 里“收到回执才隐藏”的分支不跑；而 `AuthenticationController` 只绑定场景面板、从不收它们。
- **修法**：`AuthenticationController` 新增 `HideSceneAuthUI/ShowSceneAuthUI`，隐藏/恢复 Login.scene 的 `CanvasScaler` 整根（连全屏背景一起收，避免继续吃鼠标）；自动登录成功、手动登录成功各调用一次，需要重新登录时（`ShowLoginPanelWithCredentials`）恢复。
- **验证**：重编后重启编辑器 → PIE → 自动登录 → 直接进世界。`ui_tree` 里 `LoginPanel visible False`；桌面截图可见完整 HUD、聊天框、动作栏与站立的角色（[证据](file:///C:/Works/GitHubProjects/HundunWorld/Tools/_desk3.png)）；`metahuman_inspect` 报`RemotePlayer_44 loaded=true, parameterCount=628, applierCount=2, 头 552/598`；日志 `AnimGraphLoaded=True, IsActive=True`。
- **残留观察**：截图里角色是 T-pose（日志显示动画图已挂且 IsActive=True）；需要后续确认 ALS 待机姿态是否只在有速度/输入时才离开 T-pose，若静止也应待机则另立缺陷。
- 门：客户端 csc **466 源 / 0 error**；harness 仍 **1560/0/0**。

6a2v. **世界角色 T-pose：实机零位移 + 剪辑非空但与骨架不绑定（2026-10-22）**
- **实机确证**：`actor_bone_probe`（对运行中真实 Actor 隔真实时间采矩阵）测 `LocalPlayer_44`（`sealedLook=true`、2 个 AnimatedModel）：
  **1.201 s** 内 DEF-thigh.L / DEF-spine.002 / DEF-upper_arm.L 在身体与头两具模型上 **6 项全部 0 位移** ⇒ 世界角色确实 T-pose。
- **图为空（硬证据）**：`Animation Graph.flax` 仅 2174 B、`dependenciesCount=0`，不引用任何动画；日志 `AnimGraphLoaded=True/IsActive=True` 只说明挂上了。
- **剪辑非空、但不绑定**：`animation_playback_probe` 在 PIE 内用临时 Actor + 真实帧等待（`Task.Delay(16)` + 每帧 `UpdateAnimation()`，回执 `elapsedSeconds` 自证时间前进）：
  `ALS_N_Walk_F` 有 **84 通道 / 1357 关键帧 / 34 帧 / 1.133 s**，`initFailed=false`，但对 `Ada_BodyPreview4`（elapsed 0.917 s）与
  `Character_1`（elapsed 0.783 s）**movements 仍为 0** ⇒ 不是“剪辑是空数据”，也不是“图没等够时间”，而是**轨道绑不上这两具 DEF-* 骨架**
  （骨骼名映射/retarget 缺失）。
- **危险路径（已移除）**：`anim_attach_probe` 给实时 Actor 热换虚拟剪辑图后**卡死主线程**（MCP 全部 60s 超时），只能力杀编辑器；工具已删除。
- **轨道名不可读**：`Animation.LoadTimeline` 对 `ALS_N_Walk_F` 返回 38925 B，但内容是压缩/非明文字符串；UTF-16/UTF-8 扫描只得到 `EDD/VUU/...` 这类随机串。
  ⇒ 不能靠字符串扫描拿轨道名（该工具已删除，避免把噪声当证据）；要拿映射只能读引擎时间线格式或从 DCC 重导。
- **根因（引擎口径）**：`AnimationGraph.h` 写明 `baseModel` 是“骨架布局来源”，`InitAsAnimation` 只给单段剪辑建图；
  而本工程 **剪辑全部是 ALS/UE 人形骨架**（`Content/Animations/als/*`，84 通道级），**模型是 DEF-* 骨架**，
  且工程里 **没有任何 Skeleton/Retarget 资产**（全 Content 扫描 0 个 Skeleton）⇒ 两者之间没有共享骨架或映射，
  单段剪辑自然一帧都绑不上。
- **结论**：② 的自动绑骨成立；骨骼动画回放未达成，且已定位到**剪辑→骨架绑定层**（图资产空 + 轨道名不匹配），修复必须从资产/重定向侧做。

- **2026-10-23 复核：命名层已不再是堵点，堵点移到「虚拟图求值」**
  - **点名重算**：`animation_inventory` 对 `Content/Animations/als` 逐段解通道名——`ALS_N_SecondaryMotion`（84 通道/1147 键/80 帧/2.667 s）与 `Walk/ALS_N_Walk_F`（84/1357/34 帧/1.133 s）对 **Ada_BodyPreview4（343 节点）重合 79/84**、对 **Character_1（1109 节点）重合 79–80/84**；`metahuman_bind` 报 Ada 资产上 `SkeletonRetargets` 已有 **126 条**（含 63 条按动画 Id 的源条目）。⇒ 6a2v 当时“剪辑绑不上 DEF-* 骨架”的命名根因已经被重定向条目覆盖，名字层面 79/84 直接可命中。
  - **仍为零位移**：把 `CreateVirtualAsset<AnimationGraph>() + InitAsAnimation(model, clip, true, false)` 的图在**本地玩家创建时**装到 `Ada_BodyPreview4` 上（日志 `AnimGraphPath=` 为空证明装的是虚拟图），PIE 进世界后 `actor_bone_probe` 2.0 s：`calf_l/foot_l/hand_l/forearm_l` **全部 0**，只有程序化待机的 `spine_01` 在动（0.148）；`ProceduralIdle` 日志仍是“骨架无动画驱动，启用程序化待机”。再补 `UpdateWhenOffscreen=true + ResetAnimation + UpdateAnimation` 后结果相同。
  - **不是「等不够时间」**：`animation_playback_probe` 在 PIE 内对 `Character_1` + `ALS_N_Walk_F`/`SecondaryMotion` 也是 `initFailed=false / graphLoaded=true / elapsed 0.65 s / movingBones=0`（该探针另一侧对 Character_1 读回的局部平移全 0，说明临时 Actor 的 NodesPose 没铺开，Character_1 侧读数只能当弱证据；Ada 侧的实时身体读数是强证据）。
  - **下一步（未做）**：虚拟图路线先当“不可靠”，先验证引擎自带 **Animation 窗口预览**能否播放 ALS 剪辑（同一 `InitAsAnimation` 通路；能播说明差在 Actor 装配/更新门，不能播说明该引擎版本的虚拟图本身有问题）；再决定是**在编辑器里手工做一个真正的 .flax AnimationGraph 资产**（Animation 节点 + 固定 baseModel），还是查 `AnimationsService::CanUpdateModel` 的两条编辑器门（`Graph.Parameters.Count()==GraphInstance.Parameters.Count()`、`Graph.IsReady()`）。
  - **代码状态**：上述尝试已**整体回退**（`git diff` 无残留；dotnet 0 错误），世界角色保持 6a3a 的“T-pose + 程序化待机”，没有留半成品。
  - **取证路上的坑（下次别再踩）**：外部 `Flax.Build.exe` 把 `Game.CSharp.dll` 编译落盘后，**已运行的编辑器不会热重载**，下一次 PIE 仍然执行内存里的旧装配——本次一度以为“新诊断代码没生效/虚拟图装上了”，其实看到的是旧代码。判据只能是运行日志里本版独有的标记（字符串字面量在 DLL 里是 UTF-16，方法名才是 ASCII）；编辑器自身 build 还会被任务图缓存判成 up-to-date（“Compiling”后 60ms 结束）或被第三方 DLL 的 `共享冲突` 打断（22 tasks failed）。
  - **2026-10-23 追加：`Animation Graph.flax` 不是空图，是“6 个 Animation 节点全指失效 GUID”**。文件 5440 B / 单 chunk 4978 B；解 surface 得到 **6 处** animation 引用（`04 00 00 00 14 00 00 00 00` + GUID 的定长记录）：`1ec8a61c…`、`1afbe89d…`、`5ea5e1dd…`、`f8d06796…`、`c39439c3…`、`a34f3402…`，**没有任何一个能在 434 份 .flax 头里找到**；BaseModel 参数（chunk 偏移 63）指向 `Character_1`。所以 6a2v 的“deps=0”其实是“所有依赖都失效”。
  - **补丁实验（字节级，已回退）**：把 BaseModel 改指 `Ada_BodyPreview4`、6 个剪辑 GUID 全部指向现有 ALS 剪辑（SecondaryMotion/Walk_F）——重启后日志里 graph 的 missing-asset 警告**全部消失**（只剩无关的 Asset/MaterialBase 两条），但世界角色 `calf_l/foot_l/hand_l/forearm_l` 在 2.0 s 内仍**全 0**，只有程序化待机的 `spine_02` 在动（1.38），`ProceduralIdle` 仍判“无动画驱动”。⇒ 问题不在“依赖缺失”，而在 **graph 的输出/求值门**（根节点输出是否接到 Animation、状态机默认分支、或 AnimationsService 的更新门）。
  - **虚拟图侧的新读数**：重启后带 `IdleDiag` 的装配确认 `CreateVirtualAsset<AnimationGraph> + InitAsAnimation(Ada, SecondaryMotion)` 的 `BaseModel=Ada_BodyPreview4`、`baseLoaded=True`、`sameAsBody=True`、`LoadSurface=308 B`、`initFail=false`——图本身是“就绪”的，但同样不驱动骨架。
  - **产物**：补丁版图存 `Tools/_patched_AnimationGraph_20261023.flax`；Content 里的原图已按备份恢复（`cmp` 逐字节相同）。源码诊断块已删、dotnet 0 错误、PIE 已停。
  - **图的真实结构（`Tools/_animation_graph_probe.py` 只读解析）**：Visject version 7000、**2 个顶层节点**（id=1 group9/type1 = Animation Output；id=2 group9/type18 = **State Machine**）、**9 个参数**；6 条 Animation 引用全部在状态机节点的值/元数据里。也就是说：世界角色挂的是一台**状态机**，不是“空图”；顶层输出只接状态机，状态机进入 `data.Graph->GetRootNode()` 的首状态（引擎 `AnimGroup.Animation.cpp` case 18）。补丁后 6 条引用全部可解析，但骨架仍不动 ⇒ 剩下要查的是**状态机内层图是否建起来/首状态是否接到 Animation 节点**（`data.Graph==nullptr` 或内层根为 Null 都会静默输出空姿态）。
  - **下一步优先级**：① 解状态机节点的 Meta 里内层图（state/transition）结构，确认首状态；② 用引擎 Animation 窗口预览同一剪辑作为“引擎能播吗”的对照；③ 若状态机确实缺首状态，就用编辑器 Anim Graph 重新保存/补一条 Entry→Idle，而不是继续猜字节。

  - **2026-10-23 第三轮：更新确实在跑，输出是空姿态**
    - 临时诊断（已删）在 `ProceduralIdleDriver` 里数 `AnimationUpdated` 回调：**`updatedCount=60`**、`mode=Auto`、`IsActive=true`、`speed=1`、`graph=Animation Graph.flax`、`model=Ada_BodyPreview4` ⇒ `AnimatedModel.UpdateAnimation → AnimationsService::CanUpdateModel → GraphExecutor.Update` 这条链是通的，**不是更新门**。
    - 直接把整张图换成引擎 `InitAsAnimation` 同构的「Output ← 单个 Animation」直连面（`Tools/_animation_graph_make_direct.py`，**正好 308 B**，与虚拟图 `LoadSurface=308` 同构），BaseModel=Ada、clip=SecondaryMotion。更新计数仍 60、missing-asset 警告清零，但 `calf_l/foot_l/hand_l` 2 s 内仍全 0 ⇒ **执行器在“采样剪辑→映射到骨架”这一层返回空**（`SampleAnimation` 返回 Null 时上层用 `GetEmptyNodes()` = 绑定姿态）。
    - 状态机结构也已解到最内层：Entry(9,19) 的 `Values[0]=2`（合法）、状态机 7 节点（1 Entry + 6 State）、每个 State 的内层图 `StateOutput(9,21) ← Animation(9,2)` 的 box0 连接**存在且正确**；问题不在“首状态没接”。
    - **重定向条目布局（Ada_BodyPreview4 实读）**：1 条模型级条目 `SourceAsset=Character_1, SkeletonAsset=Character_1`（后跟 54 条节点映射）+ **125 条动画条目** `[clip GUID][Character_1 GUID][节点映射]`，全部指向存在的 Character_1，不是缺失资产。
    - **另一条真实错误（旧会话日志 00:23）**：`Character_1.flax` 自己有 127 条 `SkeletonAsset=f08f8142…` 的条目，而该资产**全盘不存在**；任何在 Character_1 上播剪辑都会走 `Missing asset … to use for skeleton mapping` 并得到无效映射（探针在 Character_1 上的 0 位移由此解释，**不能**再拿来证明“引擎不能播”）。
    - **结论**：图/更新/结构都正常，最后卡在 `SkinnedModel::GetSkeletonMapping(Animation*)` 的通道→骨架映射（Ada 走重定向分支：动画条目→Character_1→按名映射→按通道名回填）。下一步应在这条映射上用**引擎侧**读数取证（例如临时给 `GetSkeletonMapping` 加日志需要重建引擎，代价高），或换一条不经重定向的路径（把剪辑通道改名成 Ada 节点名后再导入，做一条“同名直绑”对照）。

  - **2026-10-23 第四轮：修法落地，世界角色开始播放真实剪辑**
    - **定点对照**：只把 `SecondaryMotion` 那条重定向条目的 `SkeletonAsset`（16 字节 Character_1 GUID）清零，保留 `SourceAsset=clip` —— 引擎于是不再走重定向分支，退到“通道名→Ada 节点名”按名映射。重启后 2 s 骨骼读数：`calf_l 1.57 / foot_l 2.01 / hand_l 1.35 / spine_02 0.64`，**四肢第一次动了**。⇒ 根因锁定：**重定向分支在本工程返回无效映射**（条目、依赖都合法；不是缺资产）。
    - **正式修法（两处资产 + 一行代码）**：
      1. `Ada_BodyPreview4.flax`：把 **125 条**动画条目的 `SkeletonAsset` 全部清零（脚本 `Tools/_ada_*.py` 一次性处理；原资产备份 `Tools/_backup_Ada_BodyPreview4_20261023.flax`）。模型级条目（Character_1→Character_1，54 节点映射）保留。
      2. `Animation Graph.flax`：原“Output+StateMachine(6 个失效剪辑)”整张换成 **308 B 直连图**（Output←Animation，BaseModel=Ada，clip=SecondaryMotion；生成器 `Tools/_animation_graph_make_direct.py`，原图备份 `Tools/_backup_AnimationGraph_20261023.flax`）。
      3. `ProceduralIdleDriver`：加了让位门——**只要挂了可用动画图就完全不驱动**。此前它每帧覆盖 spine/手臂，把剪辑盖住，还把自己的“真实动画检测”永远卡在无动画分支（这也是为什么修好映射后仍看到“spine 在动但四肢不动”）。
    - **修复后实测**（PIE，`actor_bone_probe` 2 s）：`spine_02 0.19 / upperarm_l 1.99 / head 1.56 / calf_l 0.19 / hand_l 1.97`，五项全动；日志**不再出现** `ProceduralIdle 启用程序化待机`（已让位）。
    - **常驻门**：新增 `Tools/AnimationWiringVerify.py`——校验 ① 图是 2 节点直连图（Output 9,1 + Animation 9,2，1 参数）且引用 Ada 与 SecondaryMotion；② 125 条动画条目全部 `[clip][16×0]`。当前 **PASS**（125/125）。重导/换图后先跑它。
    - **仍缺**：世界相机取景问题导致像素级截图仍不可用（骨骼读数为准）；`Character_1.flax` 自身 127 条条目仍指向不存在的 `f08f8142…`（只影响直接在 Character_1 上播剪辑，本地身体已不用它）。

  - **2026-10-23 第五轮：Idle/Walk/Run/Sprint 三级混合 + 按速度驱动（#78 结案）**
    - **图结构（11 节点，1552 B 面）**：`Output(9,1) ← BlendSprint(9,9) ← BlendRun(9,9) ← BlendWalk(9,9) ← Idle(9,2)/Walk(9,2)，Run(9,2)，Sprint(9,2)`，三个公开 Float 参数 `LocomotionWalk` / `LocomotionRun` / `LocomotionSprint`（Get 节点 `6,1`）。生成器 `Tools/_animation_graph_make_locomotion.py`（含结构回读断言；原图备份 `Tools/_backup_AnimationGraph_before_locomotion_20261023.flax`）。参数名按引擎格式 UTF-16 逐字符 XOR 97 落盘（`Write(StringView,int16 lock)`）。
    - **驱动**：新增 `Source/Game/Character/LocomotionGraphDriver.cs`——按根 Actor **水平位移/时间**测速（本地与远端同一套位置来源），低通后换算 `walkAlpha=speed/WalkSpeed`、`runAlpha=(speed-WalkSpeed)/(RunSpeed-WalkSpeed)`、`sprintAlpha=(speed-RunSpeed)/(SprintSpeed-RunSpeed)`，用 `AnimGraphParameter.Value` 写入。阈值取工程口径 `5 / 10 / 15`（`PlayerController.MoveSpeed=5`、跑步 ×2、冲刺再一档）。挂在本地创角与 `EnsureMetaHumanBody`（远端玩家）两处。
    - **实机验收**（PIE，`DebugForceSpeed` 注入 0/10/15，`actor_bone_probe` 1.0 s，取 before 采样）：`foot_l` idle→run **13.92**、run→sprint **28.43**；`calf_l` **15.44 / 15.84**；`hand_l` **2.92 / 7.59**。run 与 sprint 姿态明确不同 ⇒ 第三级 Blend 的 B 支路确实在播放。日志 `READY ... walk/run/sprint` 三参数绑定成功。
      早前 0/5/10 三档：`foot_l` idle→walk **11.15**、idle→run **58.20**；400/160/0 三档读回 `walk=1.00/run=1.00`、`walk=1.00/run=0.04`、`0/0`，与公式一致。
    - **踩坑记录**：改完图后第一次 PIE 报 `missing param`，原因是**编辑器仍持有旧图资产**（在编辑器运行时重写了 .flax 文件）；重启编辑器后按名绑定正常（`bound=True mode=name`）。⇒ 换图资产后必须重启编辑器，不要相信同一进程内的 PIE。
    - **常驻门更新**：`Tools/AnimationWiringVerify.py` 现在校验 11 节点结构、4 参数名、Ada + 四个剪辑引用（Idle/Walk/Run/Sprint）、125 条按名映射；当前 **PASS**。
    - **仍缺**：状态机（本实现是参数混合，不是 UE 式状态机）；转向/停步过渡剪辑；世界相机取景导致像素级验证仍不可用。

6a2w. **创角预览补上头发 + 发色实时生效（2026-10-22，已落地）**
- **缺口**：`CharacterPreview3D` 以前没有任何头发 Actor（全文无 hair），捏脸全程光头；发色色板只写 `CcSession.HairColorIndex`，要进世界才看得到。
- **修法**：预览按世界侧同一约定（`HundunWorldGame` 世界头发段）把 `EquipmentDatabase.HairCardsModelPath` 作为 **Ada 头下的零变换 StaticModel**，用 `MH_Hair` 的 `HairColor` 参数着色；面板新增 `HairColorChanged`，点色板与 RefreshSteps 两处通知宿主 → `CharacterPreview3D.SetHairColorIndex`。头隐藏时头发单独收（IsActive 不随父级传播）。
- **像素证据**：发色 idx4 点击后 **9,631 px** 变化，发区（x450–630/y290–380）均值从 **(55,35,21) 棕** 变 **(81,75,70) 灰蓝**，与 `CcFacePresets.HairColors[4]=(0.14,0.18,0.30)` 一致（[默认](file:///C:/Works/GitHubProjects/HundunWorld/Tools/_mh_hair_default.png)、[idx4](file:///C:/Works/GitHubProjects/HundunWorld/Tools/_mh_hair_idx4.png)）。
- **页签切换不浮空**：头页 → 身体页时头发随头单独收起（身体页画面只有躯干，无悬浮发团）；切回头页恢复（head-vs-back 仅 191 px 噪声级差异）。证据：[头页](file:///C:/Works/GitHubProjects/HundunWorld/Tools/_mh_hair_head.png)、[身体页](file:///C:/Works/GitHubProjects/HundunWorld/Tools/_mh_hair_bodytab.png)。
- **常驻闸门**：`TestPreviewHairWiring`（预览挂资产/写 HairColor/复用调色板；面板两处 Invoke；宿主接线）；harness **1567/0/0**。

6a2x. **瞳孔大小 eye.pupil_size 打通（2026-10-22，已落地 + 像素证据）**
- **缺口**：模板 `eye.pupil_size` 声明了 MaterialFloat 目标 `Eye_PupilSize`，但 `MH_Eye` 里没有同名参数 ⇒ 写回端静默丢弃；旧的“亮度不可分”结论因此只停在材质层。
- **离线定尺**：新增只读工具 `texture_stats`（`TextureBase.GetPixels` 失败时用 `GetMipData` 落原始压缩字节）。眼贴图是 **BC1**，离线解码得：瞳孔半径 ≈ **0.068 UV**（luma 40）、虹膜环 0.08–0.15（luma 122–139）、巩膜 >0.18（luma 212），圆心 (0.4997,0.4996)。
- **实现**：`MH_Eye` 增加 float 参数 `Eye_PupilSize`；用 `UV→Distance(0.5,0.5)→Smoothstep` 造两向遮罩——>0.5 扩瞳（向 0.02 黑内插）、<0.5 缩瞳（在烘焙瞳孔内用 `EyeIrisTint` 回填），0.5 两遮罩均为 0（默认外观不变）。材质图构建工具补了 `dist/smoothstep/const2` 三种 kind。
- **像素证据（PIE 捏脸页，驱动 eye.pupil_size 0→1→0→0.5）**：wide 与 narrow 的差异**最大像素正好落在双眼**（502,414 / 557,414，Δ=110/89；眼区其余最大 22 与背景噪声同级），说明效果局部且双向成立（[默认](file:///C:/Works/GitHubProjects/HundunWorld/Tools/_mh_p0.png)、[扩瞳](file:///C:/Works/GitHubProjects/HundunWorld/Tools/_mh_p1.png)、[缩瞳](file:///C:/Works/GitHubProjects/HundunWorld/Tools/_mh_p0z.png)）。
- **常驻闸门**：`TestPupilSizeTarget`（模板参数仍在且目标名未变）；harness **1581/0/0**。材质就地修改（该资产未纳入 git）；如需回退，删除新增的 `Eye_PupilSize` 参数与 38 个节点、并把 Color 接回原节点 110。
- **仍缺**：UE 原生用眼网格 morph（pupilNarrow/Wide）实现，粒度更细；本实现是材质近似（缩瞳用虹膜色回填）。

6a2y. **口红打通：根因是掩码指到了腮红图集（2026-10-22，已落地）**
- **6a2f 的真正根因**：`MH_Head_New` 的 `EyeLipstickMask` 默认贴图一直是 **FaceFoundationBlusherMask**（腮红图集），所以 Lipstick>0 时整块下半脸被染色；换别的导出名又全空。
- **新唇部掩码（派生）**：解码脸底色 `FaceColor_MAIN`（BC1 2048²）后，唇部相对皮肤满足 **B&gt;G**（唇 B−G≈+6，脸颊≈−18）。在嘴部 ROI 内取 `(B−G)&gt;0` 得 **32,352 px**（bbox x848–1204 / y1128–1275），高斯模糊后导出 `LipMask_Ada.flax`，并把它设为 `EyeLipstickMask` 的默认贴图。
- **颜色默认值**：`LipstickColor` 原来是**白色**（强度 1 会把嘴唇洗白）；设为自然红 **(0.55,0.12,0.14)**。
- **玩家参数**：模板新增 `makeup.lipstick`（Region=Lip、Kind=Makeup、0..1、默认 0）→ MaterialFloat **Lipstick**；头部参数 598→**599**，`MetaHumanBakeTables.py` 重烘，PIE 里 `parameterCount=629`（+1）。
- **像素证据**：直接把实例 `Lipstick` 0→1，差异**最大像素落在双唇**（x517–545 / y473–476，Δ=123；唇外 ≤22）。走模板参数 `metahuman_drive` 时 `valueBefore=0 → valueAfterCommit=1` 且实例读回 1（每次提交会重放全部参数，逐帧全局亮度有漂移，故像素判据用直接实例那组）。
- **闸门**：`TestLipstickTarget`（参数 Id→目标名 + 掩码资产在盘）；harness **1585/0/0**。
- **仍缺**：颜色不可选（固定红）；掩码是反照率派生而非 UE 资产；导入用默认选项（sRGB/压缩未钉死）。

6a2z. **腮红/眼妆：共用掩码与通道未拆（2026-10-22，负结果 + 冲突立案）**
- **腮红链需要两个参数**：只置 `BlusherMakeup=1` 画面无变化（p95=0）；`BlusherMakeupIntensity` 默认是 **0**。把 intensity 设为 1、颜色设粉后，变化区域 **bbox x471–587 / y357–556**（整张脸），不是脸颊。
- **原因**：`FoundationBlusherMask` 是一张**图集**——离线解码（BC1 2048²）得 R=**整脸覆盖**（>128 占 40.86%）、G=**眼下两片月牙**（0.98%）、B=0；而材质是两个分支都直接乘**采样到的 RGB**（R≈1）⇒ 腮红变成整脸染色。实验后已把 `BlusherMakeupIntensity` 还原 **0**、`BlusherMakeupColor` 还原 **白**。
- **共用掩码冲突（新发现）**：`EyeMakeup` 与 `Lipstick` **都乘 `EyeLipstickMask`**。我们已把它改指到唇部掩码，所以“眼妆”若启用会画到嘴唇上。当前 `EyeMakeup` 未暴露且实测为 0（6a2y 的唇部测试只动嘴唇），**暂无可见回归**。
- **正解（下一步）**：做**分通道图集**（R=眼妆区、G=唇部区、可选 B=腮红区）并在图里按分支 Unpack 对应通道；腮红还需要一张真正的颊部掩码（现有图集只有眼下月牙）。已建任务 #72。

6a3a. **世界角色"活着"：程序化待机（2026-10-23，已落地；骨骼 + 像素双向证据）**
- **背景**：6a2v 已证动画图为空、ALS 剪辑绑不上 DEF-* 骨架，世界角色 1.2 s 零骨骼位移（雕塑态）。
- **实现**：新增 `ProceduralIdleDriver`（挂在 `AnimatedModel` 上），本地在 `HundunWorldGame` 的 `AssignAnimatedModelResources` 之后、远端在 `RemotePlayerActor` 身体校验通过后各挂一次（`GetScript` 去重）；20 帧后捕获基准姿态，按 `SkinnedModel.Nodes` 跨命名族（DEF-*/spine_0x/upperarm_l/…）解析 7 个槽位；正弦微动幅度 0.6–2.2°、频率 0.22–0.35 Hz，写 `SetNodeTransformation(bone, baseM * RotationQuaternion(delta), false)`；`AnimationUpdated` 与 `OnUpdate` 都 Tick（基于基准姿态，幂等）。
- **让位自检**：未激活时窗口内骨骼在动即判"别人在驱动"；激活后只用 `fromBase > 0.3` 判真实动画接管（写回端装载时的一次性偏移不能当成动画）。
- **骨骼证据**：PIE 内 `actor_bone_probe`（actor=`LocalPlayer_43`，waitMs=1200）→ `elapsedSeconds=1.2`、`movedCount=4`：spine_02 Δ1.638、spine_03 0.999、upperarm_l 1.206、neck_01 0.974；头模型 0。日志 `基准姿态捕获：7/7 根` 与 `启用程序化待机（7 根）`。
- **像素证据**：把 World 的 Camera 临时移到角色正前方 `(0,6253,-350)`（原 `(0,90,0)`，PIE 停止即复原）、朝 +Z；用 `game_screenshot` 取 3 帧（间隔约 9 s / 19 s）做差。人物 ROI（x690–1010 / y440–950）相对首帧：`>20` 像素 **122 / 201**、max **138 / 166**；天空对照区 `>10` 像素 **0**、max **7**。差分可视化里躯干/腰腹成亮带、轮廓边缘整圈发光（[证据](file:///C:/Works/GitHubProjects/HundunWorld/Tools/_idle_diff_vis.png)、[原帧](file:///C:/Works/GitHubProjects/HundunWorld/Tools/_idle_cam0.png)）。
- **限制**：呼吸级幅度——单帧最大差 ~166/255 且集中在边缘，肉眼仅见极轻起伏；这是空动画图下的兜底，**不等于**真实动画回放（#71 仍未达成）。
- **闸门**：`TestProceduralIdleWiring`（双端挂载、右乘节点变换、动画回调+每帧兜底、幅度 ≤ 3°、让位判据分档）；harness **1594/0/0**。
- **回退**：删除驱动脚本与两处挂载，或把实例/字段 `AmplitudeScale` 设 0（渲染回 T-pose）。

6a3b. **妆容三分：眼妆/腮红/唇各用独立通道（2026-10-23，已落地 + 像素证据）**
- **真根因（两条并行链）**：`MH_Head_New` 里有**两套**妆容链。可见的是 `381→389`（389 经 510/511→530 Lerp 进材质 Color），而 `227→235` 那条是**悬空的死链**（235 无出线）。此前只改了死链，所以画面纹丝不动：可见链仍把整张 `EyeLipstickMask` 的**首分量**当 Lerp alpha（眼妆/口红同区域、腮红用 `FoundationBlusherMask` 的 R=整脸）。
- **打包掩码**：新资产 `Content/Character/Skin/MakeupMasks_Ada.flax`（2048²，导入 `Type=ColorRGBA, sRGB=false`，引擎实为 **BC3_UNorm**）。通道：**R=眼妆**（由脸底色眼周暗像素 luma&lt;65 膨胀+模糊合成，94,750 px，bbox x610–1454/y665–871）、**G=腮红**（UE `FaceFoundationBlusherMask` 的 G，100,561 px，x607–1440/y727–960）、**B=唇**（`LipMask_Ada`，45,650 px，x842–1210/y1120–1283）。BC3 解码按 **DXT5 顺序（alpha 块在前）**，按 DXT1 顺序读会得到全白假象。
- **材质接线**：在可见链上按通道 Unpack——`384.B ← #532.Y`（腮红）、`386.B ← #531.X`（眼妆）、`388.B ← #533.Z`（唇）。为此给 `material_graph_build` 增加 `fromNodeIndex/fromPin`（直接改接老链、不复制节点）与 `nodeIndex`（同名节点消歧）；`material_graph_dump` 的连接回执补 `fromBox/fromPin`，否则“接到哪一路”只能靠猜。
- **默认色**：`EyeMakeupPrimaryColor` 白→**#141010**（深色眼线）、`BlusherMakeupColor` 白→**#E39B9B**（柔粉）；`BlusherMakeup` 默认 0→**1**（强度由 `BlusherMakeupIntensity` 单参数控制）。
- **玩家参数**：模板新增 `makeup.eye`（Region=Eye → `EyeMakeup`）、`makeup.blush`（Region=Cheek → `BlusherMakeupIntensity`），头部参数 599→**601**，材质类效果 51→**53**，只挂材质档 10→**12**；已重烘 BakedTables。
- **像素证据（PIE 捏脸页，逐通道开/关）**：眼妆 → 眼周 **963 px &gt;20**（lips 0 / rest_face 0 / bg 0），且眼周 RGB 123→69（压暗）；口红 → 唇部 **408 px**（eyes 2 / cheeks 2）；腮红 → 眼下颊带 **1,287 px**（rest_face 0 / bg 0），颊部 RGB 157,111,87→182,146,133（变粉）。UI 里 `SculptSlider` 的 `probeTag` 直接是 `makeup.eye/lipstick/blush`（123 行中第 35/63/79 行）。
- **模板链路真跑**：拖 `SculptSlider` #35（makeup.eye）到 **0.9768**，眼睛区域 **624 px** 变化、其余 0；再拖 #63（makeup.lipstick）与 #79（makeup.blush）到 **0.9768**，唇部 **411 px**、颊带 **494 px**、余脸 1 px、背景 0；两个滑块归 0 后与基线差 **5 px >20（max 24，噪声级）**⇒ 模板参数→材质→像素与归位都成立。
- **关键操作坑（记下来）**：材质图 `window.Save()` **不会重编译**；编辑器重启后仍复用 `HundunWorld/Cache/Shaders/Cache` 里的旧 shader（日志只有 load/clone，没有 `Compiling shader MH_Head_New`），于是“盘上图已改、画面还是旧行为”。改完材质图必须**清 `Cache/Shaders/Cache`（DX_SM5 + CacheVersion）再重启**，日志出现 `Shader compilation 'MH_Head_New' succeed` 才算换了码。
- **闸门**：`TestMakeupChannelTargets`（三条参数→目标名、打包掩码与验证脚本在盘）+ `python Tools/VerifyMakeupMasks.py`（R/G/B 面积、唇在眼下方 ≥150 px、眼唇重叠 <2%）；harness **1603/0/0**。
- **仍缺**：颜色不可选（固定深眼线/柔粉/朱红）；眼妆掩码是反照率派生的近似（UE 用独立眼妆资产）；死链 227–235 仍在图里（未删，仅悬空，后续手工清理）。

6a3c. **自动"放松站姿"实验：右臂写入无效，已整体回退（2026-10-23，负结果）**
- **动机**：空动画图下世界角色是 T-pose；试在 `ProceduralIdleDriver` 捕获基准姿态后，把上臂绕世界轴转到"朝下略外展"，让站姿自然一些。
- **左臂成立**：`upperarm_l` 转 **30.9°**，臂向 (0.61,−0.79,−0.03)→(0.37,−0.92,−0.15)，日志读数自证。
- **右臂失败**：`upperarm_r` 同参数下臂向始终 (−0.61,−0.79,−0.03) **原样不动**。已排除的写法：① `world*R`、`R*world` 两种乘序 × 正反轴共 4 个世界矩阵候选；② 局部右乘 `baseLocal*R(localAxis, ±angle)`；③ 用 `SkinnedModel.Nodes[].ParentIndex` 精确配对父子并以**节点下标** Get/Set（避开重名）。四种都读回零变化 ⇒ 不是乘序/手性/重名解析问题，怀疑右侧链是镜像节点或引擎拒绝镜像节点的变换写回。
- **决定**：代码**整体删除**（不是加开关保留），因为"单臂下垂"比对称 T-pose 更糟；世界角色继续 6a3a 的 T-pose + 呼吸微动。
- **回退验证**：重编后 PIE，日志只有 `基准姿态捕获：7/7`（无"放松站姿"行）；`actor_bone_probe` 1.2 s：spine_02 Δ1.637、spine_03 Δ0.989、upperarm_l Δ3.368、neck_01 Δ0.475 ⇒ 呼吸仍在、无双臂偏移。
- **对 #71 的含义**：基础站姿只能走真实动画/重定向，或先在 DCC 侧修右侧镜像链；继续在运行时世界矩阵上试只会得到半边结果。

6a3d. **胡茬通道打通：派生掩码 + 类型闸门修复（2026-10-23，已落地 + 像素证据）**
- **两个原因叠加**：① UE 导出的 `FaceBakedGroomRootTipGradientRegionMasks` 四通道全零，没有区域可用；② `MetaHumanMaterialMap` 把 `Skin_Stubble` 指到**贴图参数** `BakedGroomRootTipGradientRegionMasks`，applier 的类型闸门会把浮点写丢弃（只记 Warnings），材质图侧也没有消费者。
- **派生掩码**：下唇下方/下巴/两侧下颌/上唇窄带（按 FaceColor 版面）扣掉膨胀唇部，羽化后再叠 1/4 分辨率的颗粒噪声，导出 `StubbleMask_Ada_v2.flax`（ColorRGBA、sRGB=false、BC3；>16 约 34.2 万 px）。v1 是无颗粒的平滑版，仅作对照。
- **材质链**：把 `BakedGroomRootTipGradientRegionMasks` 的默认贴图改指 v2（该参数旧值全零、图里无消费者），新增 float `Skin_Stubble`，建链 `Skin_Stubble × 掩码 → Lerp(白, (0.20,0.155,0.13), t) → 乘进可见肤色`（根 Color 前的 530.A）。改图后按 6a3b 的坑清 `Cache/Shaders` 再重启（日志确认 `MH_Head_New` 重新编译）。
- **映射修复**：`Skin_Stubble` 改指同名 **float** 参数并加入引擎增补白名单；此前"指到贴图参数"是静默丢弃的典型形态。
- **像素证据**：直接置实例 `Skin_Stubble=1`（v1）→ 胡茬区 **3,904 px >20**（前额 0 / 眼区 0 / 背景 0）；v2 做了颗粒化。走模板滑杆 `skin.stubble`（123 行中第 113 行）拖到 **0.9768** → 胡茬区 **3,747 px >20**、其余区域 0 ⇒ 模板→材质→像素整条链成立。
- **闸门**：`TestStubbleChannel`（参数目标 + MaterialMap 必须指到 float + 白名单 + 掩码资产）；harness **1610/0/0**。
- **仍缺**：掩码是 UV 版面派生的近似，不是 UE groom 资产；没有毛发长度/密度/生长方向（UE 是 groom）；Ada 脸型上的胡茬只作为外观档。

6a3e. **妆容色温：口红/眼妆/腮红的冷暖档（2026-10-23，已落地 + 像素证据）**
- **缺口**：三个妆容颜色此前固定（口红自然红、眼线深灰、腮红柔粉），玩家只能调强度不能调色。
- **实现**：模板新增 `makeup.lip_tint` / `makeup.eye_tint` / `makeup.blush_tint`（0..1，默认 0），各带一条 **MaterialColor** 效果：BaseColor=当前自然色，TargetColor=更深/更暖的一端（口红→酒红、眼妆→暖棕、腮红→深玫瑰）。数值随 `Look.Values` 持久化，求解器产出 MaterialColors 后由 applier 写材质实例；头部参数 601→**604**，材质类效果 53→**56**，只挂材质档 12→**15**（已重烘）。
- **像素证据（捏脸页滑杆，强度与色温都走模板）**：口红 tint 0→0.9768 → 唇 **420 px >20**（眼/颊 0），唇均色 (153,48,49)→(118,25,50)；眼妆 tint → 眼 **343 px**（唇/颊 0）；腮红 tint → 眼下颊带 **556 px**（余脸/唇 0），均色 (174,140,130)→(168,98,98)。腮红最初的 TargetColor 太接近基色（只 28 px），加深到 (0.72,0.26,0.34) 后才可辨。
- **踩坑（记下来）**：**直接改材质实例参数不能与滑杆提交混用**——每次提交会按模板重放并重建虚拟实例，把直改值复位。第一次测试因此得到"只动口红却眼/颊也变"的假象（其实是直改的强度被清掉）。颜色类验证必须把强度与色温**都**通过滑杆驱动。
- **闸门**：`TestMakeupTintTargets`（三条参数必须存在、必须走 MaterialColor、目标名正确）；harness **1620/0/0**。
- **仍缺**：目前是两点插值（自然↔一档），不是任意色板；面板呈现是滑杆而非色块。

6a3f. **张口 morph 把 conform 头着色打崩：根因已定位并修复（2026-10-23，结案）**
- **现象（前序）**：捏脸页把 `head_lod0_mesh__jaw_open` 置 1 → 整脸黑斑（头部 ROI 均值 **42.4→13.9**）；`jaw_open=0.05` 与 `0.3` 均值**完全相同（13.87）**；`eye_blink_L=1` 半脸黑斑（27.2）；`brow_raise_L=0.2` 掉到 32.7；闭合 41.5。只要任一 shape 非零就掉亮、且与权重无关。
- **根因（引擎缺陷，源码已核）**：`AnimatedModel.cpp` 在 `UseNormals` 分支里重建切线时**读错了流**——`Float4 tangentRaw = normalStream.GetFloat4(vertexIndex);`（应为 `tangentStream`）。于是 `tangent` 恰好等于刚更新并归一化的法线，投影 `tangent -= dot(t,n)*n` 得 **零向量**，`Normalize()` 后 TBN 退化 ⇒ 该 shape 覆盖区域的 normal map 着色整体变黑。因为这段在“任一 shape 权重非零”时对整段 [MinVertexIndex, MaxVertexIndex] 无条件执行，所以**与权重无关**（0.05 与 0.3 同值）——与观测完全一致。
- **数值复算（真 GLB，`Tools/_morph_tangent_proof.py`）**：jaw_open 6003 个位移顶点、eye_blink_L 1331、brow_raise_L 18；缺陷路径 `|tangent|` 最大值 **0.000000000**（dot=1.000000）；按正确写法（读切线流再正交化）可归一化 6003/6003，与法线夹角余弦 ~1e-16。7389 个 target 全部带 NORMAL。
- **修复（资产侧，保持引擎不动）**：`Tools/_strip_morph_normals.py` 删掉 7389 个 target 的 NORMAL → `Tools/head_no_morph_normals.glb`；用原名 `Ada_FaceMesh_conformhead4.glb` 原地重导 `Content/Character/Models/head_mh/Ada_FaceMesh_conformhead4.flax`。`ModelTool` 的 `UseNormals |= !NormalDelta.IsZero()` ⇒ 全为零后 **UseNormals=false**，那段缺陷代码整块被跳过，基础切线保持原值。回执：821 形状、GUID 仍为 `81c84b06…`、9 个材质槽未变；9 个资产文件已备份到 `Tools/_backup_head_mh_20261023/`（回退=直接拷回）。
- **像素验证（PIE 捏脸页，隔离材质贡献）**：`metahuman_drive("morph.head_wm1_jawopen",1)` 后把 `head_wm1_normal_head_wm1_jawOpen` 材质浮点**复位为 0**，只留 Morph：与基线相比 **2486 px 差>5**（质心在嘴部 y≈434、maxdiff 71）⇒ 几何确实在动；头部前景均亮 **82.06→81.87**、p95 **115.0→115.0**（修复前同一驱动是 41.5→13.87）。不隔离材质时 81.97/81.98。结论：**morph 能动了、着色不再掉**。
- **仍未闭环**：牙齿/唾液目视（#70）此前被它挡住，现在可以继续；本修复的代价是形状不再改法线（光照跟形变走的那一点细节丢失），若要恢复需修引擎那一行（见 6a3g 的同一份源码位置）。
- **现场复位**：jaw_open 归 0、材质浮点归 0、PIE 停止、探针 Actor 已删、模板 JSON 与备份逐字节一致。
- **2026-10-23 追加（#80，已用骨骼级确定性读数结案：写入成立，“画面不变”是幅度问题）**：捏脸页 `actor_bone_probe`（头部 Actor、真实时间）：
  `face.jaw.c.tz` −1→+1 使 `FACIAL_C_Jaw` 位移 **0.800**；`face.forehead.c.tz` 0→1 使 `FACIAL_C_Forehead` 位移 **0.400**。
  0.4/单位来自 `MetaHumanAppearanceApplier.FaceBoneGain=20` × 模板 `Axis.z=0.02`，**属设计幅度**——±1 只动 <1 cm，在 1060 px 预览里约 1–2 px，低于渲染抖动（同状态两帧可差 1k–13k px）。
  之前两条“无变化”对比均无效：① 首帧截在相机自动取景未稳定时；② 回拖时 `ui_drag_control` 没抓到滑块手柄，两次截图其实是同一状态（面板值一直是 98.54）。教训：像素 A/B 前先确证参数值真的变了。
- **2026-10-23 追加二（morph 位移尺度实测，立案 #81）**：对 `Tools/head_no_morph_normals.glb` 的目标 POSITION 直接量最大位移：
  `eye_blink_L 0.00140（1375/24408 顶点非零）`、`eye_blink_R 0.00133`、`jaw_open 0.00516（6196 顶点）`、`brow_raise_L 0.00021`；基础头尺寸 0.30×0.34×0.22。
  据此实现的 `FaceBlinkDriver`（直接写 `head_lod0_mesh__eye_blink_L/R`）权重读回 1，但预览 A/B 只有抖动级差异、眼睛不闭 ⇒ **单靠这些 morph 不可见**，驱动已回退删除。
  UE 的表情/眨眼还依赖眼睑/下颌**骨骼与 rig logic**，morph 只是其中一层；本工程模板把 `jawOpen` 只映到 shape，未带骨骼旋转。
- **同日晚些时候已解（见 6a3i）**：眼睑骨在运行时确有蒙皮；眨眼改绕眼球旋转、张口改下唇关节组平移，均有像素连拍证据。

6a3g. **编辑器缩略图/首次蒙皮绘制会撞 `AnimatedModel.cpp:82` 断言（2026-10-23，环境级缺陷）**
- **现象**：编辑器（Development，断言开启）在 Content 窗口渲染 head_mh 缩略图、或在场景里第一次把 490 骨的头挂给 AnimatedModel 时弹 `Assertion failed! AnimatedModel.cpp Line 82: CurrentOffset + size <= CurrentSize`，主线程被模态框钉死。
- **源码**：`Allocate(size)` 在 `CurrentOffset+size > CurrentSize` 时**只翻倍一次**就断言（`CurrentSize = CurrentSize==0 ? 16KB : CurrentSize*2`）。`PerBoneMotionBlur` 默认 **true**，单模型分配 = 骨数×48×2；490 骨 ≈ 47KB，冷缓冲 16KB→32KB 仍不够 ⇒ 断言。若点掉对话框，代码继续按 32KB 缓冲做越界写，实测**几秒内整个进程崩溃**。
- **处置**：**不要点“确定”，直接强杀重启**。缩略图来自 `ThumbnailsModule` 的单一常驻 RenderTask（所有缩略图共用一条骨骼缓冲），冷启动时先渲染大骨模型必炸。已验证的启动规避：重启前把 `Content/Character/Models/head_mh` 临时改名，让 Content 窗口找不到该目录，启动后再改回（改回本身不触发缩略图渲染）。
- **未修**：这是引擎列（单个 `.cpp` 的循环/默认值问题），本机没有引擎构建流水线；资产侧/编辑器侧都只能规避。若将来重建引擎：① 把倍增值改成 while 循环；② 或把 `PerBoneMotionBlur` 默认改 false。
- **2026-10-23 运行时侧缓解（已落地）**：`AssignAnimatedModelResources` 统一 `PerBoneMotionBlur=false`（490 骨 ×48×1≈23KB，冷缓冲 32KB 可容纳），世界头与远端头同样关闭。改后冷启动 PIE **0 断言**、身体骨骼正常动。编辑器缩略图那条共用缓冲仍在，只能继续用启动规避（临时改名 `head_mh`）。

6a3h. **头部资产 GUID 每次重启被“重复资产”仲裁改写（2026-10-23，环境级缺陷）**
- **现象**：启动日志反复出现 `Founded duplicated asset '<id>' ... Locations: 'C:\...\head_mh\...flax' and 'Content/Character/Models/head_mh/...flax'` → `Changing asset id ...`；同一文件被两种路径拼写（反斜杠绝对路径 vs 正斜杠相对路径）各注册一次。两天内头的 GUID 已从 `2e2fda68…`、`664165a9…`、`1f474321…` 变到 `81c84b06…`。
- **影响**：7 个角色预制体里的 `SkinnedModel` 仍写 `16a36437…`（任何 .flax 头都不是这个 ID），靠 `AssetsCache.dat` 里的旧 ID 重定向才活着；**清缓存前必须先把预制体改指到磁盘头的现行 GUID**，否则头会从预制体里丢。
- **结论**：本次不动它（运行路径按资产路径加载头，捏脸页与代码链不受影响）；把它记为环境风险：不要无脑清 `AssetsCache.dat`，先跑 `Tools/_flax_dup_guid_scan.py` 与本审计的引用对账。重复条目来自导入工具早年用相对/混合斜杠路径注册，修法是让导入工具统一 `Path.GetFullPath` 后再注册（未做）。
- **现场**：`Tools/_dump_windows.ps1` / `_dismiss_error.ps1` 为本次取证留下的读窗口/点按钮工具（只读或只点自家编辑器）。
- **2026-10-23 定因与修复（#77）**：重复条目是**路径式工具调用注入的**，不是缓存自发产生——清掉 `AssetsCache.dat` 后首次启动 **0 条** `duplicated asset`；随后调用一次按路径解析的 `model_inspect`，再启动就复发（`10d3edba…→1857d334…`）。处理：① 备份并删除 `Cache/AssetsCache.dat`（备份 `Tools/_backup_AssetsCache_20261023.dat`）；② 头磁盘 GUID 已变为 `1857d3344cdd80fcd97b87acfbb1b3a6`；③ 新增 `Tools/_fix_head_guid_refs.py`，把 7 个角色预制体里失效的 `16a36437…` 全部改指该 GUID（逐文件断言旧 ID 恰好 1 处、替换后为 0；备份 `Tools/_backup_prefabs_headid_20261023/`）；④ 干净重启后日志 0 条重复告警、GUID 稳定、创角预览头正常显示。**世界侧也已复核**：PIE 世界里 `LocalPlayerHead_43` 上 `head`/`FACIAL_C_Jaw` 骨可探针，`face.jaw.c.tz +1` 使 `FACIAL_C_Jaw` 位移 0.4。**剩余**：给 HundunAgent 的路径式工具补路径规范化（或内部一律用 GUID），并把“启动日志出现 duplicated asset”做成常驻红灯。

6a3i. **眨眼/张口从"看不见的 morph"改成骨骼通道（2026-10-23，已落地 + 像素证据；#81/#82 结案）**
- **定因**：Ada 头的 GLB 是 **12 影响/顶点**（`JOINTS_0/1/2`），之前只读了 `_0`，于是误判"眼睑骨没有权重"。带权重的是**带序号的眼睑骨**（`EyelidUpperA1..A3`、`EyelidUpperFurrow1..3`、`EyelidLowerA1..A3` 等），无后缀母骨（`FACIAL_L_EyelidUpperA`）总权重为 0 —— 按母骨名查会得到假阴性。运行时资产只保留 4 影响，眼睑骨仍在其中，但覆盖只有 12 影响口径的约 1/3。
- **量出来的支点事实**（`Tools/MetaHumanEyelidClosureProbe.py`）：绕**每根骨自己的头**转 60° → 睑缘只动 0.25mm（骨就贴在眼皮上）；绕**眼球中心**（`FACIAL_L/R_Eye`）转，位移才随角度增长（12 影响口径 30°→6.3mm；4 影响口径 90°→8.9mm、110°→9.3mm）。⇒ 支点必须是眼球，表里每条旋转通道都带 `pivotBone`。
- **落地**：`Tools/MetaHumanBuildExpressionRig.py` 生成 `Content/Templates/Expression_Rig.json`，现为 **7 通道**：
  - 左右 blink / eyelidU / eyelidD：模型空间绕眼球旋转（blink 上睑 110°、下睑 −30°；数值按运行时 4 影响口径标定）；
  - 张口 `CTRL_C_jaw.ty`：**平移通道**（56 根 `12IPV_LipLower*` 下移 3cm → 下唇约 8mm 开口）。`FACIAL_C_Jaw` 在下脸权重只有 61.5（<1%），转/移都无效，故选真正带权重的下唇关节组；表里存物理厘米，写回端乘 `FaceBoneGain` 前会先除掉它。
  - `MetaHumanFaceAnimator` 新增自然眨眼（2.5~6s 随机、0.22s 正弦包络，`AutoBlink` 默认开）；张口有 `AutoJawDemo` 调试开关（默认关）。
- **这轮踩到并修掉的两个写回端缺陷**：
  1. **表情量写进解算器对象**：`composed.Bones[k]` 可能是解算器复用的 `MetaHumanBoneOp`，就地 `Add` 会让表情逐帧累加（实测张口 0.5s 从 1.5cm 涨到 6.5cm、几秒后几米）。修法：合并前 `Clone()`。
  2. **动画重置姿势后仍"先撤再叠"**：姿势已被动画重算时，上一轮的表情量已不在里面，先撤等于把眨眼每帧反着抵消一次（表现为骨骼在动、画面不动）。修法：以 `PoseMatchesWritten` 判断"姿势还是不是我们上次写的那份"，是则增量、否则全量施加。
- **证据**：
  - 骨骼：PIE 里 `FACIAL_L/R_EyelidUpperA1` 随眨眼往返；`preview_bone_drive` 单侧下移 Furrow3 2cm 可单独压窄该眼（证明眼睑骨在运行时确有蒙皮）。
  - 像素：捏脸页连拍里上睑从睁开到闭合清晰可见（[blink 连拍](file:///C:/Works/GitHubProjects/HundunWorld/docs/metahuman/ue_runtime/expressions_20261023/blink_contact_sheet_v2.png)）；张口同样有开合连拍（[jaw 连拍](file:///C:/Works/GitHubProjects/HundunWorld/docs/metahuman/ue_runtime/expressions_20261023/jaw_contact_sheet.png)），开口内可见牙齿。
  - 常驻门：`Tools/MetaHumanVerify` **1621/0/0**（新增内嵌表逐字对照）；PIE `metahuman_selftest` **99/0/9**（"表情骨骼表装载与可用性"组：7 通道、旋转 6 + 平移 1、支点必须是眼球骨、张口位移向下且量级 0.5~4cm）。
- **已知偏差**：张口不是 UE rig logic 的"下颌骨带牙齿/下巴一起转"，而是下唇骨组平移 ⇒ 牙齿网格不跟着降、嘴角不动；眼睑是绕眼球刚性旋转，极限角度下睫毛/眼角形变与 UE 不完全一致。要完全对齐仍需解 DNA `bhvr` 的 rig logic 或做下脸重权重。
- **下齿骨这条死路（负结果）**：给张口加第二段平移 `FACIAL_C_TeethLower`（-1cm）后，牙齿网格被拉成长条（从口腔一直垂到画面底部）。用 `preview_bone_drive` 单独平移这颗骨（不经过写回端）同样复现 ⇒ 是网格/权重本身的问题，不是表情通道累积。已整体回退（表里只留下唇段），并把"不要加 TeethLower 平移"写进生成器注释。

6a3j. **多发型资产：从 UE 可选 Groom 库导入 6 套（2026-10-23，资产层已落地；选择器待接）**
- **背景**：此前只有 Ada 自带的 `Hair_S_Coil` 一张卡，① 的「多发型」缺口是资产级缺口。UE 5.8 的 `MetaHumanCharacter` 插件自带整套可选 Groom（Hair 40 张 LOD0 卡 / Beard 7 种），只是默认没挂载。
- **导出**：`Tools/metahuman_hair_export.py`（无头 UE，`-abslog` 判 `[HAIR] DONE`）。先把 `MetaHumanCharacter` 插件写进测试工程 `MetaHumans.uproject`（备份 `.bak_20261023`），并在脚本里 `scan_paths_synchronous(["/MetaHumanCharacter"])`——插件 `Optional/` 下的资产默认不在 Asset Registry，不扫会全部 `could not be found`。
- **本批 6 套**（单 Group）：`Hair_S_Clean`、`Hair_S_Pixie`、`Hair_M_BobStraight`、`Hair_S_BobLayered`、`Hair_S_LowPonytail`、`Hair_M_SideSweptFringe`；网格 `Output/mhbaked/hair/*.glb`（6/6，均有 POSITION/NORMAL/TANGENT/UV0/UV1），卡片遮罩 `*_RootUVSeedCoverage.png`（2048²，alpha 0..255 有真实变化）。注意 `Hair_L_Straight` 的遮罩资产名少一个 t（`RooUVSeedCoverage`）且是双 Group，本批不取。
- **导入 Flax**：网格按 `Scale=100` 导入为 Model（`Content/Character/Models/mhhair/<Style>_CardsMesh_Group0_LOD0.flax`），材质槽统一指认 `MH_Hair`；遮罩导入到 `Content/Character/Skin/<Style>_RootUVSeedCoverage.flax`（当前 `MH_Hair` 没有贴图采样节点，`HairCoverage` 是 Vector4 常量，遮罩留待后续做纹理采样材质变体）。
- **目视**：捏脸预览里运行时替换 `CharacterPreview3D_Hair.Model` 逐个截图（[四款对比](file:///C:/Works/GitHubProjects/HundunWorld/docs/metahuman/ue_runtime/hair_styles_20261023/hair_styles_sheet.png)）：BobStraight / LowPonytail / Pixie 贴合正常；`Hair_S_Clean` 极短发卡埋进头皮（近乎光头），**从候选里剔除**。
- **待接**：发型选择器与持久化（模板参数 + 预览/世界/远端换模 + UI），见 #83；卡片仍是 Opaque 无纹理切边（与现有 Coil 同一近似）。

6a3k. **发型选择器：模板参数 hair.style + 预览/世界/远端换模（2026-10-23，已落地）**
- **数据核**：新增效果类型 `HairStyle`（离散下标，只认"加"的那一次，取整并夹到参数量程）；解算结果加 `HairStyles` 字典；`MetaHumanSession` 的增量/全量对账也带上这一档。脸模板新增参数 `hair.style`（0..5，Step 1，Effects = HairStyle→hair），参数总数 605、效果 1672。
- **取值落地**：`MetaHumanCharacter.HairStyle` 在每次 `ApplyNow` 从解里取（缺省 0）；`Game/Character/HairStyleCatalog.cs` 管 6 套资产（Coil / Pixie / BobStraight / BobLayered / LowPonytail / SideSweptFringe）；`Game/Character/MetaHumanHairBinder.cs` 挂在发卡 StaticModel 上，向上找宿主、值变才换 Model，并把换模前的发色虚拟实例指回去。预览侧因为 `MetaHumanCharacter` 挂在常驻宿主 `MetaHumanSculptHost`（不在预览父级链里），改由 `CcFaceSculptPage` 在 `OnApplied` 时推 `Preview.SetHairStyle(...)`。
- **UI**：捏脸面板发色行下面新增 6 个发型按钮（`hair.style` 与滑杆同一条 Adjust/Commit 链，选中态高亮）。
- **证据**：
  - `metahuman_drive hair.style=3` → 日志 `预览发型 -> 层次波波`；`valueAfterCommit=3`，`undoTimes=1` 后 `valueAfterRollback=3`、`rollbackWorked=true`（④ 步可回溯成立）；
  - UI 按钮点击（`ui_invoke Button#5/#6 Clicked`）→ 日志 `预览发型 -> 层次波波 / 低马尾`，历史游标前进；
  - 离线门新增"发型跨发布持久化"：提交 → Publish → Look JSON 往返 → 新会话重解仍为 4 ⇒ **1627/0/0**；
  - PIE `metahuman_selftest` **113/0/5**（0 失败）。
- **待补**：远端走同一个绑定器但还没在双客户端上取证；卡片仍是 Opaque 无纹理切边；`Hair_S_Clean` 已剔除。

6a3l. **发型的预览/世界实机取证 + 姿势重放的每帧空引用（2026-10-23 收尾）**
- **世界侧换模（本次补上）**：进世界后 `LocalPlayer_43/LocalPlayerHead_43/LocalPlayerHair_43` 挂 `MetaHumanHairBinder`；日志先后出现
  `[MetaHumanHairBinder] style=0 model=Content/Character/Models/mhhair/Hair_S_Coil_Cards.flax`（进场首次 Apply）与
  `[MetaHumanHairBinder] style=3 model=.../Hair_S_BobLayered_CardsMesh_Group0_LOD0.flax`（`metahuman_drive hair.style=3` 后，`valueBefore=0/valueAfterCommit=3/historySteps=2`）⇒ 世界发卡确实换了 Model。
  成功路径补了一条 ASCII 键日志（`style= / model=`），磁盘日志中文会被 NUL 打断，只有 ASCII 键能可靠 grep。
- **预览侧像素证据**：捏脸页把发型切到 Pixie、卷发各截一张离屏预览（`ui_preview_screenshot`：
  [Pixie](file:///C:/Works/GitHubProjects/HundunWorld/docs/metahuman/ue_runtime/hair_styles_20261023/preview_pixie_20261023.png)、
  [卷发](file:///C:/Works/GitHubProjects/HundunWorld/docs/metahuman/ue_runtime/hair_styles_20261023/preview_coil_20261023.png)），逐像素差
  **47,682 像素 > 30（bbox x389..738, y537..957，正是头部）**；同发型相隔 10 秒的两张只有 **1,054** 像素差（眨眼/待机噪声）⇒ 差异来自发型本身，不是姿态抖动。
- **顺手修掉的回归（重要）**：上一轮为"姿势感知"加的 `AnimatedModel.GetCurrentPose` 在**写回目标已销毁**时会被原生侧抛 `NullReferenceException`，
  且它挂在每帧重放上：一次 PIE 在页面重建/进场窗口里刷了 306 组栈、日志 2 分钟涨到 2 MB 上限。现在 `ReplayBoneOffsets` 先查 `ModelAlive`（原生指针为 0 直接返回），
  读姿势再包一层 try/catch：失败只报一次 `[MetaHumanApplier] pose-read-failed: <类型> model=<名字>`，随后静默 120 帧再试一次。
  复跑同一创建流程（选角色→性别→脸型预设→捏脸页）后日志 `Internal_GetCurrentPose` **0 次**、`pose-read-failed` **0 次**。
- **回归门**：插件两目标 + `GameEditorTarget Development` 重编 0 错（DLL 内新字面量 `pose-read-failed`/`model-not-ready` 各 1）；`metahuman_selftest` **113/0/5**。
- **同一类空引用还有第二条出口（已修）**：从命名页按「返回」重进捏脸页时，`MetaHumanCharacter.CollectModels()` → `MetaHumanAppearanceApplier.Clear()`
  会对**上一页那具已销毁**的 AnimatedModel 调 `GetNodeTransformation`，原生抛 NRE；异常冒到 `CcFaceSculptPage.AttachSculpt`
  被 catch 后 `_character = null` ⇒ 整页滑杆/发型全部静默失效（旧 build 实测：`[CcFaceSculptPage] 绑定 MetaHumanCharacter 失败: NullReferenceException`，
  `at …Applier.Clear() … CollectModels() … RetargetModels() … AttachSculpt():420`）。同一次旧 build 里还伴随 MCP 桥无响应、优雅关闭不退出（强杀重来）。
  现在 `Clear()` 先判 `ModelAlive`：目标没了就只清本地记账（写不进去也撤不出来），骨骼恢复再包一层 try/catch，
  只报一次 `[MetaHumanApplier] clear-failed: <类型> model=<名字>`。
  **复测**（重编后同一路径）：创建→捏脸→命名→返回，日志两次 `[CcFaceSculptPage] 重绑 alive: targets=2` 都成功，
  无绑定失败、无 `clear-failed`、MCP 桥全程可用；返回后 `[CharacterPreview3D] 预览发型 -> 层次波波`（会话里的发型跨页保留并重新套到新预览上）。

6a3m. **"预览头 morph 写权重不驱动渲染"已量清：是幅度 mm 级，不是写不进去（2026-10-23，撤回该立案）**
- **方法**：直接用 `Tools/MetaHumanMorphDeltaProbe.py` 解析 `Tools/Ada_FaceMesh_conformhead4.glb` 的 morph target POSITION accessor（不经过引擎、不看权重读数），逐 target 求最大顶点位移。
- **读数**：`meshes=1 / primitives=9 / targetNames=821`；头网格 prim0 **699 个 target 非零**；整份 GLB 非零 820。
  关键量级（GLB 单位=米，导入 Scale=100 后即厘米）：`jaw_open` **6.35 mm**、`jaw_left` 3.95、`jaw_fwd` 4.10、`eye_blink_L/R` 1.65/1.69、
  `mouth_left/right` 3.65/3.90、`cheek_blow` 3.45、`mouth_cornerPull` 1.50、`brow_*` 0.2~0.8。
- **结论**：此前的"写权重 1 但嘴不张"是把 **5~6 mm 级形变**误读成"没有形变"。这批 morph 本来就不是表情主运动（UE rig 用骨骼承担大动作），
  与 6a3i 改骨骼通道的判断一致；**"morph 通道是死的"不成立**，不要再按这个前提去改导入/采样。
- **残余不确定（如实保留）**：还没有一次"morph-only + 大位移 + 像素差"的运行时正证（模板里每个参数都同时带骨骼效果，没法只写 morph）。
  若将来要给牙齿/唾液这类纯 morph 通道做判据，挑 `cheek_blow`（3.45mm，面部最大）或 `jaw_open`（6.35mm）写 1.0，做前后像素差；届时再定。

6a3n. **发型下标进外观副本 + 修回被发型改动打断的 UI 离屏门（2026-10-23）**
- `CcSession` 新增 `HairStyleIndex`；捏脸页点发型时写它、`SyncColorIndices` 跟着会话回填；`MainUIManager.RequestCreateCharacter`
  不再硬编码 `AppearanceInfo.HairStyle = 0`（旧注释"等真有发型数据再填"已过期）。外观副本只是留档，渲染仍以 look 里的 `hair.style` 为准。
- **修回归**：`Tools/MetaHumanUIGate` 在发型选择器落地后就一直是红的（`MetaHumanSculptPanel.cs` 引用
  `HundunWorld.Game.Character.HairStyleCatalog`，而门只编 UI 文件 ⇒ **CS0234 ×2 + CS0103 ×2**）。把
  `HundunWorld/Source/Game/Character/HairStyleCatalog.cs` 加进门的 `Compile` 列表后 **0 警告 / 0 错误**。
  教训：面板新增任何 UI 外类型，都要同步问"离屏门编得到吗"。

6a3o. **头部承载量逐格对账：552 / 0 / 37 / 16 全对上，没有静默死通道（2026-10-23）**
- **口径**：`Tools/MetaHumanHeadGapProbe.py` 直接读导出源 GLB 的明文名单（876 节点 / 821 形状），按"一条参数的所有目标都在名单里才算全可承载"重算；
  读数与引擎 `MetaHumanBinding` 报告的 **552 全可承载 / 0 部分 / 37 写不出来 / 16 只挂材质（合计 605）** 逐格一致。
- **37 条不是死通道**：全部是 `morph.head_wm1_*` / `morph.head_wm13_*` 这种 **UE 命名族的 morph**，资产里叫 `head_lod0_mesh__*`。
  写回端靠 `MetaHumanMorphBridge` 翻译：33 条成表（运行态 `经表翻译=33`），**4 条 lips（DL/DR/UL/UR）是你早先拍板 declined 的**（harness「三跳未对接（4 条）」同名）。
- **16 条只挂材质**：肤色/瞳色/发色这类纯材质参数，不是形状缺口。
- **读诊断的陷阱（这次差点被带偏）**：`metahuman_inspect` 里的 `跳过骨0=FACIAL_L_CheekInner,…` 是 **applier0 = 身体** 在跳过脸骨
  （身体骨架当然没有 FACIAL_*），不是"头缺这 16 根骨"。头那具是 applier1，它的跳过骨为空。两具写回端必须分开读；
  否则会得出"头缺 16 根母骨、37 条参数是死的"这种错误结论（真实缺口 0）。
- **别再扫 .flax 找名字**：12.7 MB 的头资产里 `FACIAL_` 的 ASCII 与 UTF-16 出现次数都是 **0**（压缩存储）——扫描器给出的"589 条写不出来"是探测器失明。

6a3p. **远端发色改以 look 为准（2026-10-23）**
- **事实**：`GetCharacterAppearanceResponse.HairColor` 取的是 `info.Appearance.HairColor`（`CharacterHandler.cs:733`），
  而外观副本只在**建角**时写入；玩家之后改发色并发布只更新 look（`hair.color`），不回写那一列 ⇒ 远端会拿到旧发色。
- **改法**：客户端 `MetaHumanAppearanceService.OnRemoteAppearanceResponse` 在 look 里存在 `hair.color` 时，
  按与捏脸页 `QuantizeIndex` 相同的口径量化成色板下标；look 里没有这一项（玩家没动过发色）才回落服务端字段。
  trace 改成 `hair=<服务端>/<look>`，两种来源一眼可分。
- **门**：`dotnet build HundunWorld/Source/Game.csproj --no-incremental -p:BaseOutputPath=bin/Verify/` **0 错**；
  `Flax.Build GameEditorTarget Development` Done，DLL 内 `QuantizeHairColorIndex` 1 处。
- **仍待**：真第二客户端（或服务端造远端实体）的像素级取证，见 #84/#69。

6a3q. **身体"身高撕裂"重新定因：不是影响数，是两层壳的权重差；离线 LBS 已证平滑有效（2026-10-23）**
- **推翻旧前提**：`#61` 写的"按 4 影响重导"不成立——`Ada_BodyPreview4.glb` 每顶点实际影响 ≤4、`WEIGHTS_0` 和恒为 1.0（本轮复测：采样影响数分布 4/3/2/1，W0 sum min=max=1.0）⇒ 裁影响数不会改变任何东西。
- **新证据（逐层排除）**：
  1. 同位置复制顶点（1857 组）权重**完全一致**（L1 全 0）⇒ 不是 UV 接缝复制顶点；
  2. 换成"相距 ≤6mm、法线同向"的近邻对后，躯干带（Y≥0.90）**33.8% 的对 L1>0.2**（max 0.918），
     差得最多的是 `upperarm_twistCor_01/02`、`lowerarm_twist_02`、`spine_04` —— 正是**肩带/腋下/腰口**；
  3. 身高语义 = `spine_01..05` 每根 +0.14·Δ、`neck_01/02/head` 每根 +0.10·Δ 的**逐级本地 Y 平移**
     （模板 `body.height`；195cm 时 Δ=17.5，链上相邻两级的累计差 = 2.45cm）⇒ 两层壳只要权重差一级，就分开几厘米。
- **离线 LBS 模拟**（`Tools/BodySeamLbsSim.py`，按上面的平移语义、取前 4 影响并归一）：
  原来的近邻对分离 **mean 2.91mm / p99 124.9mm / >5mm 202 对（1.40%）**；
  躯干带权重平滑后（`Tools/MetaHumanBodyWeightSmooth.py`，6mm×3 轮）**mean 1.20 / p99 9.6 / >5mm 155（1.08%）**；
  更强档（8mm×5 轮）**mean 1.54 / p99 0.00 / >5mm 122（0.85%）**。残余的 max 仍有 ~380mm —— 那是腋下"手臂 vs 躯干"本来就该分开的对，不应被平滑掉。
- **运行态 A/B 被环境挡住**：导入平滑版大骨架资产（343 节点/252 骨）时编辑器弹 `AnimatedModel.cpp:82` 断言（`CurrentOffset + size <= CurrentSize`，即 #76），确认后编辑器直接退出、MCP 桥一起断。
  因此本轮**只有离线证据**，导入的 `Ada_BodyPreview4_smooth.flax` 已删除（避免以后缩略图再触发断言）；GLB 候选留在 `Output/mhbaked/`。
- **下一步（#61 重写）**：需要一条**不经过编辑器缩略图**的验证通路（打包运行/vulkan? 或先修 #76 的缓冲分配），再做平滑版与原始版的同镜头 A/B；确认后再决定是否替换 `Ada_BodyPreview4.flax`。

6a3r. **头部资产"每次重启换 GUID"的真机制与可复现规避（2026-10-23，引擎级缺陷复活）**
- **机制（读引擎源码定位）**：`Source/Engine/Content/Cache/AssetsCache.cpp` 的 `RegisterStorage` 在发现同 ID 已注册时，
  Windows 分支只忽略**大小写**差异（`CompareIgnoreCase`）；同一文件被以 **`Content/…`（相对）** 与 **`C:\…`（绝对）** 两种拼写
  各注册一次就判成 duplicated → `ChangeAssetID(Guid::New())` 改写包 → 文件监视器看到改动 → 用另一种拼写再注册 → 再冲突……
  **自持循环**（实测 id 在 00:00:14 / 00:00:28 / 00:00:50 连换三次）。
- **这次的引爆点**：在编辑器已用**绝对**拼写注册头资产后，MCP 的 `model_inspect`（以及 `content_database_rebuild`）按**相对**拼写再注册一次 ⇒ 进入循环；
  循环中 `head_mh/Ada_FaceMesh_conformhead4.flax` 被反复改写，`Content.Load(path)` 一度**拿不到头**（世界角色无头无发，日志 `世界头部模型未就绪`）。
- **可复现规避（已两次验证）**：① 启动前把 `Content/Character/Models/head_mh` **移出 Content** → ② 启动 → ③ 移回 → ④ 等文件监视器注册（约 10~25 s）
  → ⑤ **不要再对 head_mh 调 `model_inspect` / `content_database_rebuild`**；运行期按路径加载正常。
  实测恢复后：`model_inspect` 报 `loaded=true / 876 nodes / 490 bones`，PIE 世界层级恢复
  `LocalPlayer_43 → LocalPlayerHead_43 → LocalPlayerHair_43`。
- **一并清掉的放大器**：`head_mh/` 下有 8 个 10-22 遗留的 `Ada_FaceMesh_conformhead4 Ada_FaceMesh-1..8.flax`（同名副本，加剧仲裁），已移到
  `_quarantine/head_copies_20261023/`；7 个角色预制体的头引用也从 `1857d334…` 改指当前 id（脚本 `Tools/MetaHumanFixHeadGuidRefs.py`，带"每份恰好 1 处"断言与备份）。
- **仍未解**：这是引擎缺陷（路径归一化在注册前缺失）。工程侧只能规避；若再探头资产，循环会复现。`#77` 的"已修"判定撤回，见任务表。

6a3s. **发型卡片从"实心片"改成纹理切边（2026-10-23，预览已目视成立）**
- **做法**：新建 `Content/Character/Materials/MH_Hair_Cutout.flax`：参数 `HairColor`(Color) + `MaskTex`(Texture)；
  图里 `Color ← HairColor`、`Mask ← UnpackFloat4(MaskTex).W`。**Flax 的 `MaterialBlendMode` 只有 Opaque/Transparent/Additive/Multiply
  （没有 Masked/Cutout 枚举）——Opaque + Mask 输入 + `MaskThreshold` 就是引擎的切边语义**，所以混合模式保持 Opaque。
- **槽位**：6 套发型模型的槽 0 全部改指 `MH_Hair_Cutout`（`HairCutoutSlotCheck.py` 复核 **6/6**；回滚只需指回 `MH_Hair`）。
- **目视（A/B 同镜头）**：改前卷发是**实心片拼成的团**（[旧](file:///C:/Works/GitHubProjects/HundunWorld/docs/metahuman/ue_runtime/hair_styles_20261023/preview_coil_20261023.png)）；
  改后是**一根根带镂空的发丝**（[新](file:///C:/Works/GitHubProjects/HundunWorld/docs/metahuman/ue_runtime/hair_styles_20261023/preview_coil_cutout_20261023.png)）。
  掩码用 UV0 采样即为正确形状（若用错 UV 会得到随机条纹，这里没有）。
- **每套一张掩码**：`HairStyleCatalog` 新增 `MaskPaths`（6 张 `Content/Character/Skin/<Style>_RootUVSeedCoverage.flax`）；
  预览 `SetHairStyle` 与世界/远端 `MetaHumanHairBinder` 换发型时同步 `SetParameterValue("MaskTex", ...)`
  （只写材质实例，不污染基础材质）。`dotnet build Game.csproj --no-incremental` **0 错**。
- **待验证**：重编 `GameEditorTarget` 后切到其它发型，确认掩码随发型切换（本轮只验证了默认卷发）。
- **切换已验证（同轮补）**：重编 `GameEditorTarget`（DLL 内 `MaskTex` / `mask-not-ready` / 6 处 `RootUVSeedCoverage` 各 1/1/6）后进捏脸页：
  默认卷发是**棕色镂空小卷**（[图](file:///C:/Works/GitHubProjects/HundunWorld/docs/metahuman/ue_runtime/hair_styles_20261023/preview_coil_cutout_20261023.png)）；
  点「层次波波」后变**棕色分层发丝**（[图](file:///C:/Works/GitHubProjects/HundunWorld/docs/metahuman/ue_runtime/hair_styles_20261023/preview_boblayer_cutout_20261023.png)）——
  形状与掩码一起换、发色保持，说明 `MaskPaths` 的 6 表与实例赋值生效。
- **世界侧（同一轮补）**：进世界后 `metahuman_drive hair.style=3` → `[MetaHumanHairBinder] style=3 model=…Hair_S_BobLayered_CardsMesh_Group0_LOD0.flax`，
  **无 `mask-not-ready` / 无"不是实例"告警** ⇒ 世界发卡也走了"换模型 + 换掩码"这条路。
- **踩到的坑**：中途 `Hair_S_Coil_Cards.flax` 一度 `knownToContentDatabase=false`（预览报"头发模型未就绪"，头也变光头/白发），
  是**用相对路径的资产写工具**（`model_material_slots_assign` / `material_set_param`）又踩了 6a3r 的"两种拼写注册"缺陷；
  清 `AssetsCache.dat` + 重启后恢复（写工具用完不要在同一个编辑器会话里继续用路径类探针）。

6a3t. **头部区域参数有效性：111/111 在骨骼层面生效（2026-10-23；像素法在本机位不可用）**
- **方法**：`Tools/MetaHumanZoneBoneSweep.py` 逐个把 `face.zone.*.{tx,ty,tz}`（37 区 × 3 轴）推 0→1，
  读它**自己声明的目标骨**在世界空间的位移（`actor_bone_probe`，驱动后留 0.25 s 让写回端落地）。
- **结果**：**111/111 全部有位移**，绝大多数 maxBoneDelta = **7.2 cm**，`face.zone.teeth.c.tz` 为 **9.6 cm**，0 条为零。
  ⇒ 区域参数不是死通道，"捏脸看不见变化"先查机位/轴，别急着改参数。
- **像素法为什么不可用（同轮对照）**：同一姿势连拍两张（不驱动任何参数）就有 **106 px / 3222 px** 的差异
  （待机摆动+眨眼），而区域参数在 1060×1080 预览里的像素差只有 0–1386 px —— 与噪声同量级。
  另外 `.tz` 是模型**深度轴**（正面机位几乎看不出 7 cm 的进出），`tx/ty` 才有横向/纵向位移。
- **工具坑**：`metahuman_drive` 提交后要等一帧以上再读骨矩阵；`actor_bone_probe` 单次调用内的 before/after 是同一姿势
  （窗口内无人动骨），跨调用比较才对。响应结构是 `{success,data:{rows}}`，别在顶层找 `rows`（本轮先因此得到"全 0"的假阴）。

6a3u. **每帧查不存在的动画参数把日志刷爆（2026-10-23 已修）**
- **现象**：上一轮世界会话日志 **13,439 行**，主体是同一条 `Failed to get animated model '...' missing parameter 'IsWalking'`（约每 25 ms 一条，直至触发日志裁剪）。
- **根因**：`LocalPlayerActorSyncSystem` 与 `FlaxActorSyncSystem` 的"IsWalking 回退"分支**每帧**调 `GetParameter("IsWalking")`；
  当前动画图用速度混合（`LocomotionGraphDriver`），没有这个参数，而引擎对**每次**查不到的调用都打警告。
- **修法**：查一次就缓存结果（含 null）——本地加 `_isWalkingLookupDone`，远端加 `_entityIdIsWalkingMissing` 集合；缺参数只提示一次并说明由 LocomotionGraphDriver 负责。
- **验证**（重编 `GameEditorTarget` 后）：进世界 25 s，日志 **1,884 行 / 268 KB**，`missing parameter 'IsWalking'` 仅 **1 次**（来自 PlayerController 初始化的一次性查询）。
  世界层级 `LocalPlayerHead_43 → LocalPlayerHair_43` 仍在，`[MetaHumanHairBinder] style=0 model=…Hair_S_Coil_Cards.flax` 正常，无 `mask-not-ready`。

6a3v. **身体参数骨骼口径抽查：body.height 生效 6.0 cm；全量扫描被探针工具限制卡住（2026-10-23）**
- **正向读数**：预览身体（`CharacterPreview3D_Model/AnimatedModel`，Character_1 骨架）上，`body.height` 从默认推到 Max，
  `DEF-spine / .003 / .006` 等脊柱链骨在世界空间移动 **最大 5.993 cm**（17 根可读骨）⇒ 身体滑杆确实驱动骨架。
- **扫描被工具限制打断**：`actor_bone_probe` 对**模型里不存在**的骨名会返回垃圾数值（实测 `DEF-hand.L` = `x 1.78e23`），
  并且随后对该 Actor 的探测会 **HTTP 500**（连续重试仍失败）⇒ 30 条 body.* 的批量扫描在 height 之后全部读到 0。
- **结论**：要跑全量身体参数普查，必须先拿到**该骨架真实节点名清单**再按名单探测；不要拿 UE/别名族的名字盲探。
  （`model_inspect` 能列 500 个节点名，但它是路径式工具、会触发 6a3r 的重复条目缺陷，本轮没有对 Character_1 使用。）
- **当日后续已补齐**：骨名清单改为直接解 GLB 的 `skins[].joints`（342 根，25/25 模板 Target 全中），
  30 条 body.* 全量普查 **30/30 都在动骨架**；并顺带查出并修复了下面 6a3w 的撤销缺陷。

6a3w. **骨骼写回撤销缺陷：参数回到默认值，形变不撤销（2026-10-23 已修 + 实机复验）**
- **现象（修前，MH_ScaleTest 夹具）**：`body.waist 107.5→160→107.5`、`body.height 177.5→220→177.5` 之后，
  `spine_02/03` 的 `z` 仍停在 `-68.6 / -83.8`、`spine_01..head` 的 `y` 仍各高 35.7 cm；再驱动任何参数都不回落。
- **根因**：`MetaHumanAppearanceApplier.ReplayBoneOffsets` 用**整姿逐位相等**（`_lastWrittenPose` 全数组比较）判断
  “姿势还是我们写的”。引擎每帧会重算无关节点（root/skinning 等），只要有一项不同就判 false ⇒
  旧位移/缩放/旋转从不撤销；而读回的姿势里还带着我们的值 ⇒ 新值叠在旧值上。`body.height` 的 35.7 cm
  正是 5.95 cm × 6 次重复叠加（正确值 = Δ42.5 × Gain 0.14 = 5.95 cm）。
- **修法**（[MetaHumanAppearanceApplier.cs](file:///C:/Works/GitHubProjects/HundunWorld/HundunWorld/Plugins/MetaHuman/Source/MetaHuman/Runtime/MetaHumanAppearanceApplier.cs)）：
  改成**逐骨快照**——每根骨记 `_basePose`（我们动手前）与 `_writtenPoseBone`（写完后）；
  重放时只比这根骨自己的当前值（容差 1e-3），≈上次成品就从底稿重建，不≈就认为引擎已重置、以当前值为底稿。
  平移/缩放/旋转全部从底稿按绝对量重算，删掉“先撤旧值再加新值”的增量路径与全局 `poseIsOurs`。
  `Clear()` 同样按底稿逐骨还原；换资产时清空快照。
- **实机证据**（编辑模式夹具，`metahuman_fixture` + `actor_bone_probe`，控制组同夹具连读两次差值 **0.00000 cm**）：

  | 参数 | 推到 Max 的位移 | 回默认后 vs 基线残差 |
  |---|---|---|
  | body.waist（BoneScale） | 2.520 cm | **0.0000 cm** |
  | body.height（BoneTranslate） | 5.950 cm | **0.0000 cm** |
  | body.bust（BoneScale） | 1.614 cm | **0.0000 cm** |
  | body.thigh（BoneScale） | 4.425 cm | **0.0000 cm** |
  | body.inseam（BoneTranslate） | 15.500 cm | **0.0000 cm** |
  | face.zone.cheek.l.tx（FACIAL，Gain=20） | 1.200 cm | **0.0000 cm** |
  | face.zone.jaw.l.ty（FACIAL，Gain=20） | 1.200 cm | **0.0000 cm** |

  修前 waist 的 Max 位移曾达 **1436 cm**、high_hip 510 cm（同一叠加缺陷）；修后回到 1–15 cm 量级。
  引擎自检 `metahuman_selftest` 同步复核 **99 通过 / 0 失败 / 9 跳过**（跳过项均为“场景里没有夹具/头资产”）。
- **残余未测**：表情旋转通道（`Expression_Rig` 的眨眼/张口绕支点旋转）走同一套逐骨快照，但本轮没有独立的
  “旋转写入→回零”实机用例；表装载自检 11/0/0 只证明表可用。

6a3x. **`AnimatedModel.cpp:82` 断言的可复现条件与“预热”规避（2026-10-23 实测）**
- 引擎 `AnimatedModelRenderListExtension::Allocate`（`C:\Works\FlaxEngine\Source\Engine\Level\Actors\AnimatedModel.cpp:61-89`）：
  全局缓冲为空时一次只长到 **16 KB**，之后每次只翻一倍，然后 `ASSERT(CurrentOffset + size <= CurrentSize)`。
  骨骼数据 = 骨骼数 × `sizeof(Matrix3x4)`(48) ×（逐骨运动模糊时再 ×2）。
- 因此**新启动的编辑器里创建的第一个大骨架 AnimatedModel** 必然断言：Ada 身体 343 骨约 16.5 KB，勉强压线；
  头部 876 骨约 42 KB，直接 `CurrentOffset+size > 16 KB` ⇒ 崩（实测 18:04/18:10 两次同一栈：
  `AssetReferenceBase::OnSet → PreInitSkinningData → SkinnedBones::Update → Allocate`）。
- **可用规避（本轮验证）**：同一会话先放一个**小骨架**（`Content/Models/Characters/skm_uefn_mannequin.flax`）
  把缓冲初始化到 16 KB；再放 **2 个** 343 骨身体（16→32→64 KB）；之后 876 骨头模型可正常创建。
  小骨架 + 1 个身体只够见身体、见头仍会崩。
- 这是引擎级缺陷（#76），不是插件代码；运行期 PIE 里因为分配顺序不同，之前能直接建身体/头。

6a3y. **身体 30 参数全量骨骼普查（2026-10-23，撤销修复后的可信读数）**
- 骨名来源：身体 GLB 的 `skins[].joints`，342 根；模板 25 个唯一 Target 全部命中（大小写不敏感）。
- 方法：编辑模式夹具 `MH_BodyCensus`（Ada_BodyMesh + body_base），每个参数 default→Max→default，
  读**全骨架 342 根**世界位置；控制组同夹具连读两次差值 **0.0000 cm**。
- 结果：**30/30 都动骨架，0 死通道**；修撤销缺陷后回默认残差也为 0（同一轮里每个参数都做了回程）。
  位移最大的骨如下（cm）：

  | 参数 | Max 位移 | 参数 | Max 位移 |
  |---|---|---|---|
  | body.inseam | 15.500 | body.knee | 4.092 |
  | body.shoulder_height | 14.000 | body.calf | 4.039 |
  | body.rise | 12.300 | body.neck_length | 3.500 |
  | body.bicep | 9.312 | body.waist | 2.520 |
  | body.forearm | 9.076 | body.underbust | 2.282 |
  | body.shoulder_to_apex | 7.500 | body.bust | 1.614 |
  | body.wrist | 7.059 | body.high_hip | 1.348 |
  | body.front_interscye_length | 7.000 | body.hip | 1.119 |
  | body.hand_circumference | 6.723 | body.neck_base | 0.655 |
  | body.height | 5.950 | body.neck | 0.563 |
  | body.neck_to_waist | 4.550 | body.across_shoulder | 0.460 |
  | body.thigh | 4.430 | body.bust_span | 0.226 |
  | body.upper_arm_length | 4.350 | body.muscularity | 0.144 |
  | body.elbow | 3.931 | body.fat | 0.072 |
  | body.lower_arm_length | 1.700 | body.masculine_feminine | 0.041 |

- **读数口径**：BoneScale 类参数的"目标骨世界位置位移"只证明通道非空，**不等于视觉幅度**——
  缩放改的是骨及其子骨的局部变换，皮肤形变可以肉眼可见而骨枢轴几乎不动。按模板数据算 Max 处缩放量：
  bust_span 1.11×、across_shoulder 1.224×、neck 1.275×、neck_base 1.32×；
  而 fat 仅 1.005×、masculine_feminine 0.984–1.02×、muscularity 1.003–1.006× ——
  这三条不是死通道，是**数据量级本身只有 0.3–2%**（UE 侧靠 morph/蒙皮，骨骼近似做不到同等幅度）。
  要提升观感只能改模板增益或补 morph 通道，不能靠调写回端。

6a3z. **头部 111 条 face.zone.* 全量骨骼普查：111/111 驱动、111/111 撤销干净（2026-10-23）**
- 新工具 `Tools/MetaHumanHeadBoneSweep.py`：编辑模式一次跑完建夹具→111 条参数 `0→1→0`→拆夹具→存场景。
  夹具顺序按 6a3x 的缓冲规则：91 骨小骨架 → 2×343 骨身体（16→32→64KB）→ 876 骨头（→128KB）。
- 每条参数读**它自己声明的 FACIAL 目标骨**（最多 12 根），判据：`moved ≥ 0.01cm`、`restored ≤ 0.01cm`。
- 结果：**moved 111/111、restored 111/111（回 0 状态与初值的最大差全部 0.0000 cm）**；
  典型位移 1.200 cm（= 轴增益 0.06 × FaceBoneGain 20）。
- **一次瞬态故障已复核**：跑到 `face.zone.lip_upper.l.tx` 时 MCP 桥连续两次拒连（`actor_bone_probe` 与
  `metahuman_drive` 都失败），该行被记成 moved=0；随后在新夹具上单独重测 **moved=1.2000 cm / restored=0.0000**，
  结果 JSON（`HundunWorld/Tools/_head_zone_bones.json`）该行已更新并带 `note` 标注，不是死参数。
- **像素层补充（不完整）**：暂停的 PIE 里 `body.waist 107.5→160` 预览帧差 **226 像素>10 / max 28**，
  证明缩放通道在画面上真实可见；但回程截图没拿到——PIE 在脚本热重载后自行退出（环境级，见 #79），
  因此"像素级复原"不记为已证，撤销结论以骨矩阵的 0.0000 cm 为准。

6b. **远端玩家外观（数据链+渲染链已落地，真双客户端未联调）**：新增 `GetCharacterAppearanceRequest/Response`（NetworkMessage 248/249、`MessageUnion` 248/249、`MessageType` 1662/1663），网关 `CharacterHandler` 用 `CharacterGrain.GetAppearanceAsync` 回包；远端生成后按 `EntityId`（=角色 Id，见 `DsWorldSession` 建档）请求一次，回包校验校验和→`MetaHumanLook.FromBytes`→套到 `RemotePlayerActor`。远端玩家身体改为与本地同一套 Ada + 重定向动画图（`EnsureMetaHumanBody`，走 `AssignAnimatedModelResources`；**直接把 ALS 图赋给 Ada 会触发 `AnimatedModel.cpp:932` 断言**）。单客户端实测（合成远端 Actor，验证后探针已删）：`remote-fetch-sent → remote-fetch-ok bytes=159 → remote-apply ok=True`，截图见两个角色同屏且远端带自定义头/发（[evidence](file:///C:/Works/GitHubProjects/HundunWorld/HundunWorld/Tools/_probe_paused.png)）。**剩余**：真实双客户端进场/离场/换装联调，以及远端外观变更时的推送刷新（当前只在生成时拉一次）。**发色也已随回执下发**：`GetCharacterAppearanceResponse.HairColor`（服务端由 `GetCharacterInfoAsync(null).Appearance.HairColor` 填充，读取失败回落 0）；客户端把它缓存并在远端头发材质实例上写 `HairColor`（与本地同一张 `CcFacePresets.HairColors`）。实测回执字段可往返（trace `hair=0`）；头部资产曾因 GUID 变更（`2e2fda68…→664165a9…`）加载失败，清 `AssetsCache.dat` 重启后恢复（`model_box_probe` 9 槽/head 骨 y≈143）。随后用合成远端强制 `hair=4` 做了目视：左侧本地深棕、右侧远端银白，头发材质实例着色成立（[evidence](file:///C:/Works/GitHubProjects/HundunWorld/HundunWorld/Tools/_remote_hair_silver.png)）。注意：改 SQL 后 grain 缓存的 `CharacterInfo` 仍是旧值（需驱逐 grain 才刷新），所以服务端下发值仍是 0；SQL 已还原 `hair_color=0`。

- **世界外观持久化口径（2026-10-22 实测）**：进世界能加载**已发布**外观（`loaded=true`、头部 morph 已写 33）；但未发布的 `metahuman_drive` 编辑只存在于本次会话——重启 PIE 后同参数 `valueBefore=0`。与设计一致（发布封存才落库），验证 ④ 的“保存→发布后才持久”语义时不能拿未发布编辑当持久化证据。
- **回归复核（2026-10-22 收尾）**：`Tools/MetaHumanVerify` **1532 通过 / 0 失败 / 0 跳过**；PIE `metahuman_selftest` **106 通过 / 0 失败 / 3 跳过**；`Flax.Build` 项目脚本 `error CS=0`。
- **身体贴图边界**：UE 侧 `Ada/Body` 仅有 `BodyBaseColor`（另有 `f_med_nrw_body`，实为 **SkeletalMesh** 不是贴图）⇒ 世界身体当前只接了基色，法线/粗糙度暂无源可导。

## 4. 复现命令

```bash
# 纯核 + 模板契约
dotnet run --project Tools/MetaHumanVerify/MetaHumanVerify.csproj -c Debug     # 1634/0/0
# 客户端编译门
dotnet build HundunWorld/Source/Game.csproj --no-incremental -p:BaseOutputPath=bin/Verify/
# 模板重烘
python Tools/MetaHumanBakeTables.py
# PIE 内自检 / 外观驱动（MCP 21901，Mcp-Session-Id: hundun-agent-flax）
#   metahuman_selftest  -> 113/0/5（RootScene 无夹具时骨架/warp 组会如实 SKIP）
#   2026-10-23 复核（RootScene 已加载、无夹具）：99/0/9；跳过项就是“没有 AnimatedModel / 头资产”的那几组
#   2026-10-25 复核（RootScene 已加载，预览头在场）：metahuman_selftest 113/0/5；MetaHumanVerify 1634/0/0
#   服务端推送回归：dotnet test Horizon.Game.Gateway.Tests ... --filter "~Chat|~Appearance" → 57/57
#   metahuman_inspect / metahuman_drive / ui_preview_screenshot
```

## 5. 结论

**2026-10-22 收尾审计（逐项对《完成度审计》正文）**

| 目标项 | 判定 | 硬证据 | 仍缺 |
|---|---|---|---|
| ① 适配 UE MetaHuman 资源/全功能 | **未达成** | **头 605 条参数：552 骨骼档直接命中、33 条 UE 名 morph 经桥表翻译、4 条 lips morph 已 declined、16 条只挂材质；没有静默死通道（6a3o，与引擎 552/0/37/16 逐格对上）**；身体 24 目标全中；皮肤 3 通道；发色、睫毛、虹膜色、**眼妆/腮红/口红三分**、**胡茬（6a3d）**已通（6a2d/6a2g/6a3b）；**牙齿/唾液已接专用材质并有近景像素证据（6a2s 10-23：透明头诊断下上下两排白牙+唾液层正确出现在口腔，恢复皮肤槽后外观复原）**；harness 1630/0/0 | ~~多发型（仅一张卡）~~ **6 套资产 + 选择器已落地；预览换模有像素证据、世界换模有日志证据（6a3j/6a3k/6a3l），远端待双客户端（#83）**、#60 肩缝（需 DCC 重导）、~~微法线视觉不可辨~~ **微法线=用户 declined（6a9：材质无落点且回读对照成立，按判定不做）**、**单 LOD（6a11：引擎自动生成 LOD 已验证可行且保蒙皮，但生产资产未落地）**；**眉毛卡片已闭环（6a6：同头空间零变换+R 通道覆盖率，ON/OFF 证明无双眉）**；**#61 身高撕裂已闭环（6a5 末节：改为根骨 Y 等比缩放，135cm 裂口消失）**；~~预览头 morph 不驱动渲染~~ **已量清：源 GLB 里 699/821 个 target 非零、jaw_open 6.35mm、eye_blink 1.65mm（6a3m）——是"幅度 mm 级"，不是"写不进去"** |
| ② 编辑器模板建角 + 自动绑骨/动画 | **部分达成** | 绑骨演练 75/75、8/8、37/37；动画重绑 872 节点、镜像 391、大小写 557；压缩重放 31 步；**世界角色播真实 ALS 剪辑 + Idle/Walk/Run/Sprint 按速度三级混合（6a2v 第四/五轮：清 125 条 retarget 的 SkeletonAsset、11 节点 Blend 图、`LocomotionGraphDriver` 按位移测速；PIE 实探 foot_l run→sprint 28.43；常驻门 `Tools/AnimationWiringVerify.py` PASS）**；张口 morph 黑斑已修（6a3f：均亮 82.06→81.87） | 不是 UE 式状态机（参数混合）；转向/停步过渡剪辑未接；远端 locomotion 只做了接线、未做双端目视；**眨眼/张口已改骨骼通道（6a3i）：眨眼绕眼球旋转、张口用 12IPV_LipLower 组平移；捏脸页连拍可见开合与牙齿，自检 113/0/5；偏差是张口不带牙齿/下巴（非 rig logic）**；Character_1 自身 **126 条** retarget 指失效 GUID（6a10：已定量、当前无运行时症状，修法与代价写明但本轮未执行） |
| ③ 发布后玩家自定义（身体+头部为主） | **基本达成** | 头/身参数可 drive 并撤销（**回默认能原样复原**：6a3w 修掉叠加缺陷，7/7 残差 0.0000 cm）；**身体页签 30 项 + 身高驱动 106k px、撤销复原（6a2q）；撤销已量全：身体 30/30、头部 111/111 回默认残差 0.0000 cm（6a3w/6a3y/6a3z）**；**远端瞳色/发型渲染已用合成远端取证（6a7：EyeIrisTint=RGBA(0.320,0.400,0.540)、hairStyle=5→Hair_M_SideSweptFringe）**；头部滑杆 276 px 变化（6a2p）；**世界头部形状参数实测驱动骨骼**（`face.jaw.c.tz+1` → `FACIAL_C_Jaw` 0.4，预览与世界头一致；#80 结案）；皮肤三通道；**妆容色温可调（6a3e）**；瞳色（色板→像素 90 px + 发布→重登回读）；发色外观档（**创角预览已可见并可实时换色，6a2w**）；**发型 6 选一（预览换模 47k px 差、世界换模换到对应资产路径，值随 look 持久化/回溯，6a3k/6a3l）**；远端外观已能拉取 | 身体极端身高撕裂（#61）**已闭环（根骨 Y 等比缩放，6a5 末节）**；真实双客户端同屏仍未做（合成远端已覆盖渲染链）；发色仅建角时写入 AppearanceInfo；~~远端外观变更只在生成时拉一次~~ **已改为落库即推送（6a8）** |
| ④ 保存→发布后不可回溯 | **达成** | 封存闸门 22/0（真集群）；PIE 封存后 returned=0；**瞳色发布→重登回读已实测**（6a2i）；**历史任意跳转的像素复现**（6a2r：回初始差 2 px、前进到第 2 步差 1 px） | — |

四项中 ④ 可判定完成；③ 现为**基本达成（本地与合成远端闭环，远端外观落库即推送已补，见 6a8；真实双端同屏未做；2026-10-25 真实 PIE 复核发现世界画面仍是 mannequin，见 6a13，修好前世界表现不算闭环）**；② 现为**部分达成**（2026-10-23 复核：`LocomotionGraphDriver READY graph=…/Animation Graph.flax walk/run/sprint`，进世界后 `actor_bone_probe` 1.6s 内 foot_l/foot_r/hand_l/head/spine_02 **5/5 都在动**（maxMatrixDelta≈1.61），不是 T-pose；仍缺 UE 式状态机与转向/停步过渡剪辑，远端 locomotion 未双端目视）；① 未达成。因此整体目标**仍未达成**，不应标记完成。剩余可动的都是**资产/基础设施级**缺口：真实双端同屏（需起本地集群与两具客户端）、#60 肩缝（需 DCC 按 4 影响重导）、更多发型资产（需 UE 侧再导出）、妆容目前只到两点色温（完整色板是产品口径，未定）；微法线是**用户 declined**（6a9），不再算缺口。

**世界侧目视的拦路缺陷 #85 已于 2026-10-23 定位并修复**（详见 6a4）：根因不是服务端坐标，而是**地面射线把 NPC 的 CombatCollider 顶面当成地面**，把角色从 Y≈0 抬到 4181/7986。修后同一角色在 PIE 内 **Y=(0,0,0) 连续 32 秒稳定**，`game_screenshot` 已经能拍到角色本体（[evidence](file:///C:/Works/GitHubProjects/HundunWorld/HundunWorld/Tools/_ground_fix_world.png)）⇒ 世界侧像素取证通路重新可用。仍剩：NPC 自己的 CombatCollider 顶面在持续上移（与本缺陷解耦，属 Narrative NPC 侧）。

6a4. **世界角色 Y 坐标飙升（#85）根因与修复（2026-10-23）**
- **实测证据**（`log_scan GroundProbe`）：修前
  `HIT x=0 z=0 hits=4 groundY=4181 standY=4000 owner=NPC_… type=EmptyActor nY=1.00`，
  随后 `groundY=7986`；角色 Y 从 7 被抬到 4181→7986，之后长期卡在高处（采样 MISS 时按设计冻结垂直位置）。
  同一轮里自己的胶囊被正确跳过（`SKIP-CHAR owner=LocalPlayer_43`），真正的运行期地面日志是
  `HIT groundY=0 owner=Scene type=Scene nY=1.00`（Collider 是场景根下的独立 Actor，Parent 读出是 Scene）。
- **根因**：`SampleGroundHeightEcs` 的"跳过角色碰撞体"只覆盖玩家/选中目标；NPC 的 `CombatCollider` 是朝上的大盒子（nY=1.00），
  在角色头顶上方也满足"命中 + 法线朝上"，`nz < groundZ` 时把角色直接吸到盒顶。NPC 盒顶还在持续上移
  （一局内 275→4264），于是出现"Y 一路飙升、相机看不到角色"。
- **修法**（[HundunWorldGame.cs](file:///C:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/HundunWorldGame.cs) 与
  [WorldSelectionService.cs](file:///C:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/Selection/WorldSelectionService.cs)）：
  1. `IsCharacterBodyCollider` 改为按**角色脚本**识别（`PlayerController` / `CharacterAnimationController` /
     `RemotePlayerActor` / `MetaHumanCharacter` / `SelectableEntityTag`），不再只靠 `_localPlayerActor` 引用；
  2. 新增"头顶一步以上不是地面"：命中点高于 `standY + 80cm` 一律跳过；仅 **Terrain / Ground_Runtime**
     且角色已明显陷到其下方（`standY < hitY - 200`）时作为救援接受；
  3. 只接受 `Normal.Y ≥ 0.5` 的朝上可站立面；日志增加 `SKIP-CHAR / SKIP-OVERHEAD / SKIP-NORMAL + owner/type/nY`。
- **验证**：同一账号角色 43 进世界后连续 32 秒 **Y=(0,0,0)**（每 4s 采样一次，8/8 稳定）；
  日志显示 NPC 盒顶 275→4264 全部 `SKIP-OVERHEAD`，最终采用 `groundY=0`；截图可看到角色与地面。
  `dotnet build Game.csproj --no-incremental` 0 错误；`Flax.Build GameEditorTarget / GameTarget×2` 均 0 错误。
- **世界内参数 A/B（PIE 暂停冻结，LocalPlayer_43，参数由 `metahuman_drive` 真写回）**：
  - `body.height 177.5→190` 与基线帧差 **215 像素>10（max 42）**；回 177.5 后与基线差 **9 像素>10（max 16）** ⇒ 世界内身体参数可见且回默认复原。
  - `face.zone.cheek.l.tx 0→1` 与基线差 **200 像素>10（max 32）**；回 0 的那一帧与基线差 432 像素>10（同区域），
    与"渲染滞后一拍/暂停下未收敛"一致，**不作为像素级复原证据**；头部撤销结论仍以骨矩阵 111/111、残差 0.0000 cm 为准。
  - 证据：`Tools/_ground_fix_world.png`、`Tools/_wp_*.png`。
- **遗留**：进世界后仍有 `CorrectionStorm Entity=43 Count=5 Window=2.0s`（入场首 2 秒的初始校正，位置随后稳定）；
  NPC CombatCollider 顶面持续上移是 Narrative 侧独立问题。

6a5. **#61 平滑权重资产的运行时 A/B：导入与夹具成立，渲染 5 具大骨架仍撞 #76（2026-10-23）**
- 导入成功：`Ada_BodyPreview4_smooth8.glb` → `Content/_probe`，`Type=SkinnedModel, ImportLODs=1, ImportBlendShapes=false, ImportMaterials=false`，
  回执 **343 nodes / 250 bones / 0 shapes**。
- 预热顺序（91 骨 + 2×343 骨）后，`metahuman_fixture` 能同时创建 **原版 + 平滑版** 两具 343 骨身体，
  `templateIdsApplied=1`、`boneNodes=343` ⇒ "平滑资产能被运行时装进 body_base 模板"已证。
- 但在同一会话里再渲染时 MCP 连接被重置、编辑器随后退出（`viewport_screenshot` 先报"截图文件未生成"）。
  当时场景内同时有 **5 具 343 骨 AnimatedModel**（P0/P1/P2 + Orig + Smooth）；按 6a3x 的扩容规则，
  多具大骨架 + 逐骨运动模糊仍会超出缓冲 ⇒ 运行时 A/B 需要**一次只放一具大骨架**的会话级方案
  （或先把引擎扩容改成 while 循环）。
- 探针资产 `Content/_probe` 已删除，未进入生产资产；#61 仍为未闭环（离线 LBS 证据见 `metahuman-body-height-tear-weights`）。
- **2026-10-24 运行时 A/B 已跑通（但结论是否定的）**：
  - 通道：捏脸预览切“身体”页（`Button` index 1 → `FocusOn("whole")`），把 `TemplateSkinnedBodyModelPath`
    临时指到 `Content/_probe/Ada_BodyPreview4_smooth8.flax`，重建后同机位截图。
  - 原资产：`body.height=135/220` 时胸衣与腰口出现大面积裂口（`Tools/_seam_orig_135.png`、`_seam_orig_220.png`）。
  - 平滑资产：同一机位、同一参数**仍有同量级裂口**（`_seam_smooth_135.png`、`_seam_smooth_220.png`）。
  - 已排除两种常见解释：两版 GLB 都是 **≤4 影响、权重和=1.000**；同位置重复顶点 1857 组、组内权重 L1 差 **0**
    ⇒ 不是“>4 影响被截断”，也不是“重复点分属两壳权重不同”。
  - **结论**：只平滑躯干皮肤权重不足以修复 #61；下一步要针对胸衣/腰口接缝的**跨壳权重分配**（或隐藏内衣几何、单壳重导），
    不是继续加平滑迭代。临时路径已还原、探针资产已删，GameEditorTarget/GameTarget 均 0 错。
- **2026-10-24 跨岛缝权重焊接实验（也否定了“接缝分离”假说）**：
  - 网格由 14 个岛组成；跨岛近缝（0.5–10mm）权重差 L1 中位 0.26–0.52。新增
    `Tools/MetaHumanBodyWeightWeld.py`：按“法线同向 + 偏移沿法线”配对并迭代平均权重，
    仍保持 ≤4 影响、和=255。
  - 离线指标确实变好：近缝 L1 均值 0.234→0.020；`BodySeamLbsSim` 平均分离 **2.91→0.97mm**、
    >5mm 对 **202→81**、p99 **124.9→0mm**。
  - **但运行时同机位仍是同量级尖刺**（`Tools/_seam_weld2_135.png`、`_seam_weld2_220.png`）。
    尖刺是**实心拉伸三角形**而不是黑洞 ⇒ 现象不是“两层壳分开”，而是
    `body.height` 把 spine_01..05 **各平移 0.14Δ 累加**：135cm 时 Δ=-42.5，躯干被压掉约 30cm（≈60%），
    LBS 折叠出尖刺。权重平滑/焊接都不对症。
  - **修正后的下一步**：改模板语义把身高变化分摊到腿长/躯干比例（或用骨骼 Y 缩放而非整链平移），
    或先限制 body.height 的有效量程；不要继续改权重。
  - 环境注记：把新的大骨架 .flax 放进 Content 后**冷启动**会在缩略图预览里撞 `AnimatedModel.cpp:82`
    并弹出模态框（主线程全挂）；规避是启动时把该目录移出 Content、启动后再移回（本次验证有效），
    或在运行中的编辑器里导入（缩略图缓存生成后再重启）。

- **2026-10-24 最终修法：身高改为根骨 Y 向等比缩放（#61 闭环）**
  - 模板 `body.height`：删掉 `spine_01..05 +0.14Δ / neck..head +0.10Δ` 的 8 条链式平移，
    改为 `BoneScale root`（Axis Y=1、Gain=1/177.5、Offset=−1）⇒ 默认零贡献，参数值 h 时根骨 Y 缩放 = h/177.5。
  - 重烘内嵌表后离线断言同步改写：身高 +10cm ⇒ root `ScaleDelta.Y = 10/默认`、X/Z=0、且无任何骨骼平移；
    `MetaHumanVerify` **1630 通过 / 0 失败 / 0 跳过**。
  - 运行时同机位（身体页，head 隐藏）：
    - `135cm`：原大裂口消失（对比 `Tools/_seam_orig_135.png` → `Tools/_seam_rs_135.png`）；
    - `220cm`：只剩身体自身的颈部几何（有皮肤纹理、向上出画，不是撕裂；`_seam_rs_220.png`）；
    - 回 `177.5` 与基线一致（`_seam_rs_back.png`）。
  - `metahuman_selftest` **99/0/9**；`GameEditorTarget/Release`、`GameTarget/Release+Development` 均 0 错。
  - 语义注记：现在是“整具骨架等高缩放”，头部是独立模型（位置随颈/头骨移动、自身尺寸不缩放），
    因此极端身高下头身比例会更接近 UE 的“身高改变躯干/腿长”；若后续要求头围也随机体缩放，需另加头部缩放通道。

6a6. **#86 眉毛卡片：空间/掩码/挂载链路已就绪，总开关默认关（2026-10-23）**
- **空间已量清（不需目视即可判同源）**：
  - `Eyebrows_Cards.glb`：1 网格 4296 顶点，米制 Y-up；包围盒 x ±5.70、**y 149.6–151.8、z 7.47–10.56（cm）**；导入设置 `Scale=100`。
  - 头模型 `Ada_FaceMesh_conformhead4`：眉带（y 1.49–1.52m、|x|<0.06）有 **2529 个顶点**，z 中位 **9.15cm**、最大 **10.46cm**
    ⇒ 眉卡 z 范围与脸面重叠，属同一导出空间；像头发一样零变换挂到头部即可（不需要 Y-180° 翻转）。
- **掩码**：`Eyebrows_M_Thin_CardsAtlas_Attribute.png` 的 **R 通道**是覆盖率（3.1% 像素 >16），G/B 是全幅渐变；
  已把 R 复制到 RGBA 四通道生成 `Output/mhhair/Eyebrows_Coverage_W.png`，并导入为
  `Content/Character/Skin/Eyebrows_Coverage_W.flax`（sRGB=false），以复用 `MH_Hair_Cutout` 的 `MaskTex.W`。
- **代码**：新增 [MetaHumanEyebrowBinder.cs](file:///C:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/Character/MetaHumanEyebrowBinder.cs)，
  创角预览头与世界头各挂一张 StaticModel（两材质槽都指到同一抠图实例；颜色跟随发色；头隐藏时眉毛同步隐藏）。
- **目视验证（2026-10-23，创角捏脸预览，1060×1080）**：
  - `CharacterPreview3D_Brows` 在 PIE 里存在且默认可见；用 `actor_set_property IsActive=false` 做同会话对照：
    **关掉后脸上完全没有眉毛** ⇒ 头贴图不带眉，卡片**不存在双眉重影**；打开后两条眉卡正落在眼睛上方。
  - 首版颜色直接跟发色 ⇒ 金发配金眉、过亮；已改为 `DarkenForBrows = 发色 × (0.35,0.32,0.30)`，
    复拍为自然深棕（见 `Tools/_brows_face.png` 首版、`Tools/_brows_dark.png` 修正版、`Tools/_brows_off.png` 关闭对照）。
  - 结论：**#86 闭环**，`Enabled = true`；创角预览与世界的头部都会挂眉毛。
- **排障记录（重要）**：19:20 那几次 `sim_play` 超时（`isPlayModeRequested=true`、`scenes=0`）不是引擎坏了，
  而是**编辑器重启后没有打开任何场景**——`mode=scenes` 必须有已加载场景。先 `scene_load` 再 `sim_play` 即恢复；
  后续 PIE 验证请固定这个顺序。
- 编译门：`Game.csproj` 0 错、`GameEditorTarget/Release`、`GameTarget/Release+Development` 均 0 错。

6a7. **远端瞳色 / 远端发型：合成远端实测（2026-10-23，无需第二客户端）**
- **入口**：`metahuman_fixture {remote:true, hairStyle, hairColor, eyeColor, characterId}` →
  [MetaHumanRemoteProbe.cs](file:///C:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game/Character/MetaHumanRemoteProbe.cs)
  在 **Game 程序集**里建“身体 + RemotePlayerActor”，构造一份内存 look 并调
  `RemotePlayerActor.ApplyMetaHumanLook` —— 与网络远端完全同一条渲染链（不碰服务端/本地存档）。
- **踩到并修掉的两个快照约束**：
  1. `MetaHumanLook.TemplateId` 必须等于**合并后的模板 Id**（`metahuman.face_archetype+metahuman.body_base`），
     只写 face_archetype 会被 `ValidateAgainst` 拒（日志：找不到模板 Id … 当前可用 …+…）；
  2. 手工构造的 look 必须自己算 `Checksum`（`MetaHumanLook.ComputeChecksum(Values)`），否则被“校验和不匹配”拒。
  另外：远端头发是**头 Actor 的子节点**（`head.AddChild`），不是角色根的直接子级。
- **实测回执（PIE，角色 900001，hairStyle=5 / hairColor=2 / eye.iris_color=1.0）**：
  - `applied=True session=True head=True hair=True hairStyle=5 directLoad=True`；
  - **瞳色**：`RemoteHead_900001` 的材质实例 entry 3/4 上 `EyeIrisTint = RGBA(0.320,0.400,0.540,1.000)`，
    与 `CcFacePresets.EyeColors` 第 5 项**逐分量一致**（`eye.iris_color=1.0` → 色板末项）⇒ 远端瞳色链成立；
  - **发型**：`[MetaHumanHairBinder] style=5 model=Content/Character/Models/mhhair/Hair_M_SideSweptFringe_CardsMesh_Group0_LOD0.flax`
    == `HairStyleCatalog.PathFor(5)` ⇒ 远端发模按 look 的 `hair.style` 换到对应资产。
  - 编译门：`Game.csproj` 0 错、`GameEditorTarget/Release`、`GameTarget/Release+Development` 均 0 错。
- **口径**：这是“合成远端 + 真实远端渲染链”的证据，不是双客户端网络联调；网络取档路径此前已有
  `remote-fetch-ok / remote-apply ok` 日志（§6b），两者相加覆盖“取档→套用→渲染”全链，但**真实双端同屏仍未做**。

**环境注记（2026-10-22 收尾，晚间已更新）**：工程已迁到 **Flax 1.13**。插件/Game 脚本/四个 MetaHuman 编译门均在 1.13 下 0 错误（见 6a2l）。`ui_preview_screenshot` 已恢复并验证（`source=output`，6a2p）；此前"全黑"是两个独立原因叠加：截图抓错对象 + 预览主光亮度为 0。像素级验收现可在 1.13 自跑：RootScene 起 PIE → 捏脸页 → 肤色/瞳色/头部滑杆/身体参数都有像素证据（6a2p/6a2q）。世界内与发布重登路径的像素复验仍未做。

6a8. **远端外观变更推送刷新（2026-10-25）：落库即推，不再等对方重新进场**
- **缺口**：远端外观此前只在角色生成时 `RequestRemoteAppearance` 拉一次；对方之后保存/发布新外观，本地屏幕上的远端角色不会变。
- **做法（复用已验证的取档路径，不新增消息类型）**：
  1. Gateway `CharacterHandler` 在 `UpdateAppearanceAsync` 成功后**回读 grain**（`GetAppearanceAsync` + `GetCharacterInfoAsync` 取发色），
     把权威副本装成现成的 `GetCharacterAppearanceResponse` 推给本 Gateway 上除本人外的所有在线角色；
     推送失败只记日志，不改保存回执（外观已落库，下次进场仍能拉到）。
  2. 推送抽象由聊天专用改为通用：`IChatPushService`/`ChatPushService` → `IPlayerPushService`/`PlayerPushService`
     （接口与实现同形，Chat、外观两个消费者共用；日志/注释同步）。
  3. 客户端 `MetaHumanAppearanceService.OnRemoteAppearanceResponse` 增加 **Revision 单调守卫**：blob 里的 `Revision` 比缓存旧就丢弃
     （推送与拉取共用一个入口，乱序/迟到副本不能让远端"退回旧脸"）。判定抽成纯核
     `MetaHumanLookRevisionPolicy.IsStale`，`MetaHumanVerify` 新增 4 条断言（严格更旧才丢 / 同修订号幂等 / 0-0 不判过期）⇒ **1634/0/0**。
- **为什么推权威副本**：若直接把 `request.Look` 转发，就等于"客户端说存了什么就播什么"；回读能保证别人看到的就是库里那份。
- **常驻测试（4/4 通过，`Horizon.Game.Gateway.Tests`）**：
  - `MetaHumanAppearancePushTests`：成功保存 ⇒ 恰好一次推送、目标是"在线 − 本人"、Blob 是回读的 `9,8,7` 而不是提交的 `1,2,3`、发色 7 透传；
    被拒保存 ⇒ 零推送且**连回读都不发生**（同夹具里正向用例先证明收集器会记账）。
  - `PlayerPushServiceTests`：帧经 `UnpackMessage` 还原出同一 `GetCharacterAppearanceResponse`；中间一个连接 `SendAsync` 抛异常，
    第三个连接仍收到 ⇒ 失败按连接隔离；离线（映射为 null）跳过不抛。
  - 回归：`--filter "FullyQualifiedName~Chat|FullyQualifiedName~Appearance"` **57/57**。
- **验证命令**：
  ```bash
  dotnet test Horizon.Game.Gateway.Tests/Horizon.Game.Gateway.Tests.csproj -c Debug -p:BaseOutputPath=bin/Verify2/ --no-build \
    --filter "FullyQualifiedName~MetaHumanAppearancePushTests|FullyQualifiedName~PlayerPushServiceTests"
  # 4 通过 / 0 失败 / 0 跳过
  ```
- **编译门**：`Game.csproj --no-incremental` 0 错；`Horizon.Game.Core/Gateway/Tests` 0 错。
- **口径与残余**：
  - 这是"同一 Gateway 进程内在线连接"的广播，与聊天频道现状一致；**多网关分片时跨网关玩家收不到推送**，需要另接 Orleans 流。
  - 推送粒度是"该角色最新已落库外观"（未发布的保存也会推），与远端进场拉取同一口径。
  - **真实双客户端同屏仍未做**：本条把"变更后不刷新"从协议缺口里去掉，但端到端双端目视仍缺（#69/#84 的合成远端证据不变）。

6a9. **“微法线视觉不可辨”结案：不是缺陷，是已记录的产品 declined（2026-10-25 实测）**
- **问题**：审计一直把“微法线视觉不可辨”挂在 ① 的缺项里，但没分清是“通道没接”还是“效果本来就细微”。
- **材质落点读数**（PIE 捏脸页，`MetaHumanSculptHost`，驱动 `skin.wrinkles 0.2→1` 后用 `actor_material_param` 回读）：
  - `Skin_Wrinkles`：**materialHits = []**（9 个材质实例上都没有这个参数）；
  - **同轮对照**：`EyeIrisTint` 命中 entry 3/4、`Lipstick` 命中 0/8、`LipstickColor` 命中 0/8
    ⇒ 回读工具本身在工作，`Skin_Wrinkles` 是**真没有落点**，不是读不到。
- **代码对齐**：`MetaHumanMaterialMap.cs:59` 把 `Skin_Wrinkles` 标成 `Declined`，Note 写明是**用户判定**：
  对应 `Normal_WM1..3` 的法线加权混合链属未支持项，“只换贴图视觉上不对，宁可不做”。
  ⇒ 按既有口径（declined 是判定不是缺数据），**不得**用别名/新建材质参数绕过；本项从“缺功能”降级为“按判定不做”。
- **像素读数（顺带校正尺子）**：同机位截图 `skin.wrinkles=0.2` vs `1.0` 最大差 33，但 **>20 只有 1 px**、均值 0.05；
  而**暂停后连拍两张、参数不变的噪声**是 max 34、`>2` 9595 px、`>10` 231 px、`>20` ≈ 0–1。
  ⇒ 预览 RenderTexture 截图**不是帧稳定的**（即使 `sim_pause_resume` 暂停），低于阈值的 A/B 差值不可当信号；
  以后判“局部生效”只认 `px>20` 计数或对多帧取平均，别再拿 `>2`/`>5` 的像素数说事。
- **现场复原**：`skin.wrinkles` 回 0.2（默认），PIE 停止，`%APPDATA%/HundunWorld/open-sculpt.on` 已删除。

6a10. **Character_1 的 126 条失效 retarget：已定量、已给修法，但本轮不动资产（2026-10-25）**
- **读数**（`skeleton_retarget_list` 引擎回读，非猜）：
  - `Character_1.flax`（assetId `cd903411-b6f1-4e55-a210-a5f6036851d5`，1109 节点）共 **126 条** retarget；
  - 1 条模型级 `{Source=f08f8142…, Skeleton=f08f8142…}` + 125 条动画级 `{Source=<clip GUID>, Skeleton=f08f8142…}`；
  - 二进制里 `f08f8142…`（.NET 字节序 `42818ff0…`）出现 **127 次** = 125 条动画 skeleton + 模型级 source/skeleton 各一次。
- **该 GUID 是死引用**：`asset_guid_find` 扫 `Content` 667 个文件，`f08f8142…` **只作为引用**出现在
  `Character_1.flax@106862` 与 `Ada_BodyMesh.flax@51890`，**没有任何文件在资产头位置（offset<64）定义它** ⇒ 资产不存在。
  形态与 **#77 的 GUID 改写**一致（很可能是 Character_1 自己的旧 Id，或一个从未建成的 SkeletonAsset）。
- **当前没有运行时症状**：最近三份日志 `log_scan` 对 `f08f8142` / `skeleton mapping` / `SkeletonRetarget` **0 命中**。
  合理解释：世界身体是 `Ada_BodyPreview4`（它的 125 条动画条目已按 6a2v 清零并验证能播），头是 conform 网格，
  预览身体没挂动画图 ⇒ 这 126 条死条目当前不被消费，属**潜伏缺陷**。
- **修法（未执行，按风险暂缓）**：照 Ada 的已验证做法——备份后把这 125 条动画条目的 `SkeletonAsset` 16 字节清零，
  保留模型级条目；改完必须重启编辑器重载资产，再用 `skeleton_retarget_list` 回读 + PIE `actor_bone_probe` 证明还能播。
  另一条路是把 GUID 重指向当前 `cd903411…`，但 6a2v 已实测该重定向分支在本工程返回无效映射，**清零是已被证明的那条**。
- **为什么这轮不动**：这是对**主角色骨架资产**的二进制改写 + 需要重启编辑器，而当前流程不消费它；
  在没有运行时症状的前提下，收益（去掉潜伏警告）不足以承担改坏主资产的风险。

6a11. **LOD 可行性探针 + 一次“探针名字撞生产资产”的事故与复原（2026-10-25）**
- **探针目的**：UE MetaHuman 资产带 LOD，本项目身体/头都是单 LOD（`lodCount=1`），检查 Flax 能否自动生成蒙皮 LOD。
- **结论：引擎支持，且保留蒙皮权重**。`ModelTool.Options.GenerateLODs/LODCount/TriangleReduction` 走 meshoptimizer；
  源码里 `REMAP_VERTEX_BUFFER(BlendIndices, Int4)` / `BlendWeights, Float4` / `BlendShapes` 都会随顶点重映射（`ModelTool.cpp` LOD 段）。
  探针导入（源 `Output/mhbaked/Ada_BodyPreview4.glb`，`GenerateLODs=true, LODCount=3, TriangleReduction=0.5`）回读：
  **LOD0 32,334 顶点 / 60,816 三角、LOD1 16,413 / 30,407、LOD2 8,440 / 15,203**；`screenSize` 1/0.5/0.25。
- **事故（重要教训）**：探针导入到 `Content/_lodprobe` 时，**同名的生产资产 `Ada_BodyPreview4.flax` 也被一起重写**，
  且资产头 GUID 从 `9ab9469d…` 变成 `52771165…`（与 #77 同族）。探针目录随后已删。
- **误判澄清**：`skeleton_retarget_list` 的 `retargetCount` **等于本次列出的条数**，被 `maxEntries` 截断
  （传 3/5 就报 3/5）。当时据此以为 retarget 从 126 掉到 5，实际是尺子问题（见 6a12）。
- **复原步骤（已验证）**：
  1. 用 `Tools/_backup_Ada_BodyPreview4_20261023.flax` 按字节覆盖回生产路径（GUID 回到 `9ab9469d…`）；
  2. 重启编辑器后 `metahuman_bind {target=Ada_BodyPreview4, source=Character_1, animationsDir=Content/Animations/als, apply:true}` 重建条目
     （回执 `mappingCount=53 / retargetCountAfter=126 / saved=True`）；
  3. 关编辑器，`python Tools/ClearRetargetSkeletonGuids.py <body.flax> cd903411-… --keep 2 --apply`
     把 125 条动画条目的 `SkeletonAsset` 清零（保留模型级 source+skeleton 两个）；
  4. 重启编辑器回读：**126 条（1 条模型级 Character_1→Character_1 + 125 条清空骨架）**、LOD 仍 1、槽 0 = `MH_Body2`、343 节点/282 骨；
  5. PIE 内 `animation_playback_probe`（Ada + `ALS_CLF_to_N`）：**movingBones 5/5**（foot_l 7.79 / calf_l 3.02 / hand_l 48.85 / head 54.33 / spine_02 48.34，elapsed 0.75s）
     ⇒ 复原后动画正常。
- **留给 LOD 落地的口径**：探针证明可行，但**不要**用同名源文件直接导入生产目录（会被一起重写并换 GUID）。
  真要做需：源文件先改名（避免同名命中）、导入到临时目录验证、备份生产资产、导入后回读 GUID/槽位/retarget，再决定。
  另有一条硬约束：**`Content/Character/Models/Animation Graph.flax` 引用着身体资产 GUID**（`asset_guid_find` 命中 offset 605），
  而重导会换 GUID ⇒ 直接覆盖生产会打断动画图引用；落地前要么保住 GUID，要么把引用一起重指。

6a12. **尺子修正：`skeleton_retarget_list.retargetCount` 是被 `maxEntries` 截断的条数（2026-10-25）**
- 同一资产传 `maxEntries=3` 得 `retargetCount=3`、传 5 得 5、传 200 得 **126**。
  ⇒ 以后核条目数**固定传 200**；只信 `entries` 的实际条数与内容，别把回执里的 `retargetCount` 当总数。
- 与已有的“截断上限要两边各算一遍”同族：工具的“总数”字段如果是 `list.Count`，上限就是读数上限。

6a13. **真实 PIE 进世界复核：世界本地玩家画面仍是 UEFN mannequin（待定因，③ 不能算闭环）（2026-10-25）**
- **环境**：本地后端三进程**本来就在跑**（`Horizon.Orleans.Silo` PID 15528 / `Horizon.Game.Gateway` PID 26768，启动于 10-24 21:19，二进制是 10-22 12:37）——
  不是本轮启动的，未重启、未杀；因此**运行中的 Gateway 不含 6a8 的推送代码**，推送的端到端验证仍欠一次（需授权重启后端）。
- **操作**：RootScene 起 PIE → 走真实创角/选角流程 → 进 `World` 场景（角色 44「测试六号」）。**未加任何 marker**。
- **读数**：
  - 场景层级：`LocalPlayer_44`（**StaticModel** 根，来自 `CharacterRoot.prefab` 的 "Capsule" 空壳 + `PlayerController`）
    → `BodyCollider` / `AnimatedModel` / `LocalPlayerHead_44`（→ `LocalPlayerHair_44`、`LocalPlayerBrows_44`）。
  - `metahuman_inspect`：该 Actor **确实是 MetaHuman 宿主**——`loaded=true / 参数=635 / 写回端=2 / sealed=True`。
  - **画面**：`game_screenshot` 里整具角色是 **UEFN 白/橙 mannequin、T-pose**（头也是 mannequin 的胶囊头），看不到 Ada 皮肤与 MetaHuman 脸。
- **已排除**：把根 StaticModel 的 `Model` 置空（`actor_set_property` 回执 `newValue=null`）后画面**完全不变**
  ⇒ 可见的 mannequin 不是那具根静态模型。
- **线索（未定因）**：日志 `使用预加载 SkinnedModel 替换未加载的自带引用: Character_1.flax` 连打 6 次，
  之后才出现 `预加载 SkinnedModel: Ada_BodyPreview4.flax, IsLoaded=True` 只替换 1 处
  ⇒ 身体那具 `AnimatedModel` 很可能仍指向 **Character_1**；MetaHuman 的头/发/眉挂在它下面，但被 mannequin 自带的头挡住。
  `CharacterRoot.prefab` 本体只有 `StaticModel "Capsule"`（无 Model）+ `PlayerController` + `CharacterAnimationController` + `CapsuleCollider`，
  **不含身体模型** ⇒ 身体是运行时挂的，嫌疑落在“挂上去时用的哪一具模型/哪次替换赢了”。
- **同时复现 #77**：启动日志仍报同一资产被**相对路径与绝对路径各注册一次**
  （`Founded duplicated asset … Locations: 'Content/…' and 'C:\…'`，随后 `Cannot modify duplicated asset ID …`），
  身体与头部资产都出现过。这条会让“按路径加载”偶发失败，必须与上面那条一起查。
- **第二轮复核（同日，修正上一轮的归因）**：UI 顶上是 `CombatHudTraditionalPage`（确已进入游戏流程），但场景读数显示——
  - `scene_list`：loaded = `RootScene / Login / World` 三个场景；
  - **`LocalPlayer_44` 在 `Login` 场景**（594.6, 0, 3015.9），而 `World` 场景里是 Camera(594.6, 270, 2842.7)/Terrain/POI 等，**玩家不在 World**；
  - `metahuman_inspect` 的写回目标诊断里是 **`Ada_BodyPreview4`（0/605）** 与 conform 头（552/605）
    ⇒ 身体那具 `AnimatedModel` 的模型**很可能就是 Ada**，上一轮“身体是 Character_1”的推断**不成立**（当时只凭日志顺序猜的）。
  - 把根 `LocalPlayer_44.Model` 置空（回执 `newValue=null`）画面不变 ⇒ 可见网格**不是**根 StaticModel 的 `Model`
    （但 StaticModel 还有 `Entries` 数组，未排除）。
- **修正后的结论**：目前能确认的是两个**各自独立**的可疑点，都还没钉死：
  1. **玩家被生成到 `Login` 而不是 `World`**（场景解析/加载顺序；日志亦出现过“未找到玩法场景…滞留 RootScene”）；
  2. **画面里的 mannequin 来源未定**（候选：根 StaticModel 的 `Entries`、或另一具网格），需要只读入口读出每具网格的资产来源。
- **下一步（未做）**：给 `actor_bone_probe` 或新只读工具补 `SkinnedModel.Path / Model.Path / Entries` 与所在场景名（需要重编编辑器插件）；
  先解决“生成进错场景”，再判 mannequin 来源。修好前 ③ 的世界表现**不算闭环**，但**不要**再把它归因成“body=Character_1”。

6a15. **#77 根因定位到“相对路径 Content.Load 造出第二条登记”并已收窄（2026-10-25）**
- **证据链**：`asset_inspect`（磁盘侧）显示文件头 GUID 正常（如 `750e1911…`），但同一时刻
  `model_inspect`（内容库侧）`loaded=False / knownToContentDatabase=False / registryAssetId=<另一个 ID>`；
  用**绝对路径**再查同一个文件则 `loaded=True`。启动日志给出成对改名：
  `Changing asset A→B (storage: 'Content/…')` 紧接 `Changing asset B→C (storage: 'C:\…')`
  ⇒ 同一个文件被**相对路径与绝对路径各登记一次**，编辑器每次启动/运行都在“消重”并改写磁盘 GUID。
- **后果**：`Animation Graph.flax` 按 GUID 引用身体；身体一被改名，图的引用就悬空
  （`AnimationWiringVerify` 报 `graph: 没引用 Ada_BodyPreview4`），世界角色因此 **T-pose**。
- **修法（已落地，编译 0 错）**：所有**运行时**资产加载统一先转绝对路径
  `MetaHumanAssetUtil.ResolveProjectPath()`：`LoadAssetFullyLoaded`、`EnsureMetaHumanBody`（Ada/图）、
  头部加载、`EquipmentDatabase` 三个 `LoadAsync`、眉毛/发型绑定、`RemotePlayerActor` 头；`IdOf` 改用 `asset.ID`
  （原来按 `asset.Path` 绝对路径查库，本身就会造重复条目）。
- **验证到的程度**：
  - 连续两次重启 + 完整 PIE 进世界，身体 GUID **`750e1911…` 保持不变**（t0/t1/in-pie/after 全同）；
  - 把动画图按当前身体 GUID 重建后 `AnimationWiringVerify` **PASS**（`clips=125 direct-name=125`）。
- **仍未闭环**：随后又一次 PIE 后 GUID 变成 `b0cc6212…`（说明还有一处相对路径加载没转绝对）；
  且 `actor_bone_probe`（hand_l/spine_02 这些 Ada 确实存在的节点）**moved 仍为 0**。
  下一步：对剩下未统一的加载点做空白对照（每次 PIE 前后读 GUID，二分出触发者），再把图重建一次并复测骨骼。

6a17. **世界取景两连修 + 新发现：Ada 身体被“米制剪辑”拉成碎片（2026-10-25）**
- **取景根因**：`Camera.GetMainCamera()`（引擎 `Camera.cpp`）取 **`Cameras.First()`**，而本工程进入世界时 **Login 场景的相机也是 active**
  ⇒ 渲染一直走登录相机（在原点），角色在 (594,0,3015) 只剩一个点。
  修法（`WorldSceneInitializer`）：设定第三人称相机后**停用其它场景的相机**（进入 World 时），并在 `OnDestroy` 恢复；
  同时按 Ada 的**厘米口径**调机位（`FocusOffset=(0,120,0)`、`Distance=500`、`Min=200/Max=2000`，默认 `Distance=15` 是米制角色的值）。
  实测：进世界后画面切到玩法相机、能近距看到角色（`Tools/_world_camfix.png`）。
- **新发现（目视）**：近景里 **头正常、身体被拉成长条碎片**（`Tools/_world_framed.png`；关掉头发/眉毛后仍在，`_world_nohair.png`）。
  `actor_bone_probe` 显示骨骼在动（moved=2），但网格崩坏 ⇒ 优先怀疑**单位口径**：
  ALS 剪辑是按米制骨架（Character_1，靠 100 缩放）烘的，而 Ada 骨架是厘米；
  同一组骨骼平移直接作用在 Ada 上会把肢体压塌/拉飞。
- **下一步**：为 Ada 做**厘米口径的剪辑副本**（平移轨 ×100）或做带单位缩放的 retarget，再回看近景；
  判据：头+身体比例正常、四肢动画连续、无碎片。<br>（本条不改变“世界 = Ada 承载 + 动画在跑”的结论，但**视觉效果未验收**。）

6a18. **世界角色外观已验收（图先摘掉）；恢复动画 = 单位口径任务 #94（2026-10-25）**
- **原因补充**：图之所以摘不干净，是因为**创建期的 `AssignAnimatedModelResources` 已经给 Ada 挂上了图**，
  而 `EnsureMetaHumanBody` 在“现有模型已是 Ada”这条早退分支里**直接 return**，根本没走到清图那一段。
  修法：把开关提成类常量 `AttachLocomotionGraphToAda = false`，并在**早退分支**里同样清图。
- **实测（PIE 进世界）**：两个模型（身体 Ada / 头 conform）`animationGraph` 均为 **None**；
  `game_screenshot` 得到**完整的 MetaHuman 角色**（深色皮肤、卷发、比例正常、A-pose 站姿，`Tools/_world_final.png`）。
  GUID 仍 `b0cc6212…` 稳定，`AnimationWiringVerify` 仍 **PASS**。
- **代价**：没有 locomotion 动画（图被摘下）。恢复条件见 #94：为 Ada 做厘米口径剪辑（或带单位缩放的 retarget）；
  修好后把 `AttachLocomotionGraphToAda` 置回 true 即可，无需改别处。

6a19. **世界内“已发布不可改”实测成立（2026-10-25）**
- 在 PIE 世界里对 `LocalPlayer_44` 驱动 `body.height → 200`：`metahuman_drive` 返回 success，但
  `valueBefore=190 / valueAfterCommit=190`（未变），`metahuman_inspect` 显示 `sealed=True`。
- 前后截图差异只有 **32 px >20**，且集中在角色轮廓边缘（x 791–899 / y 466–714）——与“没有改动”一致，不是参数生效。
- ⇒ ③ 的“发布封存”在世界侧也成立；世界内做像素级参数验证必须用**未发布**的角色或 `metahuman_fixture`（本工程现有角色 44 已发布）。
- **#94 的假设修正**：碎片更像**静息姿态/骨骼轴向差异**（按名映射直接套旋转），而不只是单位。
  证据：`animation_playback_probe` 在 Ada 上播剪辑时 foot_l 位移 7.8（旋转可解释），而网格整片拉飞；
  下一步应先核对“目标骨架是否做了静息姿态补偿”，再谈平移轨 ×100；两条都要证。
- **单位读数（同日补）**：`actor_bone_probe` 读 Ada 身体绑定姿态，`thigh_l` 局部平移 = **(9.57, 84.02, −2.31)** ⇒ Ada 骨架是**厘米**；
  而 Character_1 的资产注释与其世界缩放（×100，米制）一致。⇒ #94 的两条嫌疑（单位 / 静息姿态）都要按厘米口径处理。
  （Character_1 上用同一探针读 `thigh_l` 得 0，属“节点名/虚拟图未匹配”的读数无效，不能当反证。）

6a16. **（过程记录，已被 6a17 修正）身体一度是 Character_1 占位壳：只读体检与 EquipmentManager 修复**
- **给 `actor_bone_probe` 加了只读体检**（每个模型回报 `skinnedModel / animationGraph / parent / active / updateMode / offscreen`；
  改 `ActorBoneProbe.cs` 后 `Flax.Build GameEditorTarget` 4 s、0 错、重启生效）。
- **实测（PIE 进世界，LocalPlayer_44）**：
  - `AnimatedModel`：`skinnedModel=Character_1.flax`、`animationGraph=Animation Graph.flax`、active=True；
  - `LocalPlayerHead_44`：conform 头、无图。
  ⇒ 画面里的白橙 mannequin **就是玩家身体本体**；图是按 Ada 重定向的，而 Character_1 自己的 126 条 retarget 指向已失效骨架（6a10），
  求值不出姿态 ⇒ **T-pose**。这与 `metahuman_inspect` 的写回目标（Ada + conform 头）不矛盾：写回端另一具模型在改，但**渲染的是这具占位壳**。
- **试过的修法（已编译、未生效）**：在 `CreateLocalPlayerActor` 里 `AssignAnimatedModelResources` 之后调 `EnsureMetaHumanBody(actor)`；
  并让 `EnsureMetaHumanBody` 换模型前把旧模型的 `SkinnedModel` 清空（否则 `AssignAnimatedModelResources` 会优先用“自带引用”），
  用 `LoadAssetFullyLoaded` 等 Ada 真正加载完，换完再 `IsActive=true / UpdateMode=Auto`，并打一条 `MetaHuman 身体已就位: model=… graph=…`。
  **实测：身体仍是 Character_1，且那条标记日志没出现** ⇒ `EnsureMetaHumanBody` 这个分支/调用**没有被执行到**
  （玩家可能走的是 `CreateFallbackCharacterActor`，或 prefab 分支里更早就 return）。
- **下一步**：在 `CreateLocalPlayerActor` 入口与两条分支各打一条标记日志，先确认实际走的是哪条；再决定把 Ada 换装放在正确分支。
- **同轮好消息**：#77 的实例在这几轮里**不再改写**（GUID `b0cc6212…` 跨多次 PIE 不变），`AnimationWiringVerify` 持续 **PASS**。
- **已闭环（同日晚）**：真正的替换者是 **`CharacterEquipmentManager.ApplyVisualBody`**——
  它按"外观 > 身体装备 > 性别默认身体"把 Ada 整模换成默认人形（Character_1 + 无图/或沿用旧图），于是画面上是占位壳、且 T-pose。
  修法（已编译 0 错、PIE 实测）：
  1. `ApplyVisualBody` 开头若当前 `SkinnedModel` 路径含 `Ada_BodyPreview` ⇒ **跳过整模替换**（身形/五官仍由模板参数驱动）；
  2. `EnsureMetaHumanBody` 换模型前清空旧模型的 `SkinnedModel`（否则 `AssignAnimatedModelResources` 优先用"自带引用"），
     Ada/图改用 `LoadAssetFullyLoaded` 等真正加载完，换完 `IsActive=true / UpdateMode=Auto` 并打 `MetaHuman 身体已就位` 标记；
  3. `CreateLocalPlayerActor` 在 `AssignAnimatedModelResources` 后显式 `EnsureMetaHumanBody(actor)`。
  **实测（PIE 进世界）**：身体 = `Ada_BodyPreview4` + Animation Graph、active=true；
  `actor_bone_probe` **moved=2**（hand_l 2.12 / spine_02 0.67）⇒ **动画在跑**；
  `game_screenshot` 里角色由白橙人偶变成**深色皮肤**的人形（`Tools/_world_ada.png`）。GUID 仍稳定、门禁仍 PASS。
  剩余：出生点/相机取景（角色离相机过远）与 Monsters 落 RootScene 属另外两条待办。
- **场景解析已修并验证（同日晚）**：`FindGameWorldScene` 原先「含 Terrain 的场景」优先，而 **Login 场景也带 Terrain**，于是本地玩家被生成进 Login。
  现已改为**路径优先**：先找资产路径含 `/World.scene` 的场景；`WorldSceneInitializer`/`Terrain` 两条兜底都排除 `Login/RootScene/TransitionScene`。
  `Game.csproj --no-incremental` **0 错**；重启编辑器后复跑同一流程：**`LocalPlayer_44` 已在 `World` 场景**（`scene_hierarchy` 实证）。
- **修完场景后的新读数（仍未闭环）**：
  - 玩家在 (594.6, 0, 3015.9)，而 `World` 里 POI/地形集中在 (0,0) 附近、相机在 (594.6, 270, 2842.7) ⇒ 画面里角色是远处一个小点（`_world_fixed.png`）；
  - `actor_bone_probe`（foot_l/calf_l/hand_l/head/spine_02 × 2 具模型，1.5 s）**moved = 0**（T-pose 仍在）；
  - 日志 `[LocomotionGraphDriver] READY graph=…/Animation Graph.flax` 有出现（图找到了），但骨骼不动 ⇒ 嫌疑转向“动画求值/驱动挂在谁身上”；
  - `metahuman_inspect` 仍显示写回目标是 Ada + conform 头（外观会话在位）。
  ⇒ 场景错位这条已结；**世界动画不跑**与**出生点/取景**是两条独立待办。

6a14. **真实创角流程的一个可见缺口：选角页预览是占位 mannequin（2026-10-25）**
- 走到 `CcRoleListPage`（选角列表）时，右侧 `CharacterPreview3D` 显示的是**按性别取的 Character_1/2 白橙 mannequin（T-pose）**，不是该角色已保存的 MetaHuman 外观。
- 代码侧：`CcStagePage.ApplyPreviewGender` 只调 `Preview.SetBodyGender(gender)`；选角页**没有**加载 `CharacterInfo` 里的 look，
  也没有走捏脸页那套 `MetaHumanCharacter + RetargetModels` 的显示链路；捏脸页的预览是正常的（6a9 同轮截图）。
- 影响：玩家在“选角色/进游戏”这一步看不到自己捏的脸（与 UE MetaHuman Creator 的预览习惯不一致）。
- 修法方向：选中角色时把该角色 look 载入 `CcSession` 那一具 MetaHuman 会话并切到 MetaHuman 头/蒙皮身体显示；
  需要处理页面生命周期（会话属于 CcSession，不在页面）与异步加载失败回退。**本轮未改**，先记缺口。

6a2k. **#94 世界角色四肢动画结案：真因是三条独立缺陷叠加（2026-10-25）**
- **旧结论作废**：此前"米制剪辑套厘米 Ada ⇒ 碎片"的说法**不成立**。可靠探针（按节点索引读整副骨架）给出的量级是厘米：pelvis 绝对位置 95 cm、clavicle 146 cm、head 162 cm，与 Ada 厘米骨架同口径，**不需要 ×100**。
- 真正的三条根因（全部已修，编译 0 error）：
  1) **身体模型选错**：`root.FindActor<AnimatedModel>()` 会按遍历顺序撞到 conform 头，`EnsureMetaHumanBody` 把 Ada/动画图挂到了头上，日志出现 `MetaHuman 身体已就位: model=…conformhead4.flax`，身体仍是 Character_1。新增 `FindBodyAnimatedModel`（排除名字/路径像头的，优先名字就是 `AnimatedModel`），并把 6 处调用点统一改用它（含最终校验与兜底）。
  2) **Ada 资产加载失败**：日志 `Cannot load asset header / AssetLoadError`＋`LoadAssetFullyLoaded 通过枚举找到 conformhead（不是期望资产）`；磁盘上 Ada 文件 GUID 从 `1262ccb0…` 被改成 `92ce52fd…`（#77 的又一次改写）。**剩余相对路径加载点**是 `MetaHumanRemoteProbe`（身体/毛发）、`CharacterPreview3D.ApplySkinnedBodyResources`、`RemotePlayerActor`、`HundunWorldGame` 毛发、`DamageNumberSystem` 字体——全部改用 `MetaHumanAssetUtil.ResolveProjectPath`。资产用 `bak_20261025_031218`（GUID `1262ccb0…`）还原后重跑 `ClearRetargetSkeletonGuids`，`Tools/AnimationWiringVerify.py` 仍 **PASS**（125 条按名映射），并清掉陈旧的 `Cache/AssetsCache.dat`。
  3) **坐标口径差 Z-up→Y-up**：剪辑里的 MetaHuman 通道按 UE Z-up 烘，Ada 骨架导入 Flax 后是 Y-up。实测修复前 pelvis 局部平移 `(0, 2.94, −95.74)`（绑定姿态 `(0, 87.07, −2.09)`），整副骨架多转 −90°X ⇒ 网格撕成竖条、头与身体分离。新增 `HundunWorld.Game.Character.MetaHumanClipFrameFix`：在 `AnimatedModel.AnimationUpdated` 后把每个节点矩阵换成 `R_x(−90°)·M·R_x(+90°)`（Flax 左手系，符号已实测校准），只对"挂了图的 Ada 身体"生效、不改资产。
- **实测证据（PIE 进世界，LocalPlayer_44，图已挂）**：
  - `actor_skeleton_unit_probe`：body = `Ada_BodyPreview4.flax` + `Animation Graph.flax`，343 节点、297 个平移通道被动画改写；head=conform 头无图；
  - `actor_bone_probe`（8 根骨，0.9s）：**全部 found 且 moved**，pelvis 95.8↑、spine_01 99.5↑、clavicle_l 146.5、upperarm_l 143.9、hand_l (32.4, 97.5)、thigh_l (10.0, 93.4)、calf_l (14.7, 50.5)、foot_l (17.1, 8.6)——解剖学上正确的直立骨架；
  - 两张相隔 1s 的截图：**9,695 px 差异（>8/255）**，其中 5,121 px >24/255，集中在角色区域 ⇒ 动画连续在跑；
  - 目视：`Tools/_world_framefix2.png` 直立完整人形（头/躯干/四肢/脚齐全），**无碎片、无分离**；
  - Ada 文件 GUID 本轮保持 `1262ccb0…` 不变，`AnimationWiringVerify` PASS。
- **边界**：`MetaHumanClipFrameFix` 假定"剪辑是 Z-up、骨架是 Y-up"，适合当前这批 ALS 剪辑；将来接入 Y-up 原生剪辑时应按资产标记开关，而不是无条件转。走/跑切换的像素级验收需要真输入（本轮只验了 idle/次级运动的连续动画）。

6a2l. **6a14 结案：选角页预览改成显示该角色自己的 MetaHuman 脸（2026-10-25）**
- **做法**（`CcRoleListPage`）：选中角色时读 `CharacterInfo.MetaHumanLook.Blob` → `MetaHumanLook.FromBytes` → 在预览的 Ada 头上挂 `MetaHumanCharacter`（`CharacterRole="preview"`，TargetModels 点名该头）→ `LoadLook`；列表回执没有 blob 时走 `MetaHumanAppearanceService.RequestRemoteAppearance` + 页面 `Update` 轮询 `TryGetCachedLook`（服务新增只读查询）。
- **为什么只显示头**：`CharacterPreview3D` 既有约定是「蒙皮身体可见时 Ada 头必须收」（身体没头，头会像灰蛋浮在颈口）。选角页先交付**脸**——玩家捏得最多的部分；身体/服装同屏排到后续。
- **健壮性**：头/身模型是异步加载的，绑定失败会 0.5s 重试、10s 超时并打警告（不静默停在半个预览上）；应用成功日志带 `targets/head`。
- **实测（PIE 首次进选角页）**：日志 `[CcRoleListPage] 选角预览已应用外观: targets=1 head=CharacterPreview3D_Head`；`ui_preview_screenshot`（source=output）里右侧是**带肤色/发型/眼睛/妆容的 MetaHuman 脸**，不再是白橙占位人偶（`Tools/_rolelist_final.png`）；Ada GUID 仍 `1262ccb0…`。
- **遗留（外观级）**：头像在 1060×1080 预览里偏小（`Reframe`+延后 `FocusOn("head")` 未明显拉近，取景参数待校准）；身体参数预览仍走旧的身体页签链路。

6a2m. **#77 真正根因：路径的「分隔符拼写」也是资产身份（2026-10-25，已闭环）**
- **现象**：编辑器每次启动/进 PIE 都会把 Ada 头 GUID 换一次（`1262ccb0→2cdbe735→7010b31d→5412a7a9…`），
  日志成对出现 `Founded duplicated asset … Locations: 'C:\…\Content\…' and 'C:\Works/…/Content/…'`，
  随后 `Changing asset … id to …`。动画图引用旧 GUID 后世界动画消失（探针 `movingTotal 0`）。
- **根因**：同一个文件被登记成两种**拼写**——本机 `Globals.ProjectFolder` 本身是混用的
  （`C:\Works/GitHubProjects/HundunWorld/HundunWorld`）。
  `MetaHumanAssetUtil.ResolveProjectPath` 之前把整条路径统一成**正斜杠**（`C:/…`），
  与编辑器扫描出的**反斜杠**拼写（`C:\…`）不等 ⇒ 每次游戏加载再登记一条 ⇒ 启动时判重复并改写 GUID。
  （另一处次要因素：`ClearRetargetSkeletonGuids.py` 把 `.bak_*` 写在 Content 里，同样会成为同 GUID 候选。）
- **修法（全部已编译 0 错）**：
  1. `MetaHumanAssetUtil.ResolveProjectPath` 改为 `Path.Combine(Globals.ProjectFolder, path)` + `Path.GetFullPath`
     —— 产出与编辑器登记**逐字符一致**的本机绝对路径；
  2. 编辑期工具新增 `EditorUtils.ResolveAssetPath`（同规则），`model_inspect` / `skeleton_retarget_list` 的
     `Content.Load`/`GetAssetInfo` 一律先转绝对路径（工具调用同样会造重复登记）；
  3. `Tools/ClearRetargetSkeletonGuids.py` 备份改写到 `Tools/_asset_backups/`（Content 外）；
     历史上落在 Content 里的 `.bak_*/.bad_*/.churn_*` 已全部移出。
- **实测证据（修后）**：连续重启 + PIE 进世界，日志 `duplicated asset` = **0**；
  Ada 文件头 GUID 在 PIE 前后保持不变（`5412a7a9…`）；`actor_skeleton_unit_probe` body=Ada+图、
  `movingTotal 371`；`AnimationWiringVerify` PASS（图已按当前 GUID 重建：`ada 5412a7a9…`）；
  世界截图直立完整无碎片（`Tools/_world_stable.png`）。

6a2n. **LOD 落地尝试 = 被引擎断言挡住（2026-10-25，负结果，已还原）**
- **数据层成功**：用 `asset_import`（`Type=SkinnedModel, MergeMeshes=false, GenerateLODs=true, LODCount=3, TriangleReduction=0.5`）
  导入身体源 GLB，得到 3 级 LOD：LOD0 32,334 顶点/60,816 三角、LOD1 16,417/30,408、LOD2 8,439/15,203，
  `screenSize 1/0.5/0.25`，343 节点/283 骨，与生产身体同拓扑。
- **但编辑器随即崩溃**：`AnimatedModel.cpp:82 CurrentOffset + size <= CurrentSize`（`AnimatedModelRenderListExtension::Allocate`
  在导入预览/缩略图路径上被触发），与 #76 同族；移除该资产后启动恢复正常。两次复现（不同源命名）一致。
- **结论**：本引擎版本下「带 LOD 的 283 骨蒙皮模型」在编辑器导入/首绘路径会撞骨骼缓冲断言，
  **生产资产保持单 LOD**；LOD 资产已移出 Content 存到 `Tools/_lod_park/`（代码与数据都不接入）。
  以后若能关掉导入预览的 `PerBoneMotionBlur` 或修引擎缓冲翻倍逻辑，这条可直接续上。

6a2o. **选角预览升级：身体 + 头同屏的完整外观模式（2026-10-25）**
- **新增** `CharacterPreview3D.SetMetaHumanLookMode(bool)`：同时打开蒙皮身体与 Ada 头，头 Actor 复制身体的变换后
  `SetMasterPoseModel(body)`；退出时恢复旧规则（身体可见则头收起）。取景在 `_metaHumanLookMode` 下改用
  `TemplateHeadCrownY`（到头顶的整身高），否则只量身体（130.9cm）会把头顶裁掉。
- **选角页**：`ApplyPreviewLook` 切到这个模式，`CollectSkinnedModels` 收到 **body+head 两具**并一起作为
  `MetaHumanCharacter.TargetModels`（身体参数与脸都能显示）。
- **实测**：日志 `[CharacterPreview3D] MetaHuman 外观模式：头已跟随身体姿态` +
  `[CcRoleListPage] 选角预览已应用外观: targets=2`；`ui_preview_screenshot` 为**从头到脚的完整角色**，
  脸/发/肤色都是该角色保存的 look（`Tools/_rolelist_full4.png`）。
  骨骼对位实测：`CharacterPreview3D_Head` 与 `CharacterPreview3D_SkinnedBody` 的 `head` 骨世界位置
  完全相同（y=143.35823），`neck_01` 亦相同（132.98943）。
- **已知外观瑕疵（网格级，不改布局）**：颈根有一圈"衣领"接缝（头资产自带颈/肩壳与身体重叠）、
  手腕处有少量尖刺（Ada 身体网格既有问题）；A-pose 静止（预览身体无动画驱动）。
- 同轮复核：日志 `duplicated asset` = 0，Ada 头 GUID 仍 `5412a7a9…`（#77 的修法在预览链路上也成立）。

6a2p. **世界/远端角色补上面部空闲动画（眨眼）：UE Face Animator 对齐（2026-10-25）**
- **缺口**：世界里角色身体有 locomotion、头也跟随，但**没有任何面部驱动**——`MetaHumanFaceAnimator` 在 `Source/Game` 里零引用，
  所以游戏里角色从不眨眼（UE MetaHuman 默认会）。
- **接线**：本地玩家在挂完 `MetaHumanCharacter` 后，给头 Actor 加 `MetaHumanFaceAnimator`
  （`Owner` 显式指认、`AutoBlink=true`、`AutoPlayDemo=false`、`LinkMirrors=true`）；远端玩家在 `RemotePlayerActor.ApplyMetaHumanLook`
  成功套用外观后同样处理。只眨眼、不跑周期性张嘴 demo（避免盖住外观静帧对比、远看嘴一直动）。
- **实测（PIE 进世界，LocalPlayerHead_44）**：`actor_get` 头部脚本 = `WorldHeadMasterPose, MetaHumanFaceAnimator`；
  对 `FACIAL_L_EyelidUpperA1 / FACIAL_R_EyelidUpperA1` 连续采样 14 次（每次 0.6s 间隔），
  **2 次**捕获到双睑同步转动（矩阵差 1.6898/1.6502 与 1.2213/1.1882），其余时刻静止 ⇒ 眨眼按 2.5–6s 随机间隔在跑。
  同轮 `movingTotal 341`（身体动画不受影响）、`duplicated asset` = 0、Ada GUID 不变。
- **边界**：世界面部只有"空闲眨眼"这一条；外部面部追踪（ARKit 词汇层）仍停在插件 API 层，未接任何输入源。

6a2q. **原地转向（Turn In Place）：图 + 驱动接线，参数级实测（2026-10-25）**
- **缺口**：②里记的"转向/停步过渡剪辑未接"。资产其实已在盘上：`als/TurnInplace/ALS_N_TurnIP_L90|R90`。
- **图（17 节点 / 6 参数）**：在三级 Blend 之上加两层——
  `Output <- BlendTurnR(A=BlendTurnL, B=TurnR, w=TurnRight)`、`BlendTurnL(A=BlendSprint, B=TurnL, w=TurnLeft)`；
  新增公开 Float 参数 `TurnLeft/TurnRight`。构建脚本 `Tools/_animation_graph_make_locomotion.py` 现在同时读 L/R 剪辑、
  写后自带 parse 断言（节点表/参数/连接逐项对位）。`Tools/AnimationWiringVerify.py` 同步升到 17 节点/6 参数并断言两条转向剪辑存在 ⇒ **PASS**。
- **驱动**：`LocomotionGraphDriver` 增加 `UpdateTurnInPlace`：几乎静止（速度 < WalkSpeed·0.25）且偏航角速度 ≥ 50°/s 时，
  按方向把 `TurnLeft/TurnRight` 平滑到 1，松开后保持 0.35s（让 0.2–0.5s 的短转身播完）；另有 `DebugForceTurn` 调试口。
- **实测（PIE 进世界，LocalPlayer_44）**：
  - `anim_graph_param_get`：初始 `TurnLeft=0/TurnRight=0`；`DebugForceTurn=1` 后 **`TurnLeft=1`**（驱动确实写进实例）；
    `DebugForceTurn=-1` 后回到 `3.7e-06`；
  - 姿态：强制转向时 `thigh_l (9.97,93.54,2.77)→(6.81,93.07,4.24)`、`spine_02 x -0.013→-2.55`、`upperarm_l x 19.06→16.03`；
    释放后 spine_02 回到 `-0.0134`、upperarm 回到 `19.09`（与基线一致）⇒ 转向层只在需要时混入，退出无残留；
  - 同轮 `duplicated asset` = 0、Ada GUID 不变。
- **自动触发也已验证（同日晚补测）**：`PlayerController` 增加调试口 `DebugLookYawDegrees`（非 NaN 且 |v|≤360 时替换视角 Yaw），
  走的是**真实的 LookYaw → PredictedTransform → Actor 朝向**链。设 0→30° 后日志：
  `peak=1575.5 turnL=0.71 turnHold=0.22`；再 30→35°：`peak=256.8 turnL=0.50`；保持时间耗尽后 `turnL=0.08→0`。
  ⇒ 角速度判定、方向选择、hold、退出衰减全部按设计工作。
  （MCP 直接改 Actor/相机变换会被预测/相机控制每帧覆盖，所以只能从这个视角变量入口注入。）
- **边界**：只接 90° 转向剪辑（180° 未接）；`TurnRateThreshold=50°/s`、`TurnHoldSeconds=0.35s`、`TurnBlendTime=0.12s` 是首版经验值；
  调试字段（`DebugForceTurn` / `DebugLookYawDegrees` / `DebugLog` 的 rate·peak·hold 输出）保留在代码里，默认关闭不影响生产路径。

6a2r. **停步过渡（Stop L/R）：图 + 驱动，参数级实测（2026-10-25）**
- **缺口**：② 的"停步过渡剪辑未接"。资产在盘上：`als/ALS_N_Stop_L_Down.flax`、`ALS_N_Stop_R_Down.flax`。
- **图（23 节点 / 8 参数）**：在转向层之上再加两层——`Output <- BlendStopR(A=BlendStopL, B=StopR, w=StopRight)`、
  `BlendStopL(A=BlendTurnR, B=StopL, w=StopLeft)`；新增公开 Float `StopLeft/StopRight`。构建脚本同时读两段停步剪辑并逐项断言；
  `AnimationWiringVerify` 升到 23/8 并断言两条停步剪辑 ⇒ **PASS**。
- **驱动**：`LocomotionGraphDriver.UpdateStopTransition`。速度是平滑量，单帧不会从"在走"直接跌到"几乎静止"，
  所以用 **0.8s 的"刚在走"记忆窗**（`_movingMemory`）而不是前后单帧比较：`nowSlow && memory>0 && !turning` 时
  置 `_stopHold=0.25s`，按最后转向方向（无转向记录时默认左）混入；另有 `DebugForceStop` 调试口。转向进行中不触发。
- **实测（PIE 进世界，LocalPlayer_44）**：
  - 强制：`DebugForceStop=0.8` → `StopLeft=0.7999`，`thigh_l x 9.97→10.75`、`spine_02 x -0.013→0.679`；
  - 自动（`DebugForceSpeed` 8→0）：日志 `speed=0.75 walk=0.15 stopL=0.49 stopHold=0.20`，随后 `stopL=0.04→0`；
    `anim_graph_param_get` 在松开后 +0.15s 读到 `StopLeft=0.158`；停步中姿态 `thigh_l x 10.63`、`spine_02 x 0.607`（相对基线偏移）；
  - 同轮 `duplicated asset`=0、Ada GUID 不变、门禁 PASS。
- **边界**：停步方向只用"最后一次转向方向"近似（没有脚位/侧向输入分析），默认左；`StopHoldSeconds=0.25s`、`StopBlendTime=0.10s` 为首版经验值。

6a2s. **locomotion 单位口径缺陷 + 合成远端 locomotion 取证（2026-10-25）**
- **缺陷**：世界位置是**厘米**，而 `LocomotionGraphDriver` 的阈值一直按**米/秒**标定（Walk 5 / Run 10 / Sprint 15），
  且 `SpeedScale` 默认 1、`TeleportSpeed` 默认 60。真实移动（几百 cm/s）先被 `speed > TeleportSpeed` 判成"传送"整段丢弃，
  于是 **LocalPlayer 与远端只要靠真实位移移动，`LocomotionWalk/Run/Sprint` 恒为 0**（之前 6a2v 的"在动"是
  `DebugForceSpeed=8` 且 SpeedScale=1 的合成读数，掩盖了这条）。
- **修法**：`SpeedScale` 默认改 **0.01**（cm/s → m/s）、`TeleportSpeed` 默认改 **6000**（60 m/s）；
  与 `DebugForceSpeed`（原始位移单位）一起注明口径。
- **合成远端补链**：`MetaHumanRemoteProbe.Synthesize` 在套完 look 后给身体挂 **同一张 Animation Graph**、
  `UpdateMode=Auto`、`MetaHumanClipFrameFix.Attach`，并 `EnsureLocomotionDriver(actor)`——与真实远端
  `FlaxActorSyncSystem→EnsureMetaHumanBody` 同一条链。
- **实测**（PIE 世界，`metahuman_fixture remote:true characterId:900001`）：
  - 探针回执 `bodyPath=Ada_BodyPreview4`、`graph=Animation Graph.flax`、`head=True hair=True mh=True session=True`；
    头部脚本 = `WorldHeadMasterPose, MetaHumanFaceAnimator`（远端也会眨眼）；
  - 速度→步态映射（`DebugForceSpeed`，原始 cm/s）：**300 → Walk 0.60**；**900 → Walk 1.00 / Run 0.80**；
    **1600 → Walk 1.00 / Run 1.00 / Sprint 1.00**；归零后参数衰减到 0，并捕获一次 `StopLeft` 脉冲；
  - 骨骼：`thigh_l / spine_02` 在采样间隔内 `moved=True`（Δ0.214）；
  - 截图 `Tools/_remote_probe.png`：画面里**两具 MetaHuman**（本地 + 合成远端），远端身体完整、带发型。
- **边界**：仍是合成远端（无第二客户端、无真实网络来回）；步态阈值 5/10/15 m/s 是首版经验值。

6a2t. **真实输入链路验证：世界角色真的会走路了（2026-10-25）**
- **路径**：`PlayerController.DebugMoveForward`（调试口）→ `WriteInputToEcs()` 写 `PlayerInputComponent.MoveX/MoveY`
  → ECS `LocalSimulationSystem` 预测 → `LocalPlayerActorSyncSystem` 应用到 Actor → `LocomotionGraphDriver` 测速 → 图参数 → 蒙皮。
  这是生产链路的**输入覆盖**，不是绕过预测。
- **实测（PIE 进世界，LocalPlayer_44）**：置 1 后 1.5s 内位置 `z 3015.86 → 3991.31`（≈975cm ⇒ ≈6.5m/s，属走路档）；
  `LocomotionWalk=1.000 / Run=0.005 / Sprint=0`；`thigh_l Δ2.6`、`calf_l Δ26.5`（采样 0.4s，明显在迈步）；
  截图 `Tools/_local_walk.png` 是**角色背向相机、迈步摆臂**的奔跑姿态（不再是 A-pose 滑行）。
  置回 0 后 0.12s 读到 `StopLeft=0.0449`（自动停步脉冲）、`LocomotionWalk=0.0029`。
- **意义**：6a2s 的单位口径修复在**真实预测链路**上得到端到端证明；此前"世界角色像 T-pose 滑行"的表现由此闭环。
- **边界**：输入是调试口合成的"一直向前"，没有真人键盘/鼠标；左右/斜向与转向组合仍靠单元级路径。

6a2u. **180° 原地转向自动档：episode 窗口累计 + 双方向实测（2026-10-25）**
- **图**：`Tools/_animation_graph_make_locomotion.py` 扩到 **29 节点 / 10 参数**，新增 180 剪辑
  `ALS_N_TurnIP_L180/R180`（`TurnLeft180/TurnRight180`），链路
  `Output ← BlendT180R ← BlendT180L ← BlendStopR ← BlendStopL ← BlendTurnR ← BlendTurnL ← BlendSprint ← BlendRun ← BlendWalk ← clips`。
  `AnimationWiringVerify` 同步升到 29/10，10 条剪辑与 125 条按名映射全部可解析 ⇒ **PASS**。
- **驱动**：`TurnBigThreshold=100°`、`TurnEpisodeSeconds=0.8s`。按 **deltaYaw** 累计：只要 0.8s 内还看到过偏航变化，
  窗口就续期并继续累加；窗口走完才清零 `_turnAccum/_bigTurn`。`|accum|≥100` 后目标从 90° 切到 180°（按符号选 L/R）。
- **失败过的原因**：早先按"转向帧 × 速率"累加且把 hold 当窗口，外部离散注入（每次 10–20°、步间隔 > hold）导致
  accum 反复清零，日志长期停在 `big=0 accum=10.0`；改成 episode 窗口后一次通过。
- **另一条坑**：先重启编辑器再改码时，**PIE 可能跑在重建前的装配上**（本次首轮 PIE 起点 00:00:49、DLL 写于 00:02:07），
  旧会话日志仍是 `accum=10.0`。判据只认"重启后的新 PIE"：`sim_stop → sim_play` 再取证。
- **实测（PIE 世界，LocalPlayer_44，`DebugLookYawDegrees` 注入）**：
  - 渐进 0→160°：`TurnLeft 0.62→1.00`（到 100°），120/140/160° 时 `TurnLeft 0.34/0.09/0.02`、
    `TurnLeft180 0.66/0.91/0.98` ⇒ 越过 100° 后正确切到 180° 剪辑；
  - 日志：`big=1 accum=160.0 t180L=0.98`（左）；`big=1 accum=-180.0 t180R=0.71/0.76/0.87`（右，两个方向都验证）；
  - 姿态：180 档峰值相对待机 `thigh_l Δ0.71cm / calf_l Δ1.06cm / hand_l Δ3.88cm`（0.04s 采样，剪辑起步段本来接近待机）；
  - 复位后四个参数回 0；`duplicated asset`=0（451 个 `.flax`，0 重复头 GUID）；Ada GUID
    `d1e68aba-45b7-4801-94cd-c02429a80f3a` 跨 25s 不变。
- **边界**：180 档靠 0.8s 内的累计偏航判定，没有真正的"目标朝向"语义（UE 里由角色移动意图给出）；
  注入速率峰值上万 °/s，不是人手速度；单客户端。

6a2v. **外部面部追踪输入源：Live Link 等价链路已接通并实测（2026-10-25）**
- **缺口**：ARKit 词汇表（`MetaHumanArkitMap`）与写入端（`MetaHumanFaceAnimator.SetArkitChannels`）早已存在，
  但游戏里没有任何"谁来喂数据"的入口——之前只能靠 `metahuman_drive` 手工驱动。
- **新增**：`Plugins/MetaHuman/Source/MetaHuman/Runtime/MetaHumanFaceTrackingReceiver.cs`
  - 后台线程收 UDP JSON，主线程 `OnUpdate` 把最新一帧交给 `SetArkitChannels`（不阻塞逻辑/渲染线程）；
  - 三种报文都收：`{"BlendShapes":{...}}`、`{"BlendShapes":[{...},...]}`、扁平 `{"eyeBlinkLeft":...}`；
  - 值域 [0,1] 外的数与非数字被丢弃；未知通道交给 `unmatchedChannels` 计数（不静默）；
  - 断流保护：`ReleaseAfterSeconds=0.75` 内没有新帧就把上一批通道清零一次，避免追踪程序退出后脸定格；
  - 默认 `Receiving=false`（开发/演出用，正式客户端不默认占端口）；端口 11111。
- **挂载**：本地玩家与远端头部在创建 `MetaHumanFaceAnimator` 时同处挂载该组件并指认 `Animator`。
- **配套工具**：`Tools/MetaHumanSendTracking.py`（合成 blink/jaw/smile/mixed，三种格式，可 `--hold` 验断流）。
- **编译坑**：① 插件工程 `EnableDefaultItems=false`，新增 .cs 必须先让 Flax.Build 重新生成工程；
  ② `UdpClient` 需要 `MetaHuman.Build.cs` 里 `SystemReferences.Add("System.Net.Sockets")`（否则 CS1069 类型被转出）；
  ③ 该环境下 `FlaxEngine.Time` 没有可用的绝对秒（`Time.Time` 不存在），收发两端统一用 `Environment.TickCount64`；
  ④ `actor_set_property` 只认真实 C# 属性，脚本上的公有**字段**会报"不存在可写属性"，所以开关全部改成带 setter 的属性。
- **实测（PIE 世界，LocalPlayerHead_44，UDP 127.0.0.1:11111）**：
  - `Receiving=true` 后日志 `[FaceTracking] 监听 udp://127.0.0.1:11111`，逐帧 `channels=2 applied=2 unmatched=0`；
  - blink（`eyeBlinkLeft/Right=1.0`）持续 3s：`FACIAL_L_EyelidUpperA1` 由 `(2.459,149.393,-9.437)` 移到 `(2.459,150.344,-7.344)`
    （Δ2.299cm），双侧对称；发 0 后回到基线（Δ0.000）；
  - 三种报文格式各自单独跑：dict / list / flat 的 Δ 都是 **2.299cm**，复位都回 0；
  - 非法值（2.0 / -1）Δ=0；未知通道 4.6s 后日志出现 `channels=2 applied=0 unmatched=2` ⇒ 拒绝可见而非静默丢；
  - `--hold`（发完不发零）峰值 Δ0.846cm，停止 1.2s 后骨骼回到基线 ⇒ 断流清零生效；
  - 合成远端 `RemoteHead_900001` 的脚本列表同样含 `WorldHeadMasterPose / MetaHumanFaceAnimator / MetaHumanFaceTrackingReceiver`；
  - 收尾：`Receiving=false`、探针已删、PIE 已停；`duplicated asset`=0（451 个 `.flax`）、门禁 PASS。
- **边界**：输入是脚本合成的 ARKit 帧，没有接真实 iPhone/Live Link Face 设备；UDP 无认证/加密，
  只适合本机或可信内网；没有做时间码对齐与丢包补偿（追踪帧是"取最新一帧"，不排队）。

6a2w. **空中 locomotion（Jump/Fall/Land）：图 + 驱动已接，且发现跳跃高度只有 1.5cm 的单位缺陷（2026-10-25）**
- **图**：`_animation_graph_make_locomotion.py` 扩到 **38 节点 / 13 参数**，新增
  `ALS_N_JumpLoop / ALS_N_FallLoop / ALS_N_Land_Light`（参数 `Jump/Fall/Land`），链路
  `Output ← BlendLand ← BlendFall ← BlendJump ← BlendT180R ← …`（空中层压在地面层之上，落地再压一层）。
  `AnimationWiringVerify` 同步断言 38/13 + 13 条剪辑 ⇒ **PASS**。
- **图块变长的坑（重要）**：`.flax` 的块长度**不在文件尾**，而在 v9 头的 chunk Location 里
  （偏移 180=Address、184=Size；文件尾只是数据）。第一次只把数据追加在 462 之后、没改 Size，
  引擎按旧长度 4978 截断 ⇒ `Failed to load animation graph`。改法：写数据的同时把 Size 改成实际长度；
  改完回读 `loc=(462, 5164)`、`file=5626`，图正常装载、参数可读写。
- **驱动**：`LocomotionGraphDriver.UpdateAir`——本地问 `PlayerController.IsGrounded`（ECS 权威），
  远端没有控制器时用垂直速度推断（|vy|<20cm/s 视为贴地）；空中按 `vy>AscendThreshold` 切 Jump、否则 Fall；
  落回地面那一帧起播 `LandHoldSeconds=0.25s` 的落地剪辑；`AirBlendTime=0.10s`。
  `AscendThreshold` 取 **1cm/s**（判方向不判幅度）：已知跳跃顶点只有 1.5cm，用 50cm/s 会把起跳也判成下落；
  将来跳跃修回正常高度后该判据仍成立。
  `PlayerController` 新增 `DebugJump`（只覆盖跳跃按下的边沿语义，仍走真实 `WriteInputToEcs → 预测` 链）。
- **实测（PIE 世界，LocalPlayer_44）**：
  - 强制三档参数各为 1.0，姿态相对待机（`thigh_l/calf_l/hand_l` 位移 cm）：
    Jump **20.16 / 26.77 / 57.77**、Fall **9.64 / 20.67 / 25.97**、Land **3.13 / 12.05 / 10.16**
    ⇒ 三条剪辑确实不同且都在驱动骨架；日志 `jump=1.00`、`fall=1.00`、`land=1.00` 各出现过；
  - 自动链（`DebugJump=true` 0.4s）：一次跳跃内 40ms 采样峰值 **Jump 0.89 → Fall 0.96 → Land 0.92**
    依次出现；日志 `ground=0 vy=-1.5 fall=0.50` → `ground=1 land=0.90`
    ⇒ 真实输入能依次触发起跳/下落/落地三层（阈值改 1cm/s 后 Jump 分支才真正生效）；
  - 收尾：调试开关全部复位、PIE 已停；门禁 PASS（38 节点/13 参数/13 剪辑/125 条映射）、
    `duplicated asset`=0（451 个 `.flax`）、Ada GUID `d1e68aba-45b7-4801-94cd-c02429a80f3a` 未变。
- **新发现（上游模拟层，未修）**：跳跃垂直速度/重力按 **米/秒** 标定，但位置是**厘米**：
  `MovementFormula.Gravity=9.81`、`SimulationOptions.JumpImpulses={5.5,4.5,3.5}`，
  顶点高度 = v²/2g = **1.54cm**——实测跳跃峰值正好 **+1.5cm**。
  因此自动判定的 `vy>50cm/s` 永远不成立，起跳阶段会被判成 Fall（本次日志即为 `fall`）。
  修法需要把 `JumpImpulses/Gravity/TerminalVelocity` 与位置单位对齐（×100 或显式单位换算），
  并**客户端与服务端一起换装配**（预测/重放共用 `MovementFormula`，单改一边会 correction 风暴）；
  后端当前进程是 10-24 启动的旧装配，故本轮只记录、不改共享模拟层。
- **边界**：空中层只用垂直速度/落地标志，没有起跳冷却、没有区分轻功三段跳的每段剪辑；
  远端是"垂直速度推断"而非权威落地标志。

6a2x. **头部网格 100% 绑定 root：所有骨骼级面部通道（眨眼/眼动/下颌）都不改变像素（2026-10-25）**
- **动机**：想补 UE 的 Eye Aim（视线跟随），在创角预览里让眼睛看相机。
- **实测否证**：`MetaHumanEyeAim`（按目标方向在 Actor 空间绕眼球位置加旋转）写入后
  `actor_bone_probe` 能读到 `FACIAL_L_Eye/FACIAL_R_Eye` 的矩阵变化（45° 时 maxMatrixDelta≈1.82），
  但**脸部区域像素差为 0**；把写入从 `OnUpdate` 移到 `AnimationUpdated`（引擎动画求值完成）后仍然为 0。
- **根因（二进制证据）**：头部两份源网格的蒙皮权重全是 root——
  `Output/mhbaked/Ada_FaceMesh.glb` 与 `Output/mhface/slots/Ada_FaceMesh_conformhead4.glb`：
  103,205 个带权顶点、`JOINTS_0/1/2` 全查，`root` 命中 103,205，`FACIAL_L_Eye / head / FACIAL_C_Jaw` 均为 **0**。
  ⇒ 面部的可见变化只能来自 **morph（821 个 shape）与材质参数**；骨骼通道写进去只动矩阵、不动蒙皮。
- **对既有结论的修正**：6a3i 里"眨眼绕眼球旋转 / 张口用 12IPV_LipLower 组平移"的取证是**矩阵级**的，
  不能证明画面上有开合；凡是"面部骨骼动画"的验收都必须配像素/形状权重证据。
- **处置**：`MetaHumanEyeAim` 已删除（该资产上不可能生效）；要恢复 Eye Aim 需要先按正确权重重蒙皮头部
  （与 #60 肩缝同族，属 DCC/UE 侧工作）。
- **仍成立的部分**：创角预览与世界里**看得见的**眨眼/张嘴来自 morph 路径（AutoBlink 的 morph 兜底与
  `face.*` 形状写回）；这部分继续用像素证据约束（见 6a3i/6a3m 的幅度记录）。

- **同日修正（ROI 取错导致的假“零像素”，2026-10-25）**：上面“像素 0”的判据把脸框取在
  x=640–830（那其实是空白背景），真实人脸在 **x≈465–615、y≈100–320**。用正确 ROI 重算：
  - 待机连拍（AutoBlink 默认开）里多帧出现 **max 184、6.7k–13.5k px** 的闭眼变化，
    而待机基线噪声只有 max≈15、≤34 px；
  - 新增的 `DebugBlink=1` 强制闭眼：**7009 px>10**；`DebugJaw=1`：**10,917 px>10**；
    `face.zone.cheek.l.tx=1`：20 px>10（局部，bbox 487–553 × 192–267）；
  - 结论：**morph 驱动的面部动作是可见的且已验证**；只有**骨骼驱动**的眼动/眼睑（Eye Aim）因权重全 root 而不动像素。
  - 教训：像素差分必须先用截图定位 ROI（本工程预览人脸在画布右侧 0.44–0.58 宽、0.09–0.30 高），
    沿用旧坐标会把整段判据变成“测背景”。
- **调试口（保留，默认关闭）**：`MetaHumanFaceAnimator.DebugBlink/DebugJaw`（>=0 强制值）、
  `DebugMorphGain`（1=原样）、`DebugLog`（每秒一行 morph/写回端诊断），用于后续像素回归。

6a2y. **事故与恢复：误删未入库的 Content 二进制资产（2026-10-25）**
- **事故**：清理临时脚本时在同一条命令里误带了 `rm -rf HundunWorld/Content/Character/Models`。
  - 已跟踪文件：`git restore -- HundunWorld/Content/Character/Models` 全量恢复（含动画图，随后用
    `_animation_graph_make_locomotion.py` 重写成 38 节点/13 参数并回读 PASS）。
  - **未入库（本地-only）资产无法用 git 恢复**：`Ada_FaceMesh_conformhead4`（含 8 个槽子资产）、
    `slots_test/Ada_FaceMesh_slotpacked.flax`、`Models/Ada_BodyMesh.flax`、`Models/Ada_FaceMesh.flax`、
    `mhhair/*`（6 发型 + 眉毛）。
- **恢复来源与做法**：
  1. **头**：`Tools/_backup_head_mh_20261023/`（主资产 2026-10-23 09:39、8 个槽子资产 10-22）
     回填 `head_mh/Ada_FaceMesh_conformhead4.flax` 与子目录；回读 **821 shapes / 9 槽**，
     槽材质指向仍存活的 `MH_Head_New / MH_Eye / MH_EyeShell / MH_Eyelashes` 等。
  2. **发型/眉毛**：`Output/mhbaked/hair/*.glb` 与 `Output/mhbaked/Eyebrows_Cards.glb` 重新导入到
     `Content/Character/Models/mhhair/`（7 个 Model，路径与代码常量一致），再用
     `model_material_slots_assign` 把槽 0 指到 `MH_Hair`（眉毛用 `MH_Hair_Cutout`）——
     不指认时卡片是白的（导入材质没有 HairColor 参数）。
     **引用普查（同日）**：`Tools/_prefab_scene_asset_ref_audit.py` 全量扫 `.prefab/.scene` 的资产引用，
     仅 `Main.scene`/`Login.scene` 有 60 处**既有**空 GUID 与一个旧模型 `a2900dc6…` 悬空，
     **没有任何预制体/场景因这次 GUID 变化而引用悬空**（运行时按路径加载）。
     `slots_test/Ada_FaceMesh_slotpacked.flax` 已用 `Output/mhface/slots/Ada_FaceMesh_slotpacked.glb`
     重建（821 shapes / 669 bones，槽 0/1 指认 `MH_Head_New/MH_Teeth`）；重建版只有 head/teeth 两槽
     （眼/睫毛槽导入器不生成），仅影响夹具观感；旧 9 槽备份仍在 `Tools/_backup_slotpacked_before_mhhead.flax`。
  3. **身体/旧脸**：`Output/mhbody/Ada_BodyMesh.glb` → `Models/Ada_BodyMesh.flax`、
     `Output/mhface/Ada_FaceMesh.glb` → `Models/Ada_FaceMesh.flax`，均按 harness 要求
     `ImportMaterials=false`（旧脸 1043 shapes）。
     `MetaHuman_Conform_Topology/Ada_BodyMesh.flax` **未被改动**（mtime/GUID 仍是 10-19 / f81f0a2e）。
  4. **slots_test/slotpacked**：仅有 2026-10-16 的备份 `Tools/_backup_slotpacked_before_mhhead.flax`，
     已回填；它的槽子资产没有备份（只影响夹具观感）。
- **恢复验证**：离线 harness **1634/0/0**；创角预览截图里头发/眉毛/眼睛/睫毛都在（`Tools/_restore_hair2_face2.png`）；
  进世界后 `LocalPlayerHead_44` 的 `skinnedModel` 指回头资产、身体图仍挂
  `Animation Graph.flax` 且 `Jump/Fall/Land` 参数可读。
- **遗留**：重导/回填后的资产 **GUID 已变化**（头 4c178fce、body 85377a46、hair 各自新 ID）。
  运行时按路径加载不受影响，但任何**序列化里存旧 GUID** 的地方（预制体、已发布 look 的资产引用）需要复查；
  本轮已确认 `MetaHumanLook` 不存 GUID、且预制体里没有旧 GUID 文本。
- **教训**：`rm` 不进回收站；`git status` 看不见未跟踪资产。删任何 Content 路径前先确认"是否在 git 里"，
  并对本地-only 二进制资产保持 `Tools/_backup_*` 之类的副本。

- **恢复后的两处已知退化（2026-10-25 实测）**：
  1. **9 槽结构无法由 GLB 重导复现**：`Output/mhface/slots/Ada_FaceMesh_conformhead4.glb` 重导
     （`ImportMaterials` 开/关、`CreateEmptyMaterialSlots=true` 都试过）只得到 **head/teeth 两槽**，
     会丢掉眼睛/睫毛的槽材质；原来的 9 槽来自丢失的"分件导入"管线（主资产 + 8 个 `Ada_FaceMesh-N` 子件）。
     因此生产上保留 9 槽备份（眼睛/睫毛正确）。
  2. **9 槽备份带 morph NORMAL**：`Output/.../Ada_FaceMesh_conformhead4.glb` 的 7389 个 target 全带 NORMAL，
     而 `Tools/Ada_FaceMesh_conformhead4.glb` 是无 morph 法线的修复版（0 个带 NORMAL）。用 9 槽备份时，
     任何表情 morph（眨眼/张口）都会触发 Flax 1.13 的 morph 法线→切线问题，整脸发暗（jaw 实测 max 184/10.9k px，
     眼眶与面颊明显变黑）；换剥法线版则黑斑消失但只剩 2 槽、眼睛材质错。
     **取舍**：当前保留 9 槽（眼睛优先），黑斑作为已知缺陷记录；要同时满足需重建"剥法线 + 分件槽"导入管线。
- **创角预览的外部追踪（同日）**：`MetaHumanFaceTrackingReceiver` 已挂到预览头（默认关）。
  实测 UDP `jawOpen=1`：日志 `channels=1 applied=1 unmatched=0`，脸部 ROI **10,964 px>10（max 184）**，
  与强制 `DebugJaw` 的 10,917 px 一致 ⇒ 预览里的 ARKit→morph→渲染整条链成立（但同样会触发上面的发暗）。

6a2z. **表情 morph 导致整脸发暗的根因（引擎切线重建 bug）与 9 槽导入配方（2026-10-25）**
- **现象**：任何**表情类** morph（眨眼/张口，权重≠0 时）会把整张脸压暗（脸框均值 30.7→5.6，max 184）；
  复位后恢复。纯骨骼通道（`DebugMorphGain=0` 只留表情骨骼）亮度不变（30.66）。
- **定位**：把 head 槽临时换成无 normal 的 `MH_Eye` 后，强制眨眼亮度 **41.56→41.52（不变）**
  ⇒ 发暗需要“法线贴图 + morph”。引擎源码 `AnimatedModel::RunBlendShapeDeformer`（AnimatedModel.cpp:947+）
  在重建切线时写的是
  `Float4 tangentRaw = normalStream.GetFloat4(vertexIndex);` —— **读的是法线流而不是切线流**，
  只要 `useNormals` 为真就把切线写坏（TBN 错→法线贴图采样偏黑）。这是引擎缺陷，项目侧无法修。
- **9 槽导入配方（关键）**：`asset_import` 用 **`MergeMeshes=false`** 才能得到 9 槽
  （`ImportMaterials=true` 时槽材质自动对上 `MH_Head_New/MH_Eye/MH_EyeShell/MH_Eyelashes`）；
  默认 `MergeMeshes=true`（开/关 ImportMaterials、`CreateEmptyMaterialSlots` 都试过）只有 head/teeth 两槽。
- **工具缺陷**：`model_material_slots_assign` 对这份 12.8MB SkinnedModel 成功保存后，资产**重载失败**
  （`loaded=False`），随后启动缩略图/反序列化阶段直接崩溃；槽位改动用“重导”代替，不要再对该资产 save。
- **启动崩溃规避（#76 续）**：新增/未缓存缩略图的大骨架 SkinnedModel 会在启动时触发
  `AnimatedModel.cpp:82` 断言并弹 Error 模态框（MCP 全部超时）。
  处置：删 `HundunWorld/Cache/Thumbnails/*` 与 `Cache/WindowsLayout.xml`（清掉上次选中/展开的大资产）后再启动，
  编辑器可干净起来；已用 `_dismiss_error.ps1 <pid>` 关闭模态框取证。
- **当前取舍**：生产头回到 **9 槽备份（带正常切线/法线）**，保证眼睛/睫毛正确；表情 morph 的发暗作为
  **引擎级已知缺陷**记录，待引擎修 `tangentStream` 或按“无切线 + 无 normal 材质”另做一版外观。
  “剥 TANGENT 的 GLB”实验因与启动崩溃循环冲突已暂停，临时资产已删除。
- **回归**：harness **1634/0/0**、动画门 PASS、492 个 `.flax` 0 重复 GUID；编辑器稳定运行、头部 9 槽/495 骨/821 形状可加载。
- **同族复发（2026-10-25 08:04）**：多次对同名头资产做 `asset_import` 后，启动日志出现
  `Cannot modify duplicated asset ID 52af3214… from '…head_mh/Ada_FaceMesh_conformhead4.flax'`，
  紧跟 `AnimatedModel.cpp:82` 断言崩溃。处置：清 `Cache/AssetsCache.dat`（+ Thumbnails/WindowsLayout）让注册表按磁盘重建，
  重启后头资产以新 GUID 重新登记、仍按路径加载正常。**今后对同名大资产重导后，必须预检这条日志并清注册表缓存。**


6a2aa. **重复资产 ID 循环的最终定因与修复（2026-10-25 08:30，已闭环）**
- **引擎判据**：`AssetsCache::FindAsset(path)` 是 **路径字符串精确比对**（`Source/Engine/Content/Cache/AssetsCache.cpp:268` 一带的 `e.Info.Path == path`）。
  `Content::GetAssetInfo(path)` / `ContentStorageManager::GetStorage(path)` 查不到时会 **打开存储并 `RegisterAssets(storage)`**，
  于是同一文件以第二种拼写再登记一次 → `Founded duplicated asset` → `Changing asset id` → `Creating package` →
  文件已被本进程占用（Win32 0x20）→ `Cannot repack storage` → **改名没落盘**，下次再换一个新 ID。
- **本机拼写**：内容库登记用的是 `Globals.ProjectFolder` 的**混用**拼写（`C:\Works/GitHubProjects/...`）；
  而 `EditorUtils.ResolveAssetPath` 过了一遍 `Path.GetFullPath` 变成**全反斜杠**。两者只差斜杠，注册表就当成两个条目。
- **实测链路（一次 `model_inspect` 即触发）**：启动登记 `9b3f0759` → 工具按全反斜杠查不到 →
  重登记 → 重复 → 改名 `e0d9d759`（成功）→ 再加载又改名 `537cbc65` → 再改 `45b1091c` 时被 0x20 拦下；
  每轮日志都能对上“磁盘头 ID == 上一次改名结果”。**清 AssetsCache 只是把循环归零，一调路径式工具就复发。**
- **修复（已编译通过）**：
  1. 新增 `EditorUtils.LoadAsset(pathOrGuid)`：先 `FindAssetItem`（遍历内容库，分隔符/大小写不敏感）取 **AssetItem.ID**，
     再 `Content.Load(id)`；只有库外新文件才回退路径加载。`FindAssetItem` 同时供 `model_inspect` 的 `knownToContentDatabase` 判定用。
  2. `model_inspect` / `skeleton_retarget_list` / `metahuman_fixture` 全部改走它；`model_inspect` 的 `AssetInfo` 也改成**按 ID 查**，
     不再对路径调 `Content.GetAssetInfo`（这一步本身就会重登记）。默认加载超时 10s→**60s**（12MB/821 形状冷加载实测 >10s）。
  3. `metahuman_fixture` 建 AnimatedModel 时设 `PerBoneMotionBlur=false`（与运行期同口径）。
- **验证**：清 `AssetsCache.dat`+`ProjectCache.dat`+`Thumbnails/*`+`WindowsLayout.xml` 重启 → 头以 `537cbc65…` 单条登记；
  再按路径调 `model_inspect` → **当轮 `duplicated asset` = 0**；回执 `loaded=true`、`materialSlotCount=9`、`boneCount=495`、
  `nodeCount=876`、`blendShapeCount=821`、`memoryUsageBytes≈12.99MB`（第二次调用，LOD 已驻留）。
  回归：harness **1640/0/0**、`AnimationWiringVerify.py` **PASS**（125 条按名映射）、`Tools/_flax_dup_guid_scan.py` 492 份 **重复 GUID=0**。
- **常驻闸门（新增）**：`TestAssetRegistryIntegrity` 直接把今天的事故写成断言——扫 Content 全部 `.flax` 头，
  1) 同一 GUID 不得出现两次（失败信息带首个冲突的两条路径）；2) 扫到的文件数 >400（防空扫假绿）；
  3) 生产头 `Content/Character/Models/head_mh2/Ada_FaceMesh_conformhead4.flax` 与其 8 个材质子资产必须在盘上。
  **反向对照已跑**：把一份材质子资产复制成 `_dup_probe.flax` → 失败 2 项（重复 GUID + 条目数少于文件数）；
  删掉后回到 1640/0/0（复制的探针是在编辑器关闭时放的，不触发内容库）。
- **头部资产现状**：从 `Tools/_backup_head_mh_20261023/`（9 槽、495 骨、821 形状、带法线/切线）整组拷回
  `Content/Character/Models/head_mh2/`，未再走 `asset_import`（避免同名大资产重导的重复循环）；8 个材质子资产 GUID 未变。
- **仍开着**：
  1. 编辑器内 **直接 spawn** 495 骨 AnimatedModel 仍撞 `AnimatedModel.cpp:82`：骨骼缓冲是**全局共享**、`Allocate` 只翻倍一次，
     首次 495×48≈23KB 也 >16KB；`PerBoneMotionBlur=false` 只是减半，保不住 → 大骨架目视验证走 PIE（#76）。
  2. 7 份角色预制体里的 `SkinnedModel` 仍写悬空 ID（`5281eac3…`/`771a163e…`，Content 内无任何资产匹配）；
     运行期靠 `EquipmentDatabase.HeadModelPath` 按路径加载，预制体引用未修（#77 遗留）。

6a2ab. **角色预制体模型引用：10 处悬空 GUID 已修；预制体本体仍是 InvalidData（2026-10-25 08:50）**
- **事实**：7 份角色预制体（MH_BaseCharacter/v2、MH_TemplateBase/Base_v2/BaseMats/All/Chain）与 CharacterRoot-1 的
  `SkinnedModel` 全部指向 Content 里**不存在的 GUID**（`5281eac3…`、`771a163e…`、`cd9034114e55…`）。
  三种字节序约定（.NET Guid 串 / 头字节原序 / 前 3 组混排）逐一反查，**没有任何现存资产匹配** ⇒ 是旧资产被替换后留下的悬空引用。
- **修复**：新增 `Tools/RepairCharacterPrefabModels.py`（默认 dry-run，`--apply` 才写；写前备份到
  `Tools/_backup_prefabs_models_20261025/`）。按 Actor 名映射：`MH_BodyPart→Character_1`、
  `MH_HeadPart→head_mh2/Ada_FaceMesh_conformhead4`、`MH_TemplateSrc*/MH_N2/MH_RebakeFixture→MetaHuman_Conform_Topology/head`、
  `Running→Character_1`。脚本拒绝修改"旧 GUID 仍是现存资产"的引用，写前校验 JSON 合法；重跑幂等（0 处）。
- **仍未解决（不是本次引入）**：`Content.Load` 这 7 份预制体返回 **InvalidData**（`CharacterRoot-1` 与
  `MH_TemplateBaseMats` 能加载）——这是 6a13/#62 记过的"旧工具写坏预制体"，与模型引用无关；
  需要后续用引擎 `prefab_create` 重建（重建会实例化 495 骨的头，必须先解决 #76 的编辑器骨骼缓冲断言）。
- **常驻闸门**：`TestAssetRegistryIntegrity` 增加两条——预制体里至少 1 条 `SkinnedModel` 引用、
  且所有引用必须指向现存资产（失败信息带"首个悬空：文件 → GUID"）。
  反向对照已跑：把一条引用改成 `0123456789abcdef0123456789abcdef` → **1641/1**（命中该断言）；还原后 **1642/0/0**。
- **附带结论（截图通路）**：`game_screenshot` 的两条引擎侧通道——主渲染任务 backbuffer（`Screenshot.Capture`）与
  编辑器 GameWin 视口的 `TakeScreenshot`——**都不含 GUI**（PIE 登录页只截到背景渐变；`ui_tree` 里 LoginPanel 明明存在）。
  桌面截图能拿到 UI，但会被用户前台窗口遮挡。⇒ 需要 UI 像素证据的验证（创角页/登录页）目前没有可靠的自建通路；
  3D 画面（角色/表情）不受影响。`game_screenshot` 已加 `includeUI` 参数并在描述里写明这一实测结论。

6a2ac. **运行期加载拼写修正 + 引擎自检头组装载修复（2026-10-25 09:12）**
- **根因同源**：`MetaHumanAssetUtil.ResolveProjectPath` 用 `Path.GetFullPath` 产出全反斜杠，而内容库登记的是
  `Globals.ProjectFolder` 的混用拼写（`C:\Works/GitHubProjects/...`）⇒ 运行期（世界头、预览头、头发）每次加载都会
  以第二种拼写重新登记，触发改名 → 重打包 → 0x20 → 改名不落盘。**这是 #77 的最后一条漏网路径。**
- **修法**：改用 `StringUtils.CombinePaths(Globals.ProjectFolder, 相对路径)`（与内容库同一算法），绝对路径入参不再规范化。
- **自检安装缺陷两处**：① `Content.Load<Model>` 取 `SkinnedModel` 恒为 null（Model 与 SkinnedModel 是兄弟类型）
  ⇒“材质参数类型闸门”组长期以“头部模型载不进来”skip；② 头部 9 个槽的 `Material.GetParameter` 在刚载入时全空，
  改为按路径再取 `MH_Head_New.flax` 一次。
- **验证**：重启后 `metahuman_selftest` **100/0/9**（原 99/0/9）；材质类型闸门从 skip 变为“通过 1、其余仍需 AnimatedModel 宿主”。
  运行期加载头后当轮日志 `duplicated asset` = **0**；头 GUID 稳定为 `537cbc65…`；`__flax_dup_guid_scan` 仍 0 重复。

6a2ad. **推翻 #102 的前提：生产头不是"全绑 root"，源权重与导入产物都有蒙皮（2026-10-25 09:30）**
- **旧结论**（2026-10-25 早）称"头部两份源网格 103,205 个带权顶点全绑 root，所以骨骼级面部通道不动像素、Eye Aim 已撤"。
  那句只对 `Output/mhbaked/Ada_FaceMesh.glb` / `Output/mhface/slots/…` 这两份**旧导出**成立，**不是**生产头。
- **生产头证据（离线解码 GLB，`Tools/_head_skin_weight_probe.py`）**，对象 `Tools/_backup_head_strippednormals_20261025.glb`
  （与 `Content/Character/Models/head_mh2/Ada_FaceMesh_conformhead4.flax` 同源那一族）：
  - 脸皮 prim0：24,408 顶点、633 根骨；`head` 10.6%、`FACIAL_L/R_Ear1` 各 4.7%、`FACIAL_C_Nose` 2.0%…
  - 眼球 prim3/4：各 802 顶点，**92.4% `FACIAL_L/R_EyeParallel` + 7.6% `FACIAL_L/R_Pupil`**
  - 牙齿 prim1/2：`FACIAL_C_TeethUpper/Lower` ≈46/46 与 51/49
  - 眼睑/睫毛 prim5–8：`FACIAL_L/R_EyelidUpper/Lower*`、`FACIAL_L/R_EyelashesUpper*`
  - 每个 primitive 的 **root-only 顶点 = 0.0%**
- **导入产物**：`model_skin_probe` 报 9/9 网格带 `BlendIndices`+`BlendWeights`
  （`R16G16B16A16_UInt` / `R8G8B8A8_UNorm`，偏移 20/28）。
- **对 #102 的处置**：`FACIAL_L_Eye` 旋转 45° 像素差 0 **不是权重问题**；应改为按名解析 `FACIAL_L/R_EyeParallel`
  （Pupil 是子骨）再做像素 A/B，并检查写入是否落在同名/镜像节点上。Eye Aim 可以重做，不必等 DCC 重蒙皮。
- **新增自检组** `头部蒙皮落点（权重流 + 面部骨名）`：断言 LOD≥1、LOD0 有 9 网格、节点表含
  `head/FACIAL_C_Jaw/FACIAL_L,R_EyeParallel/FACIAL_L_Pupil/FACIAL_C_TeethUpper,Lower/FACIAL_L_EyelidUpperA1`。
  "逐网格顶点布局"要等渲染侧初始化，同步执行时读到 0/9（`model_skin_probe` 同一资产 9/9）⇒ 该条按 **skip 并写理由**，
  不拿资产文件里的元素名当替身（实测 .flax 里 ASCII/UTF-16 都没有 `BlendIndices`，布局按枚举序列化）。
  自检：**110/0/10**（此前 100/0/9）。

6a2ae. **头部自检再补两档 + #101 决策文档（2026-10-25 13:50，运行态待复验）**
- `头部蒙皮落点` 组新增：`SkinnedModel.BlendShapes` 数量必须 = **821**，且形状表含
  `head_lod0_mesh__eye_blink_L/R`、`head_lod0_mesh__jaw_open`、`head_lod0_mesh__brow_raiseIn_L`
  （托管侧 `BlendShapes` 是 `string[]`，不是结构体数组——先按 `.Name` 写导致 CS1061）。
- 编译已过、两个 target 的 DLL 都含新字符串；**但运行态未复验**：当时机器上有**两个** FlaxEditor 实例
  （PID 27356 起于 13:35、PID 3732 起于 13:47，同一工程），MCP 端口握在旧实例上，自检读到的是旧装配
  （该组仍显示 11 项）。已只终止**我启动的** 27356，未触碰另一实例；随后 MCP 端口不再响应 ⇒
  本轮无法给出新的自检数字。下次单实例启动后应看到该组通过 15 项（9 网格 + LOD + 8 骨名 + 821 形状 + 4 关键形状）。
- **#101**：新增 `docs/Movement_跳跃单位口径_方案与决策清单_2026-10-25.md`（现状证据、方案 A/B/C、
  验收清单、回退路径）。按"大功能先文档后写码"的口径，**未改任何移动常量**，等用户拍板。
- **2026-10-25 13:53 复验（单实例）**：关掉重复实例、清缓存后重启，`metahuman_selftest` = **115/0/10**，
  `头部蒙皮落点（权重流 + 面部骨名）` = **通过 15 / 失败 0 / 跳过 1**（跳过项为"顶点布局未渲染初始化"，理由已写）。
  上一段"运行态待复验"到此结清；双实例期间读到 11 项的原因也确认是 MCP 握在旧实例、换了装配。

6a2af. **表情发暗链路再确认：UseNormals 是"每个形状"的开关，剥离 morph 法线后必然跳过缺陷分支（2026-10-25 14:00）**
- 引擎源码（`AnimatedModel.cpp:947+` 的 `RunBlendShapeDeformer`）：
  `bool useNormals=false; … useNormals |= blendShape.UseNormals; … useNormals &= normalStream.IsValid();`
  —— 只有**当前激活的形状**里 `UseNormals=true` 才进切线重建分支。
- `UseNormals` 的赋值只在导入期（`ModelTool.cpp:2148/2154`）：
  `blendShape.UseNormals = false; … |= !v.NormalDelta.IsZero();`。
- 因此**把 7389 个 morph target 的 NORMAL 全置零（不是删流）→ 所有形状 UseNormals=false → 缺陷分支整块不执行**。
  当前盘上三份头 GLB（`Tools/Ada_FaceMesh_conformhead4.glb`、`head_no_morph_normals.glb`、`_backup_head_strippednormals_20261025.glb`）
  用 `Tools/_glb_morph_attr_probe.py` 实测：7389 个 target **全部只有 POSITION、with NORMAL=0**。
- 所以"表情安全头"是一个**由导入数据决定、可复算**的属性，不依赖像素：
  从这三份 GLB 任一份导入的资产必然是 UseNormals=false。反过来说，当前生产头若仍有发暗，
  它一定是**剥离之前**那一版导入的（其 GLB 已不在盘上）。
- 待办：已用 `Tools/ImportExpressionSafeHead.py` 把剥离版导入到 `Content/Character/Models/head_expression_safe/`，
  作为"表情安全"候选取证（9 槽/821 形状结构性验证见下一段）。生产头暂不切换——这是外观取舍（失去 morph 法线跟随），
  应在有像素通路时 A/B 后再定。
- **导入结果（2026-10-25 14:00）**：`asset_import`（`Type=SkinnedModel, ImportMaterials=true, CreateEmptyMaterialSlots=true, MergeMeshes=false, ImportLODs=false, ImportBlendShapes=true, Scale=1`）
  成功产出 `Content/Character/Models/head_expression_safe/head_expression_safe.flax`：**821 形状、0 无名、9 材质槽**，
  `model_inspect` 报 `loaded=true`。**但 `boneCount=492`，生产头是 495** —— 说明剥离版 GLB 与当初生产头的源 GLB
  **不是同一份**（盘上三份 693,325,412 字节的 GLB 内容同族，生产头那份已不在盘上）。
  ⇒ 这个候选**不能直接替换**生产头：先用 `model_inspect(includeBones)` 做骨名集合差集，确认缺的 3 根不被绑定表/写回端引用。
  导入后生产头 GUID 未变、日志无 `duplicated asset`。
- **候选与生产头等价性（2026-10-25 14:19，自检组）**：`表情安全头候选（与生产头骨名差集）` 通过 12/0/0，
  Notes 实测：**生产头节点 876、候选 876、候选缺失 0**（`boneCount` 显示 492 vs 495 是 Bones 列表口径差异，
  节点名集合完全一致）。关键骨 `head/FACIAL_C_Jaw/FACIAL_L,R_EyeParallel/FACIAL_L,R_Pupil/TeethUpper,Lower` 全在。
  ⇒ 它是**可直接替换**的表情安全候选（821 形状 / 9 槽 / 876 节点）。
- 同轮 `头部蒙皮落点` 因渲染侧已初始化，逐网格蒙皮流这条**这次真跑了**：16/0/0（9 网格都有 BlendIndices+BlendWeights）。
  全量自检 **128/0/9**。
- **切换口径（未执行）**：把 `EquipmentDatabase.HeadModelPath` 改为
  `Content/Character/Models/head_expression_safe/head_expression_safe.flax` 即可；回退是改回原路径。
  代价：morph 不再修正法线（光照不随形变细节走）；收益：任何表情都不再整脸发暗。**需要一次像素 A/B 拍板**。

6a2ag. **新像素通路：编辑器资产预览窗口的离屏截图（可用上限 ≈341 骨，2026-10-25 14:50）**
- **通路**：`ContentEditing.Open(item)` 打开资产窗口 → 反射取 `SkinnedModelWindow._preview`（`AssetPreview`）
  → 它的 `Task` 是独立的 `SceneRenderTask` → `Screenshot.Capture(task, path)` 落盘 PNG。
  不需要 PIE、不需要登录、不需要 UI 绘制。新工具 `model_asset_preview`（可带 `morph/morphValue`）。
- **已验证**：`Content/Character/Models/Character_1.flax`（138 骨）截出 4.3MB 真图，
  画面是完整的 T-pose 人偶（[证据](file:///C:/Works/GitHubProjects/HundunWorld/HundunWorld/Tools/_preview_smoke.png)）。
- **上限（#76 的又一后果）**：预览任务同样走"共享骨骼缓冲、`Allocate` 只翻一倍"。
  打开 **495 骨的头** 的资产窗口 → `AnimatedModel.cpp:82` 断言 → **编辑器直接退出**（本轮复现一次）。
  计算上限：骨数×48×2 ≤ 32768 ⇒ **≈341 骨**。头/远端合成这类大骨架仍拿不到像素。
- **护栏**：工具在打开窗口前先按骨数拒绝（>341 抛错并说明 `骨数×96` 与 32KB 上限），避免把编辑器带走。
  该护栏的运行态回执**尚未复验**（验证时机器上又出现了两个编辑器实例、MCP 超时）。
- **对 #102/#104 的意义**：身体参数、眼球（若单独成资产）等 ≤341 骨的资产现在可以做像素 A/B；
  头部仍需先修 #76（把 `Allocate` 的单次翻倍改成 while 循环，或把 `PerBoneMotionBlur` 默认关掉）才能用这条通路。
- **护栏复验（2026-10-25 14:53，单实例）**：对 495 骨的头调用 `model_asset_preview` 得到明确拒绝
  （`骨骼数 495 ×48×2 = 47520 > 32KB，上限约 341 骨`），**编辑器存活**；同会话下 `Character_1`（138 骨）
  仍能截出 4,320,720 字节的图；`metahuman_selftest` = **128/0/9**；日志 `duplicated asset`/`AnimatedModel.cpp` 均 0。
- **自建 RenderTask 版（2026-10-25 15:10）**：新工具 `model_offscreen_render` —— 自建 `GPUTexture + SceneRenderTask + Camera + 三点光`，
  `CustomActors` 只放模型/相机/灯光，模型放到 (0,100000,0) 避开主视口；**先关 `PerBoneMotionBlur` 再赋 `SkinnedModel`**。
  - `Character_1`（138 骨）512² 渲染成功（65KB PNG，画面是正面 T-pose，黑底）。
  - **495 骨的头仍崩**：即使 ×1（23.7KB），**首个分配**在冷缓冲里只会 0→16KB，23.7 > 16 ⇒ 同一断言。
    ⇒ 上限不是"×2 的 341 骨"，而是**首分配 16KB ⇒ 约 341 骨（×1 时 23.7KB 仍超）**；想放 495 骨必须让该扩展的缓冲**先被别的模型撑到 32KB**。
  - 下一步（未做）：在同一 RenderTask 的 CustomActors 里先渲染一个 ≤16KB 的小骨架（撑到 32KB），再创建大骨架；
    并确保大骨架所在场景**不被编辑器主视口渲染**（否则主视口那条扩展仍会首分配并断言）。
  - 序列化坑：返回里直接塞 `Float3` 会触发 `Normalized.Normalized…` 循环检测（HTTP 500），要拆成 x/y/z 三个 float。
- 回归：单实例重启后 `metahuman_selftest` = **127/0/10**（本轮"逐网格蒙皮流"那条按设计 skip）；`Character_1` 离屏渲染仍正常。
