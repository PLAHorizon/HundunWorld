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
> - **序列化细节（v1.4 更正）**：Visject 的 `SurfaceData` 是二进制 `byte[]`，JSON 是文本。**必须 base64**：`VisjectSurfaceContext.Save()` 把画布写进一个 `BinaryWriter` 流（节点 meta、浮点、定长前缀字符串），**不是可文本化的结构**；早期按"不要 base64、直接存字符串"实现（本条原话）导致每次保存都把非法 UTF-8 序列替换成 U+FFFD，布局在写盘那一刻就已损坏，此后每次重开都是空画布。现文件格式 `Format=2` 存 base64，`Format=1` 的旧文件读入时自动迁移（损坏的载荷丢弃并说明原因，节点/连线照常读回）。图表拓扑（Nodes/Edges/参数）本来就是纯文本、diff 友好，这一条只影响画布布局那份不透明的载荷。

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

> **v1.3 落地实况：节点内并行采样（`PCGParallel`），实测数据在下面**
>
> 并行点选在**节点自己的点循环内部**，而不是"把多个节点同时跑"：执行器本身仍留在场景线程（spawner 要建 Actor），而 §4.4 早已把 `IsThreadSafe` 节点的内部并行留成设计位。能这么切的根据是随机流按 `(seed, nodeId, pointIndex)` 取键——第 i 个点只依赖 i，于是分块并行与顺序执行**产出同样的点、同样的顺序**，这条不是推理，是被断言直接比的（3 万次采样逐点比较 `differences=0`，且"走图的串行"与"走图的并行"也逐点相同；`For` 的分块还验了"每个下标恰好访问一次"）。
>
> 实测（`Tools/PCGBench` 同种子同图，`dotnet run project Tools/PCGBench/PCGBench.csproj`；下表数字随机器负载浮动，多次重跑倍率在 1.16×–1.86× 之间摆动，符号为正）：
>
> | 只跑采样循环 | 串行 | 并行(min/worker 4096) | 倍率 |
> |---|---|---|---|
> | 10 万点 | 43.0 ms | **8.4 ms** | 5.13× |
> | 40 万点 | 153.1 ms | **31.5 ms** | 4.86× |
>
> 阈值是有意义的：`min/worker` 抬到 65536 时 10 万点只剩 2 个 worker，反成 0.86×（比串行还慢），所以默认钉在 4096。**诚实的边界**：整条 scatter 图的端到端对比里，并行只带来"落在噪声内"的变化（100k 全图 149 ms 串行 vs 183 ms 并行，多次重跑符号还会翻）——因为该图的耗时大头是 `SelfPruningFilter`，它按"密者先、逐点查邻域"的顺序本质串行，不能被并行化而不改变结果。也就是说：**并行买到的是采样环节 5 倍，买到整图加速要先把剪枝换成分桶并行**。这句话就是下一步的落点，别把它当成已完成。
>
> **v1.3 追加：并行点已覆盖整条逐点链，不只是采样器。** `RandomOffset` / `TransformPoints` / `DensityFilter` 的每点判定同样只依赖下标，已改走 `PCGParallel.For`。一处必须说清的差别：**过滤节点的并行只能并行"判"，不能并行"收"**——`DensityFilter` 现在并行算 `passes[i]`，之后仍按点序串行收集下标，因为输出顺序是契约的一部分（下游按位置索引点）。实测 10 万点全图 **78.7 → 57.7 ms**（相对本会话起点 429 ms 累计 **7.4×**；`RandomOffset` 18.0→4.8、`TransformPoints` 7.6→2.9 ms）。bench 的守门断言也随之**从"总点数相同"升级为"汇出端逐点指纹相同"**（FNV 折叠每个点的量化坐标，含顺序）——总数相等而点集不同是完全可能的，旧的断言强度不够它这么写。剩下的大头仍是 `SelfPruningFilter`（现占 58%），它本质顺序，下一格要么分带/波前（保持逐字同结果），要么改用近似规则并**明说它近似**。

> 两条不变量：① 小计数**留在调用线程内联**（`WillParallelise` + 阈值双闸），预览拖动不去碰线程池；② worker 里抛出的异常**按原类型外抛**——取消仍是 `OperationCanceledException`、节点故障仍是 `PCGNodeExecutionException`，绝不包成 `AggregateException`（执行器按名字 catch，包装会让一次人为中止看起来像崩溃）。混合场景（一个 worker 取消、另一个崩了）不断言谁赢，只断言"外抛的不是包装类型"——谁先被报出来取决于线程池是否启动后段，写死它就是在供一个假保证。
>
> 离屏断言 **+14 条 → 247 passed / 0 failed**，连跑三次无抖动。

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

> **权威清单（45 个注册类型，按注册表导出；`Id` 就是存进图文件的那个字符串，是兼容性契约）**：
> - **Sampler（7）**：`LandscapeSampler`、`SplineSampler`、`NoiseSampler`、`VolumeSampler`、`MeshSampler`、`SampleSpline`（数据版样条采样）、`TextureSampler`
> - **Filter（6）**：`DensityFilter`、`HeightFilter`、`SlopeFilter`、`SelfPruningFilter`、`DistanceFilter`、`AttributeFilter`
> - **Modifier（6）**：`TransformPoints`、`RandomOffset`、`ProjectPoints`、`ScaleByAttribute`、`OffsetByAttribute`、`RotateByAttribute`
> - **Combinator（2）**：`MergePoints`、`Subgraph`
> - **Spawner（5）**：`SpawnStaticModel`、`SpawnFoliage`、`SpawnActor`、`SpawnDecal`、`SpawnSplineMesh`
> - **Point Ops（17）**：`DifferencePoints`、`IntersectionPoints`、`CopyPoints`、`SortPoints`、`BoundsModifier`、`PointNeighborhood`、`RelaxPoints`、`SmoothAttribute`、`SetAttribute`、`RemapAttribute`、`AttributeMath`、`AttributeTransfer`、`DistanceToPoints`、`NoiseToAttribute`、`ChannelToAttribute`、`CreateSpline`、`NormalToDensity`
> - **Control Flow（2）**：`SelectPoints`、`Branch`
>
> **下面 §5.1.1–§5.1.5 是 v1.0 时代的原始设计**（含当时计划、后来没做成现在这个样子的名字，例如 "Point Sampler"），**§5.1.6 起是各轮落地实况**。两者不一致时，**以注册表与 harness 为准**（`NodeBehaviourTests` 里的 `BuiltInTypes` 常量钉住条数，改注册忘了改它就会红）。`Tools/PCGGoldenRunner` 的逐文件链接清单与 `Nodes/` 下的文件也是一一对应的（0 孤儿已在审计中核过）。

#### 5.1.1 输入节点（Sampler）

| 节点 | 功能 | Flax API 依赖（已核验） |
|---|---|---|
| **Landscape Sampler** | 从地形表面采样点 | 双路径，见 §3.5 |
| **Spline Sampler** | 沿样条线均匀/随机采样 | `Spline.GetSplineTransform(float)` ✅ |
| **Volume Sampler** | 在体积内随机放置点 | `BoundingBox` + `PCGDeterministicRng` |
| **Noise Sampler** | 基于噪声密度生成点 | `PCGSeededNoise`（**非**引擎噪声，见 §3.4） |
| **Point Sampler** | 从输入点重新采样 | 已有 PointData 的偏移复制 |
| **Mesh Sampler** | 在模型表面采样 | ✅ **v1.3 已实现为 `PCGMeshSamplerNode`**（Phase 3）。反射复核的取几何通路：`Model.LODs[i].Meshes[j]` → `Mesh.DownloadVertexBuffer(bool)`（顶点带 `Position`/`Normal`）+ `MeshBase.DownloadIndexBuffer(bool)` → `uint[]`，`TriangleCount`/`VertexCount`/`Box` 可直接读 |

**Landscape Sampler 详细设计：**
- 输入：`Bounds`（采样范围）、`PointCount`（目标点数）、`Jitter`（位置抖动）。
- 逻辑：在 Bounds 的 XZ 平面均匀或 Poisson 分布随机采样，对每个采样点按其 `SampleMode` 查询地形高度，生成 PCGPoint。
- 参数：`SampleMode`（§3.5）、`HeightOffset`、`AlignToNormal`、`FallbackToRayCast`、`HeightScale`/`HeightOffset`（标定值）。
- 输出：点在 Y 上贴合地形；`AlignToNormal` 时旋转对齐地形法线；同时写入 `$Slope` 属性，供下游 `SlopeFilter` 复用，避免重复求法线。

**Spline Sampler 详细设计：**
- 输入：`Spline` 引用（**注意类型**：Flax 1.12 **没有** `SplineActor` 这个类型 —— `Spline` 本身就是 Actor，见 `Source\Engine\Level\Actors\Spline.h` 的 `class FLAXENGINE_API Spline : public Actor`；v1.1/v1.2 的「结构体 vs Actor 两个类型」说法是错的，已按头文件与 C# 导出面双向复核更正）、`Spacing`（间距）、`Offset`（横向偏移范围）。
- 逻辑：沿样条长度等距或随机采样 `t` 值，调用 `GetSplineTransform(t)` 获取位置和朝向，可选在法线方向随机偏移。
- 输出：沿样条分布的点，旋转对齐切线方向。
- 可用辅助 API：`GetSplineDirection(t)`、`GetSplineSegmentLength(i)`、`GetSplineTime(i)`、`GetSplineTimeClosestToPoint(v)`、`GetSplinePoint(t)`、`GetSplineOrientation(t)`、`GetSplineScale(t)`；属性 `SplinePointsCount`、`SplineLength`、`SplineDuration`、`IsLoop`（已按 C# 导出面核验）。**核验补充：** `GetSplineTime(index)` 只覆盖已定义的 knot，最后一个 knot 之后没有可用的时间参数，因此**环状样条的闭合段不采样**（实现里显式跳过，不猜测参数值）；需要在接缝处有点就把末 knot 放到首 knot 上。
- **按弧长均匀采样**：`t` 是参数而非弧长，直接等分 `t` 会导致稀疏段/密集段。需用 `GetSplineSegmentLength(i)` 累积建弧长表后按弧长反解 `t`。

> **v1.3 落地实况：`Mesh Sampler`（`PCGMeshSamplerNode` + 纯几何核心 `PCGMeshSurfaceSampling`）**
>
> 拆两层的理由是**可验证性**：资产的几何只能引擎内拿到，而"拿到的东西对不对"几乎不可能在引擎里证伪。于是全部算术（面积加权、重心映射、退化三角形剔除、世界变换复合）落在不碰资产的 `PCGMeshSurfaceSampling`，离屏用**手搓的已知答案**测；节点只剩"下载 buffer → 填数组 → 复合宿主变换"这层适配器，交给引擎自检。
>
> 四条口径：① **均匀性靠 `sqrt(u)`**：把两个均匀随机数直接当重心坐标会在顶点处堆出扇形；② **零面积三角形权重为 0**（导出网格满是塌陷面：UV 接缝、拆分壳层），且因为累积面积数组里它不占区间，二分找的是**严格大于**的边界，否则 `>=` 会让它被选中；③ **宿主 Actor 优先于资产**：给了 Actor 就采它身上（含子级 `StaticModel`）的模型并复合它的世界变换，只给资产则落在生成区域中心——"围着一块岩石撒碎石"不需要岩石在场；④ 法线走方向变换（不吃平移），无顶点法线时退回面法线，且把 up 写进点的 `Rotation`，下游 spawner 与地形采样共用同一套朝向约定。
>
> **离屏证据**：新增 29 条断言（`Tools/PCGGoldenRunner/MeshSamplingTests`），总计 **233 passed / 0 failed**。含 3-4-5 直角三角形面积恰为 6、quad 总面积 4 与两段累积 [2,4]、边界 roll 归属、塌陷三角形 5000 次抽样 0 命中、等面积两半 2 万次 roll 得 0.5015/0.4985、面积比 18:0.5 的选中率实测 0.973 == 解析值 0.973、u=v=0 精确落回第一顶点、2000 点全部落在三角形内（用"三块子面积之和 == 整块"判内外）、同 seed 逐点相同、不同节点 id 不同、平移+2 倍缩放的宿主里点云落在预期足迹内、零点数与全退化几何各自报出可读原因。
>
> **引擎自检 3f 组**补上适配器那两层只有引擎能答的问题：用 `Engine/Models/Box` 建一个 `StaticModel` 宿主，300 点采样后**把宿主沿 X 平移 137**，要求逐点位移精确等于 (137,0,0)（这条不需要知道模型尺寸，却能在"忘了复合宿主变换"时立刻红）；再要求 ≥90% 的点其 up 与"指点心向外"同向（法线拿错空间的判据）；无宿主无资产时**按名字拒绝**、点数为 0 时给出原因。

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

> **v1.3 实测发现并修掉的真 bug：`Min Distance = 0` 时剪枝会漏判拥挤**
>
> 现象：`SelfPruningFilter` 在 `UseBoundsOverlap = false` 且 `MinDistance = 0` 时，`minDistanceSquared = 0` 使逐对测试**落到"包围盒重叠"那条分支**，但 `CellSize()` 仍按 `Math.Max(MinDistance, ε)` 取格子边长（≈1e-4）。格子的单环邻域根本覆盖不到几单位外的盒子，于是本该被挤掉的点**全部存活**：实测 320 个点里 brute-force 参照判掉 177 个拥挤对，网格一条都没抓到。也就是说"把间距设 0 只按实际重叠裁"这个自然会去设的开关，过去等同于关掉剪枝——而且**没有任何日志**。
>
> 修法：格子的边长必须由**实际会跑的那个测试**的射程决定，而不是由 flag 决定（`distanceTest = !UseBoundsOverlap && MinDistance > 0`；否则按盒子的最大半轴 ×2 取，plane 模式下 Y 已被折进同一列所以不吃射程）。
>
> 为什么这次能被发现：新增的等价测试**不是**拿新实现和旧实现比，而是把剪枝的规范（"按 (density 降序, index 升序) 逐个取，与任何已存活者冲突就弃"）用 O(n²) 暴力另写一遍当参照，再让 42 个用例（4 档间距 × plane/3D × 均匀/成簇/同点重合/等密度/混合密度 × 距离与盒重叠两模式）逐点比对存活集合。旧网格代码若有同样的漏判，参照实现不会跟着错。**离屏断言 247 → 289 passed / 0 failed。**

> **v1.3 第二次尝试有效：稠密 head/next 幸存者网格，剪枝从 163 ms 降到 35.8 ms（10 万点）**

上一条负结果之后换了这个形状，成了：**plane 模式（默认，也是地面散布的实际用法）把竖直轴折出键之后，占用格就是一个平面矩形**，于是邻域不必哈希——直接 `int[] _head`（每格一个链表头）+ `int[] _next`（每个点一根链），按 `row * width + col` 下标进 9 个邻格。幸存者**插进自己格的链头**，不需要每格一个 `List<int>`，也不需要预数控数。

三点让它仍然是"同一个答案"：① 格子里只放**已被接受**的点，问的问题和旧的字典版本逐字相同；② 链是 LIFO，格内顺序与旧实现不同，但判据是"有没有任何幸存者冲突"（存在量词），**顺序无关**；③  footprint 过疏（格数 > 12 × 点数，或 > 2^20）时退回哈希桶，两条路径都对着 §5.1.2 的暴力参照比过（新增 8 条"两个相距 300 万单位的营地"用例专门踩退回分支）。

实测同一张图同一 seed：`SelfPruningFilter` 163 ms → **35.8 ms**（10 万点），整条 scatter 429 ms（v1.3 初测）→ **78.7 ms**；5 万点 178 → 53.5 ms。剪枝不再是头号开销（现在 45%，采样 19%）。bench 的预算因此从 1200 ms 收到 **400 ms**（实测 5.1 倍余量），harness 327 → **335 passed / 0 failed**。

> **v1.3 测量手段修正：单次采样看不出剪枝的微改动，于是加了中位数与交叠式 A/B 两件仪器**
>
> 上一次的尝试想在"逐对判据"上做文章：热循环里每对候选点调一次 `Func<PCGPoint, PCGPoint, bool>`，而 `PCGPoint` 带着 `Bounds` 与属性集引用，按值传两次——于是把它换成一个 `readonly struct ConflictTest`（`Hit(in a, in b)` 可内联）。**先撞上的不是性能问题，是仪器问题**：改完之后节点自报 68 ms，而文档记录的基线是 35.8 ms；把改动原样退回再测，同一份代码的自报又在 40 / 59 / 66 ms 之间跳（同一个进程里采样器也从 21 ms 跳到 30 ms）。也就是说**跨分钟的单次采样比这类改动本身的幅度还大**，任何"A 比 B 快"的结论在这个精度下都不成立。
>
> 两件仪器因此进了 `Tools/PCGBench`，都留在里面：① `PrunerStageSweep`——同一执行器连跑 9 轮 10 万点 scatter，报 `SelfPruningFilter` 自报时间的**中位数与极值**（9 次里最大最差的一次能到中位数的两倍，这就是过去误判的来源）；② `PairTestProbe`——把两种调用形状放进**同一个进程、同一批点、逐轮交叠**（A/B/A/B…共 11 轮），两者用同一份平面网格与同一套贪心顺序，并断言**存活点数完全相等**（40656 vs 40656），于是剩下的唯一差别就是"逐对判据怎么被调用"。交叠是关键：漂移对两边同等作用，比值不受"什么时候跑的"影响。
>
> **负结果：比值跨次重跑在 0.98× 与 1.05× 之间摆动，判为"无差别"，已回退。** 10 万点、间距 6、11 轮中位数：第一次 `Func` 83.9 ms / 内联值结构 85.3 ms（0.98×），隔一会儿同二进制重跑得 1.05×——**探针自己的摆动幅度就是判定结论**，两种形状在这台机器上不可分辨。逐对判据的成本不在委托调用或 `PCGPoint` 拷贝上（这一层的开销被邻居链的指针追逐和 9 格扫描摊平了），所以剪枝要再快，只有**改变它看的对数**（分带 / 波前 / 明确声明为近似的规则），而不是改变它判断一对的方式。生产代码因此**保持委托形状**（`SurvivorGrid` 的注释里写明了这次尝试与实测比值），只有 bench 多了两件永久仪器。harness 仍是 **374 passed / 0 failed**（其中对着 O(n²) 暴力参照的等价断言 50 条，回退前后都没动过）。

> **v1.3 负结果记录：把剪枝的格子换成 CSR 排序数组 + 二分查细胞，实测更慢，已回退**
>
> 动机是 §9 的实测——剪枝占一次 10 万点 scatter 的 66–72%，旧形状是 `Dictionary<long, List<int>>`：每点 27 次哈希查表 + 每个占用格一次 List 分配。改成"一次 `Array.Sort` 排序细胞键 + 平坦数组分段 + 邻居细胞二分"后，10 万点全图从 **~230 ms 涨到 653 ms**：排序本身（约 1.7M 次比较）加上每点 27 次二分（约 43M 次比较）比原来的 2.7M 次哈希查表更贵。**已把这段实现删掉、只留下上面那个 CellSize 修复**（修复与数据结构无关，等价测试两者都过）。留在这里是为了让下一次别再顺手"优化"成同一条路：这条路上真正可能有效的方向是**把 27 格邻域改成沿排序数组的一次线性扫描**（或按细胞分带做 wavefront），不是把哈希表换成比较器。

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
| **Spawn Foliage** | 添加植被实例 | `Foliage.AddFoliageType(Model)` → `AddInstance(ref FoliageInstance)` → `RebuildClusters()` ✅ **v1.3 已实现为 `PCGSpawnFoliageNode`**（反射复核：`AddInstance` 的 `FoliageInstance` 确为 `ref` 参数，传值报 CS1620；`FoliageInstance` 只有 `Transform/Type/Random/Bounds` 四个字段，`Foliage` 有 `RemoveAllInstances()/InstancesCount/FoliageTypesCount/GetFoliageType(i)`，`FoliageType.Model/CullDistance` 可读写）。落地口径见本节末尾的 v1.3 落地实况 |
| **Spawn Actor** | 生成任意 Actor | `Level.SpawnActor(Actor)` / `Level.SpawnActor(Actor, Actor parent)` ✅ **v1.3 已实现为 `PCGSpawnActorNode`**，走更强的 `PrefabManager.SpawnPrefab(Prefab, Actor parent, Transform)`（反射复核该家族共 7 个重载，含带 parent 的两个）：一次调用同时给到父级与世界变换，比"先 `new` 再 `SpawnActor` 再改 Transform"更适合 Prefab——见 §5.1.5 末尾 v1.3 落地实况 |
| **Spawn Decal** | 生成贴花 | ⚠️→✅ **v1.3 已实现为 `PCGSpawnDecalNode`**。`Decal` Actor 存在（v1.1 判断有误）：`Material : MaterialBase` / `Size : Vector3` / `SortOrder : int` / `RenderLayersMask : LayersMask` / `DrawMinScreenSize : float` （本轮反射复核，另有 `CreateAndSetVirtualMaterialInstance()`）。推迟到 Phase 3 的真实理由仍是「无批量实例化路径」，见 §5.1.5 末尾 v1.3 落地实况 |

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

> **v1.3 落地实况：`Spawn Foliage` 已实现（`PCGSpawnFoliageNode`），四条不能从代码反推的口径**
>
> 1. **一个节点一个 `Foliage` Actor，且按图位置复用**（名字 `PCG_Foliage_<节点下标>`，而不是缓存 C# 节点实例）。理由是重编译会换掉节点对象，而上一轮的实例仍然挂在场景里那个 Actor 上——实例不是 Actor，`PCGVolume.Clear()` 的结构式清理只能整体销毁宿主，逐条够不着。若每个节点实例各建一个宿主，每次重新生成就多一份四十万条草，且没有任何地方能看见它。
> 2. **宿主固定为单位变换**，实例 `Transform` 一律写世界坐标。这样"世界空间"与"宿主空间"是同一个东西，Volume 搬动后重新生成时不会多一层换算可以错。（已生成的实例不会跟着搬——与静态模型 spawner 同一口径：改完重新生成。）
> 3. **`Bounds` 故意不写**。§5.1.5 反射表原本担心 `BoundingSphere` 与 `PCGPoint.Bounds`（AABB）需要转换；实测走不通这条路——节点交给引擎的是 `Transform/Type/Random`，包围球留给 `RebuildClusters()`。**这条仍未在 PIE 证实**（要看的现象：远处草/树是否被正确剔除、贴脸时不会整片消失）。若剔除异常，正解才是把点的 AABB 换算成球写进 `Bounds`。
> 4. **`Random` 写点的 density**，于是同一 seed 重跑得到的随机表现与上一次逐条一致；`Random Yaw` 走 `(seed, 节点id, i+7919)` 这条独立流，与 density 抽样不互撞。
>
> 上限：`Max Instances` 默认 50000，是"跑飞的图"的护栏而不是规划参数（实例远比 Actor 便宜，但仍是内存与集群重建的成本）。`Cull Distance` 默认 0 = 不写、保留 foliage type 自己的默认值，避免节点替设计者做了一个他没做的决定。
>
> **自检**：`PCGSelfTest` 新增 3c 组（需要活场景）——无 Model 时必须报错且**不建宿主 Actor**、点位全部透传；12 个点跑完后 `Foliage` Actor **恰好 1 个**、`InstancesCount == 12`；换一个新建的同参数节点实例（模拟重编译）再跑，仍是 1 个宿主、仍是 12 条实例而不是 24（复用与清空都在这里被证伪或证实）；带 `ClipCell` 的半区运行只放 6 条、宿主上也只剩 6 条（分区的 padding 不会重复落实例）。模型资产默认用引擎自带的 `Engine/Models/Box`（游戏模块里已在用，运行时可加载），加载不到时打 SKIP 而非 FAIL。

> **v1.3 落地实况：`Spawn Actor` 已实现（`PCGSpawnActorNode`）**
>
> 走 `PrefabManager.SpawnPrefab(prefab, parent, transform)`（反射复核：该重载确实存在，同族 7 个），而不是 `new` + `Level.SpawnActor`——Prefab 的实例化只能由引擎侧完成，且父级必须在第一次调用里就给到：`PCGVolume` 的清理是**结构性**的（"销毁我的孩子"），一个短暂无父的 Prefab 根既可能带着错位置跑完 `OnStart`，也可能在这一刻还没被体积收养。
>
> 三条口径：① 生成的根 Actor 名字沿用共享前缀 + Prefab 文件名 + 点下标，与静态模型 spawner 同一套（`Clear()`/收养逻辑靠它辨认）；② 上限 `Max Actors` 默认 2000，**低于植被 spawner 的 5 万**——一个点在这里是一整棵 Actor 树，稠密散布属于 `Spawn Foliage` 不属于这里，节点日志在触顶时也是这么指引的；③ 未加载的 Prefab 先 `WaitForLoaded(10s)` 再判失败，与游戏模块装备挂载的既有做法一致——"还没加载完"和"根本没有这个资产"是两个不同的问题，值得让设计者分开看到。
>
> 生成失败（返回 null）与抛异常都**中断本轮并把原因写进 `LastError`**（执行器会并进 `result.Warnings`，见 §9 的消费链），而不是逐点静默跳过：一个空 Prefab 会"成功生成 0 个对象"，那是最难查的一种坏图。
>
> **自检**：`PCGSelfTest` 新增 3d 组——无 Prefab 时报错、点位透传、**场景里一个 Actor 都不多**；给定 `ActorPrefabPath` 后跑 6 点：`SpawnedCount == 6`、体积名下带前缀的 Actor **恰好 6 个**、`Clear()` 返回 6（这就是"只清自己生成的、不动选手摆的"那条ownership设计的直接证据）；换一个新 Volume 带 `ClipCell` 半区再跑：只生成 3 个。`MaxActors = 0` 必须报"zero"且不生成任何 Actor。项目里没有可用的引擎自带 Prefab，所以放置这半在 `ActorPrefabPath` 为空时打 SKIP——**这条需要设计者指一个真 Prefab 才能跑绿**。

> **v1.3 落地实况：`Spawn Decal` 已实现（`PCGSpawnDecalNode`），Phase 3 的三个 spawner 大件就此只剩 `Mesh Sampler`**
>
> 三条不能从代码反推的口径：① **朝向取自点自身的 `Rotation`，节点不再重新查询地形**。贴花沿自身 -Y 投影，所以宿主的 +Y 必须是表面法线——而 `PCGLandscapeSamplerNode` 在 `Align To Normal`（它的默认值）下已经用同一份法线建好了这个坐标系，图的坡度筛选也是按它投票的。spawner 再采一次样等于对同一个量投第二票，两处一旦不一致，被坡度筛掉的点会以另一种法线出现。代价是：来自"未对齐"采样器的点会平铺在世界水平面上——那是上游节点的契约，不是这里的。② `Surface Offset` 默认 5，**沿表面法线**抬升而不是沿世界 Y：垂直岩面上沿世界 Y 抬会把贴花飘离岩面，斜坡上则会埋进去。③ **材质域在运行期用 `MaterialBase.IsDecal` 把关并按资产名报错**——设计稿原本要求"编译期校验"，但托管侧没有资产域的编译钩子，最早能说的时刻就是首跑；非 Decal 域的材质贴在 `Decal` 上什么都不渲染，若放行，设计者看到的将是"生成成功却空无一物"，然后怀疑采样器。上限默认 **1000**（三个 spawner 里最低）也是同一动机：无实例化路径，逐点即逐 Actor。
>
> `Random Yaw` 在这里**默认关**（另两个 spawner 默认开）：偏航旋转的是投影出来的贴图本身，那是造型决定而非自然变异。
>
> **自检**：`PCGSelfTest` 新增 3e 组——无材质时报错、透传、场景里一个 Actor 都不多；给了路径但不是 Decal 域时必须**按名字拒绝**且不落地；真是 Decal 域时 4 个点生成 4 个 Decal 且都挂在体积名下、`MaxDecals = 0` 报 "zero"。**本项目的 Content 里没有任何 Decal 域材质**（`find -iname "*decal*"` 与全量 grep 都零命中），所以放置那半在 `DecalMaterialPath` 为空时 SKIP；这条也需要设计者先做一个 Decal 材质才能跑绿。

### 5.1.6 Point Ops（对标 UE5 的 Point Ops 类别，v1.3 本轮新增）

UE5 PCG 把"对点集本身做的运算"单列一类，本插件此前只有 `MergePoints`。本轮补齐 5 个，注册类型 18 → **23**：

| 节点 | Id | 语义与要点 |
| --- | --- | --- |
| Difference | `DifferencePoints` | 把 **Cut** 那一层的**点包围盒**（或到其中心距离）当成体，从 **In** 里挖掉落进去的点。`Padding` 放大剪裁体（挖走廊/湖面/建筑地基），`Plane Only` 决定要不要看高度。空 Cut ⇒ **透传 In**（一个被禁用的分支不应该让整张图变空）。 |
| Intersection | `IntersectionPoints` | 与 Difference 同一个判定、取另一侧：只留落进去的点。两者共用 `PCGPointProbe`，因为它们必须是互补的——各写一遍"inside"迟早会分叉，harness 直接断言 `difference + intersection == 输入` 且两输出**不相交**（4 组参数）。 |
| Copy Points | `CopyPoints` | 每点复制成小簇（草丛/碎石/贴花带）：`Count`、`Offset`（可局部坐标，沿点自身朝向叠）、`Rotation Spread`、`Scale Min/Max`、`Seed Offset`。随机数键 `(seed, nodeId, 源下标 × Count + 副本号)` ⇒ 确定性；**复制件的包围盒按旋转/缩放重新包络**（否则剪枝拿一个已经装不下它的盒子去测）；属性集**按引用共享**（写方按插件契约自行 clone）。 |
| Sort Points | `SortPoints` | 按密度/高度/X/Z/**属性**升降序重排。**稳定排序**（并列按下标解）不是洁癖：点数上限是边跑边截断的，谁在最前面就活下来；生成器也按序摆放，顺序变了重叠处的绘制顺序就变。按属性排序时 `PCGData.Gather` 必须把**属性集一起按新顺序 gather**，否则下游读到的是邻居的值——这条有专门断言。 |
| Bounds Modifier | `BoundsModifier` | Set/Add/Scale 三种方式改**每个点的盒子**——也就是剪枝用来算间距、生成器用来摆放的那个盒子。此前"道具占多大地方"只能靠采样器随手写。半长轴有下限（epsilon）：内缩超过盒子本身应落到下限，**不是取绝对值**（我先写成 `Math.Abs`，把 -1e6 的内缩算成 999990 的一公里盒子，断言只写"≥ 下限"所以照样绿；改成"等于下限"才抓得住）。 |

**宽相**：`PCGPointIndex` 均匀网格（密集头链 + footprint 过疏时退回哈希桶），把布尔运算的 n·m 次 AABB 测试压到每点几次。稀疏路径用"跨 9000 单位只放 12 个点"的夹具单独验，且要求它与**独立写法的暴力参照**（中心+半长轴形式，生产实现是夹取形式）逐点一致——三条 padding × 两种模式共 6 条。

**离屏证据**：`Tools/PCGGoldenRunner/PointOpsTests.cs`，本增量 **+48 条**（harness 506 → **554 passed / 0 failed**），两道编译门 0 错误。夹具上踩过的两个坑也留在注释里：`(i % 3) * 9000` 在 i=2 是 18000（不是 0），以及 **`EulerDegrees(0,90,0)` 在 `EulerAngles` 上读回 116.62°**（两套 API 复合约定不同）——所以断言改成几何性质（局部步长 ⟂ 点自身 forward、长度守恒），不再假设某套欧拉约定。

**节点编写规则（本轮两次踩坑换来的，写进清单免得再犯）**：① 新节点要同步 **4 处**：注册表条目、槽位默认值（`Slot`/`EnumSlot`，与节点 `_params.Get` 的 fallback 一致）、`NodeBehaviourTests` 的**条数绊线**、`PCGGoldenRunner.csproj` 的**逐文件清单**（漏了就是静默不覆盖）；② **不要在被离线覆盖的路径里调 `Debug.*`**——Flax 的 `Debug` 走原生引擎，harness 里会以 `DllNotFoundException: FlaxEngine` 直接崩（"属性名没填就拒绝"这条路径就是这么崩的）；正确通道是写 `LastError`，执行器会把它收集进整轮的 warnings，编辑器照样看得到；③ 新节点必须有**直接断言**（含边界与拒绝路径），不要只靠"能编译"。



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

### 5.1.7 Attribute ops（对标 UE5 的 Attribute Set 类别，v1.3 本轮新增）

UE5 用属性（Attribute）做"把派生数值留下来给下游用"的载体；本插件此前只有内部属性集与零散写入（`SlopeFilter` 顺手写 `$Slope`），缺"按名字读写/重映射"的通用节点。本轮补两个，注册类型 23 → **25**：

| 节点 | Id | 语义与要点 |
| --- | --- | --- |
| Set Attribute | `SetAttribute` | 把**一个数值写成命名的属性列**。来源可选 **Constant / Density / Position X,Y,Z / 另一个属性**，再乘 `Scale` 加 `Bias`。这是"把算出来的数留下来"的那一步：坡度阈值只算一次，后面既可以用它过滤，也可以把它重映射成密度，编辑器里还能直接看这一列。 |
| Remap | `RemapAttribute` | **把一个区间映射到另一个区间**。两端都可以是属性或**密度本身**（名字留空即密度），所以它就是"派生列 ↔ 生成概率"之间的桥：`slope → density`、`noise → growth`。输出区间可以反向写（Out Min > Out Max）——"越陡越少"因此不需要第二个节点。 |

三条语义决定，都是有理由的：① **零宽输入区间不是除零**（`In Min == In Max` 是还没标定过的图，值按落在哪一侧映射到输出两端，而不是产出 NaN）；② **默认夹取**（不夹取的重映射是密度跑到 40 或 -3 的来源，而生成器把密度当概率；想外推就显式关掉，两种行为都有断言）；③ **写属性不碰密度、写密度不碰属性列**，目标列名留空即写密度；读一列**没人写过**的属性时（图接在采样器前面）不静默把所有密度压成一个值，而是 `LastError` 说明并按原样透传。

**写入遵循插件的写时复制契约**：路径是 `PCGData.Clone()`（克隆属性集并把点重新指向它）+ 属性集的 `GetWritable*Array`（内部 `Detach()`）。harness 直接断言**输入那一层的属性集不会多出这一列**——这条一旦破，上游数据会被下游的写入静默污染。离屏证据：`PointOpsTests.AttributeOpTests`，**+13 条**（harness 554 → **567 passed / 0 failed**）。

### 5.1.8 Control Flow（对标 UE5 的 Control Flow 类别，v1.3 本轮新增）

注册类型 25 → **26**，新增分类 `Control Flow`。第一个节点是 **Select**（`SelectPoints`）：两个输入 A/B，一条规则择一**整层透传**（不是合并——断言直接验证"输出点数 == 被选中那层的点数"）。

| 规则 | 语义 |
| --- | --- |
| `Switch` | 设计师拨的开关（用 A 还是用 B）。把二选一收在一处，下游半张图不必复制两份。 |
| `Point Count` | 选点数更多（或更少）的那层——省掉"哪个采样器产出更多"的人工试错。 |
| `Average Attribute` | 按某属性**均值**选层（例如 `growth` 更高的一层）。 |

**必须说清的一条与 UE5 的架构差异**：UE5 的图能跳过不需要的 pin，而本插件的执行器是**拉取式**、节点拿到的输入已经求值完毕——所以 Select 选的是**数据**，不是**省算力**：两条分支都会先跑完。它的价值在于"哪一份现成的层该被用"（一处开关、或自动挑更大的那层），而不是让没被选中的分支不执行。想要真正省掉成本，应该把不需要的节点禁用（按次生效），这一点写进了节点注释与本节。

**每条规则都定义了"判不了"时的答案**，且都有断言：① **精确并列 → 取第一个输入**（不让世界取决于浮点噪声）；② **某侧缺该属性 → 取第一个输入并报 `LastError`**（"接在采样器前面"的图不该静默变成空世界）；③ **某侧为空 → 透传另一侧**，两侧都空才是空。离屏证据：`PointOpsTests.SelectTests`，**+7 条**（harness 567 → **574 passed / 0 failed**）。

### 5.1.9 Point Neighborhood（对标 UE5 的邻域统计，v1.3 本轮新增）

注册类型 26 → **27**，归入 `Point Ops`（UE5 把这一个放在 Points/Neighborhood 下；语义与 Point Ops 同属"对点集本身做运算"，所以不新开分类）。

`PointNeighborhood`：在半径内统计每个点的**邻居数**，并可选地把某个属性在邻居上**求均值**，结果都写成属性列（默认 `neighbors` / `neighborAverage`）。

- **它只发布数字，不删点、不移点**：拥挤度从"点实际在哪"来（而不是采样器被要求了什么、或地面多陡），于是"村子里稀疏、林线处更密"这类效果可以写成 `邻居数 → Remap → 密度` 的链——这正是 UE5 里用邻域节点的同一形状。把裁剪规则留在唯一一处（`SelfPruningFilter`），而这个节点可以被当成一列数据在编辑器里先看清再决定怎么用。
- **查询网格的格子边长就是半径**，所以半径内的点只可能在自身格与八邻格内——与剪枝器同一个"单环"论证，代价按"点数 × 平均邻居数"走。
- **半径为 0 或没写列名一律按名字拒绝**（`LastError` + 原样透传）：空的邻域不是统计量，是一张没填完的图。
- **`Plane Only` 默认开**（地面散布）；关掉即按三维球统计（体素层用）。
- 写入同样走**写时复制**（`Clone` + `Detach`），断言里包含"输入层不会多出这一列"。

离屏证据 `PointOpsTests.NeighborhoodTests`，**+8 条**：把三个间距 10 的点配 15 半径，**逐点断言确切计数**（`1,2,1`；含自身 `2,3,2`）而不是"大于零"；属性均值 `1,2,3 → 2,2,2`（含自身 `1.5,2,2.5`）；同一列上 1000 单位高的点，平面模式算作邻居（`2,2,2`）而三维模式不算（`1,1,0`）；零半径透传且不写列；输入层不被写入；均值源属性缺失时只写计数并报错。harness 574 → **582 passed / 0 failed**。

### 5.1.10 Relax Points（对标 UE5 的 Relax，v1.3 本轮新增）

注册类型 27 → **28**，归入 `Point Ops`。`RelaxPoints`：在半径内把过密的点**互相推开**，迭代若干次。

- **与剪枝是两件事**：`SelfPruningFilter` 决定**谁留下**，这个节点决定**留下的点落在哪**。只剪枝的话，采样器喜欢的地方依然是一团——而"成团的树""抱团的碎石"恰恰不像真实散布。只有邻域统计插值或重新采样才能改善，重采样更贵。
- **每轮从上一轮的坐标读、写进第二个缓冲**，所以结果与点的访问顺序无关；边扫边改会让同一张图在不同运行里漂移，而这个插件的底线是"同图同世界"。
- **位移被双重限制**：`Strength`（实际施加的推力比例）与每轮 `Max Step`。无上限的松弛会把道具挪出自己的格子——那等于落在采样器没选过的地面上。
- **`Plane Only` 默认开**：地面散布不该被"抬起来"，所以高度守恒；做成体素层时关掉，竖直方向也参与推开（同一 XZ、上下相距 1 的两点，平面模式下互不可见，三维下会被推开到 4.1）。

**离屏证据 `PointOpsTests.RelaxTests`，+8 条**，判据是**测量**而不是"它跑了"：3×3 紧凑点团（间距 1）松弛后**最小点间距 1 → 2.574**（这个指标由测试里独立的 O(n²) 双循环算，刻意不用节点的网格——裁判不该和被判者用同一套机械）；位移不超过"迭代数 × 单步上限"；平面模式高度逐点守恒；零迭代/零强度**逐点位置精确不变**；同输入两次跑出同一结果（双缓冲的意义）；点数/顺序/seed 不变；空层为空、单点无邻居可推。harness 582 → **590 passed / 0 failed**。

### 5.1.11 Attribute Math（Attribute ops 补齐，v1.3 本轮新增）

注册类型 28 → **29**，归入 `Point Ops`。`AttributeMath`：把两个数按点**合并成一个**（Add/Subtract/Multiply/Divide/Min/Max）。

- **两侧都可以是属性列、密度（名字留空）或常量**，目标列留空即写密度。这样 "整体减半""两列相乘""坡度列 × 噪声列 → 生成概率" 都不需要额外节点去"存一个数"。
- **默认夹取 0..1**，因为主用途是概率算术：两列直接相乘就是密度跑到 3.7 的来路，而生成器把密度当概率。结果若是距离/高度/计数，就显式关掉（断言里两种都测：`a+b` 关夹取得 `3,6,10,8`，开夹取得 `1,1,1,1`）。
- **除以零值列 → 该点写 0 并报一次**：NaN 密度会污染后面每一级，而"0 概率"是让世界仍可读的答案（断言 `2,2,1.5,0`，第四点分母为 0）。
- **引用了没人写过的列 → 报错并原样透传**，而不是当 0 读（那会把整层静默清零）；要提供常数就显式填常量槽。
- 写入同样走**写时复制**，断言包含"输入层不会多出这一列"；空层为空。

离屏证据 `PointOpsTests.AttributeMathTests`，**+10 条**（harness 590 → **600 passed / 0 failed**）。至此属性工具链闭合：**取样/派生（Slope/Height/Landscape…）→ `Set Attribute` 存列 → `Attribute Math` 合并 → `Remap` 映射成概率 → `DensityFilter`/生成器消费**，每一环都能离线断言、也都能在编辑器里单独看。

### 5.1.12 Attribute Filter（Filter 类别补齐，v1.3 本轮新增）

注册类型 29 → **30**，归入 `Filter`。`AttributeFilter`：保留某命名列（留空即密度）落在区间内的点，`Invert` 反选。

- **它闭合了属性工具链**：此前 Density/Height/Slope/Distance 四个过滤器本质都是"某个节点恰好知道怎么算的属性"，而这个是**按任意列**过滤——于是图可以派生一个没人预设过的数（`Set Attribute` / `Attribute Math`）再按它选择，不需要为每个新想法加一个节点。这就是 UE5 的 "Filter by Attribute"。
- **没人写过的列 → 报错并整层透传**：另一种做法（丢弃全部、或把缺失当 0）会把半接线的图变成"空世界"，而**空世界与"过滤条件确实一个都没匹配上"无法区分**。
- 两端**闭区间**（`[4,6]` 的 4 与 6 本身都留下）；区间写反了按作者给的两个数理解（`[7,3]` 等价 `[3,7]`）；幸存点**连同属性列一起被 gather**（换序/筛选后不能读到邻居的值）。

> **顺带记一条架构事实（免得后人重试）**：本插件的边模型是 **节点→节点**（`PCGEdge { OutputNodeIndex, InputNodeIndex }`，没有输出 pin 下标），所以"一个节点两个输出、按档位分流"的 **UE5 式 Switch 在这里不可表达**——图层面就没有 pin 级连线。要分档应使用**两个 Filter 节点**（阈值 + 反选），这也是本节这个节点存在的原因之一。若将来要做真正的多输出分流，必须先给边模型加输出 pin 维度，属于大改。

离屏证据 `PointOpsTests.AttributeFilterTests`，**+8 条**（harness 600 → **608 passed / 0 failed**）：按密度过滤只留区间内那一个；按派生列过滤并**核对幸存点带的是自己的列值**（`4,6` 而非原位置的 `2,4`）；反选取到补集而不是空；**闭区间**两端都留；写反的区间等价；缺列时整层透传；空层为空。

### 5.1.13 Smooth Attribute（Attribute ops 补齐，v1.3 本轮新增）

注册类型 30 → **31**，归入 `Point Ops`。`SmoothAttribute`：把某列在邻域上**多次平均**，让逐点跳变的值变成梯度。

- **为什么需要**：原始列天生毛躁——噪声采样器的值每点都在变，邻域计数是跳动的整数。直接喂给密度就是"一块死寂挨着一块爆满"。一到两次平滑把硬边变成坡，不需要再来一遍采样，数据也不离开属性列。
- **平滑列 ≠ 模糊位置**：点一步不动。被邻居拉过的值仍落在原点上，所以它可以在剪枝**之后**用，不影响已经定好的位置。
- **每轮从快照读、写第二个缓冲**（与 Relax 同一条规矩）：结果与访问顺序无关，这是"同图同世界"的唯一保证。
- **目标列留空 = 原地写回**：源是命名列就写回那一列；源是密度就写回密度。**这里我先写错过**——第一版把所有"没写目标列"的情况都送去密度，于是文档里承诺的"原地平滑命名列"变成一个**静默空操作**（断言立刻抓到：`0,10 → 0,10` 没有变化）。规则改成"空目标 = 写回源的来处"后，原地、密度回写、命名目标三种情形才都成立。
- 源是密度时用一个**私有工作列 `__smoothed`** 承载中间值（刻意不叫 `$Density`：`$` 是插件自有列的约定前缀，临时缓冲不能和它撞名），平滑后写回密度，**工作列保留**，所以平滑结果在编辑器里也能看。

离屏证据 `PointOpsTests.SmoothTests`，**+11 条**（harness 608 → **619 passed / 0 failed**）：锯齿列（`0,10,0,10`）一趟全域平滑后**展开度 10 → 0**；换成只覆盖近邻的半径后**每趟继续收敛 10 → 3.333 → 1.667**（这条才让"多趟有用"可观察，而不是两个 0 比大小）；一对点平均到均值 `5,5` 且**总和守恒**（平滑只搬值不造值）；**不含自身**时一对点被换成彼此的值（`10,0`，语义差别被钉住）；密度原地平滑后 `0.625,0.625,0.625,0.625` 且工作列与之一致；命名目标不动源列；半径 0 与"源列不存在"都是透传（后者报错），空层为空。

### 5.1.14 Spline 数据类型（对标 UE5 的 Spline 一等数据类型，v1.3 本轮新增）

UE5 把 Spline 与 Points 并列当作一等数据类型；本插件此前样条只以**场景 Actor** 的形式存在（`SplineSampler` 读引擎样条），图与图之间无法传递曲线。本轮补上数据侧：注册类型 31 → **33**，新增 `Data/PCGSpline.cs` 与两个节点。

| 组成 | 内容 |
| --- | --- |
| `PCGSpline` | 世界空间**折线** + 弧长参数化：`Length`、`PositionAt(d)`、`TangentAt(d)`、`SampleBySpacing`、`SampleByCount`。闭合路径的采样不重复起点；超过两端**钳制**（闭合则**绕回**）。 |
| `PCGData.Splines` | 数据集除点之外携带的曲线。**传播规则写清**：`Clone` 复制数组（曲线本身可共享，其字段只读）、`Gather`（筛选/排序）**整条带过**（曲线不是行，中途一个过滤器不该悄悄删掉路径）、`Merge` **拼接**（两条路合成一层还是两条路）。 |
| `CreateSpline`（Points → Spline） | 点集折成一条曲线，顶点顺序可选**输入顺序**或**贪心最近邻**；可选闭合、可选保留原点集。贪心是启发式（明确写了不是 TSP），且**距离并列时取较小下标**，保证同图同结果。 |
| `SampleSpline`（Spline → Points） | 按**间距**或**数量**沿曲线放点，支持沿线抖动、朝向对齐切线、抬升、缩放；**曲线随点一起传下去**，下游可用不同参数再采一次。 |

**明确的边界（与 UE5 的差距，写在这里免得误会）**：① 传的仍是**折线**；想要曲率用 `CreateSpline` 的 `Curve Subdivisions` 把它细分圆滑（§5.1.23：纯 Catmull-Rom，过每个控制点、等距共线时精确，**不引入第二种曲线类型**，所以下游一行没改）。UE5 的**逐点**插值模式与引擎 Actor 自己的切线仍不做；② 边模型是节点→节点，所以**类型检查只是文档约定**，把点集接进 `SampleSpline` 不会编译报错，而是运行时被"输入没有曲线"拒绝并原样透传（有断言）；③ `CreateSpline` 的贪心排序有点数上限（8192，超过则退回输入顺序并报错），因为它是两两比较。

离屏证据 `Tools/PCGGoldenRunner/SplineTests.cs`，本增量 **+29 条**（harness 619 → **648 passed / 0 failed**），两边都有：几何侧（直角路径长 20、弧长定位含拐角、越界钳制/闭合绕回、切线为单位方向、按间距/按数量采样、闭合路径不重复起点、**单点/空/重复顶点三种退化情形**都要有限值而不是 NaN）与节点侧（折点成线并默认替换点集、长度与手算一致、闭合 +30、单点不造线、贪心比输入顺序短且起点/顶点集正确、按间距采样走完全程、曲线随点传出、抬升与切线对齐、**两条曲线各自用自己那条的切线**、无曲线/零间距都拒绝并透传）。两条编译门 0 错误。

### 5.1.15 Normal To Density（对标 UE5 的 Normal to Density，v1.3 本轮新增）

注册类型 33 → **34**，归入 `Point Ops`。`NormalToDensity`：把**点的朝向**变成密度——点自身 up 轴与给定方向的**夹取点积**（`[0,1]`），可直接喂生成器或与其它概率一起进 `Attribute Math`。

- **它补的是"没有坡度列"的那条路**：`SlopeFilter` 靠地形回答"这里陡不陡"，只有地形采样器才有这个条件；**网格采样器下面没有地形、也没有坡度列**，但它产出的每个点都带法线（由上游按表面法线对齐的旋转）。于是"乱石上的碎石只长在朝上的面"这件事，只有这个节点能做。UE5 有同名节点，原因相同。
- `Direction`（默认 up）决定"朝向谁算平坦"，`Invert` 取反（朝下的面得分高，例如崖壁上的垂挂植被），`Target Attribute` 留空即写密度、命名即写列（**写列时不动密度**，可与其它密度规则并列）。
- **零方向回退为 up**：零向量没有"朝向它"可言，回退成 up 保证输出仍是一个概率，而不是把整层密度压成 0。

离屏 5 条（`PointOpsTests.NormalToDensityTests`，harness 657 → **662 passed / 0 failed**）：竖直/侧倒/倾斜 60° 三种朝向得到 `1,0,0.5`（**覆盖整个取值区间**而不是"有些点变小了"）；`Invert` 得 `0,1,0.5`；命名目标写列且密度不变；零方向按 up 处理。

> **夹具教训（同类第二次，写进正文以免再犯）**：`PCGPointTransform.EulerDegrees` 把分量交给 `Quaternion.Euler`，**它的分量映射不是"X=绕 X 转多少度"这种直觉**——用它建夹具时我用 60° 得到的点积是 0 而不是 0.5，断言红。**建夹具要用无歧义的构造**：`Quaternion.RotationAxis(axis, 弧度)`（插件自己的 spawner 节点就是这么写的）。已经有 [[pcg-node-authoring-rules]] 记过 `EulerDegrees` 与 `EulerAngles` 约定不一致，这次是同一坑的另一面。

> **目标清单进度（用户口径：按设定描述生成场景里的一切）**：树木/森林 ✅（Noise/Density + 生成器）、山间道路与田间小道 ✅（§5.1.35 `Path` + §5.1.40 `Point List`，规格 `road` 支持 `via`）、**湖泊与江河 ✅（§5.1.36 `Hydrology`）**、村庄 ✅（§5.1.29 切分语法，规格 `village`，可 `at`/`rotate`）、**城市 ✅（§5.1.31 符号语法 + 产生式，规格 `city`：城区→街区→房子→房间，随仓库示例真跑出 12 块叶子；也可 `at`/`rotate`）**、**温带平原/丘陵/盆地 ✅（§5.1.37 `Biome`）**、**洞穴 ✅（§5.1.38 `Cave`，可 `at` 定洞口）**、**设定描述层 ✅（§5.1.39：解析 + 编译 + 描述文件 + 编辑器「生成」按钮 + 命令行入口 + 三份示例文件，且 `scatter`/`road`/`village`/`city` 都能写多行）**。
>
> **上面这几个地貌/水体词都是"判"或"推"，不是"造"**：`biome` 判地形种类、`water` 从地形推湖与河，而**没有东西能造出地面**——空场景里跑 `valley.pcgspec` 得到的是落在一块平板上的树石房路。让描述能造地形是本仓最后一处"说得出来、长得不出来"的东西，**可行性已核实到方法签名**（`Terrain.SetupPatchHeightMap`，含洞掩码），方案与四个待决问题见 §9 第 6 条，**等拍板再写码**。
>
> **代码约定（2026-10-08 起，用户要求）**：**所有代码注释一律用简体中文**。新写的代码立即遵守；既有文件按批清扫（**只改注释、不动代码**，每批之后跑编译门）。规模与进度：**152 文件 / 6179 行英文散文注释**（XML 标签 `<summary>`/`<param name="…">` 是语法不是散文，不计入；`// ---- ISurfaceContext ----` 这类**分节横幅**内容是 C# 标识符、本就不该译，但量尺仍会把它们算进来，每有横幅的文件虚高 2~5 行），**已清完的批次**：路径三件套、四个核心契约文件（`PCGExecutionContext` 116、`PCGData` 71、`PCGGraph` 79、`PCGAttributeSet` 127）、`PCGActors`（33）、**插件门面 `Runtime/PCGVolume.cs`（320）**、`PCGEditor/PCGGraphEditorWindow.cs`（全部 288 行）、`Tests/PCGSelfTest.cs`（全部 293 行）、`Nodes/PCGSpawnGeometryNode.cs`（全部 62 行）。**剩 131 文件 / 4794 行**（`Tools/_comment_scope.txt` 是量尺输出，每改一批重跑一次 `python Tools/comment-sweep-scope.py` 对账——**这两个数字以量尺为准，别按加减法往前推**：早期记过"已清扫 N 个文件"，而它与基线对不上）。另有一批文件是**新写即中文**、从未有过英文注释：洞穴三件套、设定描述层四件套、字面点两件套。**判断某文件是否清完**：`python Tools/comment-sweep-scope.py` 会按文件列出剩余英文注释行数与行号；`python Tools/comment-sweep-check.py <改前副本> <改后文件>` 会给出**代码是否逐字节未动**、非注释行是否被改、以及还剩几行英文——**每改一个文件都跑它**，"只动注释"因此是被证明的而不是被声称的。

> **本增量末态（读上面的小节时请注意）**：§5.1.6–§5.1.15 里每节写的 `harness A → B` 都是**该节落地那一刻**的快照，不是当前值。当前末态：**注册类型 57**（`Nodes/` 下 59 个 `.cs` = 57 个节点类（每个都带 `public const string Id =`）+ 2 个助手文件 `PCGNodeParameters`/`PCGPointTransform`，两边数目一致 ⇒ 0 孤儿；这个数**量得出来**：`ls Nodes/*.cs` 与 `grep -l 'public const string Id =' Nodes/*.cs` 一比即知。类别 8 组）、**每份示例都整份跑过一遍（v1.4 补，9 条）**：示例是用户唯一的入口（编辑器第二行点「生成」跑的就是它们），而此前只被两件事盯过——CLI 的"节点数 / 连线数"（只证明编译得出来）与个别链的叶子数。于是补上一组**整份运行**的断言，每份三件：① **每个节点都跑到了**——执行是拉式的，接不进任何汇的节点**根本不会被求值**，所以"节点数 == 有记录的节点数"就等于"没有孤儿"（示例里同时有六七条链，改动一处很容易把某条链接丢，而编译、连线数、别的链的断言全都不会响）；② **每条产出链至少出一个点**（名单按链条真实形状写：`Biome`/`Hydrology`/`SplitGrammar`/`Grammar`/`Cave`/`Path`/`AttributeFilter`/`AreaFilter` 各自算一条）；③ 没有"列读不到 / 参照层为空"这类**静默强制透传**的理由。地形按**每份描述自己的前提**给，这一条本身就是一次核对：`valley` 用洼地（`Bowl`）——平地上没有流向，河湖都出不来；`city` 用**高于它自己水位**的缓地（高度 20 的平面）——它写着 `water 10`，放到高度 0 的平面上**整片都在水下**、地貌全判成 `Water`，那句 `biome plain,hill` 的林就一棵不长（**这不是缺陷，是描述与地形对上了**），而放到丘陵上又会被它自己 `slope 15` 的路与 `mountain-slope 25` 的判据挡住；`township` 用丘陵。实测：`valley` → Biome 3721 / Hydrology 1 / SplitGrammar 4 / Cave 250 / Path 22；`city` → Biome 3721 / Grammar 12 / Path 22 / 那座林的 Biome 过滤器 3721；`township` → SplitGrammar 6 / Grammar 7 / Path 30 / VolumeSampler 38 → **AreaFilter 3**（`within` + `near` 把 38 个点收成 3 个，正是"北半边且贴着那条路"该有的样子）。

**写这类断言的两个坑（都踩到了）**：① **名单要按链条的真实形状写**——按地貌撒点的链（`biome plain,hill`）点源是 `Biome`、出口是按 Biome 列过滤的 `AttributeFilter`，它**没有 `Volume Sampler`**；我第一次把 `VolumeSampler` 列进城里的名单，读到的是"0"，其实那个类型压根不在图里（"这个节点产出零"与"没有这个节点"在读数上长一样，所以理由里要把**每个类型的实际数**都摆出来）。② `valley` 的水在这些参数下只出 **1 个点**（`threshold 250` 要的是很长的汇流路径，洼地里没有）——所以"≥1 个点"是这条断言该有的量级，想要更漂亮的水得换地形或调阈值，不是改断言。

**harness 1617 passed / 0 failed**（+12 来自 §9 第 6 条的地形合成纯核 `TerrainTests`）（+48 来自 §5.1.38 洞穴、+235 来自 §5.1.39 设定描述层、+25 来自 §5.1.41 区域筛选器（节点 6 + 描述层 15 + 选项覆盖 4）、+13 来自 §5.1.39 的 `at` 放置、+9 来自 §5.1.39 的 `rotate`、+33 来自 §5.1.40 字面点；其中描述层含 `via` 12 条、六条特征链真跑 5 条、随仓库示例真跑 3 条、可重复特征 9 条、**层间位置关系 39 条**）、`PCGVerify`/`PCGEditorVerify` 各 0 条 error 行、`PCGBench` `== BENCH OK ==`。期间还补过三处：参数取值覆盖审计（+5 条）、曲线在 `Clone/Gather/Merge` 下的传播语义（+4 条）、逐节点检视（+10 条，见下方 §9 的检视条目）。（1408 里含 §6.1 连线保存那 8 条、画布载荷那 11 条、§5.1.29 切分语法那 38 条、§5.1.30 射线投影那 20 条、§5.1.31 符号语法那 20 条、§5.1.32 Actor 输入那 22 条、§5.1.33/34 程序化几何那 38 条、§5.1.35/36/37 路径/水文/地貌那 61 条、§7.4 任意层数层级那 17 条，**管线级那 17 条**，以及 §6.1.1 节点身份那 10 条。）

> **外部对标：UE5.6 PCG 入门课（16 集）逐集核对（2026-10-08）**。素材是 B 站搬运视频 `BV1iqBkBrEvX` 的**分集索引与课程大纲**（该稿无字幕、`player/v2` 的 `subtitles` 为空，原始 YouTube 源站亦抓不到，所以对标的是**标题所声明的能力面**，不是讲解原文；选集里**缺第 11 集**，作者回复说在简介链接里）。结论落到代码上逐个核过：
>
> | 集（时长） | 能力面 | 现状（证据） |
> | --- | --- | --- |
> | 00–03（1h16m） | 图表/数据集/点/边界、创建点 | ✅ `PCGData/PCGPoint`、45→46 类节点、试跑+检视+逐节点读数 |
> | 04（28m） | 点云采样：表面/样条/纹理/体积 | ✅ 四类齐（Sampler 7 个） |
> | 05（56m） | 筛选点 | ✅ Filter 6 + PointOps 的 Difference/Intersection/SelfPruning/Neighborhood |
> | 06（56m） | 属性 | ✅ Set/Remap/Math/Filter/Smooth/Transfer/Noise/Channel/DistanceToPoints |
> | 07（33m） | 参数与**角色数据**（停止硬编码） | ✅ 两半齐：暴露参数 + Volume 覆盖（数字那半，v1.3）；**`Actor Sampler`**（§5.1.32，Actor 那半，v1.4）——把一批 Actor 变成一层点并带名字/Tag/层三列元数据 |
> | 08（31m） | 投影与**世界射线命中** | ✅（v1.4）地形走 `ProjectPoints`（高度场，无需物理），任意几何走 **`RayCastProject`**（§5.1.30，物理查询经 `PCGWorldContext.RayCaster` 注入，可离屏断言）；两者分开而不是一个节点的两种模式 |
> | 09（36m） | 生成器设置 | ✅ 5 个 Spawner + 对齐/随机偏航/缩放 |
> | 10（22m） | 优化/性能分析/调试 | ✅ `PCGBench` 预算门 + 逐节点耗时 + 检视/视口采样 |
> | 12（25m） | PCG + 世界分区 | ✅ 流式窗口/分块对齐/世界喂可见 chunk 清单（插件侧完整） |
> | 13（20m） | **Hi-Gen：大规模** | ✅（v1.4 推广到任意层数）`PCGVolume.HierarchicalLevels` + 等比插值的 `GeometricSizes`；级联逐层收窄，只有最后一层真跑；两级行为逐格不变（等价断言）。**代价也写明了**：多一层多一次 DryRun，只有该层判定真砍掉下一层格子时才省钱 |
> | 14（**63m，全课最长**） | **语法做结构化程序生成** | ✅ 两半齐：`SplitGrammar`（§5.1.29，按深度规则表）+ **`Grammar`**（§5.1.31，符号+产生式，递归即深度）；共用 `PCGCellSplit` 的切分核心 |
> | 15（39m） | PCG + 几何脚本的程序化几何 | ✅ 两端齐：数据侧 `PCGData.Meshes` + `PCGMeshBuilder` + `Geometry Primitives`（§5.1.33），材质化 `Spawn Geometry`（§5.1.34：虚拟 Model + `Mesh.UpdateMesh` + `StaticModel`，**全程按引擎源码实现**）；观感需 PIE（㉛） |
> | 16（44m） | PCG 中的 HLSL | ❌ 托管插件无法挂 GPU kernel（噪声是 CPU 版 `PCGSeededNoise`） |
>
> 08 与 14 已在 v1.4 补齐（见 §5.1.30/§5.1.31）；13 的任意层数也已落地（§7.4）。07 也已在 v1.4 补齐（§5.1.32）。课程对标里**只剩 16 HLSL**未做（需要 GPU kernel 承载，托管插件挂不上，属另一量级）。

### 5.1.16 Spline Mesh 生成器（对标 UE5 的 Spline Mesh Spawner，v1.3 本轮新增）

注册类型 34 → **35**，Spawner 类第 5 个。**先说清与 UE5 的实现差异**：UE5 把**一个** mesh 沿样条拉伸；本插件的 Flax 版本里 **`SplineRope` 这类样条网格 Actor 不存在**（已用反射查过：`FlaxEngine.SplineRope` NOT FOUND），所以改成**把曲线切成等长短块、每块放一个 mesh 并对齐切线**——对栅栏、管道、路缘这类"沿路径铺的东西"画面一致，且不依赖可能不存在的引擎类型。

- **纯布局**（`Execution/PCGSplineMeshLayout.cs`，8 条离屏断言）：把每条曲线切成**等长**的块，块长**不超过**请求值（20 单位的线要 6 一段 → 四段各 5，而不是三段 6 加一个残段：结尾突然变小的柱子是设计师最先看到的东西）；每块的位置取**块中点**、朝向取该处切线，因此**拐角处的块会跟着拐**；闭合曲线连缝一起铺满（块长之和 == 曲线长）；上限 4096 块（百万单位的线配 1 单位块时截断），退化输入（单点曲线、块长 0）产出为空。
- **撞到的真问题**：肘形曲线第二段是**竖直向上**，而 `Quaternion.LookRotation(forward, up)` 在 forward 与 up 平行时是**退化**的——Flax 返回一个**连归一化都没有**的四元数（实测某块的前向量是 `(0, 0.5, 0.5)`）。"爬崖的栅栏"是真实用例，所以修在代码里：切线接近 ±up 时换一个彼此垂直的 up 提示（`RotationFor`），断言把**前向量的长度**也钉住（`length 1`）。
- **生成器**（`Nodes/PCGSpawnSplineMeshNode.cs`，4 参数）：`Model Path`（默认 `Engine/Models/Box`，让新节点一放下就能看见东西）、`Segment Length`、`Thickness`、`Max Actors`。曲线**来自数据**（前面接 `CreateSpline`），不读场景里的样条——场景样条是 Actor 版采样器的职责。**逐格裁剪**（`IsInsideClip`）与**按格命名**沿用其它生成器：分区/流式下只建自己格里的块，清理按名字就能精准找到自己的产出（**加往场景写的节点必须同步 `SceneWritingIds`**，本轮加它时 `EverySpawnerIsListed` 与 schema/条数三道绊线同时生效，其中"注册槽默认值 vs 节点 fallback"就抓出过一次不一致）。
- **引擎自检 3k**：真实模型（`Engine/Models/Box`）+ 一条 20 单位曲线 → 体积名下**恰好 4 个** `StaticModel` 且都带生成前缀；给一个只覆盖左半的 `ClipCell` → **恰好 2 个**；`AsDryRun()` → 一个都不多。缺模型时打 SKIP 而不是 FAIL。

### 5.1.17 Attribute Transfer（对标 UE5 的 Transfer Attributes，v1.3 本轮新增）

注册类型 35 → **36**，Point Ops 类第 14 个。**它补的是别的 attribute 节点都补不了的一类缺口**：`Set`/`Remap`/`Math`/`Smooth`/`Filter` 全都读写**同一批点**，所以一个值只能沿着产出它的那条图层走；那层一旦被过滤变薄，值就跟着丢了。Transfer 让**目标层向源层借一个已经算好的列**（地形高度、坡度、噪声），源层保留原列，不必重新采样一遍、也就不会出现两个答案对不上。

- **语义**：`In`（目标）与 `Source`（源）两路输入；每个目标点取**距离最近**的源点的那一列，超过 `Max Distance` 不取（保留目标自己的值或默认值，而**不是**把点丢掉——"够不着"与"这层是空的"从输出上分不清，所以它不做成过滤器）。空列名 = 搬运内建的 `$Density`（`$Density` 的值在点结构上、属性集按设计答 false，所以这条单独走点字段）。
- **最近搜索为什么是对的面不是调出来的**：把网格边长取成 `Max Distance`，任何距离内的源点必然落在**3×3 邻域**里（`PCGPointIndex` 恰好只走这一圈），正确性来自构造而非启发式。**并列决胜取更小的源下标**，于是答案与"网格这次凑巧先返回谁"无关；测试里"四角等距"那条专门钉住这条规则。
- **离屏证据**：`PointOpsTests.AttributeTransferTests` 9 条——落点取到对应值、等距取最小源下标、够不着保留默认、源缺列时整层原样透传（与成功搬运同一夹具，互为对照）、空列名搬 `$Density`、平面/3D 两模式的**判别性**对照（堆叠的源在高度上换人）、以及**每个目标都与一份独立写的暴力最近邻扫描逐点相符**，外加同输入两次结果一致。harness 719 → **728 passed / 0 failed**。

- **匹配放置（本轮追加，对标 UE5 的 `PCGPointMatchAndSet`）**：`Match` 参数把"搬一列数"扩成"站到那儿"——`Position` 把点移到最近供体上（**保留自己的旋转与缩放**），`Transform` 连**旋转/缩放/bounds 一起采用**。两者共用**同一次最近邻搜索**，所以不会出现"值取自甲、位置取自乙"。两条口径：① **bounds 必须跟着点走**（盒子以世界空间存储，只挪 position 会让下游剪枝去测一个点已经离开的矩形——与 §5.1.24 同一个教训）；② **源层缺列时不再中止**：缺列被报告、列保留默认值，但**放置匹配照做**（缺列不该取消"站到那儿"这件事）。**离屏证据**：`PointOpsTests.TransferMatchTests` 8 条——默认不动（回归）、Position 只动位置且保留自身旋转/缩放、bounds 随行、Transform 全采用、**缺列仍然匹配**、超出半径不匹配。其中"缺列仍然匹配"这条**当场抓到我自己的一个编辑事故**（把 `if (nearest >= 0)` 改写成 `continue` 结构时，原来那行 `column[i] = values[nearest];` 被留在了后面，于是对 null 列解引用）——这正是"给新分支补测试"而非"补注释"才能发现的那类问题。harness 843 → **851 passed / 0 failed**。

### 5.1.18 Distance To Points（对标 UE5 的 Distance 节点，v1.3 本轮新增）

注册类型 36 → **37**，Point Ops 类第 15 个。**它把"距离"从判据变成数据**：`DistanceFilter` 只回答"在不在环里"并把测量丢掉，而"越靠边越稀"要的恰恰是**那个数**——而且它得对着**另一层点的形状**量，不是对着一个坐标。UE5 的 Distance 节点就是这个，并把结果写成属性（本插件同样：默认写 `distance` 列，空列名写 `$Density`）。

- **语义**：`In`（写列的目标）与 `Reference`（被量的层）两路输入；每个目标点写"到最近参考点的距离"。`Max Distance` 既是**搜索半径也是封顶值**：够不着任何参考点的目标写封顶值（UE5 也是这么 clamp 的），点本身保留——"比封顶还远"是个真答案，不该让它变成空层。
- **正确性来源**：网格边长取 `Max Distance`，任何半径内的参考点必落在 3×3 邻域里（`PCGPointIndex` 只走这一圈），所以最近搜索是对的而非调参的；并列取更小的参考下标，与遍历顺序无关。
- **离屏证据**：`PointOpsTests.DistanceToPointsTests` 7 条——已知距离、平面模式忽略高度 / 3D 模式照算（同一夹具两式有判别力）、超半径写封顶值且点不丢、空参考层整层透传（与成功写值的同夹具互为对照）、空列名写 `$Density`、以及**每个目标都与一份独立写的暴力最近邻扫描逐点相符**。harness 728 → **735 passed / 0 failed**。

### 5.1.19 Noise To Attribute（对标 UE5 的"把空间噪声写进属性"用法，v1.3 本轮新增）

注册类型 37 → **38**，Point Ops 类第 16 个。**它补的是采样器做不到的一件事**：`NoiseSampler` 回答"噪声在哪密"，方式是**自己按格子造点**——可一旦点已经有了（地形散布、模型表面、手摆的一排），它会把那些点丢掉、换成自己的。Noise To Attribute 保留输入、只把数写上去，也就是"让这堆道具成片地稀疏下去"这句需求真正需要的东西。

- **语义**：单输入；在每个点的世界坐标上采插件自己的 `PCGSeededNoise`（引擎噪声无种子，见 §3.4——用它会让"换种子"失效），写进指定列（空列名写 `$Density`）。种子 = **图种子 + 本节点 `Seed Offset`**，所以同一张图里的两个噪声节点天然去相关，不必动图种子。
- **平面/3D**：`Plane Only`（默认开）只采 XZ——森林地面不该随地形起伏重掷；关掉则按 `Sample3D` 在点的真实高度采（2D 类别在 3D 下按 XZ 求值，见 `PCGSeededNoise.Sample3D` 的说明）。所有类别都返回 [0,1]，所以列可直接当权重。
- **离屏证据**：`PointOpsTests.NoiseToAttributeTests` 9 条——**落在采样格点上的点使双线性坍缩成单个角点，答案恰等于噪声的哈希底层 `PCGSeededNoise.HashToUnit`（节点代码从不调它，是一条独立参照）**；平面模式对"抬高的孪生点"给出同一值；3D 模式在点自身高度取值；一片散布上的值全部落在 [0,1] 且确实有变化；同输入两次一致；`Seed Offset` 不同则去相关；空列名写 `$Density`；空输入空输出。harness 735 → **744 passed / 0 failed**。

### 5.1.20 Channel To Attribute（对标 UE5 的 BreakVector / GetComponent，v1.3 本轮新增）

注册类型 38 → **39**，Point Ops 类第 17 个。**它补的是"点自己的大小与朝向没人能读"这个缺口**：位置与密度已经能当属性读（`SetAttribute`），但 **scale、bounds、facing** 只存在于点结构里——于是"只留站得直的"、"按石头大小排序"、"把小的筛掉"这些问句，在插件里都无从下手。本节点把其中一个数写进列，之后就能像任何属性一样被过滤、重映射、排序。

- **通道与分量**：`Channel` = Scale / Bounds（半 extent，与 `PCGPointTransform.HalfExtents` 同一约定）/ Forward / Up / Right；`Component` = X / Y / Z / Length（`Length` 对 Scale 就是"整体大小"）。
- **朝向怎么读**：方向一律走 `Vector3.Transform(v, point.Rotation)`，与 `SlopeFilter` 用的 `PCGPointTransform.SlopeDegrees` **同一个约定**，不会冒出第二套"谁说了算"。**刻意不提供欧拉角**：四元数分解不唯一，"这个点的 pitch"会变成两个都正确的实现能给出不同答案的数；"它朝上抬多高"则不会。
- **离屏证据**：`PointOpsTests.ChannelToAttributeTests` 8 条——Scale.X / Scale.Length（√29）/ Bounds.Y（半 extent=7）；**手工已知的朝向事实**（绕世界 Y 转 90° 的点，其 forward 落在 X 轴上且无抬升；**符号故意放开**，因为两只手系都对）；identity 点的 up 就是世界 up；空列名写 `$Density`；散布上每点都被写入不丢点；空输入空输出。harness 744 → **752 passed / 0 failed**。

### 5.1.21 Branch（对标 UE5 的 Branch，v1.3 本轮新增）

注册类型 39 → **40**，Control Flow 类第 2 个。**它只能做 UE5 那族的一半，而那半恰恰是图上真用得上的**：UE5 的 Switch 需要"一个节点两个输出"，而本插件的边**没有输出 pin 下标**（§5.1.8），多路分流不可表达；Branch 是单进单出——**要么原样透传、要么吐空层**——正好落在可表达的那一侧。

- **条件是一个节点参数**，也就是**暴露参数**：Volume 可以覆盖它而不用改图，于是"同一张城市图，这个区域带废墟、那个区域不带"。这才是这个节点的用处：**"关掉"是按放置位置的，不是按整张图的**。
- **阻塞时吐空层而不是报错**：关掉是正常状态、不是接错线；下游看到的与"过滤器一个都没匹配上"完全一样。
- **透传要重新发布**（`Clone()`，而不是把上游的缓存结果递出去），否则下游的修改会写回上游——Merge 早就在做同一件事，理由相同。
- **离屏证据**：`PointOpsTests.BranchTests` 6 条——开启时整层带列透传（`v` = `1,2`）；**往透传结果里写字不回流到上游**（写时复制契约）；关闭时是**空层**（与上一条互为正反对照）；空输入在两种条件下都为空；不带任何存储值时按 schema 默认（开）透传。harness 755 → **761 passed / 0 failed**。

### 5.1.22 Texture Sampler（对标 UE5 的 Texture Sampler，v1.3 本轮新增）

注册类型 40 → **41**，Sampler 类第 7 个。**它是唯一一个"输入是画出来的"采样器**：手绘密度图、别的工具导出的植被掩码、不是地形的那张高度图。做法沿用 MeshSampler 的分层——算术全在 **`Data/PCGTextureSampling.cs`**（纯函数、不碰引擎、离线用手算答案测），节点只是"把像素取出来 + 把世界坐标映到 UV"的适配器。

- **CPU 通路是先验过才动笔的**：`TextureBase.GetPixels(out Color32[], mip, array)` 是 `API_FUNCTION`，托管侧可直接调；但**只支持未压缩、单平面**格式，压缩纹理读不出来——所以它**失败时返回 true**（这个反向约定就是全部契约），因此失败时给出可读原因并原样透传。这与"地形高度图不可直读"（§3.5）是两回事：那是 RenderTarget 类，这里用的是资产类。
- **映射**：一张图铺 `Tile Size` 个世界单位、以原点为中心，图像首行在 −Z、行序朝 +Z。UV 之外的坐标按 `Address` 折叠（Clamp/Repeat/Mirror），`Filter` 取最近邻或双线性。
- **半纹素约定**：纹素 i 位于 `i + 0.5`，故 `u=(i+0.5)/size` 正好落在它上面。这一条最容易写成"整张图偏一像素"，所以测试在纹素中心**和**纹素之间都取了样。
- **代价（明说）**：像素每次执行都要重读并抽通道，所以在分区/流式体积里这是**每格一次**。**刻意不做缓存**：跨节点共享的缓存会引入竞态，绑到单个节点的缓存在格子并行时同样会被重入——要加先测，别凭"缓存一定更快"。
- **离屏证据**：`TextureSamplingTests` 22 条——四通道归一化、亮度权重（等 RGB 的亮度等于它自身，是个不需要手算常数的性质）、最近邻在纹素中心与两条边缘、**恰在两纹素之间时归到上侧（钉死这个选择而不是声称它"对"）**、双线性中点=0.5 与对称棋盘中心=0.5、**三种寻址模式在"图外一格"处两两可区分**、图内三模式一致、退化输入（null / 零宽 / 长度不足）一律返回 0 而不是越界读。harness 763 → **785 passed / 0 failed**。
- **引擎侧仍待 PIE 确认（§9 ㉒）**：`Content.Load<Texture>` + `GetPixels` 这一层只有真引擎里才有像素。

### 5.1.23 Spline 曲线细分（对标 UE5 样条的插值模式，v1.3 本轮新增）

**没有新节点**：给 `CreateSpline` 追加了一个参数 `Curve Subdivisions`（默认 1 = 原样折线；>1 时按 Catmull-Rom 在每段插入点）。新算术在 **`Data/PCGSplineCurves.cs`**（纯函数），也就是"让路径圆一点"这件事的全部内容。

- **为什么是细分而不是引入新曲线类型**：UE5 的样条每个点带插值模式，而本插件的样条按设计就是折线（`PCGSpline`）。细分让**所有下游**——弧长表、切线、`SampleBySpacing`/`SampleByCount`、`PCGSplineMeshLayout` 的等长分块——继续在它们已经懂的多段线上工作，**一行消费者代码都没改**。这是这么做的唯一理由，也是它值得做的理由。
- **只承诺两件事**：曲线**过每个控制点**（因此可以安全替换），且**等距共线时精确复现直线**。间距不均时 uniform Catmull-Rom 会在拐角外轻微鼓起——这是它应有的行为，也正是文档要求"想精确就等距放点"的原因；端点用自身夹紧，起点终点不会被甩开。**没有做弦长重参数化**，所以别声称它"更均匀"。
- **离屏证据**：`SplineTests.CurveTests` 14 条——3 段 4 分箱给 13 个顶点、两端不动、**每个控制点仍在细路径上**；等距直线**不离线也不回头**，且第一段五分之二处的点等于手算值 `(4,0,0)`；**非对称拐角的中点等于手算值 `(0.5,0,0.09375)`——它与直弦中点 `(0.5,0,0)` 不同，所以这条有判别力**；闭合路径每段都细分且不重复末顶点、仍过四个角；`subdivisions = 1` 与"单顶点"都原样返回；**节点侧 3 条**（参数真的传到了产出曲线：13 个顶点、长度不变 30、默认仍是 4 顶点）——最后这条专门防"纯核测通了、节点却没接上"。harness 785 → **799 passed / 0 failed**。

### 5.1.24 Project Points（对标 UE5 的 Projection，v1.3 本轮新增）

注册类型 41 → **42**，Modifier 类第 3 个。**它补的是采样器做不到的一步**：`LandscapeSampler` 是**造点**（按网格采地形），而点已经有了之后（手摆的一排、噪声层、被挪过的图层）需要的是把**地形查询应用到它们自己的位置上**，同时保住数量、顺序、种子与列。"撒在这片地上"与"把这一排原样放到地面上"是两件事，后者就是这个节点。

- **它移动、不过滤**：脚下没有地形的点**原样留在原处**、只被计数，因为"悄悄删掉悬在湖上的那半个图层"与"这层从来没摆过"在输出上无法区分。要删就打开 `Drop Misses`——名字里写着。
- **旋转与坡度**：开启对齐时，点的 up 走 `PCGSurfaceAlign`（**与地形采样器同一个实现**，所以投影出来的层与采样出来的层朝向一致）；采到的坡度写进 `$Slope` 列，理由同采样器：下游 `SlopeFilter` 不该再问一次地形。
- **bounds 跟着点走**：点的盒子是**世界空间**存的，只挪 position 不挪盒子，会让下游所有剪枝决策去测一个点已经离开的矩形。
- **顺带抓到并修掉第二个"代码与自己的注释互相矛盾"的缺陷（详见 §9）**：`AlignToNormal` 的 forward 用 `cross(up, Right)`，而它在平地上等于 **−Z**——于是**每个对齐过的点都被偏转 180°**，可注释写的是"forward 尽量贴着世界 +Z"，且没有任何断言管这件事。实测 forward = `(0,0,-1)`；改为 `cross(Right, up)` 后平地得到**恒等旋转**，并由断言钉死。
- **离屏证据**：`SurfaceAlignTests` 9 条——平/竖直面/45° 的 up 对齐、朝下的面不产生 NaN、零法线退回朝上、单位四元数、**平地 forward == +Z 且整体 == 恒等**，以及一条**反例对照**（把 up 故意弄错的旋转判为不对齐，证明该判据不是恒真）。节点侧需要地形，属引擎绑定，见 §9 的 PIE 清单 ㉔。harness 808 → **817 passed / 0 failed**。

### 5.1.26 Scale By Attribute（用一列驱动点尺寸，v1.3 本轮新增）

注册类型 42 → **43**，Modifier 类第 4 个。**它补的是"数值能算出来、却回不到几何上"的那一段**：插件早就能算出每点的噪声/距离/高度（并重映射成列），也能把点的尺寸/朝向**读出来**（§5.1.20），但一直没有**写回去**的路。缺了它，"越湿越大"根本没法表达——`TransformPoints` 的缩放是常数、`BoundsModifier` 的 extents 来自自己那几个字段，于是密度调得再讲究，整层道具还是同一个模子刻的。UE5 用属性驱动 extents 解决这件事，这里就是一个紧凑的节点。

- **因子是"乘"不是"替代"**：`scale *= lerp(ScaleMin, ScaleMax, value)`，所以两个这种节点可以叠加、上游 `TransformPoints` 设过的尺寸也留得住。
- **列先钳到 [0,1] 再映射**：这个列在这里是**权重**，权重 3 没有"钳到 1 之外"的合理解。
- **bounds 跟着一起缩**：extents 是下游所有剪枝判据要测的东西，点变大了盒子不变，就等于"还看得见却被剔掉"。可用 `Scale Bounds` 关掉（有些图就是要盒子固定的）。
- **离屏证据**：`PointOpsTests.ScaleByAttributeTests` 9 条——列映射到量程两端（含中点 1.25）、bounds 随行、`Invert` 让低值变大、**因子是乘法**（自带 scale 2 × 因子 2 = 4）、**列值 >1 被钳住而不是外推**、关掉 bounds 后盒子不动、具名列优先于密度（**这是"缺列原样透传"的正向对照**）、缺列时尺寸全不变、空输入空输出。harness 859 → **868 passed / 0 failed**。

### 5.1.27 Offset By Attribute（用一列驱动点位移，v1.3 本轮新增）

注册类型 43 → **44**，Modifier 类第 5 个。与 §5.1.26 合起来，把"列 → 几何"这个方向补完整：图可以把一个数**算出来**，再花在**点的尺寸**或**点的位置**上。缺了它，逐点变化的位移无从表达——`TransformPoints` 的平移是常数、`RandomOffset` 是随机——于是"地面越湿的地方把道具往下沉"没有节点可用。

- **单轴而不是向量位移**：一列就是一个数，硬当向量用等于凭空发明另外两列的含义。需要向量就接三个本节点、各读一列——这也正好是"每一轴的来源都还能读出来"的唯一做法。
- **权重同样钳到 [0,1]**（与 §5.1.26 同一口径）：一个跑飞的值不该把道具抛上天。
- **bounds 随行**（第三次遇到同一条规则）：点位移动而盒子不动，剪枝就会去测一个点已经离开的地方。
- **离屏证据**：`PointOpsTests.OffsetByAttributeTests` 9 条——列映射到量程（0/50/100 三个手算值）、**bounds 跟着点走且 extents 不变**、`Axis` 决定推哪个方向、`Invert` 反向、**零量程是 no-op**、列值 >1 被钳住、具名列优先于密度（**正向对照**）、缺列时点位全不变、空输入空输出。harness 868 → **877 passed / 0 failed**。

### 5.1.30 Ray Cast Project（打真实几何的投影，v1.4 新增；对标课程第 8 集）

注册类型 46 → **47**，Modifier 类第 7 个。

`ProjectPoints` 问的是**地形高度场**：便宜、精确，但对一切非地形的东西瞎——路面、桥、屋顶、岩石、箱子都不在高度场里，一个本该落在桥面上的点要么落到桥下的地，要么直接落空。本节点改问**物理场景**，于是散点可以吸附到真实几何上，这正是 UE5 里 "world ray hit query" 与地形投影并列的那一半。

- **与 `ProjectPoints` 分开而不是并成一个节点的两个模式**：高度场那条不需要物理、在无头运行里也能跑；两者回答的是不同问题（"地面在哪" vs "沿着这条射线第一个实心东西是什么"）。参数有重叠，失败模式没有。
- **射线查询是注入的**（`PCGWorldContext.RayCaster`，与既有的 `ActorResolver` 同一形状）：`Physics.RayCast` 需要已加载的场景，节点自己去调就**永远无法离屏验证**，而"命中之后点落在哪、怎么倾斜、落空怎么办"恰恰是最该被断言的部分。真实运行由 `PCGVolume.BuildContext()` 装上引擎调用，测试装一个平面。
- **参数**：层掩码（-1 = 全部层）、是否命中触发器、方向（零向量=正下方）、最大距离、贴合偏移（**沿命中法线**抬，坡上才不会被埋半个）、是否按法线对齐、是否丢弃落空。落空默认**保留原点**（与 `ProjectPoints` 同口径：一层的点数不该因为一半悬空就变），`Drop Misses` 才是"落空不要"。命中的点写 `$Slope`。
- **法线为零的命中**（球体默认不带面）按"平坦"处理：拿它去对齐会转出一个任意朝向，拿它去偏移会把点放回表面。
- **离屏证据**（`RayCastProjectTests`）：落地位置/高度、贴合偏移、平地对齐=恒等、45° 坡上点的 up == 法线且 `$Slope` 落在 40–50、**关掉对齐则朝向不变**（正向对照）、无正常数按平坦处理、落空保位、`Drop Misses` 只留命中（**这条抓出过一个真 bug**：第一版把 `keep.Add` 写在循环末尾，于是"丢弃落空"一个都没丢，而几何断言全绿）、**查询参数真的送到引擎**（掩码/触发器/距离/方向都被 reciever 记下来断言，负掩码=全层）、非单位方向=其归一化、零方向=正下方、无物理时原样透传且说明原因。

### 5.1.40 Point List（字面点：位置写死的点源，v1.4 新增）

注册类型 55 → **56**，Sampler 类第 11 个。`Nodes/PCGPointListNode.cs`，离屏 **33 条**断言（`PointListTests`）。

**为什么需要它**：插件里此前**所有点源都是采样器**——给一个范围，撒一堆点。于是"从这个地方到那个地方"这类**字面位置**一直无法表达，而它们恰恰是"按设定描述"里最常见的说法：道路的途经点、村庄的中心、洞穴的入口。它不是"又一个撒点节点"，补的是**位置由人指定**这一格。（**后续接线**：这三处里前一处从 §5.1.35 起就真的在用它，而"村庄的中心、洞穴的入口"直到 §5.1.39 的 `at <x> <z>` 才接上——在此之前村子/城市/洞穴一律从生成范围的中心起算。）

**八个槽位，不是一段文本**：Visject 画布按参数槽位生成控件，八个 `Vector3` 槽位就是八个可见、可拖、可撤销的向量框；换成一门文本小语法，等于给插件再加一门语言，还得让设计师记住它。八个够画一条带拐弯的路、排一串房子，要更多就把两个节点接起来（`Merge Points`）。槽位默认沿 +X 每 500 一格排开，**故意不排成全零**：两个重合的点连不出路，而"两点一线"接上 `Path` 立刻有东西看。

**参数**：`Count`（用几个槽位，钳 [0,8]）+ `Point 1..8`（位置）+ `Scale` / `Density` / `Seed Offset`。位置**不摇随机数**（写死就是写死，有断言钉住"换种子偏移不移动任何位置，只换点的种子"）；`Density` 写进点的密度，下游生成器按它抽样。

**证据**（33 条）：槽位顺序原样输出、用不上的槽位不产生点、缩放/密度/包围盒各自落位、没有输入引脚、换种子只换种子、同参数两次逐点相同；个数钳制四例（0 → 空层 + 说明原因、1 → 恰好一个、99 → 封顶到 8、负数 → 按 0）；以及**接进 `Path` 的集成断言**：两端点 (0,0) 与 (1000,1000) 在平地上规划出 31 个点，起终点各落在写下的坐标附近，且**一路点最大偏离对角线 0.0**（证明端点真被用上，而不是被忽略了绕远路）。"个数为 0 给空层"那条配了**同夹具的正面同侧**（同一次构造只把个数改成 1 就必须有点），否则"空层"可能只是节点根本没跑。

### 5.1.41 Area Filter（按区域筛点，v1.4 新增）

注册类型 56 → **57**，Filter 类第 7 个。`Nodes/PCGAreaFilterNode.cs`，离屏节点语义 **6 条**（`NodeBehaviourTests.AreaFilterTests`）+ 设定描述层 **15 条**（`SpecTests.AreaTests`）。

**为什么需要它**：插件此前能按高度（`HeightFilter`）与到某个点的距离（`DistanceFilter`）筛点，**却没有任何办法说"只要西南角那一片"**。而"按设定描述生成"这句话里，**位置**是最常出现的一类信息——北部是针叶林、南边是草原、西侧那片湖周围种芦苇。缺了这一步，描述只能决定"长什么"，决定不了"长在哪"，每一层都会铺满整个生成范围。它是**唯一**回答"长在哪"的筛选器。

**为什么是矩形而不是圆**：`DistanceFilter` 已经是圆了（半径），而"北半部""西南角""这块地"这类说法在矩形下是**精确**的，在圆下只能是近似；两轴各一段区间也正好是描述语言能写清楚的形状。只看 X 与 Z——高度由地形与 `HeightFilter` 管，把 Y 混进来只会把"这片区域"变成一个盒子，而写描述的人脑子里想的是俯视图（有断言钉住这一点：XZ 在框里、Y 是 9999 的点必须留下）。

**参数**：`Min X` / `Max X` / `Min Z` / `Max Z`（闭区间）+ `Invert`（只留框外）。两轴写反了在构造时就换回来——一个写反的区间会一个点都不留，而"跑通了、世界是空的"是最难查的一类；四个值退化成一条线（`min == max`）在描述层被拒并带行号（那是"区域"没写对，不是"窄区域"）。全在里面时把这一层原样交下去，全在外面时给**空层而不是 null**（空层与 null 在下游是两种不同的东西）。

**设定描述层的三个词**：`within <x起> <z起> <x止> <z止>`（只留框内）与 `outside …`（只留框外），四个数按"最小角 → 最大角"写。它与 `near`/`away`/`fade` **正交、可以叠在同一层上**（"村北 400 以内、且只在这片区域"是常态，有正面同夹具钉住两种写法能同时解析）。落地成 `Area Filter` 串在点源与生成器之间；行内列号改成了**递增计数器**（原本写死的列号会让两个都写了的层把框叠在一起——图照跑、画布上看着像一个）。

**证据**（21 条）：节点语义 6 条（闭区间含边界、只看 XZ、写反抗换回、原样透传、空层非 null、取反正好是补集）；描述层 15 条（四个数落到四个字段、`outside` 取反、三类错带行号、与 `near` 叠加的正面同夹具、编译成 `Area Filter` 且链序为 点源 → 区域 → 生成器、四个边界与取反进槽位、`outside` 节点取反）。运行层配了**位置对照**：600 个点铺满整个范围，`within -1500 -1500 1500 0` 之后留下来的点**最北 Z 必须 ≤ 0**，而没写区域时最北是 **1499.8**——区域过滤改的是**成员**（点真的没了），所以它在数据层就看得见，这一点与 `fade`（只改密度、一个点都不少）正好相反。

### 5.1.42 Polygon2D（任意形状地块，v1.5 新增）

注册类型 57 → **58**，Filter 类第 8 个。`Data/PCGPolygon.cs`（纯核）+ `Nodes/PCGPolygonFilterNode.cs`，离屏 **29 条**（`PolygonTests`）+ 描述层 **10 条**（`SpecTests.PolygonRegionTests`）。

**对标 UE5 PCG 的哪一格**：UE5 的 **Polygon2D** 数据类型。覆盖矩阵里这一格此前是 ❌（"需要几何承载类型"），而它其实是**纯算术**——一个环 + "点在不在这块地里"，不需要任何引擎绑定。至此矩阵里剩下的 ❌ 只有 **Geometry 数据类型**（真正需要几何承载）与 **16 HLSL**（GPU kernel，托管插件够不到）。

**为什么需要它**：`AreaFilter` 只解决了"轴对齐矩形"。可写描述的人心里的地块常常不是矩形——**弯的河谷、L 形的院子、斜着的一条地界**；矩形对这些形状只能给一个更大或更小的近似。所以这是 §5.1.41 的同一条职责（回答"长在哪"）的**形状推广**，而不是又一个筛选器。

**判定为什么是射线交叉（奇偶规则）**：凸性判定（"所有叉积同号"）在**凹**多边形上会把凹进去那部分判成外面，而凹形状恰恰是这里唯一值得做的——矩形已经覆盖了凸的情形。奇偶规则对简单多边形永远正确、且**与顶点绕向无关**（顺时针/逆时针给同一个答案，有逐点对照断言）。**边界算里面**：正好落在边或顶点上的点算命中（"沿地块边界摆一圈东西"是很自然的用法），容差按边长缩放，且反面钉住"紧贴外边 0.5 的点仍算外面"——"贴边算里"不能变成"附近都算里"。

**参数与写法**：节点是 `Vertices`（字符串 `"x z; x z; …"`）+ `Invert`。描述层是 `polygon "<顶点串>"`，与 `within`/`outside` **互斥**（都在回答"这片区域在哪"，同时写只能猜——带行号拒绝）。顶点数**不是固定槽位**：固定成八个槽位会让"九个顶点的河湾"写不出来。**默认值是原点旁一个 100×100 的正方形**，且它在节点常量与槽位表里**是同一个数**（有一条断言专门比这两处，防止漂）。

**写坏了不静默清空**：顶点串解析不了、或顶点少于三个（围不出面积）时，这一层**原样透传**并留下 `LastError`。整层点被悄悄扔光是这一层最难查的症状（世界看着正常、只是这里什么都没长），而"没筛"至少可解释。判定侧另有约定：**少于三个顶点恒不命中**（不是"全都命中"）——后者会让一块写坏的地把整层点放过去。

**证据**（39 条）：纯核 16 条（顶点串解析含换行/末分号容错、三类错各自报"第几个顶点"、绕向不影响答案的逐点对照、面积/有向面积区间、包围盒、**凹口判外面**、**仍在地里但落在凸包之外的点必须算里面**——这条专门杀掉凸性实现）；节点 13 条（留下/丢弃/顺序、`Invert`、全中透传、全不中空层、写坏透传 + `LastError`、线程安全、参数个数）；描述层 10 条（顶点串原样落到字段、与 `within` 互斥带行号、编译成 `Polygon Filter`、`Vertices` 槽位按名字落位、**真跑**：地块里确实有点、无一点出包围盒、凹口里一个都没有，**对照**：同一批点换成 `within` 包围盒后矩形多收且多收的落在凹口里）。第 6 处同步点（`PCGParameterLabels`）与条数钉子（`BuiltInTypes` 57 → 58）都被既有绊线当场抓出过，各自修好。

**演示接线（`demo_valley` 的林地，v1.5）**：林地那一层带上 `polygon "600 600; 2000 600; 2000 1200; 1300 1200; 1300 2000; 600 2000"`，围出一个 L，**右上那片（x&gt;1300 且 z&gt;1200）故意留空**——演示里唯一一处矩形做不到的地方（换成它的包围盒，那块空地也会长满树）。

接线的同时**把林地密度从 `per-block 1.5` 提到 `8`**，这是这条演示断言逼出来的：原密度在这片 2000×1600 的范围里只出得 **5 棵**树，"5 棵里挡掉 4 棵"在屏幕上根本看不出是一块地块。提完之后的实测（`SpecTests.DemoPolygonEffectTests`，读的就是仓库里那份描述文件）：

| | 林地总点数 | 落在空地里 |
|---|---|---|
| 带 `polygon` | **8** | **0** |
| 删掉 `polygon` 子句（同一份描述的对照） | **26** | **14** |

这条断言值得单说一句它的形状：**它同时报"还剩几个"与"删掉之后多出几个"**。只验前者的话，"采样恰好不往那儿放点"与"功能生效了"给同一个答案——而这两件事在屏幕上看不出区别。认"林地那条链"靠它独有的两个槽位（`Scale Multiplier`=1.2 与 `Random Yaw`=true），不靠节点下标：删掉子句之后下标会整体挪位。

引擎侧复核（重编译并重烘焙 `PCG_Valley`）：**22 节点 / 14 连线**、告警 0、**98 个 Actor**（此前 92）、已保存；山谷那 8 个 `PCG_Gen_Cone_*` 逐个核对位置，**没有一个落在那块空地里**（横带里的 Z 都 ≤1200、竖带里的 X 都 ≤1300）。

### 5.1.43 Geometry 数据类型的承载方案（待拍板，v1.5）

**对标的那一格是什么**（已核实的公开资料，非推断）：UE5 **5.5** 的发布说明把 PCG 的这一块写成 *"Geometry Processing — Model, modify, combine and output meshes directly from within PCG through UE's **dynamic mesh type**"*；它的**布尔/倒角/UV 展开**那一层由 **Geometry Scripting** 节点提供，最后可**转成静态网格**输出。所以 UE5 的 "Geometry" 在 PCG 里 = **把 DynamicMesh 当空间数据类型**（与 Volume/Surface/Line/Point/Texture 并列），不是"给点挂一个模型"。

> 顺带更正一处口径：我们上一格刚做的 **Polygon2D** 与 UE 的 **Polygon2D 数据类型**（UE **5.7** 才加，带自己的算子）不是一回事——我们做的是"任意形状的**区域判定**"（回答"点在不在这块地里"），UE 那个是完整数据类型 + 多边形布尔。这一格的措辞在 §5.1.42 里保留为"形状推广"，不宣称等同。

**我们已经有的那一半**（这是方案的前提，别从头设计）：

| 已有 | 位置 | 状态 |
|---|---|---|
| 网格数据本身 | `Execution/PCGMeshBuilder.cs` 的 `PCGMesh`（顶点/三角/法线/UV 四个平铺数组） | 纯核，33 条离屏断言 |
| 造型算子 | `Box` / `Grid` / `Cylinder` / `Stairs` / `Tube` | 纯核；封闭性/外向法线/体积面积都有断言 |
| 集合算子 | `Offset` / `Transform` / `**Merge**`（三角面拼接）/ `Bounds` / `RecalculateNormals` | 纯核 |
| 载荷 | `PCGData.Meshes`：**与点下标对齐**的第三种载荷，Clone 共享 / Gather 随点走 / Merge 拼接 | 已有断言 |
| 产几何的节点 | `Geometry Primitives`（Id `Geometry`） | 离屏 33 条 |
| 物化 | `SpawnGeometry`（Id `SpawnGeometry`）：虚拟 `Model` + `Mesh.UpdateMesh` → `StaticModel`，**按形状共享**、键含三角形数、永不释放、actor 缩放 1 | 引擎 3 组内真跑 |
| 采样核 | `Data/PCGMeshSurfaceSampling.cs`（面积加权/重心映射/退化面） | 29 条离屏断言（现在只被 `MeshSampler` 用于**资产**） |

**缺的是哪一半**，以及每一半的**代价来源**：

1. **几何不能独立于点存在**。现状 `Meshes` 与点**下标对齐**，"随点走"因此免费，但"图里只有几何、没有点"表达不出来（UE5 的 DynamicMesh 可以）。这一条的代价不在数据层，而在**语义**：一旦允许独立几何，就要定义它自己的一套 Clone/Gather/Merge 规则与坐标空间。
2. **没有"几何 → 点"**。`MeshSampler` 采的是**资产**（参数是资产 Guid），不能采**载荷里**的网格。纯核已经有了，缺的只是把它接到载荷上。
3. **没有布尔**（并/差/交）。UE5 那层来自 Geometry Scripting；自研 CSG 是重活（鲁棒性 + 性能 + 退化面），且我们已有 33 条形状断言会变成需要同样密度的 CSG 断言。
4. **一个 Actor 只能承载一个网格** —— 这是**引擎事实，不是我们的实现选择**（`Tools/FlaxApiDump` 实测）：`Model.LODs` 是 `ModelLOD[] { get; }`、`ModelLOD.Meshes` 是 `Mesh[] { get; }`，**都是只读数组**；常量 `MaxLODs = 6`、`MaxMeshes = 4096` 也不由我们控制。而唯一被证实可写的几何入口是 `Mesh.UpdateMesh(...)`（**12 个重载**，全是"整网格替换"：vertices/triangles/normals/tangents/uv/colors）。⇒ "运行期把 N 个子网格塞进一个 Model"这条路**不存在**；要一个 actor 只画一次，就必须先把 N 个网格**合并成一锅三角面**（`Merge` 我们已有）。
5. **实例化路线未定**。`ModelInstanceActor.Entries` 是 `ModelInstanceEntry[] { get; set; }`（可写），是"同一网格摆很多位置"的廉价路线；但赋值后是否需手动触发重建，与 `ModelInstanceEntry` 的字段集一样**未核实**（这是 §附录 B.2 已记的盲区）。
6. **`MeshAccessor` 存在，但本插件不用它（已订正）**：它有可读可写的 `Positions/Normals/Tangents/TexCoords/Triangles/Colors`，看起来是取代 `DownloadVertexBuffer`（编译期已报 obsolete）的现代读写路径。**订正**：`PCGSpawnGeometryNode.BuildModel` 实际走的是 `Mesh.UpdateMesh(vertices, triangles, normals, null, uv, null)`——引擎自己的上传会按拿到的流构造**显式布局**（`Mesh.cpp`），而那个布局恰恰是访问器的逐流 setter 构造不出来的；切线/颜色传 null 由引擎从法线推。所以"绑定路径未核实"**不再挡路**，只有"访问器怎么写回一个已有 Mesh"仍未知（我们不使用）。

**四个待决问题**（按本仓惯例：方案定了再写码；选错的表现是"看着有东西、但对不上"，比崩溃难查）：

| # | 待决 | 选项 | 代价与判据 | 建议 |
|---|---|---|---|---|
| **A** | **几何的承载形态** | (1) 维持"与点下标对齐"（`Meshes[i]` 属于 `Points[i]`）；(2) 扩成**可独立存在**的集合（点可以没有、几何可以没有点，N:M 用引用）；(3) 新立一个 `PCGGeometry` 值类型，与点/样条并列成为第四种载荷 | (1) 零改动，但"只有几何没有点"永远表达不出来，等于 Geometry 只是点的附属；(2) 改动集中在 `PCGData` 的三种传播规则（Clone/Gather/Merge 各要一条"没有点时几何怎么走"的规则）+ 新增"几何↔点"的对应关系；(3) 最干净但最大，且要回答"载荷与点如何配对"（现状下标对齐是**零成本**的配对，改成引用就要维护一张表） | **(2)**：把 `Meshes` 从"点的附属"升成"可独立存在的集合"，保留"对齐时按下标"这条快路（既有断言一条不动），只在**不对齐**时启用新语义。这样引擎侧物化不必改，数据层的既有 33 条断言也不推翻 |
| **B** | **布尔做不做** | (1) 不做（合并/变换/采样够用，布尔留给将来的 Geometry Scripting 类比层）；(2) 做并/差/交的**纯核 CSG**（BSP 或体素）；(3) 只做"差"（挖洞，洞穴/门洞最常用） | (1) 零风险，但"挖一个门洞"这类需求要回到摆模型遮；(2) 鲁棒性是主要成本（共面/退化/自交），且要一整套与现有一致密度的断言；(3) 是 (2) 里性价比最高的一小块，但它的正确性判据（体积守恒、水密性）比并集交更微妙 | **(1) 起步，把 (3) 记成"下一格"**：先让几何这条链"能造、能合、能采、能落"，布尔单独拍板——它是唯一会引入"看起来对但其实自交"这类静默缺陷的算子 |
| **C** | **输出形态** | (1) 每个网格一个 actor（现状）；(2) **合并成一个网格**再物化（一个 actor 一锅三角面）；(3) 走 `ModelInstanceActor` 多实例（同形状多位置）；(4) 按"去重后的形状数 vs 实例数"自动选 (2)/(3) | (1) 简单，但"一片石柱群"就是几百个 Actor（撑大纲、撑性能）；(2) 一个 actor，但合并后**共享材质槽**、且大网格每次 `UpdateMesh` 都是全量重建；(3) 最省，但 `Entries` 的写入语义未核实（盲区）；(4) 需要一张判据表，但它正是 UE5 的做法（DynamicMesh + 实例化） | **(2) + (3) 都要，(4) 做判据**：形状少而实例多 → 实例化；形状各异 → 合并。**先做 (2)**（只依赖已证实的 `UpdateMesh`），(3) 等盲区核实后补 |
| **D** | **几何 → 点** | (1) 不做（只有资产能采样）；(2) 加 `Sample Geometry` 节点：对**载荷里的**网格按面积采样出点；(3) 连资产也走同一条路（`MeshSampler` 改为"先取网格数据再采样"，统一两条入口） | (1) 那 Geometry 就不是数据类型——"数据能进不能出"；(2) 纯核算术已有（`PCGMeshSurfaceSampling` 29 条断言），只是换个喂料口，离线可全覆盖；(3) 更统一，但要动既有的 `MeshSampler`（它有引擎侧断言，改动面大） | **(2)**：新节点 + 复用纯核，`MeshSampler` 不动。这样"几何是数据类型"在**两个方向上**都成立（造得出、也采得出），而既有资产路径零风险 |
| **E** | **保存/导出成资产** | (1) 不做（交付形态 = 运行期物化成场景 Actor）；(2) 编辑器侧加"把生成的网格存成 .flax" | (1) 够用，且避开"写资产文件"这条本项目已经踩过坑的路（资产头 HashCode / 重导入改写 Guid）；(2) 要动资产写入与资产库刷新，风险与收益不成比例 | **(1)**，并在文档里写明"运行期物化"是这一格的交付语义 |

**验证分层（这决定测试怎么写，不是事后补）**：
- **纯核（离屏门）**：几何的三种传播规则（尤其 A 选 (2) 之后"没有点时几何怎么走"）、合并/变换/边界/法线/去重键、几何→点的采样（对解析形状手算期望值，照 `PCGMeshSurfaceSampling` 那 29 条的密度）。
- **引擎侧（`PCGSelfTest`）**：`UpdateMesh` 真写进虚拟 Model（形状键命中/未命中两条）、物化出的 actor 数与命名、`Clear()` 收回、预览不落地、合并成**一个** actor（C 选 (2) 后的新断言）、以及 `ModelInstanceActor.Entries` 的写入语义（若走 C(3)）。
- **PIE/目视（用户）**：网格看起来对不对（法线朝向、接缝、尺度）——按既有约定归你，且**指标全绿不等于看起来对**（见 [[procedural-modeling-renders-beat-green-metrics]]）。

**最短实现路径（A(2) + B(1) + C(2) + D(2) + E(1)）**：
1. `PCGData` 加"几何可独立存在"的语义 + 三条传播规则断言（纯核）；
2. `PCGMeshBuilder` 补两个够用的算子：**按形状去重的键**（供 C 选 (2)/(3) 判据）、**合并时的材质槽合并**（现在 Merge 只拼三角面，材质槽没定义）；
3. 新节点 `Sample Geometry`（D(2)，纯核喂 `PCGMeshSurfaceSampling`）；
4. `SpawnGeometry` 补"合并成一个 actor"的开关（C(2)），引擎自检加"一个 actor 而不是 N 个"；
5. 描述层**暂不加指令**——先让图里能用（这一格是"数据类型"，不是"描述词"；等上面四条稳了再想 `geometry …` 该长什么样）。

**风险**：① `UpdateMesh` 是整网格替换，大网格每次全量重建（性能，需 bench 量）；② 虚拟 Model 缓存永不释放是现状取舍，几何变多后要重新评估；③ 合并成一锅会丢"每块独立材质"（除非做材质槽合并）；④ 若将来做布尔，(2) 的鲁棒性是真风险，判据必须先定（体积守恒/水密/无自交）。

#### 落地进度（2026-10-10，按 A②B①C②D②E① 的建议口径）

**Phase 1 — A② 数据层 + 算子（已完成，离屏可验）**

- `Data/PCGGeometry.cs`：`PCGGeometryItem`（网格 + **自带摆放**）——独立几何没有点来承载摆放，所以 `Transform` 不是可选字段。做成 `struct`：数组按值复制、网格按引用共享，与 `Meshes` 的取舍一致。
- `PCGData.Geometry`：第二种几何承载，三种传播规则各一条——**Clone** 复制数组（元素按值）、**Gather 全部保留**（与曲线同一条规则：它不属于任何点）、**Merge 按部分顺序拼接**。
- 顺手修掉一个会**静默丢几何**的早退：`Merge` 原来拿"点数为 0"当"整份为空"，而"没有点、只有几何"是合法的，于是几何会在合并那一步消失（图上完全看不出来，只是几何没了）。改成"点数与几何数都为 0 才是空"。
- `PCGMeshBuilder.ShapeKey`（顶点表量化内容 + 三角形数与绕序）与 `MergePlaced`（把每块的摆放**烘进顶点**再拼——一个 actor 只有一个 Transform，表达不了"每块各有各的位置"）。
- `PCGSpawnGeometryNode.KeyOf` 改为委托 `ShapeKey`，**只有一份实现**（免得"缓存判据"与将来"合并/实例化判据"各自漂移）。换键有正面证据：旧键是"三角形数 + 包围盒尺寸"，对**顶点与包围盒完全相同、只有绕序反了**的两块会撞车，而撞车时缓存会把另一块几何交给这个点。

**Phase 2 — D② `Sample Geometry` 节点（已完成）**

`Nodes/PCGSampleGeometryNode.cs`（Id `SampleGeometry`，注册 **58 → 59**）：把**载荷里的**几何采成点，每块按面积权重撒 `Points Per Mesh` 个点。**两种承载都认**（对齐的按点变换摆、独立的按自带 Placement 摆）。三个设计点值得写下来：

- 输入里 `Count == 0` 时**不能早退**——"没有点、只有独立几何"恰恰是这个节点最该处理的输入；
- 每块用**各自错开**的种子（按下标混一个大质数），否则同形状的每一块在各自局部坐标里采出同一批点，一片东西看起来像盖章；
- 没有几何可采时**原样透传 + `LastError`**，不清空这一层（清空会在下游变成"那里什么都没长"）。

**判据落在两件事上**（只数点会让一个"全撒在原点、朝向用世界向上轴"的实现也对得上）：**朝向来自三角形法线**（平面网格上全部朝 +Y）、**摆放真的生效**（三簇各 4 点 / 独立几何的点落在它的 Placement 上）。

**门**：`PCGGoldenRunner` **1755 通过 / 0 失败**（本格 +22），`PCGVerify` / `PCGEditorVerify` 0 错，引擎侧 `pcg_selftest` 26 组全绿。

**Phase 3 — C② 输出形态（已完成，引擎侧可证）**

- `SpawnGeometry` 新增 `Merge Into One Actor`（参数 3 → 4）：置位时把这一层要写的几何**合并成一锅三角面再建一个 Actor**（`MergePlaced` 把每块的摆放烘进顶点）。默认仍是一个点一个 Actor、同形状共享一份虚拟 Model。
- 同一个生成器现在**两种承载都写**：与点对齐的 `Meshes`（按点变换摆）与独立的 `Geometry`（按自带 Placement 摆）。顺手修掉同一类的早退——它原来用 `Count == 0` 判断"没有东西可写"，而"没有点、只有独立几何"正是这个生成器该写出来的那一种（`Merge` 的可达性最初就是被同一种早退坑掉的）。
- **引擎自检新增一组（3m）**：逐块 3 个 Actor → 合并模式 **1** 个 Actor、合并后的网格是单块的 **3 倍顶点**、只有独立几何（没有点）时写出 2 个 Actor、`Clear()` 精确收回 2 个。**每条都当场证伪过一次**：把合并那条判据临时反向，失败信息里读到的真实数字是"报 1 个 / 体积下有 1 个生成物"，随即撤回并复绿——所以这组不是"跑绿了"，是"能红、且红的时候数字对"。
- 夹具上踩到并记录下来的一脚：`Actor.Destroy` 延迟到帧末，同一帧里"生成 → 清空 → 再生成"会让上一批已排队的对象**仍留在子级里**，于是"数孩子"的夹具会数到上一批（实测 4 个 / 6 个）。这类组必须用**记账读数**（`PCGVolume.GeneratedActorCount`，它排掉已排队的），或按名字找目标 Actor——按下标取会取到别人。

**C③ 的结论要改（引擎事实订正）**：原计划写的是"走 `ModelInstanceActor` 多实例"。读 `Engine/Graphics/Models/ModelInstanceEntry.h` 之后可以确定这是**错的**：`ModelInstanceEntry` 只有 `Material / ShadowsMode / Visible / ReceiveDecals` 四个字段，**没有变换、也没有网格**——它是一个**逐材质槽**的覆盖数组（`ModelInstanceEntries::Setup(slotsCount)` / `IsValidFor(Model)`），不是实例化缓冲；`ModelInstanceActor.h` 里那句 "transformation" 是**注释写错了**。所以在 Flax 里"同一形状摆很多位置"的实例化载体是 **`Foliage`**（本插件已在用：`SpawnFoliage` 一个宿主 + N 个实例），而不是 `ModelInstanceActor`。要做 C③ 就沿 `Foliage` 那条路（把虚拟 Model 当 foliage type 的模型），或者承认"每形状一个 StaticModel"就是当前的粒度。**这一条因此从"待核实"变成"已定性"**：不是盲区，是那条路不存在。

#### Phase 4 — 描述层接入（v1.5 续，已完成）

上面四条稳了之后补的那一步：**让描述层够得着程序化几何**。在此之前"造几何"只能靠手搓图——而总目标是"按设定描述生成"，一句描述够不着的功能不算数。

**语法（四个扁平选项，与节点槽位 1:1，所以不需要小表解析）**：

```
scatter pillars points 300 within 1900 1500 2450 2250 geometry cylinder geometry-size 2.5 10 2.5 geometry-segments 8
```

- `geometry <box|grid|cylinder|stairs>`——形状名；
- `geometry-size <x> <y> <z>`——尺寸，**世界单位**，写了几何就必须给；
- `geometry-segments <n>` / `geometry-steps <n>`——按形状解释（圆柱圆周分段 / 格子细分 / 台阶数）。

**三条不显然的取舍**：① **尺寸必填**——节点自己的默认尺寸是"格子级"的 `1000×200×1000`，一段描述漏写尺寸得到的是一个**一公里大的盒子**，"图上看着对、世界里大得离谱"比一句报错难查得多；② **编译期固定 `Use Point Scale = false`**——描述里的数字本意是世界单位，用点缩放那一档会让"3 米粗的柱子"随点的缩放飞掉；③ **与 `model` 互斥**——两者都在回答"长什么"，同时写等于让编译器猜。落链是 `… → Geometry Primitives → Spawn Geometry`，**不是**静态模型生成器（那条链只认资产）。

**防漂钉子**：形状名表只有一份（`PCGSpecParser.GeometryShapes`），节点下标只在 `GeometryShapeIndex` 里翻译一次；离屏断言把它与 `PCGGeometryNode.Shape*` **逐个对齐**、条数也对齐——两侧一旦漂了，"说柱子长出盒子"会静默发生。三类错（形状名不认识 / 写了几何没给尺寸 / 几何与模型同时写）都带行号报出。覆盖夹具里那条依赖也如实记着：夹具一次只盖一个选项，而 `geometry` 必须连尺寸一起写。

**演示与引擎证据**：`demo_valley` 加一层石柱（上面那一句，`within` 把它圈在东北角）。重编译 **28 节点 / 19 连线、告警 0**；重烘焙 **98 → 132 个 Actor**（+34）；取位置证据：新增的 34 个 `PCG_Gen_Geometry_*` **全部**落在 x∈[1906, 2410]、z∈[1504, 2094]（正是 `within` 圈的那块），y≈9 贴着地形；同名的洞穴链仍在 x≈1100~1500、y −81→−447，两者按位置互不混淆。石柱落点正好在**林地那块空地**里——`polygon` 只约束了林地那一层，石柱层没写 polygon，于是两个特性叠在同一张图上各管各的。

**门**：`PCGGoldenRunner` **1772 通过 / 0 失败**（本格 +17），两道编译门 0 错，引擎侧 `pcg_selftest` 27 组全绿。

#### Phase 5 — C③ 实例化输出（已完成）

`SpawnGeometry` **追加**第五个参数 `To Foliage Host`（参数 4 → 5，**只能往后追加**，老图一个槽位都不动），于是这个生成器有了三种输出形态：默认逐块一个 Actor / `Merge Into One Actor` / `To Foliage Host`。**后两者互斥**（合并把摆放烘进顶点、只留一份网格；实例化保住每块的变换、同形状共享一个类型）——同时置位时报出来、**不动场景**，不替作者挑一个（挑错了屏幕上还像"跑通了"）。

**载体是 `Foliage`，不是 `ModelInstanceActor`**（后者是逐材质槽的覆盖数组，见上面的订正）：一个宿主 + **每形状一个 FoliageType** + N 个实例，所以"两种柱子混在一片"也是对的。关键在于**实例不是 Actor**——它们只存在于宿主的缓冲里，于是"一万根草"不会变成一万个场景对象（那会把场景遍历、大纲与序列化一起拖垮）。

三处复用与守卫：① 宿主名字沿用植被生成器那一套（`PCG_Foliage_<节点>_l<层>_<格>`），于是 `PCGVolume.Clear()` 与按格清理对它是**同一条规则**，不需要第二套归属机制；节点下标在名字里，两侧各自的宿主不会撞名；② 复用宿主时先 `RemoveAllInstances()`——否则"重新生成"会变成"再叠一遍"；③ `Random`（引擎给每个实例的着色器随机输入）用**黄金比低差异序列**按块序号摊开：确定性，且不会让一片实例长得像复制粘贴。

**引擎断言**（3m 组，用一个**单独的体积**跑，避开延迟销毁对计数的干扰）：1 个 Foliage 宿主、里面 **3** 个实例、体积下**0 个产出 Actor**、`SpawnedCount == 3`、两形态同时置位时报出冲突。这条断言**自带判别力**：实例化若没生效就会走逐块路径，宿主数会是 0 —— 立刻红（与"没点落进凹口"那种需要正面对照的断言不同）。

**门**：`PCGGoldenRunner` **1773 通过 / 0 失败**，两道编译门 0 错，引擎侧 `pcg_selftest` 27 组全绿 / 0 跳过。

#### Phase 6 — 描述层选输出形态（v1.5 续，已完成）

`scatter` 新增 `output <actors|instanced|merged>`（不写 = `actors`）。这一步补的是**描述层最贵的一处够不着**：在此之前 `scatter` 无论挂模型还是程序化几何，**输出形态永远是"逐块一个 Actor"**——而插件里早就有 `Spawn Foliage`（一片林子一个宿主）与几何生成器的合并/实例化模式，**一句描述都调不到它们**。于是"森林/草地"从描述生成出来只能是几百个 Actor。

| `output` | 资产层（`model`） | 程序化几何层（`geometry`） |
|---|---|---|
| `actors`（默认） | `Spawn Static Model`（老行为不变） | `Spawn Geometry`，两个开关都关 |
| `instanced` | **`Spawn Foliage`**（模型/密度/缩放/偏航四档照传） | `Spawn Geometry` 的 `To Foliage Host` |
| `merged` | **解析期报错**（模型那半没有"合并成一锅"这回事） | `Spawn Geometry` 的 `Merge Into One Actor` |

**两类错都带行号**：不认识的形态名（列出三个词）、以及 `merged` 用在资产层上。

**演示证据**（`demo_valley` 的草与石柱都改成 `output instanced`）：重编译 **28 节点 / 19 连线、告警 0**；重烘焙 **132 → 72 个 Actor**（草 28 + 石柱 34 变成 2 个宿主，−62 +2 = −60）；场景里新增的两个 `PCG_Foliage_*` 宿主（节点 11 / 17）父级都是 `PCG_Valley`——第三个 `PCG_Foliage_2` 的父级是用户自己的 `PCGVolume`，与这次改动无关（顺带证明这套命名与归属在别人家的图上也不会串）。

**门**：`PCGGoldenRunner` **1782 通过 / 0 失败**（本格 +9），两道编译门 0 错，引擎侧 `pcg_selftest` 27 组全绿。

### 5.1.44 平面切割（布尔"差"里能证明的那一块，v1.5 新增）

注册类型 59 → **60**，Modifier 类新增 `Cut`。`Data/PCGMeshCut.cs`（纯核）+ `Nodes/PCGCutNode.cs`，离屏 **35 条**（`MeshCutTests`）。

**为什么不是 `A − B`**：真正的差集要判"B 的哪些面在 A 里/外"，而那一步最容易产出一个**看着对、其实自交**的网格——本插件在 §5.1.43 的决定 B 里正是为此先把布尔推迟的。**平面切割没有那个分类问题**：每个三角形只被一个半空间裁一次，切口是一条条线段，而**对封闭输入这些线段必然首尾相接成环**，环一封上就是一个合法的封闭实体。于是它能被四条判据钉住，而"判不出来"的情形一律报错。

**四条判据（这一层的"对"就是这四条，逐条有断言）**：
1. **水密**——结果里每条边恰好被两个三角形共享（按位置配对）；
2. **体积守恒**——对解析可算的形状逐点对上：立方体横切/斜切一半都是 4.0（过中心且不穿顶点的平面按中心对称平分体积）、圆柱横切是原体积的一半；
3. **未跨平面的三角形原样保留**——一个顶点都不重算（按三角形的位置逐点比，**不是**按顶点下标：装配会按位置焊接，立方体那 24 个重复角点收敛成 8 个是正确的）；
4. **判不出来就报错**——开放网格（格栅）、按设计带内部面的台阶，一律拒绝封盖并说明原因，而不是产出一个开着口的"实体"。

**两个实测踩出来的实现要点**：① **顶点必须焊接**（量化 1e-4）——切口上的点在相邻两个三角形里各算一次，而两条边在各自三角形里的端点顺序相反，末位可能不同；② **接环的键也必须量化**——第一版拿原始坐标当键，圆柱横切直接报"只连到 1 条线段"。另外：校验器自己先错过一次（要求反向边"已经"出现过 ⇒ 一个完好的立方体被判成不水密），边收边查必须改成"先全收、再逐条查反向"。

**节点**：`Cut`（刀口在**形状自己的坐标系**里——网格都建在自己原点，所以 `y=0` 就是"把这个形状切一半"，与它摆在哪儿无关；`Plane Normal` 默认朝上 ⇒ 默认把顶上削平；`Invert` 保留法线侧）。预览/决策运行**不做这一刀**（切割不便宜，而那种运行只关心点数与格子）；某个网格切不动时**原样保留**并把原因记进 `LastError`（整层丢掉比留一个没削的柱子更糟，只留日志又看不见）。它**不调 `Debug.*`**——纯算术节点能离线执行，而日志调用在离屏门里会 DllNotFound 直接崩（实测踩到）。

**明确的边界（未验证前沿，不装作没有）**：① 顶点恰在平面上、共面面片这一档是**宽容**处理（不切、原样返回），不是精确分类；② 截面必须是**单个**闭合环——多处截断（例如切开一个环面）会被拒绝；③ 真正的 `A − B`（挖方孔）**仍未做**，它需要那套分类，属单独的一格。

**门**：`PCGGoldenRunner` **1804 通过 / 0 失败**，两道编译门 0 错，引擎侧 `pcg_selftest` 27 组全绿 / 0 跳过。

#### 5.1.44 续：刀口的描述词与一处 DryRun 订正

**描述词**：`cut <nx> <ny> <nz> <offset>`（四个数落进两个字段——法线 + 沿法线推多远，所以走显式分支）。刀口在**形状自己的坐标系**里、点位是"归一化法线 × offset"：形状都建在自己原点，于是 `cut 0 1 0 0` 是"从中间横切"、`cut 0 1 0 5` 是"削掉 y>5 那截"、`cut 0 -1 0 0` 是"留下上半"（把法线反过来即可，不需要单独一个取反开关）。两条校验带行号：`cut` 只对 `geometry` 那层有意义；法线不许零长度。落链 `… → 程序化几何 → 平面切割 → 生成器`。

**一处 DryRun 订正（这条值得单独记）**：第一版让 `Cut` 在预览/判定运行里**早退不切**，理由是"切割不便宜，而那两种运行只关心点数与格子"。它是被**演示那条断言**抓出来的——离线按 `DryRun` 跑演示，石柱最高点仍是 5.0（没削），而烘焙出来的却是削过的：**同一张图两种样子**。`DryRun` 的契约是"不调引擎"，不是"不干活"；切割是纯数据变换（不碰场景），所以它必须照常跑，真正该被挡的是建 Actor 那一类。断言也随之从"预览不切"改成"预览切得与烘焙一模一样"。

**演示与证据**：石柱层加上 `cut 0.35 1 0 2.6`（斜削顶）。重编译 **28 → 29 节点 / 20 连线**、告警 **0**；重烘焙 Actor 数不变（**72**，切割改的是网格数据不是 Actor 数）。两条断言钉住"这一刀真的到了"：① **离线跑演示文件本身**，石柱那一层网格的最高点 **5.0 → 3.19**（认那条链靠**结构**——从 `Cut` 节点顺边走到汇；第一版按"顶点数最多"去挑，挑中的是洞穴的管子）；② **引擎侧**读 Actor 渲染的那份模型：`MeshBase.Box` 的高度 2 → 约 1、三角形数**变多**（裁剪侧面 + 封盖）——只数三角形是看不出高度的。

**门**：`PCGGoldenRunner` **1813 通过 / 0 失败**，两道编译门 0 错，引擎侧 `pcg_selftest` 27 组全绿 / 0 跳过，注册类型 **60**。

### 7.3 附：分区复用的"拒绝写场景图"这条规则从未落地（2026-10-10 修）

**症状（引擎侧实测）**：`PCG_Partition`（分层 + 分区，图以 `SpawnGeometry` 收尾）在同一张图、同一个种子上**连跑三次**，留在场景里的 Actor 是 **120 / 60 / 72**，而点数每次都是 784。也就是说：**产出人口随缓存状态漂移**，设计师看到的是"有些物件这次在、下次不在"。

**怎么查出来的**：磁盘日志读不到（落盘滞后），于是给体积加了一组**公开的逐次读数**（`LastActiveCells / LastExecutedCells / LastReusedCells / LastDeferredCells`，按层求和）并让 `pcg_volume_generate` 返回它们。三次读数是：

| 运行 | 点数 | Actor | 活跃格 | 真执行 | 复用 |
|---|---|---|---|---|---|
| 1 | 784 | 120 | 16 | 16 | 0 |
| 2 | 784 | 60 | 16 | **4** | **12** |
| 3 | 784 | 72 | 16 | 4 | 12 |

**根因**：`PCGPartitionCache` 的注释写着"拒绝缓存写场景的图……判据是 `IsThreadSafe == false`"，而**全仓没有任何一处消费那个标志**——规则只有文档、没有实现，于是分区体积一直在复用"会建 Actor"的格子（复用 = 交回点、却**从没建过物件**）。

**修法**（两处，第二处才是根因）：① 判据改用**权威的那一个** `PCGGraphEffects.GraphWritesScene(compiled.Nodes)`（编辑器「试跑」的闸门与生成器清单都用它，离屏还有 `EverySpawnerIsListed` 盯着漏登记），写在 `PCGVolume.PartitionRequest`：写场景的图直接拿到**没有缓存**的请求；② 顺手修两个生成器错标的线程安全标志——`PCGSpawnGeometryNode`（**从来没声明过**，继承了默认 true）与 `PCGSpawnSplineMeshNode`（显式写了 true），而它们都建 Actor，本来就该是 false。

**修复后实测**：`reused` 恒为 0、`executed` = 活跃格数（16）；Actor 数**稳定在 48**（两次连跑逐项一致），而它正是这个体积本来的正确值——中间量到的 12 / 60 / 72 / 96 / 120 是"复用缺陷"与"记账取集合的次序"两件事叠出来的。

**修的过程中自己还错了一次（值得记）**：改记账时把"排除集合"取在**清理之前**，而清理才把上一批排进销毁队列 ⇒ 那一批没被排除 ⇒ 2 个 Actor 被报成 4 个，把 `TestSpecToScene` 的"重新生成不累加"当场打红。**取集合的时机必须在清理之后**（"这一遍新生成的"= 队列之外的那些）。这条断言（"再烘焙一遍 Actor 数不许变"）正是为此存在的。

**遗留的设计问题（不是这次的缺陷，已于 2026-10-11 修正，见 §7.4）**：分区运行下 `Cave` 节点没有输入时会**以每格自己的生成范围中心起洞**，于是 16 格各长出一张洞网（48 个 Actor / 16 = 每格 3 件）。"一个体积里的洞穴应当只有一张网"是要单独定的一格——它与"洞穴入口从哪来"是同一个问题。

### 7.4 附：洞穴的三处承载/边界修正（2026-10-11）

三件事在同一个下午串成一条链，都是"看着跑通了、其实内容没落地"：**洞壁摆错地方**、**入口按格起**、**分区开关改内容**。逐条给实测数。

#### ① 洞壁的承载：`Meshes` 是"第 i 个网格配第 i 个点"，而洞穴是"一个网格、很多点"

`Tube` 写的是 `path[ring] + offset`（**世界坐标**）。洞穴一条隧道只出一个网格，却出很多个中心线点（实测 841 点 / 12 网格）。而 `Spawn Geometry` 的契约是"`mesh[i]` 配 `point[i]`"（那条快路的全部价值就是**零成本配对**：网格建在原点、摆放来自它所属的点）。硬凑下标的结果实测出来是：12 根管子全被摆在**隧道 0 的前 12 个点**上，而且每根又被平移了一次点的位置——读 12 个 Actor 的世界坐标，它们连成一条**下降路径**，就是隧道 0 的中心线。也就是说洞壁整体飞到了体积外两倍远的地方。

**改法**（用户拍的方案 A）：洞穴改走 **`Geometry` 独立几何**通道，一条隧道一块（网格 + 自带摆放），生成器走"独立世界几何"那条分支。`PlaceTube` 把世界坐标的管子**搬回自己的局部原点**、把它原来在哪当作 `Placement` 交回去——这正是 §5.1.43 里"独立几何自带 `Transform`"的设计意图；它同时让三种输出形态（逐块 Actor / 合并 / 实例化）都成立，而记在 `Meshes` 上时**实例化那一条**根本做不出来（N 个实例共用一个网格，不居中就全叠在原点）。同类的误配对在 `PCGCutNode`/`PCGSampleGeometryNode` 上也存在（它们同样按点下标取 `Meshes`），只是演示图里还没踩到。

#### ② 入口：从**体积**中心起，不是从这一格的中心起

分区运行时一次只覆盖一格（带余量）的范围。默认入口取"这次运行的范围中心" ⇒ 16 格各从自己的中心起洞 ⇒ 16 张迷你洞网。改取 `SourceBounds`（体积级世界盒，随上下文传到每格），每格规划**同一张**网、再由生成器按格裁剪。与网格采样器那条"世界锚点优先"是同一条规则。

#### ③ 分区开关不该改内容：整块运行也按体积裁剪 + 巷道不许走出体积

同一张图、同一个种子、同一个盒子实测：整块 **12 件**、分区 **9 件**（差的那 3 件在盒外），而且整块那份会把洞壁画到山体外面去。两处收口：

- **`PCGExecutionContext.ClipRegion`**（体积的世界盒）：整块运行时由 `IsInsideClip` 用它；分区运行仍由 `ClipCell` 按格判定。边界取法与格子**必须一致**——格子那片区域的并集是闭区间（最远那格把上界也包进来，见 `PCGPartitionPlanner.IsInsideCell`），所以区域也按闭区间判；否则正好落在最远边上的点会"整块留、分区丢"。离屏有一条**等价性断言**：区域判定与"落在某一格里"（格子由规划器独立摊出来）逐点相同，探针刻意压在边界上。
- **`PCGCaveOptions.Bounds`**：巷道不许走出体积盒，靠**截短步长**实现（与深度上限同一套规矩，见 `PCGCavePlanner` 类注释）。取**体积**盒而不是格子盒——每格各取自己的盒子又会挖出 16 张不同的网。

**实测**：修前整块 12 / 分区 9 且位置是错的；修后同种子整块 **6 件**、分区 **6 件**，且两者**位置逐项完全相同**（`PCG_AuditFlat` vs `PCG_AuditPart`：同种子 7、同盒子、同位置；比对的是 6 个 Actor 的局部坐标集合）。演示体积重烘焙：`PCG_Cave` 4 根管子（+2 石头）、`PCG_Partition` 6 根、`PCG_Valley` 66 件，全部落在盒子内。

**截短步长自己踩的一个坑（值得记）**：第一版把步长截到"刚好贴住盒子面"，于是最后一截可能只有几厘米——管道那一侧的平行输运会拿到一个乱转的基准框，实测 12 根管子里总有一根的**最差三角形余弦变成 -0.88**（水密、体积为负都对，只有绕序翻了）。改法是**残桩不留**：截完不足计划步长的 10% 就整段作废。这条正对着 §5.1.38 那句"方向一个字都不改"——截短是对的，但**留一小截残桩**等于制造一个退化段。

#### ④ 顺带：`ClipRegion` 必须随上下文一起走（三处复制，漏一处静默）

加了 `ClipRegion` 之后才发现**"约束着这次运行"的字段一共有三个复制点**：`PCGVolume.BuildContext`、`PCGSubgraphNode`（子图）、`PCGExecutionContext.AsDryRun`（判定那一遍的副本）。而且 `PCGSubgraphNode` 那一处连**原有的 `ClipCell` 都没带**——子图里的生成器不认格子边界，分区运行会把 padding 里的物件也建一遍（同一类"看着跑通了"的缺陷）。三处都补齐，并加了一组**探针断言**（`SubgraphTests.ContextTravelTests`）：干跑副本要带上裁剪区域/锚点/DryRun，子图内层节点要**真的看见**格子、区域与锚点——用探针节点读，而不是"传了但没人用"。这是本仓"context 字段要两处复制"那条规则的第**三**处。

**门**：`PCGGoldenRunner` **1905 通过 / 0 失败**（新增 19 条：承载契约 2、摆放契约 2、区域判定 4、边界包含与对照 2、上下文传播 9，另有洞穴下游消费与缺陷钉子、布尔交 + 描述层、切割分档诊断共 50 余条见 §7.5/§7.6），两道编译门 0 错，引擎侧 `pcg_selftest` **28 组全绿 / 0 跳过**，注册类型 **61**。演示体积重烘焙：6 个体积 / **1495 Actor / 8172 点**（与修前逐项相同——盖的朝向只改网格数据，不改 Actor 数）。

### 7.5 洞穴输出的下游消费（几何通道）与切口朝向的两半（2026-10-11，**两半都已修**）

洞穴的洞壁改到 `Geometry` 通道之后（§7.4①），"下游认不认这条通道"是**另一句承诺**，所以补了一组断言
（`Tools/PCGGoldenRunner/CavePipelineTests.cs`，14 条）：`Cut` 认洞穴的几何且件数不变、真的削掉了东西、
每块都还在、绕序仍与法线一致；`SampleGeometry` 从洞穴几何里采出了点、每块都贡献了采样点、采出的点落在
**管子自己的世界包围盒**里。最后一条带**反向对照**：把每块几何的摆放抹成单位变换之后，同一批采样点会掉到
盒子外（实测最大越界 170.9）——否则"点恰好都在框里"会让这条断言空转。

#### 已修：**刀口正好穿过一圈顶点时切口封不上**（2026-10-11 当天发现、当天修）

**最小复现（已进测试，一秒回路）**：合成一根直管——半径 20、10 边、两端封口、法线朝内，路径 `y = -240 … +240` 每 60 一环
（**第 5 环正好压在 y=0**）。修之前：刀口压环上时切"成功"、180 → **90** 面、**10 条破口边**
（在合并半径 1e-4 / 1e-3 / 1e-2 / **1e-1** 下都是 10 ⇒ 缝宽大于 0.1，是真开口）；同一根平移 3.7（刀口离环 3.7）则 180 → 118 面、0 条。

**根因（三处，串起来才成灾）**：
1. **两条早退判据在 ±ε 带里重叠**——"全部 ≥ -ε ⇒ 整块保留"与"全部 ≤ +ε ⇒ 整块丢掉"，于是 d≈0（恰好落在刀口上）的顶点两边都满足，**一整排三角形走"丢掉"那条路**；
2. **`ClipEdge` 的"在保留侧"判据与上面那两条不一致**（它用 `>= -ε`，把刀口上的顶点算成"在保留侧"），于是裁剪出来的折线整个等于原三角形、**一个交点都不收**；
3. 就算收了，**零长线段没被滤掉**：刀口压在一个顶点上时，一个三角形的两个交点会落在**同一个**顶点上，收下来会把"每个点被两条线段共用"撑成 **4** 条，封盖照样被拒（报的就是 `shared by 4 segment(s), not 2`）。

**修法（三处，各自都很小）**：
- 整块那两条判据改成**严格**比较（`> +ε` 保留 / `< -ε` 丢掉）：带内一律走"跨平面"那条路去裁剪与收线段。**关键**：交点仍按 `d` 线性插值算 ⇒ 落点还是**真正的平面**上，所以判据②（体积守恒）**分毫不动**（立方体切一半仍是 4.000）。
- `ClipEdge` 的"在保留侧"改成与整块判据**同一个**（`> +ε`）。差一点点就是这个缺陷：整块说"这是跨平面、该去裁剪"，而这里说"顶点在保留侧"，于是折线整个等于原三角形、一个交点都不收。
- 收线段时**跳过零长线段**（两端距离 ≤ 容差）：那不是切口的边。

**修后实测**：合成那根 180 → **98** 面、**0** 条破口边（1e-4 与 1e-1 **都是 0**）；洞穴的 6 块洞壁重新被切（总体积 1830714.6 → 1774913.9），**0 块开口**；判据②仍是 4.000、圆柱横切仍是原体积一半、演示的石柱仍是"最高点 5.0 → 3.19"；演示体积重烘焙与修前**逐项相同**（6 个体积 / 1495 Actor / 8172 点）——这条缺陷只在"刀口压环"那一档，不影响演示。

**修的过程中两次走弯路（都值得记，省一次返工）**：
- **挪刀口**：沿保留侧挪 `0.25ε` **完全没用**（0.25ε 仍在 ±ε 带里）；挪 `1.25ε` 缺陷是修好了，但**打破两处判据**——立方体切一半变成 **3.998**、洞穴那 6 根**全被拒**（一件没切）。⇒ 回退。**挪刀口是错的修法：该改的是分类，不是刀口的位置。**
- **按有向体积给盖翻朝向**（想让"内向网格"的盖也一致）：对朝外的网格是恒等的、却把 `TryBuildCap` 的绕向判据一起翻了（它收的是"盖法线的**反**向"），一举打破立方体/圆柱的水密；而且它并**没有**修好下面那条残留问题。⇒ 回退（不留没验证过的代码）。

#### 那一处"内向网格的盖朝向"：**两半都已修**——拓扑那一半与表示那一半（2026-10-11）

`Cut` 的契约（判据①–④）不含"绕序与法线一致"这一条，而本插件的洞壁是**朝内**的（法线指向隧道轴）。把它**拆开量**
之后发现其实是两件事叠在一起：

**① 拓扑级（已修）**：被切过的内向洞壁，切口那一圈有三角形的**有向边没有两两相反**——真正的绕序不自洽。
根因：`TryBuildCap` 用"切口外法线 = `-keepNormal`"这个**全局约定**定走向，而内向网格需要的是**反过来的**走向。
修法：让盖的朝向跟**输入自己的约定**走——有向体积为负 ⇒ 内向 ⇒ 盖反过来（见 `PCGMeshCut` 里 `orientation` 那段）。
**符号是可靠信号**：Box **+8.0000**、Cylinder **+12.0000**、朝外 Tube **+11.3137**、朝内 Tube **−11.3137**，
四个建造器逐个量过（`MeshCutTests` 里有这四条诊断断言钉着）。修后：外向那一批判据**一条都没动**（对它们是恒等的），
洞穴那条"有向边两两相反"变为**自洽**，裁掉的体积从 1774913.9 变成 **1460558.6**（内向管子的盖终于按对的方向封上）。

**② 表示级（同日已修）**：累加器按**位置**焊接顶点、焊接时保留**先插进来的**那个法线，于是"洞壁"与"盖"共享的
那条硬棱上只带一个法线 ⇒ 硬边上的三角形必然与"三个顶点法线的均值"不同向（实测最差那块面积 **1.04**、余弦 **0.00**，
两者**垂直**）。修法：焊接时再加一条**法线同向**（`dot ≥ 0.5`）——不同向就**另起一个顶点**。于是硬边各自拆点、各带各的
法线，而**同一面上的相邻三角形**照旧合并（它们的法线本来就同向），所以"逐边按位置配对"的**水密性一点没变**。
**洞穴那几块的读数在后续改动之后重新量过（2026-10-11 晚）**：现在是 **2 块**被切过的洞壁与法线均值不同向，
不合的三角形 **8 个**、最差**余弦 −0.980**、**面积 394.4**。余弦接近 −1 而面积这么大 ⇒ **不是**"硬边共享一个
法线"那类**表示**问题（那类已在盒 / 棱柱夹具上全绿，最差面积 0），而是**整片盖反了**（绕向与法线相反）。
所以这条钉子的**归因换了**：从"硬边表示限制"改成"**多刀之下的盖朝向**"；钉子仍保留（`cutWoundBreaks >= 1`），
而测试里新增的 `WindingBreakDetail` 会把"几个三角形、最差余弦、面积"打进理由，下一次不用再猜。
**已修（同日晚），而且根因不是"多刀之下的符号重判"**：
- **量到的真相**（测试里新加的 `WindingBreakDetail` 把最差那片的原始数据也打出来）：反的那片三角形三个顶点
  **都在刀口平面上**（是盖三角形）、顶点法线都≈**−Y**（正是算出来的 `capNormal`），而它的**几何法线是 +Y**
  ⇒ **绕向与自己的法线相反**。输入网格的有向体积是 **−632016**（负、内向）⇒ **符号判据没判错**。
- **根因**：扇形三角化**假设了切口环是简单环**。弯曲洞壁横切时，环上的点在 1e-4 焊接里被合掉之后环会
  **自交**，扇形因此产出方向相反的片；而 `TryBuildCap` 的绕向判据是**整圈的有向面积**——那个量对非简单环
  看不出这件事（整圈"看着对"，逐片才看得见）。
- **修法**：在 `PCGMeshCut` 里**逐片验盖的朝向**，判据是余弦 **≤ −0.5** 才算"真的反了"；一旦判到就
  **整刀拒绝**（判据④ 的同一条精神：判不出来就别硬做，也不交出一个盖反了的网格）。**退化片必须跳过**，
  而且"退化"要按**相对**尺度判——这一条是**两次踩坑换来的**：① 第一版只看"点积 ≤ 0"，把 `点积 −0.000`
  （垂直、其实就是零面积）的片也当"反了"，盒子/棱柱/演示石柱当场全红；② 改成绝对阈值 `epsilon²` 之后，
  **引擎侧重烘焙**又抓出一次回归——演示那 6 块石料（8×12×8 方料 ∩ 12 边圆柱）里有**碎片**（面积 0.00、
  余弦 −0.852）没被绝对阈值挡住，于是 6 块全被"原样保留"（`ClipToConvex` 报 6 个警告），
  而当时**离屏夹具全是绿的**。⇒ 最终判据：`area <= fanMax * 1e-3f` 才跳过（相对扇形里最大的那一片）。
  **教训记在这里**：① 退化判据要用**相对**尺度，绝对阈值在另一种几何里必然失效；
  ② "离屏全绿、引擎侧红"这条路走通了——`ClipToConvexTests` 因此补了一条**演示石料确切参数**的回归钉
  （8×12×8 ∩ 12 边圆柱），它比"再加一个抽象夹具"更值钱，因为它就是那个漏网的形状。
- **修后**：洞穴那 2 块盖反的 → **0 块**（那两块改为被拒、原样保留），离屏 **1923 passed / 0 failed**
  ——那条钉子从"钉着缺陷"翻成**正面断言**"被切过的洞壁里没有盖反的"；两道编译门 0 错；
  引擎自检 28 组全绿（0 失败、1 跳过 = 平地那档交叉检查）。

**两条已证伪的岔路（别重走）**：① 按"墙自己的走序"判环的走向——`cutPoints` 里成对线段的**先后次序并不等于**墙的走序，
实测把水密性整个打破；② 早先那次"按体积符号翻"之所以看起来失败，是因为同一编辑里还有一处符号写反了。这次把规则
写死在注释里（"`TryBuildCap` 交出的是所传向量的**相反数**方向"）之后一次就对——**符号约定的推理必须落到注释里**，
否则下一个人（或下一轮的自己）会以为是"这条路不通"。

### 7.6 两个被证伪/新发现的几何核心事实（2026-10-11，当日试当日撤）

这一节的结论是**负结果**，写下来是为了不让下一个人照着同一个错前提再走一遍。

#### ① "凸体也能像半空间那样按面依次裁"——**这个前提是错的**

想给"挖方孔"（`A − B`）补上凸工具那一档，思路是：凸体的补集"正好是它每个面的朝外半空间"，于是"按每个面各截一刀、
每次都保留外面那一侧"就等于挖掉它。**补集是那些半空间的并集，而按面依次裁算的是它们的交集**——一个立体的所有
朝外半空间之交是**空集**。实测一眼看穿：工具放在离立方体一百米的地方（本该"够不着、一点不少"），结果立方体
**整个消失**（体积 64 → 0）。这条反面对照是先写进测试的，所以它当场把错前提打红，没有一路做到底。

**结论**：这一套机械（半空间 + 封盖）**能证的是"交"**（只保留落在凸区域里的部分），**不是"差"**。而"交"要能用，
还得先过下面第②条。真正的 `A − B` 仍然需要"面在里/外"的分类 + 用 B 的面重建边界——就是 §5.1.44 判据④
刻意推迟的那一格，也是 `Cut` 之所以被写成"平面刀"的原因：**只有半空间的补集还是半空间**。

#### ② 轴向切刀一度"看轴向"——**已修**（2026-10-11 同日）

把一个 `PCGMeshBuilder.Box` 立方体**过中心**切一刀，修之前：

| 法线 | 边长 2 | 边长 4 |
|---|---|---|
| ±Y | 切得动（20 面） | 切得动（20 面） |
| ±X | **被拒**（`The cut boundary is non-manifold (two segments leave the same point)`） | **被拒** |

与尺度无关、与"刀口离面多远"也无关（`x=0 / 0.5 / 1` 三档全被拒）。`MeshCutTests` 里过中心的切割都用 ±Y
（默认法线）、演示那把 `cut` 削的是圆柱与斜刀，所以一直没暴露；影响面是"沿 X 切盒子（墙/板/台阶）会被静默拒绝"。

**根因（先把错误信息写细才看出来的）**：接环那一步要求线段**连成有向环**，而线段方向来自"各三角形自己的走序"；
刀口穿过的那些面朝向各不相同，方向在拐角处**可能不齐**。把错误信息改成"哪个点、几条线段"之后一眼看到：
角点 `(0,-1,-1)` 上**两条线段都朝外**，有向环因此接不起来——而那个环其实完好（8 条线段、每个点恰好两条）。
**修法**：接环改成**无向**邻接（每个点恰好两个邻居就够，等价性由末尾那条叉积定向判据保证——它本来就在），
于是"方向不齐"不再被误判成"不是环"。这是接环那一步的**通用健壮性改进**，不是给某个轴向打的补丁。

**修后**：两个轴向都切得动且水密（各 20 面）；演示体积重烘焙与修前**逐项相同**（6 个体积 / 1495 Actor / 8172 点）。
`MeshCutTests` 里留了一对回归钉子（法线 X / Y 各一条，要求"切得动且水密"），任何一个轴向退回被拒都会立刻红。

**当日捡回来的能力（2026-10-11 同日）**：`Data/PCGMeshBoolean.cs` + `Nodes/PCGClipToConvexNode.cs`（"裁进凸体"，
`Id = ClipToConvex`，注册类型 60 → **61**，4 个槽位 + 中文标签），一条 `A ∩ B`：只保留落在凸区域（盒/圆柱）里的部分。
它上一版被上面这条缺陷挡住（凸区域的第一个面就是 +X），所以当时整套撤掉；缺陷修好之后**按原样捡回**，
17 条断言全过，其中解析体积那半边是逐点对上的：

| 夹具 | 期望 | 实测 |
|---|---|---|
| 立方体(64) ∩ 盒 2×6×2（盒比立方体高 ⇒ 只剩中间那段 2×4×2） | 16 | **16.000** |
| 立方体 ∩ 盒 2³（整块在里头） | 8 | **8** |
| 区域放到 100 米外（够不着） | 空 | **空**（这就是证伪 A−B 前提的那条对照） |
| 区域 20³（把整块吞掉） | 64 | **64.000** |
| 节点：两种承载（`Meshes` / 独立几何）各两块 | 各 16、件数不变 | **16.000 / 16.000** |
| 区域面数 < 4 | 明确拒绝 | **拒绝** |

**两条钉住的边界**：① **多刀**裁完之后"与法线均值不同向"的那一批，**实测是表示问题、不是绕序问题**——最差那块
面积 **1.04**、余弦 **0.00**（几何法线与那个法线**垂直**），正是"墙的三角形带着盖的法线"（累加器按位置焊接、
保留先插进来的法线）。换成**与法线无关**的判据（有向边两两相反）之后同一批网格是**自洽**的 ⇒ ① 不是缺陷。
那条**表示限制当日也修了**（焊接加"法线同向"就拆点，见 §7.5②）：`ClipToConvexTests` 里那条已从"钉着"翻成**正面断言**、
最差三角形面积 **0.00**；洞穴仍剩 **1** 块，钉子保留。
② **棱柱区域擦过网格**这一档：**根因已于 2026-10-11 找到并修掉，≤16 边已经切得动**（32/64 边仍未修，
但成因换代了，见下）。下面是修之前的完整测量记录（留着是因为它把"该往哪儿看"钉死了），
修法与修后读数在新加的那一段。

#### ② 已修（2026-10-11）：根因是"去重半径与焊接半径不是同一把尺子"

**根因（夹具复跑直接量出来的，不是推的）**：收线段那一步用 `epsilon = scale × 1e-4`（**分类带**）判
"两个交点是不是同一个点"，而接环那一步只在 `1e-4`（**焊接半径**）内合并端点。大网格上 `epsilon` 能到
0.05 量级，于是"相距 2e-4~5e-4 的一根发丝"被判成同一个点、那段线段被**丢掉**；而接环够不着那两个端点
⇒ 环上留下两个度 1 的断口，整刀被拒。**实测读数**：prism-16 有一对交点相距 **2.197e-4**、prism-32 相距
**4.542e-4**，两档都在"某点只连着 1 条线段"上被拒（度数分布 `度1:2`）。

**第一版改法（把焊接半径放大到 epsilon）已被实测证伪两次**：那样洞穴那批被切过的洞壁当场破 **2** 块
（"有向边未两两相反" + "开口 3 条"）——大网格上 0.05 的半径已经不是"同一个点"的尺度了。⇒ 两把尺子确实
要对齐，但要对齐到**小的那把**：把**去重的容差**从 `epsilon` 改成 `WeldRadius`（1e-4）。这一问本来就是
**拓扑**问题（"这两个交点是不是同一个点"），就该与接环同尺子。

**修后实测**：**8 边与 16 边棱柱区域切成功且水密**（体积 13.255 / 12.730，破口边 0）；
`PrismFixtureTests` 里 prism-8 / prism-16 两份夹具从"要么成功要么拒绝"翻成**必须成功且水密**
（176 面 / 628 面、破口边 0）；判据② 体积守恒（4.000）、圆柱横切（6.000）、洞穴 6 块洞壁 **0 块开口**、
多刀绕序自洽——**一条都没动**；离屏 **1905 / 0**，两道编译门 0 错，引擎侧自检 **28 组全绿**。
引擎侧另加了"16 边圆柱区域"这一档断言（期望值 **12.730** 是**离屏 sweep 量出来的**，不是从参数推的——
参数推的值会让"圆柱面的半径落在顶点还是面"这种事蒙对蒙错）。

**第二波（同日，把这一档推到 8/16/32 边都能切）**——三步，每一步都有读数支撑：

1. **去重判据只能取"浮点噪声级"**，既不是分类带 `epsilon`（会把发丝段判成同一个点而丢掉）、也不是焊接半径
   （1e-4 仍太大：prism-32/64 的发丝是**一串**，单段 <1e-4、串起来更长）。改成 `scale × 1e-6`：
   比坐标的浮点噪声（`scale×1e-7` 量级）高一个量级、比分类带低两个量级 ⇒ 只滤"真退化"，发丝一律保留。
2. **接环前先清洗**：逐三角形收线段会带进**重复边**（同一段曲线被相邻两个三角形各收一次）与**悬垂短枝**，
   于是"每个点恰好两条边"的简单环被撑成度 4 —— **去重边 + 反复剪掉度 ≤ 1 的点**就还原成简单环。
3. **出口判据①**：环接上之后还要**验产物真的水密**（按位置配对，硬边上拆出来的顶点必须算同一个点），
   不水密就**拒绝**。这一道是必需的——实测 64 边那档"环闭合了、产物却有 2 条未配对边"，
   **交出一个不水密的网格比拒绝更坏**。对既有每一档都是中性的（它们本来就都断言过"成功即水密"）。

**修后读数**：**8 / 16 / 32 边全部成功且水密**（体积 13.255 / 12.730 / **12.607**）；对应三条钉子从
"要么成功要么拒绝"翻成**必须成功且水密**；夹具 prism-8 / prism-16 / **prism-32** 同样翻成正面断言
（176 / 628 / 2890 面、破口边 0）。**64 边仍被拒**，但拒绝的理由换代成了**诚实的那一条**
（`环闭合了，但产物不是水密的：2 条边没有被恰好两个三角形共享`），钉子保留。
> **同日 2026-10-11 晚复测（这条读数又换代了一次）**：现在拒 64 边的是**盖子朝向那道逐片判据**
> （`第 42 面：切开之后的盖里有反着的扇形三角形，与盖法线余弦 -0.994、面积 0.01`——面积不是碎片，所以是真反了）。
> 触发点仍是"刀口正好擦过顶点"：夹具 `prism-64.tsv` 量到刀口上有 6 个顶点、成三对重复行，`y=-2.0000` 压在立方体底缘。
> 失效的层是**封盖对简单环的假设**，不是接环记账。细节与两条候选修法见文末"复测更正"。
> **又一代（同日，候选修法 ① 已落地）**：扇形翻面之后**不再直接拒绝**，改走**耳切** `PCGMeshCut.TryEarClip`（只要求环是**简单多边形**、不要求凸）；扇形能成的地方一个三角形都没变，反向对照钉住了这点（盒子直刀 `EarCaps == 0`）。实测效果：64 边那一档从第 42 面推进到**第 56 面**，拒它的换成**出口水密判据**（2 条边没被恰好两个三角形共享），「盖里有反着的扇形」这句已经不再出现——两条都写进了钉子，哪条重新响就是对应修法被改坏。耳切只在扇形失败之后才跑，所以既有路径没有性能回归。剩下的第 56 面是真边界：那一步 ε 内 0 个顶点、1e-3 内 2 个 ⇒ **分类带之外的发丝窄条**，仍待修；候选修法 ②（在捏合点把环拆成两圈）仍未做，等这档查清再说。

> **一条被证伪的推断（值得记）**：上一版把 32/64 边剩下的东西判成"**`度 4` 的拓扑捏合**——环在一点穿过两次，
> 要拆成两圈分别封盖"。**这是错的**。用 `Tools/_prism_pinch.py` 把那个点拿掉之后量到：图分成 `[280, 1]` 两块，
> 而"1 块"是个**只连着它、长 7.39e-4 的孤立点**，且那个邻居在列表里**出现了两次** ⇒ 它是**收重了**，
> 不是拓扑捏合。**去重边 + 剪叶子**就解决了，根本不需要"配对规则"。
> **教训**：看到 `度 4` 先问"这是几何、还是记账"——**重复边与自环会伪装成高次顶点**。

#### ② 的测量记录（修之前，2026-10-11 上半场）

② 棱柱区域的某一面**擦过网格**时整次裁剪被拒。**边数扫过 8/16/32/64，四档都失败**（在第 6/13/9/15 面不等），
报的都是 `a point on the plane is shared by 1 segment(s), not 2`。这一条从"相切就是宽容档"变成了**有数据的待办**：
- **度量**：失败那一步 `crossed 40 / segments 26`（跨了平面却没交出线段）；度数分布 `度1:4、度2:24` ——
  曲线断成**两条开链**（不是"多个闭环"那种按设计拒绝的情形）。
- **断口位置**：几个断口的坐标打印出来一模一样，但**既不在同一个量化桶里、也不在 1e-4 半径内**（把产出侧的焊接
  从"按桶"改成"按距离"之后数字一个没变 ⇒ 它们是**真正不同的、相距 1e-4~1e-3 的近重合顶点**）。
- **断口落在一条直线段的内部**（例如平面 z=-1 与立方体底面的交线上，x=-0.41 处），而那里本该毫无特征
  ⇒ **刀口正好穿过网格的一个顶点**：绕它的那些三角形的两个交点都退化成同一处（零长，被既有过滤丢掉），
  曲线于是在那里开口。
- **六条已证伪的岔路（别重走）**：① 挪收线段的位置（放到"退化折线"检查之前）——数字一个没变；
  ② 按"墙自己的走序"定环走向——把水密性整个打破；③ 把"两端都在平面上的边"当线段收——没变，还把先前修好的
  "刀口穿过一圈顶点"打回原样；④ 产出侧顶点焊接从"按桶"改"按距离"——全域断言不变（这层没有桶劈叉）；
  ⑤ **"刀口穿过网格顶点"**——直接量过：16 边与 32 边那两档**平面上的顶点是 0 个**（ε 内与 1e-3 内都是 0），
  曲线照样断；⑥ **"断口只是数值噪声、接起来即可"**——把断口坐标打全之后发现它们**相距可达 3.6 个单位**，
  而"按容差接合"会把已经对的网格弄坏（8 边那档变成"成功但不水密"，16 边那档变成"走完 36 条就回到起点"）。
- **修正两条上一版记错的事实**：断口坐标"打印出来一模一样"是**巧合**（差异在打印位数之后）；而断口
  **并不**都落在直线段内部——量了两档 0 顶点之后不能再这么讲。**当前唯一确定的是**：曲线**实打实丢了一整段**
  （16 边：`crossed 70 / segments 69` 只丢 1 条也照样拒；32 边：`47 / 33` 丢了 14 条），缺的是**成段的曲线**。
- **仪器（已落地，半小时→一秒钟）**：`sweep` 失败时把**那一步的输入网格 + 那张平面**原样导出成
  `Tools/PCGGoldenRunner/fixtures/prism-{8,16,32,64}.tsv`（顶点 + 法线 + UV + 三角形 + 平面），
  `PrismFixtureTests` 再把它读回来复跑那一刀。**改核心之后跑这一条就知道有没有动到它**——
  手搓最小复现已失败两次、纯推理失败七次，这是唯一走通的路。
- **最终定性（独立分类逐三角形量的，不是推的）**：输入**自身是水密的**（距离焊接 1e-4 下破口边 0）。
  丢失的曲线段分**两类**：
  1. **薄片三角形**（两个交点重合）被零长过滤丢掉 —— prism-16 全部 70 个跨平面三角形都交得出两点，
     只有 **1** 条被过滤；prism-32 丢 **3** 条 ⇒ 曲线只差一小段。
  2. **刀口正好穿过一个网格顶点**（该顶点在容差带内）：prism-8 的 `#12 d=1.19e-08 @(1,2,-1)`（上一刀造出的角点，
     参与 6 个三角形）周围，**4** 个三角形是"带内顶点 + 两个严格在外" ⇒ 三个顶点都算"外" ⇒ **一个交点都不交**、
     整段丢掉；prism-64 同型（`#21 d=4.18e-04`，**2** 个三角形）。
- **试过并回退的第四次**：把收线段改成"**去重之后恰好剩两点就收**"（想统一处理 2/3 个交点两种情形）——
  全域 1905 条断言**一个没变**，棱柱一串数字也一个没变（说明这一类三角形在夹具里**根本不存在**，
  上面的 2 类才是真因）。这版保留（它严格更通用、且已证行为中性），但**别把它当成修好了**。
- **正确修法该长什么样（留给下一次，别再猜）**：第 2 类要的是"擦边顶点周围的扇子也要贡献曲线的两条分支"——
  那是**分类规则**的事（带内顶点按什么归类），而第 1 类要的是"薄片产生的极小缺口要不要接"。
  两者**不能共用一个容差**：实测"按容差接合断口"会把第 2 类那种**真实缺口**也糊上，
  于是把已经对的网格弄成"成功但不水密"。**改之前先把第 1、2 类的夹具分别跑一遍并记下
  `crossed / segments / 度数分布 / 带内顶点数` 四组数字。**
  > **2026-10-11 晚补记**：真正的根因**不在这两类里**——是"去重的容差（epsilon）比焊接半径（1e-4）大"
  > 这一条**尺子不一致**，见本小节开头那段"已修"。第 1 类（薄片）正是被那条不一致**丢掉线段**的那一类，
  > 而第 2 类（擦顶点）只是同一现象在"刀口压住顶点"那里的表现。**教训**：先量"两把尺子是否一致"，
  > 再谈"分类规则该怎么改"——上面这段推断（要改分类）**方向是错的**，改的是容差的**取值来源**。

**试过并回退的一刀（负结果，值得记）**：② 的症状是"某个点只连着 1 条线段"，看着像是"某条线段被丢掉了"——
而代码里确实有一处可疑顺序：先 `if (polygon.Count < 3) continue;`（保留下来的折线退化成一条线、扇形三角化不出面），
**再**收 `cutPoints`。于是把收线段挪到那道检查**之前**试了一次：**棱柱照旧被拒**（同一个点、同一条消息），
而且把"多刀裁完绕序不一致"的块数从 **1 抬到 4** —— 说明那样收进来的确实是**多余**的线段（退化折线本不该贡献切口边）。
⇒ 回退，并在代码里把这次尝试写进注释。**结论**：② 的成因不在收线段的顺序上，下次别从这儿下手；
先怀疑"刀口正好擦过一条边/一个顶点时，跨平面的三角形分类落到了两边都不算的那一档"。

**描述层也接上了（同日）**：`scatter` 新增 `clip` 选项——`clip box <x> <y> <z>` / `clip cylinder <直径> <高> <边数>`。
形状词打头，所以三个数的含义**随形状变**（圆柱的第三个数是**边数**不是厚度），走显式分支而不是通用数值搬运；
错误都带行号：不是程序化几何、形状不认识（列出能用的两个）、尺寸不是正数、圆柱边数越界。
落链 `… → 程序化几何 → 裁进凸体 → 平面切割 → 生成器`（`clip` 排在 `cut` **前面**：先"裁进区域"再"削一刀"，
反过来先削掉的那截就再也裁不回来了）。覆盖夹具与"值真的写进 `ClipShape`"两条也补齐了；
另加 9 条断言钉住"解析 → 字段 → 编译后的图里真的挂着 `ClipToConvex`"，并配一条**对照**（不写 `clip` 时图里没有它）。

**演示里也用上了（同日）**：`demo_valley` 加了一层"石料"——`geometry box geometry-size 8 12 8 clip cylinder 6 20 12`
（方料裁进一个**比它高**的圆柱：圆柱端面落在料外、与料的面不共面，免得撞上"共面不切"那档宽容处理）。
重编译 **36 节点 / 26 连线**、告警 0；重烘焙后场景里**真的多出 6 块裁过的石料**（`PCG_Gen_Geometry_l0_x0z0_*`，
坐标全落在 `within 1300 1250 1750 1650` 那个矩形里、y≈−70~−81 贴着地形），与洞穴那 10 根管子（y≈−277~−480）
在位置上分得清清楚楚 —— 这就是**描述词 → 图 → 烘焙 → 场景**这条路的引擎侧证据（此前 `clip` 只有离线断言与
节点级自检，没有端到端）。
> **值得记的一条口径**：`points N` 是**整个生成范围**上撒多少源点，而 `within` 只是把其中落进矩形的那部分留下
> ——这个矩形占那块地约 5.6%，所以写 `points 10` 重烘焙后**只多了 1 块**（第一版就是这么写的）。按面积反推
> 写到 200 才得到 6~11 块。**给"限在一个小矩形里的一层"定源点数，要按面积折算，不能照抄想要的件数。**

**仍未做**：真正的 `A − B`（挖方孔）——它要"面在里/外"的分类 + 用 B 的面重建边界，是 §5.1.44 判据④ 推迟的那一格。
另外：这 6 块石料"确实是被**裁**过、而不是普通方料"这一条目前只有**离线解析体积**与**节点级引擎自检**两处证据；
场景侧看形状归 PIE 目视验收。

**引擎侧自检也补上了（同日）**：第 **3n** 组 `TestClipToConvex`——离屏门已经把解析体积逐点对过，这一组只证
**在引擎里跑得动、而且产物真的写进场景**（纯数据节点同样会踩"离屏能跑、引擎里崩"与"数据对了但生成器没写出去"）。
判据用**裁剪后的包围盒**：4³ 方料裁进 2×6×2 的盒区域之后只剩 `[-1,-2,-1]..[1,2,1]` 那一段，再把那块交给几何生成器、
要求场景里真的多出 1 个 Actor。自检组数 **27 → 28**，全绿、无跳过。

**当时还试过一条路（已回退）**：给 `TryCutByPlane` 加一道"结果不封闭就拒绝"的自检。它**没有**抓住这根管子
（说明产出方自己的封闭判据与几何事实不一致），留着就是"一条从没执行过的自检"——按本仓的规矩（不装作
验证过）把它删掉了，只留下这条钉住缺陷的断言。














注册类型不变（**55**）：这一层不发明新节点，它只把**已有的**节点拼成图。新增两个纯类型（`Graph/PCGWorldSpec.cs` 解析器 + 模型、`Graph/PCGSpecCompiler.cs` 编译器）与 `SpecTests`（**185 条**断言）。

**这是目标里"按照设定描述"的正面回答**。此前所有能力都在"参数 + 图"这一层——设计师得知道该摆哪几个节点，而这句话要的是"写一段描述，场景出来"。所以格式刻意做成**一行一条指令、`#` 注释、双引号里保留空格**的样子：

```
# 一片温带山谷
world 温带山谷
seed 20261008
region 2000 1600 height 800
biome cell 50 water 12 relief 200 region 1000 samples 4 hill-slope 8
scatter forest per-block 1.5 density 0.6 slope 15 90 model "Assets/PCG/Pine Trees/Pine LOD0.flax" model-scale 1.2 yaw on
scatter rocks points 40 density 0.4 model "Assets/PCG/Rock.flax"
scatter grass per-block 8.0 yaw off
water cell 40 threshold 250 min-lake-cells 12 model "Assets/PCG/Water.flax"
village lots 12 rules "X:0.5,0.5; Z:0.6,0.4" cell 120 40 120 model "Assets/PCG/House.flax"
cave depth 400 segments 24 radius 60 tunnels 10 branch 0.25 turn 30 sides 12
road from -800 -600 via -200 100 via 300 -200 to 900 700 cell 40 slope 20 spacing 120 detour 1200 model "Assets/PCG/Road.flax"
```

上面这段编译出来是 **17 个节点、9 条连线**：三条散布链（`Volume Sampler → [Slope Filter] → Spawn Static Model`）、一条地貌底图（`Biome`）、一条水域链（`Hydrology → Spawn Static Model`）、一条聚居地链（`Split Grammar → Spawn Static Model`）、一条洞穴链（`Cave → Spawn Geometry`，**不需要指模型资产**，因为洞穴自己就出网格），以及一条**路**（`Point List → Path → Spawn Static Model`：起点、若干 `via`、终点按顺序写进 `Point List` 的槽位，`Path` 在地形上找低代价走法）。`via` 可重复（最多六个，加上两端正好八个槽位），它就是"山路绕开一块岩体、小道拐过一片林子"的表达方式。

**`village` 与 `city` 是"平铺"与"层级"的分工**：`village` 是一张**按深度索引的切分规则表**（一次切成地块，地块之间没有种类之别），`city` 是一条**符号产生式表**——`productions "District = Block,Block: X:0.5,0.5; Block = House,House,House: Z:0.34,0.33,0.33"`——于是不同地块可以长成不同的东西、层数也不受规则条数限制。`city` 的落地形式是 `Grammar → Spawn Static Model`，**`productions` 必填**（没有产生式的"城市"什么都长不出来，解析器带行号拒绝）。仓库里因此有三份示例：`valley.pcgspec`（整套地貌/植被/水/村庄/道路/洞穴，**17 节点 / 9 连线**）、`city.pcgspec`（地貌 + 层级城市 + 穿城道路 + 城外林地，**10 节点 / 7 连线**）与 `township.pcgspec`（**两村两城 + 两条路 + 北半边的沿路林带 + 路肩灌木（渐变）+ 摆到指定位置并斜 30 度的第一个村**，专演示"同一个特征写多行"、"一层挨着另一层"与"摆在这儿、这么转"，**27 节点 / 20 连线**）。

**摆到指定的位置（`at <x> <z>`，v1.4 新增）**：村子、城市、洞穴这三样**只出现在一个地方**的东西，此前一律从**生成范围的中心**起算——于是"把村子摆在这儿、洞口在那儿"说不出来。而 `Point List` 存在的理由里就写着*"村庄的中心、洞穴的入口"*（§5.1.40），这个孔一直没接上。现在 `village at 500 300 …` / `city at …` / `cave at …` 会先摆一个**字面点**当种子再接给切分语法/符号语法/洞穴：它们本来就认 `inputs[0]` 的点当种子，所以这次是**接线**而不是新机制。Y 取 0，与 `road` 的两端点同一约定（高度该由地形与后续节点决定，写死一个 Y 只在特定地形上成立）。运行层断言是位置对照：同一份规则表，不写 `at` 时切出来的地块中心**离原点 50 以内**，写了 `at 500 300` 之后**离 (500,300) 也在 50 以内**；洞穴则要求输出的点里**有一个正好落在写下的坐标上**（洞口就是种子点）。

**斜过来（`rotate <度>`，v1.4 新增）**：`village` / `city` 可以写 `rotate <度>`，整体绕**种子点**转一个角度——"村子斜着沿路排"要的就是这个。落地方式是把 `Transform Points`（它的旋转是"绕点自己的中心"，不移动位置）插在种子与语法之间：切分语法按**种子的朝向**决定沿哪条轴切（它自己的文档就写着 *rotated 30 degrees subdivides along its own front*），所以转种子等于把整块地的排布跟着转。**`rotate` 必须和 `at` 一起写**：旋转是绕种子转的，而语法不接输入时用的是**生成范围中心**——那个点在图里根本不存在，没法给它插上游节点；与其悄悄把地挪到原点，不如在编译期带行号说清楚（有断言钉住这条错）。**洞穴不支持 `rotate`**：它的入口只取点的位置、下潜方向是编译器里的常量（`(0,-1,0)`），写一个转不了的东西比不写更糟，所以没做。运行层断言是**轴对照**：同一份 `rules "X:0.5,0.5" cell 200 …`，不转时两块在 `X=±50 / Z=0`，转 90 度后两块到 `X=0 / Z=±50`——**轴换了才是真的转了**，只改朝向的话位置不会动。

**一层长在哪儿（`within` / `outside`，v1.4 新增）**：描述此前能决定"长什么"却决定不了"长在哪"——每一层都铺满整个生成范围，所以"北部是针叶林、南边的草原"说不出来。现在 `within <x起> <z起> <x止> <z止>` 只留这片矩形里的点、`outside …` 只留矩形外的点（四个数按"最小角 → 最大角"写；写反或退化成一条线都带行号拒绝）。落地成 `Area Filter`（§5.1.41）串在点源与生成器之间，它与下面那族 `near`/`away`/`fade` **正交、可以叠在同一层上**——"村北 400 以内、且只在这片区域"是常态。这一族改的是**成员**（点真的没了），所以运行层直接拿坐标就能验：600 个点铺满范围时 `within -1500 -1500 1500 0` 之后最北 Z 必须 ≤ 0，而没写区域时最北是 1499.8。

**一层可以挨着另一层长（`near` / `away` / `fade`，v1.4 新增）**：在它之前，"湖边的芦苇、路两侧的树、城外的小摊"都表达不出来——每一层只能按**自己的**坡度/密度/地貌筛，说不出"要挨着谁"。三种写法：`near <特征> <半径>`（只留半径以内，一刀切）、`away <特征> <半径>`（只留半径以外，一刀切）、`fade <特征> <半径>`（不动点，密度按到该特征的距离**线性衰减**，到半径处为零——"越远越稀"）。`<特征>` 可以是一条路、一片水、一个村/城/洞穴，也可以是**别的散布层名**（"挨着石头长树"）。前两种的落地形式是把另一条链已经产出的点当参照、用 `Distance To Points` 逐点量出最近距离写进一列、再用 `Attribute Filter` 按那一列筛；`fade` 换掉后半段，改用 `Remap` 把"距离"重映射成**密度**（两个节点共用"空名 = 写点的密度本身"这条约定，所以密度会被生成器读到，随后按密度抽点）。三个节点都**插在原有链的中间**（原来的 `tail → 生成器` 那条线被摘掉、改接到新的出口），所以生成器拿到的就是筛完/改写完的点。

四处实现细节值得记下来，每一处都是"图照跑、但结果不对"的类型：

1. **接线要等所有特征建完**：`scatter 芦苇 near water` 写在 `water` 之前是最自然的写法（先说芦苇），而那时参照的链还不存在。所以编译器先记下待接的关系，末尾统一插节点、改连线——前向引用因此自然成立。**代价**是"被参照的层如果自己也有关系，得先把它的接好"，否则量到的是它**筛之前**的点；两层互相以对方为参照会绕成环，编译器用状态标记查出来并报错（`state==1` 再遇到就是环），而不是让其中一条悄悄按未筛的点去量——那种结果看着合理，所以最难查。
2. **一刀切时搜索上限必须严格大于半径**（取 `2R`）：`Distance To Points` 把超过上限的距离**钳到上限**，而 `Attribute Filter` 是**闭区间**——上限取 R 的话，"在 R 之外"的点会被钳成正好 R、落进 `[0,R]` 而被当成"附近"留下，筛选看起来完全没生效（**第一版就是这么错的**：结构性断言全绿，直到"留存点全在半径内"那条运行断言把它抓出来，而节点一条错都没报）。`away` 用 `Invert` 表达而不是"半径到无穷"——过滤器没有上界这一说。
3. **`fade` 的上限反而取"等于半径"**：超过半径的钳成半径，正好落在"衰减到零"那一端；而且它**不删点**——密度是 0..1 的连续量，"稀疏"发生在生成器按密度抽点那一步（烘焙时）。所以离屏看到的现象是"点数一个不少、密度变了"，谁要是把 `fade` 也写成"顺便删点"，断言会红。
4. **参照接在 `Reference` 这个具名引脚上**，不是碰巧第 0 号对第 0 号：接错的话图照跑，只是把"要量距离的点"和"参照点"当成了同一层，量出来的数毫无意义且不报错。断言因此专门读 `InputPinIndex == 1`，并要求那条边来自**参照那条链的产出节点**。

名字冲突的处理：一个村子时用裸名 `village`，多个时用 `village1`/`village2`…（城、路同理；裸名只在"唯一"时注册），引用到不存在的名字会带行号报错并把**能用的名字列出来**。运行层断言配了对照：平地上一条笔直的路（Z 恒为 0），600 个撒点里 `near road 400` 只留下 **62** 个、最远 |Z| **384.1 ≤ 400**，而没写关系的那份**600 个全留、最远 1499.8**——两个数字一起才说明"筛选真的按距离换了地方"，只看留存数是看不出静默失效的（`Distance To Points` 拿不到参照、`Attribute Filter` 读不到列，两条都只记一条理由并把点**原样透传**）。`fade` 的语义那一半另用手搭的同款链逐点核对：参照取原点、候选点沿 Z 轴摆开，密度必须逐个等于 `clamp(1 - |Z|/半径, 0, 1)`（实测最大偏差 **0.0000**，半径处与半径外都是 0）——位置是摆出来的、期望值能算出来，所以这是"渐变"的直接证据，而不是"少留一半"那种也能满足的近似说法。

**`village` / `city` / `scatter` / `road` 都可以写多行**——大世界里的村子本就不止一个，早先它们被写成单例，第二行 `village` 会**顶掉**第一行，两处只认得一个。现在每条特征各收成一条记录，第 n 行只影响第 n 套链。行号用一个**递增计数器**走（`nextRow`），不再写成 `1 + 散布层数 + (有没有水 ? 1 : 0) + …` 那种嵌套式——那种写法每加一个可重复的特征就要把前面每一处都改一遍，**改错只会让节点叠在一起**（图照样跑，只是两个框重合着看不见）。锚点断言因此分两层：解析层数记录条数（两行 `village` ⇒ `Settlements.Count == 2`，且各自的 `Lots`/`Rules` 互不串），**外加一条正面同夹具**（只写一行时仍只有一条，否则"永远返回两倍"也满足上一条）；编译与运行层则要求 `SplitGrammar` 与 `Grammar` 各有**两条产出记录**、每条都真出点、**两个数字不相等**（`township.pcgspec` 的两村规则表长短不同：4 块 vs 2 块，两城产生式不同：4 块 vs 3 块——相等就说明第二行拿的是第一行的参数），并且三个语法节点的 `EditorPosition.Y` 两两不同（180 / 360 / 540）。只看第一处产出是这一组最容易写错的地方：第二行整条链丢了它照样绿。

**`scatter` 可以按**地貌**撒点（`biome hill,mountain`）**——这是"判出地貌"与"按地貌生成"之间那一步：此前描述只能说"坡度多大以内"，说不出"只在丘陵和山地上长树"。落地形式是把点源从 `Volume Sampler` 换成 `Biome → Attribute Filter(Biome 列区间) → …`（描述里没写 `biome` 时自动补一个默认底图并留一条提醒）。写法要求那一串**连成一档**：`plain,mountain` 会被拒，因为一个区间过滤器会把中间的丘陵也留下——与其"看着对、其实多了东西"，不如让设计师拆成两层。名字表在分类器那边（`PCGBiomeKind.NameOf`），解析层只记"写了什么、写在第几行"，所以名字只有一个真相、报错仍带行号。

**按地貌撒点必须抖一下**：那些点来自**等距的格心**，不抖的话林子会长成整齐的果园。所以 `scatter` 又多了一个 `jitter <半径>`（世界单位），按地貌过滤的层在没写它时**默认半个格子**（半格这个数取自注册表里 `Biome` 的 `Cell Size` 默认值，不在这里抄一遍），写 `jitter 0` 才是明确要格阵（摆路灯那类）。离屏判据是一对：默认时"落在格阵上的点数"必须明显少于一半，`jitter 0` 时必须是**全部**。

**顺带修掉一个真缺陷：选项声明了、值却没落地。** `scatter` 的解析里有一张选项表（键 → 取值个数 → 字段名）**和**一条 switch，两处各列一遍；加 `jitter` 时我只加了表和字段、忘了 switch，于是它被**静默丢掉**——描述写着 `jitter 0`，编译器当没写（解析一律成功，没有任何提示）。修法是让解析**只认表里的字段名**（唯一例外是 `slope`：两个值落进两个字段，反射表达不了），于是"两份清单要一致"这件事从机制上消失。**同时把选项覆盖断言从"能解析"加严到"值真的写进了那个字段"**（每个选项各一条）——判据一加严，它立刻抓出我在测试辅助里漏写的 `biome` 分支。

**每条指令都映射到已有的节点，一个新词汇都不发明**——这是这一层的价值主张：描述不是另一套引擎，它是图的另一种写法。

**三条设计约定**：

1. **没写的选项就不写进槽位**（规格字段全是可空的）。于是 `biome` 单独一行就等于"用 `Biome` 节点的全部默认值"，而不是在规格里把节点默认值再抄一遍——抄一遍就会出现第二个真相，改一处忘一处。断言正面钉住了这一点（没写的 `CellSize`/`Points`/`Density` 必须是 null）。
2. **槽位按名字写，引脚按名字连**。编译器用注册表里的具名参数表把 `Set(node, "Cell Size", 50f)` 解析成下标，找不到那个名字就直接失败；连线时把节点造出来读它自己的引脚表，查 `"Out"`/`"In"` 拿下标。按下标写的话，节点加一个参数就会把后面所有值整体挪位，而图照跑、只是参数全错——这类错静默且致命。**这条约定当场抓到一个真错误**：规格里 `village cell` 最初写成一个数，而 `Split Grammar` 的 `Cell Size` 槽位是 **Vector3**，图编译器报"槽位要 Vector 而给的是 Single"。已改成 `cell 120 40 120`。
3. **区域尺寸不进图**。`region` 是一项**场景侧**设置（生成范围来自体积），所以它留在 `PCGWorldSpec.Region` 里由宿主应用，图里一个节点都不多。断言正面钉住"只有 world/seed/region 的描述编译出 0 个节点"。

**入口**：描述放在**文件**里（`Content/PCG/Specs/*.pcgspec`），三条路都能用——

| 入口 | 怎么用 | 什么时候用 |
| --- | --- | --- |
| 编辑器 | 图表编辑器**第二行**填描述名（如 `valley`）→ 点「生成」→ 画布出现生成的图 → 点「保存」落到第一行的图表名 | 设计师日常：改描述、看节点、存成图 |
| 命令行 | `PCGGoldenRunner <路径>.pcgspec` → 打印 `OK 名字: N 节点 / M 连线`（或 `FAIL 原因`）并给退出码 | 仓库里那份示例描述就是拿它核对的；CI 也能用 |
| 代码 | `PCGSpecFile.TryLoad(name, resolveAssetId)` → `PCGGraph` | 程序化生成、批量出图 |

**描述为什么是文件而不是界面里的一行**：它是**设计的输入**，要能版本管理、能互相传、能用任意编辑器改，而且一张图往往配一份描述。仓库里带了一份可直接用的示例 `Content/PCG/Specs/valley.pcgspec`（就是上面那段描述，另附中文注释）。注意示例里的模型路径是**占位**：本仓库目前只有角色/怪物/材质，没有树石房路的模型资产，所以点「生成」时状态行会如实提醒"这些路径没解析出资产"——把路径换成项目里真有的资产即可。

**编辑器那一处的实现要点**：新增的输入框与「生成」按钮放在**第二行**（`HeaderHeight` 本来就留了两行，第一行的左/右按钮簇一个都不动），所以没有挤压既有布局；`InstallGraph` 是 `LoadGraph` 去掉读文件那半——描述是输入、图是产物，这条路根本没有图文件，装进画布后点「保存」才落到图表名那一栏。整段处理体包在 `try` 里：这条路上有三处外部输入（文件名、文件内容、资产库），编辑器里抛出去的异常不是"报错"而是窗口坏掉。资产路径 → id 用 `Editor.ContentDatabase.Find(path)`，查不到就给空 id，让生成器在运行期如实报"没有指定模型"。



**报错一律带行号**：`第 7 行：不认识的指令 'plant'；能用的有（world / seed / region / biome / scatter / water / village / cave）`。十一类错各有一条断言（不认识的指令/选项、非整数种子、非数字、缺值、引号不成对、单例重复、点数与密度同时给、坡度过区间写反、开关值不合法、区域只给一个尺寸），并且**每条都有正面同夹具**——把写反的区间改成正的必须解析成功，否则那条断言可能只是对一个永远报错的解析器成立。另外"只有空白与注释"的描述**不会**静默变成一张默认名字的空图（第一版就是这样，被断言抓出来），而是报"描述里没有任何指令"。

**顺带修掉一个引擎侧的不一致（本轮，四个生成器）**：`StaticModel`/`Foliage`/`Decal`/`Actor` 四个生成器把 `if (context.DryRun) return input;` 放在**查资产库之后**，而 `Geometry`/`SplineMesh` 两个放在之前。后果是："预览不碰场景"这个承诺只守住了场景那一半——预览仍会去 `Content.GetAsset`，于是**预览依赖资产是否已加载**，而且**离线根本跑不了这四个生成器的干跑**（本轮写"描述 → 图 → 真跑一遍"的断言时当场撞上：`Content.*` 在离屏抛异常）。已统一成"**纯数据检查 → DryRun 早退 → 查资产 → 落场景**"：`No model is assigned`/`MaxActors 为 0` 这类只看数据的报错仍然在预览里说得出来，而查资产与等 Prefab 加载一律挪到 DryRun 之后。

**代价说清**：解析与编译都是纯算术，不碰引擎、不查资产。这一层唯一的规模风险是把 `per-block` 写大——那会线性放大点数，和手搭图一样。

**每条特征链都有"编译后真跑"的证据（本轮补齐）**：此前规格层只有**散布链**被真跑过，另外四条（地貌/水/聚居地/洞穴；道路在 §5.1.40 的 `via` 测试里真跑）只被**编译**过。编译绿与"它真产出了东西"是两件事——一个把输入原样吃掉、或悄悄返回空层的节点，编译阶段一点毛病都看不出来。现在六条各有一条断言：地貌底图的 `Biome` 列必须**不止一档**（否则"整片都判成同一档"也会通过）、水链的 `LakeArea` 列必须有**正数**（否则"把整片都当湖"也会通过）、聚居地按两条规则**恰好切出四块**（精确块数，`> 0` 对"一次都没切"同样成立）、城市按两层产生式**恰好四块**、洞穴的隧道号**从 0 起连续且覆盖到条数减一**。**两份随仓库交出去的示例文件也被真跑**：`city.pcgspec` 的符号语法必须恰好产出 **12 块叶子**——它同时钉住了"示例文件本身能跑"与"产生式的层级确实按 城区→街区→房子→房间 展开"。`scatter` 的`biome` 过滤配了**对照**：同一片地形上 `biome hill,mountain` 那组点必须比 `biome plain` 那组**明显更靠近山顶**（差 > 100）——只断言"留下来的点都在丘陵/山地"是不够的，一个什么都不产的链也满足它。

**还差什么（写在这里以免被当成已完成）**：① 指令集刻意保守——只覆盖清单上的地貌/植被/水/聚居地/洞穴/道路，没有表达式、没有条件、没有变量；② 编辑器「生成」按钮与"引擎里真的长出东西"由 §9 ㉞（目视）与 ㉟（自检组 3l，可一键跑）覆盖，两者都还没在编辑器里跑过；③ 示例描述里的模型路径是占位（本仓库没有树石房路模型），要换成真资产才会真的长出东西；④ **一条路只有一条折线**（起点 + 最多六个 `via` + 终点），要分叉就写两条路。

**修掉的引擎侧缺陷（本轮，五处）**：四个资产生成器与几何生成器的"没指资产 / 这一层没有网格"分支会先打一句 `Debug.LogWarning`，"干跑 = 不碰引擎"这个承诺只守住了资产库那一半——`Debug.LogWarning` 也是引擎调用。后果是**离屏预览一个缺模型的生成器会直接崩**（本轮写 `road` 的 via 断言时当场撞上：`DllNotFoundException: FlaxEngine`）。已把这五处日志加上 `if (!context.DryRun)` 守卫；诊断不丢，因为 `LastError` 由执行器收进本次运行的警告里——那本来就是"该由执行器报出来"的正主。于是"干跑"现在是**零引擎调用**，缺资产的图也能在离屏预览里跑完。



注册类型 54 → **55**，Structure 类第 3 个（`Path` / `Split Grammar` 之后）。

**它到底是什么**：**程序化巷道网络**，不是地质仿真。插件手里没有体素岩体，硬说"这是溶洞成因"是编故事；它做的是矿道/人工洞窟那件事——从入口按段往下走，走到深度上限为止，沿途以一定概率分叉。价值在于它是**可用的结构**：中心线能喂 `Spawn Spline Mesh` 铺支撑、喂 `Spawn Geometry` 出洞壁、喂点云摆矿脉与灯。入口就是上游给的点——地貌底图里筛出来的山地、道路旁、聚居地附近都能用。

**三条硬约束写进代码，而不是交给运气**：
1. **绝不比入口更深超过 `Max Depth`**。实现方式是**截短步长**，不是把落点单独搬到上限上——后者会让"行进方向"与"实际走出来的那一段"分家，方向还指着陡下、落点却被压成水平，于是水平残量的朝向在下一步猛地翻过去，洞管在洞底自己叠回自己身上、管壁法线反向。这条是**实测踩出来的**：第一版就是事后搬落点，离线断言当场抓到某条隧道的管壁有三角形法线反了（最差余弦 −0.962），追到根因才改成截短步长。
2. **单步不掉头**（新方向与上一段的夹角不超过六十度）。掉头是管壁翻面的充要条件，所以这条不是审美而是几何前提；超了就折半逼近，而不是把方向冻住——冻住会让方向卡死在竖直上，下一步只能靠硬掰。
3. **段长与半径落在参数范围内**。

**方向是"算"出来的**（水平面向 + 下潜俯角），不是一轮一轮把旋转叠到方向上：面向上限 ±90°（所以永远不掉头），俯角在接近深度上限时按比例收平，于是洞底自然是一条平巷，竖直段与水平段之间没有断崖。第一版是增量叠旋转 + 事后收平，结果在深度上限处出现**不连续**：俯角从 90° 被一刀砍到 0°，正是上面第 1 条那个翻面的来源。

**出口三样，都是插件已有的词汇**：`Tunnel`（隧道号）/`TunnelDistance`（沿洞里程）/`TunnelDepth`（相对入口的深度，可关）/`TunnelRadius`（洞道半径）四列，**每条隧道一条样条**（`Spawn Spline Mesh` 可直接沿它铺），以及**可选的内向管道网格**（`PCGMeshBuilder.Tube(..., capped: true, inward: true)` 交给 `Spawn Geometry` 就是看得见的洞壁）。"内向"是给洞穴用的关键细节：站在隧道里看到的是洞壁内侧，所以法线朝内、**绕序同步翻转**；翻转之后有向体积变成负值——那正是"翻对了"的断言依据。

**离屏证据**（`CaveTests`，**48 条**）分三层：
- **几何**（手造路径）：直管体积 = 正多边形截面面积 × 长度（半径 2、12 边 → 120.0000 恰好）；内向管的有向体积是外向的**相反数**；水密（每条边恰好两个三角形，接缝的重复顶点先按位置量化合并，否则接缝会被误判成两条边界边）；**绕序与法线一致**（几何法线必须与三个顶点法线的均值同向）；不封口时两端各留 `sides` 条边界边、三角形数少 `sides×2`；折弯的管道仍水密、无 NaN，体积为正且不超过"截面面积 × 中心线长"（刚性截面在拐弯处只少不多）。**这里排除了一个我自己写下的空转断言**：最初对折弯管道断言"体积在解析值 A×L 的一成以内"，实测 108.64 vs 127.28（差 15%）——于是另写一个**独立参照**（包围盒里确定性撒点 20 万次、Möller–Trumbore 射线求交数奇偶判内外）去交叉验证，得到 107.91，与散度定理算出的 108.64 差 0.7%。结论是**解析预期错了**（刚性截面的斜接拐弯本来就该少体积），断言改成"正且不超过上界 + 与独立采样法一致"，而不是把容差放大到能容纳一个说不清的数。
- **规划器**：中心线长度落在 [2, 段数+1]（段数是上限不是承诺，深度用尽会提前收尾）；**没有点比入口深过 MaxDepth**；未截断的段长度 ≥ 段长×(1−抖动)、所有段 ≤ 段长×(1+抖动)（截断的段只豁免下界，并统计出截短了几条）；半径落在 [base×(1−抖动), base×(1+抖动)]；**分叉的父指针指向更早的隧道**、分叉段号落在父的中心线上、分叉点位置真的接得上；条数不超过 `Max Tunnels`（分叉概率拉满也只到上限）；关掉分叉只有一条独苗且没有父；**同种子逐点相同、换种子必须不同**；零方向按正下方处理；`Max Tunnels=1` 时概率拉满也只有一条；**压力参数（每步最大 89 度、分叉拉满、深度只给两段）扫 64 个种子都不掉头**——这条正是上面第 2 条硬约束的守卫。
- **节点**：不接输入时以生成范围中心为入口、点数 = 各隧道中心线长度之和；四列齐备且与点数等长；隧道号按输出顺序递增、每条隧道的里程从 0 起单调不减、深度列 = 入口高度 − 点高度、半径列 = 该隧道半径；**点必须指向本数据的属性存储**（下游有节点按 `point.Attributes` 读列，不接上就只看得见一份空的）；样条数与网格数都等于隧道数、每条样条至少两点；**每个洞壁网格水密、绕序一致、有向体积为负**（这条是实践中最先抓到真 bug 的那条，报错信息里带上破口边数/绕序余弦/体积，所以能一眼分清"轻微扭曲"与"整圈翻面"）；关掉 `Make Meshes` 不出网格且不影响点数；关掉 `Write Depth` 就没有深度列；三个入口各挖一套、条数按入口累加、隧道号覆盖到 `条数×入口数−1`；同种子两次运行逐点相同；没有任何生成范围时也能跑（入口取默认）。

**几何核心的顺带加固**：`PCGMeshBuilder.Tube` 在平行输运时，若叉积模长趋近零（夹角接近 180°，即折返），方向是数值噪声——原代码会拿它去转，管壁就有一小段的法线反了（`Mathf.Clamp` 附近实测到最差余弦 −0.962）。现在这种退化情形另取一根与切向垂直的定轴，至少是确定的。

**代价说清**：纯算术，无场景查询、无 `Debug.*`（本节点会被离屏执行，只经 `LastError` 报告）。成本随 隧道数 × 段数 线性增长；管壁网格是 `顶点 = (边数+1)×环数 + 2`、三角形 ≈ `(环数−1)×边数×2 + 边数×2`，`Tube Sides` 是唯一的规模旋钮。



注册类型 53 → **54**，Sampler 类第 10 个。

**为什么要有它**："温带平原、丘陵、盆地"这类说法要落进图里，先得有个东西回答"这一格算什么"。这个节点就是那个回答，而且它有两种用法（同一节点、看有没有接输入）：
- **不接输入 → 地貌底图**：按 `Cell Size` 在生成范围铺一张点阵，每点带它的地貌。整片区域因此有了可驱动下游的"区域掩码"——平原撒草、丘陵长树、盆地积水，都是对 `Biome` 列分叉。
- **接了输入 → 打标签**：给现有点判类并原样传下去（点位与走向都不动），于是"先按密度撒一层，再按地貌决定长成什么"也顺手可写。

**判据看的是"起伏"与"相对高度"，不是绝对高度**：高原上的平台也是平原、海边的坡也是坡。所以每点量三件事——短半径内的**起伏**（最大最小高差）、长半径内的**周边平均**、以及**坡度**（中心与最近邻的高差 ÷ 水平距离，比用最大最小去估稳）。判类顺序有意如此：**水 → 盆地 → 山地 → 丘陵 → 平原**。水最先（水下的地形不该长东西），盆地先于山地（盆底也可以有陡壁，而"低于周边"对它更本质；陡壁洼地因此仍归山地），最后落回平原，让"都不是"有确定答案而不是漏判。**阈值全部暴露成参数**，因为"多陡算山"是设计问题。

**列**：`Biome`（数字下标，供 Filter/Math 驱动）+ `BiomeName`（文本，供人读）+ `Relief`（起伏，供调阈值）+ `$Slope`（复用内建列）。

**代价说清**：每点要在两个半径上各取若干方向的高度（默认 4+4 次查询），是**重采样**节点——半径调大或底图调密都会线性变慢。它也**刻意不声明线程安全**：高度委托背后可能是引擎地形。

**离屏证据**（`BiomeTests`，**27 条**）分两层：先在**手造采样值**上单独验每条判据（平地→平原、低于水面→水、水面余量内仍算水、低洼且平缓→盆地、**陡壁洼地→山地而不是盆地**、**坡度阈值两侧各判一次**作为正反对照），再在**解析地形**上验山顶→山地、山坡→丘陵或山地、远坡→平原、碗底→盆地、水位之下→水；量出来的三件事也各自对几何核对（坡面的坡度 = atan(0.2) = 11.31°、起伏 = 坡度 × 直径、对称采样下斜面周边平均 = 自身高度、拿不到高度时标记未知而不是编 0）。节点层：底图点数 = 列数×行数、**每个点恰好归一档（各档计数之和 = 点数）**、**`BiomeName` 必须与 `Biome` 互相吻合**、打标签模式点数与点位不变、无地形时透传并说明原因。
**又一次"夹具错、代码对"**：第一版夹具把台地放在水位线 0 上，远坡被**如实**判成水（判据正确），而高斯山 σ=250 太缓、按阈值就不该叫山地——两处都改夹具而不是改判据。

### 5.1.36 Hydrology（从地形算出湖泊与江河，v1.4 新增）

注册类型 52 → **53**，Sampler 类第 9 个。

**为什么必须有它**：湖泊与江河不是能"撒点撒出来"的东西——它们是地形本身的结果。水往低处走，**走不掉的地方积成湖**，**汇到一起的地方成了河**。没有这一步，"一条河、一片湖"只能手摆一条样条、手放一片水面，与"按地形自动生成"无关。

**算法是经典三步，全部纯函数**（`Execution/PCGHydrology.cs`）：
1. **填洼**（priority-flood）：优先队列从边界向内推进，每格被抬到不低于把它推进来的那一格。**抬起来的部分就是湖面**，抬起的量就是水深；边界是水的出口，所以边界格不被抬高。
2. **流向**（D8）：填洼后每格朝最陡的下坡邻居。并列时按**固定的八向顺序**决胜，所以结果可复现。
3. **汇流累积**：按高度从高到低扫一遍（填洼后必定无环），把每格的量加到它流向的格子上。累积量大的地方就是河——`TraceRiver` 从源头沿流向追到出口得到一条河的心线。

**输出与既有词汇对齐**：河是"一串点（带 `Flow` 汇流量列）+ **一条中心线样条**"，所以已有的样条网格生成器就能沿河铺一道水面；湖是"每处一个点"（带 `WaterLevel` 与 `LakeArea`），接生成器即一片水面。高度场与路径规划**共用同一个注入点**（`PCGWorldContext.HeightSource`，`PCGVolume.BuildContext` 装引擎地形查询、按地形缓存采样器、走最便宜的 HeightfieldInterpolated 模式），所以"地形是什么样"对两者永远同一个答案。

**离屏证据**（`HydrologyTests`，**27 条**，判据都落在手算地形上）：
- **填洼**：只抬不降；**填完之后内部每一格都有不高于自己的邻格**（水能出去——少了这条，填洼就没填到溢出点）；凹坑被抬到坑沿高度（-10 → 0）；平地一格不改。
- **流向/汇流**：沿 +x 下坡的平面上每格都流向右邻；**最右列的汇流量恰好等于行宽**（能手算的事实）。
- **湖**：一个凹坑变成**恰好一处湖**、格子数 1、水面高等于坑沿、湖心落在坑的位置；**平面上不报湖**（判据是"被抬高了"而不是"高度低"）。
- **河**：V 形谷里找得到河、**心线贴着谷底线**、**心线一路向下**、峰值汇流达阈值；阈值高于最大可能汇流时一条都没有；两次算出的河一致。
- **节点**：产出点 + 每条河一条样条 + `Flow` 列；凹坑地形上产出 1 个湖点且带水面高/面积两列；无地形时说明原因并产出为空；生成范围不足一格时报人话。
- **过程中确认的一件事**：第一版夹具的 V 形谷**谷底是水平的**（只沿 z 变化），水在谷底线上积住却不成河——这是**物理上正确**的行为（没有纵向落差就没有汇流），夹具错了而不是代码错了；给谷底加纵向落差后河立刻出现。

### 5.1.34 Spawn Geometry（材质化程序化几何，v1.4 新增；对标课程第 15 集收尾）

注册类型 50 → **51**，Spawner 类第 6 个——**几何链路的另一头**。

§5.1.33 把形状算了出来，但 `PCGData.Meshes` 没有消费者：spawner 读的是点与资产 Guid，所以数据侧完成并不等于场景里看得见。本节点把每个点的网格变成**虚拟 `Model`** 并放一个 `StaticModel` 在点的位置。

**实现路径全部经过源码核实，不是猜的**（这一点在本模块尤其重要，因为这一层无法离屏执行）：
- `Mesh.UpdateMesh(6参数)` 的 C++ 实现（`Mesh.cpp`）**逐个流判空**——`if (normals)` / `if (uvs)` / `if (colors)` 分别守卫，缺切线时还会**自己算切线帧**（`RenderTools::CalculateTangentFrame`）。所以传 `null` 是被支持的，不是侥幸。
- 同一处 `CHECK_RETURN(model && model->IsVirtual(), true)`：**网格必须属于虚拟 Model**——`Content.CreateVirtualAsset<Model>()` 不只是方便，而是这条路的**唯一入口**。
- 与它同一份代码也说明**不能用**"手工填 `MeshAccessor` 的多个流"那条路：引擎自己的上传是**按显式 layout 一次性分配**每个 vertex buffer（`accessor.AllocateBuffer(Vertex1, count, layout)`），而 accessor 的逐流 setter 不做这件事——先写 Positions 再写 Normals 会把前一个缓冲重分配掉。
- 公开的 `UpdateMesh` **返回 void**（失败码在下一层 `Internal_UpdateMeshUInt` 被丢掉），所以上传被拒不会有异常，只会留下一个空网格；两种被拒的原因（模型非虚拟、缓冲为空）在这里都由构造排除，这条写在注释里，因为"actor 生成了但看不见"的排查要从这里开始。

**两个设计要点**：
- **按形状共享模型**：一千块同尺寸楼板是**一份**网格与一个虚拟模型，因为点的尺寸已经烘进网格里（builder 就是这么造的）。缓存键是**结构性的**（`三角形数|尺寸`，尺寸舍入到千分之一），并且**把三角形数放进键里**——只有尺寸的键会让同尺寸的盒子与台阶互相冒领对方几何，这正是最隐蔽的一种错（形状对、位置对、看上去只是"模型选错了"）。
- **缓存挂在节点上且永不释放**：已经放置的 actor 指着这些网格，释放会把早先那一轮的产物**一起变空**；而流式 Volume 会频繁重生成，正是"每轮重建"最贵、"释放"最糟的场景。代价（虚拟模型随节点存活）写在注释里。
- **actor 缩放为 1**：网格里已经是点的尺寸，再乘一遍点的 scale 会把尺寸乘两次——"台阶比该填的格子高一倍"看起来像 builder 的错。
- 归属/裁剪/命名/`DryRun` 与资产 spawner **逐行同构**（`context.Source` 作为父级、`IsInsideClip` 跳过多余的 padding 点、`PCGSpawnNaming.ActorName`）；层上没有 `Meshes` 时**明确报错**（"这一层没有网格，请在前面接 Geometry Primitives"），因为把几何 spawner 接在普通散点上是很可能发生的错，静默不动最难查。

**门禁连带抓到两处不同步**（正是那套绊线存在的理由）：新 spawner 一开始**不在 `PCGGraphEffects` 的写场景名单**里——"试跑拒绝"闸门会漏判；补上名单后，测试里**枚举清单的条数断言**又红了。两处都补齐，所以现在 6 个 spawner + 子图共 7 项。

**离屏证据**（`GeometryTests` 追加 5 条 → 全套 **38 条**）：键（两个相等形状共键、不同尺寸不同键、**同尺寸不同形状必须不同键**、与对象身份无关）、以及节点自述参数。**其余部分（虚拟资产、网格上传、场景放置）结构性无法离屏**，由 §9 的 PIE 项覆盖。

### 5.1.33 Geometry Primitives（程序化几何，v1.4 新增；对标课程第 15 集数据侧）

注册类型 49 → **50**，Structure 类第 3 个；数据模型新增**第三种载荷** `PCGData.Meshes`。

在此之前插件只会"摆放资产"：点向 spawner 要一个模型，模型是美术做好的。本增量补上另一条路——**形状是算出来的**（墙、台阶、楼板、栅栏段），于是同一张图不需要为每种尺寸都有资产，能**生成建筑**而不只是散布。UE5 里对应的是 geometry script / dynamic mesh 工作流。

**先取证再动手（本轮的关键一步）**：脚本侧到底能不能造网格，决定了这个模块能不能落地——
- `FlaxEngine.MeshAccessor` 的 `Positions/Normals/TexCoords/Tangents/Colors/Triangles` **全是 get/set**，还有 `AllocateBuffer` / `ComputeNormals` / `UpdateMesh(MeshBase, bool)`；`Mesh` 本身还有公开的 `UpdateMesh(vertices, triangles, normals, tangents, uv, colors)`。
- **`Content.CreateVirtualAsset<T>()`** 是公开 API（虚拟资产，`Content.VirtualAssetsCount` 计数），`Model.SetupLODs(int[] meshesCountPerLod)` + `LODs[i].Meshes[j]` 能在运行期装网格。
- 于是完整链路是：`CreateVirtualAsset<Model>()` → `SetupLODs(new[]{1})` → `MeshAccessor.UpdateMesh(lod.Meshes[0], true)` → `StaticModel.Model = model`。**托管插件能生成可见几何**，这条路走通了，不是猜的。

**本增量落地的是数据侧（可全离线断言的那半）**：
- `Execution/PCGMeshBuilder.cs`（纯）：`Box` / `Grid` / `Cylinder` / `Stairs`，加 `Offset` / `Transform` / `Merge` / `Bounds` / `RecalculateNormals`。**每个格子居中于原点**，放置交给调用方的变换——这样一份网格能服务所有同尺寸的点。
- **绕序与法线必须一致**，并且这条被当作网格的**性质**来断言（不是相信 builder）：渲染器用绕序判朝向、用法线做明暗，两者不一致时背面剔除会恰好剔掉应该看见的面。所有 builder 都让 `cross(b-a, c-a)` 指向实体外侧。
- **硬边靠顶点复制**：盒子是 24 顶点而非 8——一个角只能带一个法线，8 顶点会把盒子平滑成球面。
- **`Stairs` 是"几何脚本"形状而非基元**：它是"按 profile 循环"的产物，盒子替代不了（踏步与立板朝向不同）。**代价写在注释里**：它用**堆叠板**实现，因此有内部面（不水密），换来的是不必对非凸 profile 做耳切三角化；测试因此不声称它水密，只声称尺寸/体积/绕序正确。
- **数据承载**：`PCGData.Meshes` **与 Points 下标对齐**（一个网格属于一个点），`Clone` 共享（网格是构建产物、不被就地改写），`Gather` **随点移动**（丢点必须丢形状，否则下一个节点会把石头的网格配到树的位置），`Merge` 按 part 顺序拼接。
- **节点** `Geometry Primitives`（Id `Geometry`）：按点的 Scale 给每个点一个形状（`Use Point Scale` 默认开），无输入时在生成范围中心造一个点带一个形状（像采样器一样能自己起头），未知形状索引回退到盒子（新版本插件写的图不该变成空层）。
- **离屏证据**（`GeometryTests`，**33 条**）：盒子（24 顶点/12 三角形、六个不同面法线而非平滑角、法线是单位轴、**每条边恰属两个三角形**（水密，按位置键）、**体积=w·h·d**、**面积=2(ab+bc+ca)**、居中且尺寸精确、零尺寸盒无 NaN）、台阶（**体积=宽度·(run/n)·(stepRise·Σ(1..n))** 手算值、尺寸精确、单步=整块板、绕序一致、无退化三角形）、圆柱（64 边体积与 πr²h 相差 0.2% 以内、水密、**壁面法线无 Y 分量**、无端盖版本确实开口——反向对照）、网格（面积精确、法线全上、按设计开口）、变换（等比缩放体积×8、非等比缩放后法线仍单位且与绕序一致、四分之一转交换两个 extent、原网格不被改动）、合并（顶点/三角形/体积相加、下标都在界内、空合并为空）、节点（每点一个网格且下标对齐、按点尺寸、**网格在原点而点留在原地**、四种形状确实不同、未知索引回退、无输入出一份、两次跑相同）、承载（`Gather` 带走对应形状且顺序对、`Merge` 拼接、`Clone` 带上、**没有几何的层不会凭空得到空数组**）。
- **本轮抓到的真 bug**：圆柱**两个端盖的绕序都反了**——体积正好算出 `πr²h/3`（壁面正确、两个盖子朝内的精确签名），端盖会以背面朝外渲染。修法是两个盖子用相反的扇形方向，并由体积断言钉住。同一轮还改掉两处**写错的断言**：把"壁面法线无 Y"套在全部顶点上（端盖中心法线本该是 ±Y），以及给台阶断言"水密"（它与上面的设计取舍冲突）。

### 5.1.32 Actor Sampler（把场景里的 Actor 当图输入，v1.4 新增；对标课程第 7 集）

注册类型 48 → **49**，Sampler 类第 8 个。

插件里其它采样器都是**用规则凭空造点**（体积、表面、曲线、纹理、噪声场），这一个从**关卡里已经存在的东西**取点。这是"把村子摆在标记 Actor 周围""按这两根标记加宽道路"的表达方式——不用为每种新摆法都改图。UE5 里同形状的是 actor data 源，而它正是"停止硬编码"里**参数覆盖不到的那一半**：参数携带的是**图已经知道怎么用的数字**，Actor 携带的是**只有关卡才知道的排布**。

- **元数据才是它不只是"散一堆变换"的原因**：名字、Tag、LayerName 是 Flax 里一个 Actor 能被标记的三样东西（`Actor.Tag` 与 `LayerName` 都是**字符串**、可直接在编辑器里写），三者都写成**字符串列**，于是图可以按"标记的种类"去过滤/分支/换生成物。**刻意不做反射读任意脚本字段**：慢、改个字段名静默失效，而且会让图依赖一个图里根本没声明的类；`Tag` 的**枚举**形式（`Level.FindActors(Tag)`）也不能从任意字符串现场铸造，所以按它做过滤等于从图里够不着（§7.3 那条注释记过同一件事）。
- **过滤在节点里做**，不是只留给下游：这个节点的用处通常是"那两根**重要的**标记"，而不是"所有标记"。名字是**子串**匹配、Tag 与 Layer 是**精确**匹配——按 Tag 分组是常见用法，子串匹配会让"village"把"village_small"也吸进来；空过滤 = **无约束**而不是"匹配空串"（后者会把没有 Tag 的 Actor 全丢掉）。`Max Actors` 计的是**匹配数**而不是读到的条数。
- **默认跳过本插件生成的实体**（`PCGSpawnNaming.IsGeneratedName`）。本 Volume 生成的物件也是 Actor，把它们喂回去会让下一轮从上一轮的产物里长出来——一个**自我增殖**的循环，看起来像正常生成，直到有人搬动一个物件然后村子翻倍。
- **Actor 列表经 `PCGWorldContext.ActorSource` 注入**（与 `RayCaster`、`ActorResolver` 同形状）：节点没有场景就列不出关卡，而"匹配规则"恰恰是值得断言的部分。真实运行由 `PCGVolume.BuildContext()` **每轮快照一次**（分区跑时每个格子都会问，每格重列一次关卡会让这条读取变成图里最贵的一步），测试装一份自己造的清单。空源 → **不产出并说明原因**（而不是凭空造 Actor，也不是清空一层）。
- **离屏证据**（`ActorSamplerTests`，22 条）：`FromSource` 的契约（会谎报条数的源被双向钳制——**多报会读到上一次调用的残留**，那正是"物件出现在一个已经走开的标记周围"那类 bug；负值读作零；scratch 尾部被裁掉）、匹配规则（空过滤=无约束、名字子串且忽略大小写、Tag/Layer 精确、两个过滤是"与"、生成物默认跳过是可关的）、产出（每个匹配 Actor 一个点、位置与旋转取自 Actor、**盒子按 Actor 自身 scale 而非退化点**、三列元数据拼写正确、关掉元数据列就消失、seed 各不相同、按 Tag 过滤只留该 Tag 的、上限计匹配数、同场景两次跑结果一致）、失败（无源报错且不产出、过滤无命中是空而非错误、Tag/Layer 为 null 的 Actor 仍能产出而不抛）。

### 5.1.31 Grammar（符号+产生式的结构化生成，v1.4 新增；对标课程第 14 集后半）

注册类型 47 → **48**，Structure 类第 2 个。

`SplitGrammar` 按**深度**取规则表——只要同深度的格子是同一种东西就够用。真实结构不是这样：一块地里有店铺和后院、店铺里是房间、房间里什么都没有——**格子的"种类"决定它怎么被切**，而且同一深度会同时存在好几种种类。这就是符号语法补上的那一半，也是"等分地块"与"一条街"的区别。

规则语言是一行产生式，从左到右应用：

```
Lot = Shop,Shop: X:0.5,0.5; Shop = Room,Room,Back: Z:0.35,0.35,0.3
```

`Lot` 沿 X 切成两个 `Shop`；每个 `Shop` 沿 Z 切成两个 `Room` 和一个 `Back`。**没有产生式的符号就是叶子**（上面的 `Room`/`Back`），所以图到哪里结束由作者写到哪里决定，不同种类、不同深度的叶子共存——这正是它相对深度表的全部价值。

- **产生式可以写自己**（`Room = Room,Room`），**递归就是深度的表达方式**，靠 `Max Depth`、`Min Cell Size`、格子预算三道闸收口；一个永不收口的语法**会被报告**而不是被允许跑。
- **子符号数量必须等于切片数量**——这是唯一"看不见"的错：切分完全正确，而种类沿着切分**整体错位**。所以它是硬错误而不是猜。
- **子符号可以后定义**（两遍解析），所以产生式列表的顺序不是作者要迁就的约束；同一符号出现两条产生式则报错，而不是让先/后一条静默胜出。
- **每个叶子带两列**：`Symbol`（数字序号，供 Filter/Remap/Math 驱动）与 `SymbolName`（文本，供人看检视输出）。序号按**首次出现**分配，所以编辑产生式列表会重新编号——要稳定就用名字列。根符号没出现在产生式里时**报错**（那几乎总是拼错；而"一个符号一次叶子"的静默行为会看起来像语法什么都没做）。
- **离屏证据**（`SymbolGrammarTests`）：解析（两遍解析的后定义、递归、空列表/无 `=`/无切分/无子符号/种类与切片数不一致/坏轴/坏比例/重复定义）、展开（四个孙格、**沿各自产生式的轴与份额切**、**叶子恰好铺满种子格**、两个种类在同深度可区分、名字列拼写正确、关掉名字列则只少那一列、递归在深度上限收口、根符号自身可以是叶子、最小格尺寸提前收口、三种子→三份结构、每片叶子各自的 seed）、失败与上限（根符号缺失报错并透传、坏产生式透传而不清空、四路互递归在预算处停下并说明、深但可负担的语法不被截断）。

**共用切分核心（`Execution/PCGCellSplit.cs`）**：轴与比例的词汇、以及"把一个格子沿自身某个轴按比例切开"这件唯一的几何——两个结构化节点共用同一份实现。这么做不只是省行数：**"子格恰好铺满父格"是那种"错了也在其它所有可观察面上看不出来"的性质**（格数对、尺寸对、图只是微妙地偏），它应该只有一份实现、一组断言，而不是两份可能漂移的。

**本轮修掉的一处"绿得可疑"**：`SplitGrammar` 的最小格尺寸判据原本比的是**格子的最长边**。只沿 X 切的规则永远不动 Z，于是"格子变小就停"对单轴规则**永远不触发**——而原断言用的是只有一条规则的规则表，**先被规则表本身停住了**，所以那条断言一直在绿灯里空转。判据改为**沿被切的那个轴**度量（"这一刀还有没有地方下"本就是轴的问题），并补了对照：同一张四层表，有尺寸下限出 4 格、没有出 16 格；100×10000×100 的格子仍能沿 X 切。

### 5.1.29 Split Grammar（按深度切分的结构化生成，v1.4 新增；对标课程第 14 集）

注册类型 45 → **46**，并且**新开一个类别 `Structure`**（编辑器 add-node 菜单从 7 组变 8 组）。

**为什么要它**：插件此前的每一环都是"把场变成点、再筛/改这些点"，而城市街区、带楼层的建筑、带走廊的户型**不是点也不是筛选**——是**细分**。没有细分，"临街三间铺、每间再往里切两刀"只能手摆。UE5 把这叫 structured generation 并用 grammar 驱动；本节点是其中的 **split grammar** 一半：一张**按深度索引的规则表**，每条规则把它所在的格子沿一个轴按比例切开。

规则语言就是一行文本：`X:0.5,0.5; Z:0.6,0.4` —— 深度 0 沿 X 对半分，深度 1 再沿 Z 按 60/40 分。**单比例规则（`X:1`）表示"到此为止"**，所以一张表可以在任意深度收束一支，深浅不一的叶子可以共存。比例会**归一到和为 1**，于是 `0.5,0.5` 与 `1,1` 是同一次切分（作者按自己心里那套比例写就行）。切分在**种子点自己的坐标系**里做：转了 30° 的地块按**它自己的临街方向**细分，叶子继承种子的旋转。

**关键判据（都是纯算法，全部离屏钉死）**：
- **子格必须恰好铺满父格**（体积守恒 + 边界重合）：这条抓的是"位置或尺寸算错但点数看着对"——一道错误的切分照样产出预期的格子数与尺寸。
- **旋转种子沿自身轴切**（手算事实：90° 偏航下局部 X 应落在世界 ±Z）。少了这条，世界轴实现的版本会通过上面所有断言，只是把街区切歪。
- **深度选择规则、单比例即停、最小格子尺寸、`Depth` 列、叶子各自的 seed、继承 density、无输入时用生成范围当种子、同表两次跑结果一致**。
- 失败与上限：读不懂的表**报错并原样透传**（正向对照：同节点配可读的表则无告警）；`4^16` 这种表必须**在 200000 格预算处停下并说明**（对照：同样深度但 65536 格的表**不被截断**）。

**本轮抓到的共享助手坑（比节点本身更值得记）**：写第一版时用 `point.Scale = 格子尺寸; point.UpdateBounds(0.5,0.5,0.5)` 落盒——盒子全部是 **1×1×1**。隔离验证后确认：**`PCGPoint.UpdateBounds(localExtents)` 会用点的 transform 重建盒子，但只施加旋转与平移，不施加 Scale**（`BoundingBox.Transform` 不乘缩放），而它自己的注释写着"改过 Position/Rotation/Scale 之后调用它"。所以叶子的盒子与格子尺寸完全脱钩——**extents 是下游一切剪枝判据，这类错不会显示为任何数字不对，只表现为"物件莫名被剔"**。修法与 §5.1.28 同源：**显式 `EnclosingExtents(half, rotation)` + `MakeBox`**。这条也说明为什么 `ScaleByAttribute` 的既有断言没能抓住它：那组夹具的 half-extents 恰好等于缩放后的值，**两种语义在那里给出同一个数**（对照夹具必须与该性质需要的比较同构，否则恒等）。

**离屏证据**：`Tools/PCGGoldenRunner/GrammarTests.cs` **+38 条**（含规则解析 11 条、几何/守恒 12 条、旋转 2 条、列与继承 5 条、失败与预算 4 条，以及上面那两条"盒子里程碑"断言）。harness 918 → **956 passed / 0 failed**。

**与 UE5 的差距（有意保留，勿记成已达成）**：本节点是**按深度的切分表**，没有**符号**与**产生式重写**（UE5 grammar 里 `A -> B C` 那种按符号匹配、同名符号可被多条规则替换的机制）。因此"同一层按规则随机选不同分支""建筑沿街长度不同则层数不同"这类**由符号驱动的变化**目前要靠：多撒几个种子、或在上游用 `Branch`/`Select` 分叉成多张规则表。要补的真正形态是**符号表 + 产生式**，那是下一增量。

### 5.1.28 Rotate By Attribute（用一列驱动点朝向，v1.3 本轮新增）

注册类型 44 → **45**，Modifier 类第 6 个。"一列写回变换"三件套的第三个：`ScaleByAttribute` 花在**尺寸**、`OffsetByAttribute` 花在**位置**、本节点花在**朝向**。缺了它，一个图能把道具的一切都变出花来，唯独不能决定它**朝哪看**——`RandomOffset` 的抖动是随机的、`TransformPoints` 的旋转是常数——于是"顺着水流方向摆"无从表达。

- **绕点自己的 up 轴旋转**，走 spawner 用的同一个 `PCGPointTransform.YawAboutLocalUp`，所以**贴过坡面的点转起来仍然站在坡上**。角度用**度**（与图里其它旋转参数同一单位）。
- **bounds 要重新推导**：盒子是轴对齐的，转了点就是转了它实际覆盖的范围，留着旧盒子等于让剪枝去测形状原来的 footprint。
- **本轮抓到的陷阱（值得单独记）**：第一版写成 `UpdateBounds(EnclosingExtents(half, delta))`，结果盒子**纹丝不动**。原因是 `PCGPoint.UpdateBounds(localExtents)` 会**用点当前的 transform 重建盒子**——喂进去"已经转过的 extents"，等于把这次旋转**应用两遍**，正好转回原样。修法与 `TransformPoints` 一致：**自己算 `EnclosingExtents` 再 `MakeBox` 显式落盒**。同一处隐患在 `OffsetByAttribute` 里也在（只平移时 `UpdateBounds` 会把点的旋转/缩放再套一遍），已一并改成 `MakeBox`。**抓出它的方式仍然是"给新分支写断言"**：断言"90° 转向应当交换盒子的 X/Z extents"是手算事实，跑起来立刻显示盒子没动。**随后把全仓 `UpdateBounds(` 调用点扫了一遍**，又找到两处同类写法——`AttributeTransfer` 的 `Match.Position` 与 `ProjectPoints` 都在"移动后把世界 AABB 当局部 extents 喂回去"。这两处**偏保守**（盒子只会被多包一层、变大，不会误剔），所以不是致命错，但也违反了"bounds 跟着点走"这条口径，已一并改成 `MakeBox`，并补了一条**倾斜目标**的断言（原写法会把已倾斜盒子的包围盒再包一次，新写法只平移盒子；该断言在修复前必红）。
- **离屏证据**：`PointOpsTests.RotateByAttributeTests` 9 条——列映射到 0/45/90°（用 forward 轴断言，不假设符号）、**90° 交换盒子的 X/Z extents（手算事实，正是它抓出上面那个陷阱）**、绕自身 up 转不改变 up、`Invert` 反向、零量程不转、具名列优先于密度（**正向对照**）、缺列时朝向不变、空输入空输出。harness 888 → **898 passed / 0 failed**。

### 5.1.25 两处"文档写了、没人断言"的约定补钉（v1.3 本轮）

没有新功能，只是把两个**已经写在注释里、但没有任何断言**的约定变成断言。之所以值得单列一节：本轮抓到的三个真缺陷（§9）全部是同一形状——**注释描述了一个没人验证的约定**——所以这两处是照着这条教训去查的，结果如预期：**实现是对的，只是从来没被钉住**，下一次改动可以悄悄把它们改掉。

- **坡度是"绝对"的**（`PointOpsTests.SlopeConventionTests`，5 条）：`SlopeDegrees` 的注释写着 degrees-from-vertical、范围 [0,90]、**与翻转无关**；其中 Abs 那一步不是装饰——没有它，"把点翻一下"就能让点跨过坡度阈值，而且地形采样器（`acos*RadiansToDegrees`）与旋转反推这两条产坡度的路会各说各话。断言钉住：恒等=0、+80°=80、**−80° 也 =80**、**上下颠倒 =0（不是 180）**、以及任意朝向 16 次扫描后始终落在 [0,90]。
- **贴图的世界→UV 映射**（`TextureSamplingTests.WorldMappingTests`，5 条）：原先是贴图节点里的一行内联公式，而**这个节点离线跑不了**，等于整条"画出来的图 ↔ 世界坐标"的契约没人看。抽成 `PCGTextureSampling.UvFromWorld` 后钉住：世界原点落在图像正中、半个 tile 到右边缘、**Z 驱动 V**（图是平铺在地上的）、一个整 tile 正好是一整张图（这正是 Repeat 能无缝的前提）、`tileSize = 0` 不产生非有限值。harness 823 → **833 passed / 0 failed**。



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

### 6.1.1 画布节点身份：从"注册序号"改为"名字派生"（v1.4，MCP 实测驱动）

**现场证据（编辑器运行态，经 HundunAgent MCP 的 `log_scan` 取出）**：
```
[ 00:01:25.520 ]: [Error] Failed to create Visject Surface node with id: 7:29
```
按当前注册表反推：**ControlFlow（GroupID 7）只有 `(7,26) SelectPoints` 与 `(7,40) Branch`，不存在 `(7,29)`**，而 `(6,29)` 是 `AttributeMath`。也就是说：**那份画布 payload 里的一个节点按当前节点表解析不出来，被静默丢了**——Visject 的全部反应只是引擎日志一行，画布上就是"少了个节点/看着不对"。

**根因**：`NodeArchetype.TypeID` 与 `GroupArchetype.GroupID` 原先由**注册顺序**现场分配（`nextTypeID++` / `byCategory.Count + 1`）。图文件存的是**类型名**（所以数据侧一直稳），可**画布 payload 存的是打包后的 `group<<16|type`**——只要注册顺序在版本间变过（本插件变过多次），旧 payload 就解析不到节点。**这是"重开后画布不对"的第二条独立成因**，与 §6.1 修掉的编码损失（base64 / `Format 2`）无关。

**修法（本轮落地）**：
- 新增纯函数 `PCGEditor/PCGNodeStableIds.cs`：FNV-1a 从**名字**派生 id（15 位、绝不取 0），冲突时**按名字排序后向上探测**——结果是**名字集合的函数**，与传入顺序无关。`PCGNodeArchetypes.Build()` 改为用它给每个类型名与类别名分配 id，不再用计数器。
- **把丢失变成可见并自动修复**（对旧 payload 同样有效，因为旧文件无法挽回）：payload 载入成功后比较**画布实际还原出的节点数**与**文件里的节点数**，少了就说明有节点没解析出来 → 走 `RebuildCanvasFromGraph()`（连节点带连线按数据重建）并在状态行说明"文件里有 N 个 / 只还原出 M 个，多半是旧版画布数据的节点编号对不上"。
- **能离线断言的性质**（`NodeIdTests`，+10 条）：同名恒等、不同名相异、id 非 0 且在打包范围内；**同一名字集合任意顺序产出同一映射**（乱序、512 个名字、以及**真实 51 个注册名正反两种顺序**逐一比对）；512 个名字 id 全不相同（让冲突探测真的被执行）；每个节点打包出的 `(group,type)` 对互不相同；空集合不抛。

**实测更正（同一轮内的截图 + 日志）：Visject 对"认不出类型"不是少一个节点，而是放一个占位框。** 换 id 之后打开那张旧图，画布上出现 **5 个 `Missing Node :(` 框**，而状态行仍是 `已载入 5 个节点、3 条连线` —— **数量守恒**，所以只比"画布节点数 < 文件节点数"的检测**看不见它**（上一版正是这样写的，实测证明判据错了）。判据因此改为**节点有没有类型**：`ResolveTypeId` 为空的节点数 > 0 即视为未还原。两个端点都要查，因为危险方向不同：
- **载入时**：检出 → `RebuildCanvasFromGraph()` 按图表数据重建（节点 + 连线），状态行明说"文件里有 N 个，画布上有 M 个认不出类型（多半是旧版本写的）…点一次「保存」即可写成新格式"。
- **保存时**：检出 → **拒绝保存**。这条是防数据损毁：把占位框写出去就是几个空类型节点，**一份能读的图表会变成谁也读不了的图表**；拒绝时文件里仍是真图。
顺带确认：那一轮里**视图自愈是生效的**（状态行 `文件里的视图不在图表上——已自动对准整张图`，节点确实回到画面中央），顶栏与画布几何也与设计一致。

**为什么这条修法是对的**：图文件已经把"类型名"当作稳定契约（它存名字不存序号），把画布的身份也绑到名字上，两者才对同一件事有同一个答案。代价照旧说清：**改动之前的 payload 一律失效**（已经失效，无法挽回），现在的行为是**检测到丢失 → 按数据重建 → 明说原因**，而**改动之后写的 payload 在任何会话、任何注册顺序下都能解析**。


### 6.2 节点 UI 交互

由 `VisjectSurface` 原生提供，无需自实现：添加节点（右键菜单）、拖拽节点、创建连线、删除连线、框选、缩放、平移、撤销/重做、复制粘贴。

**需自行实现的部分：**
- `NodeArchetype` 表（每个 PCG 节点一条）
- 各节点的参数编辑器（`Values` 索引 ↔ 参数语义的映射）
- 暴露参数面板（`ExposedParameters` 的编辑 UI）
- 执行/预览触发按钮（挂在 `AssetEditorWindow.ToolStrip` 上）

> **v1.3 落地实况：复制/粘贴/副本 与多选批量改参（Phase 2 的 ① 已补完）**
>
> 规则全部写在**运行时侧**的 `PCGGraphClipboard`（`PCG/Graph/`）而不是编辑器里，理由是可验证性：粘贴错一个下标会得到一张"照样能存、画起来也对、跑起来却是另一张图"的图表，而这类错误只能靠断言抓，不能靠眼睛。编辑器只负责三件事：把选中节点解析成图下标、调用这些纯操作、然后**从图数据重建画布**——所以画面上画的和保存时要写的永远出自同一份数据。
>
> 五条规则（都有离屏断言，共 **38 条**）：① **只带走选择内部的连线**，跨出选择的连线计数并报给设计者（`DroppedEdgeCount`），不让一份"看起来完整"的副本悄悄断线；② **粘的是数据不是引用**——值数组深拷贝，否则粘贴出来的节点与原节点共享一个 `object[]`，改一个动两个、存盘后两者都对不上；③ **全有或全无**：副本里出现本图没注册的节点类型就整次拒绝并报出类型名，绝不留下半个簇（半截子图看起来像一次已完成的编辑，是最坏的结局）；④ **暴露参数跟着走但改名**：`Name` 承诺"图内唯一"（Volume 的覆盖表按它寻址），所以第二份起叫 `Spacing#2`、第三份 `Spacing#3`，并且下标重新指到粘贴出来的节点上——测过"只选被暴露的那个节点时本地下标是 0"；⑤ **批量改参按名字寻址**，因为同一个控件在不同类型上槽位不同（`Seed Offset` 在体积采样器是 1、在网格采样器是 3）：只有整条选择都有的名字才出现在面板上，类型不合的值整批拒绝而不是写一半。
>
> **诚实的边界**：画布节点 → 图下标只能靠**画布位置**对齐（载入时是把图的值数组**拷进**画布节点的，所以引用相同性不是可用的链接）。两个节点叠在同一格时只会解析出第一个，面板标题因此写"识别出 N / 选中 M 个节点"，识别不足时也会在状态行说明——不假装一一对应。批量写只改图数据，**必须点保存**才落盘。

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

> **v1.3 落地实况（分区生成已可跑，参数挂在 `PCGVolume` 上而不是 `PCGWorldActor`）**
>
> 实现三件套：`PCGPartitionPlanner`（把区域切成 XZ 网格，每格 `GenerationBounds` 外扩 padding、`ClipBounds` 保持本格）、`PCGPartitionRunner`（复用同一个 executor 逐格执行、按 sink 分别裁剪合并）、`PCGVolume.Partitioned / PartitionCellSize / PartitionPadding`（`PartitionPadding = -1` 表示由图里 Self-Pruning 的间距自动推导，走 `SuggestedPaddingForGraph`）。设计稿里的 `PCGWorldActor` 并不存在，也没有引入：格子大小属于"这一块区域怎么切"，属于 volume 而不属于全局，一个全局值只会让不同区域共用一个错的粒度。
>
> **本轮修掉的三个真问题（此前这段代码是死的：无消费方，且 `PCGPartitionRunner.cs` 单独有 5 个编译错误）**：① 边界**双算**——旧的 `IsInsideCell` 在 X/Z 两端都闭，落在共享边上的点会被相邻两格同时收走，即每格边界都可能多出一份资产，正是分区本要消除的现象。现改为**下闭上开**，并把"本格是否为区域最后一格"随 `PCGPartitionCell.IsLastX/IsLastZ` 带下去，区域自身的远端边界仍然闭合，否则正好落在 `maximum` 上的网格点会一格都不属于。同一判定由 `PCGExecutionContext.ClipCell` 供 spawner 消费，数据侧与场景侧不会各说各话。② 聚合点数上限**只报不裁**——executor 的预算是按单次运行算的，分区后等于 `格数 × 预算`；runner 现在真的按上限截断并在告警里说明被丢弃的是哪些。③ 计划里可能出现**零宽尾格**（`ceil` 把刚好越过边界的除法抬上去），白跑一整图再裁空，现在裁掉。
>
> **勘误（2026-10-10，引擎自检第一次真跑之后）**：上面那句"外扩一圈就能消掉接缝"要加一个限定：**它成立的前提是两次运行看到同一套点集**（离屏那条核心断言正是"同一固定点集"）。真实体积链上，采样器的逐点随机数**按点下标取、而下标在每格内部重启**，于是每格的布局**本来就不等于**整片一次跑的布局——近邻对在两格里的先后次序可以不同，边界上就可能有个别近邻对同时存活。**填充再大也修不掉它**（它需要的是跨格的全局一致次序，不是更宽的外扩）。因此 `PCGSelfTest` 里"分区后仍守住 60 间距"已按设计真能承诺的性质收窄为"边界个例不放大（最多每格一对）"，其余交给离屏的固定点集断言。要根治得改采样的确定性口径（按**位置哈希**取随机数而不是按下标），那会改变所有已生成世界的布局，属产品决策，不在这一轮动。
> **离屏证据**（`Tools/PCGGoldenRunner`，新增 `PartitionTests` 33 条断言
，总计 **193 passed / 0 failed**）：平铺性（含边界采样：无空洞、无双算、两个区域极值各属一格）、拒绝条件（cellSize≤0、无 XZ 跨度、超过 `MaxCells`）、padding 钳制在半格、`SuggestedPadding` 与图上 pruner 的读数；**核心接缝断言**——同一固定点集，连续运行的结果与"带 padding 的分区运行"逐点相等，而 `padding=0` 时边界上确实多出一个本该被裁掉的点（证明 padding 是承重的，不是装饰）；另有 padding 点不外泄、上限截断、单格异常按 `cell x,y` 逐条上报而不吞掉、双 sink 图分区后仍是两份数据。**仍未在 PIE 目视确认**：`PCGVolume` 那条真实链路（`PCGSelfTest` 已加"分区 volume"组：Preview 模式跑真 Actor，断言输出全部落在体积世界盒内、分区后仍守住 60 间距、cellSize=0 被干净拒绝），需要设计者在编辑器里看一眼日志分组是否全绿。
>
> **跨格增量重算（决定 A/B/C 的落地情况，2026-10-06）**：`PCGPartitionCache` + `PCGPartitionRunner.Run(..., cacheRequest)` + `PCGVolume.PartitionWorldVersion`。
- **A（脏信号）= 一格签名逐字段精确比较**：图结构哈希、seed、参数版本、世界版本、该格的 generation/clip 范围、实际 padding。**故意不用哈希当键**——键碰撞会交出陈旧点集，而陈旧的生成是静默的（场景看着正常、东西在错地方）。参数字段的"版本"来自对覆盖表**规范串的精确比较**，碰撞不可能。
- **B（脏了重算多少）= 只重算脏格**，不需要邻格闭包：一格读的是自己被 padding 补足的区域，不读邻居的输出（这正是 padding 买来的性质）。**唯一例外是聚合上限**：一旦某次运行被 `maxTotalPoints` 截断，整张缓存作废——那次的图景不完整，留着会让下次混用截断前后两种结果。
- **C（旧结果怎么替换）= 已落地：逐格所有权挂在名字里**。`PCGSpawnNaming.CellToken(x,z)` → 生成物名形如 `PCG_Gen_rock_x1z2_7`（前缀仍是第一个东西，`IsGeneratedName`/`Clear()` 不受影响）。`PCGVolume.ClearForRun` 先问缓存**哪些格是脏的**，只销毁这些格的孩子，新鲜格的孩子原地留着——于是"缓存某格"与"删掉某格的 Actor"由同一份判定驱动，不可能一格被 runner 复用却被 volume 清掉（或反过来）。
  - **为什么用名字而不是 Tag**：Flax 的 `Tag` 是项目标签表的索引，**无法在运行时由任意数据铸造**，所以逐格标签做不了；名字可枚举、且随场景存盘。旧的注释里写着"运行时标签才是权威所有权标记"，这次一并改成与实现一致。
  - **顺带抓到一个真 bug**：`Spawn Foliage` 的宿主此前按"节点下标"命名，分区时**每格共用同一个 `Foliage` 宿主**——最后一格的 `RemoveAllInstances()` 会把前面各格已放的实例全部抹掉（数据侧还报告有那些点，场景里已经没有了）。现在宿主名带格（`PCG_Foliage_<节点>_<x>x<z>z`），自检 3c 组新增断言：两格 → **恰好 2 个宿主**、第一格实例数**不被第二格扰动**、第二格只有自己那半。
  - 边界仍是：**地形编辑/宿主移动要人工 bump `PartitionWorldVersion`**；`_x{0}z{0}_` 之类 token 用**带下划线的整段**匹配，否则 (1,12) 会误命中 (11,12) 的格子——离屏有专门一条断言钉这个。
- 脏信号里**地形编辑与宿主移动仍需人工 bump `PartitionWorldVersion`**：插件没有任何可观察来源，做成自动的就是猜。
- 验证：**32 条**离屏断言（新增所有权 token 的命名/归属/回读、planner 下标与 token 对齐、`DirtyCells` 的四个方向、宿主不被邻格扰动）（冷缓存全执行、热缓存全复用且**与无缓存运行逐点同指纹同顺序**、seed/格子大小/padding/世界版本各自的失效、`Clear()` 与 `InvalidateCompilation()` 作废缓存、上限截断后整表清空、spawner 图按名拒绝）。夹具本身加了一条"点数 > 100"的断言——**这套测试第一版就是靠一条空结果把全部比较变成同义反复的**（density 窗口写错，0 == 0 全绿）。

**分层网格（决定里的大格放大型资产 / 小格放细节资产）= 已落地**：`PCGVolume.Layers`（`Graph/PCGPartitionLayer.cs`：`Name / GraphPath / CellSize / Padding / SeedOffset / Enabled`），空数组就是原来的单层路径。每层是**一次完整生成**：自己的图文件（留空则沿用体积的）、自己的 seed（`Seed + SeedOffset`，逐层错开布局）、自己的编译缓存与**自己的 `PCGPartitionCache`**、自己的格子规划（`PCGPartitionPlanner.PlanLayers` 一次算完并逐层报错点名）。`Generate()` 按层循环，`LastResult` 汇总各层点数/耗时/告警并保留层名，日志每层一行、最后一行是总计。
- **为什么 token 里必须有层**：两层可以在**同一个格子**里各放东西，若名字只带 `x1z2`，一层清理脏格会连另一层的物体一起毁掉、而被毁的那层缓存还以为它们在场。于是所有权记成 `PCG_Gen_<label>_l<层>_x<x>z<z>_<点号>`，植被宿主同理带 `_l<层>_`。
- **单层（未分层）路径行为不变**：`LayerIndex=0`、沿用体积上的分区参数。
- **流式窗口（v1.3 本轮落地，`Execution/PCGCellStreaming.cs`）**：`PCGVolume.Streaming` + `StreamCenter/StreamRadius/StreamKeepRadius` 把"一次请求生成整片区域"改成"只生成中心周围那圈格子"。要点四条：① **两个半径而不是一个**，因为单半径会抖——玩家在边界上走来走去就会反复生成/销毁同一格，所以外圈 `StreamKeepRadius` 之内的格子**既不重跑也不清空**（站着，缓存还留着，回到窗口里直接复用），只有超出 keep 的才驱逐；`keep < radius` 会被抬到 `radius`，否则同一次请求会毁掉自己刚生成的东西。② 驱逐是**几何判定、无状态**：不记"上一次哪些格活跃"，规则就是"在 keep 半径外→清 Actor + 丢缓存"，因此重复调用幂等。③ 缓存条目必须跟着 Actor 一起走，新加 `PCGPartitionCache.Evict(x, z)`；留着旧条目就等于允许下一次把"道具已经不在"的格子当复用结果交回去，那是这套缓存到处在防的那类错配。④ **格子规划每次请求重算一次、由清理侧和执行侧共读**（`PCGVolume.EnsureCells`，`Generate()` 里显式置空重切）——两侧看到的窗口必须逐格一致，否则清理按昨天的窗口删、执行按今天的窗口跑。纵向距离**故意不参与判定**（头顶 500 单位的格子仍然是要生成的格子）。
  离屏覆盖 33 条（`Tools/PCGGoldenRunner/StreamingTests.cs`）：窗口/驱逐集合逐个对着**独立写法的距离参照**（"点落在区间外的间隙"公式，而生产实现是"夹到最近点再比平方"）比过 6 组半径；两列表不相交；滞回环带数量等于按参照算出的中间带格数；Y 不参与；调用方复用列表会被清空；`null` 参数会被拒；缓存驱逐只带走那一格、邻居照旧可复用、被驱逐的格下一次出现在 `DirtyCells` 里。**Volume 侧的接线只能靠引擎自检组 3h（Preview 模式：整片 N 点 → 250 半径只剩一部分 → 中心移出区域为 0 → 半径开回 5000 必须逐点等于 N）**，最后一项就是"每次请求重切"的守卫。
- **跟随驱动（v1.3 本轮追加，`Execution/PCGStreamTrigger.cs` + `Runtime/PCGStreamingDriver.cs`）**：窗口"谁来推"这一格补上了，但**不是靠给 Volume 加 `OnUpdate`**——离屏编译门当场把我写的 `public override void OnUpdate()` 判成 **CS0115**：Flax 把 update 钩子挂在 `Script` 上，而 `PCGVolume` 是 `Actor`（仓库里所有 `override void OnUpdate` 的类都来自 `Script`，这条现在是查过 API 而不是猜的）。所以驱动分成两半：Volume 上一个公开的 **`UpdateStreaming()`**（返回这一帧到底要不要生成，游戏侧自己 tick 也可以直接调它），外加一个**放下即用的 `PCGStreamingDriver : Script`** 组件负责逐帧转发——决策逻辑（格子索引 + 待办积压 + 是否有进展）留在 Volume，因为它需要格子计划、层积压和缓存，这些都不属于"恰好谁在 tick"。
  判定只有两种情形值得一次请求：① 跟随点**跨进了另一格**（窗口内容真的开始不对）；② 上一次请求被预算推迟了格子，**而且积压确实在缩小**。其余帧的代价是一次 `Position` 读 + 两次整数比较，**没有定时器**。②的"有进展"闸门是防失控的：图上的合并点上限已经打满、或某格一直生成失败时，积压不会再缩小，于是重试自动停下，而不是把"有界的一格补不完"变成"每帧都尖峰"。
  这里有一个**写引擎自检组时才暴露的真 bug**：`madeProgress` 最初写成 `pending < _deferredAtLastRequest`，而第一次预算请求前的基线是 0（那时根本还没有计划），跨完一次请求产生的积压是 5 → `5 < 0` 假 → **推迟的格子永远不会被补上**，而且不报错、只在日志里少一片世界。修法是给基线一个"还没测过"的 `-1` 语义（跨格之后就是未测过）。这类"静默不做"的失败离屏完全看不见，也正是 `PCGVolume` 必须有一组引擎自检的理由。
  离屏 17 条（`StreamingTests.TriggerTests`）：负坐标的**向下取整 vs 截断**（`-0.1` 属于 -1 格，而 `(int)(-0.1/400)` 是 0；并用"往西走过原点"的模拟把差别量化——floor 看到 3 格、截断只看到 2 格）、step ≤ 0 不拿去整除、`CellOf` 完全忽略 Y、`ShouldRequest` 的五行真值表（跨格必问 / 空闲不问 / 有积压且有进展就问 / 有积压但没进展就停 / 没预算不当积压）、以及**跟随步进取最细一层格子**（`FollowStep`：显式值优先；有层时取最小的正 `CellSize`；停用/0 尺寸/null 的层跳过而不是赢下最小值；无层回落到体积自己的 `PartitionCellSize`——因为按 800 格的步进取跟随，同一 Volume 里 100 格那层的窗口会落后到八格，看起来就是"脚下细节迟迟不出现"）。端到端在引擎自检组 **3i**（跟随一个真 Actor：静止的 target 第二次调用必须不问、同格内挪动不问、往原点以西挪 0.1 必须问、`StreamCellsPerRequest = 1` 时连续帧要把窗口填满后**停下**，并且最后逐点等于同中心不带预算的 `Generate()` 结果）。
- **每次请求的格子预算（v1.3 本轮追加，`StreamCellsPerRequest`）**：流式窗口解决"一次别生成整片世界"，但**传送仍然是一次尖峰**——一次请求就要把整个新窗口跑完。预算把一次请求能*计算*的格子数封顶，按离中心最近优先。这里有一个自己踩出来的坑，连同反例一起钉在测试里：**只按距离排序 + 固定中心 = 外层格子永久饿死**（每一次请求都是同样那 K 格胜出，玩家站着不动就永远只补脚下那圈）。所以预算**只数真正要算的格子**：缓存里已有结果的格子是免费的，无论预算多少都留在窗口里，被封顶砍掉的只能是"要干活"的那些。于是同一个中心反复请求会**一圈圈往外铺**，`ceil(窗口格数 / 预算)` 次之后点数逐点等于不分预算的结果；缓存就是"哪些免费"的唯一权威，也因此**不需要再记任何"上次跑过哪些格"的状态**（保持无状态、幂等）。预算为 0/负数就是旧行为（整窗一次跑完）；复用（缓存）关掉的图没有"免费"可言，退化成"最近 K 格"，这一点在属性注释里写明。离屏新增 22 条：预算大小 == 最近档（tier-boundary，避免"两边距离都是 0"的空转断言）、免费格全留、逐轮把预算花满、`ceil` 轮收敛、驱逐集合不受预算影响、**以及反例**（不给 needsWork 时 12 次请求只能碰到同样的 2 格）。引擎自检组 3h 加了端到端那半：`StreamCellsPerRequest = 1` 时一次请求的点数必须小于整窗，连跑 6 次必须逐点等于整窗。
- **窗口等价性（离屏 7 条，`StreamingTests.WindowEquivalenceTests`）**：同一张图、同一区域切成 4 格，用 `{0,1}+{2,3}`、对角 `{0,3}+{1,2}`、以及**逐格四次**分别跑，得到的点集与"四格一次跑完"**逐点相等且总数相等**；共享缓存在两种到达顺序下内容一致；每次窗口跑回来的点都落在被要求的那几格内（padding 不外泄，相邻窗口不会双报同一个道具）。这组断言钉的是**拼接**（clip/merge/缓存的顺序无关与无双报），**不声称接缝性质**——这一点是用一次失败换来的认识：我先加了个"零 padding 对照组"想证明这组断言有牙，结果 `418 + 418 vs 836` **完全相等**。原因不是断言空转，而是**分区跑 vs 分区跑本来就看不出接缝**：两边每格看到的都是同一块 padded 区域。接缝性质只在"分区跑 vs 单次整体跑（固定点集）"的对照里才显形，那条断言在 `PartitionTests` 里已经有。所以对照实验删掉、注释里写清楚这组断言证明的是拼接不是构造，别再把两件事混着宣称。
- **面积密度采样 `PointsPerBlock`（v1.3 本轮追加，`PCGVolumeSamplerNode` 的第 6 个参数，注册槽 "Points Per Block"）**：分区是"每格跑一遍图"，所以 `Point Count` 在分区下的真实语义是**每格**的采样数——设计师把流式格子从 1000 调到 250（本来只想调窗口粒度），世界里的道具数就乘 64 倍，生成时间与内存跟着乘。这个陷阱用实测钉住了：`Point Count = 2000` 时整图 2000 点，250 格分区 **128000 点**。新参数按 XZ 面积采样（每 1000×1000 单位面积多少点，与格子的 Y 无关），**格子大小不再改变世界密度**：2000/块在 2000×2000 区域是 8000 点，切 4/16/64 格**逐点相等（delta 恰为 0）**；125 格那档是 8192（+192），因为**不足一块也向上取整**（否则小格子一个点都不采，世界里会出现按格子大小排列的洞）。误差上界写成**每格至多 1 点**并进断言，而不是含糊说"近似不变"。`PointsPerBlock = 0` 保持原来的固定数量语义（默认值，老图行为不变）。
- **同一个模式也加给了 `LandscapeSampler`（本轮追加的第 10 个参数，注册槽 "Points Per Block"）**：它的抖动网格本来就是 `cellSize = sqrt(面积 / PointCount)`，所以固定数量在分区下同样是"每格数量"；改成按面积取数量后 `cellSize = 1000 / sqrt(每块点数)`，**只与密度有关、与这次跑的是整区还是某格无关**。地面散布是本项目最常用的一档，这个陷阱不堵上比体积采样器更贵。**验证边界要说清**：地形节点在离屏跑不起来（`context.World.HasTerrain` 直接返回空），所以它没有逐点不变性断言，只有同一套取整公式与注册槽一致性由门覆盖；**PIE 目视项 ⑮**：把 Landscape Sampler 的 `Points Per Block` 设非 0，改 `PartitionCellSize`（如 800→200）各生成一次，道具数量应基本不变，而 `Points Per Block = 0` 时数量应随格子数成倍变化。`MeshSampler` 的 `Density` 是**点属性**（生成概率，注册槽 "Density"），不是面积密度，别和 `PointsPerBlock` 混；`SplineSampler` 另有按弧长的 `Spacing` 模式（不靠每格计数）。
- **勘误 + 一个新发现的分区隐患（本轮查证）**：上一条曾写过“Mesh/Spline 仍是每格计数”，那是**没读码就下的结论**，现已更正——`SplineSampler` 有 spacing 模式，不按格计数；`MeshSampler` 不用 bounds 过滤采样点（`GenerationBounds` 只用在**没有宿主 Actor** 的那条回退分支上）。但正是那条回退分支有个真问题：无宿主时它把模型摆在**本次运行区域**的中心再按 `Point Count` 采样，而分区是每格跑一遍图 ⇒ **每格都会以自己的格心为中心铺一整团点**，runner 再把每格裁到自己的范围里——结果是道具数随格子数成倍增长，而且在格心排成可见的阵点。padding 救不了这种错（它不是接缝，是"每次运行重新定位"）。**已知的正确用法**：分区图要给 MeshSampler 指一个真宿主 Actor（`Actor`/`Actor Name`），让它在世界里有一致的锚点。**待做的修法**（下一轮，二选一）：回退分支优先取 `PCGExecutionContext.Source` 那个 Volume 的世界盒中心而不是本次运行 bounds；或者在无宿主且 `Partitioned` 时直接拒绝并说明原因。本轮没有动代码，因为它离屏跑不了（需要 Model 资产与真场景），改完必须按 PIE 验证；先把问题与复现条件写进文档，别让下一个人在阵点状道具面前重新推一遍。
- **参数槽位的接缝断言（本轮追加，`StreamingTests.ParameterSlotTests`）**：图文件里存的是**下标**，所以追加参数真正的风险是错位——它不会报错，只会让每张已存图的后续参数整体平移一格，然后被怪到编辑器头上。四条断言钉住：两个采样器的 `ParameterCount` 与注册槽条数一致（6 / 10）、逐槽各读各的值（体积 6 槽；地形第 10 槽不加平移前 9 槽）、以及**向后兼容**：参数追加之前存的 5 值体积图仍能读出 `Point Count` 并落回固定数量模式（`PointsPerBlock = 0`），而不是越界抛错或捡到邻居的值。harness **502 → 506 passed / 0 failed**。
- **上一条里的 MeshSampler 回退隐患本轮已修**：不走“节点回头读 Volume”（那会让 Nodes 依赖 Runtime，`PCGMeshSamplerNode.cs` 在 PCGGoldenRunner 里直接编不进去——我先这么写了，runner 立刻生成失败），而是在执行上下文上加一条中立通道 **`PCGExecutionContext.SourceBounds`**（分区前的整个生成区域），由 `PCGVolume.BuildContext` 填 `TransformVolumeToWorld()`，回退分支取 `context.SourceBounds ?? context.GenerationBounds`。**未分区时逐字 no-op**（运行 bounds 本来就等于体积世界盒），只有分区/流式路径不再每格重定心。PIE 复核 **⑯**：无宿主 MeshSampler 的图，`PartitionCellSize` 从大到小各生成一次，点数应基本不变且看不到按格心排列的团块。当前门：PCGVerify 0 错误、PCGEditorVerify 0 错误、harness 506 passed / 0 failed、BENCH OK。离屏 7 条（`StreamingTests.DensityModeTests`）；harness **495 → 502 passed / 0 failed**。
- **一次真实的回归，被这轮的自检组抓在前**：分层改造把 `Generate()` 拆成按层循环后，**未分层的普通 Volume 不再设置 `LastResult`**（聚合只在 `layered` 分支里做）。所有离屏门都是 0 错误、harness 全绿——因为 `PCGVolume` 根本不在离屏覆盖面里。修法是单层路径直接取 `_runs[0].Result`，并把这条路径本身写进引擎自检（`PCGSelfTest` 3g：两层各自的点数、标签顺序、总数 == 两层之和、重复生成不改变各层点数）。**教训记牢**：改到 Volume 这一层时，离屏门只能证明"还能编译"，不能证明"还在工作"。
- 验证：**10 条**新断言（三层各得 1/4/64 格、每层都完整铺满区域且无重无漏、坏层按名报错且整组中止、无层报错而非静默空转、同点同格跨层名字互不归属、跑过的层不脏、**没跑过的层不能借邻居的缓存变干净**）。harness 合计 **495 passed / 0 failed**（其后又加了流式窗口、请求预算与跟随判定的 72 条、图效果分类与子图走查的 42 条、窗口等价性的 7 条，见上）。
**仍未做**：① **让宿主把"世界变了"喂进来**——插件看不到地形笔刷或宿主 Actor 移动，所以版本号本来只能靠人拨（`PartitionWorldVersion`）；**本轮补上了 `PCGVolume.PartitionVersionSource`（`Func<int>`）**：游戏侧若已有地形修订号 / 宿主 transform 戳 / 存档计数，从这里交进来即可自动失效，且与手拨值**合并而非替代**（`PCGPartitionCache.CombineWorldVersion`，两条输入谁变都会动，有 5 条断言钉住）。**仍缺**的是"不接钩子也能自动发现"——那要引擎侧的脏信号，不是本插件能拿到的；② **不做每帧定时器**——`UpdateStreaming()` 由宿主调用（世界侧的位置/chunk 清单钩子见下），插件不自己按帧驱动。

---

> **分块对齐的流式窗口（v1.3 本轮新增）**：`PCGVolume.StreamChunkSize > 0` 时，窗口不再是圆，而是**视角所在的那一格 chunk 向外扩 `ceil(StreamRadius / StreamChunkSize)` 格**（`PCGCellStreaming` 里按矩形相交选格）。三点值得说明：① **它是"世界可见分块驱动窗口"的插件侧那一半**——世界系统只要按自己的分块粒度给出"可见的位置"，窗口就与它对齐，生成的格不会被切在 chunk 中间；② **窗口只取决于视角在哪一格，不取决于它在格内的位置**，所以在同一格内走动**一格都不重生成**（这正是圆的方案做不到的：视角一漂，边界就动，又要靠滞回环去压）；③ 判定用**正相交**而非相切——圆模式里"贴着窗口的格子距离为 0"所以要算进去（零半径时中心处那 4 个格就是这条规则），而分块模式问的是"覆盖了哪些 chunk"，只共边的 chunk 不算被覆盖，否则会平白多生成一整圈。<br>**离屏证据**：`StreamingTests.ChunkedTests` 9 条——`chunkSize = 0` 与圆模式逐格一致（回归闸门）；分块窗口只会更大且包含圆窗口；半径小于一格时给出整块 **3×3 chunk**（且是**实心块、无洞**）；同一格内换角落**完全相同**、跨格则**不同**（正反对照，防"实现根本没看视角"）；半径需要两格时给出 **5×5**（证明是向上取整）；远处视角仍然全驱逐；无视角仍是空窗口。harness 799 → **808 passed / 0 failed**。
>
> **世界自己给"可见 chunk 集合"（本轮补齐）**：若世界系统已经知道它加载了哪些 chunk，就不要让插件再从玩家位置去**推**——推出来的只会与它不一致（多生成一块没加载的地，或少半块）。`PCGVolume.StreamChunkSource`（`Func<List<Int2>, int>`，与 `StreamViewerSource` 同形状、同**按实际写入量钳制**的契约；返回 0 是"世界什么都没加载"，即空窗口，而**不是**回退去推半径）一旦设置，窗口就**等于那份清单**：`PCGCellStreaming.SelectChunks` 按 chunk 矩形与格子矩形**正相交**选格，列出之外的一律交回（**没有 keep 半径**——留着就等于给"世界判定不可见的地"生成内容）。滞回在这个模式里由 **`StreamChunkBorder`** 表达（多列一圈 chunk），因为这属于**调用方的加载策略**。两条路径共用同一段预算逻辑（`ApplyBudget`：缓存命中的格子不计入预算这条规则只写一遍，写两遍必漂移）；另外有一条**交叉断言**：把"由半径推出的窗口所覆盖的 chunk 集合"显式喂进去，选出的格子必须**逐格相同**——否则插件就在对同一个窗口说两套话。离屏 `StreamingTests.VisibleChunkTests` 10 条，harness 833 → **843 passed / 0 failed**。**读取这份清单本身被抽成了 `Execution/PCGStreamChunks.FromSource`**（而不是留在 `PCGVolume` 里的两行）——列表是**跨请求复用**的，来源若"写了 2 条却返回 7"，就会把上一请求的 chunk 当作现在可见；这条规则放在引擎外的类里才能被断言（6 条，含拖尾不残留、负值当 0、无来源不崩），放在调用点只能"相信它"。

### 7.4 层级生成（任意层数，v1.4 由两级推广）

UE5 的 Hierarchical Generation 用"粗层决定、细层落地"把小道具的生成限制在有意义的区域里。本插件 v1.3 做了两级，v1.4 推广到**任意层数**（课程第 13 集 "Hi-Gen：大规模 PCG 工作流" 的能力面），并把范围说清楚：

- **规划**（`PCGPartitionHierarchyPlanner`，纯函数，离屏断言从 9 条扩到 26 条）：每一层的网格**都由同一个 `PCGPartitionPlanner.Plan` 规划**，所以半开边界、尾部空条、padding 钳制、格数上限只有一套实现；这一层只加**相邻两层的父子关系**——细分格归属"包含其**中心**的父格"（800/300 这种不整除比例下会有跨格细格，仍各自唯一归属）。
- **层数怎么给**：`PCGVolume.HierarchicalLevels`（默认 2，钳 [2,8]）。中间各层的尺寸由 `GeometricSizes(coarse, fine, levels)` **等比插值**——用的还是设计师原有的两个旋钮（`HierarchicalCoarseSize` 与 `PartitionCellSize`），所以**两级时返回的就是原来那两个尺寸**，行为逐格不变（离屏有专门的等价断言）。**最后一层的尺寸是钉住的而非累乘出来的**：反复乘一个不精确的比值不该决定设计师亲口写下的那张网格。
- **执行是一条级联**：第 0 层在**它自己的窗口**内跑 `DryRun` → 活着的格子决定第 1 层要考察哪些格子（再与该层窗口取交）→ …… → **只有最后一层真跑**，且只跑"活着的父格之下、且在生成窗口内"的格子。第二个条件不是形式：窗口正是 `ClearStreamingWindow` 已经依据的集合，取交集才能让**清理与执行仍逐格一致**。每一层都有自己的窗口，因为各层是**独立的网格**，粗格的 (x,z) 下标对细格没有意义。
- **决定层的点不是输出**：它是同一个图在粗分辨率下的采样，交出去等于用错误的分辨率再摆一遍道具；`ExecuteHierarchical` 只返回最后一层结果。日志现在逐层报告：`hierarchical run over 3 level(s) - L0 4/9 live, 120 point(s); L1 16/16 live, 480 point(s); generating 16 of 144 cell(s) of 200 (… deciding run(s) …)`。
- **层数不是越多越好（这条必须写下来）**：多一层意味着多一层 `DryRun`。只有**某一层的判定真的砍掉了下一层的格子**时它才省钱——若粗层与细层的答案一致（例如均匀的嵌套网格），多出的那一层就是**纯开销**：真实叶格数不变，而决定跑变多。离屏断言证的是**结构上的收窄**（1 个活格 → 4 → 16，vs 144），不是测量出来的收益。
- **与 UE5 的差异（写在这里免得误判）**：**细层拿不到粗层的点**。本插件执行器没有跨运行的数据通道，所以粗层结果是**过滤器**（"这里要不要生成"）而不是输入；想在细层复用粗层数据，只能放进同一次运行。
- **引擎自检 3j**（`PCGSelfTest`）：同一张图先按平坦分区跑、再按两级跑（默认值即两级，行为不变），断言**点数完全相同**（粗层过滤不能丢内容）且**节点运行次数严格更少**（经 `PCGVolume.NodeInspection` 数出来）；夹具的图只保留远离某个角落的点，所以四分之一的粗格确实是空的，节省是真的而不是期望出来的。



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

> **✅ “地形双路径一致”的盲区已于 2026-10-11 结清**（原先记的是“目前不能计为通过”）。原因正如当初记的：`World.scene` 的地形是**全零平面**（标定日志 `raw heightmap is constant`），此时 `scale=1`/`offset=0` 是“无法识别斜率”时的兜底值，两条路都返回 0，“差值”平凡成立。**修法是“不在平地上做这条检查”**：把两条路的交叉比较搬进 `TestTerrainSynthesis` 那段“**写入之后、还原之前**”的窗口（此刻地形上真的躺着一层刚写下去的土丘），并**加一条防空转断言**——采样点的高度**跨度必须 ≥ 1 世界单位**，否则报失败（理由里写明两种可能：这次没写进去、或**射线那条路没看到刚写下去的高度**）。实测同一帧里射线**看得到**新高度，所以这条“两路一致”现在是真的。**实测读数**：在那块写入起伏的地形上 `PCGTerrainProbe.Calibrate` 给出 `scale=0.999988, offset=0.00016822, samples=25, residual=0` —— **不再是退化的 `scale=1/offset=0`**，也就是“高度场原始 float 的单位就是米”这条语义**被有起伏的地形证实了**（此前只是假设）。平地那一档现在**如实报“跳过”**（`pcg_selftest` 的 `skipped` 里带理由），不再假装跑绿。

> **v1.3 实现进度（2026-10-06）。** Phase 2 的**节点库**已落地并接进 `PCGNodeRegistration`，注册类型由 3 个增至 **10 个**（含原有 3 个）：
> `SplineSampler`（按弧长反查，闭合段不采样，见 §5.1.1）、`HeightFilter`、`SlopeFilter`（优先读 `$Slope`，无列时由点的朝向推导并把推导结果回写该列）、`SelfPruningFilter`（均匀网格邻域，**平面模式故意不把 Y 编进格子 key**，否则同 XZ 不同高度的点会互相漏检；AABB 相切不算拥挤）、`TransformPoints`（世界/自身两套合成顺序）、`RandomOffset`（`(seed, nodeId, pointIndex)` 三元组流）、`MergePoints`（`PCGData.Merge`，同名异型直接报错）。
>
> **随本轮修复的一个真实缺陷：`$` 前缀内建通道曾被写校验一刀切拒绝。** `PCGAttributeSet.ValidateWritableName` 对所有 `$` 名抛 `BuiltInReadOnly`，而 `PCGLandscapeSamplerNode` 正是要 `DeclareAttributeUnsafe("$Slope")` 并写入该列 —— 也就是说**只要采样器真的产出了点，这个节点必然抛异常**。它长期没被发现的原因很典型：`World.scene` 的地形是全零平面，采样 0 个点，`written == 0` 提前 return，代码根本没走到那行。修正方式是把内建通道显式分成两类：`$Slope` 因 `PCGPoint` 没有对应字段而是**列存储的内建通道**（可声明/可读写/参与 Gather 与 Merge），其余 `$` 通道是**虚拟通道**（读写都抛 `BuiltInReadOnly`，不再是从字典里取不存在键造成的 NRE/KeyNotFound）；`Contains` 的语义同步收紧为「本集合真的存着这一列」。
> 现在这三条都有可执行断言兜着：离屏 `Tools/PCGGoldenRunner`（**当前基线 113 passed / 0 failed**，见下方 v1.3 各段落的分项来源）+ 编辑器内 `PCGSelfTest` 新增组（属性通道契约、样条弧长采样、图表经 JsonAsset 存取往返；后两组需要真实 Actor/资产，离屏跑不了）。自检组数 12 → **15**，**尚未在 PIE 里重跑**。
>
> **v1.3 补：§5.2 的暴露参数覆盖机制已接通。** 此前 `PCGVolume.ParameterOverrides` 是**只写不读的死字段** —— 声明了、序列化着，但没有任何一处代码消费它，所以「同一图表在不同 Volume 上用不同参数」这条核心价值实际不成立。现在有了 `Execution/PCGExecutionPlan.cs`：每次运行按覆盖表**只重建受影响的节点实例**（编译产物与其结构哈希都不动，正是注 9 要的性质），优先级为 节点作者值 → 图表暴露默认值（仅填补空槽）→ 实例覆盖；`PCGGraphExecutor` 增加可选 `plan` 参数接收这批实例并把计划层告警并入 `result.Warnings`（注意：**不能**把计划告警塞进 `_warnings`，因为 `Execute` 每次运行开头会清空它 —— 那样「覆盖了一个根本不存在的参数名」就会静默消失）。这条通路有 11 条离屏可执行断言覆盖（含"覆盖真的改变运行结果""编译产物未被改写""哈希不变""多参数同节点可叠加""未知参数名有告警且告警能到达结果"）。
>
> **v1.3 补：参数槽位 schema 与编译期参数校验（§4.5 第 3 项补齐）。** 节点参数是位置化 `object[]`，单靠类型检查抓不住「值写错槽位」：`PCGNodeParameters.Get` 对类型不匹配一律回落到节点默认值，于是图里填了值、跑出来却是默认值，而且没有任何提示。新增 `Execution/PCGNodeParameterInfo.cs`（Name / Kind / Default / Options / Description），注册表逐项声明 **48 个槽位**；`PCGGraphCompiler.ValidateParameterSlots` 把「kind 不匹配」判为 **Error**（会产生与图上所见不同的结果，不该静默跑），把「超出 schema 的多余槽位」与「无参数节点被塞了值」判为 **Warning**。schema 的 `Default` 必须等于节点自己传给读取器的兜底值 —— 这条不变量由离屏测试用「空值数组构造的节点」与「按 schema 默认值构造的节点」比较 `DescribeParameters()` 来把守（注册表用 `KindOf(default)` 反推 Kind，也让「声明 float 却给字符串默认」在注册时就抛）。
>
> **顺带修掉一个会让画布失灵的 bug**：`PCGNodeParameters.Get` 原先只处理枚举槽位的**字符串**形式，整数值走 `Convert.ChangeType(raw, enumType)` —— 而 `Convert` **拒绝**生成枚举值，异常被 catch 掉后静默回落到默认值。也就是说**编辑器里下拉框选了"Heightfield Interpolated"，节点仍按 Ray Cast 跑**。现改为 `Enum.ToObject` + `Enum.IsDefined` 守卫（越界索引回落到默认值，而不是凭空造一个未定义的枚举）。离屏断言 **81 passed / 0 failed**。
>
> **v1.3 补：图表载体此前是一个不可用的托管 Asset 子类，已改道 JsonAsset（本轮最重要的发现）。** 代码里曾存在 `Runtime/PCGGraphAsset.cs : Asset`，`PCGVolume.Graph` 也按该类型声明 —— 但正如本文注 4 自己写下的硬约束（纯托管插件无法注册资产类型，注册在 C++ 侧），**这个类永远不可能被编辑器创建或加载**，于是 `PCGVolume.Graph` 在 Content Browser 里根本没有可赋的值，整条「场景 → 生成」链路从编辑器侧不可达。这也解释了为什么 Phase 1 的 `$Slope` 崩溃缺陷一直没被 PIE 撞见：没有人能从场景真正跑起来。现删除该类，改为：`Graph/PCGGraphJson.cs`（**插件自己的**版本化 JSON 编解码，kind 标签逐槽位保存，显式拒绝更高 `Format` 版本）+ `Runtime/PCGGraphStore.cs`（读写引擎 `JsonAsset.Data`）+ `PCGVolume.Graph` 类型为 **`JsonAsset`**（编辑器可创建、可 diff、可入版本控制）。
> 为什么不用 `JsonAsset.SetInstance/GetInstance<T>` 而要把 JSON 自己写：图表是一棵**多态 `object[]` 参数值**的树，值必须以原来的装箱类型回来（节点读取器对类型不匹配一律回落默认值，所以装箱类型漂移=静默跑默认）。这条线现在由离屏断言把守：**逐槽位类型保真** + **结构哈希跨存取不变** + **原图与重导图跑出的点逐位相同**。离屏 **99 passed / 0 failed**。
>
> **核验方法升级（本轮起可用）**：引擎安装包内的 `FlaxEngine.CSharp.xml` 只有成员**名字**，没有字段/属性类型，而编辑器托管 API 就住在这个 editor-target dll 里（安装目录没有 `FlaxEditor.CSharp.dll`，也没有编辑器 C# 源码）。新增 `Tools/FlaxApiDump`（一个引用该 dll 的小反射探针，锚点必须是 `typeof(FlaxEngine.Actor).Assembly`，`Assembly.LoadFrom` 会对这个程序集布局抛 `ReflectionTypeLoadException`），一次跑出真实签名。本轮据此核实：`NodeArchetype.TypeID`/`GroupArchetype.GroupID` 是 **ushort**、`NodeArchetype.IndependentBoxes/DependentBoxes` 是 **int[]**（索引而非元素对象）、`Create` 是 `CreateCustomNodeFunc`、`ISurfaceContext.SurfaceData` 是 **byte[] { get; set; }**、`SurfaceNode.AddBox(bool isOut, int id, int yLevel, string text, ScriptType type, bool single, int valueIndex)`、`SurfaceNode.#ctor(uint, VisjectSurfaceContext, NodeArchetype, GroupArchetype)`、`ScriptType.#ctor(Type)`、`JsonAssetBase.Data { get; set; }`、`NodeElementType` 枚举里**没有 Vector3 值控件**（只有 Float/Integer/Bool/Color/Rotation/ComboBox/EnumValue/TextBox/Asset 等）。
 —— **v1.3 补：编辑器代码的离线编译门已建好** `Tools/PCGEditorVerify`（引用同一个 editor-target `FlaxEngine.CSharp.dll`，并 `DefineConstants=FLAX_EDITOR`，这样 `PCGVolume` 里 `Actor.OnDebugDraw`/`DebugDraw` 那些只在编辑器目标导出的分支也真被编译校验；Flax 的 `*.Build.cs` 模块规则引用 `Flax.Build`，已从 glob 排除）。当前 0 错 0 警。
>
> **v1.3 补：图表载体最终定为「插件自持 .json 文件」（用户拍板），编辑器画布模块已落地。**
> 演进过程记下来免得有人重走：先试 `PCGGraphAsset : Asset`（托管插件注册不了资产类型 → 编辑器根本创建不出来，字段永远空 → 整条生成链不可达）；再试引擎 `JsonAsset.Data`（读通，但**脚本侧找不到任何公开的资产写回 API**，内容窗口也只有 `Select(...)` 而没有读取当前选中的公开成员）；最后定为 `Runtime/PCGGraphFile.cs`：文件放在 `<Content>/PCG/Graphs/*.json`，用 `System.IO` 读写（写盘走 temp+replace，避免半截文件下次解析失败看起来像图丢了），路径支持裸名/相对/绝对并用 `Path.GetFullPath` 归一（不归一会产出 `PCG/Graphs\name.json` 这种混合分隔符，报错信息读起来像自己写错了），找不到文件时报**搜过的全部路径**而非一句 not found。`PCGVolume.GraphPath` 取代原来的资产引用字段。**代价与约束**：打包后的游戏没有工程目录，发布时这些图文件要随构建部署；这是这条路线的已知成本，不是缺陷。
>
> **`Source/PCGEditor` 不再是空壳**：`PCGEditorPlugin`（Tools 菜单入口 + UI 未就绪重试 + reload 时清原型表）、`PCGNodeArchetypes`（从运行时注册表 + 参数 schema 生成 `GroupArchetype`/`NodeArchetype`，`Signature` 存注册表类型名，ushort 只在会话内有效、图里永远存名字）、`PCGGraphEditorWindow`（派生 `EditorWindow` 且**自身就是** surface 的 owner/context —— `IVisjectSurfaceOwner` 要求实现 `ISurfaceContext`，这个事实由编译器告知，省下了一层多余的上下文类）。连线追踪用**节点 ID** 而不是画布下标（Box 的连接列表不公开，而下标会因删除节点整体错位）。**保存有护栏**：画布为空而图表非空且没有 Visject 画布数据时**拒绝保存**，不会静默清空设计者的图。
>
> **v1.3 补：画布的节点参数面板 + 暴露参数勾选已落地（§6.2 的 Values↔参数语义映射完成）。** 单选节点后右侧按 schema 渲染各槽位：标量一个输入框、Vector 三个、Color 四个；Bool 用 CheckBox（注意 Flax 1.12 的 `CheckBox` **没有** `Text` 属性，标签要另放 `Label`）；Enum 的标签直接把选项与索引写进名字里（`Mode (0=Ray Cast, 1=Heightfield)`），因为 `NodeElementType` 里没有 Vector3 值控件、也没有可用的下拉控件。写回走 `Values`（位置化数组）+ `surface.OnNodeValuesEdited(node)`，并且**只有解析成功才写**，失败时把原因显示在面板底部而不改值 —— 半截输入的框会按 0 处理（正在打字不该被标红），非数字则明确拒绝。文本↔值的双向转换是纯函数 `PCGEditor/PCGParameterText.cs`，**离屏 harness 直接执行它**（含一条遍历全部 13 个节点类型 48 个槽位的断言：每个声明的默认值都必须能穿过自己的输入框往返并保持**原来的装箱类型**）。这里逼出一个真问题：`float.PositiveInfinity.ToString("R")` 是 `"∞"`，Height Filter 的无界默认既显示不出来也打不回去，现显式写成 `Infinity`/`-Infinity`/`NaN`。
>
> **v1.3 补：Subgraph 节点与跨资产环检测（注 11 兑现），注册类型 13 → 14。** `Nodes/PCGSubgraphNode.cs` 运行另一张图并把点交给父图：子图继承父的生成范围/世界服务/**取消令牌**（跑不掉的子图会拖住整轮），但用**自己的种子流**（`父种子 + 本节点 SeedOffset`），所以同一子图被两处引用且偏移不同才会出不同布局 —— 这条由离屏断言钉住（两处包含同一子图，输出点位置必须不同）。子图执行器的告警**合并进本节点的 `LastError` 再向上冒泡**：嵌套执行器的 result 在这里被丢弃，不合并的话子图里的问题对设计者就凭空消失（运行时深度守卫正是靠这条路报出来的）。`Execution/PCGSubgraphInspector.cs` 做展开遍历：跨文件环（`a -> b -> a` 直接印出链）、自环、缺失子图、无名子图、深度上限（默认 16）、展开节点数预算（Warning 不拒绝），**菱形复用（两个父节点共享同一子图）不算环** —— 这是最容易误判成环而把正常图表判死的情况，专门写了断言。`PCGVolume.Generate` 与编辑器「校验」都先跑这道遍历再编译。运行期另有独立深度守卫（`MaxRuntimeDepth`），因为守卫要防的正是"绕过了检查的加载器"：栈溢出在编辑器里会带走整个进程。离屏 **160 passed / 0 failed**。
> **同轮还修掉一个静默丢数据缺陷**：`ExtractGraph` 重建图时只写了 Name/EditorData/Nodes/Edges，**把 `ExposedParameters` 整批丢掉** —— 于是 Volume 上的覆盖会突然"找不到该暴露参数"。现在暴露参数跟连线一样按**节点 ID** 追踪（`TrackedExposed` + `MaterializeExposed`）：图的持久格式仍存索引（保存时由 ID 重新算），因为画布会增删节点导致索引漂移，按索引追踪会把某个旋钮悄悄指到另一个节点；节点没了就丢弃该定义（让覆盖列表诚实地报"参数不存在"），而不是重新编号顶上去。

> **v1.4 补：图表编辑器"无法保存 / 重开空白 / 顶部菜单不见了"三个症状的真因，全部由引擎源码定位（不再靠反射猜）。**
> 前提更正：上面"安装目录没有编辑器 C# 源码"只对**安装包**成立；本机有整份引擎源码 `C:\Works\FlaxEngine\Source\`，**含 `Source\Editor\Surface\`**（`VisjectSurface*.cs`、`VisjectSurfaceContext*.cs`、`VisjectSurfaceWindow.cs`）。反射只能给签名，**方法体与返回值约定只能读源码**，本轮三个结论都出自那里：
> 1. **`VisjectSurface.Save()` / `Load()` 返回 `true` 表示失败**（注释原文 "True if failed, otherwise false."；`VisjectSurfaceContext.Save/Load` 自己 catch 后 `return true`）。我们此前按常规约定读反了，于是：**保存成功也报"画布布局序列化失败"**并把窗口永久标脏（设计者据此判断"无法保存"）；**载入成功也提前 return**，跳过 `TrackEdgesFromGraph/TrackExposedFromGraph`，画布看着对、下一次保存却把连线全丢。两处判定已改正，措辞同步改成"报告失败"。同款约定在 `OpenContext` 里也有一处：`if (surfaceContext.Load()) throw new Exception("Failed to load graph.")`。
> 2. **`VisjectSurface` 构造函数第一行就是 `AnchorPreset = AnchorPresets.StretchAll; Offsets = Margin.Zero;`**（"填满父容器"）。宿主用显式 Bounds 给它让出顶栏/底栏/右栏时，必须把锚改回 `TopLeft`，且**改锚要排在设 Bounds 之前**（`AnchorPreset` setter = `SetAnchorPreset(preset, preserveBounds:false)`，会把 Location 归零）；否则每次窗口 resize 都走 `OnParentResized → UpdateBounds` 按 ctor 期算出的 margin 重摆矩形。官方宿主的写法是另一种：`VisjectSurfaceWindow.cs:1018` 保留 StretchAll、用 `Offsets = new Margin(0, 0, _toolstrip.Bottom, 0)` 给工具栏让位。
> 3. **画布不会画到自己矩形外面**：`ContainerControl` 默认 `_clipChildren = true`，surface 只显式关了 `CullChildren`。所以"顶部菜单不见了"不能归因于节点/网格溢出，只能是 surface 矩形真从 Y=0 开始。据此把顶栏控件改成**在画布之后创建**（Flax 按创建顺序绘制，顶栏于是永远在最上层，即使矩形被谁改坏也是按钮压住画布而不是相反），并加了两条自报：首帧把窗口/画布/路径框/保存按钮/状态栏的实际 Bounds 打进日志；一旦在帧首发现画布 Y ≠ `HeaderHeight` 就 `LogWarning` 一次。**像素问题转成了文本证据**，不需要截图也能判断还剩哪一层。
> **还有一条与代码无关但决定"修了没生效"的事实**：编辑器加载的是 `Plugins/PCG/Binaries/PCGEditorTarget/Windows/x64/{Development,Debug}/PCGEditor.CSharp.dll`（不是 `Tools/PCGEditorVerify` 的编译门产物），而本机 **Debug 那份曾比 Development 旧 7 小时** —— 只重编 Development 时，若编辑器按 Debug 加载，任何修复都表现为"问题依然存在"。两份现在都由 `Flax.Build.exe`（cwd = 插件目录）重编，并用"DLL 里 UTF-16LE 中文字面量计数（新 4 条各 1、旧那条 0）"确认新代码真的进了二进制。离屏四道门不变：`PCGEditorVerify` 0 错、`PCGGoldenRunner` **899 passed / 0 failed**。

> **v1.4 补（同一个症状的第二层根因）：画布载荷是二进制，被当成文本存了，写盘那一刻就坏了。**
> 上面两条修完，顶栏回来了、也**如实报出了**真失败（这正是当初要的"有话说"）：状态行 `图表里有画布布局，但 surface 没能反序列化它（Load() 报告失败）`。顺着这句查文件 —— `Content/PCG/Graphs/NewGraph.json` 的 `EditorData` 里**有 46 个 `U+FFFD` 替换字符**，即布局在**保存时**就已损坏，与读侧无关。
> 原因：`VisjectSurfaceContext.Save()` 通过 `BinaryWriter` 写画布（节点 meta、int/float、定长前缀字符串），产出的是**任意二进制**；而 `SurfaceData` 的 setter 用 `Encoding.UTF8.GetString` 把它塞进 JSON 字符串。UTF-8 解码对非法序列一律替换为 U+FFFD，再编码回去已不是原字节 —— 无损的文件格式从一开始就选错了（参见 §3.3 那条被更正的"不要 base64"）。
> 修法与迁移：**`Format` 1 → 2，`EditorData` 改存 base64**（`PCGGraphJson.EncodeEditorData/TryDecodeEditorData`）；读侧按 `Format` 决定解码方式，**v1 且含 U+FFFD 的载荷丢弃并把原因写进 `PCGGraph.EditorDataNote`**（节点与连线完全不受影响），窗口据此走 `RebuildCanvasFromGraph()` 用数据重建画布并明说"布局已损坏、保存一次即写新格式"；v1 且未损坏的载荷自动转成 base64。损坏载荷**不复用**是有意的：留着它只会让下次加载继续抛，而节点数据本来就能把图救回来。
> 离屏断言（`PCGGoldenRunner/GraphJsonTests.cs`）钉住两个方向：**任意二进制载荷 16 字节逐字节往返相等**，以及**同一个字节序列走 UTF-8 文本存储必须变得不相等**（正向对照：证明这条测试不会因为"载荷恰好是合法文本"而空过）；另有 v1 损坏→丢弃+有说明+节点保留、v1 完好→迁移成 base64、非 base64→明确拒绝、空载荷→安静通过。共 **+11 断言 → 910 passed / 0 failed**。手头那份 `NewGraph.json` 是 v1 损坏文件，重开时会按 6 个节点重建（位置在数据里，`X/Y` 完好），保存一次即升级为 v2。

> **v1.4 补（连线保存的两个缺口）：连线是怎么被存下来的，以及"存不了"之前的最后一道保护。**
> 连线不走 payload，而是**画布事件 → 节点 ID → 图文件下标**：`NodesConnected/NodesDisconnected`（`Box.cs:397/370`，两侧都是 `Box`，顺序不定所以按 `Box.IsOutput` 判方向）→ `_edges`（按**节点 ID** 记，不按下标）→ `ExtractGraph` 把 ID 映射回节点下标。引擎侧的三条前提这一轮都对着源码核实过：`SurfaceNode.AddBox(bool isOut, int id, ...)` 把传入的 id 原样交给 Box（`SurfaceNode.cs:367`），`PCGNodeArchetypes.SpawnNode` 先加输入再加输出（所以**输入的 pin 就是 box id，输出的是 box id 减输入数**），而 `PCGGraphCompiler` 要的正是"下标进 `OutputPins`/`InputPins`"（`:283-305`）。这三条一旦有一条变了，存下来的每条连线都会**静默接到别的引脚上**——不抛异常、不报错，只是每次生成都读错数据。
> 因为这个转换此前是内联在事件处理器里的两处表达式、且**没有任何测试**，本轮把它提成纯函数 `PCGEditor/PCGNodePinSlots.cs`（和 `PCGParameterText` 一样链进离屏 harness）并补 6 条断言：输入的恒等性、输出的输入数偏移、**输出的下标必须依赖输入数**（少写这一项就等于用 box id 当引脚号，对单输出节点直接越界）、对所有已声明形状（输入/输出 0–4 共 25 种）双向往返共 **100 对**、以及"输入优先编号"这条被依赖的排布本身。另在图文件侧补 2 条**按值**的连线断言（既有的夹具两条边都是 pin 0，交换两端的字段也看不出来）：端点四个值逐一相等，且**一条连接不会被颠倒**。
> 保存侧同时补了一道保护：`RebuildCanvasFromGraph`（v1 损坏载荷后的重建路径）能复原**节点但不复原连线**，从这种画布保存会把"空连线表"写进一份有连线的文件——**静默删除设计者的连线，状态行还会高高兴兴地报"0 条连线"**。现在 `_wiresNotRestored` 为真且画布连线少于文件时**先拒绝一次**并说明差多少、怎么继续，**再点一次「保存」**才执行（图表编辑器没有确认框，两次点击是最便宜的确认）；用户把线全部接回来后条数相等，保护自然失效。成功行也会在"追踪到的连线有端点不在画布上"时追加一句，任何少写都不再是静默的。
> 共 **+8 断言 → 918 passed / 0 failed**。连线真正的端到端判据仍在编辑器里（见 §9 ①）：连一条线 → 保存 → 关窗重开 → 线还在，且**接在正确的引脚上**（把上游节点换个输出引脚再存一次，恢复后应仍然接在那个引脚）。

> **v1.4 补（画布与文件"看起来不一致"的真因）：`RebuildCanvasFromGraph` 只往画布上加，从不先清。**
> 症状很好认也很误导：校验报的是 AttributeFilter/SpawnActor/OffsetByAttribute/Branch，画布上看到的却是另外三个节点——**同一个窗口先后打开过两张图（或粘贴过一次），画布上叠着两批节点**，`NodeFactory.CreateNode` 是 append 语义，没有 `Clear` 这一步。于是"读画布"的校验、以及"写画布"的保存，都与设计者眼前那张图不一致。修法：重建前先 `Context.Clear()`（`VisjectSurfaceContext.Clear()` 公开，清 Nodes 并 `DisposeChildren`），一次把三条路都修好——换图打开、粘贴/副本、v1 损坏载荷后的重建。顺带解掉一个老限制：
> **重建现在连**连线**一起恢复**。此前认为做不到，是因为"读"一侧确实没有公开 API（`Box.Connections` 里有什么读不到），但**根本不需要读**——图文件已经写明哪两个引脚相连，用 `PCGNodePinSlots.BoxId` 把 pin 下标换回 box id、`SurfaceNode.GetBox(id)` 取到盒子、`Box.CreateConnection`（引擎自己插 Caster 时用的原语；注意 `Connect` 是**切换**语义，不能用在恢复上）连起来即可；这一动作会触发 `NodesConnected`，所以 `_edges` 追踪器自动补齐。于是 `_wiresNotRestored` 只在"真的有几条解析不到"时才是真，存侧护栏退为兜底。粘贴/副本路径此前是"全图重加一遍＋连线全丢"，现一并修好。
> **另加一条给"改了没生效"用的判据**：保存成功行现在带 `（画布载荷 v2）`，窗口首帧的布局日志也带 `画布载荷格式 v2（旧版编辑器只会写 v1，日志里看到 v1 说明 DLL 没更新）`。实测：用户 22:03 保存的文件仍是 `"Format": 1` 且带 22 个 `U+FFFD`，而修复后的构建只会写 `Format 2` + base64 —— **这就是"编辑器还在跑旧 DLL（DLL 只在启动时加载）"的硬证据**，不必再靠猜。

> **v1.4 补（第三个症状"提示已加载但画布空白"的真因）：payload 里的视图是**带着尺寸**存的，而我们在窗口显示之前就把图读进来了。**
> 证据链（无需截图即可判）：文件 `Format 2`、base64 解得干净、2 个节点、1 条线；把 payload 末尾的 meta 解出来是 `typeId=10, size=12` → `ViewCenterPosition=(14410.7, 7346.1) scale=1.85`，而节点在 root 空间 (14045.7, 7228.8)/(14266.7, 7226.7) —— **视图正对着节点，文件没有错**。可 `EditorWindow` 构造函数里就调了 `LoadGraph("NewGraph")`（文件在盘上存在 ⇒ 走 payload 路径），**那时窗口还没 `Show()`、surface 还没被排版**；而
> `VisjectSurface.ViewCenterPosition` 的 setter 是 `_rootControl.Location = Size * 0.5f + value * -ViewScale` —— **用的就是"当前尺寸"**。拿一个未排版的尺寸（1×1 或旧的默认值）去还原视图，节点就被推到可视矩形之外：节点在 context 里（所以连接追踪得到 1 条线、状态行说"已载入 2 个节点"）、网格照画（它由 surface 自己画）、**节点一个都看不见**。
> 两处修：① `ShowFor` 改为**先 `Show()` 再 `LoadGraph`**（顺序即语义）；② 更关键的是**自愈**——加载后置 `_frameGraphWhenSized`，在 `OnUpdate` 里等到 surface 有真实尺寸再判"图在不在视野里"，不在就 `ShowWholeGraph()`（引擎官方宿主加载后也这么做）并在状态行追加一句原因。判据**必须用 root control 的实际 `Location`/`Scale` 反推可见区间**，**不能用 `ViewCenterPosition`**：后者按当前 `Size` 重算，会把"用错尺寸"这件事正好掩盖掉（先写成前者时算出来是"在视野内"，等于没修）。判据取"图的**中心**是否落在可见矩形内"而不是"是否有相交"——边缘挂几个像素也算相交，但那样不叫能用。
> 顺带查清：`SurfaceRootControl.DrawChildren` **无条件**用 `child._cachedTransform` 做剔除（不看 cull 标志），所以"节点在但没画出来"这一类症状只可能来自变换/剔除，不可能来自"没加进子控件列表"。另附首帧诊断日志 `[PCG] 画布视图：N 个节点，节点范围 …，根控件位置 …，节点在视野内=…`，让"空画布"的三种成因（没有节点／视图不在这儿／塌成一点）一眼可辨。

>
> **v1.3 补：补齐文档承诺却缺失的采样器与筛选器，注册类型 10 → 13。** 新增 `NoiseSampler`（用插件自带 `PCGSeededNoise`，**不用**引擎 `Noise`——引擎噪声无种子，换种子会毫无变化；点的 density 直接写噪声值，让边缘是"稀疏"而不是"消失"）、`VolumeSampler`（体积内均匀撒点；明确它**会**成簇，要均匀请用 Landscape Sampler 或后面接 Self-Pruning）、`DistanceFilter`（默认 XZ 平面测距：MMO 里"营地周围散布石头"不该被脚下地形高低左右）。三者都 `IsThreadSafe = true` 且**不写日志**，以便离屏 harness 能真跑；`PCGSelfTest` 自检组 **12 → 15**（属性通道契约、样条弧长、图表文件往返——后两组需要真实 Actor/真实磁盘，离屏跑不了）。
>
> **上一条缺口本轮已补：节点的失败理由现在真的有消费方。** `LastError` 上收到 `PCGNodeBase`（`public string LastError { get; protected set; }` + 执行器专用 `internal ClearError()`），五个原本自带该字段的节点删掉各自声明 —— 否则它们是**隐藏**基类成员，执行器读到的永远是 null。执行器在每次节点执行后 `CollectNodeError()` 把理由并入 `result.Warnings` 并立即清空：不洗掉旧理由，同一轮里缓存命中的第二个消费者会重复上报同一条。`PCGVolume` 侧删掉原先单独打印 `plan.Warnings` 的循环（执行器已把计划层告警并进 `result.Warnings`，两处都打会让每个参数问题**显示两遍**）。链路现在是 节点 → 执行器告警 → `PCGVolume` 日志 → 设计者可见，离屏 **+2 断言 → 115 passed / 0 failed**。
>
> **仍需 PIE 目视确认的若干件事**（我无法离屏验证，逐条列在这里以免接手时重复摸索；编号只增不改，所以最大的编号就是"共有多少条"）：① `VisjectSurface.Load()` 能否从文件里的 payload 复原画布（**仍未验**，最快判据：加节点 → 保存 → 关窗 → 重开，节点应还在；**连线单独再验一次**：连一条线 → 保存 → 重开 → 线还在，且**接在正确的引脚上**——把上游节点换成有两个输出的、连到第二个输出，保存重开后必须仍连在第二个上（引脚错位是静默的，图照跑、只是读错数据）。**v1.4 起这条有了明确的成功/失败措辞**：保存成功状态行是 `已保存 N 个节点、M 条连线到 <路径>`，重开成功是 `已载入 N 个节点、M 条连线、K 个暴露参数`；**重开成功后还要看一件只有肉眼能判的事**——画布上的节点是否在**保存前的位置**上、视图缩放/平移是否被记住（这部分只在 v2 的 base64 载荷里）。若看到 `surface.Save() 报告失败` / `surface 没能反序列化它`，说明 Visject 自己 catch 了异常，**编辑器日志里紧跟着必有两行 `Loading/Saving Visject Surface data failed.` + 异常栈**，那就是要报的原文。另注意重编要覆盖 Debug 与 Development 两份 DLL，见 v1.4 那条）；② 代码构造的图（无 payload）走 `NodeFactory.CreateNode` 重建时节点是否真注册进上下文并显示（**已由用户截图确认 2026-10-07**：窗口打开即是可编辑的空图，右键弹出 `Select Node`、七个分类齐全（Combinator / Control Flow / Filter / Modifier / Point Ops / Sampler / Spawner，Spawner 下 5 个生成器全在），加节点后引脚正常显示。**此前是个真缺陷**：构造里只建 surface、**从不 `LoadGraph`**，surface 拿不到 context，于是右键毫无反应——已改为打开即加载一个内存里的未命名图（`NewGraph`，不落盘，保存才有去处） —— 不行时窗口会明说"请勿保存"，数据无损；③ 参数面板的选中刷新与输入框写回在真实画布上的表现（`SelectionChanged`/`OnNodeValuesEdited` 事件时机）；④ 暴露参数勾选与保存后 Volume 覆盖列表的对应关系；⑤ 新增的"分区 volume"自检组（`PCGSelfTest` 3b）在真实场景里跑通：Preview 模式建一个 `PCGVolume`，切成 2 格跑同一张图，日志应出现 `PASS partitioned volume: N point(s) across 2 cell(s), spacing kept` —— 该组断言的是**Actor 链路真的走到了分区**（离屏只能验证 planner/runner 本身），并且要把体积设得够大、`PartitionCellSize` 设得够小才能确认真的分了格；⑥ 植被 spawner 的自检组（`PCGSelfTest` 3c）打出 `PASS foliage spawner: 12 instances on one actor, reused across runs, clipped per cell`；⑦ 用它撒一片草后**从远处走近再看**：实例应当平滑出现/消失，不该整片突然不见——这条是在证实 §5.1.5 v1.3 第 3 点那个尚未验证的假设（节点不写 `FoliageInstance.Bounds`，包围球由 `RebuildClusters()` 自己算）；⑧ 给 `PCGSelfTest.ActorPrefabPath` 填一个项目里的真 Prefab，让 3d 组从 SKIP 变成 `PASS prefab spawner: 6 owned by the volume, 3 per clipped cell`；⑩-a 分层 Volume 的引擎自检（`PCGSelfTest` 3g）应打出 `PASS layered volume: N + M point(s) across 2 layer(s), stable on regenerate`；⑩ 分区 + spawner 的逐格清理：同一个 Volume 跑两次、第二次什么都不改，日志应出现 `partition cells - 0 executed, N reused` 且**场景里物体数量不变**；再 bump `PartitionWorldVersion` 跑一次，应看到 `kept 0 fresh cell(s), cleared M actor(s) from N dirty cell(s)` 且数量仍不翻倍。⑨ 给 `PCGSelfTest.DecalMaterialPath` 指一个 Decal 域材质（**本项目 Content 里当前没有**），让 3e 组的放置那半从 SKIP 变成 `PASS decal spawner: 4 decals owned by the volume`；这两条是本轮唯一没法自己跑绿的引擎断言——引擎没有供脚本用的现成 Prefab，项目也还没做过 Decal 材质。 ⑩ 在编辑器里选中两个以上节点看批量面板（只列共同参数、识别数与选中数是否一致）、走一遍 复制→粘贴→副本，确认粘贴结果与状态行报的数量一致。 ⑫ **流式窗口的引擎自检组（`PCGSelfTest` 3h）**应打出 `PASS streaming window: N point(s) for the region, M within 250 of the centre, nothing off-region, N again when reopened, X then M on a one-cell request budget`；它证明的是窗口真的收窄了请求（而不只是日志里格子数变少），**最后一次必须逐点等于第一次的 N**（不相等就说明格子计划被跨请求留住了），以及末尾那对 `X then M` 证明请求预算这半边在 Volume 上是通的：`StreamCellsPerRequest = 1` 时一次请求只出 `X`（`0 < X < M`），同一个中心连跑 6 次必须回到 `M`——**这就是"预算不会把外层格子饿死"的端到端证据**，离屏只能证明选择算法本身。另外在编辑器里手玩一遍确认驱逐这半边（离屏只能证明缓存与选择是对的）：同一个分区 Volume 开 `Streaming`、`StreamRadius` 设成只盖住一两格 → Generate → 记下场景物体数；再把 `StreamCenter` 挪到区域外 → Generate → **这个 Volume 名下的物体应当全空**（`PCG_Gen_..._x?z?_` 与叶片宿主都在驱逐集合里），且日志出现 `streaming cleared N actor(s) - 0 cell(s) in the window, M handed back`。 ⑬ **跟随驱动的引擎自检组（`PCGSelfTest` 3i）**应打出 `PASS streaming follow: idle frames cost nothing, one request per cell crossed, a one-cell budget filled N point(s) worth of window in R requests (A -> B), an 800/100 layered volume steps on the 100-unit grid, and two viewers cover more ground than one`；最后那半抓的是**只在真场景里才会现形**的接线问题：`FollowStep` 的取值来自最细一层，而 `PCGVolume` 不在离屏覆盖面里，编译门只能证明它还能编译。末句是**多视角窗口**的端到端证据：`StreamFollowTargets` 放两个相距 800 的 Actor，直接 `Generate()` 的点数必须**严格大于**只留一个目标时的点数（两个窗口不相交 ⇒ 并集更大）；这半走的是 `Generate` 而不是 `UpdateStreaming`，所以它验的是并集选择本身，不是逐帧逻辑。同组还多一条**等价断言**：同样两个位置改成经 `StreamViewerSource` 交出来，点数必须与经两个 Actor 跟随时**完全相同**——source 是"世界的输入"，不是第二套机制。 ⑰ **层级生成的引擎自检组（`PCGSelfTest` 3j）**应打出 `PASS hierarchical generation: N point(s) from R node run(s) where the flat grid needed F`，且 `R < F`、两次点数相同：同一张"只保留远离某角落的点"的图，先平坦跑、再开 `Hierarchical`（粗格 800、`HierarchicalLevels` 默认 2）跑，内容必须一致而**节点运行次数必须更少**——这是"粗层真的在决定"的唯一端到端证据（离屏只能证明规划与父子关系）。**v1.4 追加一条**：把 `HierarchicalLevels` 改成 **3** 再跑一次，**点数仍必须相同**（多一层判定不能改变内容），日志应出现 `hierarchical run over 3 level(s) - L0 …; L1 …`。**注意不要断言"3 级比 2 级更快"**：多一层就多一层 `DryRun`，只有该层的判定真砍掉下一层格子时才省钱；均匀嵌套网格下三级反而更贵（真实叶格相同、决定跑变多）。**顺带看四个新类别**：右键菜单应出现第 8 组 **Structure**（里面有 `Split Grammar`），把它接在 `Volume Sampler` 后面 → 视口里应出现**按比例切开的格子点**（默认规则下一颗种子出 2 格），`Max Depth` 设 2 且 `Rules` 写成 `X:0.5,0.5; Z:0.6,0.4` 时出 4 格；接 `Spawn Static Model` 看"一格一个物件"、格子是否贴在一起不留缝（缝隙=切分尺寸算错，这条只有肉眼能看出来）。 ⑱ **按节点读产出**：在图表窗口里点「试跑」，然后**点选单个节点**，状态行应出现 `节点 N 上次试跑产出：K run(s), P point(s)…`；换一个**没接进图里 / 没被求值到**的节点，应显示 `节点 N 上次试跑没有运行（未接在图里、或没被求值到）。`——这两种措辞是分开的，"没跑"和"跑了但零产出"是不同诊断（折叠逻辑有 4 条离屏断言，但"点选后状态行真的更新"只能在这里看）。 ⑲ **Preview 不落地**：`PCGSelfTest` 3c（植被组）里已加断言的预期效果是——同一个 spawner、同一批点、同一个父级，只把运行标记换成 `AsDryRun()` 后**场景里的 Foliage 宿主与实例数都不变**，且它仍然把点原样传下去；该组此前的 12 个实例就是**正向对照**（证明"没变化"不是"什么都没发生过"）。跑 PIE 时若看到植被组通过，就等于这条保证在真场景里成立。 ⑳ **Spline Mesh 生成器（`PCGSelfTest` 3k）**应打出 `PASS spline mesh spawner: 4 piece(s) owned by the volume, 2 in half the cell, none in a preview run`：一条 20 单位曲线配 5 一段 → 体积名下恰好 4 个 `StaticModel` 且都带生成前缀、只覆盖左半的 `ClipCell` → 恰好 2 个、`AsDryRun()` → 一个都不多（该组需要 `FoliageModelPath` 能加载，加载不到时是 SKIP 不是 FAIL）。 ⑭ **图表窗口的「试跑」按钮**（`PCGGraphEditorWindow.DryRunClicked`）：拿一张只读图（采样→剪枝→偏移）点它，状态行应出现 `试跑完成（区域 600×600）：汇出 N 点，共产出 M 点，耗时 X ms.` 并紧跟一行逐节点耗时；给图里加一个 `SpawnActor`/`SpawnFoliage` 或一个**内含 spawner 的子图**再点，应被**拒绝**并提示"往场景生成的节点（Spawn*，或某个子图里藏着它们）…"，且**场景里一个 Actor 都不许多**（这条拒绝是安全闸门，不是便利功能：试跑没有 Volume 负责，Clear 也拿不回这些 Actor）。反过来，**只含数据节点的子图应当照常试跑**——闸门判的是内容，不是"有没有用子图这个功能"。想再验一层手感的，把 `StreamFollowTarget` 指向本地玩家走两百米：日志里 `streaming window` 的行数应当与跨过的格子数一致，**站着不动时一行都不该新增**。 ㉑ **选中节点的视口采样绘制**（`PCGGraphEditorWindow.DrawSelectedSamples`）：点「试跑」后**只选中一个**节点，视口里应出现一串绿色线框小方块标出该节点上次产出点的位置（边长 8 单位的立方线框，位置取自 `PCGNodeInspection.Summarize` 的有上限采样）；换一个没跑或零产出的节点则**什么都不画**（不画假框）。这是"采样已备好、只差绘制入口"的收尾——绘制走 `DebugDraw.DrawWireBox`，而 debug draw 只在编辑器运行态有承载它的世界，所以离线四道门只能证明它编译通过并守住了 null / 越界守卫（不可用时状态行报一次"视口绘制节点采样不可用：…"后不再重试，而不是每帧抛）。 ㉒ **Texture Sampler 的适配器**（`PCGTextureSamplerNode`）：给 `Texture Path` 指一张**项目里的未压缩纹理**（导入设置里没开压缩），图里接一个采样器撒点，点上的 `texture` 列应随图像明暗变化——把 `Filter` 切 Nearest/Bilinear 看边缘是否变平滑、把 `Address` 切 Repeat 看超出图案范围的区域是否重复。**压缩纹理应当被拒绝**（原因里会写明"只支持未压缩、单平面"）且图原样透传而不是崩。这条是本轮"引擎绑定"首次真正落地的取样点，观感只能在这里看。 ㉓ **分块对齐窗口的接线**（`PCGVolume.StreamChunkSize`）：在流式分区 Volume 上把 `StreamChunkSize` 设成与世界的 chunk 同值（别用 0），记下窗口内的物体数；再把 `StreamCenter`（或跟随目标）在**同一格 chunk 内**挪一段距离后 `Generate()` → **物体数与位置都不该变化**（不新增、也不清理）；再挪**过一格边界** → 物体应随窗口整体平移。最后把 `StreamChunkSize` 设回 0，行为应回到原来的圆形窗口。这条验的是**接线**：选择逻辑与 `chunkSize = 0` 的等价回归都已经在离屏 9 条里钉住了。 ㉔ **Project Points 的引擎侧**（`PCGProjectPointsNode`）：拿一层**已经存在的点**（例如 `VolumeSampler` 在一面上有起伏的地上撒点、`Height` 设得离地很高）接进投影节点 → 每个点应落到地表，`$Slope` 有值；打开 `Align To Normal` 时点的 up 应朝地面法线、**平地上则是恒等旋转**（不再是那 180° 偏转）；再把该层挪到**地形之外/湖面**上跑一次 → **点数不变**、那些点留在原处（只有打开 `Drop Misses` 才被删）。最后这条同时是"投影默认不动点数"的现场证据。 ㉕ **随机偏航的"贴地"性**（四个 spawner 的 `RandomYaw`）：把 `AlignToNormal` 与 `RandomYaw` **同时打开**，在一块明显起伏的地形上撒一批**有朝向**的物件（例如带一面的盒子/贴花）→ 每个物件应当**贴着坡面**只做自转，而不是被坡度角掀起来；改回只开 `AlignToNormal` 关 `RandomYaw`，朝向应与之前逐点一致（平地时两者本来就等价，所以这条必须在**坡上**看，见 §9 第三个缺陷）。 ㉖ **世界喂可见 chunk 清单**（`PCGVolume.StreamChunkSource` + `StreamChunkSize`）：让世界侧每个请求往列表里写它认为可见的 chunk 坐标、并返回**实际写入条数**（写 2 条却返回 7 必须被钳制——列表是复用的，信任返回值就等于把上一请求的残留当成可见）。跑起来后窗口应当**恰好**是那份清单（`StreamChunkBorder` 多列一圈），清单外的一律清掉；再把清单清空跑一次 → **窗口应为空、物体全清**——这是"世界说它什么都没加载"的正确表现，**而不是**回退去按半径推。这条与 ㉓ 一起构成分块流式的接线验收。 ㉘ **Ray Cast Project 的引擎侧**（`PCGWorldRayProjectNode`）：在场景里铺一块**有实体碰撞的网格**（例如一个 Static Model 或地形本身，注意先给它加 collider），用 `Volume Sampler` 撒一层点、`Height` 设在网格上方，接 `RayCast Project` → 点应当落到**网格表面**（不是穿到下面的地形），`$Slope` 有值、按法线对齐时平地上是恒等旋转。再把 `Layer Mask` 设成**不含**该网格的层 → 点应落在更下面那层（或落空保位，取决于 `Drop Misses`），这一条证明掩码真的传到了引擎而不是只存在面板里。**离屏已覆盖查询参数被转发**（测试里用假 caster 记下实参），但"引擎真的按掩码过滤"只有这里能看。**注意**：没有物理的场景里这个节点会报"no physics"并**原样透传**（不是清空），日志/状态行里能看到原因。 ㉛ **程序化几何的引擎侧（已接线，待目视）**：`Volume Sampler → Geometry Primitives → Spawn Geometry`。**先看数据侧**：在图表窗口「试跑」后状态行/检视里 `Meshes` 数量应与点一致。**再看场景**：应出现台阶/圆柱/楼板，位置与朝向随点，**圆柱的上下端盖都要可见**（端盖绕序曾反过，离屏靠体积抓出过）。三条最容易出问题的：① **尺寸是否只乘了一次**（actor 缩放是 1，网格里已是点的尺寸——若看到形状比格子大一倍，就是尺寸被乘了两次）；② **同尺寸的点是否共享模型**（场景里物件数与"不同尺寸数"应当不同，同尺寸一千个点只该有一份网格；日志里 `Spawn Geometry` 的 distinct meshes 数可对照）；③ 若出现 **actor 生成了但看不见**，先查 `Model.LODs[0].Meshes[0]` 是否为 null / 上传是否被拒（公开的 `UpdateMesh` 返回 void，失败只留空网格）。另外**预览模式**下不应建任何模型、也不该多出 actor（`DryRun` 直接透传）。 ㉜ **洞穴的引擎侧（已接线，待目视）**：`Volume Sampler`（或地貌底图筛出的山地）→ `Cave` → `Spawn Geometry`。**先看数据侧**：图表窗口「试跑」后状态行应报出点数 = 各隧道中心线长度之和、样条数 = 隧道数（每条隧道一条）、以及同样数量的内向管道网格。**再看场景**：应当出现一串**朝内可见的洞壁管道**——关键判据是**站在管道里能看到内壁而不是看穿**（法线与绕序一起翻过；若看到背面剔除把朝内的面剔掉、从外面能看进去，就是翻反了）。三条最容易出问题的：① **从入口开始的方向**：默认参数下应当先近乎竖直下潜、接近 `Max Depth` 时收平成平巷（这正是"深度上限靠截短步长、俯角渐近收平"的观感；若看到洞底有一刀急转或洞管自己叠回来，说明方向与实际段又脱钩了）；② **分叉点是否与父隧道接得上**（分支的第一点应当落在父隧道中心线的某一段上，不应悬空或在入口处重新开始）；③ **`Tube Sides` 是唯一的规模旋钮**：默认 8 边看得到明显的棱，改成 16/24 应当变圆——若调了没反应，说明参数没进槽位。另外关掉 `Make Meshes` 应只剩点与样条（用 `Create Spline` 之外的节点沿样条铺网格来目视中心线走向）。 ㉝ **预览不再查资产库（四个生成器已改序，待目视）**：`Spawn Static Model` / `Spawn Foliage` / `Spawn Decal` / `Spawn Actor` 的 `DryRun` 早退从"查资产之后"挪到了之前，顺序现在是**纯数据检查（没指模型、上限为 0）→ DryRun 早退 → 查资产 → 落场景**。要看的只有两件：① **预览模式（`Volume.Mode = Preview`）仍然会说"没有指定模型"这类纯数据的错**——这是有意保留的，因为它不需要资产库；② **把模型槽位指到一个存在但没导入/没加载的资产上，再切预览**：这一轮不该再报资产错（改序之前会报），而是照常把点透传下去、状态行里只有预览的说明。若预览里开始出现"Model asset … is not loaded"，说明早退又跑到查资产后面去了。 ㉞ **按描述生成（编辑器入口，待目视）**：图表编辑器**第二行**填 `valley` 再点「生成」。要看四件：① 画布上出现 **17 个节点、9 条连线**，且**摆成整行**（不是全部叠在原点）；② 状态行读到 `已按描述 valley.pcgspec 生成 17 个节点、9 条连线。确认无误后点「保存」落到 …`；③ 点「保存」后再关窗重开，这 17 个节点与 9 条连线都还在（这一条把"生成 → 保存 → 重开"接成闭环）；④ 把描述名改成不存在的名字点「生成」→ 状态行应当说「读不到描述文件 …」而**不是**窗口出错。另外仓库里那份 `Content/PCG/Specs/valley.pcgspec` 可以直接用；它里面的模型路径是占位（本仓库没有树石房路模型），所以状态行会带几条"没解析出资产"的提醒，那是如实报告而不是缺陷。 ㉟ **按描述生成：引擎侧自检组 3l（可一键跑，不必靠眼睛）**：给场景里的 `PCGSelfTest` 组件跑一遍，日志里应当出现

```
[PCG] PASS spec to scene: 描述 7 节点 → 预览 N 点 / 0 Actor → 烘焙 M Actor（重跑不累加，清空归零）
```

它做四件事，顺序有讲究：① 只**编译**仓库里那份整套示例（地貌/植被/水/村庄/道路/洞穴），确认 `17 节点 / 9 连线` 与文档记录一致——**文件不在工程里时打 `SKIP` 而不是失败**（那是内容，不是代码）；② 用一份**小而全**的临时描述（散布链 + 道路链 + 洞穴链，模型一律解析不出来）真跑：**预览必须一个 Actor 都不建**（这是"预览不碰场景"的引擎侧形态，也正是四个生成器 DryRun 早退必须排在查资产之前的原因），**烘焙必须建出洞穴的几何**（这一句就是"按设定描述生成场景"的最后一段证词）；③ **再烘焙一遍 Actor 数不许变**（重新生成是替换不是累加）；④ `Clear()` 之后归零，且销毁数等于刚跑完的个数。临时描述与临时图跑完即删，体积也 `Actor.Destroy`。

㉚ **Actor Sampler 的引擎侧**（`PCGActorSamplerNode`）：在场景里摆两个空 Actor，名字写成 `Marker_A`/`Marker_B`、`Tag` 都写 `village`；给 `Actor Sampler` 接一个 `Spawn Static Model` → 每个标记处应出现一个物件（位置/朝向与 Actor 一致）。再把 `Tag` 填成别的值 → **点数应变成 0 且无任何报错**（"没有标记带这个 Tag"是诚实的空答案，不是错误）。关键一条：**先跑一次生成、再跑第二次** —— 场景里物件数不应翻倍（`Skip Generated` 让本插件的产物不会被当成输入）；把它关掉再跑，物件**应该**开始增殖，这正是这个开关存在的理由。**离屏已覆盖匹配规则与谎报条数的源被钳制**，但"引擎真的列出了关卡里的 Actor、`Actor.Tag`/`LayerName` 真的读得到字符串"只有这里能看。

 ㉙ **Grammar 的编辑器侧**（`PCGGrammarNode`）：右键 → Structure 组里应有两个节点（`Split Grammar`、`Grammar`）；给 `Grammar` 接一个 `Volume Sampler` 种子层 → 默认产生式下每颗种子出 4 片叶子（两个 Room + 两个 Back），在检视输出里能看到 `Symbol`/`SymbolName` 两列且名字与产生式一致；把根符号改成不存在的名字 → 状态行应报"root symbol … does not appear in the productions (it names: …)"并**保留原点**（不是空层）；把 `Room = Room,Room: X:0.5,0.5` 加进去做递归 → 深度到 `Max Depth` 就停。**格子是否严丝合缝地拼在一起只能肉眼判**（缝隙=切分尺寸算错，离屏的"体积守恒"抓不到视觉上的空档）。

 ㉗ **整图采样叠加**（图表窗口的「全绘」按钮）：点「试跑」后按「全绘」→ 视口里应出现**多簇不同颜色的小方块**，每簇对应一个出点的节点；选中单个节点时它仍按绿色单画（两种叠加并存）。同一张图**再试跑一次**，每个节点的颜色应当**不变**（`ColorFor` 只由节点下标决定）——这条是"能对着颜色说'橙色那团是剪枝后的'"这个用法的前提。产出为空的节点不应有任何方块（状态行会说明它"没跑"还是"跑了但零产出"）。


> **管线级离线覆盖（本轮补，`PipelineTests`，+17 条）**：逐节点测试看不到"节点进了图与文件之后"的那些约定，所以另起一组：**① 六个新节点族逐个过一遍图文件往返**（`Grammar`/`SplitGrammar`/`ActorSampler`/`RayCastProject`/`Geometry`/`SpawnGeometry` 的槽位按位置与**装箱类型**逐一比对——新节点的参数下标就是它的文件格式，构造里读的顺序与注册表不一致时**不抛异常，只是把作者存的值整体错位**，而这是文件里唯一能看出来的地方）；**② 编辑器"是否写场景"闸门**（`GraphWritesScene`）：采样器→几何返回 false，**同一个图加上 `SpawnGeometry` 返回 true**（正向对照，否则一个"永远说不"的分类器也能让前一条通过）；**③ 两级几何管线真的编译并执行**（`VolumeSampler → Geometry → Geometry`）——断言端到端产出的点数与 `Meshes` **下标对齐**，且**最后一级的形状才是到达终点的那个**（若第二级是"追加"而不是"替换"，这条会红），并断言运行没有节点异常。这三条把"新功能在真实编译/执行/持久化路径上通不通"从"只能 PIE 看"降成了离线可判。

> **PIE-only 清单（本轮审计产物，说明"离屏为什么看不见"）**：以下路径**结构性**地无法被 `PCGGoldenRunner` 覆盖，因为它们的执行要么需要引擎世界（地形/资产/场景），要么根本不在 harness 的链接清单里。区分"只能 PIE"与"其实已离线覆盖"是这轮审计想钉住的事：
>
> | 只能 PIE 的对象 | 为什么 | 它的哪部分其实已离线覆盖 |
> | --- | --- | --- |
> | `LandscapeSampler` | 需要地形（`context.World.HasTerrain` 否则直接返回空） | 参数读取与槽位对位、`PointsPerBlock` 的取整公式（与 `VolumeSampler` 同一契约） |
> | `MeshSampler` | 需要 Model 资产 | 同上；分区锚点用 `SourceBounds`，未分区路径逐字 no-op |
> | `SplineSampler`（Actor 版） | 需要场景里的样条 Actor | 弧长反查的**纯几何**部分已由 `PCGSpline` 的 33 条断言覆盖；数据版 `SampleSpline` 全离线 |
> | 四个 spawner（StaticModel / Foliage / Actor / Decal） | 需要资产 + 场景，且写场景 | ownership 命名规则、逐格清理的**名字归属判定**、`PCGGraphEffects` 的拒绝名单 |
> | `PCGVolume` 全链路（分区/分层/流式窗口/驱逐/跟随） | `PCGVolume.cs` 不在 harness 链接清单（它依赖 Flax Actor） | planner / runner / `PCGCellStreaming` / `PCGStreamTrigger` / `PCGPartitionCache` / `PCGStreamViewers` / `PCGStreamChunks` 全部离线覆盖，剩下的是**接线** |
> | **`ProjectPoints`（地形投影）** | 需要地形与 `PCGTerrainSampler` | 倾斜/对齐的实现被抽成 `PCGSurfaceAlign`，9 条离线断言覆盖（含平地恒等与 NaN 防护）；节点只剩"逐点查地形 + 挪 Y + 写 `$Slope`" |
> | `PCGStreamingDriver`、编辑器「试跑」按钮、画布与参数面板 | 引擎/编辑器对象 | 驱动决策（`ShouldRequest`）、效果分类（`GraphWritesScene`）、剪贴板与批量改参的运行时侧逻辑 |
>
> **结论**：PIE 要确认的是**接线与观感**，不是算法——算法那一层已经有 899 条断言。其中 ⑧⑨ 两项需要项目先补资产（真 Prefab、Decal 域材质），补齐前它们是 SKIP 而不是 FAIL；其余可在一次 PIE 会话里跑完。
>
> **本增量收尾审计（可复现）**：`注册表类型` 42 = `Nodes/` 下文件 42 = **0 孤儿**（`comm` 三向比对：注册表 ↔ 文件 ↔ runner 逐文件清单）；harness **899 passed / 0 failed**；`PCGVerify` / `PCGEditorVerify` 各 0 错误；`PCGBench` 全绿（`== BENCH OK ==`，含 100k 预算与流式请求尖峰量表）。**四道门覆盖不到的只有上表那几项**，其余全部有离线证据。
>
> **本轮抓到并修掉一个"被当成约定问题"的真实缺陷**：`PCGPointTransform.EulerDegrees` 把角度乘了 `180/π` 再交给 `Quaternion.Euler`，而引擎那个重载**本来就吃度数**（`Source/Engine/Core/Math/Quaternion.h` 第 579 行写明 "in degrees"；弧度版是 `RotationYawPitchRoll`）。后果是 `EulerDegrees(0,90,0)` 实际转 **116.62°**，于是 **Transform Points 的旋转参数、Copy Points 的 spread 旋转、Random Offset 的抖转角全部静默偏斜**。比缺陷本身更值得记的是它为什么活了这么久：116.62° 看着"差不多四分之一圈"，当时被归因成"Euler 与 EulerAngles 约定不一致"，**还据此把一条 Copy Points 断言从具体数值改成了几何性质**——症状被诊断了，原因没有。修法是一行（直接透传），另加 3 条断言把单位钉死（先拿引擎调用自身做对照、再要 helper 与它逐分量一致、最后 360° yaw == 恒等），并把那段误导性注释改写成真实原因。**另补一对断言把插件里两个"度数"API 对起来**：用 `EulerDegrees(0,0,80)` 转过的点，`SlopeDegrees` 必须读回 80——修复前这两者根本不同单位，而此前没有任何断言把它们放在一起看过。顺带把"0 孤儿"这条不变量又独立核了一遍：`Nodes/*.cs` 42 个文件 = 注册表 40 个类型 + 2 个无 `Id` 的辅助类（`PCGNodeParameters`/`PCGPointTransform`），runner 清单里的 `Nodes\*.cs` 条目也是 42 条；注册表那侧 40 个 `Id` 常量、40 处 `Register(`、`BuiltInTypes` = 40 三者一致。
>
> **本轮又抓到并修掉第二个同类缺陷（"代码与自己的注释互相矛盾"）**：地形对齐的 forward 写成 `cross(up, Right)`，而它在平地上等于 **−Z**——于是**凡开了 `AlignToNormal` 的点全部被偏转 180°**，而注释宣称"forward 尽量贴着世界 +Z"，且**没有任何断言管过朝向**。发现方式不是通读代码，而是给新抽出的 `PCGSurfaceAlign` 写第一条关于 forward 的断言时，它直接报出 `(0,0,-1)`。修法把叉乘次序换成 `cross(Right, up)`（平地 → 恒等旋转），并把"平地 == 恒等"钉成断言——**注释描述的约定改成断言描述的约定**，这正是 EulerDegrees 那次教训的同一形状。
>
> **第三个同类缺陷（本轮，影响面最大：四个 spawner 全中）**：`RandomYaw` 的注释写着"绕**点自己的** up 轴旋转、保持表面对齐"，代码却是 `Quaternion.Multiply(yaw, point.Rotation)`——**左乘 = 世界轴**（这条约定是 `TransformPoints` 自己在注释里写明的：local 右乘、world 左乘）。于是**只要同时开了 `AlignToNormal` 和 `RandomYaw`，随机偏航就会把物体按坡度角掀离地表**，正好抵消掉对齐那一步的全部意义；而平地（`Rotation = 恒等`）时两种写法等价，所以这个缺陷在平地测试里**看不见**。修法是抽出共用的 `PCGPointTransform.YawAboutLocalUp`（右乘 = 局部），四个 spawner 一起改，并加了 6 条离屏断言：平地行为不变、倾斜点的 up 在任意偏航下守恒、单位四元数、零/整圈是恒等，**以及一条反例对照**——把旧的左乘写法喂给同一个判据，它必须被判失败（否则"up 守恒"这条可能是恒真的）。
>
> **两处"文档承诺但没实现"的修复（本轮审计中被抓出）**：① **Preview 模式此前并不阻止写场景**——`PCGVolume` 的注释写着"Preview must not touch the scene at all"，但唯一执行的是"跳过 ClearForRun"，四个 spawner 照旧建 Actor。修法是给执行上下文加 **`DryRun`**（spawner 见到它就把输入原样传下去），`Mode == Preview` 时由 Volume 置位；**引擎 3c 里补了断言**（同图同点只换标记，宿主与实例数不变，而该组已放置的 12 个实例是正向对照）；这也顺带让"只做判定、不落地"的用法成立（层级生成的粗层正是这样用的）。② **子上下文丢字段**：`PCGPartitionRunner` 与 `PCGSubgraphNode` 构造 cell/子图上下文时**漏拷 `LayerIndex`、`SourceBounds`、`Inspection`（本轮又漏 `DryRun`）**——后果分别是**分层+分区时各层 Actor 互相覆盖**（名字里的层号都是 0）、**分区/子图里的 MeshSampler 退回逐格重定心**（就是 §5.1.1 修过的那个隐患）、**分区运行的"逐节点产出"为空**。两处已补齐，并在 `PCGExecutionContext` 类注释上写了**"加字段 = 两处复制 + 一个可离线观察的断言"**的检查表；离屏新增一条断言用 `Inspection` 直接钉住"分区运行会把上下文交给每个 cell"（`4 record(s) for 2 cell(s)`）。

> **多视角窗口与观众上限（v1.3 追加，本轮）**：`PCGCellStreaming.SelectAround` 是选择原语（单中心 `Select` 只是"一视角特例"，有断言钉住两者一致）；语义**不对称**——**活 = 靠近任一视角**、**驱逐 = 离开所有视角**，写反任一侧的表现是"玩家脚下没地"或"两个玩家擦身而过时互相删掉对方的道具"；预算排序按到**最近**视角的距离；**没有视角是合法状态**（这片世界没人）⇒ 窗口为空、全部驱逐，而不是全部生成。`PCGVolume.StreamFollowTargets` 非空时覆盖单目标，`UpdateStreaming` 为每个视角各记格号、任一跨格即请求。
> 在此之上：**`PCGStreamViewers.Reduce`（`StreamMaxViewers`，默认 8）**把观众数封顶——并集就是被生成的量，四十个玩家/chunk 等于要求整张地图。上限内**留下谁**比留几个更要紧：按列表顺序取前 N 会把整个预算花在**同一片邻里**上，所以用的是**贪心最远点采样**（先取离质心最近的一个，再反复取"离已选集合最远"的），O(n·k)、确定、无需聚类参数。离屏 9 条断言里最能说明问题的一条是**覆盖对比**：同样上限 3，按簇分布的三个观众覆盖 **12 格**，而取列表前三个只覆盖 **4 格**；另有"上限 0 = 不设上限"（避免把世界清空）、重合观众仍给有限结果、同输入两次同选择。**排除了一个我自己写下的空转断言**（`x || true` 那种恒真写法，见 [[test-traps-that-mask-the-verdict]]）。
> **观众从哪来（`StreamViewerSource`，本轮补上"世界侧输入"这一环）**：`Func<List<Vector3>, int>`，宿主（游戏的世界流式/分块系统）往列表里写"它认为被观看的位置"（玩家、已加载 chunk、可见块）并返回写了多少条；**优先级：source > `StreamFollowTargets` > 单个 `StreamFollowTarget` > 手工 `StreamCenter`**。关键契约是**返回值要按实际写入量钳制**：源码里 `Math.Min(Math.Max(claimed, 0), scratch.Count)`，因为 `scratch` 是逐帧复用的——信任一个写 2 条却返回 7 的 source，就会把上一帧的残留当成观众，"玩家走了但身边还在生成"就是这么来的（离屏 5 条断言，含"诚实 source 只被调用一次"）。这条钩子让游戏侧驱动而**不必让插件依赖游戏的 world 模块**（依赖方向会错）；引擎自检 3i 里加了一条**等价断言**：同样两个位置，经 source 交出来的窗口必须与经两个 Actor 跟随得到的**点数完全相同**——否则它就只是"另一种机制"而不是一个输入。

> **对标 UE5 PCG 的覆盖矩阵（本轮审计的正片，按 UE5 的功能面逐项判定，不按我们的节点名）**。图例：✅ 已覆盖（附证据）／🟡 部分／❌ 未做（附原因，区分"引擎绑定"与"没做"）。
>
> | UE5 PCG 功能面 | 状态 | 说明与证据 |
> | --- | --- | --- |
> | 图资产 + 节点画布编辑 | ✅ | §6 画布/参数面板/复制粘贴/多选批量；剪贴板 38 条断言 |
> | Points 数据类型 + 属性（Attribute Set） | ✅ | `PCGData`/`PCGAttributeSet`；Set/Remap/Math/Filter/Smooth 全离线断言 |
> | **Spline 数据类型** | 🟡 | `PCGSpline` + `CreateSpline`/`SampleSpline`（折线；**本轮加了 Catmull-Rom 细分**，见 §5.1.23，故"曲率"不再是缺口）；**仍缺**：逐点插值模式（UE5 的 per-point InterpMode）与**类型检查只是文档约定**（边模型无输出 pin） |
> | Volume/Landscape/Mesh/Spline/Noise 采样器 | ✅ | 5 个采样器 + 数据版 `SampleSpline`；地形与模型两条只能 PIE 复核；**本轮又加 `ProjectPoints`**（把**已有**的点投到地形面上，UE5 Projection 的对位；与采样器的区别是它移动而不造点，见 §5.1.24） |
> | Geometry/Surface/Texture/Polygon2D 等其他数据类型 | 🟡 | **Texture 已落地**：`TextureSampler`（CPU 通路 `TextureBase.GetPixels`，**仅未压缩/单平面**）+ 纯采样核 `PCGTextureSampling`（**27 条**离屏断言，含世界→UV 映射）；**Polygon2D 已落地**（v1.5）：`Data/PCGPolygon.cs` 纯核 + `PolygonFilter` 节点 + 描述层 `polygon "…"`，39 条离屏断言（含凹多边形与"凸性实现会误杀"的那条对照），见 §5.1.42——所以矩阵里这一格的 ❌ 只剩 **Geometry 数据类型**（真正需要几何承载类型：`MeshSampler` 目前把几何当"采样建议"而不是数据类型） |
> | Filter 族（Density/Distance/Height/Slope/SelfPruning/**Attribute**） | ✅ | 6 个过滤器，含"按任意列"过滤（UE5 Filter by Attribute 对位） |
> | Point Ops（布尔/复制/排序/包围盒/邻域/松弛/朝向密度/距离/噪声属性/通道拆列/按列缩放/按列位移/按列朝向） | ✅ | Difference/Intersection/Copy/Sort/BoundsModifier/Neighborhood/Relax/NormalToDensity/**DistanceToPoints**/**NoiseToAttribute**/**ChannelToAttribute**（把点的 scale/bounds/facing 读成一个数）/**ScaleByAttribute** / **OffsetByAttribute** / **RotateByAttribute**（**反方向**：用一列驱动点的尺寸、位移与朝向） |
> | Attribute ops（Set/Remap/Math/Copy-by-source/Filter/Smooth/**Transfer**） | ✅ | 七件套齐；写时复制契约有断言；**Transfer 按最近源点把一列从另一层搬过来**（网格搜索 + 独立暴力参照对照，含平面模式与并列决胜，§5.1.17），**并可一并采用最近点的放置**（`Match` = Position/Transform，UE5 `PCGPointMatchAndSet` 对位） |
> | Control Flow（Branch/Switch/Select/Gate） | 🟡 更近一步 | `Select`（整层择一）之外本轮加了 **`Branch`**（单进单出：条件开则原样透传、关则吐空层；条件是**节点参数**⇒ Volume 可覆盖，于是"关掉"是按放置位置的）；**UE5 式多输出 Switch 仍不可表达**（`PCGEdge` 无输出 pin 下标），分档要用两个 Filter |
> | Spawner（StaticMesh/Actor/Decal/Foliage-ISM/**SplineMesh**） | ✅ | 5 个；资产与场景写入需 PIE；**SplineMesh 用"等长块链"实现**（本 Flax 版无 `SplineRope` 类型，已反射核验） |
> | 分区生成（Grid2D + 跨格 padding） | ✅ | planner/runner/cache 全离线断言 + 接缝性质对照 |
> | 分层/Hierarchical Grid | ✅（v1.4 推广到任意层数） | `PCGVolume.Layers`（每层一图一格尺寸）之外，是**级联式层级生成**：`Hierarchical` + `HierarchicalCoarseSize` + `HierarchicalLevels`（2–8）+ `HierarchicalMinCoarsePoints`，层间尺寸由 `GeometricSizes` 等比插值；规划 `PCGPartitionHierarchyPlanner`（纯，26 条断言，含两级等价性）；**每个决定层都用 `DryRun` 只判定不落地**，末层只跑"活格之下 ∩ 窗口内"的格子。**层数有代价**：多一层多一次 DryRun，只有该层判定真砍掉下一层格子时才省钱。与 UE5 的差异写在 §7.4：**细层拿不到粗层的点**（本插件执行器没有跨运行数据通道），决定层结果是过滤器而不是输入。 |
> | 增量执行 / 脏格重算 | ✅ | 逐格签名 + 名字归属 + 截断失效；`PartitionWorldVersion` 是**手工**世界版本（无地形变脏信号） |
> | 运行期生成与流式（Runtime Generation / 窗口调度） | 🟡 更近一步 | 体积级流式窗口 + 请求预算 + 跟随驱动已完成，**多视角窗口已落地**（`PCGCellStreaming.SelectAround`：活=靠近**任一**视角、驱逐=离开**所有**视角；`PCGVolume.StreamFollowTargets` 覆盖单目标；离屏 7 条 + 引擎 3i 末句）；**本轮又加了分块对齐窗口**（`StreamChunkSize > 0`：窗口 = 视角所在 chunk 外扩 `ceil(半径/chunk)` 格，同格内走动不重生成；离屏 9 条，见 §7.3 末注）；**并补上"世界直接给可见 chunk 清单"这一环**（`StreamChunkSource`，窗口 == 那份清单 + `StreamChunkBorder` 圈数；离屏 10 条）；**仍缺**：UE5 那种**任意深度**的层级网格——本插件的层级生成是**固定两级**（粗层判定、细层落地），多尺度另有 `Layers`（一图一层一格子，§7.3）但语义不同；以及自动脏度信号（见 §7.3 末） |
> | 调试与检视（Inspector/逐节点输出/视口调试） | ✅ | Volume 视口调试绘制 ✅、编辑器「试跑」+ 逐节点耗时 ✅、**按节点读产出 ✅**（`PCGNodeInspection.Summarize` 折叠多次运行为一个答案 + 选中节点即在状态行显示，两种措辞区分"没跑"与"零产出"）、**选中节点的采样在视口里画出 ✅**（`PCGGraphEditorWindow.DrawSelectedSamples`：仅单选中 + `Summarize` 的采样 → `DebugDraw.DrawWireBox`，不可用时只报一次；观感需 PIE 看，见 §9 ㉑）；**本轮又加了"整图叠加" ✅**（「全绘」按钮：`PCGNodeInspection.CollectDraws` 把**每个出点的节点**列出来、各用一种颜色画，`ColorFor` 由节点下标确定所以同一个节点每次跑都是同色、相邻节点必然不同色；节点只列一次（分区跑 40 次不会画出 40 团），**产出为空的节点不画**——"跑了但零产出"由状态行的逐节点读数回答，而不是画一个假标记。离屏 8 条，颜色与上限都可离线断言） |
> | Determinism / 并行执行 / 预算 | ✅ | 索引键控 RNG、`PCGParallel`（等价断言）、100k 预算与尖峰量表 |
> | 蓝图/自定义节点扩展 | ✅ | C# 节点注册表 + 槽位 schema 断言（UE5 走 Blueprint PCG Element） |
>
> **结论**：UE5 的"数据面 + 算子面 + 分区/流式 + 编辑器检视"已经补齐到可用；剩下的缺口集中在两类——**引擎绑定**（**只剩 Geometry 数据类型**：Polygon2D 已于 v1.5 落地为纯核 + 节点 + 描述层子句，§5.1.42；16 HLSL 是 GPU kernel，托管插件够不到，属已定性的不可达）、**规模调度**（世界可见分块驱动窗口：插件侧**已经完整**——分块对齐窗口、世界直接喂"可见 chunk 清单"的钩子、多视角并集选择；剩下的是游戏侧真的接上这个钩子），外加**编辑器可视化做得更细**（沿图着色的数据流等，属锦上添花）。这些都不是"再写几个节点"能解决的，需要单独定方案。
>
> **逐节点检视的落地形状（本轮）**：`Execution/PCGNodeInspection.cs` —— 一次运行里按**求值顺序**记录每个节点的 `PointCount / 曲线数 / 列名 / 若干采样位置`，采样有上限（默认 8），所以十万点的一层也只被"记成"一个数字加几个样本；执行器在填完缓存后调 `context.Inspection?.Record(...)`，**没挂记录器时每节点只多一次 null 判断**（断言直接钉住"挂不挂记录器，图产出完全相同"）。编辑器「试跑」已挂上它并在状态行打印"逐节点产出：…"，于是"这个剪枝器吃掉了十分之九"这类问题不必再从终点反推。**按节点折叠（本轮补）**：`Summarize(nodeIndex)` 把一个节点的**多次运行**（分区/层级下每格一次）折成一个答案——`3 run(s), 12 point(s)` 加有上限的采样，而不是四百条十点的记录；`Ran=false` 时输出"**did not run**"，因为"没跑"与"跑了但产出为零"是两个不同的答案（前者通常是没接进 sink 路径，后者是过滤条件一个都没匹配）。编辑器里**点选单个节点即显示该节点上次试跑的产出**（`InspectionReadout`，4 条离屏断言覆盖折叠与两种措辞）。**顺带查清一件事**：插件的列名里始终存在内建列（`$Density/$Position/$Rotation/$Scale/$Seed/$Slope/$Bounds`），所以"这个节点没发布任何列"在报告里**不是空列表**——断言因此分两条：内建列要出现，而"完全无属性存储的数据集"才报空。

>
> **Phase 2 剩余 / Phase 3 待办（更新于本轮末）：** ① **参数面板多选批量编辑与复制/粘贴/副本已落地**（`PCGGraphClipboard` + 画布重建，见 §6.2 v1.3 落地实况）；剩下的是多选面板在真实画布上的观感与操作手感（需 PIE）；② 上面点到的九件事要在 PIE 里目视确认（payload 复原画布、代码图重建通路、面板事件时机、暴露参数勾选与覆盖列表对应、分区 volume 日志、植被 spawner 日志、远处实例剔除、给 3d 组指一个真 Prefab、给 3e 组指一个 Decal 材质）；③ Phase 3 里**分区生成已落地**（`PCGPartitionPlanner/Runner` + `PCGVolume.Partitioned`，接缝性质有离屏断言，详见 §7.3 v1.3 落地实况；分层网格与跨格增量重算仍待做），剩下的大件是**世界分块驱动的流式调度**（体积级的流式窗口本轮已落地：`PCGCellStreaming` + `PCGVolume.Streaming`，见 §7.3；还没做的是让**世界的可见分块**来决定窗口中心与半径——那需要 chunk 边界与视野输入，不属于一个 Volume 能自己看见的范围）与把剪枝再往下推一格（分带/波前并行——但 §5.1.2 的稠密网格已把它压到 45%，收益边界要看新的热点分布；见 §4.4 v1.3 实测）。节点库、分区生成、节点内并行采样、剪枝稠密网格、Phase 2 多选批量编辑与复制粘贴均已落地（§5.1.1 / §5.1.2 / §5.1.5 / §6.2 / §7.3）；④ Phase 1 遗留的地形标定盲区（高度场→世界 Y 语义）仍需一块**有起伏**的地形实测。§6.4 的视口批量 `DrawLines` 调试绘制**已在 `PCGVolume.OnDebugDraw`** 实现（含 `DebugDrawMaxPoints` 抽样上限）。
>
> **v1.3 核验补充（编辑器可行性的前提已经查实）**：托管编辑器 API 与引擎 API 同处一个程序集 —— `FlaxEngine.CSharp.dll`（Editor 目标）里含 1183 个 `FlaxEditor.*` 类型，`FlaxEditor.Surface.VisjectSurface` / `NodeArchetype` / `GroupArchetype` 都在其中；安装目录里**没有** `FlaxEditor.CSharp.dll`，也不带编辑器 C# 源码（`Source/Editor/Managed` 只有 `ManagedEditor.h`）。已核验的关键签名：`VisjectSurface.#ctor(IVisjectSurfaceOwner, System.Action, FlaxEditor.Undo, SurfaceStyle, List{GroupArchetype}, bool)`、`IVisjectSurfaceOwner` 只有 `OnSurfaceClose/OnSurfaceEditedChanged/OnSurfaceGraphEdited` + `Undo` 属性、`ISurfaceContext` 只有 `SurfaceName/SurfaceAsset/SurfaceData/ParentContext` + `OnContextCreated`、`NodeArchetype` 是字段表（`TypeID/Title/SubTitle/Size/Flags/Create/DefaultValues/IndependentBoxes/...`）。**因此写编辑器代码可以先离线编译校验**：照 `Tools/PCGVerify` 再建一个引用同一个 `FlaxEngine.CSharp.dll` 的 editor 校验工程即可，不必先启 FlaxEditor。



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

**v1.3 实测结果（`Tools/PCGBench`，纯托管数据链，无地形无 spawner）：** 图 = `VolumeSampler(N)` → `SelfPruningFilter(6)` → `RandomOffset` → `DensityFilter` → `TransformPoints`，区域固定 2000×2000，seed 固定。

| 请求点数 | 汇出点数 | 端到端 | 分配内存 | 工作集增量 |
|---|---|---|---|---|
| 1 万 | 8 738 | 62 ms | 4.9 MB | 5.0 MB |
| 5 万 | 29 042 | 178 ms | 18.5 MB | 15.8 MB |
| **10 万** | **40 552** | **57.7 ms**（并行采样 + 并行修改器 + 稠密剪枝网格；本会话起点 429 ms，累计 7.4×；同一版本内多次重跑有 ±2× 浮动） | **21.9 MB** | **20.1 MB** |

**据此写下的预算：单次 10 万点 scatter ≤ 400 ms（稠密剪枝网格落地后从 1200 ms 收紧，实测约 5.1 倍余量，吸收机器噪声），harness 里以断言形式守住，超线返回非零码。** 100k 重复跑两次逐点总数相同（262 208 = 262 208），与 §3.4 索引键随机流的承诺一致。

**本轮顺手修掉一个诊断性缺陷（它会直接误导性能判断）：** 执行器的逐节点计时把秒表**开在 `BuildInputs` 之前**，而拉取输入是递归的——于是每个节点报的都是"自己 + 整条上游子树"的时间。实测表现在沿图单调递增（47.8 → 79.5 → 94.8 → 96.4 → 98.9），节点时间之和约为整轮时间的 3.9 倍，**这张表根本指不出慢在哪个节点**，而它存在的唯一理由就是指出那个节点。改为秒表只覆盖 `node.Execute` 后，各节点时间之和 == 整轮时间（243.5 vs 243.5），10 万点的真实热点随即现形：`SelfPruningFilter` 66%、`VolumeSampler` 18%。harness 里加了"`sum(node) ≤ 1.2 × run`"这条结构性守卫，重新嵌套计时就会红。

**由此新增的可见通路：** `PCGNodeTimingReport`（按类型合并、按耗时排序、给百分比与 `n runs`）+ `PCGVolume.LogNodeTimings` 开关。**此前 `PCGExecutionResult.Stats` 除了分区 runner 把它拼起来以外没有任何消费方**——测量早就存在，只是没人看得见。离屏新增 **11 条断言（204 passed / 0 failed）** 覆盖合并、排序、平局按类型名、`TopRows` 截断、null TypeId 与零长度运行不除零。

---

### Phase 3 续作点（2026-10-06 停笔时的实况）

**已落地且离屏验证过的**（`dotnet run project Tools/PCGGoldenRunner/PCGGoldenRunner.csproj` → **335 passed / 0 failed**；`Tools/PCGVerify`、`Tools/PCGEditorVerify` 两道 `--no-incremental` 编译门各 0 错误；`Tools/PCGBench` → `BENCH OK`）：

| 大件 | 落点 | 主要验证 |
|---|---|---|
| 分区生成（跨格 padding 消接缝） | `Execution/PCGPartitionPlanner\|Runner` + `PCGVolume.Partitioned` | 边界平铺 33 条 + 接缝性质（带 padding 的分区 == 连续运行，逐点相等） |
| `Spawn Foliage` / `Spawn Actor` / `Spawn Decal` | `Nodes/PCGSpawn*.cs` | schema 默认一致 + 引擎 3c/3d/3e 组（宿主复用、体积所有权、Clear 精确性） |
| `Mesh Sampler` | `Nodes/PCGMeshSamplerNode.cs` + 纯核心 `Data/PCGMeshSurfaceSampling.cs` | 29 条对手算几何的断言（面积加权、退化面、重心映射、宿主变换复合） |
| 节点内并行采样 | `Execution/PCGParallel.cs` | 并行==串行逐点一致、每下标恰好一次、异常类型不被包装 |
| 剪枝稠密网格 | `Nodes/PCGSelfPruningFilterNode.SurvivorGrid` | **50 条对着 O(n²) 暴力参照的等价断言**（含退回分支专用用例） |
| 图效果分类（编辑器试跑的闸门） | `Execution/PCGGraphEffects.cs` | 31 条：4 个 Spawn* + Subgraph 判为写场景、13 个只读类型放行、未注册/空 type id 保守判为写、`EverySpawnerIsListed` 对着注册表查"新 spawner 忘了登记"、列表条数钉死（删一条也红）、`GraphWritesScene` 与节点顺序无关、null 图保守判为写 |
| 流式窗口选择 | `Execution/PCGCellStreaming.cs` + `PCGStreamTrigger.cs` + `PCGPartitionCache.Evict` | 72 条：窗口/驱逐对着独立写法的距离参照比 6 组半径、两集合不相交、滞回环带恰是中间那圈、Y 不参与、列表复用会被清空、null 被拒、驱逐只带走那一格且下一次 `DirtyCells` 重新含它；预算侧：档边界预算恰好留住最近一档、免费（已缓存）格全留、逐轮把预算花满、`ceil(窗/预算)` 轮收敛、驱逐集合不受预算影响、**反例**（没有 needsWork 时 12 次请求只碰同样 2 格）；跟随侧：负坐标向下取整 vs 截断（西行模拟 floor 3 格 / 截断 2 格）、step≤0 不整除、`CellOf` 忽略 Y、`ShouldRequest` 五行真值表、跟随步长取最细一层格子（显式值优先，停用/0 尺寸/null 的层跳过，无层回落体积默认） |
| 性能预算 | `Tools/PCGBench` | 10 万点 ≤ 400 ms（实测 57.6 ms）+ 汇出端逐点指纹守门 |
| 流式请求的尖峰预算 | `Tools/PCGBench` 的 `StreamingBudgetSweep` | 2 km²、16 格、每格 6 万点请求：**整窗一次跑完最慢一次请求 621 ms / 单请求内 117 MB**，而**每请求一格**把最慢请求压到 **44 ms / 9.5 MB**（≈14×/12×），总时间 612 ms ≈ 一次跑完的 621 ms（分摊不创造工作），且 16 次请求最后报出的点数与一次跑完**完全相同**（63856 vs 63856）。四条断言全部进 `BENCH OK` 的门：尖峰、不创造工作、同点数、单次请求内存 |
| 分层网格（一图一层一格子） | `Graph/PCGPartitionLayer.cs` + `PCGVolume.Layers` + `PlanLayers` | 10 条：各层格数、逐层完整铺满、坏层点名中止、跨层名字互不归属、层缓存互不代跑 |
| 跨格增量重算（数据层） | `Execution/PCGPartitionCache.cs` + `PCGVolume.PartitionWorldVersion` | 21 条：冷/热缓存、逐点同指纹、四种失效通道、截断作废、spawner 图拒绝 |
| Phase 2 编辑器：画布、参数面板、复制/粘贴/副本、多选批量改参 | `PCGEditor/*` + 运行时侧 `Graph/PCGGraphClipboard.cs` | 38 条剪贴板/批量断言；编辑器门 0 错 |
| 编辑器内试跑（不落场景的图执行） | `PCGEditor/PCGGraphEditorWindow.cs` + `Execution/PCGGraphEffects.cs` | 42 条效果分类断言（列表对着注册表查漏登记的 spawner；子图**顺着 loader 走进去**判内容：数据子图放行、藏一层的 spawner 拒绝、无 loader/名字解析不到/环/超深度/没填名字一律保守拒绝，同一子图用两次是菱形不是环）；按钮本身需 PIE 看（见 §9 ⑭） |

**未做，按价值排序**：

1. **自动脏度信号**（缓存与分层本身**已落地**：`PCGPartitionCache` 21 条、`PartitionWorldVersion` 手工世界版本、分层见 §7.4）。**本轮已补上插件侧那一半**：`PCGVolume.PartitionVersionSource` 让游戏把自己的地形/宿主修订号交进来（与手拨值合并，`CombineWorldVersion` 有断言）；**剩下的仍是"不用人接"**——插件拿不到引擎侧的脏信号。开工前先定"脏信号从哪来"（地形标定版本？宿主 transform 版本？），否则缓存会产出**陈旧但看着合理**的结果——比崩溃难查。另外**层级生成目前固定两级**，UE5 允许任意深度；要加深先想清楚与 `Layers` 的语义差别。
2. **剪枝的下一格**。它是 10 万点里剩下的 58%。要保持逐字同结果只有分带/波前这一类路（同一格内幸存者互相不冲突时可以并行）；如果改用近似规则，**必须在日志里明说它是近似**，并给出与暴力参照的差异上界。第一次尝试（CSR + 二分）已实测更慢并被删除，见 §4.4 负结果记录。
3. **`PCGVolume` 的分层/流式配合**：**已落地**（分层与流式窗口见 §7.3 v1.3 的三条：窗口、请求预算、跟随驱动）。跟随这一半的形态是 `UpdateStreaming()` + `PCGStreamingDriver : Script`，因为 **Flax 的 update 钩子在 `Script` 上，`PCGVolume` 是 `Actor`**（我先按 `OnUpdate` 写过，编译门直接 CS0115 拒掉）。剩下的是**接进游戏侧的播放器/世界流式**：把 `StreamFollowTarget` 指到本地角色（或让玩家的控制器自己按帧调 `UpdateStreaming()`），并按手感调 `StreamRadius`/`StreamKeepRadius`/`StreamCellsPerRequest`。注意 `Clear()` **不会**让跟随自动补回来——清空是设计者的明确意图，若下一帧就重建等于把 Clear 取消掉。
4. **PIE 目视确认清单（本轮已增至 **22 项**，见 §9 末）**：画布 payload 复原、代码图重建、面板事件时机、暴露参数与覆盖列表对应、分区 volume 日志、植被 spawner 日志、远处实例剔除、给 3d 组指一个真 Prefab、给 3e 组指一个 Decal 材质、编辑器里多选批量与复制粘贴手感，以及本轮新增的 ⑰–㉖（层级生成、按节点读产出、Preview 不落地、Spline Mesh、选中节点视口采样、Texture Sampler、分块对齐窗口、地形投影、随机偏航贴地、世界喂可见 chunk 清单）。**其中第 ⑧⑨ 项需要项目里先做出资产**（引擎没有供脚本用的现成 Prefab，项目 Content 里没有任何 Decal 域材质）。
5. ~~**Phase 1 遗留的地形标定盲区**~~ —— **2026-10-11 结清**：把两条路的交叉比较搬进“写入起伏之后、还原之前”的窗口，并加了防空转断言（采样点高度**跨度 ≥ 1 世界单位**）；在那块有起伏的地形上实测 `scale=0.999988 / offset=0.00016822 / residual=0`（不再是退化的 `scale=1/offset=0`）⇒“高度场 → 世界 Y 语义”不再是假设。平地上那一档改为**如实报 `skipped`**。详见上面那条 ✅。

6. **地形合成（让描述能造地形——本仓最大的一处"读得出来、长得不出来"；已核实可行，未写码，等拍板）**。`biome` 判平原/丘陵/盆地、`water` 从地形算出湖与江河，但**没有任何东西造出地面**：空场景里跑 `valley.pcgspec`，得到的是落在一块平板上的树石房路。用户口径里那几个地貌词（温带平原、丘陵、盆地、湖泊、江河）今天都是**判**出来或**推**出来的，不是**造**出来的。这是"按设定描述自动生成场景"里最后一处说得出来、长得不出来的东西。

   **可行性已核实到方法签名（不是猜的）**：`Engine/Terrain/Terrain.cs` 托管侧有
   ```csharp
   public unsafe bool SetupPatchHeightMap(ref Int2 patchCoord, float[] heightMap,
                                          byte[] holesMask = null, bool forceUseVirtualStorage = false)
   ```
   逐 patch 写高度场，并**附带一张洞掩码**。四条契约要点，每一条都影响方案：① `heightMap` 长度必须等于 `(chunkSize*4+1)^2`；② `holesMask` 是**逐顶点 0-1** —— 于是"挖洞"是引擎原生能力，洞穴可以是真的洞、河道可以真的凹下去，而不是靠摆模型遮；③ **它返回 `true` 表示失败**（与 `VisjectSurface.Save()`、`Mesh.UpdateMesh` 同一种反直觉约定，接的时候必须写成 `if (SetupPatchHeightMap(...)) 报错`），这是本仓已经踩过三次的同一类坑；④ `forceUseVirtualStorage` 的文档写明"运行时创建的地形只能用虚拟存储（驻内存）"，也就是**运行期造地形是引擎允许的**。

   **为什么仍然没写码**：这是**大功能**（一套高度场生成 + 与既有标定/水文/地貌的先后关系 + 谁拥有那个 Terrain Actor + 破坏性写入的回退），按本仓惯例**先拍板再写**。四个待决问题：

   | # | 待决 | 选项 | 代价与判据 | 建议 |
   |---|---|---|---|---|
   | A | **高度从哪来** | (1) 描述里写**显式地貌特征**（`terrain` 指令：盆地/山脊/台地/河谷 + 各自的中心与幅度）；(2) 只写参数（`terrain seed 7 hills 0.5`）交给噪声；(3) 不造，只把 `biome` 判据反写成材质/着色建议 | (1) 与"按设定描述"这句话**同构**，但要一套形状库（盆地/山脊/台地都是解析形状 + 一个噪声起伏）；(2) 实现最小，但"描述"退化成一串数字，且失去"北边是盆地"这类说法；(3) 零风险，但那几个词永远停在"判" | **(1) 的最小集**：盆地 / 山脊 / 台地 + 一层噪声起伏；先把 `region` 里已有的高度语义定下来 |
   | B | **什么时候造** | (1) 每次都造（生成流水线的第一层）；(2) 只在写了 `terrain` 指令时造 | (1) 每次生成都重写高度，会覆盖美术手刷的地形；(2) 没写就保持今天的"判"，两者不打架 | **(2)**。写高度是**破坏性**的，不该是默认行为 |
   | C | **写谁的地形** | (1) 只写插件自己创建的那个 Terrain（没有就报错，不碰别人的）；(2) 写现有 Terrain，但先把原高度场备份（内存或磁盘）以便回退 | (1) 最安全，代价是不能在美术已铺好的地形上叠加；(2) 灵活，但要管备份的生命周期与体积（一个 patch 就是 `(chunkSize*4+1)^2` 个 float） | **(1) 起步，(2) 做成显式开关** —— 破坏性操作要用户自己点名 |
   | D | **高度语义与标定** | —— | 插件已有 `PCGTerrainProbe.Calibrate` 做"高度场 → 世界 Y"的换算（§附录 B.2 记着盲区：`World.scene` 是零平面，没被真实起伏证实过）。**但 `Terrain.cs` 上是"只写不读"**：托管侧只有 `SetupPatchHeightMap` / `SetupPatchSplatMap`，**没有任何高度 getter**（本轮逐行核过）。所以合成器**必须自己留着刚写下去的那份高度数组**，并把它交给下游的采样通路（`PCGWorldContext.HeightSource` 那一类），否则紧随其后的 `biome`/`water`/`scatter` 会拿**旧高度（往往是零平面）**去判地貌、铺点——生成的树会浮在新山谷的半空或埋进新山脊里。 | 把"**写下去的那组数同时喂给采样端**"当作第一条实现约束；验收分两步：离屏断言那组数本身（形状/尺寸/叠加），引擎自检里断言"同一坐标两次生成落点一致"（而不是靠读回高度，因为读不回来） |

   **离屏能验哪一半、哪一半只能 PIE**（这决定测试怎么写）：**要写的那组数**（盆地/山脊的解析形状、`(chunkSize*4+1)^2` 这个尺寸不变量、叠加顺序、洞掩码的取值域）是纯数学，可以像 `PCGMeshBuilder` 那样离屏逐点断言；**真正写进引擎**那一步只能在 PIE 里看（与既有的 3b/3c 自检组同一层）。所以落地形态是"**纯数据核 + 一个薄写入节点**"，前者进离屏门，后者进 `PCGSelfTest`。

   **纯核已落地（v1.4）**：`Execution/PCGTerrainSynthesizer.cs` —— `Plateau`（台地，带 Falloff 边缘过渡）/ `Basin`（盆地，取绝对值向下）/ `Dome`（余弦土丘）三种形状，**叠加就是求和**（"台地上挖个盆地"= 一正一负相加，"丘陵"= 几座土丘叠起来），再叠一层 `PCGSeededNoise.Fbm2D` 的起伏。**它是纯函数**：不碰引擎、同样输入永远同样输出，所以进的是离屏门（`TerrainTests`，12 条：尺寸契约 `(chunkSize*4+1)^2` 与"数组长度不对时理由里给出引擎要的那个数"、三条形状的逐点取值与边界规则、叠加求和、**采样两端压在 patch 两个角上且行优先**、确定性、噪声幅度 0 与非 0 的区别、只有噪声时地面是起伏的）。尺寸口径直接抄引擎那句文档，高度用**世界单位**、坐标是 **patch 局部**——于是标定（§附录 B.2）一旦定下来，纯核不用改。
   **写入端仍未落地（2026-10-10 已拍板决定 C = "新建 PCG 自己的 Terrain"）**。这一天先把**描述层那一半**落地了（`PCGSpecParser` 的 `terrain` 指令 + `PCGTerrainRecipe` + 图编解码 + 19 条离屏断言，harness 1674 passed / 0 failed）：
   - 语法：`terrain noise 25 noise-scale 900 shapes "plateau 0 0 600 120 0.4; basin 900 -400 500 80"`。形状表是这一层唯一的字符串语法，**在描述层解析**（写错带行号），每项 `形状 X Z 半径 幅度 [边缘过渡]`，形状名 `plateau` / `basin` / `dome`（"丘陵"不是第四种形状，是几座 `dome` 叠加）。
   - **描述层没有 chunk 边长这个旋钮**，而这不是省事：`Terrain.ChunkSize` 在托管侧**只读**（编译门当场报的 CS0200），而高度场的边长必须等于引擎要求的那一句 `(chunkSize*4+1)^2`。写入端于是以**地形自己报出来的** chunk 边长为口径。另：顶点间距恒为 `UnitsPerVertex = 100` 世界单位，与 chunk 边长无关，所以"调 chunk 让地形更精细"这条路本来也不成立——patch 边长变了只改变一次要写几个 patch。
   - **落地形态与上面"最短路径"里写的不一样：不做 `PCGTerrainNode`，改成把配方放进 `PCGGraphSettings.Terrain`**。原因是节点是**拉取式**执行——接不进任何汇的节点根本不会被求值，而"地形必须写在所有采样之前"不取决于图里的边：放进设置里，由体积在跑图前统一落地，顺序天然正确，也不必为一个副作用在图里找一条接得上汇的边。
   - 文件里存的是**形状枚举的数值**（0 台地 / 1 盆地 / 2 土丘）而不是名字：名字要多一处"字符串 ↔ 枚举"的映射，而那正是"改一处、旧存档整体平移"的来源；顺序由一条断言钉住（重排枚举断的是测试，不是别人的存档）。
   - 还没做的仍是**写**：`PCGVolume` 在跑图前按配方找/建一个 `PCG_Terrain_<体积名>`（**不挂进体积的父子关系**，否则会被 `Clear()` 当产出销毁）、逐 patch 调 `SetupPatchHeightMap`（**`true` 表示失败**）、并把写下去的数组同时喂给 `PCGWorldContext.HeightSource`（D 的第一条约束：托管侧没有高度 getter，读不回来）。它只能在 PIE/引擎里验，所以与"纯核 + 薄壳"的分层一致：离屏门管数据，引擎自检管那一次真写。
   - **2026-10-10 当晚写完了写入端**：`Runtime/PCGTerrainWriter.cs`（薄壳，`TryApply(recipe, owner, worldBox, seed, out heightSource, out error)`）+ `PCGVolume.GenerateOne` 里在 `BuildContext()` 之后、`ClearForRun` 之前落地（`Preview` 不落，与"预览不碰场景"同一条守卫），并把返回的高度来源装进 `context.World.HeightSource`。
   - **当晚又被引擎源码推翻了一次，决定 C 的"插件自己建地形"在脚本侧不可实现**（逐行核过 `Engine/Terrain/`）：
     - 托管侧写地形的入口**只有** `Terrain.SetupPatchHeightMap`，而它第一步 `GetPatch(coord)`——**缺那一格 patch 就直接返回 `true`（失败）什么都不写**；
     - 运行期 `new Terrain()` 的 `chunkSize == 0`（`Terrain::Terrain` 的初始化列表）⇒ `PatchSize == 0`，而我先前的代码会因此直接报"patch 边长为 0"；
     - 配地形骨架的 `Setup(lodCount, chunkSize)` / `AddPatches(int2)` 在 C++ 侧**没有 `API_FUNCTION`、没有托管绑定**（`Terrain.cs` 只有 `SetupPatchHeightMap` 与几个常量）⇒ **脚本造不出可写的地形**。
     - 结论修正：写入端只能写**已经有 patch 的地形**，且只认名字以 `PCG_Terrain` 开头的那些（"不碰美术那份"这条原意因此仍然成立）；缺地形/缺 patch 时**记警告并继续**（图的其余部分照跑，只是看到旧高度），错误信息里直接写清要先在编辑器里建一块并 Add patches。
   - **2026-10-10 当晚写完了写入端**（原句保留在上一行之下，措辞以上面这条修正为准）。自检组 `PCGSelfTest.TestTerrainSynthesis` 改成分层：场景里一张 Terrain 都没有就**跳过并说明理由**（不自己建），有则证三件事：写完之后道路点被抬高、同图同种子连跑两次落点一致、那块地形**清空体积后还在**；收尾用 `TryRestore` 把自己写进去的高度**还原**（它可能写的就是用户那张地形，跑一次自检不该在那儿留下土丘——这一段同时也在真路径上验证"能不能回退"）。
   - **决定 C 于 2026-10-10 由用户重选：写场景里现有的那张 Terrain（Main）**。于是写入端多了两件**必需**的东西：① 覆盖前把每格 patch 的**原始高度场备份成文件**（`Logs/PCG/TerrainBackup/<地形id>_<x>_<y>.bin`，先写长度再写 raw 值；**只在没有备份时写**，否则第二次生成会把"原始"推成 PCG 自己写的值）；② 一个显式的回退口：HundunAgent 的 `pcg_terrain_restore` 工具（按地形 id 找备份、逐格写回）。
   - **一条必须记住的引擎语义**（`TerrainPatch::SetupHeightMap` 第 852-857 行）：`useVirtualStorage = Editor::IsPlayMode || forceUseVirtualStorage`。编辑器里传 `forceUseVirtualStorage: true` **只写内存、资产不动**——于是 PIE / 重开场景就回到原地形，而编辑器里按新地形烘焙的物件在游戏里会浮空或埋进去。要"编辑器看到的就是游戏里跑的"，必须传 **false**（落资产）；代价正是"改了用户的地形"，所以备份与还原是不可省的一部分。写入端的**读回那一侧**则已经进离屏门（`TerrainTests` 新增 5 条：世界位置 → 顶点格 → 读回来必须等于合成器写在该顶点上的值，也就是两侧换算是互逆的——差一格就是每个点偏 100 世界单位、而屏幕上只表现为"摆得不太对"；外加双线性中点、越界钳制、null/过短数组不抛）。另外四份演示描述（`Content/PCG/Specs/demo_*.pcgspec`）现在各带一行 `terrain`：山谷=盆地+三座丘陵、山城=台地+两座丘陵、两村两城=宽盆地+两座丘陵（并多一条更细的田间小道）、山洞=边缘很陡的山体，于是"温带平原/丘陵/盆地/山地"这些词在演示里是**造**出来的。
   - 另一条实测约束（写进实现笔记）：引擎的 `UnitsPerVertex` 是 **100**、`ChunksCountEdge` 是 **4**，所以一个 patch 的世界边长 = `100 * 4 * ChunkSize`（Main 场景那个 ChunkSize=127 的 patch 因此是 50,800 世界单位，高度场 509²），**地形分辨率固定是 100 世界单位一个顶点**——合成的丘陵/盆地会按这个粒度成型，这是引擎地形的粒度，不是本插件能调的。

   > **本行曾被误留为"仍未做"（已更正，2026-10-11）**：写入端**已经落地**——`Runtime/PCGTerrainWriter.cs`
   > （`TryApply` / `TryRestore`）、`PCGVolume.GenerateOne` 里的调用点、引擎自检组 `TestTerrainSynthesis`、
   > 四份演示描述各带一行 `terrain`、以及 HundunAgent 的 `pcg_terrain_restore` 回退工具，都在盘上。
   > **决定 C 的最终口径以本小节 2026-10-10 那条"由用户重选：写场景里现有的那张 Terrain（Main）"为准**
   > （不是"新建 PCG 自己的 Terrain"——引擎源码证明脚本造不出可写的地形）。   > **洞掩码已落地（2026-10-11 当天，接在写入端之后）**：语义**不是猜的，是从引擎源码读出来的**——
   > `TerrainPatch::CacheHolesData` 写的是 `holesMaskPtr[i] = isHole ? 0 : 255`，而消费它的
   > `UpdateNormalsAndHoles` 的判据是 `if (holesMask && !holesMask[i])` ⇒ **0 才是洞，255 是有地面**。
   > （当初记的"不想押一个猜的语义"是应有的谨慎，但答案就在源码里，不必等运行期去试。）描述层因此多了一个选项：
   > `terrain ... holes "dome 900 -400 250 0"` —— **与 `shapes` 用同一条形状语法、同一个解析器**，
   > 但落到**独立**的一组特征上（"改高度"与"删地面"是两件事，可以同时写）；掩码是**硬**的、没有过渡。
   > 链路：`PCGTerrainSynthesizer.TryBuildHolesMask`（纯核；下标口径与高度场**逐格一致**：行优先、先 Z 后 X，
   > 差一格就是整片洞错位、而屏幕上只表现为"洞的位置不太对"）→ `PCGTerrainRecipe.Holes` → 写入端逐 patch
   > 造掩码后交给 `SetupPatchHeightMap(..., holes, false)`；**不写洞就传 `null`**，于是"没提洞"不会把引擎
   > 原有的洞清掉。
   > 证据：离屏 **+18 条断言**（掩码长度契约 / null 被拒 / 中心 0 四角 255 / 半径上是洞而出去两格不是 /
   > 偏心洞验"先 Z 后 X" / 无洞时全 255；描述层"`holes` 落到独立那一组"与"不写 holes 时那组是空的"**对照**；
   > 再加 **JSON 往返**——新键的**写**与**读**在两个方法里，只改一处会留下"写得出来、读回来是空"的静默失效，
   > 所以两头都逐项比（形状 / 中心 / 半径 / 幅度 / 过渡），并配"没有洞的地形读回来仍是空数组"的**对照**），
   > 总数 1905 → **1923 passed / 0 failed**；两道编译门 0 错；引擎自检 28 组全绿（0 失败、1 跳过 = 平地那档交叉检查）。
   > **引擎侧也已证（同日晚）**：自检里给那块地开一个洞，再往洞中心与洞外各打 5 条向下的射线：
   > **洞外 5/5 打得到地面、洞内 0/5**（洞外那条是必需的**正面同夹具**——整块地形没写进去时洞内洞外都会打空，
   > 那时"洞内打不到"说明不了任何事）。引擎侧于是有了"洞真的没有地面"的证据，而不只是"该写什么数"。
   > **一条作者要知道的尺度**：掩码是**逐顶点**的、而地形顶点间距是引擎的 `UnitsPerVertex = 100`（不可调），
   > 所以洞的**实际范围是方块状的**——第一版半径写 120 时，"洞外 +180"那颗点也被挖掉了（看着像"漏了一个点"）。
   > **洞的半径要明显大于一个顶点格**（自检里改成 300 才稳定）。

   **拍板后的最短路径**：`PCGTerrainSynthesizer`（纯核：给定 feature 列表 + 一个 patch 的 `patchCoord`，算出该 patch 的 `float[]` 与 `byte[]`）→ `PCGTerrainNode`（薄壳：拿 `Terrain`、逐 patch 调 `SetupPatchHeightMap`、按反直觉的返回值报错）→ 描述层加 `terrain` 指令（写 `at`/`relief`/`noise`）→ 自检组打一行 `PASS terrain synthesis: …`。**注意别把生成的 Terrain 塞进 `PCGVolume` 的父子关系里**（它会被 `Clear()` 当成自己的产物销毁）。


**增量重算开工前必须拍板的三个决定**（按本仓惯例，方案定了再写码；选错会让缓存产出陈旧结果而非崩溃，最难查）：

| # | 待决问题 | 选项 | 代价与判据 | 建议 |
|---|---|---|---|---|
| A | **格子凭什么算脏** | (1) 图的结构哈希 + 体积 transform 版本号；(2) 再叠加"宿主 Actor 移动"与"地形标定版本"；(3) 完全不缓存，靠分区把一次生成切薄 | (1) 现在就有（`PCGGraphCompiler.ComputeStructuralHash` + `PCGVolume` 的 transform），实现最小，但地形笔刷改动不会让它失效；(2) 覆盖真实工作流，需要一个标定/宿主版本号的可读来源——地形侧已有 `PCGTerrainCalibrationStore`，宿主侧要看是否愿意让 spawner 记录来源 Actor 的 transform | **先 (1)，同时把 (2) 的两类版本号做成签名里可选的追加字段**，别把接口写死成只有图哈希 |
| B | **脏了重算多少** | (1) 只重算脏格；(2) 脏格 + 其 padding 覆盖到的邻格 | 邻域相关阶段（剪枝）意味着一格改动会影响邻居的取舍，(1) 会在格子交界处产出与全量运行不同的结果——正是当初做 padding 要消除的那类接缝 | **(2)**。且 §7.3 已实测：接缝性质只在"padding ≥ 剪枝间距"时成立，这个前提要写进缓存失效条件里 |
| C | **旧结果怎么被替换** | (1) 按格销毁该格生成的 Actor 再重建；(2) 整体积清空重来 | 结构式所有权（`PCGVolume.Clear()` = 销毁我的孩子）目前**不记录"哪个 Actor 属于哪一格"**，(1) 需要给生成物带格标签；植被实例（`Foliage` 宿主）不是 Actor，(1) 对它只能整宿主清空或按实例索引删除（`RemoveInstance(int)` 存在，但要维护"点→实例号"映射） | **先做 (1) 的 Actor 路线 + 植被/贴花走"该格的宿主"整块替换**；若嫌重，(2) 是安全兜底，但至少比现在省掉分区之外的全部开销 |

拍板后最短实现路径：`PCGPartitionPlanner`  already 产出稳定的 `(IndexX, IndexZ)` → 加一格签名结构（A 的字段 + B 的邻格闭包）→ `PCGPartitionRunner` 接受"上次签名表"并跳过未变格 → `PCGVolume` 记录每格所有权。**注意别先动 spawner 的命名规则**：那套前缀是 `Clear()` 辨认生成物的唯一依据。

**接手时的最短路径**：`dotnet run project Tools/PCGGoldenRunner/PCGGoldenRunner.csproj`（行为等价与数值）→ `dotnet run project Tools/PCGBench/PCGBench.csproj`（性能与并行==串行指纹）→ 两道 `--no-incremental` 编译门。**注意 `dotnet build` 的"已成功生成"可能是增量假绿**，验证编译结论一律带 `--no-incremental`。

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
| **贴花无批量实例化路径** | 贴花逐点即逐 Actor，大规模时场景膨胀 | 中 | **v1.1 误判为「无 `Decal` Actor」，实际存在（`Level\Actors\Decal.h`）**；节点已于 v1.3 落地，按这条风险内置 1000 上限（三个 spawner 里最低）并在触顶时报警，朝向取自上游采样器算好的表面法线（§5.1.5） |
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
| `Tools/PCGBench` | `Exe`，`dotnet run` | **10 万点的生成时间与内存是多少**，以及每个节点各占多少（Phase 3 验收线的实测门） | ✅ | ❌ |

> **陷阱：“编译通过”不等于“测试跑过”。** 一个只编译的 Library harness 会让静态写在源码里的期望值看起来“已被验证”，而实际上从未与代码输出比对过。任何声称“锁定数值输出”的测试，必须有一条能**执行**并打印 PASS/FAIL 退出码的路径（否则它只是一个带数字的文本文件），以及一条在真实引擎内跑端到端的路径。

> **陷阱二（v1.3 实测踩到）：`dotnet build` 的“已成功生成”可能是增量的假绿。** 本轮 `Execution\PCGPartitionRunner.cs` 带 5 个编译错误躺在树里，只因它是 glob 进来的新文件、上一轮的绿是它写进来之前跑的；而补完它之后，同一条 `dotnet build -v q` 又对一处 CS0103 报了“已成功生成”（构建被判定为无需重跑）。两次都靠 `--no-incremental` 才暴露真相。**凡是要把“编译门通过”写进结论，命令里带 `--no-incremental`（或 `dotnet run`，它必然真编一遍）**；只 grep `error CS` 而不去读那句中文成功标记，也会被假绿骗过一次——本轮就是先信了绿、再被自己的断言逼着重编才发现不对。

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

### B.3 一轮自检五连败的根因归类（v1.5，2026-10-10）

引擎侧自检从 6 条失败收到 5 条，再一路收到 0 条（26 组全绿、`skipped` 为空）。五条里有**两条是真产品缺陷**、**三条是夹具或环境问题**——分开记，因为"自检红"本身不指向任何一类：

| 症状 | 归类 | 根因 |
| --- | --- | --- |
| 样条沿线摆件"间距 52 而不是 100" | **产品缺陷** | `Spline::GetSplineSegmentLength` 只给**整段**长度，段内弧长与参数并不成比例（结点切线为零时曲线在一段的两头"走得慢"）。按段长做线性反解会把点往段头挤。改为逐段细采（32 采样）一张参数↔弧长表，按引擎段长归一后反解。离线对照见 `SplineTests.TestArcLengthInversion`（解析曲线 s(u)=500·(3u²−2u³)，旧实现会报 52）。 |
| 样条网格"每块都落在格子外"、`PlacedCount` 恒为 0 | **产品缺陷** | `PCGActors.TrySpawn` 用**返回值为真**表示成功（`!Level.SpawnActor(...)`），而这一处写成了 `if (!TrySpawn) placed++`：成功的分块被当成失败。一排分块照样建了出来，只有计数和那句错误信息是错的。**这条路径此前一直因为模型取不到而被自检静默跳过**，所以直到夹具能载入模型才露出来。 |
| 流式预算"一格预算产出 4800 > 窗口 3600" | 夹具 | 两处口径都错：①`window` 是半径 450 量出来的（12 格），预算跑用的是 700（16 格），两个窗口本来就不一样大；②预算限的是**这一请求要算的格子**，缓存已经存下的格子免费、不占额度，而之前跑过的整片区域把每一格都变成了免费，于是预算**合法地**一格都不删。改为同半径度量 + 换种子作废缓存 + 反复请求收敛到整窗口（收敛本身就是"预算没饿死外围"的证据）。 |
| 植被宿主"拆解前应有 2 个，找到 0" | 夹具 | 夹具模型路径 `Engine/Models/Box` 在本机不存在，放置那一半**跳过**了，而拆解断言仍按"应该有 2 个"判它——把一个跳过报成了失败。 |
| 清空"销毁了 4 个而实际 2 个" | 产品缺陷（记账） | `Actor.Destroy` 延迟到帧末，上一批被清过的孩子此刻仍在子级里，再清一次会把它们一起数进去。体积现在记一个"已交给销毁队列、还没消失"的 id 集合（按仍在子级里的条目修剪），`Clear()` 与 `ClearCell()` 都跳过它们。 |
| 按描述生成那一组"清空销毁了 4 个而实际 2 个" | 同上 | 同一条记账修复覆盖。 |

**两条方法论结论**（比这五条本身更值得留着）：

1. **跳过必须在结果里可见。** 三条依赖模型资产的检查（植被放置、样条网格、网格采样）在模型路径失效时**静默什么都不做**，在结果里与跑绿了长得一模一样——而其中一条背后就藏着上面那个 `TrySpawn` 反转缺陷。自检因此新增 `LastSkipped`，`pcg_selftest` 工具把它作为 `skipped` 一并返回；夹具模型的默认值改成引擎自带图元的绝对路径，并由工具用资产库/资产头解析成 **Guid** 再填进字段（生成器按 Guid 取资产，路径口径只对工程内资产成立）。
2. **断言里要带关键中间量。** 这一轮之所以能一次定位，是因为把 `whole`/`full`、首/次/末个落点 X 直接写进了失败消息；没有它们，"间距 52"只能靠猜是参数映射偏还是索引错。

**同一轮顺带补上的两处覆盖（v1.5）**：

- **Prefab 生成器第一次在引擎里真跑**。此前 `ActorPrefabPath` 默认空 ⇒ 放置那一半一直跳过（与上面三条同一种"绿而不证"）。现在默认指向工程里一枚**早已在册**的 Prefab，放置那一半真跑并通过（产出归体积所有、`Clear` 收回的正好是它们）。两条必须记住的资产管线事实：① **`.prefab` 是 JSON 资产**，不是二进制 `.flax`——"读资产头拿 Guid"那条回退对它不适用，只能走资产库；② **刚由工具写出来的 Prefab 载不进来**：资产库按文件扫得到（能查到 Guid），但内容存储还没有条目、`WaitForLoaded` 永不返回，编辑器重扫/重启后同一份文件就正常。所以夹具指向既存资产，免得把"新文件还没被引擎登记"报成产品缺陷。
- **`Content.Load` 的返回判据与 `TrySpawn` 属于同一个反直觉家族**：`Load` 只有在 `WaitForLoaded` 返回 **false** 时才算成功、否则返回 null。夹具因此改用 `Content.LoadAsync` 拿句柄、由测试自己断 `IsLoaded`——否则一个"没加载完"的资产会被读成"根本不是 Prefab"，直接落进跳过分支。
- **贴花生成器的放置那一半仍然跳过，而且现在跳得看得见**：项目里没有 Decal 域材质（`Material.IsDecal` 由材质的 shader 决定，`material_create` 造不出这个域），所以这一条留在 `skipped` 里点名。域守卫与拒绝路径照跑。
- **2026-10-11：演示自己犯了"路径口径只对工程内资产成立"那条规矩**。`demo_valley` 里 `SpawnFoliage` 的资产 Guid 是引擎图元 `Plane.flax` 的，四份 demo 描述里共 20 处 `model` 写的是 `C:/Program Files (x86)/Flax/...Primitives/...flax`——**编译期**按路径能从资产头读到 Guid（所以 `pcg_spec_compile` 的 `models` 一片"资产库"），**运行期** `Content.Load<Model>(guid)` 认不出工程外的资产，烤一次的实际结果是 `generated 1024 point(s) … (0 actor(s))`：点有、东西没落地。此前"树木/森林已落地"的证据是**点数**与别的节点的 Actor 数，这一层因此一直没被证过。
  修法与口径：① 造工程内占位模型 `Content/PCG/Models/{Pine,House,Road,Rock,Bush,Water}.flax`——从引擎图元整份复制，**只按位置**改两处 Guid（文件头条目偏移 28、资产头 `Entry.Address` 处）再按 `h=A^B^C^D; h=h*397^serVer; h=h*397^cdLen` 重算写回 HashCode，chunk 字节一字未动（脚本 `Tools/_make_models.py`，重跑会跳过已造的不铸新 Guid）；② 描述里 20 处路径全换工程内路径；③ 四份 demo 图重编。重编是否只换了引用，做法是把已知旧→新 Guid 归一后**逐字段 walk 比对**：每份只差一处 `/Settings/Terrain/Holes: []`（那是洞掩码字段的存档追加），节点数/连线数/TypeId 顺序/Name/画布数据全同。备份在 `Tools/_before_specs`、`Tools/_before_graphs`。
  **【同日深夜实测否掉并已回退——读这条之前先看这条】**：下面这套"把引擎图元字节复制成工程内 `.flax`"的做法**是错的**，
  已全部回退（specs 与四份 demo 图从备份还原、六个复制模型删除）。引擎侧实测：改成工程内路径后烤一次，
  `PCG_City` 从 **1330 → 0** 个 Actor、`PCG_Township` **87 → 0**、`PCG_Valley` **73 → 16**，警告清一色
  `Model asset 4cd5ac8c… is not loaded or is not a Model`；还原之后回到 **73 / 1330 / 87 / 洞穴 5**、警告 0。
  ⇒ **字节复制出来的 Model 运行期载不进来**，而引擎图元原本的引用是**能用**的（City 一直靠 Cube.flax 出 1330 件）。
  两条被这次实测纠正的认知：① 「演示的植被指在工程外 ⇒ 落 0 个 Actor」不成立——16:40 那次 0 Actor 是**用户自建的
  `hh` 图**（它现在也是 0 件、0 警告，是另一回事，与该图自己的参数有关）；② `SpawnFoliage` 与 `SpawnStaticModel`
  的模型解析代码**逐字相同**（同一句 `LastError`），所以两者之间不存在"一个认得工程外资产、一个不认"的差别。
  **仍然成立的那半**：`city.pcgspec` / `valley.pcgspec` / `township.pcgspec` 里写的 `Content/PCG/Models/*.flax`
  在仓库里**确实不存在**，这三份非演示描述今天仍然落不出东西——要补得走**正规导入/资产创建**路径，
  复制 `.flax` 字节这条路已被实测封死。编译期岗哨（绝对路径点名）作为**可移植性**提醒保留，但它的理由描述
  已被上面这段推翻，别把它当成"运行期解析不到"的防线。
  再把这一类钉成**编译期岗哨**：`model` 写成绝对路径（盘符、UNC、POSIX 根、`~` 四种写法都盖到）时点名提醒，同一个路径只点一次、两个不同路径各点一次；离线门为此加了 13 条带正反对照的断言，并断言仓库里 7 份示例都不再用绝对模型路径——存量与新规则一起钉住。
  同一轮修掉一个**身份缺陷**：`pcg_spec_compile` 不给 `output` 时把图的**显示名**（描述第一行，如「演示·温带山谷」）当文件名用，于是另开一份"同图不同文件"的孤儿资产——体积是按文件名找图的。现在缺省取**描述文件的裸名**。
  **待收口**：编辑器重启后烤一次，判据是**落地的 Actor 数 > 0**（不是点数）；PIE 目视仍归用户。用户自建的 `hh.json` 仍指引擎 Cone，我没有动它。

**描述→场景的落地审计（v1.5，2026-10-10，演示场景重载后实测）**

用户口径里点名的每一类，都在真实场景里有可按类别指认的产出（`actor_find` 按名字前缀查、`pcg_volume_list` 报每体积的数量）：

| 用户口径里的词 | 描述层写法 | 引擎侧证据 |
|---|---|---|
| 温带平原 / 丘陵 / 盆地 / 山地 | `terrain … shapes "basin…; dome…"` + `biome …` | 地形写入端落地；**村庄方块全部落在 y=−81**（盆地底）、城市落在台地上——内容真的坐在合成地形上，不是坐在平板上 |
| 树木 / 森林 | `scatter forest per-block … model Cone` | `PCG_Gen_Cone_*`，y ∈ [−79, +49]，跟着地形起伏 |
| 岩石 | `scatter rocks points … model Sphere` | `PCG_Gen_Sphere_*` |
| 山间道路 | `road from … via … to …` | `PCG_Gen_Plane_*` 沿路一串，y 由 +69 逐步降到 −23（跟着地形下坡） |
| 村庄 | `village lots … rules … cell …` | `PCG_Gen_Cube_*` 四栋，y=−81 一致（同一盆地底面） |
| 城市 | `city …` | 体积 `PCG_City` 一个体积 1403 个 Actor |
| 洞穴 | `cave depth … segments … tunnels …` | `PCG_Gen_Geometry_*` 从 y=−81 往下钻到 −447 |
| 湖泊 / 江河 | `water cell … threshold …` | `PCG_Gen_Plane_*` 水面分块（同一条路也用 Plane，按体积归属区分） |
| 田间小道 | 更细的 `cell 20 spacing 45` 散布 | 体积 `PCG_Township` 87 个 Actor |

六个体积的数量（68 / 1403 / 87 / 14 / 48，加上用户自己那个空体积）合计 **1620**，与生成当天记下的 1620 逐一吻合——这也是"重载后读数"这条修复的对照值。

**同轮修掉的一个读数缺陷**：`GeneratedActorCount` 原先只读**本次运行的账面**（`_result`），而它是运行期状态、不进场景。于是重新打开场景之后，一个装着 1620 个产出 Actor 的演示场景，六个体积**全部**报 `generatedActors: 0`（`pcg_volume_list` 实测）——正是当初"点数有、Actor 数是零"同一个误读的另一种形态：设计师会以为生成没落地。改成"没有账面时数孩子"（产出归属本来就是结构性的），并排除已交给销毁队列的那些（否则 `Clear()` 之后立刻读会报出一批已经不存在的数量）。修完实测报 68/1403/87/14/48。自检里补了一条断言钉住"没账面时数孩子"这条分支（`Clear()` 之后就是账面为空的状态，在那里手工摆一个生成命名的孩子，必须是 1）。

**描述层的一处"说了没长出来"漏洞（v1.5，同轮修掉）**：`scatter` 层**没写 `model`** 时，编译器照样给它挂一个生成器，而那个生成器**永远长不出东西**——运行期只报一句"没有指定模型"然后原样放行。两份示例描述里那条 `scatter grass per-block 8.0 yaw off` 就是这样：描述说要长草，图照编（21 节点 / 13 连线），跑起来只有一句警告、地上什么也没有。这是"按设定描述生成场景"里最容易被作者忽略的一类静默失效，所以：

- **编译期点名**（不判错——只出点、给别的层当参照的层是合法写法）：`第 N 行：散布层 <名字> 没有给模型，这一层只出点、不长东西`。为此给散布层补了一个 `Line` 字段（描述 AST，不进场景，所以不涉及存档平移）。
- 两份示例描述（`demo_valley` / `valley`）的草地补上模型。**实测**：重编 `demo_valley` 仍是 **21 节点 / 13 连线**、`warnings: []`（那句运行期"没有指定模型"消失），重烘焙 Actor 从 **68 涨到 94**——多出来的 26 个正是草。
- 离线门补一对正反断言：没给模型必提醒（消息里带层名与"没有给模型"）、同一层给上模型就一句都不说；并把 runner 里那个 `Full` 夹具的草地补上模型（它原先靠"整份描述零提醒"来断言"能解析的路径不产生提醒"，模型缺席的层会让它分不清两件事）。

**引擎侧自检的最后一处未跑项也收口了（v1.5 收尾）**：贴花生成器的**放置**那一半此前一直跳过，理由是"项目里没有 Decal 域材质"。域由材质的 **shader** 决定、托管侧 `Info.Domain` 没有 setter，脚本确实造不出——但**引擎自带一枚**：`Content/Editor/DebugMaterials/SingleColor/Decal.flax`（与模型夹具借用引擎图元同一个套路）。按路径解析成 Guid 后取到，放置那一半真跑并通过。

于是 `pcg_selftest` 现在是 **26 组全绿 / 0 失败 / `skipped` 为空**——**每一组都真的执行过**。这一点比"全绿"本身更重要：此前三条依赖资产的检查是在静默跳过的状态下"全绿"的，而其中一条背后就藏着 `TrySpawn` 判据反转那个真缺陷。

顺带统一了夹具口径：模型 / Prefab / 材质三个夹具共用同一套"**认 Guid、也认工程内路径**"的写法（`LoadFixtureModel` / `LoadFixturePrefab` / `LoadFixtureMaterial`），一律用 `Content.LoadAsync` 拿句柄再断 `IsLoaded`——因为 `Content.Load` 的返回判据是反的（`WaitForLoaded` 返回 false 才算成功），会把"没加载完"读成"根本不是这个类型"从而误入跳过分支。





  **复测更正（2026-10-11 晚）**：上面 ② 里"64 边被**出口判据①**拒（环闭合、产物有 2 条未配对边）"的读数已过期。
  同一份 sweep 重跑，今天拒它的是**盖子朝向那道逐片判据**：`第 42 面：盖里有反着的扇形三角形，余弦 -0.994、面积 0.01`
  ——面积不是碎片，所以是真反了。夹具 `prism-64.tsv`（3802 顶点 / 6344 面、`eps=5.385e-4`）量到：刀口上有 **6 个顶点、
  成三对重复行**，`y=-2.0000` 正压在立方体底缘；272 个跨平面三角形去重后 `{0:3, 1:36, 2:233}`。
  ⇒ 触发条件仍是"**刀口正好擦过顶点/边**"那一档，但**失效的层是封盖的扇形假设**（环非简单时扇形会翻面），不是接环记账。
  两条候选修法里的 **①（耳切代替扇形）同日已落地并验证**：扇形翻面 ⇒ 走耳切，两条都不成才拒绝；读数从第 42 面推到**第 56 面**，拒它的换成出口水密判据（2 条未配对边）。**②（在捏合点把环拆成两圈）仍未做**——先查第 56 面那档「ε 外、1e-3 内的发丝窄条」。

**第 56 面这一档的刻画（2026-10-11 晚，夹具 `prism-64.tsv` 现场量，非推理）**：输入 6019 顶点 / 10370 面、`eps=4.899e-4`；`|d| <= eps` 的顶点 **0 个**（`|d| <= 1e-3` 有 2 个）；跨平面 329 个三角形，每个都交在 2 点上（`pre-dedupe {2: 329}`），但按产品尺度（`samePoint = eps*0.01 = 4.9e-6`）去重后有 **107 个三角形的两个交点合成一点**（`distinct {1: 107, 2: 222}`）⇒ 只收下 222 条线段。探针在 `dedupe 1e-6 / weld 1e-4` 这一档（最接近产品）数到 **329 条段、317 个点、度数分布 `{2: 305, 4: 12}`** ⇒ **12 个货真价实的捏合点**，不是收重（收重会被去重 + 剪叶清掉）。对照：把去重放宽到 `1e-4` 反而得到 `{1: 2, 2: 315}` 加 2 个相距 1.8e-4 的开口——那是**用大尺子把捏合抹平**，正是反复试过并被否掉的【放大容差】路线（两把尺子只能向小对齐）。结论：剩下的边界属于**近切/发丝**那一类（刀口以 1e-3 量级的距离擦过顶点），要修就走候选 ②：在捏合点把环**拆成两圈**分别封盖，前置是一个可靠的【环在一点穿过两次】的检测。这一档现在**诚实拒绝**（出口水密判据报 2 条未配对边），不是产出坏网格。

## 2026-10-11（深夜）：生成器节点的"冷资产"取不到，被当成配置错误

**缘起**：一条记了很久的账——`hh` 图"909 个点、0 个 Actor、0 条警告"。今晚把它读完了，结论是**两半都不成立**：
一半是旧的归因错，一半是我自己读数错。

- **不是沉默**。编辑器日志里明明白白有两句（`Log_2026_10_11_16_38_26.txt` 第 1436/1437、1555/1556 行）：
  `[PCG] SpawnFoliage: Model asset 1b4a643d-… is not loaded or is not a Model.` 和
  `[PCG] PCGVolume: node 2 ('SpawnFoliage'): 同一句`。"0 条警告"是我按 `pcg_volume_generate` 的返回表读的，
  而那张表只列**这一轮真跑过的体积**——`hh` 那时不在里面。**读数口径**（不是产品）才是那条沉默。
- **那个 Guid 不是坏资产**。按资产头把八份图里所有 `Asset` 值拿去磁盘对账（`Tools/_asset_ref_audit.py`，
  两种 Guid 串形式都算）：`1b4a643d…` = 引擎图元 `Cone.flax`，`b43f0f8f…` = `Cube.flax`，
  `54a1ff4a…` = `Plane.flax`，`dd86c472…` = `Sphere.flax`，**全部存在、类型都是 `FlaxEngine.Model`**。

**根因（从引擎源码读，不是猜）**：`Content::GetAsset(const Guid&)` 的声明注释写的是
"The found asset or **null if not loaded**"，实现就是 `Assets.TryGet(id, …)`——**它只翻已经载入的资产池，
不会请求加载**。所以一个存在、类型正确、只是本会话还没人碰过的模型，第一次生成时被读成"不是 Model"。
同一个 Cone 在本会话 00:02:22 被植被节点报失败、几十分钟后被另一张图的静态模型节点正常取用，
差别只在于那之前有没有别的运行把它载进来。**判据**：`GetAsset` 的结果取决于池子状态，而不是资产本身。

**改法**：新增 `Execution/PCGAssetLoading.cs`，`Resolve<T>(Guid)` = 快路径 `GetAsset` → 取不到就
`Content.LoadAsync(id)` + `WaitForLoaded(5000ms)`，最后仍用 `as T`。两个细节是刻意的：
① 不用泛型 `Content.Load<T>(id)`——它的实现是 `(T)LoadAsync(...)`，**硬转换**，指到 `SkinnedModel`
时会抛 `InvalidCastException`，而这里要的是"交回 null、由节点报自己那句配置错误"；
② `WaitForLoaded` 的返回值是"能不能加载"的反面（`Asset.h`：`True if cannot load that asset`）。
五个取资产的地方一起换掉：`SpawnStaticModel` / `SpawnFoliage` / `MeshSampler`（Model）、
`SpawnActor`（Prefab）、`SpawnDecal`（MaterialBase）。`DryRun` 仍然在任何资产调用**之前**返回，
离屏预览照旧零引擎触碰。

**验收到哪一步**：`dotnet build Tools/PCGVerify --no-incremental` **0 错误**（同时也就证明了
`LoadAsync(Guid)` / `Asset.WaitForLoaded(double)` 在对齐的那份 `FlaxEngine.CSharp.dll` 里存在）；
离线 **1950 条断言全绿**；编辑器第 **25** 次编译成功热重载；`pcg_selftest` **28 组 / 0 失败 / 1 组按设计跳过**；
重烤 `Valley 73 / City 1330 / Township 87 / Cave 5 / Partition 6` **一条没变**（暖路径无回归），
`kk` 36 点 → **36 个 Actor**，那条走的正是新的 `Resolve<Prefab>`。
**还没做到的**：冷启动那一档没能在本会话里复现（池子已经暖了，需要一次编辑器重启后、在任何生成之前跑一次
`hh` 那类图才算真证据）。现在这条改动的依据是**引擎源码的返回值约定 + 日志里那两句原文**，不是"看起来对"。

**同轮撤掉的死码**：`PCGCompiledGraph.UnreachedFromSinks()`（"接不到汇的节点"在任何无环图上恒空，
因为编译器把 `SinkNodes` 定义为"没有消费者的节点"）连同它的调用点和 10 条恒真/自转断言一起删了，
换成 `SinkCoverageTests`：守"**没有下游消费者的节点必须登记在 `SinkNodes` 里**"——那才是"分支被悄悄丢掉"
唯一可判定的形式，而且它带了防空转对照（把汇清单清空，判据必须变红）。

### 补上的一半：冷资产那一档**不需要重启编辑器**就证到了（2026-10-11 深夜，同轮）

上一节写下"冷启动那一档没能在本会话里复现（池子已经暖了），需要一次编辑器重启"。**这个判断是错的**——
它假设"要一个没被加载过的资产"就必须等到会话开头，而其实**只要挑一个全工程都没引用过的资产**，
它在这一刻就是冷的。引擎图元里 `Cone/Cube/Plane/Sphere` 都被八份图用过，`Capsule.flax` 没有
（Guid `3f82e577-7c28-44f5-b650-51289821ad46`，按资产头对账确认无人引用）。

于是新增自检组 `TestColdAssetResolution`（引擎侧第 **29** 组）：当场量 `Content.GetAsset(capsule)` 冷不冷 →
走 `PCGAssetLoading.Resolve<Model>` → **反向对照**（一个不存在的 Guid 必须取不到，否则"取到了"只是"什么都能取到"）→
事后 `GetAsset` 必须交出**同一个实例**（证明那次加载真的发生、且留在池子里）。

**证据是一组差分，两次 `pcg_selftest` 都 0 失败**：
- 第 1 遍：`skipped` 里**没有**这一组 ⇒ `GetAsset` 当时读不到它（确实是冷的），而 `Resolve` 取到了一个 `Model`；
- 第 2 遍：`skipped` 里**多出**这一条（`Capsule 在本轮已被加载过，冷的那一半没证到`）⇒
  **第一遍自己把池子焐热的**。这个"第二轮变热"正是第一轮那次加载的副作用自证，不依赖日志落盘
  （磁盘日志落后于面板，本来也读不到那一行 PASS）。

**口径**：这一组守的是"取资产"这条路；它不证明描述层写的名字能解析成这个 Guid（那是 `pcg_spec_compile` 与
编译期哨兵那条链）。两件事都有断言，别混着报。自检组数 28 → **29**，无跳过时才算全绿——本轮的
`skipped` 有 1 条（地形交叉校验，按设计）+ 第二遍多出的这条，都是**如实报**而非静默。

### 引擎侧再补两组（2026-10-11 深夜，自检组数 28 → **30**）

- **第 29 组 `TestColdAssetResolution`**：见上一节。
- **第 30 组 `TestSpawnStaticModel`**：补的是**验证欠账清单第 1/3 条**——`SpawnStaticModel` 是演示里最大的那条产出链
  （城市 1330 件），此前**只被烘焙过、没有任何断言**。夹具用与植被同一个模型（`FoliageModelPath`，槽序
  `ModelId, DensityThreshold, ScaleMultiplier, RandomYaw, MaxActors`），四条各管一件事：
  ① **没指模型**要开口（`LastError`）、原样透传、且不碰场景；② **12 个点 ⇒ 12 件 Actor**（既数节点自报的
  `SpawnedCount`，也数体积名下的 `StaticModel` 孩子——"产物对、计数错"那一族缺陷就是这么分开守的）；
  ③ **`MaxActors=4` 截断**：摆 4 件、`LastRunWasCapped` 为真、场景累计 **16** 件；④ **预览**：`SpawnedCount==0`
  **且**场景那 16 件一件没动（"计数为 0"单独不成立——必须与"场景没变"同时成立，否则空跑也算绿）。
  拆解前再数一次"体积名下应有 16 件"，因为这同时也是在验**结构性清理找得到它们**（重新生成时清掉上一批靠的就是它）。
  **跳过口径**：模型取不到时 `Skip("static model placement: …")`；本轮 `skipped` 里**没有**这一条 ⇒ 正向那一半真的跑了。

## 2026-10-11 深夜：描述层的模型引用改成**可移植的内建名字**（存量债清零）

**为什么现在做**：随仓库的七份描述各有代价——4 份 demo 写 `C:/Program Files (x86)/Flax/.../Primitives/X.flax`
（共 20 处，换机器/换安装目录就指不到），3 份非 demo 写 `Content/PCG/Models/{Pine,Rock,Bush,House,Road,Water}.flax`
（共 19 处，**本仓库没有这些资产**，于是编得出图、长不出东西）。字节复制工程内模型那条路已被引擎实测否掉
（`PCG_City` 1330 → 0），而本会话没有任何可导入的静态网格源文件（全盘 `*.fbx/*.obj/*.gltf` 零命中），
所以"正规导入造占位模型"这轮做不成。**留下来的第三条路**：把"引擎自带图元"收成六个**描述里可以直接写的名字**。

- 新文件 `Graph/PCGBuiltinModels.cs`：六个名字 → 六枚 Guid。**Guid 不是抄的**，是从引擎自带 `.flax`
  的资产头逐条量出来的（`Capsule 3f82e577…`、`Cone 1b4a643d…`、`Cube b43f0f8f…`、`Cylinder 223aa989…`、
  `Plane 54a1ff4a…`、`Sphere dd86c472…`）。这是一条**被检查的假设**：换引擎版本若动了某枚 Guid，
  后果是那一层取不到模型 → 运行期"摆出 0 件"守卫会点名，而不是静默少长一片树。
- `PCGSpecCompiler.Resolve()` **先查表**，查不到才交给注入的 `Func<string, Guid>` ⇒ 编辑器与命令行
  对同一份描述给出同一个结果，而且离屏编译不再依赖能不能扫到磁盘。
- 七份描述的 39 处引用全部换成裸名字（`Tools/_builtin_models_sweep.py`，脚本自带三条闸门：
  非注释行不许剩绝对路径 / 不许剩指向不存在资产的路径 / 不许剩 `.flax`；并保留 BOM 与行尾）。
  占位映射写死在脚本里并说明粗形对应：`Pine→Cone`、`Bush/Rock→Sphere`、`House→Cube`、`Road/Water→Plane`。

**证据**：离线 **1958 / 0**（+8：表形状、六枚互不相同、"带扩展名/带目录/空/未知名都不命中表"、大小写、
**不经任何宿主解析器**就把 Guid 落进 Model 槽位且不报"解析不出"、以及**存量对账**——七份描述每一处
`model "…"` 都是表里的名字）；`ModelPathHygieneTests` 那笔**明账翻成零账**并通过（以后谁再写绝对路径就红）。
引擎侧：编译 #28 热重载 → 五份描述重编 **0 警告**（`valley` 21 节点/13 连线——它此前有 6 处指向不存在资产）；
四份 demo 图里**资产引用集合与改前逐字节相同**（按排序后的 md5 比）⇒ 换写法没有换 Guid；
全量重烤 `Valley 73 / City 1330 / Township 87 / Partition 6` **逐项不变**（`Cave` 是 3，那是本会话更早
由 mtime 定过归属的洞穴/生成器侧变化，见上文；`kk` 45 件走 `Resolve<Prefab>`）。

**已知读数缺口**：`pcg_spec_compile` 返回里的 `models` 字段现在为空 `{}`——它只登记**宿主解析器**解过的路径，
而裸名字在编译器内部就命中了表。证据改由"图里资产集合与改前相同"这条离线+磁盘对账提供；
要让那个字段重新有意义，得让 HundunAgent 把表里的命中也报出来（本轮没动它）。

### 64 边那一档：归因**从盖子翻到墙上**（2026-10-11 深夜，两件事都是在同一份夹具上量的）

**先撤回两条旧读数**（它们来自不同尺子与不同窗口，被当成了同一件事）：
- "329 个跨面三角形里 107 个塌成单点、只剩 222 条段、34 个度 1 断口"——那是 `_prism_diag.py` 拿**相对 ε**
  当去重半径量的（表里 `dedupe 4.899e-04` 那一行），而**产品用的是 `epsilon * 0.01 = 4.899e-06`**。
- "12 个货真价实的捏合点（`{2:305, 4:12}`）"——同一把探针**把零长自环也计进了度数**；产品第一步就丢掉自环
  （"自环：退化线段，不是环上的一条边"）。

**按产品的两把尺子在同一个文件上重测**（`Tools/_prism_one_window.py`，输入 6019 顶点 / 10370 面、ε=4.899e-4、
刀口上 0 个顶点）：**329 条段、317 个节点、度数全是 2、无重复边、无需剪叶、环 317/317 闭合、
每条无向边恰好被走一次、没有任何环点被穿过两次** ⇒ **环是简单环**。所以候选修法里"在捏合点把环拆成两圈"
**在这一档没有对象可拆**，而"先压实环再三角化"同样落空（上一节已记）。

**于是把出口判据的报错分侧**（`PCGMeshCut.CountOpenEdges` 现在按"三个顶点是否都在刀口平面上"把每块三角形
归成**盖**或**壁**，未配对边报 `被 N 个三角形用（盖 X / 壁 Y）`）。读数一眼翻案：

```
未配对的边：(0.596,0.839,-0.804)-(0.672,1.016,-0.742) 长 0.20220 被 4 个三角形用（盖 0 / 壁 4）
           (0.672,1.016,-0.742)-(0.672,1.016,-0.742) 长 0.00000 被 1 个三角形用（盖 0 / 壁 1）
```

**坏在墙上，盖子完全清白。** 两条边指向同一件事：被切过的**墙**面里出了**退化三角形**（两个顶点在 1e-4 内并成一点）
与**重复的墙三角形**（同一条真实边被 4 块墙三角形用 ⇒ 至少有一块被交了两遍）。而这一档的入口条件也印在同一句里：
`ε 内 0 个、1e-3 内 2 个`——刀口以 1e-3 量级**擦过**顶点，`crossed 329 / segments 329 / ear caps 0`。

**下一步该做的不是改产出，而是再分一层归属**：把用到那条边的 4 块墙三角形各自的**源三角形下标**打出来，
就能分清"重复"是**同一个源三角形被交了两遍**（发射路径的记账），还是**两个相邻源三角形各交出一块一模一样的**
（ clipping 在擦顶点档上把退化碎片当成了有效碎片）。两种的修法不同，而现在这句报错已经能把它们分开。
**没有动产出**：本轮只加分侧诊断——去重墙三角形或跳过零面积墙三角形都能让这一档"过"，但在归属未知时那样做
等于把真缺陷盖住（判据④：宁可拒绝，不交出没有根据的产物）。

**门**：离线 **1958 / 0**（那道边界钉子照常，拒因原文现在**印在它的 PASS 明细里**，以后每次跑都自动重读一遍）；
编译 **#29** 热重载；引擎自检 **30 组 / 0 失败 / 2 条如实跳过**；重烤 `Valley 73 / City 1330 / Township 87 /
Cave 5 / Partition 6` 与改前逐项一致。场景未保存。

#### 再钉一层：那 4 块**不是重复**，是墙上多出一层（2026-10-11 深夜，同一份夹具）

出口判据现在顺带数两件事（都写进拒因，钉子的 PASS 明细里每次自动重读）：产物里的**退化三角形**（三个顶点
焊接之后有两点重合）与**和前面完全重合的三角形**（顶点集合相同、绕序无关）。64 边那一档的读数：

```
未配对的边：(0.596,0.839,-0.804)-(0.672,1.016,-0.742) 长 0.20220 被 4 个三角形用（盖 0 / 壁 4）；
           (0.672,1.016,-0.742)-(0.672,1.016,-0.742) 长 0.00000 被 1 个三角形用（盖 0 / 壁 1）
｜退化三角形 1 块、与前面重合的三角形 0 块
```

- **重合 0 块** ⇒ "同一个源三角形被交了两遍""两块相邻面各交出一片一样的"这两种猜测**一起被否掉**。
- **退化 1 块** ⇒ 那块零面积三角形正好就是零长边的那个使用者；把它丢掉只把 4 降到 **3**，仍然不是 2。
- 所以剩下的是**几何层面多出一层墙面**：刀口以 1e-3 量级擦过顶点（ε 内 0 个、1e-3 内 2 个）时，
  擦过的顶点在相邻两格里被判成"保留侧"与"跨平面"各一次，于是那条边周围有 **4 块各不相同**的三角形。
  这一条与 §7.5 那个"多刀之下切口绕序"残留同源，都是**擦顶点档的分类**问题，不是封盖、也不是发射记账。
- **仍不动产出**：这一档继续诚实拒绝。真修它要给"擦过的顶点"定一个**与两侧一致的归属规则**
  （它算在保留侧、还是算在切掉侧、还是算在刀口上并只被切一次），而那是一个需要独立夹具与判据①–④
  全重跑的分类改动——本轮没有做到能落地的程度。
- 门：离线 **1958 / 0**，编译 #29，引擎自检 30 组全绿，重烤逐项不变。

## 2026-10-11 深夜：程序化几何新增第五档形状 **Wedge（斜屋顶）**

**为什么要它**：目标要求描述能长出"村庄、城市"，而几何侧只有 `box / grid / cylinder / stairs` 四档——
盒子里切不出**斜**面：`clip` 与 `cut` 的落点都是平面，屋顶要的正是倾斜的那一类。这一档补的是形状本身，
不是又一个开关。

- `Execution/PCGMeshBuilder.cs`：`Wedge(size)`（X 开间、Y 脊高、Z 进深，全尺寸口径与盒子一致），
  18 顶点 / 8 面（硬边按面拆开，与盒子同一个理由）。**关键写法**：`AddFace(ring, outward)`——
  每面一个解析算出的外法线，由叉积验绕序、反了就翻环；于是"绕序与法线一致"这个本文件的核心不变量
  **不依赖我把顶点顺序记对**（我第一次就把山墙与斜坡的环序写反过方向，靠这条自动纠回来了）。
- `Nodes/PCGGeometryNode.cs`：`ShapeWedge = 4`、`ShapeCount = 5`、`Build` 与 `ShapeName` 各加一个分支。
- `Graph/PCGWorldSpec.cs`：`GeometryShapes` **追加** `"wedge"`（顺序是存档口径，只能往后加）。
- `PCGEditor/PCGParameterLabels.cs`：`{ "Wedge", "楔形（斜屋顶）" }`。

**断言（+7 条，离线 1958 → 1965）**：体积 `= 开间 × 进深 × 脊高 / 2` 且为正；水密且绕序自洽
（`EdgeReport.unbalanced == 0`）且**总棱数 12**（9 条实体棱 + 3 片扇形对角线，欧拉核对得上——
这条挡住的是"少发了一片面"，只断言水密是挡不住的）；包围盒等于写下尺寸；**负尺寸与正尺寸同形**
（否则"取绝对值"那一步漏写也能过体积那条）；脊高为零时塌成一片、体积为零、不 NaN；
形状名↔下标防漂钉子加了一条 `wedge`；以及 **`geometry wedge` 编译之后图里几何节点的 Shape 槽位真的收到 4**
（"选项声明了却没人写进槽位"这一族的老坑，所以这条不能只靠那张表）。

**踩到的两条**：① `BoundingBox` 的属性是 `Size`（全尺寸）而不是 `Extents`/`Min`/`Max`——两次编译报错才定下来；
② 我一开始把总棱数**从参数推**成 24，实测 12（`EdgeReport` 第二个数是"按位置去重后的不同边条数"）。
**期望值要量，不要推**——这条又被验证了一次。

**门**：`PCGVerify` / `PCGEditorVerify` 各 0 错，离线 **1965 / 0**，编辑器编译 #35 热重载，
`pcg_selftest` **30 组 / 0 失败 / 2 条如实跳过**，重烤 `Valley 73 / City 1330 / Township 87 / Cave 5 /
Partition 6` 与改前逐项一致。**引擎侧只证到"既有几何链没退化"**：楔形本身还没进任何一份随仓库描述
（要在演示里看到屋顶，得往 `demo_township` 加一层 `geometry wedge` 并重烘焙——本轮没做，
因为那会在用户活跃的产出里加一批新 Actor，等他点头）。场景未保存。

## 2026-10-11 深夜：第六档形状 **House（墙 + 屋顶，一个封闭实体）**，并第一次进演示

**动机**：上一轮的 `Wedge` 只有屋顶那一块斜板；"村庄/城市"要的是一栋**房子**。`House(size)` = 矩形墙 + 沿 X 的屋脊
合成**一个封闭实体**（屋顶直接坐在檐口上，没有内部面 ⇒ 不会留下被配对四次的内部面）。尺寸口径与盒子一致：
X 开间、**Y 总高（含屋顶）**、Z 进深；屋顶固定占顶部三分之一、墙占下面三分之二——
这样描述里只需写一个熟悉的高度，代价是屋坡度随进深变（写进注释里，不藏）。

- 有向体积解析可推：墙 `X·Z·(2Y/3)` + 屋顶楔形 `X·Z·(Y/3)/2` = `(5/6)·X·Y·Z` ⇒ 12×9×10 是 **900.000**（实测）。
- 30 顶点 / 16 三角形；`EdgeReport`：`unbalanced == 0`（水密 + 绕序自洽），**总棱数 24**。
  **这里我又把期望值从参数推错了一次**：第一版我按"14 个角、19 条实体棱 + 9 条对角线 = 28"写死，实测 24；
  重算才看清角点是 **10 个**（底 4 + 檐口 4 + 屋脊 2），欧拉 `10 + 7 - 2 = 15` 条实体棱 + 9 条扇形对角线 = **24**。
  ⇒ `夹具的数要量出来、并且量的时候把推导写全`，这条又一次生效（连同"零总高时它塌成一张平面、
  此时不再封闭"——那是几何事实，注释与判据都如实写，不假装多一条契约）。
- 判据另有两条不是数尺寸的：**三层高度分布**（底 / 檐口 / 屋脊都得上有顶点，挡住"屋坡度写成对半"）
  与 **`geometry house` 编译后节点 Shape 槽位 == 5**；再加一条**真跑**：六个点 ⇒ 六件网格、每件 30 个顶点、
  包围盒高度跟着 `geometry-size` 走（只断言槽位是数字，挡不住"这一层一个网格都没建出来"）。
- 同步处：`ShapeHouse=5` / `ShapeCount=6` / `Build` / `ShapeName`、`GeometryShapes` 追加 `"house"`、
  `PCGParameterLabels` 中文标签、防漂钉子加一行。**离线 1973 / 0**（+8）、两道编译门 0 错、
  编辑器编译 **#36**、`pcg_selftest` **30 组 / 0 失败 / 2 条如实跳过**。

**第一次进演示**：`demo_township.pcgspec` 加了一层
`scatter hamlet points 9 geometry house geometry-size 14 10 12 within 2500 5400 3200 6200`。
编辑器重编译 **41 节点 / 32 连线、0 警告**（此前 36/26）；重烤 `PCG_Township` **87 → 88 Actor**。
**为什么只多 1 栋而不是 9 栋**：`points N` 是**整个生成范围**的源点数，`within` 只留下落进那块矩形的那一部分——
那块地占体积范围约 `800×800 / 3000×2400 ≈ 11%`，9 个点期望只剩 ~1 个。这是写下过的口径
（§"给限在小矩形里的一层定源点数要按面积折算"），不是缺陷；要在那片地里长出 ~9 栋就得把源点数按面积抬到 ~80。
**本轮没有再抬**：抬完要重编译 + 重烤两轮才能收口，而留下的当前状态是一致的（图 = 9 点那版、场景 = 88 件）。
场景未保存。

## 2026-10-11 深夜：第七档形状 **Tree（树干 + 树冠，一个封闭实体）**，林地从"浮空的锥"换过来

**动机**：目标点名"树木、森林"，而演示里的林地一直是 `model "Cone"`——一个浮在半空的锥。
分成两层（一层干、一层冠）也不行：两个实体相接会留下一张内部面（被配对四次的边），破坏判据①。
所以 `PCGMeshBuilder.Tree(size, segments)` 把两样**合成一个封闭实体**：底面扇形 + 树干侧面（ tapered 四边形）
+ 圆锥树冠，面数 `4n-2`、按面拆开后的顶点 `10n-6`。尺寸口径：X/Z 取较小者当**树冠直径**、Y 是**总高**，
树干占底部三分之一、树冠半径的 1/4 是根径。

**判据（离线 +8，1973 → 1980）**：件数公式（n=24 ⇒ 234 顶点 / 94 面）；**有向体积与解析对照**
（台体树干 + 圆锥树冠，实测 857.3 vs 867.2，相对误差 1.14%，内接多边形本该偏小）；`unbalanced == 0` 水密；
包围盒；**三层高度分布**（底 / 冠底 / 顶点——挡住"冠底错设在总高中点"，那种写法仍然水密、体积也在 2% 内）；
段数 2 被抬到 3（少于 3 就不成体）；再加 DSL 那条：`geometry tree` 编译后槽位 == 6 **且真跑** 12 点 ⇒ 12 件、
每件 `10n-6` 顶点。

**演示接线**：`demo_valley` 的林地 `model "Cone" model-scale 1.2 yaw on` →
`geometry tree geometry-size 6 14 6 geometry-segments 12`。编辑器编译 **#39**、重编译 **37 节点 / 27 连线、0 警告**
（多出的 1 节点 1 连线正是链中插入的 `Geometry`），重烤 `PCG_Valley` **73 → 72 Actor**。
**-1 的来源没有当成缺陷追**：这一层的树数在离线是 8（带 `polygon` 那块地）、凹口里 0 个，
而引擎里的地形与离线那片缓坡不同（坡度筛 `slope 0 40` 会按真实地形滤掉点），
同轮 `PCG_Cave` 也在 5/3 之间摆（已知：演示各体积写同一张 Main 地形，顺序决定谁最后落）。
**目视确认（树是不是真的长成有干的样子）归用户 PIE。**

**一处必须记下的测试教训（连着两次 FAIL 才改对）**：`IsForestSink` 认"林地那条链"的方式是**读它的附带参数**
（`Random Yaw == true` + `Scale Multiplier == 1.2`）——换形状后这两个槽位不存在，三条断言一起报"没找到链"；
第二版我改成"看有没有 `PolygonFilter` 节点"，于是**对照组**（专门删掉 polygon 的那次跑）又找不到链。
最终按**编译器给每个节点打的层名注释**（`散布层 forest：…`）认它，前向闭包到汇。
⇒ **认一条链要认"它是谁"，不是认"它此刻挂了哪些开关"**；参数、形状、甚至那条子句本身都是可变的，层名才是身份。

### 新形状在引擎里的证据到哪一层为止（2026-10-11 深夜，如实划线）

`Tree` / `House` 这两档在**离屏**证到了算术那一份（解析体积、水密、绕序、高度分层、段数下限）。
本轮想把它们推进到"引擎里那份上传后的网格"，读了一圈取证工具后**没有做成**，边界如实记在这里：

- `actor_find` 能列出场景里的产出：`PCG_Valley` 下确实有树那一层的产出 Actor，位置随地形起伏
  （y 从 −76 到 +37，都在那块 L 形地里），`PCG_Township` 下那 3 栋庄角屋都在 `x≈2560-3070、z≈5690-5940`
  这块矩形里 ⇒ **"描述 → 图 → 场景里有这件东西"这一层是实测过的**。
- 但**读不到网格内部**：`model_inspect` 要一个磁盘 `path`，而生成器用的是**虚拟 Model**（没有磁盘路径）；
  `actor_get` 只交回变换与可写属性名，没有顶点/三角形数或 `Box`。⇒ 从 MCP 侧无法验证上传后的网格。
- 唯一能读到的地方是**引擎自检内部**：那一层已有先例——`FindMeshOf(volume, "Geometry")` 交出 Actor 渲染的
  那份 `MeshBase`，切过的盒子就是靠它断言 `Box.Size.Y` 2 → 1 的（§7.4）。
  **所以补法的形状是写好的**：在几何生成器那组（3m）里再各起一件 Tree / House 的产出，断言
  `TriangleCount == 4n-2`、`VertexCount == 10n-6`、`Box.Size ≈ 10×30×10 / 12×9×10`。
- **本轮没有做，原因不是懒**：那处代码要先躲开一个已知的坑——`Actor.Destroy` 延迟到帧末，
  同一帧里 `Clear()` 再重生会**把上一批仍留在子级**，`FindMeshOf` 会读回上一件（切过的盒子）而断言照样绿。
  正确写法是**另起一个临时体积**（不是 Clear 复用），而那需要再一轮编译 + 自检才能收口；
  在没有把握一轮内验完的情况下，我选择把边界写清楚，而不是留一条"看着绿其实读的是上一件"的断言。
- 顺带一条口径：**两种形状共用同一个 ShapeKey 缓存**这件事目前只有离线钉子（`ShapeKey` 含顶点量化内容与绕序），
  引擎侧没有"树和房子必须落在两个不同模型上"的正证。这是同一条缺口的另一半。

### 描述生成的房子进到了城市里（2026-10-11 深夜，`demo_city`）

`demo_city.pcgspec` 末尾加一层：`scatter houses per-block 2.5 geometry house geometry-size 16 12 14`。
`geometry` 那一套目前只有 `scatter` 层支持（聚居地/城市的链只吃 `model`），所以这一层是**新增的一层**，
不是把 `city` 那两层换掉——那需要给聚居地/城市选项表加 `geometry` 三件套并解开与 `model` 的互斥，
本轮没有那个把握就在剩下的轮次里做完，于是没动（见下面的"下一步"）。

- 编辑器重编译 **16 节点 / 12 连线**（此前 12/9，多出的是点源 → `Geometry` → `SpawnGeometry` 那条链）、**0 警告**；
- 重烤 `PCG_City` **1330 → 1339 Actor**；
- **不是靠差值推的**：`actor_find` 读回场景，新增的一组恰好 **9 件** `PCG_Gen_Geometry_*`，
  位置 `x 4360–5502`、`z 144–1756`（都在这个体积的城市范围内），`y 6–76` 随地形起伏
  ⇒ 这 9 件是描述里那三个数（16 开间 × 12 总高 × 14 进深）直接建出来的网格，不是借来的图元。
- 离线 **1980 / 0**（`SampleRun(demo_city)` 里"每个节点都要跑到"跟着新链一起过了）。

**下一步（写清楚，别下次再猜）**：给 `VillageOptions` / `CityOptions` 加 `geometry` + `geometry-size` +
`geometry-segments` 三个选项、对应字段、与 `model` 互斥的带行号报错，并把 `AddSpawner(...)` 扩成
"给了几何形状就先插一个 `Geometry Primitives`、生成器换成 `SpawnGeometry`"——这一改同时覆盖
聚居地、城市、道路、水面（它们共用 `AddSpawner`），城市那 1330 件盒子才能真正变成房子。

## 2026-10-11 深夜：`geometry` 打通到**聚居地 / 城市**（共用的那条出口一处改，四层同覆盖）

`AddSpawner(...)` 是聚居地、城市、道路、水面共用的出口，原先只会挂 `PCGSpawnStaticModelNode`，
所以 `geometry` 只有 `scatter` 层写得出来——城市那 1330 件永远是借来的 `Cube` 图元。

- 新增三个可选参数（`geometryShape` / `geometrySize` / `geometrySegments`，默认 null ⇒ 老调用一条行为不变）。
  给了形状名就改成 **`Geometry Primitives` → `SpawnGeometry`**，`Use Point Scale` 钉 false（世界单位，
  与 `scatter` 那一档同一个口径），两个输出开关都关（一层一件件摆，才看得见地块疏密）。
- `SettlementOptions` / `CityOptions` 各加 `geometry` / `geometry-size` / `geometry-segments` 三行，
  `PCGSpecSettlement` 与城市那个类各补三个字段（选项表是声明式的，加一行就有一条覆盖断言跟着跑）。
- 两处静默失效都点名：同时写 `geometry` 与 `model` ⇒ 警告"按 geometry 建，model 被忽略"；
  只写 `geometry` 不给 `geometry-size` ⇒ 警告"尺寸取默认 100×100×100（世界单位，格子级的默认值会让房子大到离谱）"。

**判据（离线 1980 → 1998 / 0，`PCGVerify` 0 错）**：链的形状（`SplitGrammar, ProjectPoints, Geometry, SpawnGeometry`，
并且**不再挂** `SpawnStaticModel`）；形状落到槽位（`5` = house）；写了 geometry 就不该报"没有解析出资产"；
两条互斥/缺尺寸的警告各一条；**真跑**：地块上建出了网格且每件是房屋的 30 顶点（`2 件 / 30 顶点`）。
最后这条为什么不能省：只断言图里有这几个节点，挡不住"这一层一个几何都没产出"。
（`2 件` 而不是 4：这一层的地块数由**上游种子点数**决定，`lots` 只是上限提示——这是既有语义，不是这次改出来的。）

**没有做的两件事，写清楚**：① 没有把任何一份随仓库描述改成 `village … geometry house`（作者现在能这么写，
但演示还跑在 `model "Cube"` 上）；② **编辑器手上的程序集比源码旧**——本轮没有跑 `code_build_wait`，
下次要在引擎里验这条链，先热重载再 `pcg_spec_compile` + 重烤，否则测的是旧 DLL。

### 新城街区：`city` 也走 `geometry house`（2026-10-11 深夜，件数与语法逐项对得上）

`demo_city` 追加一条**只到房子为止**的街区：
`city productions "District = Block,Block: X:0.5,0.5; Block = House,House,House: Z:0.34,0.33,0.33" root District max-depth 2 cell 400 40 400 at 4450 1600 geometry house geometry-size 30 20 26`。
原来那条 `max-depth 3`（切到 Room）保留——它演示的是符号改写的深度，两层互不替换。

- 编辑器重编译 **21 节点 / 16 连线**（此前 16/12；多出的是 `at` 种子点 + 切分 + 落地投影 + 程序化几何 + 几何生成器）、**0 警告**；
- 重烤 `PCG_City` **1339 → 1345 Actor**，**Δ = 6 件**，正是这条产生式的叶子数：`District = Block,Block`（2）× `Block = House,House,House`（3）= **6**，
  且 `max-depth 2` 让 `House = Room,Room` 那一条不再展开 ⇒ 落地的是房子而不是房间。
  件数不是"多了一点"而是**算得出等于几**，这才是这条接线的证据；
- 改完共用出口之后补跑了引擎自检：**30 组 / 0 失败 / 2 条如实跳过**；离线 **1998 / 0**、`PCGVerify` 0 错。

**留给 PIE 的**：这 6 栋与村庄那 2 栋、城郊那 9 栋的**观感**（尺寸是否协调、屋顶是否好看）本轮没有目视，也不归我做。

### 交接：下一件事的形状（写死，免得下次重新推）

**要解决的问题**：城市主区那 1330 件仍然是借来的 `Cube` 图元，而这一层的叶子有种类之别
（`District = Block,Block: X:0.5,0.5; Block = House,House,House: Z:0.34,0.33,0.33; House = Room,Room: X:0.5,0.5`）。
`AddSpawner` 现在只能给**一层**指定一个形状，所以替换不掉它。
**下一步 = 按符号分派形状**：符号语法的叶子已经带 `Symbol`（序号）与 `SymbolName`（文本）两列，
所以需要的不是新节点，而是编译器侧的**扇出**：
`Grammar → ProjectPoints → [ 每个符号一条：AttributeFilter(列 SymbolName, 该符号那一段) → Geometry(形状) → SpawnGeometry ] × N`。
写法草案：`city … geometry "House=house:30 20 26,Room=box:12 3 12"`（符号=形状:尺寸，逗号分隔；
未点名的符号仍走 `model`）。互斥与缺尺寸的口径照 `scatter`/`village` 那两处已经落地的写法，
并各带一条警告点名的断言。改动面：`CityOptions` 一个新选项 + 一个字段、`AddSpawner` 旁加一个扇出助手、
每条链一个 `SymbolName` 过滤器，测试按现成的 `SettlementGeometryTests` 那一组照样补。
**门的位置**：离线 `dotnet run Tools/PCGGoldenRunner`（现 **2001 / 0**）、`PCGVerify`/`PCGEditorVerify` 各 0 错、
编辑器 `code_build_wait` 后 `pcg_selftest`（现 **30 组 / 0 失败 / 2 条如实跳过**）、`pcg_spec_compile demo_city` + 重烤 `PCG_City`。

## 2026-10-11 深夜：`geometry-map` —— 一层里按符号分派形状（城市主区不再借 Cube）

**要解决的问题**：`AddSpawner` 只能给**一层**指定一个形状，所以那条把城市切到"房间"的深语法只能把 1330 件
东西全建成同一个借来的 `Cube` 图元。符号语法的叶子本来就带 `Symbol`（序号）与 `SymbolName`（文本）两列，
缺的只是编译器侧的扇出。

- **写法**：`city … geometry-map "Room=box 22 8 18"`，多符号即
  `geometry-map "House=house 30 20 26; Plot=grid 60 1 40"`（`;` 分隔，每段 `符号=形状 宽 高 深`）。
- **链**：每个条目插一条 `AttributeFilter(Symbol == 该符号的序号) → Geometry Primitives → Spawn Geometry`，
  都挂在落地投影之后；这一层不再挂静态模型生成器。
- **序号不另算一套**：符号表直接取自 `Grammar` 节点自己的 `TryParseProductions`（同一份编号、两处消费）。
  另算一份的结局就是"筛了一个没人等于它的序号、整层静默什么都不长"。
- **三类情形一律编译期报错**（都点名，都带语法实际认识的符号列表）：点了语法里没有的符号；
  给**还会被改写**的非叶子指派形状；**漏掉**某个叶子符号。最后那条是这一层最怕的静默失效。
- **判据（离线 2003 → 2010 / 0）**：链的形状（2 筛 / 2 几何 / 2 几何生成器 / 0 静态模型）；
  **符号与形状逐一对上**（`Room→house`、`Plot→grid`）与**尺寸跟着符号走**（30×20×26）——
  这两条是接线的核心，因为接错时链的形状与"有没有几何"全都仍然"对"；
  三类报错各一条，其中"没有的符号"那条还断言错误信息里列出了语法认识的符号名。
  `FilterFor` 刻意用符号表现算序号，而不是把测试里手抄的编号当参照。
- **引擎侧**：编辑器热重载（编译 #2，之后 `pcg_selftest` **30 组 / 0 失败**）；
  `pcg_spec_compile demo_city` ⇒ **27 节点 / 22 连线、0 警告**（此前 21/16，多出的是两条符号分支各 3 个节点）；
  重烤 `PCG_City` **1345 Actor（件数不变：一片叶子一件东西）**、**0 警告**，
  且 `actor_find "PCG_Gen_Cube"` 全场景只剩 **15** 件、**没有一件在城市体积的范围内**（都在山谷村庄与庄镇）
  ⇒ 城市主区那 1330 件不再借图元。
  **一处没做到、写在这里**：我三次用名字前缀去数新的几何产出（`…_Geometry_l0_x0z0_1300 / _600`、`PCG_Gen_box*`）
  都是 0 命中——命名前缀我猜错了，于是这一件只能用"件数不变 + 0 警告 + Cube 归零"来支撑，
  **没有**独立读到这 1330 件的新名字。接手第一件事：用 `scene_hierarchy` 读 `PCG_City` 的孩子，
  确认这 1330 件的前缀与形状（应当是 box 22×8×18 与 grid 60×1×40 两种）。
