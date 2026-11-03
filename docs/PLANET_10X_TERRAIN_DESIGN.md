# 10 倍地球半径星球地形方案（路线 C：全球坐标 + 局部切平面流送）

- 日期：2026-11-01
- 状态：方案评审稿（P0 前）
- 适用工程：HundunWorld（Flax 1.13 + Orleans/ECS）
- 修订：2026-11-01 追加 §12「真实地球基场（GEBCO_2026 15″）」；§4.1 的第 1 层大陆掩码由真实数据替换，其余层不变。
- 已拍板：
  1. **半径 10 倍地球**：R = 63,710 km，赤道周长 400,300 km，表面积 5.10×10¹⁰ km²。
  2. **路线 C（混合）**：全球双精度坐标 + 客户端局部切平面 + 近景 Flax Terrain + 中远景立方体球 LOD。
  3. **允许按需重建 Flax 引擎并开启 `USE_LARGE_WORLDS`**，但不作为 P1 的前置条件。
  4. **地形权威与缓存按 P1 协议设计**：P1 采用“客户端分块缓存 + 双端共享确定性高度采样器”，不引入地形瓦片服务；协议中保留 `terrainSeed/terrainVersion/tileKey`，后续可无缝切换为服务端瓦片源。

---

## 1. 目标与非目标

### 1.1 目标

- 支持半径 63,710 km 的闭合星球坐标；玩家可在任意区域落地、移动、跨区、传送。
- 客户端始终在 **局部切平面** 中渲染与物理，Flax 的 float32 世界坐标永远不会出现 10⁹ cm 量级。
- 近景地形有碰撞、可刷怪、可寻路；中远景只负责视觉与地平线；全球视图/地图单独渲染。
- 服务端权威位置、AOI、持久化都使用全球坐标；网络包使用区域局部坐标，避免每帧传 64 位全局值。
- P1 不依赖 Flax 引擎重建；`USE_LARGE_WORLDS` 作为 P3 的可选增强保留。

### 1.2 非目标（P1 明确不做）

- 不做真球面重力、球面角色控制器、环球无缝步行（这些属于 C2，见 §10）。
- 不做全球手工内容；P1 只做程序化荒野 + 少量测试 POI。
- 不做地面到太空的单场景连续飞行；轨道/星球视图用独立缩放场景。
- 不做服务端地形瓦片服务；P1 由双端同一套高度函数生成，缓存留在客户端。

---

## 2. 总体架构

```
┌────────────────────────────── 服务端（Orleans） ──────────────────────────────┐
│  PlanetRegionGrain（区域权威）  AOI/兴趣管理  全局坐标持久化  共享高度采样器   │
└───────────────▲───────────────────────────────────────────────▲──────────────┘
                │ 区域局部坐标 + RegionId（P1 协议）              │ 同一份高度函数
┌───────────────┴───────────────────────────────────────────────┴──────────────┐
│ 客户端 PlanetRuntime                                                          │
│  ├─ RegionFrame / WorldOrigin（double 全局原点，float 局部坐标）              │
│  ├─ TerrainStreamer（近景 5×5 Flax Terrain + 中景四叉树 + 远景 globe）        │
│  ├─ PlanetTileCache（磁盘 LRU，seed/version 失效）                            │
│  ├─ PlanetPhysics（局部 PhysX + heightfield，局部 -Y 重力）                   │
│  └─ PlanetNavigation（按区域 Recast tile，区域图连接）                        │
└───────────────────────────────────────────────────────────────────────────────┘
```

分层职责：

| 层 | 职责 | 关键约束 |
|---|---|---|
| 全球坐标层 | ECEF int64 厘米、RegionId、区域原点/切平面基 | 双端必须逐位一致 |
| 区域层 | 服务端权威、AOI、跨区移交、持久化 | 区域局部 float 不超过 ±4.5 km |
| 局部渲染层 | Flax 场景、近景 Terrain、中远景 mesh | 不出现全局绝对 float |
| 地形生成层 | 高度函数、tile 生成、缓存、LOD | 高度函数带版本，缓存必须可失效 |
| 物理/导航层 | 近景 heightfield、区域 navmesh | 物理和导航都只处理局部 |

---

## 3. 坐标与区域

### 3.1 全球坐标

- 权威坐标：`PlanetPoint` = ECEF（地心直角坐标）**int64 厘米**（x, y, z）。
  - 10 倍地球半径 6.371×10⁹ cm，int64 余量充足。
  - 1 cm 精度满足角色/载具；速度、朝向用 float/double 单独表达。
- 地形寻址：立方体球（cube-sphere）六面 + 四叉树 tile key。
- 区域寻址：`RegionId` 打包为 int64：

```
RegionId = (face 3 bits) << 58
         | (level 4 bits) << 54
         | (x 27 bits) << 27
         | (y 27 bits)
```

  - face：0–5；level：0–15；x/y：0–2²⁷−1。
  - P1 采用 **level 14**。**实测**（`Horizon.Planet.Tests/RegionMetricReportTests`，cube-sphere 的瓦片度量在面中心和面边缘不同）：
    | 瓦片位置 | 宽 × 高 | 中心到最远角 | 该处曲率下垂 |
    |---|---|---|---|
    | 面中心 | 7.777 × 7.777 km | 5.499 km | 0.237 m |
    | 面边缘 | 3.889 × 5.499 km | 3.368 km | 0.089 m |
    | 面角 | 3.666 × 3.666 km | 3.175 km | 0.079 m |
    最窄瓦片 3.666 km > 近景 5×5 patch 窗口 2.540 km，所以**一片近景永远装在一个区域里**（判据 `TerrainWindow_FitsInsideTheSmallestRegion`）。
  - 每个面 level 14 有 16384×16384 个区域；全球约 1.6×10⁹ 个区域，按需稀疏创建。
  - 早期稿写的“level 13 ≈ 8.98 km”是按立方体面边长直接相除估的，忽略了球面投影的面内外尺度差，作废。

区域原点与切平面基由 `RegionId` **纯函数**计算，双端不传也不缓存“区域元数据”：

- `GetRegionOrigin(RegionId)` → ECEF int64 厘米（区域中心、海平面半径处）。
- `GetRegionFrame(RegionId)` → `{origin, east, up, north}`（double）。
- 区域内的局部坐标：`local = global - origin`，投影到 `(east, up, north)` 得到 `(x, y, z)`，单位为 cm，存 float。

### 3.2 局部坐标系（Flax）

- Flax 是 Y-up 左手系；局部坐标按实现口径为：
  - `Y` = 区域中心地表法线（up）；
  - `X` = 东向切线；
  - `Z` = **南向**切线（即 **北 = −Z**，与工程既有导航/地图口径一致，`Horizon.Planet` 的 `RegionFrame.South` 就是这个轴）。
- 近景 Terrain/角色/相机都挂在这个局部坐标系下；区域切换时整体换帧。
- 当前工程的 ECS 是 Z-up、Flax 是 Y-up，转换只允许在 **Flax 边界**做一次；不要在 Terrain/网络/物理之间来回换轴。现有转换见 `HundunWorld/Source/Game/HundunWorldGame.cs` 的 `SampleGroundHeightEcs`。

### 3.3 精度预算

float32 在局部坐标下的 ULP（1 单位 = 1 cm）：

| 距区域中心 | ULP |
|---:|---:|
| 100 m | 约 0.008 mm |
| 1 km | 约 0.06 mm |
| 4.5 km | 约 0.3 mm |
| 9 km | 约 0.6 mm |
| 100 km | 约 1 cm |
| 1,000 km | 约 8 cm |
| 地球半径 6,371 km | 约 64 cm |
| 10× 半径 63,710 km | 约 5.12 m |

结论：

- 区域（level 14）中心到最远角实测 5.499 km，局部坐标最远 ±5.5 km 时 float32 的 ULP 约 0.06 mm，**不需要 large worlds 也能做角色/物理**（判据 `LocalGlobal_RoundTrips_WithinHalfHundredthOfACentimetre`：双精度往返 < 0.005 cm）。
- 全局绝对坐标绝不能进入 Flax 的 `Vector3`；只在 double/int64 层运算。

### 3.4 曲率与“局部平面”

10× 地球半径很大，局部切平面的曲率下垂近似 `sag ≈ d² / (2R)`：

| 距区域中心 d | 下垂 |
|---:|---:|
| 1 km | 0.008 m |
| 2.5 km | 0.049 m |
| 4.5 km | 0.159 m |
| 9 km | 0.636 m |

因此：

- 近景 5×5 patches（2.54 km）内，单个 patch 角点的下垂只有 4 mm，肉眼不可见。
- 区域最远角（实测 5.499 km）下垂 0.237 m；`PlanetPatchSampler` 直接用 `(R + h)·cos∠ − R` 精确换算，曲率项不需要手工近似，也保证近景平面与远景球面接得上（判据 `SpherePoint_LocalY_EqualsNegativeCurvatureSag`、`ConstantElevation_AtFrameOrigin_IsExact_AndDipsByCurvature`）。
- **C1 不需要球面重力**：区域尺度 5.5 km 对 63,710 km 半径，重力方向偏差约 0.005°。

### 3.5 原点重定（rebase）

- 客户端维护 `WorldOrigin`（ECEF int64 cm）与 `RegionFrame`。
- 玩家局部坐标任一分量超过 **2 km**，或接近区域边界（距边界 < 1 km）时触发：
  1. 预加载目标区域/邻区地形；
  2. 服务端确认区域移交；
  3. 切换 `RegionId/WorldOrigin/RegionFrame`；
  4. 平移已加载 actor、物理场景原点、相机；重建局部坐标；
  5. 用 1–2 帧的视觉过渡（雾/淡入）隐藏切换。
- 相邻区域切平面夹角 ≈ 面中心瓦片宽 7.777 km / 63,710 km = 1.22×10⁻⁴ rad（0.0070°），可以直接切换；不要做整场景旋转插值，收益极小、风险高。

---

## 4. 地形生成与流送

### 4.1 高度函数

`HeightAboveSphere(face, u, v)`，返回海平面以上的厘米高度；分层：

1. 大陆/海盆掩码 —— **改为真实地球基场**（GEBCO_2026 15″，见 §12）；不再用低频噪声生成大陆轮廓；
2. 山脉脊线（ridged multifractal）；
3. 丘陵/河谷（fBm）；
4. 高频细节（只在近景采样，避免远景闪烁）；
5. 海平面、湖泊/河流掩码（海陆界直接取基场 `h = 0`，见 §12.5）。

第 2–4 层的**起始波长由基场决定**：真实数据只能提供 λ ≥ 9.27 km 的信息，2–4 层必须从 9.27 km 往下铺到 1 m，且不能重复计低频，否则地形会双重起伏（§12.4）。

要求：

- 使用 **整数哈希噪声 + 固定点/双精度插值**，避免平台/运行库差异；不要用 `MathF.Sin` 之类的跨平台浮点函数作为主噪声。
- 函数带 `terrainSeed` 与 `terrainVersion`；版本变更即缓存全失效。
- 双端共享同一份 `Horizon.Planet` 库（建议 netstandard/net10，无 Flax 依赖）。
- 局部高度 = `HeightAboveSphere(worldPoint) - curvatureSag(distance)`；近景 Terrain 高度图存这个值。

### 4.2 LOD 分级

| 层级 | 范围 | 载体 | 水平分辨率 | 碰撞 |
|---|---|---|---|---|
| 近景 | 玩家周围 0–2.5 km | Flax Terrain，5×5 patches | 1 m（509²/508 m） | 有 |
| 中景 | 2.5–50 km | cube-sphere 四叉树自定义 mesh | 4–16 m | 无 |
| 远景 | 50 km–全球 | 低模 cube-sphere / globe | 32–500 m | 无 |
| 轨道/地图 | 全球 | 独立缩放场景（1 单位 = 1 km） | 屏幕分辨率级 | 无 |

- 近景固定 5×5 是“流送窗口”，不是区域大小；玩家移动时按 patch 粒度增删。
- 中景四叉树按屏幕空间误差选 LOD，节点带裙边（skirt）遮裂缝。
- 远景只负责地平线/球面轮廓；进入轨道或打开世界地图时切到独立场景，避免和地面相机共用远平面。

### 4.3 近景 Flax Terrain

- 初始化：`Terrain.Setup(lodCount: 6, chunkSize: 127)`，然后按 5×5 添加 patch。
- 高度：`SetupPatchHeightMap(ref patchCoord, float[509*509])`；返回值 **true 表示失败**。
- 贴图：`SetupPatchSplatMap`，2 张 splatmap × 4 层 = 8 层；每 chunk 可用 `SetChunkOverrideMaterial` 覆盖材质。
- 运行时创建的 patch 只能虚拟存储（RAM），没有磁盘流送；必须自己做 tile 缓存与内存淘汰。
- 高度 **只写不读**：`SetupPatchHeightMap` 之后引擎不提供 getter；必须保留自己的高度数组，供：
  - 刷怪/植被/POI 落点；
  - LOD/接缝采样；
  - 服务端权威校验；
  - 中景 mesh 与近景 patch 的一致性。
- `AddPatch/RemovePatch` 可运行时调用；`AddPatch` 的碰撞烹饪是同步的，流送时要限帧、预生成、避免一次加 25 个。
- 单 patch 预算：高度 1.04 MB + 2 张 splatmap 2.07 MB + 碰撞/GPU ≈ 4 MB；5×5 ≈ 100 MB，9×9 ≈ 320 MB。默认只保留 5×5，第二圈用中景 mesh。

### 4.4 中远景自定义网格

- 每个四叉树 tile 用 65×65 顶点（64×64 quad）+ 1 像素边界采样，避免接缝。
- 顶点数据：位置/法线/UV ≈ 32 B/顶点 → 约 135 KB/tile；索引约 98 KB/tile；可见 200 个 tile ≈ 47 MB。
- 生成流程：后台线程采样高度 → 主线程 `Mesh.UpdateMesh` → 一个 `StaticModel` 或自定义 Actor 渲染。
- Flax 一个 `Model` 只有一个可写网格槽，不能把多个 tile 塞进一个 Model；接受每 tile 一个 draw call，或用自定义渲染任务合批（P2 再优化）。
- 中景不生成碰撞；玩家落到中景区域时，由 `TerrainStreamer` 把近景 patch 重新居中，期间用高度函数兜底。

### 4.5 接缝、水面与大气

- 高度函数是唯一真相：近景 Terrain、中景 mesh、远景 globe 都在同一世界坐标采样。
- 近景/中景重叠至少 1 个 patch，用裙边 + 距离雾/透明度淡出遮接缝。
- 水面：全球海平面用一个球壳；近景局部用与切平面贴合的水面材质/几何；不做全球流体模拟。
- 大气/云层：远景 globe 的独立材质/壳层；P2 再做。

---

## 5. 客户端运行时

### 5.1 建议组件

| 组件 | 职责 |
|---|---|
| `PlanetRuntime` | 持有 RegionFrame、WorldOrigin、当前 RegionId；驱动 rebase |
| `PlanetMath`（共享库） | ECEF/cube-sphere/RegionId/切平面基/高度函数 |
| `TerrainStreamer` | 近景 patch 窗口、中景四叉树、生成队列、内存预算 |
| `NearTerrainManager` | 创建/销毁 Flax Terrain、SetupPatchHeightMap/SplatMap、碰撞环 |
| `FarTerrainRenderer` | 中景 mesh / 远景 globe / 轨道场景 |
| `PlanetTileCache` | 磁盘 LRU、原子写、版本失效、校验 |
| `PlanetPhysics` | 局部 PhysX、heightfield、地面采样兜底、重力 |
| `PlanetNavigation` | 区域 navmesh 构建/缓存、区域图 |
| `PlanetNetwork` | 区域协议、区域移交、AOI 订阅 |

### 5.2 每帧顺序

1. 读输入 → 本地预测（局部坐标）；
2. 更新 RegionFrame / 判断 rebase；
3. 计算流送请求（近景 patch / 中景 tile）；
4. 后台生成完成的结果在主线程上传（限帧）；
5. 物理/角色/动画；
6. 渲染（近景 Terrain + 中景 mesh + 远景）。

主线程只做上传与 actor 生命周期；高度采样、网格构建、splat 计算全部放后台。

### 5.3 相机与远平面

- 当前工程视距上限 150 m（`GameConfigurationService.ViewDistanceToFarPlane`），必须提高到 **20–50 km** 量级；先按 30 km 调，实测深度精度。
- 地面相机：近平面 0.05–0.1 m，远平面 30–50 km；Flax 支持反向深度，但仍要做深度/阴影实测。
- 若出现深度冲突，再拆“近相机 0–2 km + 远相机 2–50 km”两层，用渲染层合成；P1 先不拆。
- 轨道/地图：独立场景，1 单位 = 1 km，相机绕星球；不要复用地面相机。

### 5.4 物理与导航

- 物理只存在于近景：5×5（或 3×3）patch 的 heightfield；中远景无碰撞。
- `PhysicsScene.Origin` 可用于局部原点管理；但 PhysX 内部仍是 float，**不要让物理 actor 离开局部窗口**。
- 地面采样：优先 `Physics.RayCast`；流送未完成/无碰撞时回退到 `HeightAboveSphere`，避免 NaN/掉穿。
- 导航：按区域构建 Recast tile（`Navigation.BuildNavMesh(BoundingBox, ...)`），缓存到磁盘；区域之间用 portal/off-mesh link；不要建全球 navmesh。
- 现有 `HundunWorldGame.SampleGroundHeightEcs`、`PositionComponent`、`WorldManager` 都是 float 局部语义，P1 保持它们不变，只在外层加 RegionFrame 转换。

---

## 6. 服务端与 P1 协议

### 6.1 区域权威

- 每个 `RegionId` 对应一个 `PlanetRegionGrain`；玩家/怪物/掉落属于当前区域。
- 玩家跨区：服务端做区域移交，旧区保留一小段重叠（例如 2 s），新区接管；客户端预加载后切换。
- 服务端模拟仍使用**区域局部 float**（`WorldPosition` 的 float 不变），只是外层多一个 `RegionId` 和区域原点；这样移动校验、预测、回滚逻辑的改动最小。
- 服务端高度：调用共享 `HeightAboveSphere` 做地面约束/AOI/刷怪；与客户端同一 `terrainVersion`。

### 6.2 P1 消息

```text
PlanetHandshake {
  protocolVersion : u16
  planetRadiusCm  : i64      // 6,371,000,000
  regionLevel     : u8       // 13
  terrainSeed     : u64
  terrainVersion  : u32
}

PlayerState {
  tick        : i64
  regionId    : i64
  localX/Y/Z  : i32 (cm)     // 区域局部
  velocity    : i32 (cm/s) ×3
  yaw/pitch   : u16 ×2
  flags       : u16
}

RegionTransferRequest {
  fromRegionId : i64
  toRegionId   : i64
  globalPosCm  : i64 ×3
  tick         : i64
}

RegionTransferAck {
  regionId     : i64
  localX/Y/Z   : i32 (cm)
  originCm     : i64 ×3     // 可校验，可省略
  frame        : 压缩四元数
}
```

- 每帧不上报全局 64 位坐标；只报 `RegionId + 局部 i32`。
- 局部坐标范围 ±4.5 km = ±450,000 cm，i32 足够。
- `terrainSeed/terrainVersion` 不匹配直接拒绝进入世界，避免两边地形不一致。
- 后续若引入服务端地形瓦片，增加 `TerrainTileRequest/Response`，键为 `(face, level, x, y, version)`；P1 不实现。

### 6.3 区域切换

1. 客户端在距边界 1 km 时请求邻区地形/实体；
2. 服务端在边界内完成 grain 移交，发 `RegionTransferAck`；
3. 客户端切换 `RegionId/WorldOrigin/RegionFrame`，把旧局部坐标转新局部坐标；
4. 服务端在新区域继续同一套移动校验；输入 tick 连续；
5. 旧区实体在重叠窗口内继续可见，窗口结束移除。

跨区瞬间不做长距离回滚；若客户端预测与服务端不一致，按常规 correction 处理。

### 6.4 AOI 与持久化

- AOI 三层：区域（level 14，实测瓦片 3.67–7.78 km）→ 100 m cell → 实体。
- 玩家只订阅当前区域 + 邻区边界 cell；远处实体不模拟。
- 持久化存 `globalEcefCm`（i64×3）+ `regionId` + 局部坐标（便于快速恢复）；数据库字段用 bigint。
- 传送/快速旅行直接改 `globalEcefCm`，客户端走 `RegionTransfer`，不做长距离插值。

---

## 7. 缓存设计

### 7.1 缓存键与目录

```
%APPDATA%/HundunWorld/Cache/Planet/v1/{seed:x16}/{terrainVersion}/
  region_{regionId:x16}/
    near_{px}_{pz}.hgt      // 509×509 float32 局部 Y（厘米）+ header，见 §13.7
    mid_{level}_{x}_{y}.hgt // 65×65 int16 + min/max（尚未落地）
    meta.json               // 算法版本、采样范围、校验和（尚未落地）
```

- header：magic、formatVersion、seed、terrainVersion、regionId、patchX/patchZ、edge、floatCount、crc32（64 字节定长，保留区必须为 0）。
- 近景载荷用 float32 而不是 int16：`FillPatchHeights` 的产物就是 float32，再量化会让"缓存命中"变成一条精度分支（§13.7）。中景 65×65 用 int16 + min/max 仍然合适——它是远景轮廓，本来就不参与碰撞。
- 原子写：写 `.tmp` → flush → rename；读取时校验 header/crc，不信任旧文件。
- LRU 淘汰按“最后访问 + 文件大小”；默认上限 2 GB，近景 patch 优先保留。
- splatmap 默认不落盘（可由高度/生物群系即时生成）；若生成成本高再缓存压缩版本。

### 7.2 失效

- `terrainSeed`、`terrainVersion`、`formatVersion` 任一变化：整目录失效。
- 高度函数算法变更必须 bump `terrainVersion`；不要靠“看起来一样”复用旧缓存。
- 客户端启动时只做轻量校验，不扫描全缓存；按需读 header。

### 7.3 P1 权威口径

- **P1 不做地形瓦片服务**：客户端按 seed/version 生成 tile 并缓存；服务端用同一函数算高度。
- 共享库 `Horizon.Planet` 是唯一实现；禁止客户端/服务端各写一套噪声。
- 若后续要做地形编辑/权威碰撞，新增 `ITerrainTileSource` 远程实现，协议已带 `tileKey/version`，客户端缓存层不变。

---

## 8. 引擎 64 位构建（P3 可选）

### 8.1 何时启用

P1/P2 **不启用**，原因：

- 物理（PhysX）仍是 float；导航 Recast 也是 float；全局双精度不能替代区域局部化。
- 近景 Terrain/角色都在 9 km 内，float32 精度亚毫米级，large worlds 没有收益。
- 引擎重建会改变 `Vector3` 布局（float→double）、C# API 行为、插件 ABI 和性能，风险远大于收益。

只有在以下需求出现时才启用：

- 地面到轨道单场景连续飞行，且必须在同一场景保留绝对坐标；
- 需要把大量远景对象放在绝对坐标的同一个 Flax 场景里；
- 需要引擎级的 `LargeWorlds` 渲染原点吸附来简化远景渲染。

### 8.2 构建与验证

源码树：`C:\Works\FlaxEngine`（要求 Visual Studio 2026+ 与 Git LFS）。

```text
1. GenerateProjectFiles.bat -useLargeWorlds
2. 用 Flax.Build 全量构建 Editor / Game，所有构建调用都带 -useLargeWorlds
3. 额外手动重建 C# 绑定，确保绑定生成也带 -useLargeWorlds
4. 用 FlaxApiDump 检查 FlaxEngine.Vector3.X 是否为 System.Double、SizeInBytes 是否为 24
5. 全量回归现有测试/PIE；重建所有 native 插件；记录性能基线
```

已知陷阱：

- `GenerateProjectFiles.bat` 最后的绑定构建命令**没有透传 `%*`**，需要手动补 `-useLargeWorlds`，否则绑定和引擎精度不一致。
- 必须清掉旧 `Binaries/Cache` 做全量构建，避免增量假绿。
- `Vector3` 变 double 会影响所有 C# 脚本、序列化、GPU 上传边界、第三方插件；必须单独分支、单独 CI。
- 编辑器/游戏/插件必须使用同一套构建产物；不要把 large-worlds 的 DLL 和 float 的编辑器混用。

### 8.3 决策闸门

- 先做 P2 的轨道/远景验证；如果独立缩放场景已经满足需求，**不启用** large worlds。
- 如果启用，必须通过：`Vector3` 类型验证、现有回归测试、PIE 回归、帧时间/内存对比、插件加载验证。

---

## 9. 分阶段与验收

| 阶段 | 交付物 | 验收判据 |
|---|---|---|
| **P0 坐标/流送验证** | `Horizon.Planet` 数学库 + 单元测试；5×5 Terrain 验证场景；patch 增删/碰撞耗时报告 | 局部↔全局往返误差 ≤ 1 cm；区域边界/极点无奇异；5×5 patch 增删不超帧预算；`SetupPatchHeightMap` 失败分支有断言 |
| **P1 区域与协议** | RegionId/RegionFrame、RegionTransfer 协议、服务端 grain、AOI、磁盘缓存、近景流送 | 双客户端跨区不丢实体；位置误差 ≤ 1 cm；缓存命中/失效可复现；terrainVersion 不匹配拒绝进入 |
| **P2 中远景** | cube-sphere 四叉树、globe、轨道/地图场景、接缝/水面/大气 | 地平线/曲率截图；无 LOD 裂缝；内存/帧时间达标；远景与近景高度一致 |
| **P3 可选球面玩法** | （C2）球面重力/控制器/载具；或 large worlds 引擎构建 | 环球片段可走通；角色不翻转；导航跨区可用；large worlds 全回归 |
| **P4 内容与工具** | POI 编辑器、全球地图、快速旅行、性能压测 | 100+ 玩家区域压测；缓存/内存稳定；内容生产流程可重复 |

P0 必须先写判据再写实现：

- 坐标往返：随机 10⁶ 点，`GlobalToLocal(LocalToGlobal(p))` 误差 ≤ 1 cm。
- 区域边界：相邻区域边缘高度差 ≤ 1 cm；切平面夹角符合 `regionSize/R`。
- 高度确定性：双端对同一批 tile 采样做哈希，必须逐位一致。
- 流送：单 patch 生成/上传/碰撞耗时分布；5×5 窗口切换无 NaN、无掉穿。
- 网络：跨区前后实体集合、输入 tick、纠正次数对账。

---

## 10. 风险与未决

| 风险 | 影响 | 处理 |
|---|---|---|
| 10× 地球内容密度 | 99% 区域是程序化荒野，玩家体验空 | P1 只开放测试大陆；POI/任务按区域投放；快速旅行 |
| Flax Terrain 运行时只能虚拟存储 | 近景 patch 内存 4 MB/块，5×5 约 100 MB | 只保留 5×5；中景用 mesh；LRU 淘汰；监测内存峰值 |
| terrain patch 碰撞烹饪同步 | 流送时掉帧 | 预生成、限帧、优先补碰撞环；必要时缩小碰撞环 |
| 中景 mesh draw call | 每 tile 一个 draw call | P2 自定义合批/渲染任务；先测可见 tile 数 |
| 深度精度/远平面 | 30–50 km 视距出现 z-fighting | 反向深度 + 近平面调整；必要时拆双相机 |
| 球面重力（C2） | 角色/相机/动画/导航全部重做 | P1 不做；P3 单独立项 |
| large worlds 引擎重建 | ABI/插件/性能/维护成本 | 只作为 P3 开关；默认不启用；单独分支/CI |
| 双端高度函数不一致 | 客户端视觉与服务端碰撞/刷怪错位 | 共享库 + 版本 + 哈希对账；禁止两套实现 |
| 区域切换抖动 | 跨区瞬间位置/实体跳变 | 预加载 + 重叠窗口 + 不插值旋转；跨区不触发长回滚 |

### 待拍板

1. **C1 vs C2**：P1 默认 C1（局部 Y-up、无球面重力）；是否需要在 P3 做真球面步行/载具？
2. **区域尺寸**：已定 level 14（实测瓦片 3.67–7.78 km，最远角 5.5 km，下垂 0.237 m，近景 2.54 km 窗口装得下）。cube-sphere 的瓦片尺度在面中心与面边缘差 2 倍是**固有属性**，如果后面不能接受这种不均，就得把"权威区域"与"渲染帧"拆成两套（区域走等面积网格，帧走 cube-sphere），这是新增的待拍板 9。
3. **视距目标**：30 km / 50 km / 100 km？决定中景 LOD 与深度方案。
4. **是否需要飞行/太空**：决定是否做轨道场景、过渡和 large worlds。
5. **地形是否可编辑**：纯程序化 vs 局部手工编辑；后者会把 P1 的“共享采样器”升级为“服务端瓦片权威”。
6–8. 真实地球基场的三条（交叉频率 / `baseGain` / 海洋是否可玩）见 §12.8。

---

## 11. P0 立刻可做的任务

1. 新建 `Horizon.Planet`（无 Flax 依赖）：RegionId、cube-sphere、ECEF/局部转换、切平面基、整数噪声、高度函数、单元测试。
2. 在测试场景做 5×5 Flax Terrain 流送 spike；测量 `AddPatch`、`SetupPatchHeightMap`、碰撞烹饪、`RemovePatch` 的耗时与内存。
3. 客户端加 `RegionFrame/WorldOrigin` 与 rebase；在 4.5 km 范围内做抖动/接缝截图对比。
4. 服务端加 `RegionId + 局部坐标` 协议骨架；先用单区域验证，不做跨区。
5. 缓存层先做近景 `.hgt` 原子读写 + 版本失效；不做 splatmap 缓存。
6. 记录基线：patch 增删耗时、内存、远平面/深度表现、双端高度哈希。
7. 基场接入（可与 1–6 并行）：跑 §12.9 三条命令拿到 COG + `.raw` + `manifest.json`，`Tools/gebco_verify.py` 的 §12.7 判据全绿，并把四条 mutant 各翻红一次；然后在 `Horizon.Planet` 里实现 `IBaseFieldSource` + `RawMappedSource` 与 §12.4 的经纬度↔像元公式，客户端/服务端各跑同一批点做逐位哈希对账。

P0 通过后再进入 P1；P0 的任何一项判据不通过，都先改方案，不进入协议/服务端改造。

---

## 12. 真实地球基场（GEBCO_2026 15″）

把 §4.1 第 1 层的「低频噪声大陆掩码」换成真实地球高程，让星球的大陆轮廓、海岸线、山带、海盆是**真的地球**。本节口径全部按 2026-11-01 从源站实测得到的事实写，不引用二手博客参数。

### 12.1 源数据实测规格

| 项 | 值 | 取证方式 |
|---|---|---|
| 产品 | GEBCO_2026 Grid，`ice_surface_elevation` | gebco.net 下载页；CEDA 直链 |
| 格网间隔 | 15 弧秒 = 1/240° = 0.0041666667° | GeoTIFF `ModelPixelScale` |
| 全球尺寸 | 43200 行 × 86400 列 = 3,732,480,000 像元 | 单幅 21600×21600 × 8 幅 |
| 数据型 | Int16 **signed**（`SampleFormat=2`, `BitsPerSample=16`） | IFD 解析 |
| 单位 | 米，相对平均海平面 | 产品文档 |
| **NoData** | **−32767**（不是网上流传的 −32768） | `GDAL_NODATA` |
| **配准约定** | **像素角点**：`ModelTiepoint = (0,0,0 → lon_w, lat_n)`，即 col0 ↔ 该幅西边界、row0 ↔ 北边界 | `ModelTiepoint` |
| 分幅 | 2 个纬度带（0–90N / 90S–0）× 4 个经度象限，**无重叠、无缝**（90°/15″ = 21600 恰好整除，角点约定下不需要 +1） | 文件名 + 尺寸 |
| 内部布局 | strip-per-row、**未压缩**（`Compression=1`）→ 必须重烤成 COG | IFD `RowsPerStrip=1` |
| 坐标系 | EPSG:4326 (WGS 84) | GeoKeyDirectory |
| 下载体积 | `gebco_2026_geotiff.zip` = 4,241,629,269 B；netCDF 版 4,252,016,343 B | HTTP `Content-Length` |
| 原始格网体积 | 7,464,960,000 B（6.95 GiB） | 3,732,480,000 × 2 |
| 许可 | UK Open Government Licence v3，可商用，须署名 | CEDA 页脚 |

同源还有 `sub_ice_topography_bathymetry`（冰下地形）与 `type_identifier_grid`（TID，标水源类型）。P1 只用 `ice_surface_elevation`；TID 留给后续「这块海底是实测还是重力预测」的上屏诚实化。

### 12.2 尺度失配：真实数据到底管到哪一层

这是本节最重要的一条，决定了混合公式的边界。星球 R = 63,710 km，赤道周长 400,301 km，故 **1° = 1,111.9 km，15″ 像元 = 4.63 km**（地球上是 463 m，被放大 10 倍）。

| 层（§4.2） | 目标采样 | 一个窗口跨几个源格 | 基场能提供什么 |
|---|---|---:|---|
| 近景 5×5 patch（2.54 km，1 m） | 1 m | **不足 1** | 只有一个常数抬升 + 区域级倾斜；脚下 1 m 起伏 100% 来自程序化 |
| 区域（level 14，实测 3.67–7.78 km） | — | 1–2 | 真实高程基准与区域级倾斜 |
| 中景 tile（2.5–50 km，4–16 m） | 4–16 m | 1–11 | 5–50 km 的**真实趋势**（山带走向、海岸形态）；5 km 以下没有 |
| 远景 globe（50 km–全球，32–500 m） | 32–500 m | 数十–数千 | 主要内容由基场给出 |
| 轨道/世界地图 | 屏幕级 | 最多 86400 | 完全是基场 |

**结论（必须接受的事实）**：真实数据的有效信息下界是 **2 个像元 = 9.27 km 波长**（Nyquist）。所以基场管「地球长什么样」，程序化管「脚下 9 km 以内长什么样」。任何「用真实数据做出 1 m 地形」的预期都不成立——10 倍半径这个已拍板的决定，本身就否掉了细节尺度还原。想还原细节只有两条路：星球按 1:1 地球半径，或海洋/陆地换 3″（93 m）级源（§12.8 待拍板 6）。

### 12.3 离线产物与 tile 切分

源数据与派生产物一律落 `Data/`（已加 `.gitignore`，**不入库**）；重导命令见 §12.9。

```
Data/GEBCO_2026/
  raw/gebco_2026_geotiff.zip        4.24 GB 官方原包，只做一次
  parts/p*****.part                 8 MB 分段续传缓存（完成后清）
  tiles/gebco_2026_n*_s*_w*_e*_geotiff.tif   解出的 8 幅官方分幅
  derived/gebco_2026_15s_cog.tif    工程用单一全球 COG（主产物）
  derived/gebco_2026_15s_i16le.raw  扁平行优先 Int16 LE，运行时零依赖读
  derived/manifest.json             规格 + sha256 + 统计 + 判据结果
  fetch_receipt.json                下载回执（bytes/sha256/耗时）
```

**`gebco_2026_15s_cog.tif`（工程用 GeoTIFF）规格**：

- 单一全球文件，43200×86400，Int16 signed，EPSG:4326；
- 地理变换**强制**写成角点约定：`Affine(1/240, 0, −180, 0, −1/240, 90)`，丢弃 GCP；这条要 §12.7 的等式断言坐实，不能靠 `gdal_translate` 默认行为；
- 内部 tiled 512×512、`COMPRESS=DEFLATE`、`PREDICTOR=3`（整型水平预测，对高程场压缩比明显好于无预测）、`BIGTIFF=YES`、`SPARSE_OK=YES`；
- overview：`gdaladdo -r average 2 4 8 16 32 64 128 256`，只给远景/轨道视图用；**采样器永远读 level 0**，禁止误用 overview 导致精度静默下降；
- 镶嵌用 `rasterio.merge` 纯拼装，**不做 warp、不重采样**：源和目标同为 4326 且格网严格对齐，warp 只会引入插值噪声并破坏 Int16 精确性；
- 断言合并后**恰好** 43200×86400、8 幅各 21600×21600、各幅边界经/纬度与文件名一致（`n/s/w/e` 从文件名解析后逐条比 `src.bounds`）——这一步是防「分幅命名同构但边界不同」的静默丢行。

**运行时取数：不做 tile 服务，用扁平 raw**

基场单格 4.63 km ≫ 近景 patch 508 m，所以运行时随机访问的成本就是「读 2 行 × 2 列 = 4 个 int16」。因此 P1 **不切运行时瓦片**，直接 `MemoryMappedFile` 打开 `.raw`：

```
offset = (row * 86400 + col) * 2      // row=0 是 90°N，col=0 是 180°W，小端
```

好处：无 tile 簿记、无接缝、无失效分支、双端（客户端 C# / 服务端 Orleans）用同一行公式。代价：单文件 6.95 GiB，开发期本地放盘即可。

**可选的 `5°×5°` 切分（只在将来上瓦片服务时启用）**：1200×1200 像元 = 2,880,000 B/块，72×36 = 2,592 块，总量不变。键 `bf_{lonIdx:02d}_{latIdx:02d}.i16`，头结构复用 §7.1（magic/formatVersion/seed/basefieldVersion/crc32）。`IBaseFieldSource` 接口从 P0 就抽出来，`RawMappedSource` 是第一实现，`TileHttpSource` 以后换实现不动调用方。

### 12.4 混合高度函数

**经纬度 ↔ 像元**（唯一口径，双端共用，写进共享库 `Horizon.Planet`）：

```
e   = normalize(ecefCm)                           // 单位向量；标准 WGS84 ECEF
lat = asin(e.Z)                                   // +Z 是北极
lon = atan2(e.Y, e.X)                             // +X 指 0° 经线，向东经 +Y 为正

fx  = (lon + 180) * 240 - 0.5                     // 角点→格中心：减 0.5
fy  = (90  - lat) * 240 - 0.5
```

- **极轴口径（2026-11-02 与代码统一）**：全局 ECEF 用 `+Z = 北极` 的右手标准地理系（`Horizon.Planet.Geodetic` 实测实现 + 单测覆盖），只有落到区域切平面之后才转成 Flax 的 Y-up 左手局部系（X=东、Y=上、Z=南，见 §3.2）。早先稿写的"+Y 是北极"作废——两套轴并存会直接让双端高度哈希对不上。

- **减 0.5 是角点约定的落点**，§12.1 实测确认源是角点配准，漏掉这 0.5 会让全场整体偏移半格（2.3 km），且统计量看起来完全正常——必须靠 §12.7 的变换等式断言钉住，不能靠点对；
- 边界：`fx ∈ [−0.5, 86399.5]`，`col = floor(fx)` 越界时**经度方向回绕**（±180° 是同一条经线），`fy` 越界时**钳到 0/43199**；极点 `lat=±90` 经度无定义，钳位即可；
- 双端逐位一致：`lat/lon` 用 `double`，`asin/atan2` 在 .NET 与 Flax(mono/coreclr, .NET 10) 两侧必须是同一实现——沿用 §4.1「不要用平台浮点函数当主噪声」的纪律，**把三角函数只用在经纬度反算这一处**，插值本身只用 `+ - *`，保证双端哈希一致；
- 采样：基线用**双线性**（4 点），近景 patch 烘焙用**双三次 Catmull-Rom**（16 点，避免双线性在海岸线产生的菱形伪影）；两者都由 §12.7 的一致性用例固定权重公式，禁止一处 Catmull-Rom 一处双线性。

**叠加式**（替换 §4.1 第 1 层，2–4 层保留）：

```
H(worldPoint) = baseGain · B_real(lat, lon)                  // 真实基场，米
              + Σ_{k=0}^{K-1} A_k · detail_k(p / λ_k)         // 程序化，λ_0 = λ_c 起
              − curvatureSag(d)                               // §3.4，不变
```

- `λ_c = 2 × 4.63 km = 9.27 km` 是**唯一交叉频率**：程序化各阶波长必须 ≤ λ_c，`λ_{k} = λ_c / 2^k`，`K = ceil(log2(λ_c / 1 m)) = 14`；
- 幅度沿用谱斜率 `A_k ∝ λ_k^((H+1))`（H≈0.75），**且在 λ > λ_c 一侧必须为 0**：低频重复计数会让全球起伏整体放大，红在截图上表现为「哪儿都是山」；
- `baseGain` 默认 1.0（真实米）。放大它=山更陡但海岸仍是地球海岸；`baseGain` 与 `λ_c` 都是 `terrainVersion` 的一部分；
- 近景 patch 存的仍是 `H − curvatureSag`，§4.3 那条「高度只写不读，自己留数组」不变——基场只是换了 `H` 的第一项来源。

**海洋与海岸**：`B_real < 0` 即海，§4.5 的球壳水面直接落在 `h = 0`，海岸线自动是真的。浅水搁浅/行船判定用 `B_real` 自身；潮间带（|h| < 15 m）用程序化细节覆盖，避免 4.63 km 格产生的锯齿海岸。

**基场为 nodata（−32767）时**：不许当 0，也不许静默插值——按 §12.7 统计出的实际占比决定：若为 0（GEBCO_2026 声称全球无缝）则该分支只留断言；若非 0，退化为「同 tile 邻域实测均值 + 上屏告警」，并 bump `basefieldVersion`。

### 12.5 版本与协议

`PlanetHandshake`（§6.2）追加两字段，其余不变：

```
basefieldVersion : u32        // 源产品标识，GEBCO_2026 = 2026
basefieldHashHi  : u64        // manifest.json 里 raw 文件 sha256 前 8 字节
```

不匹配直接拒绝进入世界，理由与 `terrainVersion` 同：两侧基场不同 ⇒ 地面高度不同 ⇒ 服务端权威校验与客户端碰撞必然打架。

### 12.6 为什么不能直接用 netCDF / 不能只留 GeoTIFF

- netCDF 是**像素中心**对齐（GDAL 会挂 GCP 而不是 tiepoint+scale），转进来要自己加半格；官方 GeoTIFF 实测是角点 tiepoint，少一层换算风险；
- 运行时（Flax C# 与 Orleans）**没有 GDAL**：COG 只当「唯一真源 + 人工/QGIS 可视化 + 后续重烤的输入」；运行时只认扁平 `.raw` + `manifest.json`，两者都由转换脚本一次生成、同源同哈希。

### 12.7 验收判据（转换脚本必须自带，红在判据而不是退出码）

结构门：
- 合并后尺寸**恰好** 43200×86400；dtype Int16；CRS 是 EPSG:4326；
- COG `transform` 与独立算出来的 `Affine(1/240, 0, −180, 0, −1/240, 90)` **逐系数相等**（不是近似）；
- 8 幅各自 bounds 与文件名 `n/s/w/e` 一致；`merge` 后每幅覆盖区不重复；
- `.raw` 字节数 == 7,464,960,000；`sha256` 写进 `manifest.json`。

独立参照（用 `struct.unpack_from` 直读 `.raw`，与 rasterio 读取路径完全分开，两边同数才算过）：

| 点 | 经纬度 | 期望 |
|---|---|---|
| 珠峰 | 27.9881 N, 86.9250 E | 5×5 窗口内最大值 ≥ 8,300 m |
| 卓奥友…镜像反证 | **−**27.9881, 86.9250（南印度洋） | ≤ −3,000 m，且与珠峰点差 > 5,000 m |
| 挑战者深渊 | 11.3467 N, 142.1986 E | ≤ −9,500 m（打印实测值） |
| 乞力马扎罗 | −3.0674, 37.3556 | ≥ 5,500 m |
| 死海 | 31.50 N, 35.40 E | −600 … −300 m |
| Null Island | 0, 0 | −5,400 … −4,400 m |
| 好望角 | −34.3568, 18.4722 | 陆地 > 0 m |

总体统计门（这些是独立可查的地球常数，不是从数据里推的）：
- `h > 0` 占比 28.5 %–29.5 %（地球陆地 29.2 %）；
- 海面以下像元均值 −3,900 … −3,500 m（平均海洋深度 3,688 m）；
- `min ≥ −12,000`、`max ≤ 9,000`；nodata 占比与来源声明对账并打印。

反证协议（改坏→跑→复原，一条命令内自复原）：
1. 把 `fy` 公式的 `- 0.5` 去掉 ⇒ 变换等式断言必须红；
2. 行列互换 ⇒ 镜像反证点必须红；
3. 把 nodata 当 0 参与统计 ⇒ 均值/陆占比必须红；
4. 只喂 7 幅（丢一幅）⇒ 尺寸断言必须红。
任一 mutant 跑出绿，说明那条判据是空转，先修判据再谈数据。

### 12.8 待拍板（追加到 §10）

6. **基场到细节的交叉频率**：`λ_c = 9.27 km`（15″ 的 Nyquist）。若要更实的近景，换 3″ 源（GDEM_2022，`λ_c = 1.85 km`）——但它 187 GB，且它的细节是深度学生成的、与区域实测的平均绝对误差 19.58 m，只有 63.99 % 的误差 < 10 m；
7. **要不要为「看起来像真地球」加 `baseGain`**：10× 星球上横向被拉大 10 倍，山会变宽变钝；`baseGain = 2~3` 能补观感但会破坏「真实高程」的声称，上屏文案要跟着改；
8. **海洋值不值得留**：洋底只有约 27 % 是现代测深标准，其余是重力反演；做水下玩法就是做假地形，需要产品先定「海是水面还是可行空间」。

### 12.9 许可、引用与重导

署名口径（进游戏「关于」页与素材清单）：

> GEBCO_2026 Grid, GEBCO Bathymetric Compilation Group (2026), 15 arc-second grid, NERC EDS BODC, doi:10.5285/… , UK Open Government Licence v3.

重导命令（本地，`Data/` 不入库）：

```
python Tools/gebco_fetch.py                      # 分段并发续传下载（源站单连接限速 ~13 KB/s，24 路 ≈ 310 KB/s）
python Tools/gebco_convert.py                    # 解压→镶嵌→COG→raw→manifest
python Tools/gebco_verify.py                     # §12.7 全部判据，非 0 退出即失败
```

---

## 13. P0 落地进展（2026-11-02）

### 13.1 已经在盘上的东西

| 产物 | 内容 | 状态 |
|---|---|---|
| `Data/GEBCO_2026/raw/gebco_2026_geotiff.zip` | 4,241,629,269 B，sha256 `39bb6eb4…9368` | 独立复核：SHA256 与回执一致、10 个条目 CRC 全过 |
| 源规格 | 8 幅 21600×21600、Int16 signed、nodata −32767、EPSG:4326、strip-per-row、未压缩、合计 3,732,480,000 像元 | 与官方 43200×86400 声明逐位吻合 |
| `Tools/gebco_build_dem_pyramid.py` | 流式 2:1 金字塔 + manifest + 四道自检模式 `--selftest / --check / --xcheck / --verify-only` | 建成 L0–L5（9.95 GB），闸门绿 |
| `Horizon.Planet` | 无 Flax 依赖：RegionId 打包、cube-sphere、ECEF int64 厘米 ↔ 局部 float、切平面基、曲率、`DemGrid` 采样器、`PlanetPatchSampler`、`Crc32` | `dotnet test` **67/67 绿**（含 CRC 标准 check value `0xCBF43926`） |
| `Tools/DemProbe` | C# 侧 `crc` / `anchors` / `patch` 三种复核模式 | 6 个层级 CRC 由 C# 独立重算，与 manifest **全部一致** |

复核读（按文件顺序整读 9.95 GB 重算）出的总体统计，**面积口径**（`cos 纬度`加权）：

| 判据 | 实测 | 公开事实 |
|---|---:|---:|
| 面积均高 | −2391.9 m | ≈ −2.4 km |
| 陆地面积占比 | 0.2899 | 29.2 % |
| 海洋均高 | −3695.9 m | 平均海深 3,688 m |
| 陆地均高 | +797.2 m | ≈ 840 m |
| 北/南半球陆地密度 | 0.3900 / 0.1898 | 北半球约是南半球两倍 |
| 北纬≥70 / 南纬≤−70 均高 | −825.5 / +1139.4 m | 北冰洋是海、南极是冰盖 |
| 极值 | [−10931, +8627] m | 挑战者深渊 / 喜马拉雅 |
| nodata | 0 格 | GEBCO 声称全球无缝 |

计数口径（不加权）同批数字是 海洋 −3448.1 m、陆地 +1109.5 m、陆地占比 0.3394 —— 与面积口径差 250 m / 5 个百分点，这就是第一轮闸门误判的来源，也是"统计必须写明口径"的原因。

**跨语言对账**：`python --xcheck`（Python 侧独立实现的双线性）与 `dotnet run --project Tools/DemProbe -- anchors`（C# `DemGrid`）在 200 个坐标、最细/最粗两级上**逐字节相同**；C# 侧另按文件顺序重算 6 个层级的 CRC32，与 manifest 一致。也就是说离线产物、Python 参照、运行期 C# 读取器三者对同一份约定达成一致。

### 13.2 本轮抓到的四个错（都已钉进判据）

1. **口径错**：拿面积加权的公开事实去卡按格计数的统计，判出"陆地占比 34% 不合格"。等距网格极区格面积小，按格计数必然偏高；现在统计一律带 `cos(lat)` 权。
2. **crc 语义错**：写盘时按"逐幅逐行"顺序累计 crc，`--check` 按"文件行序"重算，两者不是同一个量 ⇒ 复核永远红。现在 manifest 里 `crc32`（文件序，运行期与 C# 用的）与 `crc32_write_order`（写入序，溯源）分列，并且多一趟整文件复核读。
3. **下采样漏了横向 2:1**：第一版只做纵向两行配对，各级列宽根本不减半；顺带还暴露"每级都量化回 int16 会把舍入误差传到粗级"。`--selftest` 用**直接从原始网格算每级**的独立参照（不是逐级再均值）钉住这两条。
4. **`STEP_DEG << level` 是浮点位移**：Python 直接 TypeError，且这条路径在加面积统计之前从没被执行过。

### 13.3 与 §12 规格的分歧（下一步要统一）

| 项 | §12 口径 | 本轮实现 | 状态 |
|---|---|---|---|
| 产物命名/布局 | `derived/gebco_2026_15s_cog.tif` + `derived/…_i16le.raw` + `derived/manifest.json`，分幅解到 `tiles/` | 已改成同一套：`derived/gebco_2026_15s_i16le.raw`(L0) + `derived/pyramid/level_01..05.bin` + `derived/manifest.json`，临时分幅走 `tiles/` | **已统一**；COG 仍未产出（见下） |
| 极轴 | 原写 `+Y` 是北极 | `+Z` 是北极（标准 WGS84 ECEF），`lon = atan2(y, x)` | **已统一**：§12.4 公式已随代码改 |
| 运行时层级 | 永远读 level 0，overview 只给远景，禁止误用 | L0–L5 显式文件，近景 L0、中远景按 LOD 选级 | **已闭环**：`HybridHeightSource` 构造时拒绝非最细级（`Hybrid_RejectsCoarseBaseField_ButAcceptsFinest`） |
| 插值 | 近景双三次 Catmull-Rom（16 点）、其余双线性，两者由一致性用例固定 | 两条路径都在，另有 `HeightSampler` 走窗口缓存 | **已闭环**：`Bicubic_MatchesBilinear_OnLinearField`、权重和/复现判据、缓存与逐点路径**逐位相等** |
| COG | 要（QGIS/人工核图用） | 未产出 | 待补一次 rasterio 转换；不进运行时 |
| 变换等式断言 | §12.7 要求"`- 0.5` 漏掉必须红" | `--mutants`：三条基线必须绿、四个变异体必须红 | **已闭环**（结果见 §13.5） |
| 握手字段 | `basefieldVersion`、`basefieldHashHi` | `Horizon.Planet/Wire/PlanetWire.cs`：`PlanetHandshake` 35 B 已含全字段并有独立偏移表判据 | 编解码**已落地**；接进 `Horizon.Game.Message` 的 MessageType/MessageUnion 留作后续薄步骤（该目录当前有并行会话未提交改动） |
| 混合高度函数 | `baseGain·B_real + Σ A_k·detail_k(λ_k ≤ λ_c=9.27 km)`，K=14 | `TerrainProfile`+`DeterministicValueNoise`+`HybridHeightSource` 已实现并测 | **已落地**；指数口径有一处偏离待拍板（§13.5 之 9） |

### 13.4 P0 剩余（按优先级）

1. ~~**5×5 Flax Terrain 流送 spike**（任务 3）~~ **已跑**（§13.15）：布局 `index = z*509 + x` 与 50800 cm 契约在引擎里证实；`SetupPatchHeightMap` 返回 true=失败证实；耗时/碰撞三角形数/`CollisionLOD` 档位都量了。**剩下两件**：① 上传能否离开主线程**仍未证**（发行版没带 `Terrain.cpp`，做线程实验有触发引擎断言模态框的风险）；② 每帧加载数与 `CollisionLOD` 的取舍要拍板（§13.15 E 段：现行 4 块/帧在任何 LOD 下都超一帧，只有"1 块/帧 + LOD≥3"进得了 16.7 ms）。
   ~~阻塞点：本机 `FlaxEditor.exe` 已在跑，属并行会话所有~~ 2026-11-03 07:37 该进程已退出，本轮起的单实例编辑器已按 WM_CLOSE 关掉，`World.scene` 的 mtime 全程未变。
2. ~~**细节层与近景插值**（任务 6）~~ 已完成，见 §13.5：`TerrainProfile` + `DeterministicValueNoise` + `HybridHeightSource` + `HeightSampler`、Catmull-Rom 与一致性用例、"基场必须最细级"的构造期拒绝、`--mutants` 反证协议。
3. **区域协议骨架**（P1 头一口）：编解码与判据已在 `Horizon.Planet/Wire` 落地（§13.6）；剩下的接法是把它注册进 `Horizon.Game.Message`（`MessageType` + `MessageUnion`）并在 `SyncHandshakeHandler` 里比对 `DescribeMismatch`——等该目录的并行改动落定再做。
4. **COG 产物**：只为 QGIS/人工核图，不进运行时。
5. **客户端装配**：`Horizon.Planet.dll` 进 `Game.Build.cs` 的 `dlls[]`（等第一个真正引用它的客户端文件再加，避免空引用清单）；同时核对引擎 Tools 刷新清单，防"门禁绿而编辑器吃旧 DLL"。
6. **盘**：C: 剩 86 GB（94% 已用）。`Data/GEBCO_2026/parts/` 的 506 个分片（4.24 GB）在 zip 已 SHA256+CRC 双验后只剩续传价值，可清；`raw/`(4.24 GB) + `derived/`(9.95 GB) 保留。**未删**——那是等你点头的动作，不是建议项。

### 13.5 混合高度场与反证（同日第二轮）

新增组件（都在 [Horizon.Planet](../Horizon.Planet)）：`TerrainProfile`（λ_c、带数、谱斜率、baseGain、潮间带阈值、`terrainVersion`/噪声种子）、`DeterministicValueNoise`（splitmix64 格点 + 五次平滑，纯整数哈希，无平台超越函数）、`HybridHeightSource`（基场×gain + 细节叠加）、`HeightSampler`（5×5 模板缓存）、`Crc32`。

**实测（真实 GEBCO 产物，`Tools/DemProbe patch`）**：

| 项 | 值 |
|---|---|
| λ_c / 基场单格 / 带数 | 9.27 km / 4.63 km / 14 带 |
| 一个 patch（509²=259,081 顶点）串行、逐点采样 | 1,802.8 ms |
| 同上 + 5×5 模板缓存 | 1,335.4 ms |
| 同上 + 按行并行（结果逐位不变） | **141.8 ms** |
| 5×5 窗口总 CPU | ≈ 3.5 s（后台线程） |
| 30N/100E（藏东南）末 patch 局部 Y | 496,302.6 ~ 500,673.4 cm，patch 内起伏 43 m |
| 0N/150W（开阔洋）末 patch | 起伏 **51 cm**、centre−corner −11.8 cm |

洋面那片 51 cm 就是 §12.2 的直接印证：**基场在一个 patch 里几乎是常数**，脚下起伏 100% 来自细节层；反过来若哪天洋底出现"米级小起伏"，说明细节层漏过了水下闸门。

**反证协议 `--mutants` 全绿**（基线不该红的都绿，四个变异体都红）：

| 判据 | 结果 |
|---|---|
| 基线：配准等式 / 锚点 / 象限覆盖 | 三条绿 |
| 变异 1 去掉格心 `-0.5` | 红：`格 (32034,58664) 格心插值 -498425 != 原始值 -497800 cm` |
| 变异 2 行列互换 | 红：珠峰读到 −4113 m |
| 变异 3 nodata 当 0 | 红（合成夹具）：诚实 −1779.5 m / 陆地 6.30% vs 掺假 −2021.6 m / 6.25% |
| 变异 4 少喂一幅 | 红：第 5 幅象限三点全 0 |

变异 3 特意跑在**合成带洞夹具**上：真实 GEBCO 一个 nodata 都没有，这条判据在真数据上是空转的，只有合成夹具能证明"门本身有效"。

**这一轮自己咬到的三个错**（都留成判据，不靠"我看了一眼"）：

1. Catmull-Rom 我说成"精确复现三次"——错，中心差分斜率的误差项含 f‴，它复现的是**二次**；判据改成 Σw·x² = t²。
2. 缓存路径多除了一次权重（和 ≈ 1−1e-16），与逐点路径差在末位 ULP；改成与 `DemGrid` 同结合序、不归一，判据升级为**逐位相等**。
3. 配准等式最早写在"格心整点"上，二进制边界会让 floor 掉一格（(32035,58664) 反算成 32034）——那是浮点边界不是约定错；索引等式改用离边界 1/4 步的内点，值等式交给连续插值。

**测试**：`Horizon.Planet.Tests` **90/90 绿**（含并行 vs 串行逐位一致、缓存 vs 逐点逐位一致、`terrainVersion` 八个旋钮各自都会变、双线性与双三次两条分支各自的计数器断言）。

**待拍板 9（新增）**：`λ_min` 现在取 1 m（= 顶点间距），于是 K=14。但 1 m 采样本来就表不出 2 m 以下的波长，`λ_min = 2 m`（顶点 Nyquist）才是"最后一带有意义"的口径，能把噪声成本降约 1/14 且不丢任何可表达的细节。要不要按 2 m 定，等 PIE 里看过实际观感再定；定下来就是 `TerrainProfile(minWavelengthCm:)` 一个参数，`terrainVersion` 会自动跟着变。


### 13.6 P1 线协议编解码（同日第三轮）

落在 [Horizon.Planet/Wire/PlanetWire.cs](../Horizon.Planet/Wire/PlanetWire.cs)，`Span<byte>` + `BinaryPrimitives`（无分配、显式小端），刻意**不碰** `Horizon.Game.Message`（该目录有并行会话未提交改动）：

| 消息 | 字节 | 字段 |
|---|---:|---|
| `PlanetHandshake` | 35 | wireProtocol, planetRadiusCm, regionLevel, terrainSeed, terrainVersion, basefieldVersion, basefieldHashHi |
| `PlayerStatePacket` | 46 | tick, regionId, 3×i32 局部厘米, 3×i32 速度 cm/s, yaw/pitch u16, flags |
| `RegionTransferRequest` | 48 | from/to regionId, 3×i64 全局 ECEF cm, tick |
| `RegionTransferAck` | 52 | regionId, 3×i32 局部, 3×i64 原点, 4×i16 姿态 |

判据（`PlanetWireTests`，共 104 条测试全绿）：

- **独立偏移表**复核每一条布局——读写同时错同一格时 round-trip 仍会绿，只有把偏移写进测试才抓得住；
- **每个截断长度**（0..Size−1 逐一试）都必须 `Truncated` + `consumed=0`；
- 拒绝分支（截断 / 尾随字节 / 协议号不支持 / 局部坐标越界）都要回同一套对账字段（status + consumed + 已解出的原值），并且每条都配同夹具的正向对照；
- 量化误差写成上界：局部厘米 ≤ 0.5 cm、角度 ≤ 1/65536 圈、四元数 ≤ 0.5/32767；
- `terrainVersion` 必须真的吃到基场身份：换 `basefieldHashHi` 就必须变（`Handshake_CarriesTerrainIdentityFromTheRealProfile`）。

这一轮同样抓到四处自己的错，都已改成判据：

1. `TrailingBytes` 我设了状态却仍返回 `true` —— 语义上"帧长不符"就是拒绝，实现与文档不一致被测试逮到；
2. 夹具数组写成 `short?[8,4]` 却按 4×8 填 —— 越界；
3. 四元数量化用了默认的银行家舍入，与局部坐标的 `.5 远离零` 不一致，测试按截断算期望值差 1 —— 统一成 `AwayFromZero` 并把规则写进注释；
4. 角度的"误差上界"循环包含了 turn=1.0，而那一圈按定义回绕成 0 —— 循环上界改为不含 1.0，回绕另有专测。

**下一步（等外部条件）**：注册进 `MessageType`/`MessageUnion`、在 `SyncHandshakeHandler` 里比 `DescribeMismatch` 并按结果拒绝入世界；以及 PIE 里量 `AddPatch`/`SetupPatchHeightMap`/碰撞烹饪（编辑器仍被并行会话占用）。

### 13.7 近景流送调度与磁盘 patch 缓存（同日第四轮）

落在 [Horizon.Planet/PlanetPatchCache.cs](../Horizon.Planet/PlanetPatchCache.cs) 与 [Horizon.Planet/PlanetTerrainStreamer.cs](../Horizon.Planet/PlanetTerrainStreamer.cs)，加两个常量 `PatchWindowHysteresisCm = patch/4 = 127 m`、`PatchLoadsPerPlan = 4`。测试 **141/141 绿**（本轮 +37：缓存 21 条、调度器 16 条）。

**缓存载荷用 float32 而不是 §7.1 原文写的 int16**。理由不是省事：`FillPatchHeights` 的产物本来就是 float32，再量化一次会让"命中缓存"变成一条精度分支——同一个 patch 在冷跑和热跑里给出两套高度，接缝处就会出现只有重启才可见的台阶。判据写成**逐字节相等**（`AssertBitsEqual` 比的是 `MemoryMarshal.AsBytes`，不是 `Assert.Equal(float)`，后者会把 NaN≠NaN、−0=+0 都算过）。§7.1 的表因此改成：

| 文件 | 字节 | 载荷 |
|---|---:|---|
| `region_{regionId:x16}/near_{px}_{pz}.hgt` | 1,036,388 | 64 字节 header + 509² × float32 局部 Y（厘米） |

header 里 `magic/formatVersion/seed/terrainVersion/regionId/patchX/patchZ/edge/floatCount/crc32` + 16 字节保留（**必须为 0**，谁要往里放东西就 bump formatVersion）。目录名已经写了 seed 与 version，header 再自证一遍：文件名说是哪一版、内容就得对得上，两边不一致按坏文件删掉重算。写盘 `.tmp` → `Flush(true)` → `Move(overwrite)`；新实例只列目录、不读内容（§7.2 的"轻量校验"口径），所以上一次运行留下的 25 块重启后照样计入占用与 LRU——不然预算闸门一重启就把占用数成 0，会整目录重写一遍还不觉得超。

**调度器的四条口径**：

- 窗口 5×5 以"玩家所在 patch 格"为中心，`patchX = floor(localX / 50800)`（边界归右侧那块，负坐标也按 floor）；
- 重中心是**单向滞后（施密特触发）**：越过格边界还要再走 127 m 才动一格，往回同理，所以站在边界上抖 ±10 m 一次都不会换心；端点行为钉在"到余量末端即动"（`playerCm >= boundary + h` 才换），回程用同一条规则的镜像；跳两格以上按传送直接对齐，滞后此时没有意义；
- 加载队列由近及远：环号 → 平方距离 → (dx, dz) 全序，同一位置两次计划给完全相同的顺序（`LoadOrder_IsNearestFirstAndIdenticalAcrossRuns` 断言前 9 块正好是中心 + 第一环 8 块）；
- 一次计划最多排 4 块（`PatchLoadsPerPlan`）——注意这是**排队的上界，不是帧时预算**：冷跑 179 ms/块，4 块就是 716 ms，就算只排 1 块也超一帧。所以生成必须离开主线程（`FillPatchHeights` 本身按行分片、结果与线程数无关，可以整块丢给 Task），客户端只负责"好了再加"。真正能让主线程承受的是热跑那 6 ms/块；这条量出来的结论会直接决定 PIE spike 里驱动方式怎么搭；
- 出窗的 patch 在**计划生成时**就从驻留集摘掉（再调一次不会重复报卸载），但磁盘文件不删——回来时走命中。引擎自己丢了某块要 `MarkUnloaded`，否则调度器再也不会加载它（这条有正反两半夹具）。

**实测耗时**（`dotnet run --project Tools/DemProbe -- stream Data/GEBCO_2026/derived <lat> <lon>`，同一窗口冷跑一次、换新调度器热跑一次，逐块比 crc）：

| 地点 | 冷跑 ms/块 | 热跑 ms/块 | 提速 | 25 块磁盘 |
|---|---:|---:|---:|---:|
| 30.0N 120.0E 海岸（近景含细节层） | 179.4 | 6.24 | 28.8× | 24.7 MB |
| 28.5N 86.9E 高原 | 175.7 | 6.07 | 29.0× | 24.7 MB |

冷跑 = 上一轮的 141.8 ms 生成 + 33.9～37.6 ms 写盘并 `Flush(true)`（两处地点各减一次）。热跑里 **83–87% 是算 crc**（单块 1,036,324 字节逐字节查表 5.07–5.41 ms，专门拆出来量的，否则下一步优化就是瞎猜）；真要压这 6 ms，方向是硬件 CRC32、或只对 header + 抽样块体做校验，但那会让"每次读都全量验"这条保证打折，先不动。

**四条反证都跑过，且都只红在该红的地方**（改坏→跑→复原→再跑，`MUTANT` 字样已全部清出仓库）：

| 变异 | 结果 |
|---|---|
| 读取时 crc 期望值改成"从载荷现算" | 红 1 条：`SinglePayloadBitFlip_IsCaughtByCrc` |
| `PathFor` 去掉 `region_` 那一层 | 红 2 条：`CrossRegion_SamePatchCoords_DoNotCollide`、`DirectoryRootIsNamedBySeedAndVersion` |
| `Advance` 去掉滞后带直接返回 candidate | 红 2 条：`Hysteresis_DeadBand...`、`JitterAcrossABoundary_DoesNotChurnTheWindow` |
| 命中时只 `CopyTo` 半程载荷 | 红 15 条（所有逐位相等与截断类判据一起塌）——证明这批判据不是恒等式 |
| 把 `Tick` 里的 `TrimToBudget` 注掉 | 红 1 条：`Tick_EnforcesCacheBudget_InsideBudgetNothingIsEvicted`（条目停在 25、淘汰 0） |

**§7.1 那个 2 GB 上限现在真的有人执行了**：`Tick` 每帧调一次 `TrimToBudget(cacheBudgetBytes)`，没超预算时那次调用只比一个数就返回（所以帧时不受影响），超了才按最后访问逐块淘汰。之前它只是文档里的一个数字——写了预算、没人读，走 100 个区域就能无声涨到几 GB。构造时另挡一种自欺配置：预算小于一块 patch（1,036,388 字节）等于刚写就删，直接抛。判据两半都配了：默认预算冷加载 25 块 → 淘汰 0；预算只够 10 块 → 条目收敛到 10、淘汰 15、盘上文件数同步变 10，而引擎侧驻留集仍是满的 25（磁盘淘汰不等于世界里的地块消失）。

**每条拒绝分支都配了同夹具的正向对照**：改坏单个 header 字段 → 断言它落进**自己那一档**计数器 → 断言文件被清掉 → 原样重写 → 断言读回逐位相同。`DescribeStats()` 把 `reject_badmagic/badreserved/badgeometry/badseed/badterrainversion/badregion/badpatchcoords/badcrc/...` 全部逐条念出来（缺档就等着哪天误报死值），未命中（`MissingFile`）单列在 `misses=`，不混进"坏"里。

**这一轮咬到自己的五处**（都改成了判据或按实测纠正）：

1. 首版 `PlanetPatchCache.cs` 里留了草稿残渣——`Try { ... } catch { }` 这种 C# 里不存在的语法、`if (... && false) { }` 这样的死行、`static (() => ...)` 的错括号。写完立刻编译才逮到（读代码时它们混在正常行里滑过去了）；重写成一份干净的，而不是打补丁。
2. `PatchOf` 的上限期望我写成了 787 格——那是按"400 km"算的，而常量后来改成了协议里那个 100 km（`PlayerStatePacket.MaxAbsLocalCm`，PlanetWire.cs:91），实测是 196 格。数字必须从它引用的那个常量重算，不能沿用改之前的推导；顺带把两处口径合一，只留一处定义。
3. 三条判据一开始红，原因都在夹具而不是产品：新调度器的驻留集本来是空的（`ToLoad` 自然是 4 不是 0）、默认预算 4 挡住了"只换一列 = 5 块"的断言、直读缓存那一次已经先把 `cache.Hits` 记成 1。改成"预算 25 的夹具量窗口算术、默认预算的夹具量收敛"，两件事分开量；
4. 计划第 7 帧装满 25 块、第 8 帧才报 `IsSatisfied`——`IsSatisfied` 说的是"计划时还缺不缺"，不是"这一帧加载完没加载完"。判据按实测写成 `{4,4,4,4,4,4,1,0}` 与 `{0,4,8,…,25}`，没有去把实现改成迎合"7 帧"这个我先验的说法；
5. `Assert.NotEqual(byte[], byte[])` 直接编译不过（重载被推到 `IEnumerable<T>/IAsyncEnumerable<T>` 上），改成两边各算 crc 再比标量——"拿数组比较来反证内容不同"这种写法本身就不牢，能编译也未必是逐元素比。

**接线状态（诚实口径）**：`PlanetPatchCache`/`PlanetTerrainStreamer` 目前只有两类调用者——判据和 `DemProbe stream`。客户端/服务端都还没有一行代码引用它们，PIE 里 `AddPatch`/`SetupPatchHeightMap`/碰撞烹饪的耗时（任务 3）仍未量，`Game.Build.cs`、`HundunWorldGame.cs`、`World.scene` 三个文件还在被并行会话改着，加 dll 引用绕不开它们。所以下面这张成本表证明的是"纯 C# 侧准备好接了"，不证明"游戏里已经在流送"。

### 13.8 中景/远景 LOD 网格（同日第五轮）

落在 [Horizon.Planet/PlanetRegionMesh.cs](../Horizon.Planet/PlanetRegionMesh.cs)：`BuildTile` 出一块区域的球面网格，`BuildLevel` 把一个层级（6 面 × 4^level 块）按**整数厘米焊接**成封闭球面，`Analyze` 出拓扑自检报告。测试 **159/159 绿**（本轮 +18）。

**为什么中远景不能用切平面**：切平面是近景的约定（5 km 内下垂 < 0.24 m）。区域层级一粗，tile 就到几百 km～10 万 km 量级，平面片会整体浮在球面外几十米到几万公里。所以这里的顶点直接建在 `方向 × (R + 高程)` 上，方向由 `CubeSphere.ToDirection(face,u,v)` 给出（**已归一化**——用未归一化的立方体投影点会把半径放大到最多 √3 倍，判据 (1) 专抓它）。近景与远景因此共面：实测同一 8 km 山顶，近景点半径与远景球面半径差 **< 1 cm**（余量给的是 float32 局部 Y 在 8 km 处的 0.0625 cm ULP，`NearPatchAndFarMesh_SitOnTheSameSurface` 还配了反证那一半：拿"远景忘了加高程"的口径去比，差值就该等于海拔本身）。

**焊接按厘米而不是 double**：相邻 tile 的公共点由不同表达式算出（面内共享 `u` 端点、面间换轴向），double 层可能差最后一个 ULP；落到整数厘米就是同一个点，`BuildLevel(0)` 的顶点数正好等于立方体细分的公式值 `8 + 12(n−1) + 6(n−1)²`，`E = 3F/2`、`χ = 2`、边界棱 0、非流形棱 0。单块 tile 则 `χ = 1`（圆盘）、边界棱 `4n`、棱数 `2n(n+1) + n²` 独立算一遍对账。

**实测 LOD 阶梯**（`dotnet run --project Tools/DemProbe -- mesh Data/GEBCO_2026/derived 4`，face 0 面中心 tile，quads/edge = 4）：

| 区域级 | tile 宽 km | 网格间距 km | 选中基场级 | 该级像元 km | 建一块 tile | 拓扑 |
|---:|---:|---:|---:|---:|---:|---|
| 0 | 100,075 | 25,019 | 5（回退档） | 148.3 | 25.5 ms | χ=1 bnd=16 inward=0 degen=0 |
| 2 | 28,774 | 7,193 | 5（回退档） | 148.3 | 3.9 ms | 干净 |
| 4 | 7,907 | 1,977 | 5（回退档） | 148.3 | 0.75 ms | 干净 |
| 6 | 1,990 | 498 | 5（回退档） | 148.3 | 0.94 ms | 干净 |
| 8 | 498 | 124 | 5 | 148.3 | 0.96 ms | 干净 |
| 10 | 124 | 31 | 3 | 37.1 | 14.5 ms | 干净 |
| 12 | 31 | 7.8 | 1 | 9.3 | 26.3 ms | 干净 |
| 14 | 7.8 | 1.9 | 0 | 4.6 | 37.4 ms | 干净 |

`PlanetLod.DemLevelForSpacing` 的口径是"**像元 ≥ 网格间距里最细的一级**"（比它细的一级会欠采样出摩尔纹：山随视角忽有忽无）。标注"回退档"的四行是**真的没有够粗的级可选**——金字塔最粗只到 8′（148 km），所以网格间距 > 148 km 时它回最粗一级，调用方可以用 `CellWidthCm < spacing` 看出这一步是欠采样的。耗时别横向比：25.51 ms 那行含本进程第一次读该文件的块加载，10/12/14 三行贵是因为它们分别落在基场 3/1/0 级上（同样的 25 个点会摊到更多个 `DemGrid` 缓存块里，是随机 IO 而不是算术）。真正比的是"同一级基场内部"的相对量，以及远景球面整体 34.1 ms 建完。远景球面（`BuildLevel(0, 4)`，最粗一级基场）实测 `V=98 F=192 E=288 χ=2 bnd=0` **CLOSED**，采样到的半径偏差 min −5,824 m / max +2,817 m——说明远景画的是有起伏的地球，不是一个球。

**由这张表得到的具体结论**：网格间距 > 148 km 时（区域级 ≤ 6 的 tile、以及远景球面）金字塔没有够粗的级可用，`DemLevelForSpacing` 只能回最粗一级，调用方用 `CellWidthCm < spacing` 能看出这一步欠采样。**这一条我原本推荐"再加 3 级（6/7/8 → 像元 296/593/1186 km，合计约 2.4 MB），命令 `--levels 9`"——那个建议的前提是错的，见 §13.10：源几何的象限对齐使 level ≥ 6 无法由这套 2:1 配对链造出来。**

**本轮的四条反证**（改坏→跑→复原，`MUTANT` 字样已清出仓库）：

| 变异 | 结果 |
|---|---|
| 三角形绕序整体翻过来 | 红 8 条（tile/球面/六面的 `inward` 判据一起） |
| 关掉焊接（每块 tile 各留一份顶点） | 红 4 条：`LevelZero(n=1,2,4)`、`LevelOne`（边界棱和非流形计数立刻暴露） |
| 把 tile 水平镜像（`u` 用 `n−i`） | 红 11 条：绕序、uv 格点等式、相邻 tile 共边全部塌 |
| （上一轮的 5 条闸门反证见 §13.7） | — |

镜像那条还暴露了我自己写错的**夹具算术**两处：`24×2×2` 当成 24 块 tile 的三角形数（每块是 `2n²=8` 个，应是 `24×8`）、`RegionKey(2,3,5,9)` 越出 level 3 的 y 上界（span=8）。另外 `LevelOne` 原来只断言封闭不断言朝外——是"关掉焊接"那条变异跑出红之后才补上的：**一条判据没红过，就不知道它拦不拦得住**，所以每条反证的红都往回改出了新断言。

**接线状态**：`PlanetRegionMesh` 仍只有判据和 `DemProbe mesh` 两类调用者；喂给引擎的那一步（`AddPatch` 之外的远景 mesh 提交）和任务 3 一起等 Flax 侧文件腾出来。

### 13.9 端到端走查：跨区不丢实体（同日第五轮之二）

前面四轮都是"一个单元 + 一套判据"。这一轮把 **区域 → 帧 → 量化 → 编码 → 解码 → 迁移握手 → 位置重建 → patch 生成 → 落盘 → 重启读回** 串成一条路径跑（[PlanetWalkIntegrationTests.cs](../Horizon.Planet.Tests/PlanetWalkIntegrationTests.cs)），路径选在**赤道上跨过立方体面边界**的那一段（0.27° 经差 ≈ 300 km）。测试 **164/164 绿**（本轮 +5：走查 2 条 + 姿态基判据 3 条）。

**实测回执**（走查自己印的一行，判据里也各带了上界）：

```
walk steps=120 regions=78 crossings=77 face_changes=1 step_max=2523 m
wire_drift_max=0.773 cm  transfer_pure_max=0.773 cm
transfer_quat_max=25.4 cm  quat_budget_max=81.4 cm  radius_drift_max=1.060 cm
```

- **P1 那句"位置误差 ≤ 1 cm"第一次被量出来：0.773 cm**。它是"上报一个点 → 过线 → 另一个进程按 RegionId 重算帧 → 重建"的完整往返，不是单步量化的上界。跨区迁移后同样 0.773 cm——**"跨区不丢实体"这一半也量到了**（77 次迁移、1 次跨面，没有一次把玩家弹走或算到隔壁帧）。
- 反证不空转的两条：`crossings >= 40` 与 `face_changes >= 1`（跨面才测得到轴向翻转），另加 `step_max` 必须小到不能整块跳过区域，否则 crossings 这个数本身就不可信。
- 离面距离 1.060 cm 单列：它等于"全局点取整 ≤ √3/2 cm"加上"重建漂移 ≤ 1 cm"，和位置漂移是两个量，不能顺手都写成 1 cm。

**这一轮串出来的真缺陷（前四轮都看不见）**：`QuaternionQuantized` 的 `Rotate` 我按 `w²·v + 2(v·q)q + 2w(q×v)` 写了——**漏了 `|q_vec|²` 那一项**，正确闭式是 `(2w²−1)·v + …`。后果不是"差一点"：`w = 0` 的姿态（绕水平轴 180°）会让它退化成 `2(v·q)q`，凡与转轴垂直的向量**直接被归零**。走查里它的表现是 `Rotate(X)` 与 `East` 差恰好 `1.000`——一个单位基向量被投成了别的东西，量级上就不像量化噪声。

为什么单元测试没抓到：线协议那批判据是**round-trip**（写出去读回来逐字段相同），而 `Rotate` 是当时还不存在的消费侧方法——round-trip 再全也证明不了"能被正确使用"。这一条正好是 §12.7 那类"声明了字段没人读/写了没人消费"的镜像：**ack 里带着姿态，但两端都没有把它变成坐标的能力**，直到接上走查才第一次真用。补齐的两端都进了产品代码：`FromBasis`（发侧，带右手正交校验，非法基直接抛）与 `Rotate`（收侧），并各配判据：恒等基、90° 循环基、**w=0 的 180° 基**（回归钉）、六个真实区域帧的基偏差 `< 1e-4`、以及"上界从 Q15 步长推出来"而不是挑一个刚好过得去的数。

**量化姿态能不能当位置用——量出来的答案是不能**：同一个跨区点，纯函数帧 0.773 cm，按 ack 的原点 + 量化姿态重建 **25.4 cm**。这不是 bug 是算术：基偏差 ≤ 1e-4 是"每个轴像"的误差，作用到 |v| 上按 √3·|v|·上界 放大，而高原上的力臂含 3.2 km 抬升（|v| ≈ 3.7 km），所以位置误差比基偏差大两个量级。判据写成关系（`误差 ≤ √3·|v|·1e-4 + 1 cm`，逐次取最大），不是拍一个 20 cm——第一版拍的 20 cm 当场被 25.4 cm 打脸，正说明magic number 不可信。结论进协议口径：**姿态只配当校验/朝向，位置重建必须走 RegionId 纯函数帧**。

**两条反证**（改坏→跑→复原，`MUTANT` 已清出仓库）：

| 变异 | 结果 |
|---|---|
| `Rotate` 退回漏项的 `w²·v` | 红 4 条（三条姿态判据 + 走查），含专门钉 w=0 的那条 |
| `FromBasis` 把上/南两列接反 | 红 3 条；拒绝类判据仍绿——它测的是"非法基被拒"，与列序无关，这个"没红"是对的 |

**夹具又咬到我两次，而且是同一个错**：`RegionKey(2, 3, 5, 9)`（level 3 的 span=8，y 越界）和 `RegionKey(5, 12, 4096, 4095)`（level 12 上界 4095）——**区域下标上界必须按 `1 << level` 现算，不能凭印象写整数**；两次都是编译过、运行时大声抛出来才发现（`RegionKey` 的构造校验在这里当了判据）。第三次是 `Assert.Contains("writes=40")` 打在**重启后的实例**上——计数是每个实例各自的账（写发生在第一个实例），改成 `cache.Writes == 40` 与 `reopened.Hits == 40 && reopened.Writes == 0 && reopened.Misses == 1`，那次唯一的 miss 就是跨区域探针。

**接线状态**：`Horizon.Planet` 这条链在纯 C# 侧已经能从头走到尾（含跨面迁移与磁盘缓存重启），但客户端/服务端仍然一行都没调用它——`PlanetWire` 的四种消息还没进 `Horizon.Game.Message` 的 `MessageType`/`MessageUnion`，`Game.Build.cs`/`HundunWorldGame.cs`/`World.scene` 仍在被并行会话改，PIE 里 `AddPatch`/碰撞烹饪的成本还没量。

### 13.10 任务 10 跑不了：金字塔的对齐上限就是 level 5（同日第六轮）

§13.8 里"再加 3 级，命令 `--levels 9`，代价约 2.4 MB"这条建议**前提是错的**，所以我没有跑，先取证。

**闸门在工具自己身上**：`Pyramid.begin_quadrant` 要求每幅的 `row_off / col_off` 能被 `2^max_level` 整除，否则 2:1 纵向配对会跨幅错位——它不是保守检查，是这套写法的正确性条件。而 GEBCO 的几何是固定的：象限 `21600 = 2^5 × 675`，全球 `43200 = 2^6 × 675`、`86400 = 2^7 × 675`。于是：

| max_level | 2^lv | 21600 % 2^lv | 43200 % 2^lv | 结果 |
|---:|---:|---:|---:|---|
| 5 | 32 | 0 | 0 | 闸门通过，`level_dims` = 2700×1350 |
| 6 | 64 | **32** | 0 | 拒绝：`col_off=21600 不能被 2^6 整除，配对会错位` |
| 7 | 128 | **96** | **64** | 拒绝（且 `43200>>7 = 337` 会静默少 64 行） |
| 8 | 256 | **96** | **192** | 拒绝（`86400>>8 = 337` 同理） |

取证脚本是本机的一次性验算（`Tools/_*` 按仓库约定不入库；`git ls-files 'Tools/_*'` 为 0）：它用 `Pyramid.__new__` 跳过 `__init__`（`__init__` 会 `"r+b"` 打开各级文件），只调真闸门，**不创建、不截断、不改写任何产物**——跑完 `pyramid/` 仍只有 `level_01..05.bin`（没有 `level_06`），`manifest.json` / `verify_receipt.json` / `level_05.bin` 的 mtime 仍是上一轮构建的 `07:41`/`07:29`，而探针跑在 `21:44`。不在本机的人可以直接验算闸门条件：`21600 % 64 == 32`（非 0 即被拒），`43200 % 128 == 64`、`86400 % 256 == 128`（`level_dims` 用 `>>`，会静默丢行丢列）。

**我当时怎么错的**：只对"像元尺寸"做了换算（15″×2^k ↔ 296/593/1186 km，按 1° = 1111 km），那部分算术现在仍然对；错在**没去查生产端那条象限成对不变量**——它正好是 §12.1 那套配准/对齐假设的边界。"尺寸算得出"和"这套链造得出"是两件事，后者要读实现才能知道。顺带订正两个耗时口径（都是从 `build.log` 读的，不是印象）：完整 0-5 构建 **214 s**，`--verify-only` 复核 **122 s**；也就是说重跑整条链也造不出 6 级以上——闸门是几何性的，与跑多久无关。

**能真正消掉欠采样的三条路**（§13.8 表里区域级 0–6 与远景球面全在回退档）：

| 路线 | 做法 | 代价 | 适合 |
|---|---|---|---|
| A 独立粗级全球格 | 不再递归 2:1，改成按**度分桶**直接聚合（每桶存 均值/最低/最高 三元组），配新 reader 与新闸门 | 新产物 + `DemGrid` 要认第二种网格 + 一轮反证 | 远景要的是**山形剪影**：148 km 单元的均值会把 5 km 山峰抹平，min/max 才留得住轮廓 |
| B 采样侧面积平均 | 数据不动；粗网格顶点对 L5 做 n×n 超采样取均值（`PlanetRegionMesh` 的高度源换成"盒式平均采样器"） | 纯 C#，几十行 + 判据；每顶点采样量随间距增大 | 立刻消掉摩尔纹（山随视角忽有忽无），零数据风险 |
| C 接受欠采样 | 远景只当剪影，把结论钉在文档里 | 0 | 如果远景永远不参与碰撞与落地判定 |

**建议**：先做 **B**（它把可见缺陷直接拿掉，且不碰共享数据），A 留到远景真的需要真山形时再做——A 会改数据格式与 reader 语义，那是比"补一级金字塔"大得多的动作，值得单独拍板。

**路线 B 已落地**（同日第六轮之二，用户选"先 B 再 A"）：新增 [Horizon.Planet/Dem/DemBoxAverageSource.cs](../Horizon.Planet/Dem/DemBoxAverageSource.cs)（面积加权 k×k 抽头采样器）与 `PlanetLod.PlanSampling` → `LodSamplePlan{DemLevel,TapsPerEdge,CellWidthCm,FootprintCm,RequestedSpacingCm,FullyCovered}`。抽头数**向上取整**（向下取整会留下没平均到的缝，"看着像 B、实际还在摩尔纹里"），上限 8/轴；到不了就在计划里写 `covered=False`。测试 **172/172 绿**（+8）。

真实产物上的对照（`mesh Data/GEBCO_2026/derived 4 alias`，把整张顶点网格沿经度平移 0.37 个像元，看同一批顶点的读数最大变化）：

| 区域级 | 网格间距 | 抽头 | covered | 逐点摆幅 | 平均摆幅 | 压低 |
|---:|---:|---:|---|---:|---:|---:|
| 0 | 25,019 km | 8（夹住） | False | 143.5 m | 42.9 m | 3.3× |
| 2 | 7,193 km | 8（夹住） | False | 143.5 m | 67.6 m | 2.1× |
| 4 | 1,977 km | 8（夹住） | False | 483.1 m | 83.6 m | 5.8× |
| 6 | 497.5 km | 4 | True | 163.4 m | 59.8 m | 2.7× |
| 8 | 124.4 km | 1 | True | 131.0 m | 131.0 m | 1.0×（不需要平均） |
| 10 / 12 / 14 | 31 / 7.8 / 1.9 km | 1 | True | 31.1 / 1.3 / 0.2 m | 同左 | 1.0× |

三件事被这张表定下来：

1. **B 真的把中景的摩尔纹压下去了**（区域级 4–6 压 2.7–5.8×），而且 `taps=1` 的档位（区域级 8–14，网格比基场细）与逐点采样**逐位同值**——平均层在没有需要时不加成本也不改数值，这条是退化档正向对照。
2. **B 罩不住远景球面**：8 抽头最多覆盖 1,186 km，而 level 0 的网格单元是 25,019 km，残余摆幅仍有 43–68 m。`covered=False` 就是这件事的回执，不是把它藏起来——**A 仍然要做**，而且远景要的是山形（min/max），不是均值。
3. 成本不是问题：25 顶点的 tile 上 64 抽头/顶点与逐点同量级（9.8–4.9 ms 对 1.0–19.6 ms，被 `DemGrid` 块缓存主导，看不出稳定的贵）。近景那条链一行没动，所以 §13.7 的 141.8/178.9 ms 仍然成立。

反证一条：把抽头网格塌成"全部落在查询点上"（`half = 0`，等价于重复采同一个点再做加权）→ 红 2 条：`Spike_StopsComingAndGoingWithSamplingPhase`（摆动没被压）与 `Mean_UsesCosLatitudeWeights_MatchesClosedForm`（cos 权重对不上闭式）。也就是说"面积平均"这个机制的两个部件——**抽头铺开**和**纬度加权**——各自都有判据盯着，不是一个整体黑箱。

判据刻意写成关系而不是绝对阈值：夹具上的摆幅与真数据差几个数量级，"平均后摆幅 < 逐点的 1/4 且峰没被抹平（摆幅 > 10 cm）"这一对关系在两种数据上都成立，而任何单一阈值都会在某一边失效。

### 13.11 路线 A 的必要性判据：实测残余都在像素底线下（同日第六轮之三）

A 会引入新产物文件、让 `DemGrid` 认第二种网格、给 manifest 增段——动作很大。所以在动它之前先问一句：**那些残余摆动在游戏里看得见吗**。用 §13.10 在真实产物上量到的残余，按 1080p / 60° 垂直视场换算（每像素 `9.696e-4 rad`）：

| LOD | 网格间距 | 实测残余摆幅 | 残余转成亚像素所需的最短观察距离 | 比值 残余/间距 |
|---|---:|---:|---:|---:|
| level 0 | 25,018.9 km | 42.9 m | 44.2 km | 1.7e-6 |
| level 2 | 7,193.4 km | 67.6 m | 69.7 km | 9.4e-6 |
| level 4 | 1,976.9 km | 83.6 m | 86.2 km | 4.2e-5 |
| level 6 | 497.5 km | 59.8 m | 61.7 km | 1.2e-4 |
| level 8 | 124.4 km | 131.0 m | 135.1 km | 1.06e-3 |

关键的比较是**最后一列与每像素张角**：一个 LOD 只会在"距离 ≥ 它自己的间距"的场合被使用（更近就该切到更细的一级），所以在那个最坏距离上，只要 `残余/间距 < 9.7e-4` 就还是亚像素。level 0–6 比这条线低 8–2 个数量级，**B 的抽头上限带来的 `covered=False` 在画面上不存在**；level 8（`taps=1`，本来就不做平均）恰好在 1 像素附近，它的处置办法是"更近就换 level 10"，不是"造一个更粗的基场产物"。

再加上几何视界：10× 半径上，眼睛抬高 1000 m 的几何视界是 **357 km**，5000 m 的高峰可看到 **798 km**（两者相加约 1155 km 内还能看见山尖）——这个范围整段落在 level 6 / level 8 已经达标或靠换级解决的区间里。

**结论**：路线 A 对游戏玩法不需要，任务 11 改为"条件性"——只有当真的要做**整颗星球视角**（入轨、过场、全球地图）时才值得做，而那时正确的原语也更可能是影像金字塔（贴图 + min/max 瓦片），不是给网格再加一种基场。这是先算账再动手省下来的一整个数据格式工程。

### 13.12 后台生成泵：一次偶发红追出"把读不动当成读坏了"（同日第七轮）

落在 [Horizon.Planet/PlanetPatchPump.cs](../Horizon.Planet/PlanetPatchPump.cs)：主线程只做 `Request` 投递与 `TryDequeueCompleted` 取件，采样、落盘、缓冲区回收都在线程池上；配套 `dotnet run --project Tools/DemProbe -- pump Data/GEBCO_2026/derived <lat> <lon>` 在真实 DEM 上量同一件事。测试 **185/185 绿**（本轮 +11：泵 10 条、缓存分类 1 条）。

**泵定的五件事**（每件都有判据，不靠"看起来会好吗"）：

1. **换线程不换数**：泵算出来的高度与主线程同步算的**逐位相同**（`PumpResults_MatchTheSynchronousPathBitForBit`；真数据上 workers=1/2/4 的中心块 crc 全等于同步路径的 crc）。多 worker 时每个 worker 用自己的一份 `HeightSampler`——它的 5×5 模板是可变的，共用会互相踩出错值。
2. **位子就是限流**：先占位再干活，`max_inflight` 两头断言（没超上限，也确实并到过），只断"没超"的话限流形同虚设也照样绿。
3. **换区不许落地旧结果**：`Work` 完成时比较投递时的代次；旧结果丢进 `StaleDropped` 并把缓冲区回收。排队项在 `BeginRegion` 当场清掉。
4. **等式必须平**：`Requested = Completed + StaleDropped + DuplicatesRejected`，`GenerationFailures` 与 `ReadyDiscarded` 明确标成子口径不参与相加。写这条判据时我先断言 `Completed == 0` 跑出红——"完成"和"成品未取走就被丢"被我算进同一个桶，等式看着平其实重复计数，于是拆出 `ReadyDiscarded`。**一条从没红过的等式，不能证明它被真的对过账。**
5. **计数器一律 long**：跑图久了 int 翻负，占用与 LRU 会一次性数错（`Describe()` 把等式两边都念出来）。

**缓存的并发代价是四笔账**（都是并发改出来的，不是重构口味）：

- `_bytesUsed +=/-=` 与 `Dictionary` 不原子；更阴的是 `File.Move` 与 `Track` 之间被 `TrimToBudget` 插进来，会出现"账上有条目、盘上没文件"。改法：账本一把锁，**落位与记账在同一锁段内**（1 MB 的写与 fsync 留在锁外，不串行化磁盘），临时名带实例内递增序号，淘汰"删文件+出账"也在同一原子段里。
- Windows 上目标正被读时覆盖式 `Move` 抛拒绝访问：这不是坏文件。改成有界重试 3 次（只跨那个短窗口），最终失败清临时文件并统一包成 `IOException`——`Write` 从此只有一个契约"要么落盘、要么 IOException"。
- **只有读到的内容能证明损坏。**长度不对、header 不符、crc 不符才算坏；`ReadAllBytes` 抛出来的 `IOException`/`UnauthorizedAccessException`（共享冲突、被占用、刚被淘汰）只能算未命中，而且**不许删文件、不许出账**。原来这两类都会掉进 `BadLength` 并 `Discard`，一次竞态就会销毁一片好数据。
- 嵌套超额订阅：泵给了一层并发，`FillPatchHeights` 内部又按核数分片，变成 `workers × 核数` 个线程抢 `核数` 个核。改成 `parallel: _maxWorkers <= 1`（同一次运行内 workers=1 wall 13.0 s、workers=2 31.5 s；跨运行的绝对数会漂，见下一段）。

**那条偶发红是怎么追的**：整套连跑第 2 轮出现 1 条红（`ConcurrentWritesAndTrim_KeepTheLedgerExact` 的 `CorruptRejects` 期望 0 得 2），随后 3 次全绿——典型的"红了也说不清"。先把假设写死成**可确定性复现**的判据 `LockedFile_IsNotJudgedCorrupt_AndSurvivesTheFailedRead`：用 `FileShare.None` 独占打开自己刚写的文件（等价于任何一次并发读撞上占用，不需要运气），断言"读不动只能算未命中、`CorruptRejects` 仍是 0、文件必须还在"，再用翻一个比特做正向对照（真坏才进损坏账并消失）。这条判据当场把两个分类学问题钉住，之后连跑 12 次 0 红、临时目录 0 残留。

**顺带查出的测试卫生问题**：临时目录里攒了 25 个 `hundun_dem_*`——`TempDem.Dispose` 把删除失败整个咽掉了。真实原因是最后一个 `FileStream` 关掉后 Windows 还会短暂占着目录（进程一退出就能删 ⇒ 时机问题不是永久锁），另外有一条判据里 `new DemGrid(...)` 没 `using`，句柄一直开着。改法：重试窗口拉长到 8×50 ms，仍失败就把路径打到 stderr（静默吞掉才是这批残留的来源），并把那条漏掉的 `using` 补上。清完连跑 12 次残留为 0。

**这条"0 残留"后来被推翻了**（同日第八轮，见 §13.13）：全量跑稳定留 1 个 `hundun_dem_*`，而只跑 `DemGridTests` 是 0——上一轮把残留归因成"句柄时机 + 一条漏掉的 `using`"，判据却只在**它自己筛过的那一组**里数残留，于是漏掉了另一个文件里的调用点。真正的机制是 `TempDem.Create(...).Load()` 这种**链式一次性调用**：`Load()` 交出清单，清理目录的责任还挂在没被任何变量绑住的 `TempDem` 上，它永远不会 `Dispose`。这不是时机、重试也不救，是恒定的 1 个。改法是把夹具绑进 `using var dem`，并把"数残留"从按组筛改成**全量跑完之后**数（分组跑各自 0 不能合成总体 0）。

**真数据量到的（这台机器今天很吵）**：

```
lat=30 lon=120 region=Region(f2 L14 3462,2730) patches=25
主线程同步 25 块：wall=8287 ms  单块最长=949.7 ms
workers  wall_ms  相对同步  max_inflight  主线程单帧最长ms  中心crc一致  一个窗口省下的分配
      1    13043      0.64x             1              13.62        OK          3.0 MB
      2    31456      0.26x             2               0.40        OK          4.0 MB
      4    21846      0.38x             4               0.37        OK          5.9 MB
```

结论只有一条站得住：**泵不是速度收益，是"主线程不被占"的收益**——同步路径单块最长 950 ms 压在帧上就是几帧的白屏，而取件路径是亚毫秒；wall 反而更差（不再一次性占满全部核）。缓冲区池一个窗口省下 3–6 MB 的 1 MB 级分配（LOH 抖动来源）。

**计时纪律（这条比上面任何数字都重要）**：同一份二进制、同一份数据，"搬 1 MB 做一遍校验"在今天从 5.4 ms 漂到 8.8 / 34.2 / 46.9 ms，倍率 6–9×；我一度写的"单帧超过 1 ms 就算失败"于是变成随机红。现在探针里那项只是观察值，真正的断言在单元测试 `DequeuePath_IsFarCheaperThanCheckingOneMegabyte`：**用同进程内一次 1 MB 校验的耗时当尺子**（取件必须比它的 1/4 还便宜），并且量之前先等 CPU 空下来。PIE 阶段所有耗时判据都要按这个写法——绝对毫秒数只能记账，不能当闸门。

**四条反证，各红一条、不连带**：去掉 `_scheduled.TryAdd` 去重 → 红 `SameKeyWhileInFlight_IsNotGeneratedTwice`；去掉 `Work` 的代次比较 → 红 `RegionChange_InFlightResultsNeverLandInTheNewRegion`；把 1 MB 校验塞进取件路径 → 红 `DequeuePath_...`；位子在工作开跑前就 `Release` → 红 `WorkerLimit_IsBothRespectedAndActuallyReached`（`max_inflight` 掉到 1，"两头断言"在这里显出用处）。

**接线状态**：泵目前只有单元测试与 `DemProbe pump` 两个调用者。PIE 驱动层的调用顺序已经定死：每帧 `streamer.BuildPlan` → 新块 `pump.Request` → `TryDequeueCompleted` 交给引擎 `AddPatch/SetupPatchHeightMap` → `pump.Release`；换区 `pump.BeginRegion` + `streamer.AttachRegion`；`Dispose` 只在退出时调（它会等在飞的收尾）。任务 3 仍等 `Game.Build.cs`/`HundunWorldGame.cs`/`World.scene` 腾出来。

### 13.13 跨面相邻与服务端迁移校验：两条被我当成先验的规则（同日第八轮）

落在 [Horizon.Planet/PlanetRegions.cs](../Horizon.Planet/PlanetRegions.cs) 与 [Horizon.Planet.Tests/PlanetRegionsTests.cs](../Horizon.Planet.Tests/PlanetRegionsTests.cs)（+16 条，全量 **201/201 绿**，`-p:CopyToFlax=false --no-incremental`）。

**这块补的是哪个洞**：§13.9 的端到端走查里跨区迁移的 `to` 是客户端自己报的，服务端没有"这个 `to` 成不成立"的判定——任何 `to` 都会被接受。`PlanetRegions` 给出共边邻居表与迁移判定：`TryNeighbour` / `EdgeNeighbours`（恒 4 个，闭合面上不存在"少一个邻居"）/ `TrySharedEdge`（**唯一**回程边：0 条或 ≥2 条都判邻接关系不干净，因为自指的邻居表会直接放走错误传送）/ `AreEdgeAdjacent` / `ValidateTransfer`。判定不信客户端报的 `to`：先用全局点反查真正所在的区域，再要求它与 `from` 共边；`TransferDecision.ToString()` 把 `RegionOfClaimedPoint`、离面厘米、半径一并念出，日志能直接答"为什么拒"。七种拒绝各有枚举（`MalformedRegion`/`MalformedPoint`/`LevelMismatch`/`PointNotInTarget`/`SameRegion`/`NotAdjacent`/`TooFarFromSurface`），每个都在同一个夹具上带正向对照；`regionId` 解不开只算坏包，异常不穿过服务端逻辑。

**邻居为什么要推出半格**：`TryNeighbour` 把采样点沿边**推出半格**，因为 u=±1 那条棱同时属于两个面，正好落在棱上的方向有两个并列的最大分量，`FaceOf` 只能按先后规则硬选一面——邻居就成了实现细节而不是几何。

**两条我当成先验、其实错的规则**（都是判据自己红出来才发现的）：

1. **"每条边的中点都踩在立方体棱上"——错。** level 1 的 256 条边里只有 128 条落在面边界（并列差为 0），另外 128 条在面内部（并列差 0.447，三个分量本来就分得出大小）。原来那条判据对所有边都断言并列差为 0，所以恒红。改成两类各守自己的分支，并把"两类都不为空"当结构判据：任何一类归零就说明轴表变了，这条证据要重写。
2. **"回程边就是对边"——错。** level 0 的 24 次跨边实测 **12 次落在对边、12 次落在别的边**（这两个数是临时把判据改成恒假、从失败消息里读出来的，不是推的）。所以 `ValidateTransfer` 与走查都不许拿 `Opposite(edge)` 当回程，只认 `TrySharedEdge` 报出来的那条；`SameFaceNeighbour…` 那条现在守的是真正的不变量——沿回程边再走一次必须落回原块。

**四条反证，各红一处、不连带**：去掉共边检查 → 红 `EachRejectionHasItsOwnReason…`（远处的 `to` 被放行）；去掉离面闸门 → 同一条红（天外坐标被放行）；去掉"点必须在声称的 `to` 里" → 同一条红（抄一个别的同级区域就能骗）；把半格改回按边采样 → 红 `EveryTile_HasFourDistinctEdgeNeighbours`（level 0/2/3/4 全红，邻居直接算破）。每次只翻一处，跑完按字节还原并与备份 `diff` 为空；`if (false &&` 字样已从源码清出。

**顺带推翻了上一轮"残留为 0"的结论**：全量跑稳定留 1 个 `hundun_dem_*`，而按组筛（`--filter ~DemGridTests`）是 0——上一轮的清理结论只在它筛过的那一组里成立。逐文件跑定位到 `HybridHeightFieldTests`：`TempDem.Create(...).Load()` 是链式一次性调用，`Load()` 交出清单后没有任何变量绑住那个 `TempDem`，于是目录永远不 `Dispose`，**恒定漏 1 个**；这与句柄时机无关，重试窗口再长也不救。改成 `using var dem = TempDem.Create(...)` 后连跑两次全量残留为 0。规矩两条：**残留计数要在完整跑之后做，分组各自 0 合成不了总体 0**；验收命令 `dotnet test Horizon.Planet.Tests/... --no-build` 之后 `ls -d $TEMP/hundun_dem_* | wc -l` 必须为 0。

**接线状态**：`PlanetRegions` 目前只有单元测试两个调用者，网关/DS 的迁移入口还没接——`RegionTransferRequest` 注册进 `Horizon.Game.Message` 仍等该目录的并行改动落定（同 §13.6 的口径）。也就是说"服务端拒绝乱报的 `to`"这条能力现在**在库里、不在链路上**，真正闭环要在迁移处理里调到它之后另记。

### 13.14 近景只留一条填块路径：流送与泵共用同一份"读盘→算→回填"（同日第八轮之二）

`PlanetTerrainStreamer.FillPatch` 与 `PlanetPatchPump.Work` 原先各抄了一遍同一条规则（查缓存 → 未命中就 `FillPatchHeights` → 回填写盘）。抄两份的代价不是难看：**抄错的那条会往缓存里写另一种命名空间或另一种字节布局，于是"命中与重算逐位相同"只在一条路径上成立，而两条路径各自都有测试、各自都绿**，分叉要等到 PIE 里两条路同时跑才暴露。现在规则本体收进 [Horizon.Planet/PlanetTerrainStreamer.cs](../Horizon.Planet/PlanetTerrainStreamer.cs) 里的 `internal static PlanetPatchFill.Fill(frame, terrain, cache, sampler, key, buffer, parallel, out cacheWriteFailed)`，两个调用者只保留自己的账（`CacheHits/Misses/WriteFailures` 与 `CacheWriteFailures/GenerationFailures`），计数口径与收拢前逐条对齐，改完 **202/202 绿**（+1 条跨路径判据）。

**新增的那条判据** `StreamerAndPump_ShareOneCacheNamespace_EachConsumesTheOthersWrites`：流送冷写的块，泵再取必须是 `CacheHit` 且逐位相同；泵冷写的块，流送再取同样必须命中。它钉的是"两个调用者对同一块用的是同一个键"（regionId 来自投递时抓到的 frame、px/pz 来自 `PatchKey`），而不是数值本身——数值相同由既有的 `PumpResults_MatchTheSynchronousPathBitForBit` 守。

**三条反证，第三条给出了一个我没料到的答案**：写盘对调 px/pz → 红 `Expected: CacheHit`；只算不读盘 → 同样红；把 `regionId` 混进 `key.X`（跨区命名空间污染）→ **单跑这条新判据是绿的**。新判据只有一个区域，同键同区怎么混都还是自己命中自己，所以它证明不了"不同区域不得共用一块盘"。于是把同一个变异体放进**全量**跑：红在 `PlanetTerrainStreamerTests.CacheHit_IsBitIdenticalToRecompute`。结论两条：闸门可以是分布式的（一条判据没咬住不等于整体没咬住，要先问"这套判据合起来能不能看见"再决定补哪一条）；以及这条污染的归属是**流送层那组**，不是新加的跨路径判据。变异体跑完按字节还原，`2654435761` 已从源码清出。

**顺带把上一轮"残留为 0"的判据补全**：`TempCacheDir` 与 `TempDem` 是同一种夹具，残留要两个前缀一起数——现在的验收命令是 `dotnet test ... --no-build` 之后 `ls -d $TEMP/hundun_dem_* $TEMP/hundun_patchcache_*` 两个计数都为 0（本轮实测 0/0）。

**接线状态**：`PlanetPatchFill` 是 internal，调用者仍只有流送与泵两处 + 单元测试；PIE 驱动层照 §13.12 末尾的调用顺序接，不需要也不应该再自己写一遍填块。

### 13.15 引擎侧实机 spike：布局对上了，帧预算只够"每帧一块"（同日第九轮）

任务 3 跑起来了。载体是 [HundunWorld/Source/Game/Planet/PlanetTerrainSpike.cs](../HundunWorld/Source/Game/Planet/PlanetTerrainSpike.cs)（客户端脚本，标记文件触发，回执写 `HundunWorld/Saved/planet_terrain_spike.result.txt`），在**新建的空白场景**里由脚本自己 `new Terrain()`（桥的 `actor_create` 不认原生 actor 类型），全程不碰 `World.scene`。装配侧只需把 `Horizon.Planet.dll` 加进 [Game.Build.cs](../HundunWorld/Source/Game/Game.Build.cs) 的 `dlls[]`——解析器优先取仓库 `bin/Debug/net10.0` 那份，引擎 Tools 目录不需要动。

**复现顺序**（编辑器单实例，已确认当时没人占用；跑完按 WM_CLOSE 优雅退出）：

```
dotnet build HundunWorld/Source/Game.csproj                    # 从仓库根跑：HundunWorld/global.json 钉了 10.0.300
FlaxEditor.exe -project <abs>/HundunWorld/HundunWorld.flaxproj   # 然后走 21901 的桥
  code_write Game/Planet/PlanetTerrainSpike.cs → code_build_wait（PIE 停着才能信 compilations 推进了）
  scene_new / scene_load 只认**绝对路径**（相对路径报"未找到场景资产"）
  actor_create EmptyActor → ui_script_add 用**简名** PlanetTerrainSpike（全限定名反而找不到类型）
  printf '30 120 14' > Saved/planet_terrain_spike.on → sim_play → 读回执
判据自证：回执首行 build_tag=planet_spike_b4；查装配里有没有这个串要按 **UTF-16** 找，按 ASCII 找会得到假的"没装进去"
```

**A. 引擎契约与离线假设逐条对上**（这条以前只有源码级推断）：`ChunkSize=127`、`HeightmapSize=509`、`PatchSize=50800 cm`、`509²=HeightmapLength` 全部 `contract_ok=1`；`SetupPatchHeightMap` **返回 true 才是失败**（探针 `setup_returns_true_on_failure_probe=0`，整窗 `setup_failures=0`）；另有一条新事实——**每块的第 509 行/列是与邻居共享的末顶点**，单块在场时打不到它（`layout_shared_edge_probes=1 hits=0`），所以相邻块必须交出同一套末顶点数据，否则接缝会各画一半。

**B. `index = z*509 + x`（x=东、z=南）在引擎里成立**：往一块里写 i:j = 100:1 的不对称场（跨度 513 m），按 25 个内部格向下打射线——按"我写的口径"解释最大偏差 **2.06 cm**，按"转置"解释最大偏差 **50194 cm**，差四个数量级，不存在歧义。同一次运行里另一条对照：把同一套高度按生产字段（每块几十米起伏）写入，偏差 **0.16 cm**。

**C. 耗时结构（热态，默认 CollisionLOD，单位 ms/块）**

| 阶段 | avg | max | 能不能离开主线程 |
|---|---|---|---|
| 算高度（`FillPatch`，含细节层噪声） | 6.6 | — | **能**（泵已在线程池上量过，逐位相同） |
| `AddPatch` | 0.08 | 0.19 | 未证 |
| `SetupPatchHeightMap`（上传+建资源） | **23.8** | 30.9 | 未证（引擎发行版不带 `Terrain.cpp`，我没做线程实验：怕触发断言弹模态框钉住主线程） |
| `RemovePatch` | 0.04 | — | 未证 |
| `ExtractCollisionGeometry`（我为了数三角形才调，不在游戏路径里） | 17.1 | — | — |

冷启动那一轮 `per_patch_avg=50.9`、热态 `32.9`——**同一个 PIE 会话里第一轮整场都在付建资源的钱**，把两轮混成一个平均会把结论算错 1.5 倍（b1/b2 就被这个骗过一次）。默认 CollisionLOD 下每块的碰撞三角形是 **516 128**（= 508²×2，全分辨率），25 块就是 1290 万个三角形压在物理上。

**D. `CollisionLOD` 是唯一的旋钮，而它是"上传耗时 vs 物理与视觉一致性"的同一根轴**（每个 LOD 连测两轮，只报第二轮；b2 曾按轮次顺序取数，把 LOD=1 读成 14.7 ms，b4 复测是 34.7/70.1 ms——顺序污染，这条已在下面"我读错的"里记账）：

| CollisionLOD | setup_avg | setup_max | 三角形/块 | 射线偏差 |
|---|---|---|---|---|
| -1（默认=全分辨率） | 23.2 | 28.7 | 516 128 | **0.16 cm** |
| 1 | 34.7 | 70.1 | 127 008 | 5.2 cm |
| 2 | 15.0 | 55.1 | 30 752 | 15.8 cm |
| 3 | 11.0 | 13.3 | 7 200 | **36.9 cm** |
| 4 | 11.3 | 13.5 | 1 568 | 79.1 cm |

**E. 帧预算：§9 那条"5×5 增删不超帧预算"只在"每帧一块 + CollisionLOD≥3"下成立。** 帧步进（每帧只上传一块，LOD=3，25 帧铺完）实测 `upload_avg=11.7 / median=10.9 / max=14.4`，`frames_over_16_7ms=0`，`patches_count_after_stepping=25`。反过来两条都不成立：默认 LOD 下 23.8 ms/块 → 连每帧一块都超；而现行 `PatchLoadsPerPlan=4`（§13.7 为了压窗口切换时间定的）按 LOD=3 算要 4×11 ≈ 45 ms/帧。**所以这里要拍板，不是我能顺手改的**：要么把每帧加载数降到 1（窗口铺开要多花 24 帧≈0.4 s，玩家跨区走动的观感可接受但边缘 patch 晚到），要么接受物理面与视觉面差 5–37 cm（LOD1–3）来换更快的铺窗，要么把近景做成不依赖 Flax Terrain 的自定义网格层（远景/中景本来就走 `PlanetRegionMesh` 那条路）。同一次运行里引擎报的最大帧时长是 100 ms，但我的单帧上传从不超过 14.4 ms，这条不能算在我头上（编辑器失焦时的节流，归因未做）。

**F. 顺带把"引擎垂直精度会不会顶穿 ≤1 cm 判据"量清楚了**：Flax 按**每块自己的跨度**存高度，实机看跨度 513 m 时偏差 2.06 cm、跨度几十米时 0.16 cm。离线普查（新增 `DemProbe span`，七处真实点位）：陆地上每块跨度 avg 36–48 m / worst 62–74 m（**来自我这套细节噪声的振幅，不是 GEBCO**——海面点位只有 1.9–2.2 m），按 `span/65536` 折算的隐含栅格 worst **0.113 cm**，`patches_over_1cm_step=0/25` 七处全过。**结论：量化不是风险，CollisionLOD 才是。**

**我读错的三处（都记下来，免得下次再当真）**：① b2 的 LOD 扫描按"跑一遍取一次"读数，被轮次顺序污染，LOD=1/2 的均值和 b4 差 2–4 倍；② b1/b2 把探针打在 `edge-1` 那一列上，得到"25 发 16 中"，我一度以为引擎边界行为可疑——其实是我的夹具撞上了共享末顶点；③ 判据字符串 `per_patch_avg=` 长度多数了一位，b2 跑到最后 `FormatException` 崩在**我自己的解析**上，verdict 因此是假的 FAIL（从那以后数值一律在生成处直接带出，不再回读自己格式化的字符串）。

**接线状态**：spike 是 `HundunWorld/Source/Game/Planet/` 下唯一的新文件，`verdict=PASS` 的那次跑的是 `planet_spike_b4`；之后我只改了两处**不影响测量**的东西（区域定位改调权威 `Geodetic.DirectionFromLatLon`——原先手抄的那份与它轴序不同，同一个 lat/lon 会落到别的地皮上；标记吃掉那条补了帧步进出口），tag 升到 `b5`、未重跑，重跑时以新回执为准。现场：空白场景文件与临时 actor 已清、标记 0、编辑器已退出、`World.scene` 的 mtime 与开工前一致（并行会话的文件没被碰）、离线 **202/202 绿**、三个 TEMP 前缀残留 0。

**真数据复跑**（收拢后同一台机器）：`dotnet run --project Tools/DemProbe --no-build -- pump Data/GEBCO_2026/derived 30 120` → 中心块 `crc=6311574D`，workers=1/2/4 三档与主线程同步基线**全部一致**，`PUMP PASS`；主线程单帧最长 0.062–0.072 ms（同步路径单块最长 1149.5 ms）。夹具临时目录按前缀数（`hundun_dem_*` / `hundun_patchcache_*` / `hundun_pump_probe_*`）本轮全为 0。

### 13.16 上传能不能离开主线程：单线程可以，并发会把地形静默写坏（同日第九轮之二）

§13.15 留了一条没证的口子：`SetupPatchHeightMap` 约 24 ms/块，如果它只能在主线程上跑，流送就只能"每帧一块"；如果能挪到后台线程，主线程可以完全不付这笔钱。发行版不带 `Terrain.cpp`，读不到线程守卫，只能实测。载体还是同一个 spike（[PlanetTerrainSpike.cs](../HundunWorld/Source/Game/Planet/PlanetTerrainSpike.cs)），标记文件多认三个 token：`thread` / `concurrent` / `live`。

**b6 单块 off-thread**：主线程先把同一份高度成功上传一次作**正对照**（`err 0.1357 cm`），再把同一份数据、同一套坐标交给一个工作线程——无异常、返回 false(成功)、`HasPatch=1`、事后射线偏差同为 `0.1357 cm`。

**b7/b8 workers 剂量曲线**（同一批 81 块 = 9×9，每遍之后逐块回验；判据看**数值**而不是看有没有抛异常，因为坏掉的正是数值）：

| workers | 高程偏差 ≥1 cm 的块数 | 最大偏差 | setup 报失败 | 抛异常 | 缺块 |
|---|---|---|---|---|---|
| 1 | **0/81** | 0.164 cm | 0 | 0 | 0 |
| 2 | 5/81 | 3 549 cm | 0 | 0 | 0 |
| 4 | 41/81 | 6 066 cm | 0 | 0 | 0 |
| 8 | 54/81 | 6 645 cm | 0 | 0 | 0 |
| 16 | 75/81 | 6 932 cm | 0 | 0 | 0 |
| 16（复查） | 66/81 | 6 938 cm | 0 | 0 | 0 |

单调变坏、可复现。**最要命的是失败的形式**：`setup_failures`、`throws`、`missing` 全程为 0，`patches_count` 也对——引擎既不断言也不报错，只是**把高度写成别的块的数值**（最大 69 m 级偏差）。任何只看"调用成功与否"的接线都会把它读成正常。所以这条不是"性能不够"，是**正确性**：并发上传地形这条路封死。

**b10 主线程照常跑帧时**（设计真正要的形态；b9 版把结论写空了，见下）：一条工作线程传 81 块（1 935.6 ms），主线程不 Join、照常 `OnUpdate`——期间主线程出帧 **33 次**，81 块全部事后验对（`bad_err=0/81`、`worst 0.164 cm`、零失败零缺块）。回执里 `live_verdict=SINGLE_WORKER_UPLOADS_CORRECTLY_WHILE_MAIN_THREAD_TICKS`。

**两条必须跟着这句的限定**：① 帧时长本身**不能报数**（samples min 6.4 / avg 26.3 / max 100 ms）——后台未聚焦的编辑器本身就在这个区间跑，我没有"同场景不上传"的对照，所以这组 ms 归因不了，只有"出帧次数"和"数据对不对"是有效读数；② 这是**实测没坏**，不是**引擎契约允许**——没有任何文档或断言说工作线程可以碰地形，b10 只覆盖了一种场景（无材质、单线程、81 块、编辑器 Development 装配）。要把它当成设计地基，需要一份引擎侧的正式约定，或者把上传仍然留在主线程、按 §13.15 E 的"每帧一块 + `CollisionLOD≥3`"来排。

**b9 那条假 PASS（记账）**：同一支探针的第一版报 `passed=1`，但 `frames=1`——上传窗口里主线程一次都没 tick，"帧时长 max=0"是**零样本上的最大值**，判据在空转。修法不是放宽阈值，而是**要求样本数**：`ticks_while_uploading` 必须 ≥8 才允许报结论，否则 `INCONCLUSIVE_main_thread_did_not_tick_enough_while_uploading`；同时把窗口从 25 块拉到 81 块。这条与"自检全绿可能只是那行从没执行"是同一个坑，只是这次样本数为 1 而不是 0，比空转更难看出来。

**现场**：临时场景 `Content/Maps/TmpTerrainSpike.scene` 与标记文件已删、编辑器已退出、`World.scene` 全程 mtime 未变（并行会话的文件没被碰）；编辑器顺手重写的 11 个插件 `.csproj`（Flax 重新生成工程文件时的 GUID 抖动）已 `git checkout` 还原，只保留 `HundunWorld/Source/Game.csproj` 里 Flax 自己写入的 `Horizon.Planet` 引用与 spike 的 `Compile Include`。

**桥的三条使用口径（踩过的坑，省下一轮试错）**：`scene_new`/`scene_load` **只认绝对路径**（相对路径报"未找到场景资产"，尽管工具描述写的是项目相对）；`ui_script_add` 认**类型简名**，全限定名反而找不到；**编辑器未聚焦时不会起脚本编译**，此时 `code_build_wait` 会 40 ms 返回假的 `success`（正解：看 `isSourceDirty=true` 而 `compilations` 不推进，或直接重启编辑器让它开机编译）；验证装配里有没有新代码要按 **UTF-16** 找字符串常量。

### 13.17 真实 GEBCO 第一次整链进引擎：上传保真 0.3 cm，磁盘缓存在 PIE 里省掉 209 倍

spike 多了一个 `dem` 模式（标记写 `30 120 dem <derived 绝对路径>`），走的是生产同一条链：`DemManifest.TryLoad` → `DemGrid` → `TerrainProfile` → `HybridHeightSource` → `HeightSampler` → `PlanetTerrainStreamer.FillPatch`（**接磁盘 patch 缓存**，落在 `Saved/planet_patch_cache`）→ `SetupPatchHeightMap`。这是"真实地球高程变成可玩地形"第一次真的发生在引擎里，此前所有引擎侧测量喂的都是解析斜面。

**点位对上了**：`dem_region=Region(f2 L14 3462,2730)`，与离线 `DemProbe` 同经纬度**同一个区域**——这条要靠 §13.15 之后那次"把经纬度→方向统一到 `Geodetic.DirectionFromLatLon`"才成立（在此之前 spike 手抄的那份是另一套轴序，同一个 lat/lon 会落到别的地皮上，两边数字看着都"自洽"却对不上）。

**量到的（30°N 120°E，东海—浙 coast 一带，5×5 窗口）**

| 项 | 值 |
|---|---|
| 每块高程（离线 CLI 同点位可比） | `min −41.788 m … max +100.992 m`（海域块 −41…−10 m，靠岸块 +25…+86 m） |
| 上传保真度（写进去的数组 vs 引擎里射线读回） | **worst 0.2959 cm**，25/25 块，零 setup 失败、零缺块 |
| 冷跑（盘上无缓存） | `fill_total=32 778.8 ms`（≈1.3 s/块，被 DEM 分片首次读盘主导）+ `upload_total=566.9 ms` |
| 热跑（同一批块再走一遍） | `fill_total=156.3 ms`，`cache_hits=25 misses=0` → **冷/热 ≈ 209×** |
| 跨 PIE 会话 | 第二次启动的"冷跑" `fill_total=159.9 ms`——**缓存活过一次进程**，写次数 25、字节 25 909 700（=25×1 036 388 精确对上 `EntryBytes`） |

**出图**：`game_screenshot` 拿到的画面有实体地表与一条地平线（约在画面 45% 处，上半均值亮度 51、下半 112）。看起来"平"是设计使然而非 bug：这块地皮的起伏是 ±40–100 m 摊在 5 km 上，而切平面内 2.5 km 的曲率下垂只有 ~0.5 cm；材质用的是项目里那张通用 `Content/Materials/Material.flax`（没有任何地形纹理）。数值与画面不矛盾——射线已经证明高度确实写进引擎了。
**第一次截图是全黑的**，原因不在内容：相机 `actor_create` 后我按直觉朝 +Z 摆，而这个 target 的 `DefineConstants` 里有 `REVERSE_Z`，等于背对地形。掉转（`yaw=180`）后就有画面了。**"全黑先查身份与朝向，别急着判内容坏了"**——这条与"零像素差先证资产身份"是同一类。

**§13.16 缺的那条对照补上了，但它把结论改了一半**：同一次编辑器会话、同一场景，`control` 模式（不起任何上传线程）测到帧时长 `min 6.775 / avg 16.917 / max 53.457 ms`；而 b10 那条"单 worker 上传 81 块期间"是 `min 6.386 / avg 26.277 / max 100 ms`。**所以"后台上传对主线程零成本"不成立**：主线程平均多付约 9.4 ms/帧（+55%）。但这条对照**没有对齐场景内容**——上传过程中场景里的 patch 从 0 涨到 81，渲染本身就在变重，所以这 9.4 ms 不能整笔算成锁竞争。要分清得再做一个"场景里先放好 81 块、只测不再上传的那几十帧"的匹配对照（已记进任务 14）。

**这一轮我自己的两个缺陷（都改了，也都记账）**：① spike 有个 `static bool _ranOnce`，于是**同一次编辑器会话里的第二次 PIE 永远不跑**——表现是"没有回执"，与"标记没放"完全同形；`OnStart` 每次 PIE 本来就只发一次，这个守卫是纯负债，删掉，触发只看标记文件。② `control` 探针报完结果不收手，每个后续帧再写一条，实测跑到 `ticks=133`（数字仍可用，但窗口不是我说的 40 帧）——改成报一次即停。

**现场**：临时场景与标记已删、编辑器已退出、`World.scene` mtime 全程未变（07:17:57）、插件 `.csproj` 的重新生成抖动已还原；`Saved/planet_patch_cache` 留了 25 MB 引擎侧 patch 缓存（`/HundunWorld/Saved` 在 `.gitignore` 里），下次跑可省掉那 33 s 冷读盘。

### 13.18 近景驱动层第一次真跑：预算闸门原来只装在主线程那条入口上

新增 [HundunWorld/Source/Game/Planet/PlanetNearFieldDriver.cs](../HundunWorld/Source/Game/Planet/PlanetNearFieldDriver.cs)：把 §13.12 定死的顺序在 PIE 里真跑——每帧 `BuildPlan` → 缺的块投给泵（线程池生成）→ `TryDequeueCompleted` → 每帧最多 `LoadsPerFrame` 块在主线程 `AddPatch`+`SetupPatchHeightMap` → `Release` 回池；出窗 `RemovePatch`+`MarkUnloaded`；跨区时按全局点重算局部坐标并 `pump.BeginRegion`+`streamer.AttachRegion`。策略参数全是公开字段，脚本桥能改，不用重编。标记 `Saved/planet_near_field.on`，内容 `"<lat> <lon> [loadsPerFrame] [collisionLod] [stepFrames] [maxFrames] [dem目录]"`。

**它第一件事就抓到一条产品级缺陷：缓存预算闸门只装在 `Tick` 上。** 驱动走的是异步路径（只调 `BuildPlan` 问计划、自己排帧上传），于是 900 帧里盘上攒了 **231 MB 且还在涨**——§7.1 那个 2 GB 上限对这条路径完全不设防。修法是把 `TrimToBudget` 从 `Tick` 挪进 `BuildPlan`（两条入口都经过它，没超预算时只比一个数就返回，每帧调得起），[PlanetTerrainStreamer.cs:234](../Horizon.Planet/PlanetTerrainStreamer.cs#L234)。配套判据 `BuildPlan_EnforcesCacheBudget_WithoutEverCallingTick`（正向对照同样只走 BuildPlan：默认预算内一块不逐；预算只够 10 块时 25 块必须收敛到 10 且盘上文件数同步落下，驻留集仍是 25——磁盘淘汰不动引擎侧）。**反证**：把闸门挪回 `Tick` 单独跑一次，红的恰好只有这条新判据（202 通过 / 1 失败），原有 `Tick_EnforcesCacheBudget…` 照绿——说明两条入口各自都有判据、没有互相顶包；还原后 **203/203 绿**。

**v1 那趟暴露的四件事**（都是"看起来在跑"才会露出来的）：① 上面那条预算；② 换区按 `TryNeighbour(UPlus)` 取邻居是**错的**——局部 +X 是这一面的"东"，而六个面的 uv 轴向各不相同，于是换区后玩家的局部坐标凭空多出一整个区域宽（`local_now` 每次 +8.1 km）；③ 验收撞在 `maxFrames` 那一帧，而那一帧正好刚换完区、整窗被摘光 → `desired=25 checked=0 missing=25`，看着像"地形没了"其实是取错了时刻；④ 对账等式在非静置时刻当然不平（`requested=237` vs `233`，差的是当时还在飞的 2 件）。

**v2 的改法与结果**：换区改成"拿全局点反查真正所在区域"（与 §13.9 走查同一口径）并用 `PlanetRegions.AreEdgeAdjacent` 校验它确实是邻家；收尾前先进入 `settling` 相，等泵排空、取件口空、计划满足再验。结果：

```
frames=935 steps=31 crossings=1 uploaded=190 unloaded=140 upload_failures=0 queue_overflow=0
final_window: desired=25 checked=25 missing=0 worst_ray_err_cm=0.1641
pump_equation requested=190 completed=190 stale=0 duplicate=0 pending_or_inflight=0 balanced=1
cache_bytes_used=196913720 budget=2147483648 over_budget=0
crossings_accepted=1 crossings_rejected=3
upload_ms avg=58.624 max=192.22 | driver_work_ms avg=12.036 | engine_frame_ms avg=34.354 max=100
plan_lag_frames avg=7.16 max=25 samples=190 | unsatisfied_frames=418/935 worst_ready_in_window=25
```

最后一窗 25/25 全部验到、偏差 0.164 cm，等式平了，缓存 197 MB 在预算内——**驱动骨架是成立的**。但两条没关掉：`crossings_rejected=3`（例 `from=Region(f2 L14 3461,2730) → to=Region(f2 L14 3459,2729)`，跨 2 格还带 1 格纵向偏移，而玩家每 6 步只走 3.05 km、L14 一格实测 3.666–7.777 km，物理上不可能跨 2 格——共边校验把它拦下来了，根因待查，任务 15）；上传耗时从 v1 的 23.6/49.8 ms 跳到 58.6/192 ms，可能是与后台生成的争用，也可能是这台机器的漂移（任务 16 要用重复测量 + 同进程尺子来分）。

**又一个自己的缺陷（v3 已改）**：`driver_verdict=DRIVER_PROBLEM` 而 `fail_reason=` 是**空的**——`ok` 里有八个合取分支，我只给五个写了名字。现在每个分支各自带数值报名（`crossings_rejected=3`、`uploaded_lt_desired=…`、`pump_equation_unbalanced` 等），verdict 为假而理由为空这种形状从此不可能出现。

**现场**：临时场景与标记已删、编辑器已退出、`RootScene.scene` 被 Flax 重新序列化过一次（只有行尾差异，内容逐字节相同，已 `git checkout` 还原）、`World.scene` mtime 全程 07:17:57 未变；离线 **203/203 绿**，客户端脚本 0 错误。驱动 v3 只改了回执报名，未重跑。


### 13.19 换区"非法跳格"的根因与标题相反：是我把服务端的不变量用在了客户端

§13.18 留下的 `crossings_rejected=3` 查清了，**坐标链没有错**。纯 C# 复现（新增 [Horizon.Planet.Tests/PlanetRegionWalkGeometryTests.cs](../Horizon.Planet.Tests/PlanetRegionWalkGeometryTests.cs)）给出的走查序列是：

```
start region=Region(f2 L14 3462,2730) width=5.258 km
check 1: local=(3.048 km) found=Region(f2 L14 3461,2730) delta=(-1,0)  edge_adjacent=True   -> 换基后 local=(-2.003,-1.458) km
check 3: local=(4.093 km) found=Region(f2 L14 3460,2729) delta=(-1,-1) edge_adjacent=False  <- 对角，但这是正确落点
check 5: local=(5.138 km) found=Region(f2 L14 3459,2729) delta=(-1,0)  edge_adjacent=True
```

根因一句话：**立方投影的 uv 格网与局部东西线不平行**，所以沿局部 +X 走会合法地同时对角换格（`y` 从 2730 漂到 2729 就是这件事）。`AreEdgeAdjacent` 是**服务端**的不变量（一次输入只能跨一格，§13.13），把它当成客户端换基的闸门，就会拒绝一个正确结果；而拒绝之后驱动留在旧基里继续走，才把"跨一格"累积成回执里那个"跨两格"——**假形状是真拒绝的下游产物**。v1 的 `local_now` 每次 +8.1 km 同源。

顺带量清了一个此前含糊的数：`DefaultRegionLevel=14` 的区域实测宽 **4.226–7.723 km**（中心到角 ≤5.471 km），而 5×5 patch 窗口只有 2.54 km——窗口确实落在一个区域之内，§13.7 的设计前提成立。

**驱动 v4 的改法**：换基改成无条件跟到 `RegionKey.FromDirection` 反查出的区域；该守的不变量换成"**一次检查最多跨一格**"（`reach>1` 才计错），并把形状分开计数。引擎侧实测：

```
crossings_total=3 edge=2 diagonal=1 multi_tile_rejected=0
local_now 全部落在 ±2.1 km 内（v1 是 +8.1 km）
final_window: desired=25 checked=25 missing=0 worst_ray_err_cm=0.1719
pump_equation requested=220 completed=220 pending_or_inflight=0 balanced=1
upload_ms avg=23.015 max=40.415 | plan_lag_frames avg=5.06 max=13
driver_verdict=NEAR_FIELD_DRIVER_RUNS_IN_ENGINE
```

**两条新判据各自被变异验过**：把走查里的纵向漂移抹掉（`localZ` 不再带走）→ `diagonal > 0` 红；把检查间隔从 6 步放大到 20 步（10.2 km > 最窄区域宽）→ `multiTile == 0` 红。两次都只红该红的那条。

**我自己的两处**：① 这条判据的第一版**也是错的**——它只带走 `localX`、丢掉 `localZ`，等于每换一次基就把玩家拽回新基的东西线，于是"对角换区"凭空消失、两条判据一起红；修的是夹具而不是断言。② v4 回执在 `verdict=PASS` 时仍印了一行 `fail_reason=(none_but_verdict_false)`（把报名从 `if (!ok)` 里挪出来时没管住），v5 改成"只在判据为假时报名"——PASS 也印 fail_reason 会让读回执的人以为"哪里不对但没说"。

**#16 的嫌疑排序因此变了**：v2 那趟 58.6/192 ms 的上传耗时，发生在"换区被误拒、玩家越走越远"的状态下；修好后同一策略回到 23.0/40.4 ms。所以那 2.5 倍更像**被误用的换区逻辑带来的额外流送压力**，而不是机器漂移或后台生成争用。仍需重复测量与匹配对照才能定论（任务 16）。

**现场**：离线 **205/205 绿**，编辑器已退出、临时场景与标记删除、`World.scene` mtime 未变。驱动 v5 只改了回执报名，未重跑。
