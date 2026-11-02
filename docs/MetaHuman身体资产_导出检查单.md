# MetaHuman 身体资产导出检查单（给 DCC / UE 侧）

> 结论先行：`Output/mhbaked/Ada_BodyMesh.glb`（及其 4 影响烘焙版）**不能用于捏脸预览的身体**。
> 运行时侧已全部打通（写回目标发布、页签切换、骨骼写入 24 根、`body.height` 的骨骼偏移随参数变化），
> 差的只是一份满足下面五条的干净导出。本文每条都附 2026-10-19 的实测证据。

## 一、必须满足的五条

| # | 要求 | 为什么（实测证据） |
|---|------|------------------|
| 1 | **网格与骨架同一空间、同一比例** | 现文件网格头顶 `y=0.9656m`，骨架 `head` 关节在 `y=1.4336m`（约 **0.67×**）。同一文件里两套尺度，任何骨骼运动都会让网格与骨架错位。 |
| 2 | **每顶点影响 ≤ 4，且权重之和 = 1** | 现文件权重分在 `JOINTS_0..2` 三组（uint8）：**5,730 个顶点**有 >5% 权重落在第 1 组，其主导关节几乎全是手部骨（`middle_metacarpal_*` / `index_*` / `ring_*`）。引擎蒙皮按 4 权重导入，手部权重被丢 ⇒ 手飞在身体两侧。 |
| 3 | **不得包含头/颈以上几何**（或另出一个无头变体） | 现文件手臂与头处在**同一高度带**：按颈线（`neck_01` y=1.3299）切掉颈线以上会删掉 **23.5% 的面**，而这些顶点的主导关节是 `hand_l/hand_r/index_*` ⇒ 切头必连带切手。 |
| 4 | **UV 与目标皮肤材质一致**（或直接带上皮肤材质） | 把 `Content/Character/Materials/MH_Head.flax` 指到身体第 0 槽（回执 `saved:true`）后，渲染仍是纯白 ⇒ UV 采样不到头部材质。 |
| 5 | **正立 + 同一朝向**（与 `body.flax` 同向） | 预览侧已按 `TemplateFbxPose()`（Ry180 补偿离屏翻转）统一姿态；导出若另有一套朝向，需要单独补偿。 |

## 二、验收方式（不需要人眼）

1. **离线**：`Tools/_body_skin_diag.py`（绑定一致性 + 顶点按主导关节分桶）应满足
   - 关节 `W @ IBM ≈ I` 的最大偏差 `< 1e-3`（现文件通过）；
   - 每个顶点权重和 `= 1`（现文件不通过：总和 ≈ 1.5×）；
   - 顶点影响数 ≤ 4（现文件不通过）。
2. **引擎内**：把 `EquipmentDatabase.TemplateSkinnedBodyModelPath` 指到新资产、`UseSkinnedBodyForPreview = true`，
   起 PIE 后在捏脸页切「身体」，用桥接工具 `ui_preview_screenshot` 存图；
   同时拖 `body.height`，回执里 `SkinnedBody{... offSum=...}` 应随参数变化（现文件已满足：152.9→24.59、187.5→10.00）。
3. **必须同时满足**：截图里**无悬浮手、无灰蛋头**，且 `body.height` 变化时包围盒/偏移随之变化。

## 三、接上干净资产的操作（一步）

1. 新 GLB 放进 `Output/mhbaked/`；
2. `asset_import`（`Type=SkinnedModel`、`OptimizeMeshes=false`）导入到
   `Content/Character/MetaHuman_Conform_Topology/`；
3. 改 `EquipmentDatabase.TemplateSkinnedBodyModelPath` 指向新资产、把 `UseSkinnedBodyForPreview` 改为 `true`；
4. 按上一节验收。

## 四、当前盘上状态（便于回退/对照）

- `Ada_BodyMesh.flax`：原始导出（1 材质槽，Material 0 为空材质）。
- `Ada_BodyMesh4.flax`：**4 影响烘焙 + 权重归一化**版（手从拉丝变实心，但空间/切头问题仍在），
  材质槽已指向 `MH_Head.flax`。
- 预览默认 `UseSkinnedBodyForPreview = false`：身体页签显示**静态模板身体**（视觉干净，但身体参数不可见地生效——
  静态网格无骨架）。这是**已知折中**，不是修好。
