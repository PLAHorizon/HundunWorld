# PCG 插件 — Phase 1 实施说明

**状态：** Phase 1 代码完成，C# 层已验证编译通过
**设计文档：** `docs/PCG_PLUGIN_DESIGN.md`（v1.1）
**插件路径：** `HundunWorld/Plugins/PCG/`

---

## 1. 已交付内容

### 1.1 数据层（`Source/PCG/Data/`）

| 文件 | 内容 |
|---|---|
| `PCGPoint.cs` | 点结构：`Position`(Vector3) / `Rotation`(Quaternion) / `Scale`(Float3) / `Density` / `Seed`(int) / `Bounds` / `Attributes` |
| `PCGAttributeSet.cs` | SoA 属性存储，8 种类型，**就地 `Detach()` 写屏障** + `Share()`/`Release()` 引用计数 |
| `PCGData.cs` | 点数组 + 属性集容器，含 `Gather`（筛选压缩）、`Merge`（属性列并集 + 类型冲突检测）、`Clone`、`Aggregate`/`Flatten`（多输入引脚） |
| `PCGSeededNoise.cs` | 插件自带种子化噪声：Value2D/3D、Perlin2D、Worley2D/3D、Fbm2D/3D |
| `PCGDeterministicRng.cs` | 由 `(seed, nodeId, pointIndex)` 派生的确定性 RNG |

### 1.2 执行层（`Source/PCG/Execution/`、`Source/PCG/Graph/`）

- `PCGGraph.cs` — 图表数据模型（节点/连线/暴露参数/设置/编辑器数据槽）
- `PCGNodeRegistry.cs` — 类型注册表，工厂模式，重载安全
- `PCGGraphCompiler.cs` — **环检测（迭代式 DFS 三色）**、拓扑排序、引脚与必需输入校验、**结构哈希**
- `PCGGraphExecutor.cs` — 拉取式执行、按节点结果缓存、点预算保护、`CancelToken` 轮询、逐节点耗时统计
- `PCGExecutionContext.cs` / `PCGWorldContext.cs` — 执行上下文与场景查询
- `PCGSpawnNaming.cs` — 生成对象命名契约

### 1.3 地形采样（`Source/PCG/Terrain/`）

- `PCGTerrainSampler.cs` — **双路径**：`RayCast`（正确性基准）与 `HeightfieldInterpolated`（性能，双线性插值 + 中心差分法线）
- `PCGTerrainProbe.cs` — 标定器：射线求真值 + 高度场读原始值 → **仿射最小二乘拟合** → 独立验证残差，超容差即判定标定无效
- `PCGTerrainCalibrationStore.cs` — 按地形缓存标定，避免每次执行重跑探测

### 1.4 节点（`Source/PCG/Nodes/`）

| 节点 | TypeId | 说明 |
|---|---|---|
| Landscape Sampler | `LandscapeSampler` | 抖动网格采样，写 `$Slope` 供下游复用，可切双路径 |
| Density Filter | `DensityFilter` | 密度区间筛选，产出压缩副本（不共享上游属性集） |
| Spawn Static Model | `SpawnStaticModel` | 逐点生成 `StaticModel`，密度阈值化成生成概率，`MaxActors` 上限保护 |

### 1.5 运行时与测试（`Source/PCG/Runtime/`、`Source/PCG/Tests/`）

- `PCGGraphAsset.cs` — 图表资产，直接持有 `PCGGraph`（Flax 序列化器持久化）
- `PCGVolume.cs` — 场景 Actor：本地空间体积、种子、三种模式、批量调试绘制、**容器式清理**
- `PCGRuntimePlugin.cs` — GamePlugin，注册节点 + 重载时清理静态状态
- `PCGNoiseGoldenTests.cs` — 黄金值测试（数值已由独立验算程序得出并冻结）
- `PCGSelfTest.cs` — 引擎内自检组件（挂在任意 Actor 上即运行）

---

## 2. 验证状态

### 2.1 已验证 ✅

**编译验证**（Flax 生成的真实工程文件，非手工搭建）：

```
dotnet build HundunWorld/Plugins/PCG/Source/PCG.csproj       → 0 warning, 0 error → PCG.CSharp.dll
dotnet build HundunWorld/Plugins/PCG/Source/PCGEditor.csproj → 0 warning, 0 error → PCGEditor.CSharp.dll
```

**数值验证**（对照 Flax 实际构建产出的 `PCG.CSharp.dll`，非等价复制品）：

- 黄金值：11 个采样点 + 3 个 RNG 序列值，**全部与冻结值逐位一致**
- 取值范围：7 种噪声 × 2D/3D 扫描，全部落在 `[0,1]`，无 NaN/Inf
- 连续性：Perlin2D 跨晶格无跳变
- **种子敏感性**：不同种子去相关；晶格点哈希对种子敏感
- **增量执行不变量**：逆序单独重算全部 512 点 == 全量执行结果
- RNG 分布：均值 0.499460（20 万样本）

> **验证过程发现并修复了一个真实缺陷（已修复，勿回退）。**
> 初版 `PCGSeededNoise.Hash` 的返回类型被写成了 **`float`**。32 位哈希值超过 2²⁴ 后，float 只有 24 位尾数，**低 8 位被静默舍入**。而 Perlin 用 `h & 7` 从哈希低位选梯度 —— 低位被破坏，梯度就选错。
>
> 症状极具迷惑性：`Value2D`/`Value3D`/`Worley`（只消费哈希的量级）全部正确，**只有 Perlin/Fbm 偏离**。如果只做"取值为 [0,1]、均值≈0.5、跨晶格连续"这类性质测试，会**完全通过**；只有冻结黄金值才能抓到它。
>
> 修复：`Hash` 改回 `uint`，`GradientDot` 直接用 `Hash(...) & 7u`，不再经过 float 中转。修复后 Flax 构建产物的 `Hash` 返回类型经反射确认为 `UInt32`，13 项全部通过。
>
> **教训：** 这就是设计文档 §9 把"黄金值测试"列为 Phase 1 交付物的原因 —— 性质测试无法替代数值冻结。

### 2.2 尚未验证 ⚠️（需在装有完整工具链的机器上执行）

`Flax.Build` 原生链路在本机无法运行：

- 报错 `No x64 host compiler tools found for target architecture ARM64`
- 报错 `Missing Windows SDK` / `Missing Vulkan SDK`

因此以下项目**需要你在 Flax 编辑器中验证**：

1. **在编辑器内首次编译一次**：Flax 的构建步骤会生成
   `Cache/Intermediate/PCGEditorTarget/<config>/PCG/PCG.Bindings.Gen.cs`（本机 `-genproject` 已成功，
   但该 bindings 文件由 Flax 的 C# 绑定生成器产出，而生成器本身需要原生工具链，本机无法运行）。
   本插件未使用任何自定义 `API_CLASS`，故该文件内容应为空，但**首次必须由 Flax 触发一次构建**，
   否则 `dotnet msbuild -t:CoreCompile` 会报 `CS2001: 未能找到源文件 PCG.Bindings.Gen.cs`（这是预期现象，不是代码错误）。
2. **地形标定语义**：`TerrainPatch.GetHeightmapData()` 返回值到世界 Y 的换算关系，引擎头文件未定义。设计文档 §3.5 的标定器就是为此写的 —— 请在真实地形上运行一次 `PCGSelfTest`，它会打印实测的 `HeightScale`/`HeightOffset` 与两条路径的残差。
3. **`Terrain.RayCast` 线程安全性**：未验证，故路径 B 保持 `IsThreadSafe == false`（主线程执行）。
4. **`ModelInstanceActor` 写入语义**：反射已确认 `Entries` 是 `ModelInstanceEntry[]`，但批量写入后是否需手动触发更新未确认（当前 Phase 1 未使用该路径）。

### 2.3 如何运行自检

1. 打开 `HundunWorld` 工程
2. 在任意场景中给一个 Actor 添加 `PCGSelfTest` 脚本
3. 运行场景，查看 `Logs/` 或编辑器控制台

它会输出分组结果：噪声黄金值、节点注册、编译器行为（空图/未知类型/环/缺输入/正常执行）、属性 COW 隔离、确定性重排序、结构哈希语义、地形双路径一致性。

---

## 3. 已注册进工程

- `HundunWorld/HundunWorld.flaxproj` → 新增 `Plugins/PCG/PCG.flaxproj` 引用
- `HundunWorld/HundunWorld.sln` → Flax 的 `-genproject` 已自动加入 PCG / PCGEditor 工程与解决方案文件夹
- `HundunWorld/Plugins/PCG/Source/{PCG.csproj, PCGEditor.csproj, PCG.Gen.cs, PCGEditor.Gen.cs}`

> `PCG.Gen.cs` / `PCGEditor.Gen.cs` 是 AssemblyInfo 桩，正常由 Flax 生成。此处手工创建以便 `dotnet build` 可直接验证。Flax 重新生成时会覆盖为等价内容。

---

## 4. 实现期对设计文档的修正

三条重要修正已回写 `docs/PCG_PLUGIN_DESIGN.md`：

1. **COW 写屏障改为就地 `Detach()`**（§3.2 注 3）。设计初稿写 `EnsureOwned()` 返回克隆，实现时发现：若调用方漏接返回值，克隆被静默丢弃、写入落到共享原体，COW 完全失效且无编译错误。改为就地克隆底层列并把 `_refCount` 置 1，"忘记接返回值"这个错误在构造上不可能发生。

2. **`ModelInstanceActor` 批量路径不成立**（§5.1.5）。反射确认 `Entries` 的字段只有 `Material`/`Visible`/`ShadowsMode`/`ReceiveDecals`，**没有变换字段**，逐实例变换挂在 `MeshReference` 上。因此实例仍是独立场景对象，批量并不带来设计文档假设的大幅收益。Phase 1 改为逐点生成 `StaticModel` + `MaxActors` 上限（默认 5000）；真正密集放置应改用 `Foliage`（`AddFoliageType` + `AddInstance`，不创建 Actor）。

3. **清理改为容器式而非标签式**（§7.2）。设计文档建议用标签回收输出，但实测 `Level.FindActors` 需要 `Tag` 类型，而 Flax 的 `Tag` 是工程标签表索引、无法由任意字符串在运行时构造。改为**把生成对象 parent 到 PCGVolume 下**，清理即"销毁我的子级" —— 该父子关系随场景保存，重载后依然可回收，比内存侧表更可靠。

---

## 5. Phase 2 起点

按路线图，Phase 2 是编辑器与可视化。关键结论（§6.1）是**不要自研画布**：派生 `FlaxEditor.Surface.VisjectSurface` 即可获得节点拖拽、连线、框选、缩放平移、撤销重做、复制粘贴与右键节点搜索菜单。

下一步建议顺序：

1. `PCGSurface : VisjectSurface` + 为已有 3 个节点写 `NodeArchetype` 表
2. `PCGGraphEditorWindow : AssetEditorWindow`，工具栏挂 Generate / Clear
3. 为 `PCGVolume` 写 `CustomEditor`，暴露 Generate / Clear 按钮与结果摘要
4. 视口调试绘制接入（当前已在 `PCGVolume.OnDebugDraw` 用批量 `DrawLines` + 4 档密度着色实现，需接到编辑器重绘）
