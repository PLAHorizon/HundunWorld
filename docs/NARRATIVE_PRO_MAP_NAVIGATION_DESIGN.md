# NarrativePro 地图导航 / 地图传送 / 地图瓦片生成 设计文档

**版本：** 1.0
**状态：** 设计阶段 — 已对照 Flax 1.12.6913 头文件与 C# 导出面（`FlaxEngine.CSharp.xml`）逐条双向核验
**参考实现：** UE5 版 NarrativePro（`C:\Works\GitHubProjects\20260801\HundunWorld\Plugins\NarrativePro`，模块 `NarrativeArsenal`）
**目标工程：** `HundunWorld\HundunWorld\Plugins\NarrativePro`（纯托管 C# 插件，`BuildNativeCode = false`）

---

## 1. 概述

### 1.1 目标

参照 UE5 版 NarrativePro 的 `Navigation/` 子系统（`NarrativeArsenal` 模块，共 18 个头文件 / 3552 行），补齐 Flax 版 `Plugins\NarrativePro\Source\NarrativePro\Navigation\` 中**仍是占位或假实现**的能力，聚焦三块：

| 领域 | 内容 |
|---|---|
| **地图瓦片生成** | 瓦片数据网格生成、多层级贴图切换、POI 连线可视化、编辑器一键离屏烘焙瓦片贴图 |
| **地图导航** | 世界↔地图坐标投影、面包屑路径、POI 图 A* 长距离寻路、路径存在性判定 |
| **地图传送** | 传送落点求解、快速旅行执行、传送事件（Tales）与到达判定 |

### 1.2 范围边界

- **本次做**：`Plugins\NarrativePro` 内部的实现补齐 + `Source\Game\UI` 三个地图界面的**最小数据源接入**（视觉不变）。
- **本次不做**：UE5 蓝图资产（`BP_MapTileActor`、`WBP_Navigator_Map_*`、`W_NarrativeMenu_FastTravel`）的移植；这些在 Flax 侧无对应物，改由 C# 组件 + 现有 Ink UI 承载。
- **不在本次范围**：覆盖 UE5 的 Mass/车辆/交通灯等其它模块。

---

## 2. 现状盘点

### 2.1 已移植且基本对齐

| 能力 | UE5 参考 | Flax 现状 |
|---|---|---|
| 瓦片数据模型 | `FMapTile{ TileImage, TileImages[], TileLocation }`、`FMapTileSet{ TileLayer, GridWidth, TileImageSize, Tiles[] }` | `MapTile` / `MapTileSet`（`MapTile.cs`）字段齐全 |
| POI 数据 | `FPOIData`（含 `POIFastTravelSpot` / `bSupportsFastTravel` / `bIsDiscoverable` / `LinkedPOIs`） | `POIData`（`MapTile.cs`）字段齐全 |
| 标记基类 | `UMapMarker`：设置覆盖、域、ZOrder、`GetMarkerSettings` | `MapMarker.cs` 基本对齐（405 行） |
| 导航器域标签 | `FNavigatorGameplayTags` | `NavigatorGameplayTags.cs` |
| 快速旅行事件点 | `OnFastTravelRequested` 委托 | `NarrativeNavigationComponent.OnFastTravelRequested` 事件在 |
| 传送后回调 | `ANarrativeCharacter::TeleportSucceeded` → `OnTeleported` | `NarrativeCharacter.OnTeleportSucceeded` / `OnTeleported` 在 |
| 子系统 POI 缓存 | `UNavigationSubsystem::POILookupMap` | `NavigationSubsystem`（Singleton 适配） |

### 2.2 缺口清单（本次要补）

| # | 能力 | UE5 参考行为 | Flax 现状 | 严重度 |
|---|---|---|---|---|
| G1 | **瓦片标记生成** | `UNarrativeNavigationComponent::BeginPlay` 遍历 `TileSet.Tiles`，逐个 `NewObject<UMapTileMarker>`，按当前层取 `TileImages[layer]`，`IconSize = TileSet.TileImageSize`，`MarkerTransform = FTransform(TileLocation)`，加入 `MapTiles` 并 `AddMarker` | **完全没有**。`NarrativeNavigationComponent.OnEnable` 仅注册自身，从不读 `TileSet` | 高 |
| G2 | **瓦片网格生成** | C++ 不参与（蓝图 `BP_MapTileActor` 生成 `TileSet.Tiles`）；`NumTiles` 字段仅声明 | `MapTileSet.GridWidth` / `TileImageSize` **从不被计算**；`NumTiles` 是死字段 | 高 |
| G3 | **分层切换** | `SetMapLayer` 遍历 `MapTiles`，按 `Tile.TileImages[NewLayer]` 换图标，无则回退 `TileImage`，随后 `RefreshMarker()`；`CurrentMapLayer` 参与存档 | `SetMapLayer` **只改 `TileSet.TileLayer` 就 return true** | 高 |
| G4 | **瓦片渲染组件** | `FMapTileRenderingSceneProxy`：绘制 POI 间 `LinkedPOIs` 连线（`ArrowLines`），`CalcBounds` 把 POI 位置并入包围盒 | 只画边界盒 + 网格；网格步长由 `bounds/gridWidth` 另算，**与 `TileImageSize` 不自洽** | 中 |
| G5 | **瓦片贴图产出** | 蓝图 `BP_MapTileActor`（编辑器侧拍摄/生成） | **无任何产出路径** | 高 |
| G6 | **POI 图 A\* 寻路** | `UAITask_LongMove::CalculatePath`：完整 A\*（`GScore`/`FScore`/`CameFrom`/`OpenSet` 堆），启发式为 2D 距离，`LinkedPOIs` 为邻接 | `AITask_LongMove.CalculatePath` **直接 `return false`** | 高 |
| G7 | **长距离移动执行** | `PerformMove` + `MoveFinished` 逐段推进 + `UpdateNavigablePOI` 定时器跳过已可直达 POI | `PerformMove` / `GetPOIPoints` / `UpdateNavigablePOI` / `RunLongMove` **全为空** | 高 |
| G8 | **路径存在性判定** | `UArsenalStatics::CheckPathExists`（NavMesh 查询） | **不存在** | 高 |
| G9 | **面包屑路径** | `FindPathAsync` → `FInterpCurveVector` → 按 `DistanceBetweenPoints` 重采样 → `DrawDashedLine` | `InitializeBreadcrumb` / `UpdateBreadcrumbNavPath` / `GeneratePath` / `DrawBreadcrumb` **4 个方法全是「Flax-不兼容」占位注释** | 高 |
| G10 | **传送落点求解** | `UArsenalStatics::BPFindTeleportSpot` → `UWorld::FindTeleportSpot` | **不存在** | 高 |
| G11 | **快速旅行执行** | 蓝图菜单 → `OnFastTravelRequested` → 游戏侧传送 | `RequestFastTravel` **只 `Invoke` 事件**，无传送逻辑；`POIActor.GetFastTravelTransform()` 写好了但**无人调用** | 高 |
| G12 | **Tales 传送事件** | 蓝图事件（传送到指定 Location） | `TeleportEvent.ExecuteEvent` **只 `Debug.Log`** | 中 |
| G13 | **前往地点任务判定** | 蓝图任务（距离判定） | `GoToLocationTask` **只有描述文本**，无到达判定 | 中 |
| G14 | **坐标平面约定** | UE Z-up，地图平面 XY，`FVector::Dist2D` 取 (X,Y) | Flax Y-up，地图平面应为 **XZ**；但 `NavigatorStatics.WorldToMapLocalPosition` 与 `MapMarker.GetMarkerMapLocalPosition` **用的是 (X,Y)** —— 与 `MapTileRenderingComponent` 的 XZ 网格绘制**自相矛盾** | 高（真实缺陷） |
| G15 | 最近 POI 距离 | `FVector::Dist2D` | 用 3D `Vector3.Distance` —— 高度差会污染最近点选择 | 低 |

### 2.3 关键错误结论（必须纠正）

现有代码中**至少 4 处注释断言「Flax 无 NavMesh / 无导航系统对应物」，这是事实错误**：

| 位置 | 错误注释 | 事实 |
|---|---|---|
| `MapMarker.cs:379,390,396` | 「Flax 无原生 NavMesh，面包屑简化为直线」 | Flax 1.12 自带 Recast/Detour 导航模块，有完整 `FlaxEngine.Navigation` 静态 API |
| `AITask_LongMove.cs:100,116,125,134` | 「Flax 无 NavMesh/AITask，保留占位」 | 导航 API 齐备；仅 AITask 基类确实无对应物（保留事件回调约定） |
| `MapMarkerQueryFilter.cs` | 「Flax 无 UNavigationQueryFilter 等价物」 | 属实（Flax 无查询过滤器概念），但需改为「用 `Navigation.TestPath` 做可通行性判定」的说明 |
| `POIRenderingComponent.cs` / `ArsenalStatics.TimeUntilCollision` | 部分占位 | 同上，可改用 `Navigation.*` |

---

## 3. Flax 引擎能力核验结论（双向验证）

核验方式：`C:\Program Files (x86)\Flax\Flax_1.12` 下 **引擎头文件**（`API_FUNCTION` / `API_PROPERTY` 宏）+ **C# 导出面**（`Binaries\Editor\Win64\Development\FlaxEngine.CSharp.xml`）交叉确认。两者均命中才判定为可用（遵循本项目「API 存在性双重验证」规则）。

### 3.1 导航（`Source\Engine\Navigation\` + `FlaxEngine.CSharp.xml`）

| API | 签名 | 用途 | 核验 |
|---|---|---|---|
| `Navigation.FindPath` | `bool FindPath(Vector3, Vector3, out Vector3[] resultPath)` | 面包屑路径、长距离逐段移动 | ✅ 头文件 `Navigation.h:32` / XML `38323` |
| `Navigation.TestPath` | `bool TestPath(Vector3, Vector3)` | **等价 `CheckPathExists`**（非分段路径连通性） | ✅ `Navigation.h:40` / XML `38332` |
| `Navigation.FindClosestPoint` | `bool FindClosestPoint(Vector3, out Vector3)` | **等价传送落点投影**（投影到导航网格） | ✅ `Navigation.h:48` / XML `38340` |
| `Navigation.RayCast` | `bool RayCast(Vector3, Vector3, out NavMeshHit)` | 导航网格贴地射线 | ✅ `Navigation.h:85` / XML `38373` |
| `Navigation.FindDistanceToWall` | `bool FindDistanceToWall(Vector3, out NavMeshHit, float)` | 落点离墙距离校验（避免卡墙） | ✅ `Navigation.h:23` / XML `38314` |
| `Navigation.FindRandomPointAroundCircle` | `bool FindRandomPointAroundCircle(Vector3, float, out Vector3)` | 落点求解失败时半径内重采样 | ✅ `Navigation.h:76` / XML `38364` |
| `Navigation.BuildNavMesh` | `void BuildNavMesh(Scene, float)` / `(BoundingBox, Scene, float)` | 按脏区域重建导航网格 | ✅ `Navigation.h:104,113` / XML `38382..38407` |
| `Navigation.IsBuildingNavMesh` / `NavMeshBuildingProgress` | `bool` / `float` | 就绪判定与降级 | ✅ `Navigation.h:91,96` / XML `38304,38309` |
| `NavMeshHit` 结构 | `{ Vector3 Position; float Distance; Vector3 Normal; }` | 命中结果 | ✅ `NavigationTypes.h:139` / XML |
| `NavMeshBoundsVolume` / `NavMesh` Actor | `API_CLASS` | 场景侧导航网格定义 | ✅ `NavMeshBoundsVolume.h:11` / `NavMesh.h:17` |

### 3.2 物理（用于传送落点碰撞校验）

| API | 用途 | 核验 |
|---|---|---|
| `Physics.RayCast(origin, dir, out RayCastHit, maxDist, layerMask, hitTriggers)` | 单射线 | ✅ `Physics.h:151` / XML `44738` |
| `Physics.LineCast(start, end, out RayCastHit, ...)` | 线段（视线） | ✅ `Physics.h:117` / XML `44705` |
| `Physics.SphereCast(center, radius, dir, out RayCastHit, ...)` | 胶囊/球形扫掠（落点净空校验） | ✅ `Physics.h:229` / XML `44815` |
| `Physics.BoxCast` / `CapsuleCast` | 同上，盒/胶囊几何 | ✅ `Physics.h:190,271` |

### 3.3 离屏渲染与截图（用于瓦片贴图烘焙）

| API | 说明 | 核验 |
|---|---|---|
| `SceneRenderTask { GPUTexture Output; ScriptingObjectReference<Camera> Camera; RenderView View; ActorsSources ActorsSource; CustomActors; }` | 独立渲染任务 | ✅ `RenderTask.h:216..283` / XML `30074..` |
| `Camera.UsePerspective` (bool, 默认 true) | **关掉即为正交投影** | ✅ `Camera.h:76` / XML `35463` |
| `Camera.OrthographicSize` / `OrthographicScale` | 正交视口尺寸 | ✅ `Camera.h:126..142` / XML `35488,35493` |
| **`Screenshot.Capture(SceneRenderTask, string path)`** | **直接把渲染任务后缓冲存成图片（免自写编码器）** | ✅ XML `53159` |
| `Screenshot.Capture(GPUTexture, string path)` | 直接存 GPUTexture | ✅ XML `53150` |
| `GPUTexture.DownloadData(TextureData)` / `TextureData.GetPixels(Color32[])` | 需要 CPU 像素时使用 | ✅ XML `31473` / `32110` |
| `GPUTextureDescription.New2D(w, h, PixelFormat, GPUTextureFlags, mipCount)` | 纹理描述 | ✅ XML `31708` |
| `GPUTextureBrush(GPUTexture)` | 把渲染结果画进 UI | ✅ XML |

> **已有实测先例**：本工程 `Source\Game\UI\Ink\Components\CharacterPreview3D.cs` 已用「`GPUTexture` + 离屏 `Camera` + `SceneRenderTask`」实现角色 3D 预览，瓦片烘焙复用同一套已验证路径。

### 3.4 编辑器扩展与资产导入

| API | 说明 | 核验 |
|---|---|---|
| `EditorPlugin` + `InitializeEditor()` / `DeinitializeEditor()` | 编辑器插件生命周期 | ✅ 本工程先例 `Plugins\HundunAgent\Source\HundunAgentEditor\HundunAgentPlugin.cs`、`Source\Game\Editor\AutoSkinnedModelImportPlugin.cs` |
| `editor.ContentImporting.Import(sourcePath, ContentFolder folder, bool)` → `ContentImporting.IsImporting` | **把磁盘 PNG 导入为 Content 资产** | ✅ 本工程实测用法 `Plugins\HundunAgent\...\MaterialAssetTools.cs:216,221` |
| `editor.ContentDatabase.Find(path/guid)` / `.Rebuild(true)` | 资产项查找与刷新 | ✅ 同上 `MaterialAssetTools.cs:207..230` |
| `Content.Load<Texture>(path)` | 运行时加载贴图 | ✅ 本工程多处（`InventoryUI.cs:526` 等） |

---

## 4. 架构设计

```
┌──────────────────────────────────────────────────────────────────────┐
│  编辑器层 (NarrativeProEditor)                                        │
│  ┌────────────────────────────────────────────────────────────────┐  │
│  │ MapTileBaker（EditorPlugin 命令）                               │  │
│  │  正交 Camera → GPUTexture → SceneRenderTask ──┐                 │  │
│  │  Screenshot.Capture(ST, png) ←────────────────┘                 │  │
│  │  ContentImporting.Import(png) → Texture 资产                    │  │
│  │  回填 TileSet.Tiles[].TileImages[layer]                         │  │
│  └────────────────────────────────────────────────────────────────┘  │
└──────────────────────────────┬───────────────────────────────────────┘
                               │ 写入
┌──────────────────────────────▼───────────────────────────────────────┐
│  运行时层 (NarrativePro.Navigation)                                   │
│                                                                       │
│  ┌─── 瓦片 ───────────────────────────────────────────────────────┐  │
│  │ MapTileBoundsActor.RegenerateTiles()  ← G2                     │  │
│  │   MapWidth / NumTiles → GridWidth / TileImageSize / TileLocation│  │
│  │                                                               │  │
│  │ NarrativeNavigationComponent                                    │  │
│  │   OnEnable → 按 TileSet 生成 MapTileMarker + POIMapMarker  ← G1 │  │
│  │   SetMapLayer() → 按层换图标 + RefreshMarker              ← G3 │  │
│  │ MapTileRenderingComponent → 网格 + POI LinkedPOIs 连线     ← G4 │  │
│  └───────────────────────────────────────────────────────────────┘  │
│                                                                       │
│  ┌─── 导航 ───────────────────────────────────────────────────────┐  │
│  │ NavigatorStatics.WorldToMap / MapToWorld  (XZ 平面)        ← G14│  │
│  │ MapMarker 面包屑: Navigation.FindPath → 重采样 → 虚线     ← G9 │  │
│  │ AITask_LongMove: A*(POI图) + 逐段移动 + 可导航POI更新      ← G6,7│  │
│  │ ArsenalStatics.CheckPathExists → Navigation.TestPath        ← G8 │  │
│  └───────────────────────────────────────────────────────────────┘  │
│                                                                       │
│  ┌─── 传送 ───────────────────────────────────────────────────────┐  │
│  │ ArsenalStatics.FindTeleportSpot                              ← G10│  │
│  │   Navigation.FindClosestPoint + Physics.SphereCast 净空校验     │  │
│  │   + Navigation.FindDistanceToWall + FindRandomPointAroundCircle │  │
│  │ NarrativeNavigationComponent.RequestFastTravel() 真执行     ← G11│  │
│  │ TeleportEvent 真传送 / GoToLocationTask 到达判定      ← G12,13 │  │
│  └───────────────────────────────────────────────────────────────┘  │
└───────────────────────────────┬──────────────────────────────────────┘
                                │ 数据源接入（视觉不变）
┌───────────────────────────────▼──────────────────────────────────────┐
│  游戏侧 UI（Source\Game\UI\Ink）                                      │
│  WorldMapPage / InkMinimap / CompassPage                              │
│  → 优先读 NavigationSubsystem（POI/标记/航点）；无数据时回退现有假数据  │
└──────────────────────────────────────────────────────────────────────┘
```

### 4.1 地图瓦片生成

#### 4.1.1 坐标平面约定（先修正 G14）

Flax 为 **Y-up、左手系**，地面平面是 **XZ**。UE 为 Z-up，地图平面 XY。移植时必须统一：

- 世界 → 地图：`u = (world.X - origin.X) * scale`，`v = (world.Z - origin.Z) * scale`
- 地图 → 世界：`world.X = u * invScale + origin.X`，`world.Z = v * invScale + origin.Z`，`world.Y` 由射线/导航网格回填
- `MapTileBoundsActor` 的包围盒在 XZ 上的边长 = `MapWidth`；高度（Y）仅取 1 用于可视化
- `MapTileRenderingComponent` 的网格绘制已在 XZ 上（保持），但其步长改为 `MapWidth / GridWidth`

#### 4.1.2 瓦片数据网格生成（`MapTileBoundsActor.RegenerateTiles()`）

由 `MapWidth`（世界宽度）+ `NumTiles`（每边瓦片数）+ Actor 位置（地图中心）推导：

```
GridWidth      = NumTiles
TileWorldSize  = MapWidth / NumTiles          // 单块世界边长
TileImageSize  = TilePixelSize                // 单块贴图分辨率（新增参数，默认 1024）
TileCount      = NumTiles * NumTiles
TileLocation[i]= 中心 + (col - (GridWidth-1)/2) * TileWorldSize  在 X
                         (row - (GridWidth-1)/2) * TileWorldSize  在 Z
```

生成后重建 `TileSet.Tiles`，保留已有 `TileImages`（按索引匹配，避免重烘焙丢失贴图）。

#### 4.1.3 瓦片标记生成（G1）与分层（G3）

`NarrativeNavigationComponent.OnEnable` 增补 `BuildNavigationFromMapBounds()`：

1. 查找场景中 `MapTileBoundsActor`（无则沿用现有自动查找逻辑）；同步 `MapWidth` / `MapOrigin`。
2. 遍历 `TileSet.Tiles` → 建 `MapTileMarker`：
   - `DefaultMarkerSettings.LocationIconPath` = 当前层 `TileImages[CurrentMapLayer]`，缺失回退 `TileImagePath`
   - `IconSize = (TileImageSize, TileImageSize)`，`ZOrder = 0`（图标 ZOrder ≥ 1 才能盖在瓦片上）
   - `MarkerTransform = Transform(Translation: TileLocation)`
   - 加入 `MapTiles` 并 `AddMarker`
3. 遍历 `MapTileBounds.POIs` → 建 `POIMapMarker`（`bNeedsMapMarker` 为真时），填充 `POILookupMap` / `POIMarkers`。

`SetMapLayer(newLayer, domains)`：记录 `CurrentMapLayer` → 遍历 `MapTiles`，按 `Tile.TileImages[newLayer]` 换图标（无则回退 `TileImagePath`）→ `RefreshMarker()`。`domains` 参数保留（UE 中亦为 TODO），文档标注为未实现的多域差异。

`SetMapLayer` 需在 `Load()` 末尾回调一次以恢复存档层级（对齐 UE `Load_Implementation` 的行为）。

#### 4.1.4 瓦片渲染组件（G4）

重写 `MapTileRenderingComponent`：

- **包围盒**：`MapWidth` 的正方形（XZ），中心为 Actor 位置；Y 方向厚度取 1。
- **网格**：`GridWidth` 条分割线，步长 `MapWidth / GridWidth`（与 `TileImageSize` 分离：前者是世界尺寸，后者是像素尺寸）。
- **POI 连线**：遍历 `MapTileBounds.POIs`，对每个 `LinkedPOIs` 找同名 POI，`DebugDraw.DrawLine` 连线（对应 UE `ArrowLines` 的 `FColor::Red`）。
- 保留 `OnDebugDraw` 作为 Flax 侧可视化入口（UE 的 `FDebugRenderSceneProxy` 在 Flax 无对应，`DebugDraw` 是等价路径）。

#### 4.1.5 编辑器一键离屏渲染烘焙（G5）

新增 `NarrativeProEditor/Map/MapTileBaker.cs`（`EditorPlugin` 命令 + 可编程 API）：

```
对每一块瓦片 tile、每一个待烘焙层 layer：
  1. 建临时 EmptyActor 承载 Camera：UsePerspective = false
     OrthographicSize 由 tile 世界尺寸标定（见「风险 R1」）
     位置 = (tile.TileLocation.X, MapHeight, tile.TileLocation.Z)，俯视旋转
  2. GPUTexture(desc = New2D(TilePixelSize, TilePixelSize, PixelFormat.R8G8B8A8_UNorm, ...))
  3. SceneRenderTask { Output = tex, Camera = cam, Enabled = true }
  4. 等待渲染（至少 1~2 帧；OrthographicSize 变化后需 `CameraCut()`）
  5. Screenshot.Capture(renderTask, pngPath)  →  等待文件生成
  6. 层/瓦片全部完成后：ContentImporting.Import(pngPath, folder, true) → 等待 !IsImporting
  7. ContentDatabase.Rebuild(true) → 回填 TileImages[layer] = 资产路径
  8. 清理临时 Actor / RenderTask / GPUTexture
```

- 落盘目录默认 `Content\Maps\<MapName>\Layer_<Layer>_T{index}.png`，可配置。
- 需在主线程逐帧推进（避免一次性阻塞），并提供进度回调。
- **不去改已存在资产的身份**：只导入新生成的 PNG（遵循 `AutoSkinnedModelImportPlugin` 的教训——不擅自改变已有资产 GUID）。

### 4.2 地图导航

#### 4.2.1 路径存在性（G8）

```csharp
// ArsenalStatics
public static bool CheckPathExists(Vector3 start, Vector3 end, Actor agent = null, float maxDistance = 0f)
{
    if (maxDistance > 0f && Vector3.Distance(start, end) > maxDistance) return false;
    if (Navigation.IsBuildingNavMesh) return true;   // 未就绪时不误判为「不可达」
    return Navigation.TestPath(start, end);
}
```
> `Navigation.IsBuildingNavMesh == true` 时返回 `true` 是**刻意的降级**：导航网格构建中，`TestPath` 会返回 false，若直接采信会导致所有远程移动判定为失败。

#### 4.2.2 面包屑路径（G9）

| 方法 | 实现 |
|---|---|
| `InitializeBreadcrumb(navActor)` | 记录 `OwnerNavActor`；按 `UpdateNavPathRate` 注册限频刷新（Flax 无 `FTimerManager`，用 `Scripting.InvokeOnUpdate` 轮询 + 时间累加，或由导航组件驱动的 tick 循环） |
| `UpdateBreadcrumbNavPath()` | ① `bDrawBreadcrumbs` / `OwnerNavActor` / `bWantsOnPaint` 三重前置判断；② 与 `CachedLocation` 距离 < `UpdateNavDistanceThreshhold` 则跳过；③ `Navigation.FindPath(player, target, out corners)`；④ 把拐点按弧长参数化，以 `DistanceBetweenPoints` 等距重采样成 `NavPath`（等价 UE 的 `FInterpCurveVector` + `Eval`） |
| `GeneratePath(...)` | 由重采样点填充 `NavPath` |
| `DrawBreadcrumb(onPaintData)` | 世界点 → 地图绘制空间 → 虚线绘制（`BreadcrumbColor` / `BreadcrumbThickness` / `BreadcrumbDashLength`） |
| `CleanupBreadcrumb()` | 注销刷新回调、清空缓存 |

**性能约束**：`Navigation.FindPath` 是**同步**调用（无 UE 的 `FindPathAsync`），因此必须限频（默认 4 次/秒）+ 距离阈值门控，否则多标记场景会卡主线程。

#### 4.2.3 POI 图 A* 与长距离移动（G6/G7）

`AITask_LongMove` 保持「无 AITask 基类 ⇒ 普通类 + 事件回调」的既有移植约定（这是唯一确实无对应物的部分）。

- `CalculatePath(startingPOI, endingPOI, out path)`：标准 A\*
  - 启发式 `h(n) = Dist2D(n.POILocation, ending.POILocation)`（XZ 平面）
  - `LinkedPOIs` 作为邻接表，节点查 `NavigationSubsystem.POILookupMap`
  - `OpenSet` 用最小堆（`List` + 上浮/下沉，或按 `FScore` 排序取首）
  - 到达 `endingPOI.POITag` 时沿 `CameFrom` 回溯并反转
- `GetPOIPoints(out start, out end)`：遍历 `MapTileBounds.POIs`，分别取距 agent 最近、距 `FinalGoal` 最近者
- `PerformMove(goal)`：`Navigation.FindPath(currentPos, goal, out corners)` 得到拐点，交给 NPC 移动（对接 `NarrativePathFollowingComp` / `NarrativeCharacterMovement`）；无路径则 `FailLongMove()`
- `UpdateNavigablePOI()`：按 `UpdatePOIRate` 轮询，用 `Navigation.TestPath` 检测是否可直达路径更后段的 POI，能直达则把 `CurrentTargetIndex` 前移并重发移动
- `RunLongMove(...)`：静态入口，构造实例并 `Setup`

#### 4.2.4 最近 POI（G15）

`GetNearestPOIToPoint` 改为 XZ 平面距离（忽略 Y 高度差），与 UE `Dist2D` 对齐。

### 4.3 地图传送

#### 4.3.1 传送落点求解（G10）

```csharp
// ArsenalStatics：等价 UE BPFindTeleportSpot
public static bool FindTeleportSpot(Actor actor, ref Vector3 location, ref Quaternion rotation)
{
    // 1) 投影到导航网格
    if (!Navigation.FindClosestPoint(location, out var projected)) return false;

    // 2) 净空校验：胶囊向上/向下扫掠确认无阻挡
    if (IsSpotClear(actor, projected)) { location = projected; return true; }

    // 3) 半径内重采样（等价 UE UWorld::FindTeleportSpot 的多点尝试）
    for (int i = 0; i < MaxSpotSearchAttempts; i++) {
        if (!Navigation.FindRandomPointAroundCircle(location, SpotSearchRadius, out var candidate)) break;
        if (IsSpotClear(actor, candidate)) { location = candidate; return true; }
    }
    return false;
}
```
`IsSpotClear` 用 `Physics.SphereCast`（半径取 `CharacterController.Radius`，向上 `CapsuleHalfHeight`）确认净空；可选加 `Navigation.FindDistanceToWall` 避免落点贴墙。

`rotation` 优先沿用 POI 的朝向，其次朝向地图中心。

#### 4.3.2 快速旅行执行（G11）

`NarrativeNavigationComponent.RequestFastTravel(POIData poi)` 真实现：

1. 校验：`poi != null`、`poi.bSupportsFastTravel`、可发现 POI 必须已发现（`HasDiscoveredPOI`）、非冷却中。
2. 解析落点（优先级）：`poi.POIFastTravelSpot` 有效 → `POIActor.GetFastTravelTransform()` → `FindTeleportSpot` 求解。
3. 解析目标 pawn：本地玩家的 `NarrativeCharacter`。
4. **执行传送**：设置目标 `Actor.Position` / `Actor.Orientation`；同步 `CharacterController`（若与控制器的位置缓存耦合，需一并写入，避免回弹——参考本项目既有「位置回弹」类缺陷经验）。
5. 触发 `NarrativeCharacter.OnTeleportSucceeded(false)` → 广播 `OnTeleported`。
6. 广播 `OnFastTravelRequested(poi)`（保持 UE 语义，供游戏侧接管加载画面/淡入淡出）。

失败路径：任一步失败则记录警告并**不**广播 `OnFastTravelRequested`，避免游戏侧进入传送流程却未实际移动。

#### 4.3.3 Tales 事件与任务（G12/G13）

- `TeleportEvent`：`TargetLocationName` 优先按 POI 标签 / `POIActor` 名解析，解析失败时回退 `(X,Y,Z)`；传送目标为 `target`（pawn）或 `controller` 的受控 pawn；传送后同样走 `OnTeleportSucceeded`。
- `GoToLocationTask`：覆写 `TickTask()`（基类已有 `TickInterval` / `TickTask` 机制），玩家到 `TargetLocation` 的 XZ 距离 ≤ `AcceptanceRadius` 时 `AddProgress(1)` 完成；`LocationName` 为空且有 `OwningPawn` 时，可用 `Navigation.FindClosestPoint` 把目标吸附到导航网格上，避免目标点不可达导致任务永不完成。

---

## 5. 游戏侧最小接入（视觉不变）

| 界面 | 现状 | 接入方式 |
|---|---|---|
| `WorldMapPage.cs`（1051 行） | 硬编码 `MData` 标记（归一化 0~1 坐标）+ 假传送点 | 标记来源改为 `NavigationSubsystem`（POI）∪ 导航组件的自定义航点；`Mk.Teleport` 对应 `POI.bSupportsFastTravel`；点击传送点 → `RequestFastTravel`。世界坐标经 `NavigatorStatics.WorldToMapLocal` 转归一化 |
| `InkMinimap.cs`（350 行） | 独立绘制 | 玩家与标记点来自导航组件；瓦片底图（若已烘焙）用 `Screenshot` 产出的 Texture 资产 |
| `CompassPage.cs`（606 行） | 独立绘制 | 标记按 `MarkerDomain` 含 `NavigatorTypes_Compass` 过滤；用 `MarkerStartFadeInDistance` / `MarkerStartFadeOutDistance` 做距离淡入淡出 |

**回退要求**：三者均需在「导航子系统无数据 / 无 `MapTileBoundsActor`」时**回退到现有假数据**，保证当前观感与既有 UI 验证结论（见 `docs/UI-DESIGN-*`）不被破坏。

---

## 6. 相位划分

| 相位 | 内容 | 风险 | 依赖 |
|---|---|---|---|
| **P0** | 本文档 | — | — |
| **P1** | 瓦片数据网格 + 分层 + 标记生成 + 组件重写 + XZ 坐标修正 | 低（纯数据） | — |
| **P2** | 导航：`CheckPathExists` + 面包屑 + POI 图 A\* + 长距离移动 | 中（需导航网格就绪） | — |
| **P3** | 传送：落点求解 + 快速旅行执行 + `TeleportEvent` + `GoToLocationTask` | 中（需与角色移动/物理同步） | 依赖 P2 的 `Navigation.*` 封装 |
| **P4** | 编辑器瓦片烘焙（正交相机 + `SceneRenderTask` + `Screenshot.Capture` + 资产导入） | 中高（正交尺寸标定、异步落盘时序） | 依赖 P1 的瓦片数据结构 |
| **P5** | 游戏侧最小接入 + 编译验证 + 文档回填 | 低 | P1~P4 |

---

## 7. 风险与待实测项

| # | 风险 | 应对 |
|---|---|---|
| **R1** | `Camera.OrthographicSize` 语义（半高 or 全高）在 Flax 文档中不明确，直接决定瓦片拼接是否错位 | **以实测定标**：烘焙单块后检查世界标志物在图中的像素跨度，反推系数；本项目已有「以实测定标」先例（Skinned 模型轴向扶正） |
| **R2** | `Screenshot.Capture(SceneRenderTask, path)` 为异步落盘（「may be taken one or more frames later」） | 落盘后 `File.Exists` 轮询 + 超时（复用 `ViewportEnvTools` 的 `WaitUntilAsync` 模式） |
| **R3** | 导航网格未生成时 `Navigation.TestPath` / `FindPath` / `FindClosestPoint` 全部返回 false，会误判为「不可达 / 无落点」 | 用 `Navigation.IsBuildingNavMesh` + 场景是否存在 `NavMeshBoundsVolume` 做就绪判定，未就绪时**降级**（寻路回退 POI 图/直线，落点回退 POI 原始变换），并输出一次性日志 |
| **R4** | `Navigation.FindPath` 同步调用，多标记面包屑可能拖慢主线程 | 限频（`UpdateNavPathRate`）+ 距离阈值门控 + 仅对 `bWantsOnPaint` 的标记计算 |
| **R5** | 逐瓦片渲染需跨帧推进，若在单次调用内循环会阻塞编辑器 | 烘焙器改为分帧任务（每帧 1 块），提供进度与取消 |
| **R6** | 传送后位置回弹（本项目历史缺陷类型：控制器/服务端位置缓存覆盖客户端位置） | 传送时同步写入 `CharacterController` 与网络同步缓存；接入 `OnTeleported` 供上层刷新 |
| **R7** | 游戏侧 UI 接入破坏既有视觉验证 | 严格「有数据用数据、无数据回退假数据」；接入后按 `docs/UI-DESIGN-LANDING-CHECKLIST-20260923.md` 复核 |

---

## 8. 与 UE5 参考的刻意差异

| 差异 | 原因 |
|---|---|
| 瓦片生成由 C# `RegenerateTiles()` 承担（UE 在蓝图 `BP_MapTileActor`） | Flax 纯托管插件无法提供蓝图节点，且 `NumTiles` 字段本就需要消费方 |
| 瓦片贴图改由编辑器 `MapTileBaker` 离屏烘焙 | Flax 无 SceneCapture2D 等价 Actor，改用 `Camera(正交) + SceneRenderTask + Screenshot.Capture`（本项目已验证路径） |
| 面包屑/寻路改直接调用 `FlaxEngine.Navigation` | Flax 导航 API 为静态函数，无 `ANavigationData` / `FPathFindingQuery` 抽象 |
| `MapMarkerQueryFilter` 保留为说明性扩展点 | Flax 无查询过滤器（区域成本、搜索上限）概念，仅能通过限频与距离阈值近似 |
| 无 `AITask` 基类 | Flax 无行为树/AITask 系统；`AITask_LongMove` 保持普通类 + 事件回调 |
| 瓦片合成使用 XZ 平面 | Flax 为 Y-up 左手系 |

---

## 9. 实现状态（P1~P5 已落地）

编译验证：`Flax.Build -build -buildtargets=GameEditorTarget -configuration=Development -platform=Windows -arch=x64`，**0 error**（构建命令与工程既有用法一致）。

### 9.1 交付清单

| 相位 | 文件 | 内容 |
|---|---|---|
| **P1** | `Navigation/MapTileBounds.cs` | 新增 `TilePixelSize` / `TilesPerSide` / `TileWorldSize` / `TileCount`；`RegenerateTiles()` 按 `MapWidth`/`NumTiles` 生成网格与 `TileLocation`（按位置就近保留已烘焙贴图）；`GetTileWorldLocation` / `WorldToTileCoords` / `GetTileAtWorldPosition` / `GetWorldBounds`；`MapTileLayerImage` + `MapTile.GetImagePathForLayer` / `SetImagePathForLayer`（多层级）；碰撞体由 `Actor.GetChild<BoxCollider>()` 获取并同步尺寸 |
| **P1** | `Navigation/NarrativeNavigationComponent.cs` | `TryBuildNavigation()`（按帧重试，解决启用顺序不确定）；`BuildTileMarkers()` / `BuildPOIMarkers()`（= UE `BeginPlay` 的瓦片与 POI 构建）；`SetMapLayer()` 真实现（按层换图标 + `RefreshMarker`）；`CurrentMapLayer` 参与存档；`GetNearestPOIToPoint` 改 XZ 平面距离 |
| **P1** | `Navigation/MapTile.cs` | `MapTileMarker` 增 `TileIndex` / `ApplyLayerImage` / `ApplyTileImageSize`，`ZOrder = 0` |
| **P1** | `Navigation/MapTileRenderingComponent.cs` | 网格步长改 `MapWidth / GridWidth`（与像素分辨率解耦）；新增 POI `LinkedPOIs` 连线绘制；`DebugDraw.DrawWireBox` 替代手写 12 边 |
| **P1** | `Navigation/NavigatorStatics.cs`、`MapMarker.cs` | **修正坐标平面缺陷 G14**：全部投影改 XZ |
| **P2** | `Navigation/NavigatorStatics.cs` | 导航查询封装：`IsNavigationReady` / `TestPath` / `TryFindPath` / `TryFindClosestPoint` / `TryFindRandomPointAroundCircle` / `TryFindDistanceToWall` / `ResamplePath`（等弧长重采样，带上限） |
| **P2** | `Navigation/MapMarker.cs` | 面包屑真实现：`Tick`（限频驱动）/ `InitializeBreadcrumb` / `UpdateBreadcrumbNavPath`（`Navigation.FindPath` + 距离门控）/ `GeneratePath` / `GetBreadcrumbPath` / `CleanupBreadcrumb`；`SetDrawMarkerPathEnabled` 语义修正为 UE 的 `bWantsOnPaint` |
| **P2** | `AI/AITask_LongMove.cs` | 改为普通类（对齐 `MoveToLocationAndRotationTask` 约定）；完整 POI 图 A\*（最小堆 + `GScore`/`FScore`/`CameFrom`）；`GetPOIPoints` / `PerformMove` / `UpdateNavigablePOI` / `RunLongMove` / `TickTask`；可插拔 `ILongMoveExecutor`（未提供时内置直线推进） |
| **P2** | `Core/ArsenalStatics.cs` | `CheckPathExists`（`Navigation.TestPath`，导航未就绪时按「未知」放行） |
| **P3** | `Core/ArsenalStatics.cs` | `FindTeleportSpot`（导航投影 → 球体净空校验 → 半径内重采样 → 回退投影点并告警）；`IsTeleportSpotClear`；`FindCharacterController` / `ResolveAgentSize`；`TeleportCharacter`；`ResolveCharacterFromContext` |
| **P3** | `Navigation/NarrativeNavigationComponent.cs` | `RequestFastTravel` 真实现（校验 → 落点求解 → 传送 → `OnTeleported` → 广播事件）；`ResolveFastTravelTransform` / `ResolveLocalCharacter` / `FindPOIActor` |
| **P3** | `UnrealFramework/NarrativeCharacter.cs`、`NarrativePlayerController.cs` | `OnTeleportSucceeded` 提升为 public（对齐 UE）；`FastTravelToPOI` 接上导航组件 |
| **P3** | `Tales/Events/Builtin/TeleportEvent.cs`、`Tales/Tasks/BuiltinTasks/GoToLocationTask.cs` | 事件真传送（POI 标签或显式坐标）；任务到达判定（XZ 距离 + 目标吸附导航网格） |
| **P4** | `NarrativeProEditor/Navigation/MapTileBaker.cs` | 分帧状态机：正交相机（`UsePerspective=false`）→ `GPUTexture` → `SceneRenderTask` → `Screenshot.Capture` 落 PNG → `ContentImporting.Import` → 回填 `TileImages[layer]`；含超时、取消与资源清理 |
| **P4** | `NarrativeProEditor/Navigation/NarrativeProNavigationEditorPlugin.cs` | `EditorPlugin`，Tools 菜单两项：重新生成瓦片网格 / 烘焙瓦片贴图；`Editor.EditorUpdate` 逐帧驱动 |
| **P5** | `Source/Game/World/PluginNavigationBridge.cs` | 插件导航 → UI 只读视图（标记 / 玩家位姿 / 快速旅行），不可用时返回 false |
| **P5** | `Source/Game/UI/Ink/Pages/Map/WorldMapPage.cs` | 标记与地名改为桥接数据（不可用回退内置演示）；点击传送点触发快速旅行；地名标签上限 6 防堆叠 |
| **P5** | `Source/Game/UI/Ink/Components/InkMinimap.cs` | 新增 `RefreshFromNavigation()`（玩家为圆心，`NavigationRange` 归一化），节流 0.5s 自驱；失败不清空既有数据 |
| **P5** | `Source/Game/UI/Ink/Pages/Map/CompassPage.cs` | POI 改由导航驱动（正北 -Z 为 0° 方位角），节流刷新；不可用回退内置 3 点 |
| **P5** | `Source/Game/Game.Build.cs` | 新增 `options.PublicDependencies.Add("NarrativePro")`；已重新生成 `Game.csproj` 与 `HundunWorld.sln` |

### 9.2 实现期核验修正（新增发现）

| # | 发现 | 影响与处置 |
|---|---|---|
| **G16** | `GameplayTag` 原为 `readonly struct` + 只读属性，**Flax 序列化器只还原有 setter 的成员**，导致场景/存档中被写成空对象 `{}`（World.scene 中 `POITag: {}` 即证据），POI 标签与地图层级静默丢失 | 改为非 readonly 结构体 + 可写 `TagName`（带 null 归一）。**这是导航可用性的前置缺陷** |
| **G17** | `POIActor` 用 `Actor.GetScript<Collider>()`，但 `Collider` 是 **Actor** 而非 Script | 改 `Actor.GetChild<Collider>()`；发现触发器自动选取优先取 `IsTrigger` 且非快速旅行落点的碰撞体 |
| — | `Vector3.IsZero()` **存在于头文件但未导出到 C#** | 改用 `LengthSquared`（双重验证规则再次生效） |
| — | `GPUTexture.Init(desc)` 与 `Level.SpawnActor(actor)` 均为「**返回 true 表示失败**」的反向约定 | 烘焙器中按反向语义处理 |
| — | `Camera` / `Navigation` 等标识符在本工程会解析为 `NarrativePro.Camera` / `NarrativePro.Navigation` **命名空间**（CS0118） | 统一全限定 `FlaxEngine.Camera` / `FlaxEngine.Navigation` |
| — | 碰撞/净空查询若不禁用触发器，**POI 自身的发现触发器会把落点判为被占用** | 所有 `Physics.*Cast/CheckSphere` 一律传 `hitTriggers: false` |
| — | `Navigation.FindPath` 为**同步**调用（无 UE 的异步版本） | 面包屑限频（`UpdateNavPathRate`）+ 距离门控；重采样带上限 |

### 9.3 待实测标定（须在编辑器中确认）

| # | 项 | 方法 |
|---|---|---|
| R1 | `Camera.OrthographicSize` 语义（半高/全高）与 `CameraPitchDegrees` 俯视方向 | 烘焙单块瓦片后比对世界标志物的像素跨度，调整 `MapTileBakeJob.OrthographicSizeMultiplier` 至瓦片无缝拼接 |
| R2 | 瓦片贴图落盘与导入的时序 | 已内置 `File.Exists` + 超时；首次运行观察日志确认无超时 |
| R3 | 快速旅行落点是否被 POI 的实体碰撞体拒绝 | 若日志出现「未找到完全净空的落点」，检查 POI 的碰撞体是否应设为 Trigger |

### 9.4 建议的验证步骤

1. 打开 `Content/Maps/World.scene`（已含 `NavigationSubsystem` 与 `MapTileBoundsActor`）。
2. 选中 `MapTileBoundsActor`，将 `MapWidth` / `NumTiles` 调到实际世界尺度（当前场景存的是占位值 `GridWidth: 512`，且 `NumTiles` 为默认 1）→ 菜单 **Tools → NarrativePro: 重新生成地图瓦片网格** → 保存场景。
3. 放置 `POIActor`（含 Trigger 碰撞体）并填写 `POITag`、`POIDisplayName`、`bSupportsFastTravel`；用 `TileSet` 或 POI 的 `LinkedPOIs` 连成导航图。
4. 菜单 **Tools → NarrativePro: 烘焙地图瓦片贴图** 生成贴图并回填层级。
5. 运行后打开世界地图 / 小地图 / 罗盘：有 POI 时显示真实点位，无 POI 时保持既有演示观感；点击界碑触发快速旅行。

### 9.5 本次未做（与设计范围一致）

- `SetMapLayer` 的 `domains` 多域差异（UE5 亦为 TODO）。
- 烘焙仅支持「同场景 + 可配置相机高度」的单/多层，不做多场景切换。
- Tales 的 `TickTask` 目前**没有全局驱动器**（`QuestBranch` 只调 `BeginTaskInit`），故 `GoToLocationTask` 的到达判定需由任务系统或外部脚本按 `TickInterval` 主动调用 `CheckArrival()`。这是既有缺口，非本次引入。

### 9.6 发现的既有问题（非本次引入）

- `Plugins/PCG/Source/PCG/Runtime/PCGVolume.cs:356`：`PCGVolume : Actor` 却 `override OnDebugDraw()`（`Actor` 无此方法），导致 **`GameTarget` 无法构建**（报 CS0115）。`GameEditorTarget`（工程实际使用的目标）不受影响。修复方式为给该类加 `#if FLAX_EDITOR` 守卫并改为在编辑器侧绘制。

---

## 10. 增量二：POI/传送点落地为场景 POIActor + 距离驱动发现

### 10.1 内容

| 项 | 说明 |
|---|---|
| 场景 | `Content/Maps/World.scene` 新增 **10 个原生 `POIActor`**（各含 宿主 EmptyActor + 触发器 `SphereCollider`(`IsTrigger`) + `POIActor` 脚本）：4 个传送点（开封城/青云山/凉州/江南渡口）+ 6 个兴趣点（清河村/落雁峰/北山道/古战场遗迹/燕北关/药王谷），并用 `LinkedPOIPaths` 连成导航图 |
| 地图区域 | 修正为 `MapWidth=4000`、`NumTiles=4`、中心 (-400,0,0)（原为默认 100000/原点居中，会导致 POI 全部挤在地图正中） |
| 发现开关 | 传送点 `bIsDiscoverable=false`（传送网络默认已知、可立即作为快速旅行目标）；兴趣点 `true`（需探索） |
| 发现半径 | `POIData.DiscoveryRadius` / `POIActor.DiscoveryRadius`（传送点 900~1200，兴趣点 700~800） |
| 发现驱动 | 新增 `Source/Game/World/MapDiscoveryDriver.cs`，由 `HundunWorldGame.OnUpdate` 每帧调用（内部 0.5s 限频）：玩家进入半径即调 `NarrativeNavigationComponent.DiscoverPOI` |
| 玩家侧 | `CreateLocalPlayerActor` 给玩家挂 `NarrativeNavigationComponent`（**仅脚本、不加碰撞体**，不改变玩家物理） |

### 10.2 关键设计决策：为何"发现"用距离而不是触发体积

`POIActor` 的触发体积已就位，但**本项目玩家角色没有碰撞体**：

- `Content/Prefabs/Character/CharacterRoot.prefab` 只含 2 个 StaticModel + `PlayerController` 脚本；
- `HundunWorldGame.CreateLocalPlayerActor` 的 Prefab 路径只 `AddScript`（装备/属性/同步），**不加碰撞体**；兜底路径的 `BoxCollider` 也只是静态 collider（无 RigidBody）。

而 PhysX 的触发器重叠事件要求至少一方为动态刚体 → `DiscoveryCollider.TriggerEnter` 在当前角色结构下**不会触发**。因此当前以距离判定作为唯一可靠途径；**场景中的触发器体积保留**，待玩家角色获得碰撞体后可直接切回触发式（`POIActor.FindDiscoveryCollider` 已能自动挂接）。

### 10.3 现场约定（`bIsDiscoverable` 与快速旅行）

`RequestFastTravel` 会拒绝"可发现但未发现"的 POI。因此把传送点设为 `bIsDiscoverable=false` 是必要配置——否则玩家在走近之前**无法把传送点作为快速旅行目标**。

### 10.4 各界面表现

| 界面 | 已发现/已知 | 未发现 |
|---|---|---|
| 世界地图 | 传送点金色界碑；兴趣点金色圆点 | 兴趣点显示为青色圆点（`Mk.Oddity`，与"异闻"同款视觉，未额外新增字形） |
| 小地图 | 显示（`NavigationRange` 内） | **不显示**（迷雾语义） |
| 方位条 | 传送点「界」金色、兴趣点「景」青色 | 「？」纸色 |

### 10.5 编辑器落地过程中的坑（详见记忆 `flax-mcp-scene-editing`）

- `flax_mcp` 的 `scene_load` **只能用 GUID**，且**每次都报超时但实际成功**；重试会把已打开场景卸载（`scenes: []`）。
- **编辑器会缓存场景**：外部手改 `.scene` 不被读取，随后编辑器保存会覆盖手改内容（本轮因此丢失过一次手写的地图区域配置）。
- **`sim_play`/`sim_stop` 之后 `scene_save` 不再持久化编辑态改动**（实测确定），故需重载脚本域时要"先保存、再 PIE"。
- `ui_script_add` 只认**短类名**；`ui_script_set` **不支持自定义结构体**（`GameplayTag` → 报"不支持的类型"）。应对：`POIActor` 增加字符串字段 `POITagName`（编辑器可写、Flax 可原生持久化），`BuildPOIData` 在 `POITag` 无效时回退用它。
- `actor_set_property` 一次只能设一个属性；`actor_create` 建子 Actor 时**必须传 position**，否则局部偏移会变成父级坐标的负值（碰撞体落到世界原点）。

---

## 11. 增量三：快速旅行的执行权归属（修复"点击传送点无反应"）

### 11.1 现象与定位

点击世界地图上的传送点无反应。日志（`log_scan` 命中 `Log_2026_09_24_00_10_34.txt`）显示点击事件本身是通的，失败在插件侧：

```
[Warning] [NarrativePro] RequestFastTravel: 找不到本地玩家角色，无法传送
[Warning] [WorldMapPage] 无法传送到 Navigator.PointOfInterest.Qingyunshan（未发现/不支持快速旅行/无安全落点）
```

### 11.2 两个根因

| # | 根因 | 说明 |
|---|---|---|
| 1 | **插件强依赖 `NarrativeCharacter`** | `RequestFastTravel` 用 `ResolveLocalCharacter()` 取玩家，而它只认 `NarrativePlayerController` / `NarrativeCharacter` 脚本；本项目的玩家是普通 Actor（挂 `HundunWorld.Game.PlayerController` 等游戏脚本），因此恒为 null |
| 2 | **客户端改位置会被同步系统覆盖** | `LocalPlayerActorSyncSystem.OnUpdate` **每帧**把 `Actor.Position` 写成 ECS `PredictedTransformComponent` 的值（`PredictedTransformComponent` 是本地玩家位置的单一事实源）——任何只改 `Actor.Position` 的传送都会在下一帧被打回原处 |

### 11.3 架构决策：插件只发请求，游戏侧执行移动

回归 UE5 的原始语义（`OnFastTravelRequested` 本就是"各游戏自行实现"的钩子）：

- **插件**：`RequestFastTravel` 只做校验（支持快速旅行、可发现者须已发现）与**目标解析**，然后广播 `OnFastTravelRequested(POIData, Transform)`。**无订阅者时**（插件独立使用）才回退为直接 `TeleportActor`。
- **游戏侧**（`HundunWorldGame`）：在挂载导航组件时 `SetLocalPlayerActor(actor)` 注入玩家并订阅该事件；处理器把目标位置写入 ECS `PredictedTransformComponent`，并同步 `Actor.Position` 以获得即时视觉反馈。
- **轴映射**（与同步系统一致）：`Flax.X → pred.X`、`Flax.Y → pred.Z`（上下）、`Flax.Z → pred.Y`（前后）；同时写 `pred.Yaw` 并把 `pred.Vz` 清零（避免残留重力把玩家拽离落点）。
- **高度**：优先用 `SampleGroundHeightEcs(x, z)` 贴地；采样失败（NaN，例如该点无地形）时退回 POI 配置高度并告警。
- 新增 `ArsenalStatics.TeleportActor(Actor, Vector3, Quaternion)`：不要求目标是 `NarrativeCharacter`；若该 Actor 上恰好有 `NarrativeCharacter` 仍会触发其 `OnTeleportSucceeded`。原 `TeleportCharacter` 委托给它。

### 11.4 服务端权威传送（已实现，增量四）

代码取证表明客户端侧传送**必然**被回滚：`MovementValidator.CheckAcceleration` 对水平位移
> `TeleportDistanceThreshold`(100m) 直接返回 `AccelerationViolation.TeleportDetected`（`IsValid=false`），
随后经 `CorrectionPacket` → 客户端 `ReconciliationSystem` 把玩家拉回。故补上服务端权威路径：

| 层 | 改动 |
|---|---|
| 协议 | 新增 `FastTravelRequestMessage`（union tag **226**、`MessageType.FastTravelRequest = 1406`、`ServiceType.Game`），并在 `MessageUnion.cs` 手工登记 `MemoryPackUnion(226, …)`（生成器默认不产出 union 块，漏加会被 `UnionRegistrationParityTests` 双射断言打红） |
| Handler | 新增 `Horizon.Game.Core/Handlers/FastTravelHandler`：`EntityId` 经 `IShardRouter.Resolve` 定位分片 → 调 `IZoneShardGrain.RequestFastTravelAsync`。成功返回 `null` 包（不回包，位置由快照下发）；失败回 `ErrorMessage` |
| Sim | 新增 `SimWorld.TeleportTo(entityId, x, y, z, yawRadians)`：在 `SetPosition`（对齐权威位置 + 重置 `Prev*`/`ReportedEnd*` 瞬移与漂移基线）之上**额外清空 `PendingInputs`**——否则传送前的输入会在新位置继续回放，把玩家又走回原处 |
| Grain | `IZoneShardGrain.RequestFastTravelAsync` + `ZoneShardGrain` 实现：调 `SimWorld.TeleportTo` → 镜像 `_simulatedEntities`（含清 `PendingInputs`）→ `_forceFullSnapshotNextTick = true` 立即下发 |
| 客户端 | `HundunWorldGame` 保留本地预测（即时手感），并 `SendMessageAsync(FastTravelRequestMessage)` 上报权威应用（fire-and-forget） |

**轴映射**（客户端与 `ZoneShardGrain` 一致）：`Flax.X → ECS.X`、`Flax.Y → ECS.Z`（上下）、`Flax.Z → ECS.Y`（前后）；`Yaw` 为弧度。

**信任模型（已知边界）**：服务端目前**接受客户端上报的坐标**，只做实体存在性校验，不做"该坐标是否为合法传送点"的校验——因为 POI 目录只在客户端/场景侧。若要防作弊，需要把传送点目录与"已发现"状态搬到服务端，这是后续工作。

### 11.5 部署要求（重要）

1. **客户端编译/运行依赖的共享库位于 Flax 引擎目录**：`Game.Build.cs` 通过 `AppDomain.CurrentDomain.BaseDirectory`
   解析 `Horizon.Game.Message.dll` / `Horizon.Game.Sim.dll` / `Horizon.Game.Core.dll`，即
   `C:\Program Files (x86)\Flax\Flax_1.12\Binaries\Tools\`——**写入需 UAC 提权**。协议变更后必须把这 3 个 DLL 复制过去，
   否则客户端编译报 `CS0234`，或运行期加载到旧协议。
   （`HundunWorld/Output/Windows/` 也会被 `Game.csproj` 的 HintPath 引用，可一并同步。）
2. **改动了 Orleans Grain 接口**（`IZoneShardGrain`），必须**停止并重启网关与 Silo** 后新协议才生效；
   在旧进程仍运行时编译会被文件占用阻塞（`MSB3027/MSB3021`，非编译错误）。
