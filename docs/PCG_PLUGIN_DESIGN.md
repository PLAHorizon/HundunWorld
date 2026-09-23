# FlaxEngine PCG（程序化内容生成）插件全功能设计文档

**版本：** 1.2
**状态：** 设计阶段 — 已对照 Flax 1.12.6913 实际 API 逐条核验
**适用引擎：** FlaxEngine **1.12.6913**（本仓库实际安装版本，`C:\Program Files (x86)\Flax\Flax_1.12`）
**目标工程：** `HundunWorld\HundunWorld.flaxproj`

**v1.2 变更摘要：** 对 v1.1 做二次独立复核（引擎头文件 + C# XML 全量导出面交叉验证），改动四项：（1）**v1.1 关于「Flax 无 `Decal` Actor」的判断是错的** —— `Source\Engine\Level\Actors\Decal.h` 明确定义 `class Decal : public Actor`，且 C# 侧全量导出，`Spawn Decal` 节点成立；推迟到 Phase 3 的真实理由是**无批量实例化路径**而非「做不出来」；（2）**坡度法线无需再靠实测猜测** —— `TerrainPatch.RayCast` 有直接输出法线的重载，核验盲区之一关闭；（3）**自定义 `Asset` 类型对纯托管插件不可行**（需 C++ 侧注册并重编引擎），`JsonAsset` 由「规避风险」升格为「唯一可行路径」；（4）新增一条**核验方法教训**：PowerShell 下 `findstr` 会因转义产生假阴性，可造成整条架构结论错误（本次即为此踩坑）。详见 [附录 A.3c](#a3c-v11--v12-二次复核修正表)。

**v1.1 变更摘要：** v1.0 中附录与正文引用的 Flax API 有 11 处名称/类型错误（含 3 处会导致编译失败、4 处会导致设计无法实现），另有 6 处架构级方案需要修正。本版逐条校正，其中两项变更显著改变工程量与风险：**编辑器画布改为派生 `VisjectSurface`**（工作量大降）、**噪声体系改为插件自带种子化实现**（引擎噪声无种子，PCG 的可复现性目标无法靠它达成）。核查手段除 API 文档与引擎头文件外，还使用了 .NET 反射直读程序集元数据，额外查明 9 项文档无法暴露的类型细节（附录 A.3b）。详见 [附录 A：v1.0 → v1.1 勘误表](#附录-av10--v11-勘误表) 与 [附录 B：核验方法说明](#附录-b核验方法说明)。

---

## 1. 概述

### 1.1 目标

为 FlaxEngine 设计一个功能对等 UE5 PCG（Procedural Content Generation Framework）的程序化内容生成插件。核心目标包括：

- **点数据驱动**：以 PCG Point 为核心数据单元，支持密度、变换、属性元数据。
- **图表化工作流**：提供可视化节点图编辑器，用户通过连线构建生成逻辑。
- **采样与生成**：支持从地形、样条线、体积等来源采样点，并生成静态模型、植被等输出。
- **编辑器内迭代**：支持在编辑器中实时预览生成结果，参数调整后即时刷新。

### 1.2 技术底座（已核验）

FlaxEngine 已具备以下能力，直接支撑 PCG 插件实现。**"核验结果"列为本版新增，标注了实现时必须注意的限制。**

| Flax 能力 | 支撑的 PCG 功能 | 核验结果 |
|---|---|---|
| 插件系统（`GamePlugin` / `EditorPlugin`） | 运行时与编辑器扩展 | ✅ 可用。`Plugin` 基类提供 `Initialize()` / `Deinitialize()`；`EditorPlugin` 追加 `InitializeEditor()` / `DeinitializeEditor()` |
| `FlaxEngine.Utilities.Noise` 静态类 | 噪声采样节点 | ⚠️ 可用但有 3 个限制：**无种子参数**、**仅 2D**、**返回类型不一**。见 §3.4 与 §5.1.1 |
| 样条线 Actor 与 `Spline` 结构体 | 样条线采样与放置 | ✅ `Spline.GetSplineTransform(float)` 存在。注意 `Spline`（结构体）与 `SplineActor`（Actor）是两个类型 |
| 地形高度图 API | 地形表面采样、坡度采样 | ⚠️ 无 `GetHeight()`。有 3 条可行路径，见 §3.5 |
| `ModelInstanceActor` 实例化 | 批量生成静态模型 | ✅ 优于逐点建 `StaticModel`，见 §5.1.5 |
| `Foliage` 实例 API | 植被生成 | ✅ `AddFoliageType(Model)` + `AddInstance(ref FoliageInstance)` |
| Visject 节点图框架 | PCG 图表编辑器底座 | ✅ **重点**：`FlaxEditor.Surface.VisjectSurface` 提供完整节点画布，v1.0 的"自研画布"方案已废弃，见 §6.1 |
| 程序化资产创建 | 运行时生成模型/植被 | ✅ `Content.CreateVirtualAsset<T>()` |

---

## 2. 系统架构

### 2.1 分层架构

```
┌─────────────────────────────────────────────────────────────┐
│                    编辑器层 (Editor Layer)                    │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────────┐  │
│  │ PCG 图表编辑器 │  │ PCG 组件面板  │  │ 视口调试绘制器    │  │
│  │(VisjectSurface│  │(CustomEditor)│  │ (DrawLines 批处理)│  │
│  │  子类)        │  │              │  │                  │  │
│  └──────┬───────┘  └──────┬───────┘  └────────┬─────────┘  │
└─────────┼─────────────────┼───────────────────┼────────────┘
          │                 │                   │
┌─────────┼─────────────────┼───────────────────┼────────────┐
│         ▼                 ▼                   ▼            │
│                    运行时层 (Runtime Layer)                   │
│  ┌──────────────────────────────────────────────────────┐  │
│  │                 PCG 执行引擎                           │  │
│  │  ┌──────────┐  ┌──────────┐  ┌──────────────────┐   │  │
│  │  │ 图表编译器│→│ 节点执行器 │→│ 点数据（PCGPoint）│   │  │
│  │  └──────────┘  └──────────┘  └──────────────────┘   │  │
│  └──────────────────────────────────────────────────────┘  │
│  ┌──────────────────────────────────────────────────────┐  │
│  │                 PCG 数据层                            │  │
│  │  PCGPoint │ PCGPointList │ PCGGraphAsset │ SeededNoise │  │
│  └──────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

### 2.2 核心模块

| 模块 | 职责 | 实现载体 |
|---|---|---|
| **PCGPoint 数据层** | 定义点结构、属性容器、密度语义 | `readonly struct` + 引用型属性存储 |
| **PCG 图表资产** | 存储节点网络拓扑与参数 | 自定义 `Asset` + Visject `Save`/`Load` |
| **PCG 执行引擎** | 按拓扑执行节点，管理数据流 | 拉取式递归 + 节点结果缓存 |
| **PCG 节点库** | 提供采样/筛选/变换/生成节点 | `PCGNodeBase` + `NodeArchetype` 表 |
| **PCG 图表编辑器** | 节点拖拽、连线、参数编辑 | `VisjectSurface` 派生（非自研） |
| **PCG 组件** | 场景集成，触发执行，暴露参数 | `PCGVolume : Actor` |
| **视口调试器** | 绘制生成点、密度色、边界 | `DebugDraw.DrawLines` 单批调用 |

---

## 3. 核心数据模型

### 3.1 PCGPoint

```csharp
/// <summary>PCG 点。世界空间变换 + 密度 + 包围盒。</summary>
public struct PCGPoint
{
    public Vector3 Position;        // 世界空间位置（必须 Vector3，不能用 Float3 —— 见下方注 1）
    public Quaternion Rotation;     // 旋转
    public Float3 Scale;            // 缩放（用 Float3，与引擎 Transform/Actor.Scale 一致）
    public float Density;           // 密度 [0,1]，影响筛选与生成
    public int Seed;                // 随机种子（int，非 long —— 见下方注 2）

    public BoundingBox Bounds;      // 轴对齐包围盒，用于自剪枝与重叠检测
    public PCGAttributeSet Attributes; // 引用类型，允许共享（见 §3.2 COW）
}
```

> **注 1（v1.1 修正）：`Position`/`Rotation` 用 `Vector3`/`Quaternion`，`Scale` 用 `Float3`。**
> 经反射核验，`FlaxEngine.Transform` 的字段类型为：`Translation : Vector3`、`Orientation : Quaternion`、`Scale : Float3`；`Actor.Scale`、`Actor.LocalScale`、`Actor.EulerAngles` 亦均为 `Float3`。因此本设计与之对齐：
> - **`Position` 必须是 `Vector3`（float32）**。`FlaxEngine.Float3` 是半精度（16 bit/分量）类型，表示位置时在大坐标下会剧烈丢失精度，绝不可用于世界空间位置。
> - **`Rotation` 用 `Quaternion`**。
> - **`Scale` 用 `Float3` 是正确且推荐的** —— 半精度对缩放比值（通常 0.01–100）足够，且与引擎 `Transform` 直接互操作，避免每点转换开销。
>
> 对比：v1.0 把缩放写成 `Vector3`，虽然能编译，但与引擎 `Transform.Scale` 类型不一致，每次实例化都要转换。（本条的初稿曾把"禁用 Float3"过度泛化到缩放，已按反射结果更正。）
>
> **注 2（v1.1 修正）：种子类型必须是 `int`。**
> Flax 的 `RandomStream` 只接受 `int`（`RandomStream(int)`、`RandomStream.Initialize(int)`、`RandomStream.GetInitialSeed()` 返回 `int`）。v1.0 使用 `long` 会导致无法与引擎 RNG 互操作。

**设计要点：**
- `Density` 独立于属性系统，作为高频操作字段直接访问。
- `Bounds` 由采样器根据资产或默认值计算，用于重叠剔除。**注意：Flax 的 `BoundingBox` 是轴对齐（AABB）**，因此 Self-Pruning Filter 无法使用旋转后的点包围盒。若需 OBB，须显式使用 `OrientedBoundingBox`（存在该类型，但会增加每点 28 字节开销），本设计默认按 AABB 处理，并在节点参数中暴露 `PruneMode`。
- `Attributes` 为引用类型，允许继承与共享（降低拷贝开销）。**但由此产生一个必须显式处理的语义陷阱，见 §3.2 注 3。**

### 3.2 PCGAttributeSet（属性系统）

属性系统采用 **SoA（Structure of Arrays）** 布局以优化缓存性能：

```csharp
public sealed class PCGAttributeSet
{
    // 属性描述符
    private Dictionary<string, AttributeDescriptor> _descriptors;

    // 数据存储（按属性类型分数组）
    private Dictionary<string, float[]> _floatArrays;
    private Dictionary<string, Vector3[]> _vectorArrays;
    private Dictionary<string, int[]> _intArrays;
    private Dictionary<string, string[]> _stringArrays;
    // ... 其他类型

    private int _refCount;    // 持有者计数
    public int Count { get; }
    public bool IsShared => _refCount > 1;

    public float GetFloat(string name, int index);
    public void SetFloat(string name, int index, float value);   // 内部先 Detach()
    // ... 类型化访问器

    /// <summary>写屏障：若本存储被共享，克隆底层列数组并使本实例独占。</summary>
    public void Detach();

    /// <summary>登记一个额外持有者（只读共享），必须与 Release 配对。</summary>
    public PCGAttributeSet Share();
    public void Release();
}
```

**支持的类型**（参照 UE5 PCG 属性类型）：`float`、`int`、`Vector2`、`Vector3`、`Vector4`、`bool`、`string`、`Quaternion`。

**关键设计决策：**
- 属性名以 `$` 前缀表示内置属性（如 `$Density`、`$Position`），自定义属性无前缀。
- 当节点修改属性时，采用**写时复制（Copy-on-Write）** 策略：共享的存储在被写入前先 `Detach()`。
- 支持**属性继承**：子点可以从父点继承属性，减少存储重复。

> **注 3（v1.1 新增）——COW 的语义陷阱与强制约定。**
> v1.0 把 `PCGAttributeSet` 放在 `struct PCGPoint` 里，同时依赖 COW 共享。这两者组合有一个隐蔽问题：**结构体的逐成员拷贝会静默复制引用**。当 `PCGPoint[]` 数组被拷贝（`Array.Copy`、`ToArray()`、切片、`List<T>` 扩容）时，每个点仍指向**同一个** `AttributeSet` 实例。若此后某节点对该集合做原地写入，会同时改掉所有"看似独立"的点。
>
> 因此本设计定下不可违反的三条约定：
> 1. **写屏障必须就地生效（`Detach()` 而非返回副本）。** 这是实现期才发现的关键细节：若把 `EnsureOwned()` 设计成"返回一个克隆"，而调用方写成 `EnsureOwned().Set(...)` 或忘记接收返回值，克隆会被**静默丢弃**，写入落到共享原体上 —— COW 完全失效，且没有任何编译错误。因此 `Detach()` **就地克隆底层数组并把 `_refCount` 置为 1**，不返回新对象。这样"调用方忘记接收返回值"这个错误在构造上不可能发生。
>    > *（本设计文档 §3.2 初稿曾写 `EnsureOwned()` 返回副本，实现时纠正为就地 `Detach()`。此处以免后人改回错误形态。）*
> 2. 节点内**禁止**跨阶段缓存 `attrSet` 引用；一阶段内取用一次。
> 3. **读取用只读访问器，写入必须用 `GetWritable*`。** 批量访问器有两套：`GetFloatArray()` 返回活引用（读用），`GetWritableFloatArray()` 先 `Detach()` 再返回（写用）。批量写入器只 `Detach()` 一次，避免逐行做引用计数检查。
>
> 违反第 3 条的典型症状是"筛选后修改一个点的密度，其余点跟着变"，且极难定位。
>
> **实现期补充的判断：** `PCGData` 层面只在 `MergePoints` / `SplitPoints` 这类"产生新集合"的节点做共享（通过 `Share()` 登记）；筛选/变换类节点默认走 `Gather()` / `Clone()` 产出独占副本。`Share()` 只用于"确知下游只读"的场景。

### 3.3 PCGGraphAsset

```csharp
public class PCGGraphAsset : Asset
{
    public PCGNode[] Nodes;
    public PCGEdge[] Edges;
    public PCGParameterDefinition[] ExposedParameters; // 暴露给组件的参数
    public PCGGraphSettings Settings;                  // 种子策略、边界策略、调试开关

    // Visject 持久化钩子（见 §6.1）
    internal byte[] SurfaceData;
}
```

> **注 4（v1.1 校正）：自定义 `Asset` 的注册方式。**
> Flax 1.12 **不存在**公开的 `CreateAssetAttribute`（已核验：全量 XML 文档中无此类型）。自定义资产的注册走引擎内部的 `API_CLASS` + `[Asset]` 约定与内容导入器（`ContentImporter` / `AssetProxy`）机制；编辑器侧窗口派生自 `FlaxEditor.Windows.Assets.AssetEditorWindow`，序列化类型名通过 `SerializationTypename` 属性暴露。
> **实现动作：** Phase 1 先用**引擎支持的既有资产类型**（如 `JsonAsset` 承载图表拓扑）打通闭环，把"原生二进制的 `PCGGraphAsset` + 双击打开 + 缩略图"推迟到 Phase 2 一并做，避免在最开始就卡在引擎侧资产注册的边界上。此决策已纳入 §9 路线图。
>
> **v1.2 升格：这不是「规避风险」，而是唯一可行路径。** 复核后确认 Flax 的自定义 `Asset` 类型注册完全在 **C++ 侧**（`API_CLASS` + `IImporter` + 引擎构建期资产类型注册），**纯托管（C#）插件无法注册新资产类型**，必须修改引擎源码并重新编译。因此：
> - Phase 1 / Phase 2 均以 `JsonAsset` 承载图表拓扑，插件保持纯托管、可随引擎小版本升级；
> - 若最终确需原生 `PCGGraphAsset`（为双击打开、缩略图、依赖追踪），则必须附带 **C++ 插件或引擎 fork**，工程量与升级维护成本需单独立项评估，不进入 PCG 主体路线图；
> - **序列化细节**：Visject 的 `SurfaceData` 是二进制 `byte[]`，JSON 是文本。不要 base64 内嵌 —— `VisjectSurface.Save()` 产出的本身就是可文本化的结构，直接以 JSON 子节点存入 `JsonAsset`，避免多一层编码与可读性损失（图表拓扑进版本控制时 diff 友好）。

```csharp
public class PCGNode
{
    public string NodeTypeId;        // 节点类型标识（对应 NodeArchetype.TypeID）
    public Vector2 EditorPosition;   // 图表编辑器位置
    public Dictionary<string, object> Parameters; // 节点参数
}

public class PCGEdge
{
    public int OutputNodeIndex;
    public string OutputPinName;
    public int InputNodeIndex;
    public string InputPinName;
}
```

### 3.4 随机与噪声（v1.1 新增章节）

**这是 v1.0 最大的设计缺口。** v1.0 的 `PCGExecutionContext.Seed` 被定义并要求"全局随机种子"，但附录推荐的 `FlaxEngine.Utilities.Noise` **全部函数都没有种子参数**：

```csharp
// C:\Program Files (x86)\Flax\Flax_1.12\Source\Engine\Utilities\Noise.h （已核验全文）
API_FUNCTION() static float  PerlinNoise(const Float2& p);              // float, 0-1
API_FUNCTION() static float  PerlinNoise(const Float2& p, const Float2& rep);  // 可平铺
API_FUNCTION() static float  SimplexNoise(const Float2& p);             // float, 0-1
API_FUNCTION() static Float2 WorleyNoise(const Float2& p);              // Float2: F1, F2
API_FUNCTION() static Float3 VoronoiNoise(const Float2& p);             // Float3: minDist, randomColor, minEdgeDist
API_FUNCTION() static float  CustomNoise(const Float3& p);              // 无文档语义
API_FUNCTION() static Float3 CustomNoise3D(const Float3& p);
API_FUNCTION() static Float3 CustomNoise3D(const Float3& p, int32 octaves, float roughness);
```

三个必须处理的事实：

1. **无种子 → PCG 的可复现性目标无法靠引擎噪声达成。** PCG 的核心价值之一是"同一图表 + 同一种子 = 同一结果"，而引擎噪声对同一点永远返回同一值。必须自建 `SeededNoise`。
2. **返回类型不统一 → "噪声值"不是一个统一概念。** `PerlinNoise`/`SimplexNoise` 返回标量 float；`WorleyNoise` 返回 `Float2`；`VoronoiNoise` 返回 `Float3`。下游节点必须显式选通道，否则语义不明。
3. **没有 3D Perlin/Simplex → Volume Sampler 无法按 v1.0 的设想实现。** 只有 `CustomNoise(Float3)` 与 `CustomNoise3D(Float3, octaves, roughness)`，且其数值语义在头文件中无文档说明。

**决策：插件自带种子化噪声，不依赖引擎噪声。**

```csharp
/// <summary>
/// 插件自带种子化噪声。与种子绑定，保证跨平台/跨引擎版本可复现。
/// 实现：整数哈希（PCG hash / xxHash32 变体）驱动的 value noise + FBM。
/// </summary>
public static class PCGSeededNoise
{
    public enum NoiseKind { Perlin2D, Simplex2D, Worley2D, Voronoi2D, Value3D, Worley3D }

    /// <summary>2D 标量采样，返回 [0,1]。</summary>
    public static float Sample2D(NoiseKind kind, Vector2 p, int seed, float frequency);

    /// <summary>3D 标量采样，返回 [0,1]。支持任意 kind 的 3D 扩展。</summary>
    public static float Sample3D(NoiseKind kind, Vector3 p, int seed, float frequency);

    /// <summary>分形叠加。</summary>
    public static float Fbm2D(Vector2 p, int seed, int octaves, float lacunarity, float gain);
    public static float Fbm3D(Vector3 p, int seed, int octaves, float lacunarity, float gain);

    /// <summary>Worley/Voronoi 的多通道结果。</summary>
    public static Vector3 SampleFeature3D(Vector3 p, int seed, float frequency);
}

/// <summary>PCG 专用确定性 RNG：由 (全局种子, 节点ID, 点索引) 三元组派生。</summary>
public struct PCGDeterministicRng
{
    private uint _state;
    public PCGDeterministicRng(int globalSeed, int nodeId, int pointIndex);
    public float NextFloat();                       // [0,1)
    public float NextRange(float min, float max);
    public Vector3 NextUnitVector();
    public Quaternion NextRotation();
}
```

**设计意图：**
- `PCGDeterministicRng` 由三元组派生而非顺序调用，使**单点重算与全量重算结果一致** —— 这是"增量执行"（§4.4）能正确工作的前提。顺序 RNG 会让增量执行产生与全量执行不同的结果，是 v1.0 未发现的正确性缺陷。
- `PCGSeededNoise` 需要单元测试锁定数值输出（黄金值测试），防止重构悄悄改变生成结果。
- 若确需复用引擎噪声以匹配引擎内建地形外观，保留一个 `Engine` 模式作为可选项，并在 UI 上明确标注"不可种子化"。

`PCGExecutionContext.Seed` 保持为 `int`，与 `RandomStream` 兼容：

```csharp
public class PCGExecutionContext
{
    public int Seed;                       // 全局随机种子（int，兼容 RandomStream）
    public BoundingBox GenerationBounds;
    public PCGWorldContext World;
    public CancellationToken CancelToken;
    public PCGDebugMode DebugMode;
}
```

### 3.5 地形采样策略（v1.1 新增章节）

v1.0 依赖 `Terrain.GetHeightmapSize()` 推测高度查询方式，实际上 Flax 1.12 **没有任何 `Terrain.GetHeight()` 方法**。核验后的可用路径有三条，各有明确取舍：

| 路径 | API | 优点 | 代价 | 核验状态 |
|---|---|---|---|---|
| **A. 高度场直读** | `TerrainPatch.GetHeightmapData()` → **`float*`** | 最快，适合 10 万点量级 | 高度值语义需实测标定；patch 边界与缺失 chunk 需自行处理 | ⚠️ 返回类型已反射确认（`float*`），**到世界 Y 的换算语义未在头文件中定义，必须实测标定** |
| **B. 逐点射线** | `Terrain.RayCast(Vector3 origin, Vector3 dir, out float hitDistance, float maxDistance)` 等 4 个重载 | 语义绝对正确，无标定问题 | 每点一次射线检测，大规模明显慢；**不返回法线**（见下） | ✅ 反射确认全部重载 |
| **C. 高度图纹理** | `TerrainPatch.Heightmap` | — | 反射确认 `Heightmap : Texture`，是 **GPU 纹理**，CPU 侧不可直读 | ❌ v1.0 此路径不成立 |

**决策（按用户选择）：两条都实现，节点上可切换。**

```csharp
public enum PCGTerrainSampleMode
{
    /// <summary>路径 B：逐点射线投射。默认值 —— 正确性优先，作为标定基准。</summary>
    RayCast,

    /// <summary>路径 A：高度场双线性插值。性能优先，首次使用需标定。</summary>
    HeightfieldInterpolated,
}

// Landscape Sampler 节点参数
public PCGTerrainSampleMode SampleMode = PCGTerrainSampleMode.RayCast;

/// <summary>路径 A 的高度换算参数。由 Calibrate() 自动填充。</summary>
public float HeightScale = 1.0f;
public float HeightOffset = 0.0f;

/// <summary>路径 A 失败时（patch 缺失/标定未完成）是否回退到路径 B。</summary>
public bool FallbackToRayCast = true;
```

**实现的六个必须处理点：**

1. **高度语义标定（路径 A 的前置条件）。** `GetHeightmapData()` 返回的 float 到世界 Y 的换算关系未在引擎头文件中定义，且 `TerrainPatch.GetHeightY()` 是**高度范围**（`_yHeight`）而非高度值 —— 极易误用。做法：写一个 `PCGTerrainProbe.Calibrate(Terrain)` 工具，在地形上取 3×3 个已知世界坐标点，同时用路径 A 与路径 B 求高度，解出 `HeightScale` / `HeightOffset` 并缓存。**在标定完成前强制走路径 B**，避免静默错误。
2. **Patch 坐标换算。** 地形几何：`ChunksCount = 16`、`ChunksCountEdge = 4`、`ChunkSize` 由 `Terrain.Setup(lodCount, chunkSize)` 设定（默认 127），故 `HeightmapSize = ChunkSize * 4 + 1`（每 patch），`PatchSize = UnitsPerVertex * ChunksCountEdge * ChunkSize`。世界坐标 → patch：`patchX = floor(worldX / PatchSize)`，用 `Terrain.HasPatch(Int2)` / `Terrain.GetPatch(Int2)` 取 patch，patch 内 UV 用 `TerrainPatch.GetBounds()` 求。
3. **法线/坡度（v1.2 修正 —— 原「必须实测」项已关闭）。** `Terrain.RayCast` 只有 2 个重载且**均不输出法线**；但 `TerrainPatch.RayCast` 有 4 个重载，其中 **`RayCast(Vector3, Vector3, out float hitDistance, out Vector3 hitNormal, float maxDistance)` 直接给出法线**（已同时经 `TerrainPatch.h` 头文件与 C# XML 导出面确认）。因此坡度获取约定为：
   - 路径 A：在高度场上做中心差分求法线（最省，且 `TerrainPatch.GetHeightmapData()` 已有邻域数据）。
   - 路径 B：**直接用 `TerrainPatch.RayCast` 的法线重载**，不必依赖 `RayCastHit` 是否被填充，也不必退化为差分。注意需先把点定位到所属 patch（`Terrain.GetPatch(Int2)`）再查询 —— `Terrain` 级 API 拿不到法线。
   - 不再需要「用 Y 方向近似法线」这种退化路径（那会让 `SlopeFilter` 在所有非平地失效）。
   - `SlopeFilter` 节点仍须记录法线来源，两条路径结果会有细微差异，测试需容差。
4. **缺失 patch。** `Terrain.GetPatch()` 可返回 null（未创建的 patch）。此时路径 A 应跳过该点或按参数回退到路径 B，不可崩。
5. **边界行为。** 采样点落在 patch 边缘时，双线性插值需要相邻 patch 的顶点。实现上取"patch 外一环"或对边缘做单侧插值；两者在视觉上难以区分，但会影响可复现性，需固定为其中一种并写入测试。
6. **多线程安全。** 路径 A 只读高度场数组，可安全并行（§4.4）；路径 B 的 `RayCast` 涉及物理/几何查询，**必须验证其线程安全性后再并行**，Phase 1 保守地在主线程执行，仅在压测通过后放开。

---

## 4. 执行引擎设计

### 4.1 执行模型

执行引擎采用 **拉取式（Pull-based）** 拓扑执行：

1. 从输出节点（Spawner）开始，递归拉取上游节点数据。
2. 每个节点缓存本次执行的结果，避免重复计算。
3. 执行过程支持 **分帧（Time-sliced）** 以保持编辑器响应性。

> **注 5（v1.1 新增）：拉取式 + 递归需要显式环检测。**
> v1.0 未提环。节点图允许用户连出环（A→B→A），递归拉取会直接栈溢出崩溃，而这是编辑器里几秒钟就能制造出来的操作。必须在"编译"阶段（§4.5）做一次 DFS 三色标记检测环，发现环时拒绝执行并在编辑器中标红涉事节点与连线。**不要**依赖运行时深度限制来兜底。

### 4.2 执行上下文

```csharp
public enum PCGDebugMode
{
    None,
    Points,          // 仅点位置
    PointsDensity,   // 点位置 + 密度着色
    Bounds,          // 仅包围盒
    Full,            // 全部
}
```

（`PCGExecutionContext` 定义见 §3.4。）

### 4.3 节点执行接口

```csharp
public abstract class PCGNodeBase
{
    /// <summary>节点类型标识，与 NodeArchetype.TypeID 对应。</summary>
    public abstract string TypeId { get; }

    /// <summary>执行。inputs 长度与 InputPins 一致，已连线的为数据，未连线的为 null。</summary>
    public abstract PCGData Execute(PCGExecutionContext context, PCGData[] inputs);

    public abstract PCGPinDefinition[] InputPins { get; }
    public abstract PCGPinDefinition[] OutputPins { get; }

    /// <summary>节点是否可并行执行（§4.4）。</summary>
    public virtual bool IsThreadSafe => false;
}

public class PCGData
{
    public PCGPoint[] Points;
    public PCGAttributeSet SharedAttributes; // 可空，支持属性继承
}
```

> **注 6（v1.1 新增）：节点实例的构造方式。**
> 节点需要从 `PCGNode.Parameters`（序列化字典）构造出执行体。v1.0 未定义此映射。做法：注册表保存 `(TypeId → NodeArchetype + Func<PCGNode, PCGNodeBase>)`，由工厂从序列化数据建实例，并在建实例时做一次参数校验（缺失/类型错配），错误在编辑器内以节点角标提示而非运行时抛异常。

### 4.4 执行性能策略

- **多线程采样**：采样器节点使用 `Task.Run` 并行生成点。**约束：只有 `IsThreadSafe == true` 的节点可并行。** 路径 A 地形采样可并行；`RayCast` 路径需先验证（§3.5 第 6 点）。
- **惰性属性求值**：属性仅在节点实际需要时计算。
- **增量执行**：当图表中仅参数变化时，仅重新执行受影响的下游节点（脏标记传播）。
- **分块处理**：对于大规模生成，将点数据分块（如每 1000 点）处理，降低峰值内存。

> **注 7（v1.1 新增）：增量执行与确定性 RNG 的耦合。**
> 「增量执行」要求"只重算受影响的下游节点"，而随机偏移类节点如果用顺序 RNG，重算一个子集会得到与全量执行不同的结果 —— 视觉上表现为"改了个参数，别的地方的点跳了一下"。这正是 §3.4 要求 `PCGDeterministicRng` 由 `(seed, nodeId, pointIndex)` 派生而非顺序调用的原因。**若采用顺序 RNG，必须放弃增量执行，否则结果不确定。** 二者是绑定的，不可分开取舍。
>
> **注 8（v1.1 新增）：`CancelToken` 需要在长循环内轮询。**
> `CancellationToken` 只在被检查时有意义。并行节点需在每个点的循环体中周期性（如每 256 点）检查 `CancelToken.IsCancellationRequested`，否则"图表变更时取消上一次执行"（§6.3）不会真正中断，快速拖动参数会堆积多个跑满的旧任务。

### 4.5 图表编译（v1.1 新增章节）

v1.0 缺失"编译"这一层，但它承担三项不可省略的职责。建议 `PCGGraphCompiler.Compile(PCGGraphAsset) → PCGCompiledGraph | PCGCompileError[]`：

| 职责 | 说明 | 不做会怎样 |
|---|---|---|
| **环检测** | DFS 三色标记，见注 5 | 栈溢出崩溃 |
| **拓扑排序 + 执行计划** | 生成执行顺序与依赖表，供缓存/脏标记/分帧使用 | 每次执行都要重新推导依赖 |
| **参数校验** | 校验 `Parameters` 是否满足各节点声明；校验暴露参数定义的一致性 | 运行时才抛异常 |
| **（可选）类型检查** | 校验引脚连接的类型兼容性 | 连错线到运行时才炸 |

编译产物带**结构哈希**：节点拓扑 + 连线 + 非暴露参数参与哈希。参数值变化**不**改变哈希 → 触发"仅参数变化 → 增量执行"；拓扑/连线变化 → 哈希变化 → 全量重编译与重执行。这为 §4.4 的增量执行提供了明确的判定依据。

> **注 9（v1.1 新增）：暴露参数的覆盖不能改变结构哈希。**
> 否则"同一个图表被两个 PCGVolume 用不同参数复用"（§5.2 的核心价值）会导致每次实例参数不同就要全量重编译，复用机制的性能收益归零。参数覆盖必须走**独立的实例化参数表**，在编译产物之外叠加。

---

## 5. 节点库设计

### 5.1 节点分类与完整清单

#### 5.1.1 输入节点（Sampler）

| 节点 | 功能 | Flax API 依赖（已核验） |
|---|---|---|
| **Landscape Sampler** | 从地形表面采样点 | 双路径，见 §3.5 |
| **Spline Sampler** | 沿样条线均匀/随机采样 | `Spline.GetSplineTransform(float)` ✅ |
| **Volume Sampler** | 在体积内随机放置点 | `BoundingBox` + `PCGDeterministicRng` |
| **Noise Sampler** | 基于噪声密度生成点 | `PCGSeededNoise`（**非**引擎噪声，见 §3.4） |
| **Point Sampler** | 从输入点重新采样 | 已有 PointData 的偏移复制 |
| **Mesh Sampler** | 在模型表面采样 | 模型三角形采样（Phase 3） |

**Landscape Sampler 详细设计：**
- 输入：`Bounds`（采样范围）、`PointCount`（目标点数）、`Jitter`（位置抖动）。
- 逻辑：在 Bounds 的 XZ 平面均匀或 Poisson 分布随机采样，对每个采样点按其 `SampleMode` 查询地形高度，生成 PCGPoint。
- 参数：`SampleMode`（§3.5）、`HeightOffset`、`AlignToNormal`、`FallbackToRayCast`、`HeightScale`/`HeightOffset`（标定值）。
- 输出：点在 Y 上贴合地形；`AlignToNormal` 时旋转对齐地形法线；同时写入 `$Slope` 属性，供下游 `SlopeFilter` 复用，避免重复求法线。

**Spline Sampler 详细设计：**
- 输入：`SplineActor` 引用（**注意类型**）、`Spacing`（间距）、`Offset`（横向偏移范围）。
- 逻辑：沿样条长度等距或随机采样 `t` 值，调用 `GetSplineTransform(t)` 获取位置和朝向，可选在法线方向随机偏移。
- 输出：沿样条分布的点，旋转对齐切线方向。
- 可用辅助 API：`GetSplineDirection(t)`、`GetSplineSegmentLength(i)`、`GetSplineTime(i)`、`GetSplineTimeClosestToPoint(v)`、`GetSplinePoint(t)`、`GetSplineOrientation(t)`、`GetSplineScale(t)`。
- **按弧长均匀采样**：`t` 是参数而非弧长，直接等分 `t` 会导致稀疏段/密集段。需用 `GetSplineSegmentLength(i)` 累积建弧长表后按弧长反解 `t`。

#### 5.1.2 筛选节点（Filter）

| 节点 | 功能 | 实现逻辑 |
|---|---|---|
| **Density Filter** | 按密度阈值筛选 | `Density >= Min && Density <= Max` |
| **Slope Filter** | 按地形坡度筛选 | 优先复用 `$Slope` 属性；缺失时按采样模式求法线 |
| **Height Filter** | 按高度范围筛选 | `Position.Y` 范围判断 |
| **Bounds Filter** | 按包围盒筛选 | 剔除 Bounds 外的点 |
| **Attribute Filter** | 按自定义属性筛选 | 比较属性值与阈值 |
| **Distance Filter** | 按到参考点距离筛选 | 到指定点的距离范围 |
| **Self-Pruning Filter** | 剔除重叠点 | 基于 `Bounds` 重叠检测（**AABB 语义**，见 §3.1 注 1） |

**Self-Pruning Filter 的性能说明：** 朴素两两比较是 O(n²)，1 万点即 5000 万次比较，不可接受。实现必须用空间哈希网格（以点包围盒最大边长为格子尺寸，每点只与邻格比较）。这是该节点唯一可行的实现方式。

#### 5.1.3 修改节点（Modifier）

| 节点 | 功能 |
|---|---|
| **Transform Points** | 平移/旋转/缩放点 |
| **Set Density** | 设置密度值 |
| **Density Remap** | 密度曲线重映射 |
| **Set Attribute** | 写入自定义属性 |
| **Copy Attribute** | 复制属性值 |
| **Random Offset** | 随机位置偏移（**必须用 `PCGDeterministicRng`**，见注 7） |
| **Align to Surface** | 对齐到地形法线 |

#### 5.1.4 组合节点（Combinator）

| 节点 | 功能 |
|---|---|
| **Merge Points** | 合并多个点数据集 |
| **Difference** | 从集合 A 中剔除集合 B（按 Bounds） |
| **Intersection** | 保留 A 和 B 中重叠的点 |
| **Subgraph** | 引用另一个 PCG 图表作为子节点 |

> **注 10（v1.1 新增）：`Merge Points` 的属性集冲突处理。**
> 两个输入数据集的属性集可能属性名相同、类型不同，或一方缺失。v1.0 未定义。约定：并按属性名求并集；同名同类型则拼接；同名不同类型 → 编译期报错并标红；一方缺失 → 以类型默认值填充并**不**自动补属性（避免静默产生误导性的 0 值）。此规则需写入用户文档。

> **注 11（v1.1 新增）：`Subgraph` 的环风险。**
> 子图表引用使环可以跨资产形成（图表 A 引用 B，B 引用 A）。环检测必须作用于**展开后的全局图**，而非单个资产。这要求子图表展开有最大深度限制。

#### 5.1.5 输出节点（Spawner）

| 节点 | 功能 | Flax API（已核验） |
|---|---|---|
| **Spawn Static Model（少量）** | 逐点生成 `StaticModel` Actor | `new StaticModel { Model = ... }` + `Level.SpawnActor(actor, parent)` ✅ |
| **Spawn Static Model（批量）** | 单 Actor 承载大量实例 | ⭐ `ModelInstanceActor`：`Entries` 集合 + `MeshReference.{Actor, MeshIndex, LODIndex}` + `ModelInstanceEntry.{Material, Visible, ShadowsMode, ReceiveDecals}` ✅ |
| **Spawn Foliage** | 添加植被实例 | `Foliage.AddFoliageType(Model)` → `AddInstance(ref FoliageInstance)` → `RebuildClusters()` ✅ |
| **Spawn Actor** | 生成任意 Actor | `Level.SpawnActor(Actor)` / `Level.SpawnActor(Actor, Actor parent)` ✅ |
| **Spawn Decal** | 生成贴花 | ⚠️ **`Decal` Actor 存在**（v1.1 判断有误）：`new Decal` + `Material` / `Size`(`Vector3`) / `SortOrder` / `RenderLayersMask` / `DrawMinScreenSize` 全量可用。**推迟到 Phase 3** 的原因是「无批量实例化路径」而非「做不出来」，详见 §10 风险表 |

**批量生成的性能分水岭（v1.1 修正）：** v1.0 只有"逐点建 `StaticModel`"一条路。按 5 万个点估算：5 万个 Actor 会带来巨大的场景序列化体积、编辑器 UI 卡顿和 draw call 压力。**约定：点数 > 500 时默认走 `ModelInstanceActor` 路径，< 500 时走独立 Actor 路径，阈值可由节点参数覆盖。** 这是 Phase 1 就要定下的架构选择，事后改代价高。

**反射核验的类型细节（实现时直接照用）：**

| 成员 | 实际类型（Flax 1.12.6913） | 备注 |
|---|---|---|
| `Level.SpawnActor(Actor)` / `(Actor, Actor parent)` | → **`bool`**（成功与否） | **必须检查返回值**，v1.0 未提。生成失败（如父级未就绪）会静默丢对象 |
| `ModelInstanceActor.Entries` | **`ModelInstanceEntry[]`**（数组，非 `List<T>`） | 批量填充需先算好数量再一次性赋值，不能 `Add` |
| `ModelInstanceActor.MeshReference` | `{ Actor, MeshIndex, LODIndex }` | `Actor` 字段承载每个实例的 `Transform` |
| `ModelInstanceEntry` | `{ Material, Visible, ShadowsMode, ReceiveDecals }` | 逐实例外观控制 |
| `FoliageInstance` | `{ Transform : Transform, Type : int, Random : float, Bounds : BoundingSphere }` | ⚠️ `Bounds` 是 **`BoundingSphere`**，不是 `BoundingBox` —— 与 `PCGPoint.Bounds`（AABB）不同类型，Foliage Spawner 需做转换 |
| `Foliage.AddInstance(ref FoliageInstance)` | → 实例索引 | 之后可用 `Foliage.SetInstanceTransform(int, Transform)` 增量更新 |
| `Transform` 字段 | `Translation : Vector3`、`Orientation : Quaternion`、`Scale : Float3` | 见 §3.1 注 1 |
| `Actor.PerInstanceRandom` | `float` | 可直接用作逐实例随机，无需自建字段 |

`FoliageInstance.Random` 与 `Actor.PerInstanceRandom` 均可承载逐实例随机以供材质/着色器使用；`ModelInstanceEntry` 无此字段，若批量路径需要逐实例随机，应写入 `PCGAttributeSet` 并由 Spawner 节点选择目标表现（如按随机值选材质）。

### 5.2 节点参数暴露

每个节点可将其内部参数标记为 **Exposed**，在 `PCGGraphAsset` 层面形成统一参数列表。PCGVolume 可通过参数覆盖机制覆盖这些值，实现同一图表在不同组件上的复用。

```csharp
[AttributeUsage(AttributeTargets.Field)]
public class PCGExposedParameterAttribute : Attribute
{
    public string DisplayName;
    public object DefaultValue;
}
```

**覆盖机制的执行流程（v1.1 补充）：**

```
PCGGraphAsset.SurfaceData ──编译──> PCGCompiledGraph（结构哈希 H）
                                        │
PCGVolume.ParameterOverrides ───────────┼──> PCGExecutionPlan（实例化参数表，不参与 H）
                                        │
PCGExecutionContext.Seed ────────────────┘
```

多层覆盖优先级：节点默认值 < 图表暴露参数默认值 < `PCGVolume.ParameterOverrides` < 全局种子派生（仅影响随机类参数）。**覆盖表不参与结构哈希**（见注 9）。

---

## 6. 图表编辑器设计

### 6.1 编辑器架构（v1.1 重大修正）

> **v1.0 方案已废弃。** v1.0 计划参照 Flax 粒子函数编辑器**自研**画布（`Canvas` + `NodeWidget` + `EdgeWidget` + `GridRenderer`），并在 §10 把"编辑器开发量大"列为风险。核验后确认：**Flax 1.12 的 `FlaxEditor.Surface.VisjectSurface` 就是引擎自带的通用节点图框架**，粒子和动画图都在其上构建，且公开可继承。自研画布会把风险表里那条"复用 UI 组件"的缓解措施真正落实的成本从数周降到数天。

**修正方案：PCG 图表编辑器派生自 `VisjectSurface`。**

已核验可用成员（`FlaxEditor.Surface` 命名空间）：

| 类型 | 关键成员 | 用途 |
|---|---|---|
| `VisjectSurface` | `Load` / `Save` / `Context` / `CreateContext` / `ChangeContext` / `ShowPrimaryMenu` / `ConnectingStart` / `ConnectingOver` / `ConnectingEnd` / `IsPrimaryMenuOpened` / `OnShowPrimaryMenu` / `OnPrimaryMenuButtonClick` / `IsTypeCompatible` / `CanUseDirectCast` / `Copy` | 画布、连线、右键菜单、缩放平移、复制粘贴 |
| `NodeArchetype` | `TypeID` / `Title` / `SubTitle` / `Size` / `Flags` / `Create` / `Signature` / `Description` / `AlternativeTitles` / `IsInputCompatible` / `IsOutputCompatible` / `GetInputOutputDescription` | 声明一个 PCG 节点类型 |
| `GroupArchetype` | `GroupID` / `Name` / `Color` / `Tag` / `Archetypes` | 节点分类（Sampler / Filter / Modifier / Combinator / Spawner） |
| `SurfaceNode` | `Title` / `Type` / `ID` / `Values` / `Elements` / `Archetype` / `AddBox` / `AddElement` / `ResizeAuto` | 节点 UI 单元 |
| `AssetEditorWindow` | `ToolStrip` / `Item` / `IsEdited` / `SerializationTypename` / `WindowTitleName` | 资产编辑器窗口基类 |

**收益：** 节点拖拽、连线、框选、缩放平移、撤销/重做、复制粘贴、右键节点搜索菜单——**全部由 `VisjectSurface` 提供**，无需实现。v1.0 §6.2 的交互清单基本可以直接移除，只需为 PCG 节点类型提供 `NodeArchetype` 表。

**工作量重估：** v1.0 §10 中"图表编辑器开发量大 / 交付延迟"的风险等级由 **高降至中低**；Phase 2 的编辑器部分从"数周"降为"数天（主要为 `NodeArchetype` 表与参数编辑器的对接）"。

**新增的实现约束：** `VisjectSurface` 使用 `Values`（`object[]`）而非 v1.0 的 `Dictionary<string, object>` 承载节点参数，且引脚是**位置索引**而非 v1.0 的 `string PinName`。因此 §3.3 的数据模型需调整：

```csharp
public class PCGNode
{
    public string NodeTypeId;      // → NodeArchetype.TypeID
    public Vector2 EditorPosition;
    public object[] Values;        // ← 修正：Visject 的 Values 数组，替换原 Dictionary
}

public class PCGEdge
{
    public int OutputNodeIndex;
    public int OutputBoxIndex;     // ← 修正：引脚索引，替换原 string OutputPinName
    public int InputNodeIndex;
    public int InputBoxIndex;      // ← 修正：引脚索引，替换原 string InputPinName
}
```

> **保留自研的理由（若将来需要）：** 仅当需要 `VisjectSurface` 无法表达的特性时才考虑，例如节点的实时数据缩略图内嵌到节点体上。即使如此，也应先在 `SurfaceNode` 层面扩展，而非换掉整个画布。

### 6.2 节点 UI 交互

由 `VisjectSurface` 原生提供，无需自实现：添加节点（右键菜单）、拖拽节点、创建连线、删除连线、框选、缩放、平移、撤销/重做、复制粘贴。

**需自行实现的部分：**
- `NodeArchetype` 表（每个 PCG 节点一条）
- 各节点的参数编辑器（`Values` 索引 ↔ 参数语义的映射）
- 暴露参数面板（`ExposedParameters` 的编辑 UI）
- 执行/预览触发按钮（挂在 `AssetEditorWindow.ToolStrip` 上）

### 6.3 实时预览机制

当图表发生变化时：

1. 计算结构哈希（§4.5）。哈希未变 → 走增量执行；已变 → 全量重编译。
2. 启动 **防抖计时器（Debounce，200ms）**。
3. 计时器到期后，取消上一次未完成执行（**依赖注 8 的 `CancelToken` 轮询**），在后台线程执行图表。
4. 执行结果通过 **主线程调度** 更新到场景。
5. 生成的点以 **DebugDraw** 方式预览。

> **注 12（v1.1 新增）：主线程调度是硬约束。**
> Flax 的 Actor 创建/销毁/变换写入、`Foliage.AddInstance`、`DebugDraw` 都必须在主线程。后台线程只能产出 `PCGData`，不得触碰场景。执行结果需通过一个主线程队列（在 `Editor.EditorUpdate` 或 `Script.Update` 中排空）交付。v1.0 提到"通过主线程调度更新到场景"但未说明这是**唯一合法路径**，实现时容易在并行采样节点里顺手 `SpawnActor` 而引入随机崩溃。

### 6.4 调试可视化（v1.1 修正）

> **v1.0 方案有性能缺陷。** v1.0 写"生成的点以 DebugDraw 方式预览（小十字/球体）"。逐点调用 `DebugDraw.DrawSphere` 或 `DrawWireSphere` 意味着每点一次绘制调用——1 万个点就是 1 万次调用，编辑器会直接卡死。

**修正方案：用批量 API 一次提交。**

已核验可用（`FlaxEngine.DebugDraw`，签名经反射确认）：

| API | 用途 |
|---|---|
| ⭐ `DrawLines(Double3[] lines, Matrix transform, Color color, float duration, bool depthTest)` | **批量绘制所有点为十字** —— 每 2 个元素是一条线段；一个点画成十字需 4 个顶点（见注 17），1 万点 = 1 次调用 |
| ⭐ `DrawLines(Float3[] lines, Matrix transform, Color color, float duration, bool depthTest)` | 同上，半精度版本（内存减半，适合超大点集预览） |
| ⭐ `DrawLines(GPUBuffer lines, Matrix transform, float duration, bool depthTest)` | GPU 缓冲版本（最高吞吐，需自管缓冲生命周期） |
| `DrawLine(Vector3, Vector3, Color, float, bool)` / `DrawLine(Vector3, Vector3, Color, Color, float, bool)` | 单线（少量点或连线高亮） |
| `DrawWireBox(BoundingBox, Color, float, bool)` / `DrawBox(BoundingBox, ...)` | Spawner 节点选中时绘制生成对象包围盒 |
| `DrawBox(OrientedBoundingBox, ...)` / `DrawWireBox(OrientedBoundingBox, ...)` | 旋转包围盒（若 §3.1 启用 OBB 剪枝） |
| `DrawWireSphere(BoundingSphere, Color, float, bool)` / `DrawSphere(BoundingSphere, Color, float, bool)` | 少量点或半径指示（逐点调用，仅限小规模） |
| `DrawCircle(Vector3, Float3 normal, float radius, Color, float, bool)` | 样条采样可视化 |

> **注 17（v1.1 新增）：`DrawLines` 的参数陷阱。**
> `DrawLines` 的 `lines` 是**扁平顶点数组**（每 2 个元素组成一条线段），不是"点数组"。因此把 N 个点画成十字，需要生成 `4N` 个顶点（两条线段：X 向 + Z 向）。缓冲大小应为 `4 * N`，且**必须在生成前按 §6.4 的抽样上限裁剪**，否则 5 万点会瞬间分配 20 万个 `Double3`（约 4.8 MB）并每帧重建 —— 应复用缓冲并仅在数据变化时重建。

**关键约定：**
- 所有点积攒到 `Double3[]` 缓冲，**单次** `DrawLines` 提交。缓冲复用（不每次分配）。
- **密度着色**：`DrawLines` 的 `Color` 是整体单色，无法逐点着色。密度着色需要**按密度分档**：把点分成 4-8 个密度档，每档一次 `DrawLines`。这样调用次数从 O(n) 降到 O(档数)。放弃"逐点连续渐变色"。
- **点数上限保护**：`MaxDebugPoints`（默认 20000），超过则按步长抽样绘制，并在 UI 上提示"仅显示 1/4 采样点"。不做上限保护的话，大规模图表一改参数编辑器就假死，用户会以为程序崩了。
- **密度色阶**：低密度偏蓝 `Color(0.2, 0.4, 1.0)` → 高密度偏红 `Color(1.0, 0.3, 0.2)`。
- **连线数据流高亮**：执行预览时活跃路径的连线高亮 —— 由 `VisjectSurface` 的连线渲染配合节点高亮实现。

---

## 7. 场景集成（PCG 组件）

### 7.1 PCGVolume Actor

```csharp
public class PCGVolume : Actor
{
    [Tooltip("引用的 PCG 图表资产")]
    public PCGGraphAsset Graph;

    [Tooltip("生成范围（本地空间，随 Actor 变换）")]
    public BoundingBox Volume = new BoundingBox(new Vector3(-500), new Vector3(500));

    [Tooltip("图表暴露参数的覆盖值")]
    public PCGParameterOverride[] ParameterOverrides;

    [Tooltip("随机种子")]
    public int Seed = 0;

    [Tooltip("生成模式")]
    public PCGGenerationMode Mode = PCGGenerationMode.Bake;

    // 运行时
    private PCGGenerationResult _result;

    public void Generate();
    public void Clear();
}
```

> **注 13（v1.1 新增）：`Volume` 与 Actor 变换的关系。**
> v1.0 未定义 `Volume` 是本地空间还是世界空间。必须是**本地空间**（随 Actor 移动/旋转），否则移动 PCGVolume 时生成范围不动，是明显的易用性缺陷。同理，`$Position` 必须是**世界空间**（与 §3.1 一致），在生成时由 Actor 变换一次性烘焙进点数据（`Bounds` 亦同），故生成结果**不会**随 Actor 移动而自动更新——需在 Actor 变换变化时触发重生成（脏标记），或明确告知用户"Bake 后移动 Volume 不重新生成"。

### 7.2 生成模式

| 模式 | 说明 | 适用场景 |
|---|---|---|
| **Bake** | 编辑器内执行，结果固化为静态 Actor，图表不再关联 | 最终生产输出 |
| **Runtime** | 运行时按需执行，结果动态生成 | 程序化关卡、动态世界 |
| **Preview** | 编辑器内执行，仅调试绘制，不生成实际 Actor | 图表迭代 |

**Bake 模式的实现要点（v1.1 实现期修正）：**
- 执行后，将 Spawner 节点生成的 Actor **parent 到 `PCGVolume` 自身之下**。Spawner 节点在场景根创建对象、并不知道是哪个 Volume 请求的执行，因此所有权在 Volume 侧通过 `Scene.GetChildren<Actor>()` 扫描 + 命名前缀 `PCG_Gen_` 识别后建立。
- 场景的 `Level.SaveScene(Scene, bool)` 会序列化所有生成对象。
- 图表引用可选择性保留（用于后续重新生成）或清除（减少依赖）。
- **清理即"销毁我的子级"**。`PCGVolume.Clear()` 遍历 `GetChildren<Actor>()` 并 `Actor.Destroy(...)`，然后 `PCGVolume.Generate()` 在重新生成前先调用它，确保不会叠加第二份输出。
- **清理必须彻底**：逐点路径下需销毁 N 个 Actor。

> **注 18（v1.1 实现期新增）：为什么不用标签（Tag）回收输出。**
> 本设计初稿建议用"稳定命名 + 标记"让重新生成能定位并清理上一次结果。实测失败：`Level.FindActors` 的签名是 `FindActors(Tag, bool, Actor)`，而 Flax 的 `Tag` 是**工程标签表的索引值**（只有 `Tag(uint index)` 构造函数），**无法由任意字符串在运行时构造**。因此"给每个 Volume 铸一个唯一标签再按标签查询"这条路在本引擎上不成立。
>
> 改用**结构性所有权**：父子关系本身就是查询，且随场景保存，所以即使脚本重载或场景重新加载导致内存侧表丢失，`Clear()` 依然能精确回收本 Volume 的输出。这比标签方案更可靠，也无需引入任何运行时索引。
>
> 命名前缀 `PCG_Gen_` 仍保留，但只承担两个角色：outliner 可读性，以及初次识别"这是本插件生成的"。

### 7.3 分区生成（Phase 3 扩展）

参照 UE5 PCG 的分区生成模式，将生成范围划分为网格，每个单元格独立执行：

- `PCGWorldActor` 管理全局分区网格大小。
- `PCGVolume` 在单元格粒度上调度生成任务。
- 支持 **分层网格**：大网格放大型资产，小网格放细节资产。
- 分区带来的新问题（v1.1 补充）：**跨格点的一致性问题**。噪声采样本身无状态，跨格一致；但 Poisson 分布、Self-Pruning、Worley 特征点都是邻域相关的，分区独立执行会在格子边界出现可见接缝。解决方式是为每格生成时**外扩一圈（padding）**，生成后裁剪到格内。`PCGWorldActor` 需暴露 `CellPadding` 参数。

---

## 8. 插件结构与生命周期

### 8.1 插件类型

同时提供 **Game Plugin** 和 **Editor Plugin**，参照仓库内既有插件（`HundunWorld\Plugins\NarrativePro` 为 `GamePlugin`，`HundunWorld\Plugins\HundunAgent` 为 `EditorPlugin`）。

```csharp
// Game Plugin：运行时 PCG 执行引擎
public class PCGRuntimePlugin : GamePlugin
{
    public PCGRuntimePlugin()
    {
        _description = new PluginDescription
        {
            Name = "PCG",
            Category = "Procedural",
            Author = "HundunWorld",
            Description = "PCG - 程序化内容生成",
            Version = new Version(1, 0),
            IsAlpha = true,
        };
    }

    public override void Initialize() { /* 初始化节点注册表 */ }
    public override void Deinitialize() { /* 释放注册表 */ }
}

// Editor Plugin：图表编辑器与调试工具
public class PCGEditorPlugin : EditorPlugin
{
    public override void Initialize()
    {
        base.Initialize();
        // 注册节点 Schema
        // 注册 EditorWindow 工厂
    }

    public override void InitializeEditor()
    {
        base.InitializeEditor();
        // 菜单按钮（参考 HundunAgentPlugin 的 TryAddMenuButton 重试模式）
        // 注册调试绘制
    }

    public override void DeinitializeEditor() { /* 清理 UI 资源、退订事件 */ }
    public override void Deinitialize() { /* 清理静态缓存 */ }
}
```

> **注 14（v1.1 校正）：插件描述字段与基类。**
> Flax 的插件基类是 `Plugin`（提供 `Initialize()` / `Deinitialize()` 与受保护的 `_description` 字段），`GamePlugin` 与 `EditorPlugin` 均派生自它。`PluginDescription` 需在构造函数中赋值（见 `HundunAgentPlugin.cs` 的实际做法）。`EditorPlugin` 额外提供 `InitializeEditor()` / `DeinitializeEditor()` 与 `Editor` 属性。
>
> **注 15（v1.1 校正）：菜单注册 API。**
> v1.0 写 `Editor.UI.ToolStrip.AddButton()`。实际是两件事：**菜单按钮**用 `Editor.UI.AddMenuButton(菜单名, 按钮名, 回调)`；**工具栏**用 `FlaxEditor.GUI.ToolStrip`（注意命名空间是 `FlaxEditor.GUI`，不是 `Editor.UI`）的 `AddButton(SpriteHandle, string, Action)` / `AddButton(string, Action)`。`HundunAgentPlugin` 使用前者并带了 UI 未就绪时的重试逻辑 —— 该重试模式应沿用（`InitializeEditor` 时 UI 可能尚未构建完成）。
>
> **注 16（v1.1 新增）：`InitializeEditor` 时序。**
> 编辑器 UI 在 `InitializeEditor` 阶段可能尚未就绪（`Editor?.UI == null`）。必须照 `HundunAgentPlugin` 的做法：先试一次，失败则订阅 `Editor.EditorUpdate` 重试，成功后立即退订。不做这个兜底会导致插件在部分启动路径下静默丢失菜单入口。

### 8.2 脚本重载安全

Flax 编辑器在脚本重载时会销毁所有脚本实例。PCG 插件需在重载前后释放/重建缓存的类型信息与委托。

已核验的事件：

| 事件 | 位置 | 适用侧 |
|---|---|---|
| ✅ `ScriptsBuilder.ScriptsReloadBegin` / `ScriptsReload` / `ScriptsReloadEnd` / `ScriptsReloadCalled` | `FlaxEditor.ScriptsBuilder` | 编辑器侧（v1.0 引用正确） |
| ⭐ `Level.ScriptsReloadStart` / `ScriptsReload` / `ScriptsReloaded` / `ScriptsReloadEnd` | `FlaxEngine.Level` | **运行时侧（v1.1 新增）** |

**必须清理的静态状态清单（v1.1 新增，v1.0 仅泛泛提及）：**
- 节点注册表（`TypeId → NodeArchetype` / 工厂委托）—— 委托指向旧程序集中的类型，不清理会导致重载后调用旧代码或 `MissingMethodException`
- 已编译图表的缓存（含编译产物中的委托/反射句柄）
- 手动创建的 `Task` / `CancellationTokenSource` —— 必须取消并等待收尾，否则可能有后台线程仍持有旧类型
- DebugDraw 缓冲等静态缓存
- 编辑器菜单/工具栏按钮与事件订阅（在 `DeinitializeEditor` 中退订）

**明确不建议做的：** 不要在重载期间尝试保持图表执行状态。Flax 重载语义是"所有脚本实例被销毁"，带状态的顽强保留会引入难以调试的跨重载状态污染。正确做法是重载后从图表资产**重新编译**，用资产数据（而非内存状态）作为唯一真相来源。

---

## 9. 实施路线图

### Phase 1：数据层与执行引擎（核心闭环）

**交付物：**
- `PCGPoint`、`PCGAttributeSet`（含 COW，写屏障名为 `Detach()` 而非初稿的 `EnsureOwned()`，见 §3.2 注 3）、`PCGData`。
- `PCGSeededNoise` + `PCGDeterministicRng`（§3.4）—— **前置于所有节点**，因为所有随机类节点都依赖它。
- 图表编译器：环检测、拓扑排序、结构哈希（§4.5）。
- 拉取式执行引擎 + 节点结果缓存。
- 地形采样双路径 + `PCGTerrainProbe.Calibrate()`（§3.5）。
- 3 个节点：`Landscape Sampler`、`Density Filter`、`Spawn Static Model`（含批量/逐点双路径）。
- 简易的 `PCGVolume` Actor（仅代码生成，无编辑器 UI）。
- 图表载体：先用 `JsonAsset` 承载拓扑（注 4）。

**验证标准：** 通过代码构建图表，在地形上生成石头；同一 `Seed` 重复执行结果逐位一致；`SampleMode` 两条路径的高度差在校准后 < 1e-3；**`PCGSeededNoise` / `PCGDeterministicRng` 的黄金值测试入仓并锁定输出**（这两项是所有随机类节点的地基，一旦漂移会静默改变全部生成结果）。

> **v1.2 验证入口（已跑通）：** 黄金值测试不再需要启动 Flax 编辑器。`Tools/PCGGoldenRunner` 以 `Link` 方式引入 `PCGSeededNoise.cs` / `PCGDeterministicRng.cs` 的**真实源文件**并在无引擎环境下执行（两者均为纯数学，不依赖 `Debug.Log`）：
>
> ```powershell
> dotnet run --project Tools/PCGGoldenRunner/PCGGoldenRunner.csproj
> # == 23 passed, 0 failed ==
> ```
>
> 存在的必要：`Tools/PCGVerify` 是 `OutputType=Library`，**只证明能编译，一行代码都没执行过**；而黄金值的意义恰好在于“有人真的跑过算法并读到了数”。两者不互为替代：PCGVerify 管编译，PCGGoldenRunner 管数值回归（建议进 CI），编辑器内的 `PCGSelfTest` 管需要真实场景的部分（地形标定、节点执行、Actor 生成）。

#### Phase 1 实测结果（v1.2，编辑器内 12 组全绿）

跑法：加载 `Maps/World.scene`（含 `FlaxEngine.Terrain`）→ 建一个 `EmptyActor` 挂 `PCGSelfTest` → **进入播放模式**（编辑器脚本重载只换程序集，**不会重跑 `OnStart`**）。

```
[PCG] PASS attribute copy-on-write isolation
[PCG] PASS structural hash distinguishes topology from parameters
[PCG] PASS terrain heightfield/raycast agreement
========== [PCG] self-test PASSED (12 group(s)) ==========
```

跑出的三个真实缺陷与处置：

| 缺陷 | 定性 | 处置 |
|---|---|---|
| COW 写入共享存储后原值被改 | **测试断言了不可实现的行为**：`Share()` 返回 `this`，而 `Detach()` 就地换列，同一实例上无法隔离（§3.2 注 3 的 COW 在单实例设计下结构性不可用） | 改测 `Clone()` 隔离（生产路径实际依赖的那条），共享重构跟 `MergePoints`/`SplitPoints` 一起在 Phase 2 做 |
| 结构哈希因“标记暴露”而改变 | **实现正确，测试断言写错了性质**：注 9 要保的是“暴露参数的**取值**不进哈希”，而不是“暴露与否不进哈希”（后者必然改变折叠输入集） | 改为断言取值变化不改哈希，实现未动 |
| 标量 setter 缺 `$` 前缀守卫 | **真 bug（本轮新发现）**：`SetFloat("$Density", ...)` 能通过 `ValidateIndex`（`TryGetDescriptor` 会解析内建通道），然后去索引自定义列 —— 集合已有 float 列时**静默 no-op**，没有列时抛 **NRE 穿透 `catch (PCGAttributeException)`** | 新增 `ValidateWritableIndex`（先 `ValidateWritableName` 再 `ValidateIndex`），8 个 setter 全部改走它；读侧保持可查 `$Density` |

另记一条工具链事实：**修改已存在的插件源文件能热重载，新增文件不行**（需要进工程文件）；而且**自检必须靠进入播放模式触发**，否则看起来“改了就生效”其实没重跑。

> **⚠️ “地形双路径一致”目前不能计为通过。** 标定日志输出 `raw heightmap is constant` —— `World.scene` 的地形是**全零平面**（方差≈ 0，走了退化分支）。此时 `scale=1`/`offset=0` 不是拟合出来的，而是“无法识别斜率”时的兜底值，两条路径都在返回 0，“差值 < 1e-3”平凡成立。**因此附录 B.2 的“高度场→世界 Y 换算语义”盲区仍未消除**，必须在一块有起伏的地形上重跑才算。

### Phase 2：编辑器与可视化

**交付物：**
- `PCGGraphEditorWindow : AssetEditorWindow`，内嵌 `PCGSurface : VisjectSurface`（§6.1，**工作量已大幅下降**）。
- 全部节点的 `NodeArchetype` 表 + 分组的 `GroupArchetype`（Sampler/Filter/Modifier/Combinator/Spawner）。
- 视口调试绘制（批量 `DrawLines` + 密度分档 + 点数上限保护，§6.4）。
- 暴露参数系统 + 覆盖表（不参与结构哈希，注 9）。
- 新增 5-8 个节点：`Spline Sampler`（含弧长表）、`Slope Filter`（法线取 `TerrainPatch.RayCast` 重载，§3.5 点 3）、`Transform Points`、`Merge Points`、`Random Offset`、`Self-Pruning Filter`（空间哈希）。

> **v1.2 移除项：** 原列入 Phase 2 的「原生 `PCGGraphAsset` 资产类型与双击打开」已移出路线图。原因：自定义资产类型注册完全在 C++ 侧，纯托管插件做不到（注 4）。图表长期以 `JsonAsset` 承载；双击打开由插件监听资产选择 + 自开 `PCGGraphEditorWindow` 实现，不需要原生资产类型。

**验证标准：** 设计师可在编辑器内创建图表，实时看到地形上生成的效果；1 万点预览不卡顿。

### Phase 3：生产就绪

**交付物：**
- 完整节点库（20+ 节点）。
- Bake 模式与 Runtime 模式（含结果容器的稳定命名与彻底清理）。
- 分区生成支持（含跨格 padding，§7.3）。
- 子图表（Subgraph，含跨资产环检测，注 11）。
- `Spawn Decal` 节点（**v1.2：从「不成立」改判为「可行但推后」** —— `Decal` Actor 确实存在；因无批量实例化路径、逐点即逐 Actor，需内置点数上限，且旋转必须对齐表面法线（贴花沿自身 -Y 投影）、材质域必须为 `Decal`（编译期校验）（§5.1.5）。
- 性能优化：并行采样（含 `RayCast` 线程安全验证）、增量执行、分块处理、`CancelToken` 轮询。

**验证标准：** 可用于中型关卡的程序化植被/岩石放置（10 万点量级，生成时间与内存有明确预算）。

---

## 10. 风险与缓解

| 风险 | 影响 | 概率 | 缓解措施 |
|---|---|---|---|
| **高度场数值语义未标定**（路径 A） | 静默生成错误高度的点，排查成本极高 | 中 | `PCGTerrainProbe.Calibrate()` 自动标定；未标定前强制走路径 B（§3.5 点 1）；路径 A/B 交叉验证测试 |
| **`RayCast` 路径线程安全性未知** | 并行采样导致随机崩溃 | 中 | Phase 1 保守单线程；先做压测与并发验证再放开（§3.5 点 6） |
| **COW 引用共享导致的静默别名 bug** | "改一个点，别的点跟着变"，极难定位 | **高** | 三条强制约定（§3.2 注 3）+ 断言（`_isShared` 时写操作必走克隆）+ 单元测试覆盖筛选/变换后写入场景 |
| **顺序 RNG 与增量执行冲突** | 结果不可复现，参数调整引发无关点跳动 | **高** | `PCGDeterministicRng` 由三元组派生（§3.4、注 7），二者不可分开取舍 |
| **节点图成环** | 递归拉取栈溢出崩溃 | **高**（用户几秒即可制造） | 编译期 DFS 三色标记环检测（注 5）；跨资产环检测（注 11） |
| **图表编辑器开发量大** | 交付延迟 | **低**（v1.1 由高下调） | 派生 `VisjectSurface`，复用引擎节点图框架（§6.1），交互全部原生提供 |
| **逐点 DebugDraw 卡死编辑器** | 1 万点即 1 万次绘制调用 | **高** | 批量 `DrawLines` + 密度分档 + `MaxDebugPoints` 上限与抽样提示（§6.4） |
| **逐点 SpawnActor 场景膨胀** | 大量 Actor 导致序列化体积、UI 卡顿、draw call 压力 | 中 | 点数 > 500 默认走 `ModelInstanceActor`（§5.1.5）；阈值可覆盖 |
| **属性系统内存开销** | 大量点时内存膨胀 | 中 | SoA 布局 + 属性共享 + COW；但共享仅限"产生新集合"的节点 |
| **编辑器脚本重载崩溃** | 开发体验差 | 中 | 监听 `ScriptsReloadBegin`（编辑器）+ `Level.ScriptsReload`（运行时）；按 §8.2 清单逐项清理静态状态 |
| **贴花无批量实例化路径** | 贴花逐点即逐 Actor，大规模时场景膨胀 | 中 | **v1.1 误判为「无 `Decal` Actor」，实际存在（`Level\Actors\Decal.h`）**，节点技术上可行；`ModelInstanceActor` 不适用贴花，故推迟到 Phase 3 并需来节点内置点数上限与法线对齐（§5.1.5） |
| **原生 `PCGGraphAsset` 对纯托管插件不可行** | 无法注册新资产类型 | **确定**（非风险，是硬约束） | Phase 1/2 均以 `JsonAsset` 承载；如需原生资产必须走 C++ 插件或引擎 fork，单独立项评估（注 4） |
| **节点生态空白** | 用户学习成本高 | 中 | 提供示例图表资产 + 内置常用预设 |
| **噪声值跨版本漂移** | 升级引擎后生成结果全变 | 低 | 插件自带 `PCGSeededNoise`（不依赖引擎噪声，§3.4）+ 黄金值单元测试 |

---

## 11. 关键设计决策总结

1. **属性系统采用 SoA + COW**：平衡灵活性与性能，属性名使用 `$` 前缀区分内置与自定义。**并强制 `EnsureOwned()` 收口所有写入路径**（注 3）。
2. **执行引擎采用拉取式拓扑 + 编译期环检测**：简化依赖管理，天然支持节点缓存；环检测放在编译层而非运行时（注 5）。
3. **插件自带种子化噪声，不依赖引擎 `Noise`**：引擎噪声无种子、仅 2D、返回类型不一，无法满足 PCG 的可复现性目标（§3.4）。**这是相较 v1.0 最重要的架构变更。**
4. **地形采样双路径，默认射线、可切高度场**：正确性优先、性能可选，并用自动标定 + 交叉验证消除高度语义的不确定性（§3.5）。坡度法线直接取 `TerrainPatch.RayCast` 的法线重载，不依赖 `RayCastHit`（v1.2）。
5. **编辑器派生 `VisjectSurface` 而非自研画布**：把最大的一项工作量风险转为复用引擎能力（§6.1）。**这是相较 v1.0 收益最高的变更。**
6. **确定性 RNG 由 (seed, nodeId, pointIndex) 派生**：使增量执行结果与全量执行一致（注 7），二者绑定。
7. **编辑器优先 Bake 模式**：降低运行时复杂度，符合关卡设计工作流。
8. **批量生成默认走 `ModelInstanceActor`（>500 点）**：在 Phase 1 就定下，事后改代价高（§5.1.5）。
9. **调试绘制批量提交 + 点数上限**：把 O(n) 绘制调用降为 O(档数），并保证大规模下的编辑器可用性（§6.4）。
10. **节点参数暴露机制**：实现图表复用，是"一个图表多场景使用"的关键；覆盖表不参与结构哈希（注 9）。
11. **分区生成作为 Phase 3**：先验证核心闭环，再扩展大规模场景能力；分区需处理跨格 padding 一致性（§7.3）。

---

## 附录 A：v1.0 → v1.1 勘误表

核验来源：`FlaxEngine.CSharp.xml`（Flax 1.12.6913 完整 API 文档）、`Source\Engine\Utilities\Noise.h`、`Source\Engine\Terrain\TerrainPatch.h`、`Source\Engine\Terrain\Terrain.h`、仓库内既有插件源码。

### A.1 会导致编译失败的（3 处）

| # | v1.0 | 实际 API | 修正位置 |
|---|---|---|---|
| 1 | `Noise.Perlin(...)` | `Noise.PerlinNoise(Float2)` | §5.1.1 |
| 2 | `Noise.Simplex(...)` | `Noise.SimplexNoise(Float2)` | §5.1.1 |
| 3 | `Editor.UI.ToolStrip.AddButton()` | 工具栏为 `FlaxEditor.GUI.ToolStrip.AddButton(...)`；菜单为 `Editor.UI.AddMenuButton(...)` | §8.1 注 15 |

### A.2 会导致设计无法实现的（4 处）

| # | v1.0 | 实际情况 | 修正位置 |
|---|---|---|---|
| 4 | `PCGExecutionContext.Seed` 为全局种子（暗示引擎噪声可种子化） | `Noise` 全部函数**无种子参数** | §3.4（新增章节，自带 `PCGSeededNoise`） |
| 5 | `Noise.Worley/Voronoi` 返回"噪声值"（标量） | `WorleyNoise` → `Float2`（F1,F2）；`VoronoiNoise` → `Float3`（minDist, randomColor, minEdgeDist） | §3.4 |
| 6 | Volume Sampler 用噪声做 3D 采样 | **无 3D Perlin/Simplex**，仅 `CustomNoise(Float3)` / `CustomNoise3D(...)` 且语义无文档 | §3.4 |
| 7 | `Terrain.GetHeightmapSize()` 采样地形高度 | 无 `Terrain.GetHeight()`；`TerrainPatch.Heightmap` 是 **GPU 纹理**不可直读；高度查询需走 `GetHeightmapData()`（需标定）或 `RayCast` | §3.5（新增章节） |

### A.3 命名/类型不精确（4 处）

| # | v1.0 | 实际 API | 修正位置 |
|---|---|---|---|
| 8 | `Terrain.GetHeightmapSize()` | 属性 `Terrain.HeightmapSize`（`int`） | §3.5 |
| 9 | `SetupHeightMap` | `Terrain.SetupPatchHeightMap(...)` / `TerrainPatch.SetupHeightMap(...)` | §2.1 |
| 10 | `Spline.GetSplineTransform(float time)` | ✅ 名称正确。但补注 `Spline`（结构体）与 `SplineActor`（Actor）是不同类型，且需按弧长采样 | §5.1.1 |
| 11 | `PCGPoint.Seed` 为 `long` | Flax RNG 体系用 `int`（`RandomStream(int)`） | §3.1 注 2 |

### A.3b 反射核验发现的类型细节（v1.1 新增，v1.0 未涉及）

以下由 .NET 反射直接读取 `FlaxEngine.CSharp.dll` 元数据得到，非文档推断。虽非 v1.0 的"错"，但都是实现时会踩的点：

| 项 | 实际类型 | 影响 |
|---|---|---|
| `Level.SpawnActor(...)` | 返回 **`bool`** | 必须检查返回值，否则生成失败会静默丢对象（§5.1.5） |
| `Terrain.RayCast` | 4 个重载；其中 3 个只给距离，仅 `RayCastHit&` 重载可能给法线 | 坡度计算不能想当然用射线法线（§3.5 点 3） |
| `TerrainPatch.Heightmap` | `Texture`（GPU） | 佐证路径 C 不成立（§3.5） |
| `TerrainPatch.GetHeightmapData()` | `float*` | 路径 A 的入口类型确认（§3.5） |
| `ModelInstanceActor.Entries` | `ModelInstanceEntry[]`（数组） | 批量填充须一次性赋值，不能 `Add`（§5.1.5） |
| `FoliageInstance.Bounds` | `BoundingSphere`（非 `BoundingBox`） | 与 `PCGPoint.Bounds` 类型不同，需转换（§5.1.5） |
| `Transform` 字段 | `Translation : Vector3`、`Orientation : Quaternion`、`Scale : Float3` | `PCGPoint` 的 `Position` 用 `Vector3`、`Scale` 用 `Float3`（§3.1 注 1） |
| `Actor.PerInstanceRandom` | `float` | 可复用为逐实例随机，无需自建（§5.1.5） |
| `DebugDraw.DrawLines` | `lines` 为**扁平顶点数组**（2 元素/线段） | 每点画十字需 4 个顶点；缓冲大小 `4N`（注 17） |

### A.4 未列错但需补充的架构级修正（6 处）

| # | 主题 | 修正 |
|---|---|---|
| 12 | 编辑器画布 | 自研 → 派生 `FlaxEditor.Surface.VisjectSurface`（§6.1） |
| 13 | 调试绘制 | 逐点 `DrawSphere` → 批量 `DebugDraw.DrawLines` + 密度分档 + 点数上限（§6.4） |
| 14 | 节点图成环 | 新增编译期环检测（注 5） |
| 15 | 增量执行与 RNG | 新增确定性 RNG 设计，二者绑定（注 7） |
| 16 | 批量生成 | 新增 `ModelInstanceActor` 路径与 500 点阈值（§5.1.5） |
| 17 | COW 别名 | 新增三条强制约定与 `EnsureOwned()` 收口（注 3） |

### A.5 附录中原表保留与更正

原 v1.0 附录"参考的 Flax API 关键接口"更正后：

| 功能 | 正确 API（Flax 1.12.6913） |
|---|---|
| 噪声采样 | `FlaxEngine.Utilities.Noise.PerlinNoise/SimplexNoise(Float2)`、`WorleyNoise(Float2)→Float2`、`VoronoiNoise(Float2)→Float3`、`CustomNoise(Float3)`、`CustomNoise3D(Float3[,octaves,roughness])` —— **均无种子，插件自带 `PCGSeededNoise`** |
| 种子化 RNG | `FlaxEngine.RandomStream(int)` / `.Initialize(int)` / `.GetFraction()` / `.RandRange(float,float)` / `.GetUnitVector()` / `.GetVector3()` |
| 样条变换查询 | `Spline.GetSplineTransform(float)`、`.GetSplineDirection(float)`、`.GetSplineSegmentLength(int)`、`.GetSplineTime(int)`、`.GetSplineTimeClosestToPoint(Vector3)` |
| 地形高度图尺寸 | `Terrain.HeightmapSize`（属性）；patch 尺寸 `Terrain.PatchSize`；patch 访问 `Terrain.HasPatch(Int2)` / `GetPatch(Int2)` / `GetPatchBounds(int, out BoundingBox)` |
| 地形高度查询 | `Terrain.RayCast(Vector3, Vector3, out float, out TerrainChunk, float)`、`TerrainPatch.RayCast(...)`（**含 `out Vector3 hitNormal` 重载，取坡度用它**）、`TerrainPatch.GetHeightmapData()` |
| 地形几何常量 | `Terrain.ChunksCount = 16`、`Terrain.ChunksCountEdge = 4`、`Terrain.Setup(lodCount = 6, chunkSize = 127)`、`Terrain.UnitsPerVertex` |
| 脚本重载事件 | 编辑器：`FlaxEditor.ScriptsBuilder.ScriptsReloadBegin/ScriptsReload/ScriptsReloadEnd`；运行时：`FlaxEngine.Level.ScriptsReload/ScriptsReloaded/ScriptsReloadEnd` |
| 编辑器菜单/工具栏 | `Editor.UI.AddMenuButton(string, string, Action)`；`FlaxEditor.GUI.ToolStrip.AddButton(SpriteHandle, string, Action)` |
| 节点图框架 | `FlaxEditor.Surface.VisjectSurface`、`NodeArchetype`、`GroupArchetype`、`SurfaceNode`；窗口基类 `FlaxEditor.Windows.Assets.AssetEditorWindow` |
| 程序化资产创建 | `Content.CreateVirtualAsset<T>()` ✅ |
| 批量实例生成 | `ModelInstanceActor.Entries`、`ModelInstanceActor.MeshReference.{Actor,MeshIndex,LODIndex}`、`ModelInstanceEntry.{Material,Visible,ShadowsMode,ReceiveDecals}` |
| 植被生成 | `Foliage.AddFoliageType(Model)`、`Foliage.AddInstance(ref FoliageInstance)`、`Foliage.SetInstanceTransform(int, Transform)`、`Foliage.RebuildClusters()`；`FoliageInstance.{Transform,Type,Random,Bounds}` |
| 场景生成 | `Level.SpawnActor(Actor)` / `Level.SpawnActor(Actor, Actor parent)` → **`bool`**、`Level.GetActors(Type, bool)` / `Level.GetActors<T>(bool)`、`Level.FindActors(Tag, ...)`、`Level.SaveScene(Scene, bool prettyJson)` |
| 调试绘制 | `DebugDraw.DrawLines(Double3[] lines, Matrix transform, Color color, float duration, bool depthTest)`（批量，`lines` 为扁平顶点数组）、`DrawLines(Float3[], ...)`、`DrawLines(GPUBuffer, ...)`、`DrawLine`、`DrawWireBox(BoundingBox, ...)`、`DrawWireSphere(BoundingSphere, ...)`、`DrawSphere(BoundingSphere, ...)`、`DrawCircle(Vector3, Float3, float, Color, float, bool)` |
| 贴花 | ⭐ **`FlaxEngine.Decal : Actor`**（`Source\Engine\Level\Actors\Decal.h`，v1.1 误判为不存在）：`Material`（必须为 `MaterialDomain.Decal`）、`Size : Vector3`、`SortOrder : int`、`RenderLayersMask`、`DrawMinScreenSize : float`、`CreateAndSetVirtualMaterialInstance()` |

### A.3c v1.1 → v1.2 二次复核修正表

复核手段：引擎头文件与 C# XML 导出面**双向交叉**。v1.1 主要靠反射与文档，未将“某个类型在头文件里找不到”与“它确实未导出”区分开，导致一条假阴性。

| # | v1.1 结论 | 实际 | 影响 |
|---|---|---|---|
| 18 | “**无 `Decal` Actor 类型**，贴花靠 `IsDecal` 材质驱动，节点不成立” | ❌ **错**。`Source\Engine\Level\Actors\Decal.h`：`API_CLASS(...) class Decal : public Actor`，带 `ActorToolbox("Visuals")`；C# 侧 `FlaxEngine.Decal` 的 `#ctor` + 5 属性 + 1 方法全量导出 | **反向修正**：节点成立。推迟到 Phase 3 的真实理由改为“无批量实例化路径、逐点即逐 Actor”（§5.1.5、§10） |
| 19 | “`Terrain.RayCast` 的 4 个重载，只有 `RayCastHit` 那个**可能**给法线，且地形版是否填充 `Normal` 未核验，必须实测” | ⚠️ **归类错了但结论更乐观**。`Terrain.RayCast` 只有 **2** 个重载且都不给法线；给法线的是 **`TerrainPatch.RayCast(..., out float, out Vector3 hitNormal, ...)`**，已在头文件与 C# 导出面双向确认 | 附录 B.2 一条盲区**基本关闭**；`SlopeFilter` 无需差分回退，但查询必须下推到 patch 级（§3.5 点 3） |
| 20 | “自定义资产注册走引擎内部导入器机制，Phase 1 先用 `JsonAsset` 是为了避开边界” | 机制描述正确，但强度不足：注册完全在 **C++ 侧**，纯托管插件**无论如何都注册不了**新资产类型 | 定性从“降风险”改为“硬约束”，原生资产移出 PCG 主体路线图（注 4） |
| 21 | （未提及） | `TerrainPatch.GetHeightfield()` 在 C++ 头文件中存在（返回 `RawDataAsset*`），但 **C# XML 导出面 0 命中** —— 脚本层用不到 | 路径 A 入口只能是 `GetHeightmapData()`，v1.1 选定正确（避免实现时误走此路） |
| 22 | “`Terrain.GetHeightmapSize()` → 属性 `Terrain.HeightmapSize`”等 v1.0 勘误项 | ✅ 复核一致（`API_PROPERTY() int32 GetHeightmapSize()`） | 无变更 |
| 23 | 噪声体系结论（无种子 / 仅 2D / `WorleyNoise`→`Float2` / `VoronoiNoise`→`Float3` / 无 3D Perlin） | ✅ `Noise.h` 逐行比对一致 | 无变更，§3.4 自带种子化噪声的决策继续成立 |

---

## 附录 B：核验方法说明

本版的 API 核验通过以下手段完成，后续实现阶段遇到不确定的 API 时应复用同样方法，而非依赖记忆或推测：

1. **全量 API 文档检索**：`"C:\Program Files (x86)\Flax\Flax_1.12\Binaries\Editor\Win64\Development\FlaxEngine.CSharp.xml"`（约 6.1 MB）。用 `<member name="M:类型.方法` 模式精确枚举成员与签名，可确认存在性与参数类型。
2. **引擎头文件直读**：`"C:\Program Files (x86)\Flax\Flax_1.12\Source\Engine\..."`。可读到 `API_FUNCTION` / `API_PROPERTY` 的原始 C++ 签名与注释（如 `Noise.h` 全文，确认无种子参数）。
3. **本机 DLL 版本确认**：`flaxdump.res` 与 `FlaxEngine.CSharp.dll` 的版本资源 → `1.12.6913`。
4. **仓库内既有实现对照**：`HundunWorld\Plugins\HundunAgent\Source\HundunAgentEditor\HundunAgentPlugin.cs`（插件基类用法、`PluginDescription` 赋值、菜单注册与 UI 未就绪重试模式）、`HundunWorld\Plugins\NarrativePro\NarrativePro.flaxproj`（插件工程文件结构）。

### B.0 三个验证 Harness 的分工（v1.2 新增）

| 入口 | 类型 | 回答的问题 | 能执行代码？ | 需启动编辑器？ |
|---|---|---|---|---|
| `Tools/PCGVerify` | `Library`，`dotnet build` | 插件运行时源码能否编译（快速、诊断精确，绕过 Flax 构建链） | ❌ | ❌ |
| ⭐ `Tools/PCGGoldenRunner` | `Exe`，`dotnet run` | 噪声/RNG 的**数值输出是否仍等于黄金值**，以及行为不变量（连续性、增量重算一致性、流独立性、均匀性） | ✅ | ❌ |
| `PCGSelfTest`（挂在场景 Actor 上的 `Script`） | 编辑器/游戏内 | 需要真实场景的部分：地形标定与双路径交叉验证、节点注册/编译/环拒绝、COW 隔离、Actor 生成 | ✅ | ✅ |

> **陷阱：“编译通过”不等于“测试跑过”。** 一个只编译的 Library harness 会让静态写在源码里的期望值看起来“已被验证”，而实际上从未与代码输出比对过。任何声称“锁定数值输出”的测试，必须有一条能**执行**并打印 PASS/FAIL 退出码的路径（否则它只是一个带数字的文本文件），以及一条在真实引擎内跑端到端的路径。

### B.1 反射核验（v1.1 新增手段，强烈建议后续沿用）

XML 文档**不包含字段与属性的实际类型**（`<summary>` 里只有文字描述）。凡是需要精确类型的地方，用 .NET 反射直接读元数据是最可靠的手段。可复用的做法：

```powershell
# 1) 建一个 net10.0 控制台工程（Flax 1.12 的 CSharp 程序集 tfm 为 net10.0，已核验）
# 2) 引用 FlaxEngine.CSharp.dll，且必须 <Private>true</Private>（否则运行时找不到程序集）
#    路径：C:\Program Files (x86)\Flax\Flax_1.12\Binaries\Editor\Win64\Development\FlaxEngine.CSharp.dll
# 3) 代码里用 typeof(FlaxEngine.Actor).Assembly 拿程序集，再枚举字段/属性/方法签名
```

**关键坑（本次实际踩到，记录备查）：**

| 做法 | 结果 |
|---|---|
| `Assembly.LoadFrom(dll)` 后调用 `GetTypes()` | ❌ 抛 `ReflectionTypeLoadException`（依赖无法解析） |
| `Assembly.GetType("FlaxEngine.Transform")` 直接在 PowerShell 里解析 | ❌ 返回 `null`（该程序集并非常规托管程序集布局，需实际运行环境解析） |
| `MetadataLoadContext` | ❌ 需额外 NuGet 包，`Add-Type` 加载失败 |
| ✅ **`typeof(FlaxEngine.Actor).Assembly` + `GetFields()`/`GetMethods()`** | ✅ **唯一可靠路径**，且能同时拿到参数名与返回类型 |

本次正是靠这一手段发现了两处仅靠 XML 文档无法察觉的问题：`Level.SpawnActor` 返回 `bool`（不是 `void`），以及 `DebugDraw.DrawLines` 的 `lines` 是扁平顶点数组（不是点数组）。

### B.1b 搜索工具本身会造假阴性（v1.2 新增，必读）

v1.1 的「无 `Decal` Actor」错判就源于此。记录两条硬规则：

| 现象 | 原因 | 规则 |
|---|---|---|
| `findstr /s /i /m "Decal" "...\Source\Engine\*.h"` 返回空（或乱码如 `24ae11837f`） | PowerShell 下 `findstr` 的参数路径/引号转义会被破坏，并且会把 GUID 片段当输出；它**不是可靠的否定证据** | 在本仓库环境（PowerShell + 含空格路径）下，**禁用 `findstr` 做 API 存在性判定**，统一用 `Select-String` |
| `Get-ChildItem "...\Source\Engine\Actor"` 报“路径不存在” | 凭直觉猜目录布局会漏 —— `Decal.h` 实际在 `Source\Engine\Level\Actors\`，而非 `Source\Engine\Actor\` | 搜索必须从 `Source\Engine` 根**递归**，不得先验限定子目录 |

**可复用的核验命令（已验证）：**

```powershell
# C# 侧导出面（判定“脚本能不能用”的唯一真相）
$xml = "C:\Program Files (x86)\Flax\Flax_1.12\Binaries\Editor\Win64\Development\FlaxEngine.CSharp.xml"
Select-String -Path $xml -Pattern 'name="[TMP]:FlaxEngine\.Decal' | ForEach-Object { $_.Line.Trim() }

# 头文件递归（按文件名）
Get-ChildItem "C:\Program Files (x86)\Flax\Flax_1.12\Source\Engine" -Recurse -Filter *.h |
    Where-Object { $_.Name -match "Decal" } | ForEach-Object { $_.FullName }

# 头文件递归（按内容）
Select-String -Path "$eng\Terrain\*.h" -Pattern "API_FUNCTION" | ForEach-Object { $_.Line.Trim() }
```

> **定规：** “某 API 不存在”属于**否定型结论**，风险远高于肯定型（它会把一个可行方案直接删掉）。必须头文件与 C# 导出面**两边各命中一次否定**才能写入设计；单边搜不到只能记为“待确认”，不得直接当作架构依据。

### B.2 已知的核验盲区（实现时须实测，不可从源码/文档推断）

- `TerrainPatch.GetHeightmapData()` 返回的高度值到世界 Y 的换算关系 —— 引擎安装包内 `.cpp` 实现文件缺失（仅 `Source\Engine\Terrain\*.h` 可用），无法从源码读出语义。**必须用路径 B 交叉标定**（§3.5 点 1）。
  > **v1.2 实测补充：此盲区仍未消除。** 在 `World.scene` 上跑通了标定流程，但该地形高度场为常量（全零），标定走了零方差退化分支，得到 `scale=1`/`offset=0` 属于兜底值而非拟合结果。**“标定能跑”不等于“换算语义已证实”**，需要一块真正有起伏的地形重跑。
- `Noise.CustomNoise(Float3)` / `CustomNoise3D` 的数值语义与取值范围 —— 头文件注释仅为 "Noise result."，无范围说明。**这也是选择自带 `PCGSeededNoise` 的原因之一。**
- `Terrain.RayCast` 的线程安全性 —— 文档未说明。**并行前必须验证**（§3.5 点 6）。
- ~~`Terrain.RayCast(..., out RayCastHit, ...)` 是否真正填充 `RayCastHit.Normal`~~ —— **v1.2 基本关闭**：`TerrainPatch.RayCast(Vector3, Vector3, out float, out Vector3 hitNormal, float)` 直接以出参提供法线，无需依赖 `RayCastHit`（§3.5 点 3）。**残留风险：** “重载存在”只证明签名，不证明实现真的写入合理法线（引擎安装包的 `.cpp` 缺失，无法读实现）。路径 A 标定与交叉验证机制保留，因此即使此处不理想也能被发现，不会静默出错。
- `ModelInstanceActor.Entries` 的**写入语义** —— 类型已由反射确认为 `ModelInstanceEntry[]`，但赋数组后是否需手动触发重建/更新（如同 `Foliage.RebuildClusters()`）未见文档。**Phase 1 实现批量 Spawner 时需实测。**
- **工具链盲区（v1.2 新增）：** 任何以“搜不到”为依据的结论，均属于核验盲区而非核验结果，必须按 B.1b 的双向交叉规则重复确认。
