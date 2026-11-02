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

1. **5×5 Flax Terrain 流送 spike**（任务 3）：在 PIE 里实测 `AddPatch` / `SetupPatchHeightMap` / 碰撞烹饪耗时与内存，并验证 `heights[z*509+x]` 布局在引擎里真的对得上——现在只有源码级推断 + 单测定向检验（`EastFacingRamp_RisesAlongLocalX_NotAlongZ` / `SouthFacingRamp_RisesAlongLocalZ_NotAlongX`）。
   阻塞点：本机 `FlaxEditor.exe`（PID 29908）已在跑，属并行会话所有；再起第二个实例会损坏 AssetsCache，所以这一步要等编辑器空出来或经确认走现有编辑器的脚本桥。
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

取证脚本是 [Tools/_probe_pyramid_alignment.py](../Tools/_probe_pyramid_alignment.py)：它用 `Pyramid.__new__` 跳过 `__init__`（`__init__` 会 `"r+b"` 打开各级文件），只调真闸门，**不创建、不截断、不改写任何产物**——跑完 `pyramid/` 仍只有 `level_01..05.bin`（没有 `level_06`），`manifest.json` / `verify_receipt.json` / `level_05.bin` 的 mtime 仍是上一轮构建的 `07:41`/`07:29`，而探针跑在 `21:44`。想复认时直接 `python Tools/_probe_pyramid_alignment.py`。

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
