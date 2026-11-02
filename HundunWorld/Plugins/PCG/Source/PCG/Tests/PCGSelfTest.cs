// PCG 插件 - 自检组件
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §9 的 Phase 1 验收标准。

using System;
using System.Collections.Generic;
using FlaxEngine;
using PCG.Execution;
using PCG.Graph;
using PCG.Nodes;
using PCG.TerrainSampling;

namespace PCG.Runtime
{
    /// <summary>
    /// 在引擎里跑插件的自检，并把结果报到日志。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 噪声与随机数那两组在哪儿都能跑，但 Phase 1 里有好几条验收标准是**绑引擎**的：
    /// 节点工厂真的注册上了、代码搭出来的图真的能编译并执行、环是被拒掉而不是把栈撑爆、
    /// 执行、环是被拒掉而不是把栈撑爆，以及两条地形采样
    /// 通路在标定之后结果一致。这些都要一个活着的引擎，所以它们住在这里。
    /// </para>
    /// <para>
    /// 把这个脚本挂到场景里任意一个 Actor 上，它在第一次更新之后跑一次。它不生成任何东西、
    /// 也不改任何东西，所以迭代期间一直挂着是安全的。
    /// </para>
    /// </remarks>
    public class PCGSelfTest : Script
    {
        /// <summary>启动时跑检查。</summary>
        [Tooltip("Run the PCG self-checks once after the scene starts.")]
        public bool RunOnStart = true;

        /// <summary>是否包含地形标定的交叉校验。需要场景里有一块地形。</summary>
        [Tooltip("Also verify that the heightfield and ray cast terrain paths agree. Requires a terrain.")]
        public bool TestTerrain = true;

        /// <summary>每条轴上的地形校验探针个数。</summary>
        [Tooltip("Terrain verification probes per axis.")]
        public int TerrainProbesPerAxis = 5;

        /// <summary>
        /// 三条放置检查（植被 / 样条网格 / 网格采样）共用的模型。可以写资产路径，也可以写资产 Guid。
        /// </summary>
        /// <remarks>
        /// 默认值是引擎自带的图元（和演示描述同一个口径）：它在**工程资产库之外**，所以
        /// <c>Content.Load(path)</c> 那条路取不到，只有 <c>Content.Load(Guid)</c> 稳——而把路径解析成
        /// Guid 是编辑器的活，`pcg_selftest` 工具会用资产库或资产头做这件事、再把 Guid 填进这个字段。
        /// 空值、路径不存在、或引擎装在别处，这几项都只**跳过**（理由进结果的 skipped），不会判失败。
        /// </remarks>
        [Tooltip("Model asset path or Guid for the placement checks. Unresolvable skips those halves.")]
        /// <summary>
        /// 引擎 Content 目录：新引擎托管 API 已没有 <c>Globals.EngineContentFolder</c>，
        /// 从 <see cref="Globals.StartupFolder"/> 向上找带 Editor 子目录的 Content；找不到返回空串，
        /// 夹具路径解析失败时按原语义进 skipped。
        /// </summary>
        private static string EngineContentFolderBestEffort()
        {
            var dir = new System.IO.DirectoryInfo(Globals.StartupFolder);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            {
                string candidate = System.IO.Path.Combine(dir.FullName, "Content");
                if (System.IO.Directory.Exists(System.IO.Path.Combine(candidate, "Editor")))
                    return candidate;
            }
            return string.Empty;
        }

        public string FoliageModelPath =
            EngineContentFolderBestEffort() + "/Editor/Primitives/Cone.flax";

        /// <summary>
        /// Actor 生成器放置检查用的 Prefab。引擎没有自带可分发的预制体可退，所以这里指向工程里一个
        /// **真资产**（`Content/PCG/Prefabs/PCG_TestProp.prefab`，由 `prefab_create` 从一个临时
        /// StaticModel 存出来）。清空这一行就跳过放置那一半；失败路径的检查照跑不误。
        /// </summary>
        /// <summary>
        /// Actor 生成器放置检查用的 Prefab。工程里任何一枚 Prefab 都行——这条检查要证的是"产出的 Actor
        /// 归体积所有、于是 <c>Clear</c> 收回的正好是它们"，与预制体里装的是什么无关。
        /// </summary>
        /// <remarks>
        /// 实测（2026-10-10）一条必须知道的资产管线事实：**刚刚由工具写出来的 Prefab 载不进来**——
        /// 资产库按文件扫到了它（能查到 Guid），但内容存储里还没有条目，<c>WaitForLoaded</c> 一直不返回；
        /// 编辑器重扫/重启之后同一份文件就正常了。所以这里指向一枚**早已在册**的资产，
        /// 免得自检因为一个"新文件还没被引擎登记"而报假红。清空这一行只跳过放置那一半，
        /// 失败路径的检查照跑不误。
        /// </remarks>
        [Tooltip("Prefab asset path for the Spawn Actor placement check. Empty skips that half.")]
        public string ActorPrefabPath = "Content/Prefabs/UI/Input_ValidatedTextBox.prefab";

        /// <summary>
        /// 贴花放置检查用的材质：必须落在 **Decal 域**（`MaterialBase.IsDecal`）。
        /// </summary>
        /// <remarks>
        /// 域由材质的 **shader** 决定，托管侧只能读 `Info.Domain`、没有任何 setter，所以脚本造不出 Decal 域材质。
        /// 但**引擎自带一枚**：`Content/Editor/DebugMaterials/SingleColor/Decal.flax`（与模型夹具借用引擎图元同一个套路）；
        /// 它和模型一样在工程资产库之外，路径由 `pcg_selftest` 工具解析成 Guid 再填进来。
        /// 载不进来时这一半只跳过（理由进结果的 skipped），域的守卫与拒绝路径照跑。
        /// </remarks>
        [Tooltip("Decal-domain material path for the Spawn Decal placement check. Empty skips that half.")]
        public string DecalMaterialPath =
            // 引擎自带资产的路径**不能写死这台机器的安装位置**：换一台机器（或引擎装在别处）
            // 这两个夹具就静默进 skipped，引擎门悄悄变小，而报出来的仍是"全绿"。
            // 新引擎的托管 API 已移除 `Globals.EngineContentFolder`，改用从 StartupFolder 向上找 Content。
            EngineContentFolderBestEffort() + "/Editor/DebugMaterials/SingleColor/Decal.flax";

        /// <summary>最近一次运行的失败项。</summary>
        public int LastFailureCount { get; private set; }

        /// <summary>最近一次运行做了多少项检查。</summary>
        public int LastCheckCount { get; private set; }

        /// <summary>
        /// 最近一次运行的失败原因（逐条）。
        /// </summary>
        /// <remarks>
        /// 这些串本来只打给编辑器的 Debug Log 面板，而**磁盘日志不一定收得到**（面板先到、文件落后），
        /// 于是"跑完只知道有 6 条失败、看不到是哪 6 条"。暴露出来之后，调用方（HundunAgent 的
        /// `pcg_selftest`）就能把理由直接带回给发起人。`[NoSerialize]`：这是运行期状态，不该被存进场景。
        /// </remarks>
        [NoSerialize]
        public string[] LastFailures { get; private set; } = Array.Empty<string>();

        /// <summary>
        /// 最近一次运行里"因为夹具缺失而没跑"的检查（逐条写下理由）。
        /// </summary>
        /// <remarks>
        /// 跳过与通过必须分得开：一个依赖模型资产的检查在路径失效之后会**静默地什么都不做**，
        /// 于是它在结果里和真跑绿了长得一模一样。实测三条依赖模型的检查（植被放置、样条网格、网格采样）
        /// 在模型路径不存在时全绿，而它们一件都没验。这里把理由捞出来，调用方（`pcg_selftest`）
        /// 会把它作为 skipped 一并返回。
        /// </remarks>
        [NoSerialize]
        public string[] LastSkipped { get; private set; } = Array.Empty<string>();

        /// <summary>本轮跳过的检查；只在 <see cref="RunAll"/> 期间非空。</summary>
        private static List<string> _skipped;

        /// <summary>记一条"这组没跑"并打日志。跳过不是通过，所以要留痕。</summary>
        private static void Skip(string reason)
        {
            _skipped?.Add(reason);
            Debug.Log($"[PCG] SKIP {reason}");
        }

        private bool _hasRun;

        /// <inheritdoc />
        public override void OnStart()
        {
            if (RunOnStart)
                RunAll();
        }

        /// <summary>让 <see cref="RunAll"/> 可以被再调一次，便于在编辑器里手动重跑。</summary>
        public void ResetToRunAgain()
        {
            _hasRun = false;
        }

        /// <summary>跑完所有检查并打一行汇总。</summary>
        /// <returns>失败项个数。</returns>
        public int RunAll()
        {
            // 挡住第二次调用：不然日志里会再叠一整轮。
            if (_hasRun)
            {
                Debug.Log("[PCG] Self-test already ran on this instance; skipping. Call ResetToRunAgain to force a re-run.");
                return LastFailureCount;
            }

            _hasRun = true;
            LastFailureCount = 0;
            LastCheckCount = 0;
            _skipped = new List<string>();

            Debug.Log("========== [PCG] self-test begin ==========");

            var failures = new List<string>();
            var checks = 0;
            var failureCount = 0;

            // 1. 噪声与随机数核心的黄金值测试。
            int noiseFailures = Tests.PCGNoiseGoldenTests.RunAll();
            failureCount += noiseFailures;
            checks++;

            // 2. 节点注册。
            checks++;
            if (!PCGNodeRegistration.IsRegistered && PCGNodeRegistry.Count == 0)
                PCGNodeRegistration.RegisterAll();

            int registered = PCGNodeRegistry.Count;
            if (registered < 3)
            {
                failures.Add($"expected at least 3 registered node types, found {registered}");
                Debug.LogError($"[PCG] FAIL node registration: {registered} type(s) registered");
            }
            else
            {
                Debug.Log($"[PCG] PASS node registration: {registered} type(s)");
            }

            failureCount += TestNodeFactories();
            checks++;

            // 2b. 内建属性通道的契约：不需要场景，但它曾经静默坏过一次。
            checks++;
            failureCount += TestAttributeChannels(failures);

            // 2c. 样条采样器是**只有引擎里才有**的：它读一个活着的 Actor，所以离屏 runner 覆盖不到。
            checks++;
            failureCount += TestSplineSampler(failures);

            // 2d. 图存储这条路径：经引擎的项目目录与临时目录，把一个插件自己的图文件存下来再读回去，
            // 包含设计师真会撞上的两类失败。
            checks++;
            failureCount += TestGraphFileStorage(failures);

            // 3. 图的编译、环的拒绝与执行。
            checks += TestCompiler(failures, ref failureCount);

            // 3b. 体积的分区路径。planner 与 runner 都由离屏覆盖；只有 Actor 能证明的是
            // PCGVolume 自己的接线真的走到了它们、裁到了体积上，并且图真跑起来之后
            // 仍然守住了间距的承诺。
            checks++;
            failureCount += TestVolumePartitioning(failures);

            // 3c. 植被生成器。它是插件唯一的密集放置路径，而它能出的错都是**看不见且昂贵**的：
            // 每次重新生成多出一个 Foliage Actor，或者实例数**累加**而不是替换。
            // 两者都在这里被断言，因为它们需要一个活着的场景。
            checks++;
            failureCount += TestSpawnFoliage(failures, FoliageModelPath);

            // 3c′. 静态模型生成器：演示里**最大**的那条产出链（城市 1330 件）走的就是它，
            // 而它此前只被"烤过"、没被断言过——摆放件数、上限截断、预览不碰场景、没模型时会不会说话。
            checks++;
            failureCount += TestSpawnStaticModel(failures, FoliageModelPath);

            // 3c″. 描述层的 A − B（`within` 圈地 + `except` 挖洞）。离屏数的是**点**，这一组数的是
            // 真落到场景里的**件数**：洞排在别的筛之前时完全可能筛掉了点而一件没少，那种洞在屏幕上等于没挖。
            checks++;
            failureCount += TestSpecAreaHole(failures);
            checks++;
            failureCount += TestProducerRegion(failures);
            checks++;
            failureCount += TestProducerRegionWater(failures);

            // 3d. Prefab 生成器，以及关于它只有引擎能证明的那一件事：它放进场景的东西
            // 归这个体积所有，于是 Clear 收回的正好是那些 Actor、不多不少。
            checks++;
            failureCount += TestSpawnActor(failures, ActorPrefabPath);

            // 3f. 网格采样器。算术由离屏断言；只有引擎能说几何体是否真的从资产里出来了、
            // 以及采出来的点云是否真的跟着 Actor 走。
            checks++;
            failureCount += TestMeshSampler(failures, FoliageModelPath);

            // 3g. 分层生成。规划算术与命名隔离都由离屏检查；只有活着的体积能显示
            // 只有活着的体积能显示：各层是**各自独立**的一次生成、各产出各的点，
            // 以及重新生成是**替换**而不是再叠一份。
            checks++;
            failureCount += TestGenerationLayers(failures);

            // 3h. 流式窗口。选择逻辑对着距离参照在离屏断言过；体积这里要展示的是
            // 它真的把一次请求收窄到了窗口，而且下一次请求会**重切**窗口，
            // 而不是抱着上一次那个不放。
            checks++;
            failureCount += TestStreamingWindow(failures);

            // 3i. 跟随触发。向下取整与截断的差别、以及请求的真值表都在离屏断言过；
            // 只有活着的体积能显示：站着不动不花代价、跨一格恰好付一次请求、
            // 以及带预算的积压会自己走完。
            checks++;
            failureCount += TestStreamingFollow(failures);

            // 3j. 两级生成。规划算术由离屏断言；只有活着的体积能显示粗层**真的在决定**——
            // 同样的内容、严格更少的格子运行次数。
            checks++;
            failureCount += TestHierarchicalGeneration(failures);

            // 3k. 样条网格生成器。分块算术由离屏断言；活着的场景能显示的是
            // 这一排最终挂在体积之下、裁到了自己的格子、并且在预览里不出现。
            checks++;
            failureCount += TestSpawnSplineMesh(failures, FoliageModelPath);

            // 3m. 几何生成器：逐块 / 合并成一个 Actor 两条输出形态，以及"只有独立几何没有点"的输入。
            checks++;
            failureCount += TestSpawnGeometry(failures);

            // 3n. 裁进凸体。解析体积在离屏门里已经逐点对过（§7.6）；这一组只证它在**引擎里**跑得动、
            // 而且产物真的写进场景——纯数据节点同样会踩"离屏能跑、引擎里崩"与"数据对了但没写出去"。
            checks++;
            failureCount += TestClipToConvex(failures);

            // 3l. 按描述生成：一份 .pcgspec → 图 → 真体积 → 场景里真有 Actor。描述解析与图编译都在离屏门里，
            // 只有"体积跑完之后场景里真的多出东西"这一句必须由引擎作证。
            checks++;
            failureCount += TestSpecToScene(failures);
            failureCount += TestTerrainSynthesis(failures);

            // 3e. 贴花生成器的域守卫。本项目不带 Decal 域材质，所以
            // 放置那一半需要设计师；永远会跑的是"域错了的材质会被**点名**拒绝"，
            // 而不是悄悄什么都没投影出来。
            checks++;
            failureCount += TestSpawnDecal(failures, DecalMaterialPath);

            // 4. 属性的写时复制隔离。
            // 加守卫：这里的缺陷可能以**异常**的形式冒出来（一条非法的写入路径会去索引一个空的
            // 列存储）而不是返回 false，而未加守卫的抛出会中断 RunAll，把它之后的每一项检查
            // 都藏起来。地形标定排在更后面，于是一个存储缺陷看起来会是
            // "地形那组从来没报过任何东西"，而不是一次失败。
            checks++;
            try
            {
                if (!TestAttributeCopyOnWrite(out var cowError))
                {
                    failureCount++;
                    failures.Add(cowError);
                    Debug.LogError($"[PCG] FAIL attribute copy-on-write: {cowError}");
                }
                else
                {
                    Debug.Log("[PCG] PASS attribute copy-on-write isolation");
                }
            }
            catch (Exception ex)
            {
                failureCount++;
                failures.Add($"attribute copy-on-write threw {ex.GetType().Name}: {ex.Message}");
                Debug.LogError($"[PCG] FAIL attribute copy-on-write threw {ex.GetType().Name}: {ex.Message}");
            }

            // 5. 重排之下的确定性随机数——增量执行正是靠它。
            checks++;
            if (!TestDeterministicUnderReordering(out var rngError))
            {
                failureCount++;
                failures.Add(rngError);
                Debug.LogError($"[PCG] FAIL deterministic reordering: {rngError}");
            }
            else
            {
                Debug.Log("[PCG] PASS deterministic RNG under reordering");
            }

            // 6. 结构哈希的敏感度，它决定"重编还是复用"。
            checks++;
            if (!TestStructuralHash(out var hashError))
            {
                failureCount++;
                failures.Add(hashError);
                Debug.LogError($"[PCG] FAIL structural hash: {hashError}");
            }
            else
            {
                Debug.Log("[PCG] PASS structural hash distinguishes topology from parameters");
            }

            // 7. 地形两条通路的交叉检查。**它已经搬到"地形上真的有起伏"的那段窗口里**
            //    （见 `TestTerrainSynthesis`：写入之后、还原之前），因为场景里的地形是全零平面，
            //    在这里比两条路等于没比——日志实测就是 `raw heightmap is constant; treating terrain as flat`。
            //    那不是跑绿，是没跑。所以这一组在这里**如实报"跳过"**，并把真跑的位置写清楚。
            if (TestTerrain)
            {
                checks++;
                var terrains = Level.GetActors<Terrain>();

                if (terrains == null || terrains.Length == 0)
                {
                    Debug.Log("[PCG] SKIP terrain cross-check: no terrain in the scene.");
                    Skip("terrain cross-check: 场景里一张 Terrain 都没有");
                }
                else
                {
                    Skip("terrain cross-check: 场景地形是平地，在这里比两条路是空转；" +
                         "真跑在 terrain synthesis 组写入起伏之后的那段窗口里（看该组里 `[PCG] PASS terrain cross-check` 那一行）");
                }
            }

            // 8. 冷资产解析：描述里写下的模型，不该因为"本会话还没人加载过它"而被判成配置错误。
            checks++;
            if (!TestColdAssetResolution(out var coldError))
            {
                failureCount++;
                failures.Add(coldError);
                Debug.LogError($"[PCG] FAIL cold asset resolution: {coldError}");
            }

            LastCheckCount = checks;
            LastFailureCount = failureCount;
            LastFailures = failures.ToArray();
            LastSkipped = _skipped.ToArray();
            _skipped = null;

            if (failureCount == 0)
            {
                Debug.Log($"========== [PCG] self-test PASSED ({checks} group(s)) ==========");
            }
            else
            {
                Debug.LogError($"========== [PCG] self-test FAILED: {failureCount} failure(s) across {checks} group(s) ==========");
                for (int i = 0; i < failures.Count; i++)
                    Debug.LogError($"[PCG]   - {failures[i]}");
            }

            return failureCount;
        }

        /// <summary>检查每个注册的节点类型都能构造出来，并报出它的引脚。</summary>
        private static int TestNodeFactories()
        {
            int failures = 0;

            for (int i = 0; i < PCGNodeRegistry.All.Count; i++)
            {
                var info = PCGNodeRegistry.All[i];
                var node = new Graph.PCGNode { TypeId = info.TypeId, Values = new object[16] };

                var instance = PCGNodeRegistry.CreateNode(node, 0, out var error);
                if (instance == null)
                {
                    failures++;
                    Debug.LogError($"[PCG] FAIL factory for '{info.TypeId}': {error}");
                    continue;
                }

                if (instance.InputPins == null || instance.OutputPins == null)
                {
                    failures++;
                    Debug.LogError($"[PCG] FAIL '{info.TypeId}' returned null pin arrays");
                    continue;
                }

                if (string.IsNullOrEmpty(instance.TypeId))
                {
                    failures++;
                    Debug.LogError($"[PCG] FAIL '{info.TypeId}' reports an empty TypeId");
                    continue;
                }

                Debug.Log($"[PCG]   node '{info.TypeId}': {instance.InputPins.Length} in / " +
                          $"{instance.OutputPins.Length} out, {instance.DescribeParameters()}");
            }

            if (failures == 0)
                Debug.Log($"[PCG] PASS node factories: all {PCGNodeRegistry.All.Count} type(s) constructible");

            return failures;
        }

        /// <summary>
        /// 检查内建属性通道里"被存下来的"与"虚的"那两类的划分。
        /// </summary>
        /// <remarks>
        /// <c>$Slope</c> 没有对应的 <see cref="PCGPoint"/> 字段，所以它必须是一列真列。而写入
        /// 校验器一度拒绝每个以 <c>$</c> 开头的名字，于是 Landscape Sampler 只要产出
        /// 一个点就抛——而一块地形产不出任何点的场景会提前返回，所以那个失败
        /// 是**看不见**的。这一组不需要场景，所以任何一次运行都会触发它。
        /// </remarks>
        private static int TestAttributeChannels(List<string> failures)
        {
            var local = new List<string>();

            try
            {
                var set = new PCGAttributeSet(3);
                set.DeclareAttributeUnsafe(PCGAttributeSet.SlopeName, PCGAttributeType.Float);
                var column = set.GetWritableFloatArray(PCGAttributeSet.SlopeName);
                column[1] = 25.0f;

                if (set.GetFloat(PCGAttributeSet.SlopeName, 1) != 25.0f)
                    local.Add("the stored $Slope channel did not read back the value that was written");

                // 虚通道属于点；这份集合必须用一个带类型的错误拒掉它们，
                // 而不是从一个从来没装过这个名字的字典里漏出一个 NullReferenceException。
                try
                {
                    set.GetFloat(PCGAttributeSet.DensityName, 0);
                    local.Add("reading $Density through the attribute set should have been rejected");
                }
                catch (PCGAttributeException ex)
                {
                    if (ex.Error != PCGAttributeError.BuiltInReadOnly)
                        local.Add($"reading $Density threw {ex.Error}, expected BuiltInReadOnly");
                }
                catch (Exception ex)
                {
                    local.Add($"reading $Density threw an untyped {ex.GetType().Name}");
                }

                try
                {
                    set.SetFloat(PCGAttributeSet.DensityName, 0, 1.0f);
                    local.Add("writing $Density through the attribute set should have been rejected");
                }
                catch (PCGAttributeException)
                {
                    // 预期之内。
                }

                var gathered = set.Gather(new[] { 2, 1 }, 2);
                if (!gathered.Contains(PCGAttributeSet.SlopeName) ||
                    gathered.GetFloat(PCGAttributeSet.SlopeName, 1) != 25.0f)
                {
                    local.Add("Gather dropped the $Slope column, so a filter would discard the cached slope");
                }

                var clone = set.Clone();
                clone.GetWritableFloatArray(PCGAttributeSet.SlopeName)[1] = -1.0f;
                if (set.GetFloat(PCGAttributeSet.SlopeName, 1) != 25.0f)
                    local.Add("Clone did not isolate a write to the stored $Slope column");
            }
            catch (Exception ex)
            {
                local.Add($"attribute channel checks threw {ex.GetType().Name}: {ex.Message}");
            }

            foreach (var message in local)
            {
                failures.Add(message);
                Debug.LogError($"[PCG] FAIL attribute channels: {message}");
            }

            if (local.Count == 0)
                Debug.Log("[PCG] PASS attribute channel contract ($Slope stored, virtual channels refused)");

            return local.Count;
        }

        /// <summary>
        /// 把一个图存进插件自己的文件再读回来。
        /// </summary>
        /// <remarks>
        /// 编解码本身由离屏覆盖。只有跑着的引擎能回答的是它周围的
        /// 路径解析（图都住在其下的那个项目 content 目录）以及磁盘上真实的"先写后读"，
        /// 包含设计师真会撞上的两条失败路径：文件不存在与文件是空的。
        /// 用临时目录是有意的，好让自检永远不会在 Content 树里留下文件。
        /// </remarks>
        private static int TestGraphFileStorage(List<string> failures)
        {
            var local = new List<string>();
            string path = System.IO.Path.Combine(Globals.TemporaryFolder, "pcg_selftest_graph.json");

            try
            {
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);

                // 文件不存在时必须说清它找过哪些地方，而不是只说"没找到"。
                if (PCGGraphFile.TryLoad(path, out _, out string missingError))
                {
                    local.Add("a deleted graph file still loaded");
                }
                else if (string.IsNullOrEmpty(missingError) ||
                         missingError.IndexOf("Looked in", StringComparison.Ordinal) < 0)
                {
                    local.Add("the missing-file rejection did not name the paths it searched");
                }

                var graph = new Graph.PCGGraph
                {
                    Name = "SelfTestGraph",
                    Nodes = new[]
                    {
                        new Graph.PCGNode
                        {
                            TypeId = PCG.Nodes.PCGLandscapeSamplerNode.Id,
                            Values = new object[] { 32, 0.5f, 0.0f, false, 0, true, 1.0f, 90.0f, 0 },
                        },
                        new Graph.PCGNode
                        {
                            TypeId = PCG.Nodes.PCGDensityFilterNode.Id,
                            Values = new object[] { 0.4f, 1.0f, false },
                        },
                    },
                    Edges = new[]
                    {
                        new Graph.PCGEdge { OutputNodeIndex = 0, OutputPinIndex = 0, InputNodeIndex = 1, InputPinIndex = 0 },
                    },
                };

                if (!PCGGraphFile.TrySave(path, graph, out string writeError))
                {
                    local.Add($"saving the graph file failed: {writeError}");
                }
                else if (!PCGGraphFile.TryLoad(path, out var read, out string readError))
                {
                    local.Add($"reading the saved graph file failed: {readError}");
                }
                else
                {
                    if (read.NodeCount != 2 || read.EdgeCount != 1)
                        local.Add($"the round-tripped graph has {read.NodeCount} node(s) and {read.EdgeCount} edge(s), expected 2 and 1");

                    var slot = read.Nodes?[1].Values?[0];
                    if (!(slot is float))
                        local.Add($"a parameter slot came back as {slot?.GetType().Name ?? "null"} instead of float; " +
                                  "the node would ignore it and use its default");
                    else if (Math.Abs((float)slot - 0.4f) > 1e-6f)
                        local.Add($"a parameter slot changed value across the round-trip: {(float)slot}");

                    int before = Execution.PCGGraphCompiler.ComputeStructuralHash(graph);
                    int after = Execution.PCGGraphCompiler.ComputeStructuralHash(read);
                    if (before != after)
                        local.Add($"the structural hash changed across the file round-trip ({before} vs {after}); a volume would recompile on every load");

                    if (!PCGGraphFile.Exists(path))
                        local.Add("Exists disagreed with the load that had just succeeded");
                }

                // 裸名才是关卡设计师会敲的东西；它必须解析到图目录里。
                string resolved = PCGGraphFile.ResolvePath("forest_rocks");
                if (string.IsNullOrEmpty(resolved) ||
                    resolved.IndexOf("forest_rocks", StringComparison.Ordinal) < 0 ||
                    !resolved.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    local.Add($"a bare graph name resolved to something unusable: '{resolved}'");
                }
            }
            catch (Exception ex)
            {
                local.Add($"graph file storage threw {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                try
                {
                    if (System.IO.File.Exists(path))
                        System.IO.File.Delete(path);
                }
                catch (Exception)
                {
                    // 一个残留的临时文件不值得让整轮失败。
                }
            }

            foreach (var message in local)
            {
                failures.Add(message);
                Debug.LogError($"[PCG] FAIL graph file storage: {message}");
            }

            if (local.Count == 0)
                Debug.Log($"[PCG] PASS graph file round-trips through {PCGGraphFile.GraphsFolder}");

            return local.Count;
        }

        /// <summary>
        /// 按描述生成：一份 <c>.pcgspec</c> 描述 → 图 → 真体积 → 场景里真有 Actor。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 这一组存在的唯一理由：**"按设定描述生成场景"这句话的最后一段只有引擎能作证**。描述解析、图编译、
        /// 槽位落位全都在离屏门里，但"体积跑完之后场景里真的多出东西"离屏永远看不见——离屏没有场景。
        /// </para>
        /// <para>
        /// 所以顺序是：先只**编译**仓库里那份整套示例（地貌/植被/水/村庄/道路/洞穴，跑起来几百个 Actor，
        /// 不该拿来当自检的负担，缺文件就跳过），再用一份**小而全**的临时描述真跑两遍：
        /// 预览一遍（应当一个 Actor 都不建）与烘焙一遍（应当建出洞穴的几何），最后清一遍。
        /// </para>
        /// </remarks>
        /// <summary>
        /// 几何生成器的两条输出形态，以及"只有独立几何、没有点"的那种输入。
        /// </summary>
        /// <remarks>
        /// 这三件事都只有活着的场景能证：① 默认一个点一个 Actor；② 合并模式下**只有一个** Actor
        /// （`Model.LODs`/`ModelLOD.Meshes` 是只读数组，所以"一个 Actor 画一整片"只能靠合并成一锅三角面——
        /// 而"到底建了几个"只有场景数得清）；③ 独立几何（没有点）也能被写出来，那才是"几何是数据类型"的落地形态。
        /// </remarks>
        private static int TestSpawnGeometry(List<string> failures)
        {
            const int nodeIndex = 13;

            var local = new List<string>();
            PCGVolume volume = null;
            string uploadedNote = "上传检查没跑到";
            PCGVolume instanceVolume = null;

            try
            {
                volume = new PCGVolume();

                if (!PCGActors.TrySpawn(volume, null))
                {
                    local.Add("could not spawn a PCGVolume to own the geometry spawner test");
                }
                else
                {
                    volume.Name = "PCG_SelfTest_Geometry";

                    var box = PCGMeshBuilder.Box(new Vector3(2.0f, 2.0f, 2.0f));
                    var points = PCGData.Create(3);

                    for (int i = 0; i < 3; i++)
                    {
                        points.Points[i] = PCGPoint.Create(new Vector3(i * 100.0f, 0.0f, 0.0f), 1.0f, i);
                        points.Points[i].Scale = Float3.One;
                        points.Points[i].UpdateBounds(new Vector3(1.0f, 1.0f, 1.0f));
                    }

                    points.Meshes = new[] { box, box, box };

                    var context = new PCGExecutionContext
                    {
                        Seed = 9,
                        CurrentNodeId = nodeIndex,
                        GenerationBounds = new BoundingBox(new Vector3(-400, -50, -400), new Vector3(400, 50, 400)),
                        Source = volume,
                    };

                    var perPiece = MakeGeometrySpawner(nodeIndex, merge: false);
                    perPiece.OnExecute(context, new[] { points });

                    // 数**记账读数**而不是原始孩子：`Actor.Destroy` 延迟到帧末，所以同一帧里"生成→清空→再生成"
                    // 会让上一批已经排队的对象还留在子级里（这一组一轮里要跑三次生成，非用记账不可）。
                    int pieceActors = volume.GeneratedActorCount;

                    if (perPiece.LastError != null)
                        local.Add($"逐块模式报了 {perPiece.LastError}");

                    if (perPiece.SpawnedCount != 3 || pieceActors != 3)
                        local.Add($"逐块模式应当建 3 个 Actor，报 {perPiece.SpawnedCount} 个 / 体积下有 {pieceActors} 个生成物");

                    // 清掉再验合并：**一个** Actor 是 C② 的全部意义（一片石柱群不该是几百个 Actor）。
                    volume.Clear();

                    var merged = MakeGeometrySpawner(nodeIndex, merge: true);
                    merged.OnExecute(context, new[] { points });

                    int mergedActors = volume.GeneratedActorCount;

                    if (merged.LastError != null)
                        local.Add($"合并模式报了 {merged.LastError}");

                    if (merged.SpawnedCount != 1 || mergedActors != 1)
                        local.Add($"合并模式应当只建 1 个 Actor，报 {merged.SpawnedCount} 个 / 体积下有 {mergedActors} 个生成物");

                    // 合并后的网格要**盖住三块**：摆放是烘进顶点的，所以顶点数是单块的三倍。
                    // 少了它，"只建了一个 Actor"也可能是"只写进了一块"。按名字找那个合并出来的 Actor——
                    // 同一帧里上一批还挂在下面，按下标取会取错。
                    var mergedMesh = FindMeshOf(volume, "GeometryMerged");

                    if (mergedMesh == null || mergedMesh.VertexCount != box.VertexCount * 3)
                        local.Add($"合并后的网格应当是单块的 3 倍顶点（{box.VertexCount * 3}），" +
                                  $"实得 {(mergedMesh == null ? "没有网格" : mergedMesh.VertexCount.ToString())}");

                    // 只有独立几何、没有点：这是"几何能独立于点存在"能被写出来的证据。
                    volume.Clear();

                    var loose = new PCGData(Array.Empty<PCGPoint>())
                    {
                        Geometry = new[]
                        {
                            PCGGeometryItem.At(box, new Vector3(0.0f, 0.0f, 0.0f)),
                            PCGGeometryItem.At(box, new Vector3(200.0f, 0.0f, 0.0f)),
                        },
                    };

                    var fromLoose = MakeGeometrySpawner(nodeIndex, merge: false);
                    fromLoose.OnExecute(context, new[] { loose });

                    int looseActors = volume.GeneratedActorCount;

                    if (fromLoose.LastError != null)
                        local.Add($"只有独立几何时报了 {fromLoose.LastError}");

                    if (fromLoose.SpawnedCount != 2 || looseActors != 2)
                        local.Add($"只有独立几何时应当建 2 个 Actor，报 {fromLoose.SpawnedCount} 个 / " +
                                  $"体积下有 {looseActors} 个生成物");

                    // 清空必须把它精确收回（产出归属是结构性的）。
                    int removed = volume.Clear();
                    int left = volume.GeneratedActorCount;

                    if (left != 0)
                        local.Add($"清空之后还留着 {left} 个生成物");

                    if (removed != 2)
                        local.Add($"清空应当收回 2 个，报 {removed} 个");

                    // 实例化输出用**另一个体积**：`Actor.Destroy` 延迟到帧末，同一帧里换模式的产物会互相干扰，
                    // 而这一条要数的是"宿主有几个孩子"——换个干净的体积才数得清。
                    instanceVolume = new PCGVolume();

                    if (!PCGActors.TrySpawn(instanceVolume, null))
                    {
                        local.Add("could not spawn a second PCGVolume for the instanced geometry test");
                    }
                    else
                    {
                        instanceVolume.Name = "PCG_SelfTest_GeometryInstanced";

                        var instanceContext = new PCGExecutionContext
                        {
                            Seed = 11,
                            CurrentNodeId = nodeIndex,
                            GenerationBounds = new BoundingBox(new Vector3(-400, -50, -400), new Vector3(400, 50, 400)),
                            Source = instanceVolume,
                        };

                        var instanced = MakeGeometrySpawner(nodeIndex, merge: false, toFoliage: true);
                        instanced.OnExecute(instanceContext, new[] { points });

                        if (instanced.LastError != null)
                            local.Add($"实例化模式报了 {instanced.LastError}");

                        // 一个宿主、五个实例——而体积下**一个生成物 Actor 都没有**（实例不是 Actor）。
                        var hosts = instanceVolume.GetChildren<Foliage>();
                        int hostCount = hosts?.Length ?? 0;
                        int instanceCount = hostCount == 1 && hosts[0] != null ? hosts[0].InstancesCount : -1;

                        if (hostCount != 1 || instanceCount != 3)
                            local.Add($"实例化模式应当给 1 个 Foliage 宿主、里面 3 个实例，" +
                                      $"实得 {hostCount} 个宿主 / {instanceCount} 个实例");

                        if (instanceVolume.GeneratedActorCount != 0)
                            local.Add($"实例化模式下不该有产出 Actor，实得 {instanceVolume.GeneratedActorCount} 个");

                        if (instanced.SpawnedCount != 3)
                            local.Add($"实例化模式应当报 3 个实例，报 {instanced.SpawnedCount} 个");

                        // 两种输出形态互斥：同时置位必须报出来、且不动场景（替作者挑一个会在屏幕上看着像跑通了）。
                        var both = MakeGeometrySpawner(nodeIndex, merge: true, toFoliage: true);
                        both.OnExecute(instanceContext, new[] { points });

                        if (both.LastError == null)
                            local.Add("合并与实例化同时置位时没有报出冲突");

                        // 切过的网格要**真的进到 Actor 渲染的那份模型里**：拿盒子的高度当判据（2 → 1），
                        // 三角形数还要**变多**（裁剪出的侧面 + 封盖），而只数三角形是看不出高度的。
                        instanceVolume.Clear();

                        var cutPoints = PCGData.Create(1);
                        cutPoints.Points[0] = PCGPoint.Create(Vector3.Zero, 1.0f, 0);
                        cutPoints.Points[0].Scale = Float3.One;
                        cutPoints.Points[0].UpdateBounds(new Vector3(1, 1, 1));
                        cutPoints.Meshes = new[] { box };

                        var cutNode = new PCG.Nodes.PCGCutNode(new PCGNodeRegistry.PCGNodeSerializationData(
                            PCG.Nodes.PCGCutNode.Id, new object[] { Vector3.Zero, Vector3.UnitY, false }, nodeIndex));
                        var cutData = cutNode.Execute(instanceContext, new[] { cutPoints });

                        if (cutNode.LastError != null)
                            local.Add($"平面切割报了 {cutNode.LastError}");

                        var cutSpawner = MakeGeometrySpawner(nodeIndex, merge: false);
                        cutSpawner.OnExecute(instanceContext, new[] { cutData });

                        var cutMesh = FindMeshOf(instanceVolume, "Geometry");

                        if (cutMesh == null)
                        {
                            local.Add("切过的几何没有写出 Actor 的网格");
                        }
                        else
                        {
                            float height = cutMesh.Box.Size.Y;

                            if (height > 1.2f || height < 0.8f)
                                local.Add($"切过一半的盒子高度应当是 1 左右，实得 {height:F2}");

                            if (cutMesh.TriangleCount <= box.TriangleCount)
                                local.Add($"切过的网格三角形应当变多（裁剪 + 封盖），实得 " +
                                          $"{cutMesh.TriangleCount} vs 原 {box.TriangleCount}");
                        }

                        // 树与房屋这两档新形状：要证明的是**上传进虚拟 Model 的那一份网格**就是算术那一份，
                        // 并且两件共用生成器的模型缓存时没有撞车（键要是把形状并成一份，屏幕上就是"树长成房子"）。
                        // 离屏门能验体积/水密/绕序，但看不见引擎的模型写入，所以这一半只能在这里补。
                        // **另起一个体积**而不是 Clear 复用：`Actor.Destroy` 延迟到帧末，同一帧清空再重生时，
                        // 下面读子级会把上一批也算进来（这一族的坑踩过一次：报 1 个、实际排着两个）。
                        var shapeVolume = new PCGVolume();

                        if (!PCGActors.TrySpawn(shapeVolume, null))
                        {
                            local.Add("could not spawn a PCGVolume for the uploaded-mesh check");
                        }
                        else
                        {
                            try
                            {
                                shapeVolume.Name = "PCG_SelfTest_Shapes";

                                var shapeContext = new PCGExecutionContext
                                {
                                    Seed = 11,
                                    CurrentNodeId = nodeIndex,
                                    GenerationBounds = new BoundingBox(new Vector3(-400, -50, -400),
                                                                       new Vector3(400, 50, 400)),
                                    Source = shapeVolume,
                                };

                                var shapePoints = PCGData.Create(2);
                                shapePoints.Points[0] = PCGPoint.Create(new Vector3(-60, 0, 0), 1.0f, 0);
                                shapePoints.Points[0].Scale = Float3.One;
                                shapePoints.Points[0].UpdateBounds(new Vector3(5.0f, 15.0f, 5.0f));
                                shapePoints.Points[1] = PCGPoint.Create(new Vector3(60, 0, 0), 1.0f, 1);
                                shapePoints.Points[1].Scale = Float3.One;
                                shapePoints.Points[1].UpdateBounds(new Vector3(6.0f, 5.0f, 5.0f));

                                // 树按 n=12 ⇒ 10n-6 = 114 顶点 / 4n-2 = 46 面；房屋 ⇒ 30 顶点 / 16 面。
                                shapePoints.Meshes = new[]
                                {
                                    PCGMeshBuilder.Tree(new Vector3(10.0f, 30.0f, 10.0f), 12),
                                    PCGMeshBuilder.House(new Vector3(12.0f, 9.0f, 10.0f)),
                                };

                                var shapeSpawner = MakeGeometrySpawner(nodeIndex, merge: false);
                                shapeSpawner.OnExecute(shapeContext, new[] { shapePoints });

                                var shapeActors = shapeVolume.GetChildren<StaticModel>();
                                int shapeActorCount = shapeActors?.Length ?? 0;

                                if (shapeActorCount != 2)
                                {
                                    local.Add($"一棵树 + 一栋房应当各建一个 Actor，实得 {shapeActorCount} 个");
                                }
                                else
                                {
                                    var firstUploaded = shapeActors[0].Model?.LODs?[0]?.Meshes?[0];
                                    var secondUploaded = shapeActors[1].Model?.LODs?[0]?.Meshes?[0];

                                    if (firstUploaded == null || secondUploaded == null)
                                    {
                                        local.Add("读不到上传后的网格（虚拟 Model 没写进 LOD？）");
                                    }
                                    else
                                    {
                                        int firstKey = firstUploaded.VertexCount * 1000 + firstUploaded.TriangleCount;
                                        int secondKey = secondUploaded.VertexCount * 1000 + secondUploaded.TriangleCount;

                                        // 与先后顺序无关：这两档必须**各出现一次**。撞车时两份会是同一个键，这条就红。
                                        bool pair = (firstKey == 114046 && secondKey == 30016) ||
                                                    (firstKey == 30016 && secondKey == 114046);

                                        if (!pair)
                                            local.Add($"引擎里那两份网格不是一棵树配一栋房：" +
                                                      $"{firstUploaded.VertexCount}/{firstUploaded.TriangleCount} 与 " +
                                                      $"{secondUploaded.VertexCount}/{secondUploaded.TriangleCount}，" +
                                                      "期望 114/46 与 30/16（两份相同就说明模型缓存撞了键）");
                                        else if (ReferenceEquals(firstUploaded, secondUploaded))
                                            local.Add("树与房屋拿到的是同一个 Mesh 实例 ⇒ ShapeKey 把两种形状并成了一份");
                                        else
                                            uploadedNote = $"上传后两份是 {firstUploaded.VertexCount}/{firstUploaded.TriangleCount}" +
                                                           $" 与 {secondUploaded.VertexCount}/{secondUploaded.TriangleCount}，" +
                                                           $"盒子 {firstUploaded.Box.Size} / {secondUploaded.Box.Size}";
                                    }
                                }
                            }
                            finally
                            {
                                Actor.Destroy(shapeVolume);
                            }
                        }
                    }

                    // 几何生成器的"薄层"与"归因"。它以前只会说 "every piece fell outside the cell"，
                    // 而演示里那一层其实是被**抽签**吃光的——照着那句话查会白查一轮，所以：
                    // ① 三个去向各报一个数（格外 / 抽签 / 没有可用网格）；
                    // ② 期望不足一件的那一层，哪怕这次摆出来了也要说；
                    // ③ 密度 1.0 的对照必须闭嘴，否则①②是永远绿的空检查。
                    var thinPoints = PCGData.Create(3);
                    for (int i = 0; i < thinPoints.Count; i++)
                    {
                        thinPoints.Points[i] = PCGPoint.Create(new Vector3(i * 3.0f, 0.0f, 0.0f), 0.3f, i);
                        thinPoints.Points[i].Scale = Float3.One;
                        thinPoints.Points[i].UpdateBounds(new Vector3(1, 1, 1));
                    }

                    thinPoints.Meshes = new[] { box, box, box };

                    // 这一份检查自己带体积：探针摆出的件数归它所有，末尾自己收回（组的其余部分不受影响）。
                    var probeVolume = new PCGVolume();

                    if (!PCGActors.TrySpawn(probeVolume, null))
                        local.Add("几何薄层检查起不了自己的体积，那一半根本没跑");

                    probeVolume.Name = "PCG_SelfTest_GeometryThin";

                    var probeContext = new PCGExecutionContext
                    {
                        Seed = 7,
                        World = new PCGWorldContext(),
                        GenerationBounds = new BoundingBox(new Vector3(-100, -100, -100), new Vector3(100, 100, 100)),
                    };

                    int thinRunsPlaced = 0;

                    for (int seed = 7; seed <= 9; seed++)
                    {
                        var thinContext = new PCGExecutionContext
                        {
                            Seed = seed,
                            World = probeContext.World,
                            GenerationBounds = probeContext.GenerationBounds,
                            Source = probeVolume,
                        };

                        var thin = MakeGeometrySpawner(nodeIndex, merge: false, densityThreshold: 0.5f);
                        thin.OnExecute(thinContext, new[] { thinPoints });

                        if (thin.LastError == null)
                            local.Add($"seed {seed}: 三个点各 0.3 密度（整层期望 0.9 件）摆出 " +
                                      $"{thin.SpawnedCount} 件却什么都没说");

                        if (thin.SpawnedCount > 0)
                        {
                            thinRunsPlaced++;

                            if (thin.LastError != null &&
                                thin.LastError.IndexOf("expected only", StringComparison.Ordinal) < 0)
                            {
                                local.Add($"seed {seed}: 摆出了 {thin.SpawnedCount} 件但没报出期望件数：{thin.LastError}");
                            }
                        }
                    }

                    if (thinRunsPlaced == 0)
                        local.Add("3 seeds × 3 points at density 0.3 never placed a piece, so the geometry " +
                                  "\"placed but under-one-expected\" branch went unexercised — raise the fixture's " +
                                  "density a little (keep the sum under 1) rather than trusting this as passing");

                    // 归因：把点全搬到生成范围之外，那句话必须说"3 fell outside"，
                    // 而不是把抽签也一起喊成"都落在格外"。
                    var farPoints = PCGData.Create(3);
                    for (int i = 0; i < farPoints.Count; i++)
                    {
                        farPoints.Points[i] = PCGPoint.Create(new Vector3(9000.0f + i * 3.0f, 0.0f, 9000.0f), 1.0f, i);
                        farPoints.Points[i].Scale = Float3.One;
                        farPoints.Points[i].UpdateBounds(new Vector3(1, 1, 1));
                    }

                    farPoints.Meshes = new[] { box, box, box };

                    var far = MakeGeometrySpawner(nodeIndex, merge: false, densityThreshold: 0.5f);
                    // 裁切看的是 ClipRegion/ClipCell，**不是** GenerationBounds（先前弄错过一次：
                    // 只给生成范围时这些点全被算成"在区域内"，那条归因断言于是空转报绿）。
                    var farBox = new BoundingBox(new Vector3(-100, -100, -100), new Vector3(100, 100, 100));
                    var farContext = new PCGExecutionContext
                    {
                        Seed = 7,
                        World = new PCGWorldContext(),
                        GenerationBounds = farBox,
                        ClipRegion = farBox,
                        Source = probeVolume,
                    };

                    far.OnExecute(farContext, new[] { farPoints });

                    if (far.LastError == null || far.LastError.IndexOf("3 fell outside the region being generated",
                            StringComparison.Ordinal) < 0)
                    {
                        local.Add($"三个点都在范围外时没有报出 3 个是被裁切挡住的：{far.LastError ?? "（没说话）"}");
                    }

                    // 正向对照：密度 1.0 恒过抽签 ⇒ 3 件、且**不许**有抱怨。
                    var richPoints = PCGData.Create(3);
                    for (int i = 0; i < richPoints.Count; i++)
                    {
                        richPoints.Points[i] = PCGPoint.Create(new Vector3(i * 3.0f, 0.0f, 0.0f), 1.0f, i);
                        richPoints.Points[i].Scale = Float3.One;
                        richPoints.Points[i].UpdateBounds(new Vector3(1, 1, 1));
                    }

                    richPoints.Meshes = new[] { box, box, box };

                    var rich = MakeGeometrySpawner(nodeIndex, merge: false, densityThreshold: 0.5f);
                    rich.OnExecute(probeContext, new[] { richPoints });

                    if (rich.SpawnedCount != 3)
                        local.Add($"三个密度 1.0 的点该摆出 3 件，实得 {rich.SpawnedCount}");
                    if (rich.LastError != null)
                        local.Add($"期望 3 件的那一层不该说话：{rich.LastError}");

                    probeVolume.Clear();
                    Actor.Destroy(probeVolume);

                    if (local.Count == 0)
                        Debug.Log($"[PCG] PASS geometry spawner: {pieceActors} then {mergedActors} actor(s), " +
                                  $"merged mesh {box.VertexCount * 3} vertex/vertices, " +
                                  $"{removed} removed by Clear；{uploadedNote}");                }
            }
            catch (Exception ex)
            {
                local.Add($"geometry spawner check threw {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                DestroyTestVolume(ref volume);
                DestroyTestVolume(ref instanceVolume);
            }

            foreach (var message in local)
            {
                failures.Add(message);
                Debug.LogError($"[PCG] FAIL geometry spawner: {message}");
            }

            return local.Count;
        }

        /// <summary>把测试用的体积清空并拆掉；拆解失败不该决定这次运行的结果。</summary>
        /// <summary>
        /// 3n. 裁进凸体（`ClipToConvex`）：解析体积在离屏门里已经逐点对过，这一组只证**在引擎里跑得动、
        /// 而且产物真的写进场景**——纯数据节点同样会踩到"离屏能跑、引擎里崩"与"数据对了但生成器没写出去"。
        /// 判据用**裁剪后的包围盒**：一块 4³ 的方料裁进 2×6×2 的盒区域（盒比它高）之后，
        /// 只剩下 x∈[-1,1]、y∈[-2,2]、z∈[-1,1] 那一段——解析值，不靠"看着像"。
        /// </summary>
        private static int TestClipToConvex(List<string> failures)
        {
            const int nodeIndex = 19;
            const float tolerance = 0.05f;

            var local = new List<string>();
            PCGVolume volume = null;

            try
            {
                volume = new PCGVolume();

                if (!PCGActors.TrySpawn(volume, null))
                {
                    local.Add("could not spawn a PCGVolume to own the clip test");
                }
                else
                {
                    volume.Name = "PCG_SelfTest_Clip";

                    var material = PCGMeshBuilder.Box(new Vector3(4.0f, 4.0f, 4.0f));
                    var points = PCGData.Create(1);

                    points.Points[0] = PCGPoint.Create(Vector3.Zero, 1.0f, 0);
                    points.Points[0].Scale = Float3.One;
                    points.Points[0].UpdateBounds(new Vector3(2.0f, 2.0f, 2.0f));
                    points.Meshes = new[] { material };

                    var context = new PCGExecutionContext
                    {
                        Seed = 11,
                        CurrentNodeId = nodeIndex,
                        GenerationBounds = new BoundingBox(new Vector3(-400, -50, -400), new Vector3(400, 50, 400)),
                        Source = volume,
                    };

                    var node = new PCGClipToConvexNode(new PCGNodeRegistry.PCGNodeSerializationData(
                        PCGClipToConvexNode.Id,
                        new object[] { PCGClipToConvexNode.BoxShape, Vector3.Zero, new Vector3(2.0f, 6.0f, 2.0f), 16 },
                        0));

                    var clipped = node.Execute(context, new[] { points });

                    if (node.LastError != null)
                        local.Add($"裁切在引擎里报了 {node.LastError}");

                    var mesh = clipped?.Meshes != null && clipped.Meshes.Length == 1 ? clipped.Meshes[0] : null;

                    if (mesh == null || mesh.VertexCount == 0)
                    {
                        local.Add("裁完之后没有网格");
                    }
                    else
                    {
                        var minimum = mesh.Vertices[0];
                        var maximum = mesh.Vertices[0];

                        for (int i = 1; i < mesh.VertexCount; i++)
                        {
                            minimum = Vector3.Min(minimum, mesh.Vertices[i]);
                            maximum = Vector3.Max(maximum, mesh.Vertices[i]);
                        }

                        bool boundsOk = Math.Abs(minimum.X + 1.0f) <= tolerance && Math.Abs(maximum.X - 1.0f) <= tolerance &&
                                        Math.Abs(minimum.Y + 2.0f) <= tolerance && Math.Abs(maximum.Y - 2.0f) <= tolerance &&
                                        Math.Abs(minimum.Z + 1.0f) <= tolerance && Math.Abs(maximum.Z - 1.0f) <= tolerance;

                        if (!boundsOk)
                            local.Add($"裁完的包围盒应当是 [-1,-2,-1]..[1,2,1]，实得 {minimum}..{maximum}");
                    }

                    // 真的摆出去：数据对了却没写进场景，正是这一组独立存在的理由。
                    var spawner = MakeGeometrySpawner(nodeIndex, merge: false);

                    spawner.OnExecute(context, new[] { clipped });

                    if (spawner.LastError != null)
                        local.Add($"裁完之后的生成器报了 {spawner.LastError}");

                    if (spawner.SpawnedCount != 1 || volume.GeneratedActorCount != 1)
                        local.Add($"裁完的那一块应当建 1 个 Actor，报 {spawner.SpawnedCount} 个 / " +
                                  $"体积下有 {volume.GeneratedActorCount} 个生成物");

                    // **圆柱区域（16 边）这一档**：以前整次裁剪会被拒（§7.6② 的"边界②"），
                    // 2026-10-11 把"去重半径与焊接半径"对齐之后它切得动了。这一条是**能力**的引擎侧证据——
                    // 纯核在离屏已经量过，但"离屏能跑、引擎里被拒"正是这一组存在的理由。
                    // 期望值 **12.730** 是**离屏 sweep 量出来的**（同一块 4³ 方料、同一个 R=1 / h=6 / 16 边的圆柱区域），
                    // 不是从参数推出来的——参数推出来的值会让"圆柱面的定义（半径落在顶点还是面）"这种事蒙对蒙错。
                    // **圆柱区域两档都走一遍**：16 边与 32 边。32 边这一档是 2026-10-11 才切得动的
                    // （去重判据从"分类带"改成"浮点噪声级"+ 接环清洗两步），而且它**真的踩到过新路径**
                    //（"发丝"段成串），所以值得在引擎里也真跑一次，不能只信离屏。
                    // 期望值 **12.730 / 12.607** 都是**离屏 sweep 量出来的**（同一块 4³ 方料、同一个 R=1 / h=6 的圆柱区域），
                    // 不是从参数推的——参数推的值会让"圆柱面的半径落在顶点还是面"这种事蒙对蒙错。
                    foreach (var (cylinderSides, expectedVolume) in new[] { (16, 12.730f), (32, 12.607f) })
                    {
                        var cylinderMaterial = PCGMeshBuilder.Box(new Vector3(4.0f, 4.0f, 4.0f));
                        var cylinderPoints = PCGData.Create(1);

                        cylinderPoints.Points[0] = PCGPoint.Create(Vector3.Zero, 1.0f, 0);
                        cylinderPoints.Points[0].Scale = Float3.One;
                        cylinderPoints.Points[0].UpdateBounds(new Vector3(2.0f, 2.0f, 2.0f));
                        cylinderPoints.Meshes = new[] { cylinderMaterial };

                        var cylinderNode = new PCGClipToConvexNode(new PCGNodeRegistry.PCGNodeSerializationData(
                            PCGClipToConvexNode.Id,
                            new object[]
                            {
                                PCGClipToConvexNode.CylinderShape, Vector3.Zero,
                                new Vector3(2.0f, 6.0f, cylinderSides), cylinderSides,
                            },
                            0));

                        var cylinderClipped = cylinderNode.Execute(context, new[] { cylinderPoints });

                        if (cylinderNode.LastError != null)
                            local.Add($"{cylinderSides} 边圆柱区域又被拒了：{cylinderNode.LastError}");

                        var cylinderMesh = cylinderClipped?.Meshes != null && cylinderClipped.Meshes.Length == 1
                            ? cylinderClipped.Meshes[0]
                            : null;

                        if (cylinderMesh == null || cylinderMesh.TriangleCount == 0)
                        {
                            local.Add($"{cylinderSides} 边圆柱区域裁完之后没有网格");
                            continue;
                        }

                        double cylinderSum = 0.0;

                        for (int t = 0; t < cylinderMesh.TriangleCount; t++)
                        {
                            var ca = cylinderMesh.Vertices[cylinderMesh.Triangles[t * 3]];
                            var cb = cylinderMesh.Vertices[cylinderMesh.Triangles[t * 3 + 1]];
                            var cc = cylinderMesh.Vertices[cylinderMesh.Triangles[t * 3 + 2]];
                            cylinderSum += Vector3.Dot(ca, Vector3.Cross(cb, cc)) / 6.0;
                        }

                        float cylinderVolume = (float)Math.Abs(cylinderSum);

                        if (Math.Abs(cylinderVolume - expectedVolume) > tolerance)
                            local.Add($"{cylinderSides} 边圆柱区域裁出来的体积应当是 {expectedVolume:F3}（离屏 sweep 量的），" +
                                      $"实得 {cylinderVolume:F3}");
                    }
                }
            }
            catch (Exception ex)
            {
                local.Add($"裁进凸体的自检抛了异常：{ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                DestroyTestVolume(ref volume);
            }

            if (local.Count > 0)
            {
                failures.AddRange(local);
                return 1;
            }

            return 0;
        }

        private static void DestroyTestVolume(ref PCGVolume volume)
        {
            if (volume == null)
                return;

            try
            {
                volume.Clear();
                Actor.Destroy(volume);
            }
            catch (Exception)
            {
                // 同上。
            }

            volume = null;
        }

        private static PCG.Nodes.PCGSpawnGeometryNode MakeGeometrySpawner(
            int nodeIndex, bool merge, bool toFoliage = false, float densityThreshold = 0.0f) =>
            new PCG.Nodes.PCGSpawnGeometryNode(new PCGNodeRegistry.PCGNodeSerializationData(
                PCG.Nodes.PCGSpawnGeometryNode.Id, new object[] { 100, densityThreshold, 0, merge, toFoliage }, nodeIndex));

        /// <summary>按名字片段找体积下一个生成物 Actor 的首个网格；找不到给 null。</summary>
        /// <remarks>
        /// 按名字而不是下标取：同一帧里被清掉的那一批还挂在子级下（`Actor.Destroy` 延迟到帧末），
        /// 按下标会取到别人。
        /// </remarks>
        private static Mesh FindMeshOf(Actor owner, string nameFragment)
        {
            var children = owner?.GetChildren<StaticModel>();

            if (children == null)
                return null;

            for (int i = 0; i < children.Length; i++)
            {
                var child = children[i];

                if (child == null || child.Name == null ||
                    child.Name.IndexOf(nameFragment, StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                return child.Model?.LODs?[0]?.Meshes?[0];
            }

            return null;
        }

        private static int TestSpecToScene(List<string> failures)
        {
            var local = new List<string>();
            string specPath = System.IO.Path.Combine(Globals.TemporaryFolder, "pcg_selftest_spec.pcgspec");
            string graphPath = System.IO.Path.Combine(Globals.TemporaryFolder, "pcg_selftest_spec.json");
            PCGVolume volume = null;

            const string smallSpec =
                "world 自检小洞\n" +
                "seed 20261008\n" +
                "scatter rocks points 12 density 1\n" +
                "road from -200 -100 to 200 100 cell 50 spacing 80\n" +
                "cave depth 200 segments 6 radius 40 tunnels 3 sides 8\n";

            try
            {
                // 1. 仓库里那份整套示例：只编译，不跑。它证明"示例描述与当前节点表对得上"——但描述文件不在
                //    工程里时跳过而不是失败：这是内容，不是代码。
                var shipped = PCGSpecFile.TryLoad("valley", path => Guid.Empty);

                if (!PCGSpecFile.Exists("valley"))
                {
                    Debug.Log($"[PCG] SKIP shipped spec: {PCGSpecFile.ResolvePath("valley")} 不在，跳过示例描述这一项");
                }
                else if (!shipped.Ok)
                {
                    local.Add($"仓库里那份示例描述没编译过：{shipped.Error}");
                }
                // 22 / 14：路这一条链现在自己带一个程序化几何节点（路面不再借 Flax 的 Plane 图元，
                // 那东西是竖直面片，摆出来的路是一排墙），于是比记录过的 21 / 13 多一个节点、一条连线。
                else if (shipped.Graph.NodeCount != 22 || shipped.Graph.EdgeCount != 14)
                {
                    local.Add($"示例描述编译成 {shipped.Graph.NodeCount} 节点 / {shipped.Graph.EdgeCount} 连线，不是记录的 22 / 14");
                }

                // 2. 小而全的临时描述：散布链 + 道路链 + 洞穴链，模型一律解析不出来（自检不该依赖资产）。
                System.IO.File.WriteAllText(specPath, smallSpec);

                var compiled = PCGSpecFile.TryLoad(specPath, path => Guid.Empty);

                if (!compiled.Ok)
                {
                    local.Add($"自检描述没编译过：{compiled.Error}");
                }
                else if (compiled.Graph.NodeCount != 8 || compiled.Graph.EdgeCount != 5)
                {
                    local.Add($"自检描述编译成 {compiled.Graph.NodeCount} 节点 / {compiled.Graph.EdgeCount} 连线，期望 8 / 5");
                }
                else if (!PCGGraphFile.TrySave(graphPath, compiled.Graph, out string saveError))
                {
                    local.Add($"自检描述编译出来的图存不下来：{saveError}");
                }
                else
                {
                    volume = new PCGVolume();

                    if (!PCGActors.TrySpawn(volume, null))
                    {
                        local.Add("自检没能建出 PCGVolume");
                    }
                    else
                    {
                        volume.Name = "PCG_SelfTest_Spec";
                        volume.GraphPath = graphPath;
                        volume.Seed = 20261008;
                        volume.Volume = new BoundingBox(
                            new Vector3(-400.0f, -400.0f, -400.0f), new Vector3(400.0f, 100.0f, 400.0f));

                        // 预览：点要出来，Actor 一个都不该建。这是"预览不碰场景"承诺的引擎侧形态，
                        // 也正是四个生成器的 DryRun 早退必须排在查资产**之前**的原因。
                        volume.Mode = PCGGenerationMode.Preview;
                        bool previewRan = volume.Generate();
                        int previewPoints = volume.LastOutput?.Count ?? 0;
                        int previewActors = volume.GeneratedActorCount;

                        if (!previewRan)
                            local.Add("预览跑完说没有产出");
                        if (previewPoints <= 0)
                            local.Add("预览一个点都没出来，后面烘焙那一步也就无从谈起");
                        if (previewActors != 0)
                            local.Add($"预览建了 {previewActors} 个 Actor，预览应当一个都不建");

                        // 烘焙：洞穴自己就出网格，所以它一定会在场景里建出东西——这正是"按描述生成场景"的证据。
                        volume.Mode = PCGGenerationMode.Bake;
                        bool baked = volume.Generate();
                        int bakedActors = volume.GeneratedActorCount;

                        if (!baked)
                            local.Add("烘焙跑完说没有产出");
                        if (bakedActors <= 0)
                            local.Add("烘焙之后场景里一个 Actor 都没有；洞穴的几何链路没落地");

                        // 再跑一遍：同样的图和种子不该把场景翻倍。翻倍说明每次生成都在旧结果上累加，
                        // 而"重新生成"在设计师眼里永远是"替换"。
                        volume.Generate();
                        int again = volume.GeneratedActorCount;

                        if (again != bakedActors)
                            local.Add($"再烘焙一遍后 Actor 从 {bakedActors} 变成 {again}，重新生成应当是替换而不是累加");

                        // 清空：所有权是结构性的（生成的物件挂在这个体积下），所以清空就是"销毁我的孩子"。
                        int removed = volume.Clear();
                        int left = volume.GeneratedActorCount;

                        if (left != 0)
                            local.Add($"清空之后还留着 {left} 个 Actor");
                        if (removed != again)
                            local.Add($"清空销毁了 {removed} 个 Actor，而刚跑完时有 {again} 个");

                        // 重新打开场景（或编辑器重启）之后 `_result` 是空的——它是运行期状态、不进场景——
                        // 而体积之下的产出还在。这时读数必须去数孩子，否则一个装着 1403 个 Actor 的体积
                        // 会报 0（实测：演示场景重载后六个体积**全部**报 0，而 `actor_find` 一查有 1620 个）。
                        // `Clear()` 刚跑完就是"账面为空"这个状态，所以在这里手工摆一个"看起来像产出"的孩子
                        // 来钉住那条回退分支：它必须是 1，不能被当成"什么都没生成"。
                        var stray = new StaticModel
                        {
                            Name = PCG.Execution.PCGSpawnNaming.ActorName("Stray", 0, 0, 0, 0),
                        };

                        PCGActors.TrySpawn(stray, volume);

                        if (volume.GeneratedActorCount != 1)
                            local.Add($"没有账面时应当数孩子，期望 1 个、实得 {volume.GeneratedActorCount}");

                        Actor.Destroy(stray);

                        if (local.Count == 0)
                        {
                            Debug.Log($"[PCG] PASS spec to scene: 描述 {compiled.Graph.NodeCount} 节点 → " +
                                      $"预览 {previewPoints} 点 / 0 Actor → 烘焙 {bakedActors} Actor（重跑不累加，清空归零）");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                local.Add($"按描述生成那一组抛了 {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (volume != null)
                {
                    try
                    {
                        volume.Clear();
                        Actor.Destroy(volume);
                    }
                    catch (Exception)
                    {
                        // 留下一个测试体积不值得让整轮失败。
                    }
                }

                try
                {
                    if (System.IO.File.Exists(specPath))
                        System.IO.File.Delete(specPath);

                    if (System.IO.File.Exists(graphPath))
                        System.IO.File.Delete(graphPath);
                }
                catch (Exception)
                {
                    // 临时文件同理。
                }
            }

            foreach (var message in local)
            {
                failures.Add(message);
                Debug.LogError($"[PCG] FAIL spec to scene: {message}");
            }

            return local.Count;
        }

        /// <summary>
        /// 地形合成的**写入端**：描述 → 配方那一半在离屏门里，这里只证引擎才能作证的三件事。
        /// </summary>
        /// <remarks>
        /// <para>
        /// ① 跑了带 `terrain` 的图之后，场景里多出一个 **PCG 自己的 Terrain**；
        /// ② 它**不在体积的父子关系里**（否则 `Clear()` 会按前缀把它当产出销毁），所以清空之后它还在；
        /// ③ 同一张图、同一个种子连跑两次，**产出的点位置一模一样**，而且明显**不贴着零平面**——
        /// 高度读不回来（托管侧没有高度 getter），所以"喂给采样端的那组数是对的"只能这样证：
        /// 路的高低取自高度来源，拿到的若是旧高度（零平面），点会全部趴在 y≈0。
        /// </para>
        /// <para>
        /// 不做"读回来比对高度"：那条路要标定，而标定在本仓还没定（见设计文档附录 B.2）。
        /// </para>
        /// </remarks>
        private static int TestTerrainSynthesis(List<string> failures)
        {
            var local = new List<string>();
            string specPath = System.IO.Path.Combine(Globals.TemporaryFolder, "pcg_selftest_terrain.pcgspec");
            string graphPath = System.IO.Path.Combine(Globals.TemporaryFolder, "pcg_selftest_terrain.json");
            PCGVolume volume = null;
            Terrain terrain = null;
            string terrainName = null;

            // 一座土丘把地面抬高 120（形状的位置在找到地形之后按**它的**位置拼进去，
            // 于是自检不依赖用户把 PCG 的地形建在哪儿）：只要采样端拿到的是新地形，
            // 穿过它的那条路就该被抬起来。
            string terrainSpec = null;

            Terrain FindTerrain(string name)
            {
                foreach (var scene in Level.Scenes)
                {
                    if (scene == null)
                        continue;

                    var roots = scene.GetChildren<Actor>();

                    if (roots == null)
                        continue;

                    for (int i = 0; i < roots.Length; i++)
                    {
                        if (roots[i] is Terrain candidate && string.Equals(candidate.Name, name, StringComparison.Ordinal))
                            return candidate;
                    }
                }

                return null;
            }

            // 自检要写的那块地形：与写入端同一条选法（先认 PCG_Terrain 前缀，否则场景里第一张 Terrain）。
            Terrain FindPcgTerrain()
            {
                Terrain first = null;

                foreach (var scene in Level.Scenes)
                {
                    if (scene == null)
                        continue;

                    var roots = scene.GetChildren<Actor>();

                    if (roots == null)
                        continue;

                    for (int i = 0; i < roots.Length; i++)
                    {
                        if (!(roots[i] is Terrain candidate))
                            continue;

                        if (candidate.Name != null &&
                            candidate.Name.StartsWith(PCGTerrainWriter.TerrainActorPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            return candidate;
                        }

                        if (first == null)
                            first = candidate;
                    }
                }

                return first;
            }

            try
            {
                // 先找 PCG 自己的那块地形。**没有就跳过**，而不是像其余组那样自己建一个：
                // 托管侧没有建 patch 的 API（`AddPatches`/`Setup` 都没导出），脚本新建的 Terrain
                // chunk 边长为 0、一格 patch 都没有，写高度会静默失败（`SetupPatchHeightMap`
                // 在缺 patch 时返回 true 表示失败）。
                terrain = FindPcgTerrain();

                if (terrain == null)
                {
                    Debug.Log("[PCG] SKIP terrain synthesis: 场景里一张 Terrain 都没有" +
                              "（写入端要写进一块**已经有 patch** 的地形，而托管侧建不了 patch）");
                    return local.Count;
                }

                terrainName = terrain.Name;

                // 探测区必须落在**已有的那一格 patch 里面**：patch (0,0) 覆盖
                // [地形位置, +100*4*chunk]，而地形位置常常就是世界原点——拿它当盒心会让半边盒子
                // 落进不存在的 patch (-1,·)，写入端会（正确地）拒绝并跳过，于是这一组白跑。
                // 取 patch 的中点最稳：无论设计师把地形摆在哪、chunk 设多大，它都在 patch 里面。
                float patchSpan = terrain.PatchSize;
                float centerX = terrain.Position.X + patchSpan * 0.5f;
                float centerZ = terrain.Position.Z + patchSpan * 0.5f;

                // 洞的位置：**避开道路走廊**（路沿对角走），否则洞会把路点吃掉、干扰下面那几条"落点被抬高"的断言。
                // 半径取 **300**：地形顶点间距是引擎的 `UnitsPerVertex = 100`（不是本插件能调的），
                // 洞是**逐顶点**的掩码 ⇒ 一个 120 半径的洞只盖住 1~2 个顶点、实际范围是**方块状**的
                //（实测第一版半径 120 时"洞外"那颗 +180 的点也被挖掉了）。所以洞要**明显大于一个顶点格**。
                float holeX = centerX - 300.0f;
                float holeZ = centerZ + 250.0f;
                const float holeRadius = 300.0f;

                terrainSpec =
                    "world 自检地形\n" +
                    "seed 20261008\n" +
                    $"terrain noise 0 shapes \"dome {centerX:F0} {centerZ:F0} 300 120\"" +
                    $" holes \"dome {holeX:F0} {holeZ:F0} {holeRadius:F0} 0\"\n" +
                    $"road from {centerX - 300.0f:F0} {centerZ - 200.0f:F0} to {centerX + 300.0f:F0} {centerZ + 200.0f:F0} cell 40 spacing 100\n" +
                    // 一层会落地的摆件：下面那条"生成物在不在合成后的地面上"的判据要有对象可量。
                    "scatter pins points 80 model \"Cube\"\n";

                System.IO.File.WriteAllText(specPath, terrainSpec);

                var compiled = PCGSpecFile.TryLoad(specPath, path => Guid.Empty);

                if (!compiled.Ok)
                {
                    local.Add($"自检地形描述没编译过：{compiled.Error}");
                }
                else if (compiled.Graph.Settings == null || compiled.Graph.Settings.Terrain == null)
                {
                    local.Add("地形描述编译出来的图设置里没有配方（描述层 → 图设置这一段断了）");
                }
                else if (!PCGGraphFile.TrySave(graphPath, compiled.Graph, out string saveError))
                {
                    local.Add($"自检地形图存不下来：{saveError}");
                }
                else
                {
                    volume = new PCGVolume();

                    if (!PCGActors.TrySpawn(volume, null))
                    {
                        local.Add("自检没能建出 PCGVolume");
                    }
                    else
                    {
                        volume.Name = "PCG_SelfTest_Terrain";
                        volume.GraphPath = graphPath;
                        volume.Seed = 20261008;
                        volume.Mode = PCGGenerationMode.Bake;

                        // 盒子围着那块地形摆，形状也在同一个位置，所以"被抬高"这件事与地形建在哪儿无关。
                        volume.Volume = new BoundingBox(
                            new Vector3(centerX - 400.0f, -400.0f, centerZ - 400.0f),
                            new Vector3(centerX + 400.0f, 400.0f, centerZ + 400.0f));

                        bool first = volume.Generate();
                        var firstPoints = SnapshotPointPositions(volume);
                        terrain = FindTerrain(terrainName);

                        if (!first || firstPoints.Count == 0)
                        {
                            local.Add("带地形的图跑完没有产出点，后面几项都无从谈起");
                        }
                        else
                        {
                            float highest = float.MinValue;

                            foreach (var position in firstPoints)
                                highest = Math.Max(highest, position.Y);

                            // 141 是地块被抬高之后该到的高度量级；这里只要"明显离开零平面"，
                            // 因为它要证的是**采样端拿到了新地形**，不是高度数值标定得准。
                            if (highest < 10.0f)
                            {
                                local.Add($"产出的点最高才到 y={highest:F2}；地形像是没喂给采样端（依旧贴着零平面）");
                            }

                            // 重新生成一遍：同样的图与种子，落点必须一模一样。
                            volume.Generate();
                            var secondPoints = SnapshotPointPositions(volume);
                            string mismatch = FirstPositionMismatch(firstPoints, secondPoints);

                            if (mismatch != null)
                                local.Add("同一张图重跑一遍，落点变了：" + mismatch);
                        }

                        if (terrain == null)
                        {
                            local.Add($"场景根下没有找到地形 actor '{terrainName}'（写入端没建出来，或建成了体积的孩子）");
                        }
                        else
                        {
                            // **在"写入之后、还原之前"这段窗口里再验一次两条路一致**（2026-10-11）：
                            // 此刻地形上真的躺着一层刚写下去的起伏（上面那次 Generate 写的土丘 + 道路），
                            // 标定与两路比较才有意义。下面那个独立的调用跑在**已还原的平地**上，
                            // 那条现在会被"跨度太小"判红——因为它确实什么都没证明。两条都要留着：
                            // 一条证明"有起伏时两路一致"，一条证明"平地这条退化分支不冒充证据"。
                            if (!TestTerrainPaths(terrain, 5, out string roughPathError))
                                local.Add($"写入起伏之后的两路一致检查失败：{roughPathError}");

                            // **生成物到底落在哪儿**（2026-10-12）：此前"摆件贴没贴地"只能看截图——
                            // 而一次真实的误判就是这么来的：盆地里 y≈-81 的一批其实是**合成后的盆底**，
                            // 数值门全绿也说不清这是对的还是埋了。所以拿射线真值逐件量一遍。
                            var groundTruth = new PCGTerrainSampler(terrain, PCGTerrainCalibration.Invalid);
                            var props = volume.GetChildren<StaticModel>();
                            int grounded = 0;
                            float worstGround = 0.0f;
                            float minGround = float.MaxValue;
                            float maxGround = float.MinValue;

                            foreach (var prop in props ?? Array.Empty<StaticModel>())
                            {
                                if (prop == null)
                                    continue;

                                var at = prop.Position;
                                var hit = groundTruth.Sample(at.X, at.Z, PCGTerrainSampleMode.RayCast);

                                if (!hit.Hit)
                                    continue;

                                float delta = Math.Abs(at.Y - hit.Height);

                                if (delta > worstGround)
                                    worstGround = delta;

                                minGround = Mathf.Min(minGround, hit.Height);
                                maxGround = Mathf.Max(maxGround, hit.Height);
                                grounded++;
                            }

                            if (grounded == 0)
                            {
                                local.Add("自检体积名下没有可对地面采样的 StaticModel，落地判据根本没跑到");
                            }
                            else if (maxGround - minGround < 1.0f)
                            {
                                local.Add($"这些落点处的地面跨度只有 {maxGround - minGround:F2}，" +
                                          "逐件比较是空转（起伏没写进去，或摆件全落在同一处）");
                            }
                            else if (worstGround > 2.0f)
                            {
                                local.Add($"{grounded} 件生成物离射线真值地面最多 {worstGround:F2} 世界单位：" +
                                          "落地投影没落在刚合成出来的地面上（埋进地里或浮在空中）");
                            }
                            else
                            {
                                Debug.Log($"[PCG] PASS grounded props: {grounded} 件都落在真值地面 ≤ {worstGround:F2} " +
                                          $"世界单位内，落点处地面跨度 {maxGround - minGround:F2}（不是平地，不是空转）");
                            }

                            // **洞掩码在引擎里到底有没有生效**（2026-10-11）：掩码的字节约定（0 = 洞、
                            // 255 = 有地面）是从引擎源码读出来的（`TerrainPatch::CacheHolesData`），
                            // 离屏也逐格钉过——但"引擎收下之后那一片真的没有地面了吗"只能在引擎里量。
                            // 做法：往洞中心与洞外各打几条向下的射线，数命中。
                            // **洞外那几条是必需的正面同夹具**：如果整块地形都没写进去，洞内洞外都会打空，
                            // 那时"洞内打不到"就说明不了任何事。
                            var holeProbe = new PCGTerrainSampler(terrain, PCGTerrainCalibration.Invalid);
                            int insideHits = 0;
                            int outsideHits = 0;
                            const int holeProbes = 5;

                            for (int i = 0; i < holeProbes; i++)
                            {
                                // 洞内：半径的一半以内（沿 X 排开，全部落在体积盒里）。
                                float inX = holeX - holeRadius * 0.5f + i * (holeRadius * 0.25f);

                                if (holeProbe.Sample(inX, holeZ, PCGTerrainSampleMode.RayCast).Hit)
                                    insideHits++;

                                // 洞外：半径的 2 倍以外、**同侧排开**——留出"顶点格是 100 世界单位"的余量，
                                // 否则会落在那个方块状洞的边上（实测第一版就是这样误报 4/5）。
                                float outX = holeX + holeRadius * (2.0f + i * 0.25f);

                                if (holeProbe.Sample(outX, holeZ, PCGTerrainSampleMode.RayCast).Hit)
                                    outsideHits++;
                            }

                            Debug.Log($"[PCG] holes mask in engine: 洞内命中 {insideHits}/{holeProbes}、" +
                                      $"洞外命中 {outsideHits}/{holeProbes}");

                            // 读数**写进失败理由里**：磁盘日志落后于 Debug Log 面板，而 MCP 返回的 `reasons`
                            // 是唯一能立刻读到的地方——这样即使这条红了，也知道具体几个点命中（否则只能猜）。
                            if (outsideHits != holeProbes)
                            {
                                // 洞外都没铺满 ⇒ 洞内那几条说明不了什么（也可能是夹具/覆盖的问题）。
                                local.Add($"洞掩码探针：洞外只有 {outsideHits}/{holeProbes} 个点打到地面" +
                                          $"（洞内 {insideHits}/{holeProbes}）——洞外那一圈没铺满，洞内那几条不算数");
                            }
                            else if (insideHits != 0)
                            {
                                // 洞外全中、洞内也中 ⇒ 引擎侧"洞那一片还有几何"。掩码本身是对的（字节约定取自
                                // 引擎源码 + 离屏逐格钉过），所以这一条是**引擎行为的读数**，不是"数写错了"。
                                local.Add($"洞掩码探针：洞外 {outsideHits}/{holeProbes} 全中，但**洞内也中了 " +
                                          $"{insideHits}/{holeProbes}**——掩码写对了（离屏逐格钉过），这里是" +
                                          "「引擎那一片仍有几何」的读数：要么同帧内几何没重建完、要么洞在引擎里另有条件");
                            }

                            // 归属：地形**不是**体积的孩子，否则清空会把它一起销毁。
                            var children = volume.GetChildren<Actor>();

                            if (children != null)
                            {
                                for (int i = 0; i < children.Length; i++)
                                {
                                    if (children[i] == terrain)
                                        local.Add("地形 actor 被挂进了体积之下；清空会把它当产出销毁");
                                }
                            }

                            volume.Clear();

                            if (FindTerrain(terrainName) == null)
                                local.Add("清空体积之后地形 actor 不见了（它本该活过一次又一次的重新生成）");
                        }

                        if (local.Count == 0)
                        {
                            Debug.Log($"[PCG] PASS terrain synthesis: 建出 '{terrainName}'（不在体积之下、清空后还在），" +
                                      $"两次生成落点一致且被抬到 y={HighestPoint(volume):F1}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                local.Add($"地形合成那一组抛了 {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                // 地形**不销毁**，但必须把自检写进去的高度**还原**：它很可能是用户自己那张地形
                // （写入端在场景里没有 PCG_Terrain 时就写它），跑一次自检不该在那儿留下一座土丘。
                // 还原的依据是写入端留下的备份文件，所以这一段同时也在用真路径验证"能不能回退"。
                try
                {
                    if (PCGTerrainWriter.TryRestore((Actor)terrain ?? volume, out int restored, out string restoreError))
                    {
                        Debug.Log($"[PCG] terrain synthesis self-test restored {restored} patch(es)");
                    }
                    else
                    {
                        Debug.LogWarning($"[PCG] FAIL terrain synthesis: 自检收尾还原地形失败：{restoreError}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PCG] FAIL terrain synthesis: 自检收尾还原地形抛了 {ex.GetType().Name}: {ex.Message}");
                }

                if (volume != null)
                {
                    try
                    {
                        volume.Clear();
                        Actor.Destroy(volume);
                    }
                    catch (Exception)
                    {
                        // 同理。
                    }
                }

                try
                {
                    if (System.IO.File.Exists(specPath))
                        System.IO.File.Delete(specPath);

                    if (System.IO.File.Exists(graphPath))
                        System.IO.File.Delete(graphPath);
                }
                catch (Exception)
                {
                    // 临时文件同理。
                }
            }

            foreach (var message in local)
            {
                failures.Add(message);
                Debug.LogError($"[PCG] FAIL terrain synthesis: {message}");
            }

            return local.Count;
        }

        /// <summary>把这次运行的产出点位置抄一份出来（重新生成会换掉数据，比对要在副本上做）。</summary>
        private static List<Vector3> SnapshotPointPositions(PCGVolume volume)
        {
            var positions = new List<Vector3>();
            var output = volume?.LastOutput;
            var points = output?.Points;

            if (points == null)
                return positions;

            for (int i = 0; i < points.Length; i++)
                positions.Add(points[i].Position);

            return positions;
        }

        /// <summary>两份落点表不一致时给出第一处差异；一致时给 null。</summary>
        private static string FirstPositionMismatch(List<Vector3> first, List<Vector3> second)
        {
            if (first.Count != second.Count)
                return $"点数从 {first.Count} 变成 {second.Count}";

            for (int i = 0; i < first.Count; i++)
            {
                if (Vector3.Distance(first[i], second[i]) > 0.01f)
                    return $"第 {i} 个点从 {first[i]} 变成 {second[i]}";
            }

            return null;
        }

        private static float HighestPoint(PCGVolume volume)
        {
            float highest = 0.0f;
            var points = volume?.LastOutput?.Points;

            if (points == null)
                return highest;

            for (int i = 0; i < points.Length; i++)
                highest = Math.Max(highest, points[i].Position.Y);

            return highest;
        }

        /// <summary>
        /// 在一个分区过的图上跑一个**真的** <see cref="PCGVolume"/>，检查体积那条路径承诺了什么。
        /// </summary>
        /// <remarks>
        /// <para>
        /// planner 与 runner 都已被离屏 harness 覆盖，所以这一组不再重复那份
        /// 算术。它要说的是只有 Actor 能证明的三件事：<c>PCGVolume.Generate</c>
        /// 真的走到了分区那条路、合并出来的点留在体积自己的世界盒子里、以及
        /// 图里的间距规则在真实采样器于每格重启点下标之后仍然跨边界成立。
        /// 这一句是上一条的收尾：每格都重启下标。
        /// </para>
        /// <para>
        /// 预览模式，所以场景里什么都没加、这一轮也不留垃圾。
        /// </para>
        /// </remarks>
        private static int TestVolumePartitioning(List<string> failures)
        {
            const float spacing = 60.0f;
            const float cellSize = 400.0f;

            var local = new List<string>();
            string path = System.IO.Path.Combine(Globals.TemporaryFolder, "pcg_selftest_partition.json");
            PCGVolume volume = null;

            try
            {
                var graph = new Graph.PCGGraph
                {
                    Name = "SelfTestPartition",
                    Nodes = new[]
                    {
                        new Graph.PCGNode
                        {
                            TypeId = PCG.Nodes.PCGVolumeSamplerNode.Id,
                            Values = new object[] { 480, 0, 1.0f, false, 1.0f },
                        },
                        new Graph.PCGNode
                        {
                            TypeId = PCG.Nodes.PCGSelfPruningFilterNode.Id,
                            Values = new object[] { spacing, false, true },
                        },
                    },
                    Edges = new[] { new Graph.PCGEdge { OutputNodeIndex = 0, InputNodeIndex = 1 } },
                };

                if (!PCGGraphFile.TrySave(path, graph, out string saveError))
                {
                    local.Add($"could not save the partition test graph: {saveError}");
                }
                else
                {
                    volume = new PCGVolume();
                    if (!PCGActors.TrySpawn(volume, null))
                    {
                        local.Add("could not spawn a PCGVolume for the partition test");
                    }
                    else
                    {
                        volume.Name = "PCG_SelfTest_Partition";
                        volume.GraphPath = path;
                        volume.Mode = PCGGenerationMode.Preview;
                        volume.Seed = 20261006;
                        volume.Volume = new BoundingBox(
                            new Vector3(-400.0f, -100.0f, -200.0f), new Vector3(400.0f, 100.0f, 200.0f));

                        var world = volume.TransformVolumeToWorld();
                        var cells = PCGPartitionPlanner.Plan(world, cellSize, spacing, out _);
                        if (cells == null || cells.Count < 2)
                        {
                            local.Add("the test volume does not split into more than one cell, so nothing about borders is being tested");
                        }

                        // 先跑基线：如果一次非分区运行连自己的间距都守不住，
                        // 分区那次跑成什么样都不能怪它。
                        volume.Partitioned = false;
                        if (!volume.Generate())
                        {
                            local.Add("the unpartitioned run produced nothing, so the partitioned run proves nothing");
                        }
                        else
                        {
                            int violations = CountSpacingViolations(volume.LastOutput, spacing);
                            if (violations != 0)
                                local.Add($"the unpartitioned baseline already breaks its own {spacing} spacing in {violations} pair(s)");
                        }

                        volume.Partitioned = true;
                        volume.PartitionCellSize = cellSize;
                        // 有意留成 -1：这一轮用的 padding 于是由图自己的
                        // 剪枝器推出来，而那正是设计师实际会留着的设置。
                        volume.PartitionPadding = -1.0f;

                        if (!volume.Generate())
                        {
                            local.Add($"the partitioned run produced nothing ({cells?.Count ?? 0} cell(s) of {cellSize})");
                        }
                        else
                        {
                            int kept = volume.LastOutput.Count;
                            int outside = 0;

                            var points = volume.LastOutput.Points;
                            for (int i = 0; i < points.Length; i++)
                            {
                                var p = points[i].Position;
                                if (p.X < world.Minimum.X - 0.01f || p.X > world.Maximum.X + 0.01f ||
                                    p.Z < world.Minimum.Z - 0.01f || p.Z > world.Maximum.Z + 0.01f)
                                {
                                    outside++;
                                }
                            }

                            int crowded = CountSpacingViolations(volume.LastOutput, spacing);


                            if (outside != 0)
                                local.Add($"{outside} of {kept} partitioned point(s) escaped the volume, so the cell padding leaked into the output");
                            // 分区**不承诺**与"整片一次跑"给出同一套布局：采样器的逐点随机数按点下标取，
                            // 而下标在每格内部重启（设计文档 §7.3 明说），于是同一对近邻在两格的两次运行里
                            // 谁先被剪掉可以不同，边界上就可能有个别近邻对同时存活。填充救不了它——
                            // 那需要跨格的全局一致次序；"固定点集"下的接缝性在离屏 `PartitionTests` 里另有断言。
                            // 所以这里只守一条设计真能承诺的性质：边界上的个例不放大（最多每格一对）。
                            int allowed = cells != null ? cells.Count : 0;

                            if (crowded > allowed)
                            {
                                local.Add($"the partitioned run has {crowded} point pair(s) closer than {spacing}, " +
                                          $"more than the {allowed} border pairs the per-cell restart can explain");
                            }
                            else if (crowded > 0)
                            {
                                Debug.Log($"[PCG] note: partitioned run has {crowded} border pair(s) closer than {spacing} " +
                                          "(per-cell layouts differ by design; see design doc §7.3)");
                            }
                            if (kept > 0 && outside == 0 && crowded == 0)
                                Debug.Log($"[PCG] PASS partitioned volume: {kept} point(s) across {cells.Count} cell(s), spacing kept");
                        }

                        // 规划器拒绝的格子尺寸必须以一句话中止这次运行，而不是生成一个
                        // 单格结果、看着像分区过的。
                        volume.PartitionCellSize = 0.0f;
                        if (volume.Generate())
                            local.Add("a cell size of zero was accepted by the volume instead of being rejected");
                    }
                }
            }
            catch (Exception ex)
            {
                local.Add($"partitioned volume run threw {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (volume != null)
                {
                    try
                    {
                        Actor.Destroy(volume);
                    }
                    catch (Exception)
                    {
                        // 把一个测试 Actor 留在场上不值得让整轮失败。
                    }
                }

                try
                {
                    if (System.IO.File.Exists(path))
                        System.IO.File.Delete(path);
                }
                catch (Exception)
                {
                    // 临时图文件同理。
                }
            }

            foreach (var message in local)
            {
                failures.Add(message);
                Debug.LogError($"[PCG] FAIL partitioned volume: {message}");
            }

            return local.Count;
        }

        /// <summary>有多少对点比图自己的间距规则允许的更近。</summary>
        private static int CountSpacingViolations(PCGData data, float spacing)
        {
            var points = data?.Points;
            if (points == null)
                return 0;

            int violations = 0;
            float squared = spacing * spacing;

            for (int i = 0; i < points.Length; i++)
            {
                for (int j = i + 1; j < points.Length; j++)
                {
                    float dx = points[i].Position.X - points[j].Position.X;
                    float dz = points[i].Position.Z - points[j].Position.Z;
                    if (dx * dx + dz * dz < squared - 0.001f)
                        violations++;
                }
            }

            return violations;
        }

        /// <summary>
        /// 在一个活着的场景里跑植被生成器，检查它的复用设计所声称的那两件事。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 植被实例**不是** Actor，所以场景里没有任何东西列出它们、大纲里也没有任何条目会显示
        /// 泄漏：一个每次重新生成都多建一个 <c>Foliage</c>、或者累加实例而不是
        /// 替换的生成器，会**看不见地**长大，直到关卡变慢。那正是这一组
        /// 存在的意义，而它只能在 <c>Level.SpawnActor</c> 是真的时候被抓到。
        /// </para>
        /// <para>
        /// 配置错误那一半不需要资产、永远会跑；放置那一半用引擎自带的
        /// 盒子模型，连它都载不进来时带一句明确的话跳过。
        /// </para>
        /// </remarks>
        /// <summary>
        /// 静态模型生成器的引擎侧断言：件数、上限截断、预览不碰场景、没有模型时必须开口。
        /// </summary>
        /// <remarks>
        /// 这条链是演示里最大的产出（城市 1330 件），而它此前只被烘焙过、没有任何断言——
        /// "有东西长出来"与"长出来的件数是对的"不是一回事，插件里 <c>TrySpawn</c> 判据写反那类缺陷
        /// （产物对、计数错）正是藏在这种只看不数的地方。空的哪一种（点全在格子外）由植被组末尾
        /// 那条同夹具用例覆盖，这里不重复。
        /// </remarks>
        private static int TestSpawnStaticModel(List<string> failures, string modelPath)
        {
            const int nodeIndex = 12;
            const int pointCount = 12;
            const int cap = 4;

            var local = new List<string>();
            PCGVolume volume = null;

            // 摆放跑过之后体积应当拥有几件；-1 = 那一半根本没跑（模型没载进来），
            // 拆解时就不该再拿"应该有 N 件"去判它——那会把一个跳过报成失败。
            int expectedOwned = -1;

            try
            {
                volume = new PCGVolume();
                if (!PCGActors.TrySpawn(volume, null))
                {
                    local.Add("could not spawn a PCGVolume to own the static-model test actors");
                }
                else
                {
                    volume.Name = "PCG_SelfTest_StaticModel";

                    var points = new PCGPoint[pointCount];
                    for (int i = 0; i < pointCount; i++)
                    {
                        points[i] = PCGPoint.Create(new Vector3(-55.0f + i * 10.0f, 0.0f, 0.0f), 1.0f, i);
                        points[i].UpdateBounds(new Vector3(5.0f, 5.0f, 5.0f));
                    }

                    var data = new PCGData(points);
                    var region = new BoundingBox(new Vector3(-60, -10, -10), new Vector3(60, 10, 10));
                    var context = new PCGExecutionContext
                    {
                        Seed = 7,
                        CurrentNodeId = nodeIndex,
                        GenerationBounds = region,
                        Source = volume,
                    };

                    // 槽序：ModelId, DensityThreshold, ScaleMultiplier, RandomYaw, MaxActors。
                    object[] ValuesOf(Guid id, int maxActors) => new object[] { id, 0.0f, 1.0f, false, maxActors };

                    var unset = new PCG.Nodes.PCGSpawnStaticModelNode(new PCGNodeRegistry.PCGNodeSerializationData(
                        PCG.Nodes.PCGSpawnStaticModelNode.Id, ValuesOf(Guid.Empty, 50), nodeIndex));

                    var passthrough = unset.OnExecute(context, new[] { data });
                    if (unset.LastError == null)
                        local.Add("a static-model spawner with no model reported no problem");
                    if (passthrough == null || passthrough.Count != pointCount)
                        local.Add($"a failed run should pass the points through, got {passthrough?.Count ?? -1}");
                    if (CountStaticModelActors(volume) != 0)
                        local.Add("a static-model spawner with no model still created actors");

                    var model = LoadFixtureModel(modelPath);

                    if (model == null)
                    {
                        Skip($"static model placement: {modelPath} did not load as a Model.");
                    }
                    else
                    {
                        var first = new PCG.Nodes.PCGSpawnStaticModelNode(new PCGNodeRegistry.PCGNodeSerializationData(
                            PCG.Nodes.PCGSpawnStaticModelNode.Id, ValuesOf(model.ID, 50), nodeIndex));

                        var placed = first.OnExecute(context, new[] { data });
                        if (first.LastError != null)
                            local.Add($"the first static-model run reported: {first.LastError}");
                        if (first.SpawnedCount != pointCount)
                            local.Add($"expected {pointCount} actor(s), got {first.SpawnedCount}");
                        if (placed == null || placed.Count != pointCount)
                            local.Add($"the spawner should pass on every point it placed, got {placed?.Count ?? -1}");
                        if (CountStaticModelActors(volume) != pointCount)
                            local.Add($"the scene holds {CountStaticModelActors(volume)} actor(s), expected {pointCount}");

                        // 上限：截断必须自己说，不能静默少摆（静默的少摆与"点本来就少"同形）。
                        var capped = new PCG.Nodes.PCGSpawnStaticModelNode(new PCGNodeRegistry.PCGNodeSerializationData(
                            PCG.Nodes.PCGSpawnStaticModelNode.Id, ValuesOf(model.ID, cap), nodeIndex));
                        capped.OnExecute(context, new[] { data });

                        if (capped.SpawnedCount != cap)
                            local.Add($"MaxActors={cap} over {pointCount} points placed {capped.SpawnedCount}");
                        if (!capped.LastRunWasCapped)
                            local.Add($"MaxActors={cap} over {pointCount} points did not report being capped");
                        if (CountStaticModelActors(volume) != pointCount + cap)
                            local.Add($"after the capped run the scene holds {CountStaticModelActors(volume)} actor(s), " +
                                      $"expected {pointCount + cap}");

                        expectedOwned = pointCount + cap;

                        // 预览：一件都不摆，也不许动上面那些——正面孪生就是刚才那 16 件，
                        // 所以"计数为 0"单独不成立：它必须与"场景没变"一起成立。
                        var dry = new PCG.Nodes.PCGSpawnStaticModelNode(new PCGNodeRegistry.PCGNodeSerializationData(
                            PCG.Nodes.PCGSpawnStaticModelNode.Id, ValuesOf(model.ID, 50), nodeIndex));
                        var dryOut = dry.OnExecute(context.AsDryRun(), new[] { data });

                        if (dry.SpawnedCount != 0)
                            local.Add($"a preview run reported placing {dry.SpawnedCount} actor(s)");
                        if (dryOut == null || dryOut.Count != pointCount)
                            local.Add($"a preview run should still pass its points on, got {dryOut?.Count ?? -1}");
                        if (CountStaticModelActors(volume) != expectedOwned)
                            local.Add($"a preview run changed the scene: {CountStaticModelActors(volume)} actor(s), " +
                                      $"was {expectedOwned}");
                    }

                    // 薄到"等于没长"的那一层必须自己说话。抽签是 `Density >= rng.NextFloat()`，
                    // 所以全层密度之和就是期望件数：3 个点各 0.3 ⇒ 期望 0.9 件。
                    // 这种层有两种结局，**都得报**：一件没摆（确定空）与摆了一件（这一次运气站在作者这边，
                    // 换个种子就整层消失）——只报前者会让"稀"和"没了"在屏幕上长得一样。
                    var thinPoints = new PCGPoint[3];
                    for (int i = 0; i < thinPoints.Length; i++)
                    {
                        thinPoints[i] = PCGPoint.Create(new Vector3(-5.0f + i * 5.0f, 0.0f, 0.0f), 0.3f, i);
                        thinPoints[i].UpdateBounds(new Vector3(5.0f, 5.0f, 5.0f));
                    }

                    var thinData = new PCGData(thinPoints);

                    // 三个种子各跑一遍：单个种子上这一层可能一件都不摆（那走的是"0 件"那条老话），
                    // 于是**新加的这句"摆出来了但期望不足一件"可能一次都没被执行**——编译过不等于跑过。
                    // 抽签是按种子定死的，所以这里不是碰运气：这一轮要是有个种子摆出了东西，往后每轮都会有。
                    int thinPlacedRuns = 0;
                    int thinPlacedTotal = 0;

                    for (int seed = 7; seed <= 9; seed++)
                    {
                        var thinContext = new PCGExecutionContext
                        {
                            Seed = seed,
                            CurrentNodeId = nodeIndex,
                            GenerationBounds = region,
                            Source = volume,
                        };

                        var thin = new PCG.Nodes.PCGSpawnStaticModelNode(
                            new PCGNodeRegistry.PCGNodeSerializationData(
                                PCG.Nodes.PCGSpawnStaticModelNode.Id,
                                new object[] { model.ID, 0.5f, 1.0f, false, 50 }, nodeIndex));

                        thin.OnExecute(thinContext, new[] { thinData });

                        if (thin.LastError == null)
                            local.Add($"seed {seed}: a layer whose 3 points expected only 0.9 props placed " +
                                      $"{thin.SpawnedCount} and said nothing about being that thin");

                        thinPlacedTotal += thin.SpawnedCount;

                        if (thin.SpawnedCount > 0)
                        {
                            thinPlacedRuns++;

                            if (thin.LastError != null && thin.LastError.IndexOf("expected only",
                                    StringComparison.Ordinal) < 0)
                            {
                                local.Add($"seed {seed}: placed {thin.SpawnedCount} on a 0.9-expected layer but " +
                                          $"didn't name the expectation: {thin.LastError}");
                            }
                        }
                    }

                    if (thinPlacedRuns == 0)
                        local.Add("3 seeds × 3 points at density 0.3 never placed a single prop, so the " +
                                  "\"placed but under-one-expected\" branch went unexercised — raise the fixture's " +
                                  "density a little (keep the sum under 1) rather than trusting this as passing");

                    // 正向同夹具对照：密度 1.0 的点**必然**过抽签（1.0 恒 >= 任何 [0,1) 的抽签值），
                    // 期望 3 件、摆出 3 件，这时就该闭嘴——否则上面那条"必须说话"是永远绿的空检查。
                    var richPoints = new PCGPoint[3];
                    for (int i = 0; i < richPoints.Length; i++)
                    {
                        richPoints[i] = PCGPoint.Create(new Vector3(-5.0f + i * 5.0f, 0.0f, 0.0f), 1.0f, i);
                        richPoints[i].UpdateBounds(new Vector3(5.0f, 5.0f, 5.0f));
                    }

                    var rich = new PCG.Nodes.PCGSpawnStaticModelNode(new PCGNodeRegistry.PCGNodeSerializationData(
                        PCG.Nodes.PCGSpawnStaticModelNode.Id, new object[] { model.ID, 0.5f, 1.0f, false, 50 },
                        nodeIndex));

                    rich.OnExecute(context, new[] { new PCGData(richPoints) });

                    if (rich.SpawnedCount != 3)
                        local.Add($"3 points at density 1.0 should all be placed, got {rich.SpawnedCount}");
                    if (rich.LastError != null)
                        local.Add($"a layer expecting 3 props complained anyway: {rich.LastError}");
                    // 主体积上的件数是**累计**判的：`Clear()` 只把 Actor 标成待销毁，同一帧里 `GetChildren`
                    // 照样数得到（实测把前面几轮的 16 件一起数了进来），所以这里按
                    // "旧的两轮 + 薄层真摆出去的 + 这一轮 3 件"判，而不是按单轮判。
                    int expectedAll = pointCount + cap + thinPlacedTotal + 3;

                    if (CountStaticModelActors(volume) != expectedAll)
                        local.Add($"after the thin and control runs the scene holds " +
                                  $"{CountStaticModelActors(volume)} actor(s), expected {expectedAll} " +
                                  $"({pointCount} + cap {cap} + thin {thinPlacedTotal} + control 3)");

                    volume.Clear();

                    // 上面两次 Clear 把先前几轮摆的也一并收走了——拆解时就不该再拿旧的件数去判。
                    expectedOwned = expectedAll;
                }
            }
            catch (Exception ex)
            {
                local.Add($"static-model spawner check threw {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (volume != null)
                {
                    try
                    {
                        // 销毁这个拥有者同时也是检查：这些 Actor 能被体积自己的结构性清理找到——
                        // 那正是重新生成时清掉上一批的机制。
                        var owned = CountStaticModelActors(volume);
                        if (expectedOwned >= 0 && owned != expectedOwned)
                            local.Add($"the volume should own {expectedOwned} StaticModel actor(s) before teardown, found {owned}");

                        Actor.Destroy(volume);
                    }
                    catch (Exception ex)
                    {
                        local.Add($"tearing down the static-model volume threw {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }

            if (local.Count > 0)
            {
                for (int i = 0; i < local.Count; i++)
                {
                    failures.Add(local[i]);
                    Debug.LogError($"[PCG] FAIL static-model spawner: {local[i]}");
                }
            }
            else
            {
                Debug.Log($"[PCG] PASS static-model spawner: {pointCount} actor(s), capped run stopped at {cap}, " +
                          "preview touched nothing, no-model run spoke up");
            }

            return local.Count;
        }

        /// <summary>
        /// 描述层的 A − B：`within` 圈一块地，`except` 再从这块地里挖掉一半。
        /// </summary>
        /// <remarks>
        /// 离屏那套断言数的是**点数**，而点数少了不等于屏幕上少了几件——洞排在坡度、间距、密度抽签**之前**时，
        /// 被它筛掉的那些点可能本来也会被后面那道抽签抽掉，于是"挖了个寂寞"。只有活着的场景能分清这两种。
        /// 两份描述各挂各的体积：件数是各自名下数出来的，不是两次累加。
        /// </remarks>
        private static int TestSpecAreaHole(List<string> failures)
        {
            var local = new List<string>();
            PCGVolume holed = null;
            PCGVolume plain = null;

            // 圈地 360×360，洞取它的左半边——挖掉的应当正好是那一半里的东西。
            const string Band = "-180 -180 180 180";
            const string Hole = "-180 -180 0 180";

            try
            {
                holed = new PCGVolume();
                plain = new PCGVolume();

                if (!PCGActors.TrySpawn(holed, null) || !PCGActors.TrySpawn(plain, null))
                {
                    local.Add("起不了两个 PCGVolume 来分别承接有洞与无洞的那两份产出");
                }
                else
                {
                    holed.Name = "PCG_SelfTest_AreaHole";
                    plain.Name = "PCG_SelfTest_AreaPlain";

                    string Spec(string clause) =>
                        "world 挖洞\nseed 7\nregion 400 400 height 10\nscatter 屋 points 60 within " + Band +
                        clause + " model \"Cube\"";

                    int holeCount = RunSpecOnVolume(holed, Spec(" except " + Hole), out string holeError);
                    int plainCount = RunSpecOnVolume(plain, Spec(string.Empty), out string plainError);

                    if (holeError != null || plainError != null)
                    {
                        local.Add($"描述在体积上跑不动：有洞「{holeError ?? "无"}」、无洞「{plainError ?? "无"}」");
                    }
                    else
                    {
                        // 三条一起才叫"洞在屏幕上真的挖掉了一块"：两份都要有货、有洞那份必须更少、
                        // 而落下来的位置**一个都不在洞里**。
                        if (plainCount <= 0)
                            local.Add($"无洞那份一件都没落地（{plainCount} 件），拿它当对照等于空转");

                        if (holeCount <= 0)
                            local.Add($"有洞那份一件都没落地（{holeCount} 件），整层被挖空了");

                        if (plainCount > 0 && holeCount >= plainCount)
                            local.Add($"挖洞一件都没少：无洞 {plainCount} 件 vs 有洞 {holeCount} 件");

                        int inside = CountInsideHole(holed);

                        if (inside > 0)
                            local.Add($"洞里还留着 {inside} 件，那道反向筛没把它们拦住");

                        if (local.Count == 0)
                        {
                            Debug.Log($"[PCG] PASS spec area hole: 无洞 {plainCount} 件 → 有洞 {holeCount} 件，" +
                                      $"洞里剩 {inside} 件");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                local.Add($"描述层挖洞检查抛了 {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                // 这两个体积是本次检查自己造的，必须原样收回；留在场景里就是给用户的场景添垃圾。
                if (plain != null)
                {
                    try { Actor.Destroy(plain); }
                    catch (Exception ex) { local.Add($"拆掉无洞那份体积时抛了 {ex.GetType().Name}: {ex.Message}"); }
                }

                if (holed != null)
                {
                    try { Actor.Destroy(holed); }
                    catch (Exception ex) { local.Add($"拆掉有洞那份体积时抛了 {ex.GetType().Name}: {ex.Message}"); }
                }
            }

            for (int i = 0; i < local.Count; i++)
            {
                failures.Add(local[i]);
                Debug.LogError($"[PCG] FAIL spec area hole: {local[i]}");
            }

            return local.Count;
        }

        /// <summary>把一段描述编译成图、在给定体积上**真跑**（不是预览），交回该体积名下落地的件数。</summary>
        /// <summary>
        /// 产生器侧的区域子句（<c>cave … within</c>）在引擎里真的框住了产出。
        /// </summary>
        /// <remarks>
        /// 离屏门已经钉过"值进槽位"与"走法内截断"，这一组要证的是另一件事：**引擎里真跑一遍，
        /// 洞道的中心线点一个都不在框外**。
        /// 数的是**点**而不是 Actor：洞穴的产出会被合并，一个 Actor 的位置只是它的锚点，
        /// 拿它当尺子量出来的是"1 件、界外 0 件"这种既不为真也不为假的数（第一版就是这样空转的）。
        /// 干跑因此不往场景里放任何东西，也不需要造体积再拆——用户的场景一个 Actor 都不该多出来。
        /// 三条一起才算数：不设区域时确实有中心线点出界（否则这一组在空转）、设了之后一个出界的都没有、
        /// 而且它仍然产出（不是把洞穴整个闷死来"满足"判据）。
        /// </remarks>
        private static int TestProducerRegion(List<string> failures)
        {
            var local = new List<string>();

            const string Band = "within -120 -120 120 120";

            try
            {
                string Spec(string clause) =>
                    "world 区域洞穴\nseed 7\nregion 400 400 height 20\ncave depth 200 segments 10 tunnels 3 " +
                    "radius 20 sides 6 " + clause;

                var boxed = SpecPointPositions(Spec(Band), out string boxedError);
                var free = SpecPointPositions(Spec(string.Empty), out string freeError);

                if (boxedError != null || freeError != null)
                {
                    local.Add($"洞穴描述在引擎里跑不动：有区域「{boxedError ?? "无"}」、无区域「{freeError ?? "无"}」");
                }
                else
                {
                    int freeOutside = CountOutsideBand(free);
                    int boxedOutside = CountOutsideBand(boxed);

                    if (free.Length == 0 || freeOutside == 0)
                    {
                        local.Add($"不设区域时中心线没有出过界（{free.Length} 个点 / 界外 {freeOutside} 个），" +
                                  "拿它当对照等于空转");
                    }

                    if (boxed.Length == 0)
                        local.Add("设了区域之后一个点都没产出（0 个）——那是闷死，不是框住");

                    if (boxedOutside > 0)
                        local.Add($"区域外还留着 {boxedOutside} 个中心线点，走法内的截断没拦住");

                    if (local.Count == 0)
                    {
                        Debug.Log($"[PCG] PASS producer region: 无区域 {free.Length} 点（界外 {freeOutside}）→ " +
                                  $"有区域 {boxed.Length} 点（界外 {boxedOutside}）");
                    }
                }
            }
            catch (Exception ex)
            {
                local.Add($"产生器区域检查抛了 {ex.GetType().Name}: {ex.Message}");
            }

            for (int i = 0; i < local.Count; i++)
            {
                failures.Add(local[i]);
                Debug.LogError($"[PCG] FAIL producer region: {local[i]}");
            }

            return local.Count;
        }

        /// <summary>
        /// 把一份描述在引擎里**干跑**一遍，取回它最终产出的点坐标。
        /// </summary>
        /// <remarks>
        /// 干跑不写场景：这一组要的是"点落在哪"，而那在采样与走法阶段就定了，不需要真的生成 Actor。
        /// 生成范围取 ±200 而区域取 ±120 —— 不给越界留出余地，对照那条就永远红不了。
        /// </remarks>
        private static Vector3[] SpecPointPositions(string text, out string error)
        {
            error = null;

            if (!PCGSpecParser.TryParse(text, out var spec, out string parseError))
            {
                error = parseError;
                return null;
            }

            var compiled = PCGSpecCompiler.Compile(spec);

            if (!compiled.Ok)
            {
                error = compiled.Error;
                return null;
            }

            if (!PCGGraphCompiler.TryCompile(compiled.Graph, out var program))
            {
                error = "编译不出执行计划";
                return null;
            }

            var output = new PCGGraphExecutor(program).Execute(
                new PCGExecutionContext
                {
                    Seed = 7,
                    World = new PCGWorldContext(),
                    GenerationBounds = new BoundingBox(new Vector3(-200, -200, -200), new Vector3(200, 200, 200)),
                }.AsDryRun()).PrimaryOutput;

            if (output == null)
            {
                error = "没有产出层";
                return null;
            }

            var positions = new Vector3[output.Count];

            for (int i = 0; i < output.Count; i++)
                positions[i] = output.Points[i].Position;

            return positions;
        }

        /// <summary>数有多少个点落在 ±120 那条带以外（区域判据的反向读数）。</summary>
        private static int CountOutsideBand(Vector3[] positions)
        {
            if (positions == null)
                return 0;

            int outside = 0;

            for (int i = 0; i < positions.Length; i++)
            {
                if (positions[i].X < -120.0f || positions[i].X > 120.0f ||
                    positions[i].Z < -120.0f || positions[i].Z > 120.0f)
                    outside++;
            }

            return outside;
        }

        /// <summary>
        /// 水系那一侧的区域约束在引擎里跑一遍：湖与河的产出点不许落在框外，而不设区域时确实会有落在框外的。
        /// </summary>
        /// <remarks>
        /// 与洞穴那一组同一形状，但**产出条件更苛刻**：水文要从高度场里汇水，高度场不合适时一条水都不产，
        /// 那不是缺陷而是夹具。所以这里的"没产出"走 Skip（在结果里可见），只有"产出了却越界"才判失败——
        /// 反过来把"零产出"判成通过，就是本仓反复点名的"绿是因为什么都没跑"。
        /// </remarks>
        private static int TestProducerRegionWater(List<string> failures)
        {
            var local = new List<string>();

            const string Band = "within -120 -120 120 120";

            // 两处等深洼地：一处落在框内（原点），一处落在框外（(160,160)）。
            // 上一版只有一处在原点的碗，于是"不设区域时也没有点落在框外"——对照不成立，判据整轮空转（如实报 SKIP）。
            // 洼地数取小（`min-lake-cells 4`）是因为一格碗被分成两半后每处的积水格数会变少。
            float? Basin(float x, float z)
            {
                float inner = x * x + z * z;
                float outer = (x - 160.0f) * (x - 160.0f) + (z - 160.0f) * (z - 160.0f);

                return Math.Min(inner, outer) * 0.0004f;
            }

            try
            {
                string Spec(string clause) =>
                    "world 区域水系\nseed 7\nregion 400 400 height 20\nwater cell 40 threshold 6 min-lake-cells 4 " + clause;

                var boxed = SpecPointPositions(Spec(Band), Basin);
                var free = SpecPointPositions(Spec(string.Empty), Basin);

                if (boxed.Error != null || free.Error != null)
                {
                    local.Add($"水系描述在引擎里跑不动：有区域「{boxed.Error ?? "无"}」、无区域「{free.Error ?? "无"}」");
                }
                else if (free.Positions.Length == 0)
                {
                    Skip("producer region water: 这份碗形高度场没汇出任何水（夹具条件，不是缺陷），" +
                         "越界判据这一轮没跑到");
                }
                else
                {
                    int freeOutside = CountOutsideBand(free.Positions);
                    int boxedOutside = CountOutsideBand(boxed.Positions);

                    if (freeOutside == 0)
                    {
                        // 对照不成立时不能继续宣称"框住了"：那只是水本来就都在框里。
                        Skip($"producer region water: 不设区域时 {free.Positions.Length} 个产出点没有一个落在 ±120 之外，" +
                             "越界判据在这份高度场上是空转（夹具需要两处洼地，一里一外）");
                    }
                    else if (boxed.Positions.Length == 0)
                    {
                        // 全部水源都在框外时，"什么都不产"是**产生器输入的正确结果**，不是闷死——
                        // 约束说的是"只准在这块里发源"，一块都不在里头，产出就该是空。
                        // 判据要真跑到"框住了"，得让夹具里既有一处的水在框内、也有一处在框外。
                        Skip($"producer region water: 无区域时界外有 {freeOutside} 个点，但设了区域后一个都不产" +
                             "（全部水源都在框外，这是正确结果）；补一里一外两处洼地才能跑到越界判据");
                    }
                    else
                    {
                        if (boxedOutside > 0)
                            local.Add($"区域外还留着 {boxedOutside} 个水产出点，发源判据没拦住");

                        // 反转那一档：把框内变成禁区 ⇒ 只剩框外那处洼地发源。
                        // 这一条才是"outside 不是装饰"的证据：它要求产出**换了位置**，而不是单纯变少。
                        var excluded = SpecPointPositions(Spec("outside -120 -120 120 120"), Basin);

                        if (excluded.Error != null)
                        {
                            local.Add($"outside 那份水系跑不动：{excluded.Error}");
                        }
                        else
                        {
                            int excludedInside = excluded.Positions.Length - CountOutsideBand(excluded.Positions);

                            if (excluded.Positions.Length == 0)
                            {
                                Skip("producer region water: outside 之后一个水产出点都不剩（两处洼地都被判掉？" +
                                     "夹具条件，不是缺陷），反转判据这一轮没跑到");
                            }
                            else
                            {
                                // 判据是**发源**，不是"产出点必须落在框外"：框外发起的河可以流进框内，那正是
                                // `outside` 允许的。之前我把这条写成"框内必须为 0"，于是把正确行为当成缺陷，
                                // 引擎与离屏两边都被我误判成 bug（#17 的真相）。
                                // 正确的形状：outside 的产出是无约束产出的**真子集**——框内发源的那处水必须消失，
                                // 但整片判光（= 没生效或写反）同样不合格。
                                if (excluded.Positions.Length == 0 ||
                                    excluded.Positions.Length >= free.Positions.Length)
                                {
                                    local.Add($"outside 没有只丢掉框内发源的那部分水：无约束 {free.Positions.Length} 点" +
                                              $" → outside {excluded.Positions.Length} 点" +
                                              "（应是无约束的真子集）");
                                }

                                if (boxed.Positions.Length == 0 || boxed.Positions.Length >= free.Positions.Length)
                                {
                                    local.Add($"within 没有只留下框内发源的水：无约束 {free.Positions.Length} 点" +
                                              $" → within {boxed.Positions.Length} 点");
                                }
                            }
                        }

                        if (local.Count == 0)
                        {
                            Debug.Log($"[PCG] PASS producer region water: 无区域 {free.Positions.Length} 点" +
                                      $"（界外 {freeOutside}）→ 有区域 {boxed.Positions.Length} 点（界外 {boxedOutside}）" +
                                      $"→ outside {excluded.Positions.Length} 点（界内 " +
                                      $"{excluded.Positions.Length - CountOutsideBand(excluded.Positions)}）");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                local.Add($"水系区域检查抛了 {ex.GetType().Name}: {ex.Message}");
            }

            for (int i = 0; i < local.Count; i++)
            {
                failures.Add(local[i]);
                Debug.LogError($"[PCG] FAIL producer region water: {local[i]}");
            }

            return local.Count;
        }

        /// <summary>带高度场的干跑版本：水文要从高度里汇水，没有高度源就什么都产不出。</summary>
        private static (Vector3[] Positions, string Error) SpecPointPositions(string text, Func<float, float, float?> height)
        {
            if (!PCGSpecParser.TryParse(text, out var spec, out string parseError))
                return (null, parseError);

            var compiled = PCGSpecCompiler.Compile(spec);

            if (!compiled.Ok)
                return (null, compiled.Error);

            if (!PCGGraphCompiler.TryCompile(compiled.Graph, out var program))
                return (null, "编译不出执行计划");

            var output = new PCGGraphExecutor(program).Execute(
                new PCGExecutionContext
                {
                    Seed = 7,
                    World = new PCGWorldContext { HeightSource = height },
                    GenerationBounds = new BoundingBox(new Vector3(-200, -200, -200), new Vector3(200, 200, 200)),
                }.AsDryRun()).PrimaryOutput;

            if (output == null)
                return (null, "没有产出层");

            var positions = new Vector3[output.Count];

            for (int i = 0; i < output.Count; i++)
                positions[i] = output.Points[i].Position;

            return (positions, null);
        }

        private static int RunSpecOnVolume(PCGVolume volume, string text, out string error)
        {
            error = null;

            if (!PCGSpecParser.TryParse(text, out var spec, out string parseError))
            {
                error = parseError;
                return -1;
            }

            // 模型 resolver 传空：`Cube` 走的是内建图元那张表，编辑器与命令行两侧同一条路。
            var compiled = PCGSpecCompiler.Compile(spec);

            if (!compiled.Ok)
            {
                error = compiled.Error;
                return -1;
            }

            if (!PCGGraphCompiler.TryCompile(compiled.Graph, out var program))
            {
                error = "编译不出执行计划";
                return -1;
            }

            var context = new PCGExecutionContext
            {
                Seed = 7,
                World = new PCGWorldContext(),
                GenerationBounds = new BoundingBox(new Vector3(-200, -20, -200), new Vector3(200, 20, 200)),
                Source = volume,
            };

            new PCGGraphExecutor(program).Execute(context);

            var children = volume.GetChildren<StaticModel>();
            return children?.Length ?? 0;
        }

        /// <summary>数一体积名下列地里**落在洞里**的那几件（洞即 `except` 那半块矩形）。</summary>
        private static int CountInsideHole(PCGVolume volume)
        {
            var children = volume?.GetChildren<StaticModel>();
            int inside = 0;

            if (children == null)
                return 0;

            for (int i = 0; i < children.Length; i++)
            {
                var position = children[i].Position;

                if (position.X >= -180.0f && position.X <= 0.0f &&
                    position.Z >= -180.0f && position.Z <= 180.0f)
                    inside++;
            }

            return inside;
        }

        /// <summary>数一体积名下已经落地的 <see cref="StaticModel"/>。</summary>
        private static int CountStaticModelActors(Actor parent)
        {
            var children = parent?.GetChildren<StaticModel>();
            return children?.Length ?? 0;
        }

        private static int TestSpawnFoliage(List<string> failures, string modelPath)
        {
            const int nodeIndex = 11;
            const int pointCount = 12;

            var local = new List<string>();
            PCGVolume volume = null;

            // 放置那一半跑过之后期望体积拥有几个植被宿主；-1 表示那一半根本没跑（模型没载进来），
            // 于是拆解时不该再拿"应该有 2 个"去判它——那是把一个**跳过**报成了失败。
            int expectedHosts = -1;

            try
            {
                volume = new PCGVolume();
                if (!PCGActors.TrySpawn(volume, null))
                {
                    local.Add("could not spawn a PCGVolume to own the foliage test actor");
                }
                else
                {
                    volume.Name = "PCG_SelfTest_Foliage";

                    var points = new PCGPoint[pointCount];
                    for (int i = 0; i < pointCount; i++)
                    {
                        points[i] = PCGPoint.Create(new Vector3(-55.0f + i * 10.0f, 0.0f, 0.0f), 1.0f, i);
                        points[i].UpdateBounds(new Vector3(5.0f, 5.0f, 5.0f));
                    }

                    var data = new PCGData(points);
                    var region = new BoundingBox(new Vector3(-60, -10, -10), new Vector3(60, 10, 10));
                    var context = new PCGExecutionContext
                    {
                        Seed = 5,
                        CurrentNodeId = nodeIndex,
                        GenerationBounds = region,
                        Source = volume,
                    };

                    // 第一半：没有东西可放的生成器必须说出来，并且不碰场景。
                    var unset = new PCG.Nodes.PCGSpawnFoliageNode(new PCGNodeRegistry.PCGNodeSerializationData(
                        PCG.Nodes.PCGSpawnFoliageNode.Id,
                        new object[] { Guid.Empty, 0.0f, 1.0f, false, PCG.Nodes.PCGSpawnFoliageNode.DefaultMaxInstances, 0.0f },
                        nodeIndex));

                    var passthrough = unset.OnExecute(context, new[] { data });
                    if (unset.LastError == null)
                        local.Add("a foliage spawner with no model reported no problem");
                    if (passthrough == null || passthrough.Count != pointCount)
                        local.Add($"a failed foliage run should pass the points through, got {passthrough?.Count ?? -1}");
                    if (CountFoliageActors(volume) != 0)
                        local.Add("a foliage spawner with no model still created a Foliage actor");

                    var model = LoadFixtureModel(modelPath);

                    if (model == null)
                    {
                        Skip($"foliage placement: {modelPath} did not load as a Model.");
                    }
                    else
                    {
                        object[] ValuesOf(Guid id) => new object[]
                        {
                            id, 0.0f, 1.0f, false, PCG.Nodes.PCGSpawnFoliageNode.DefaultMaxInstances, 0.0f,
                        };

                        var first = new PCG.Nodes.PCGSpawnFoliageNode(new PCGNodeRegistry.PCGNodeSerializationData(
                            PCG.Nodes.PCGSpawnFoliageNode.Id, ValuesOf(model.ID), nodeIndex));

                        var placedFirst = first.OnExecute(context, new[] { data });
                        if (first.LastError != null)
                            local.Add($"the first foliage run reported: {first.LastError}");
                        if (first.PlacedCount != pointCount)
                            local.Add($"expected {pointCount} instances placed, got {first.PlacedCount}");
                        if (placedFirst == null || placedFirst.Count != pointCount)
                            local.Add($"the foliage spawner should pass on every point it placed, got {placedFirst?.Count ?? -1}");
                        if (CountFoliageActors(volume) != 1)
                            local.Add($"expected exactly 1 Foliage actor, found {CountFoliageActors(volume)}");

                        var actor = FindFoliageActor(volume);
                        if (actor != null && actor.InstancesCount != pointCount)
                            local.Add($"the Foliage actor holds {actor.InstancesCount} instance(s), expected {pointCount}");

                        // 一次重编会把崭新的节点对象交给图；复用必须以**图里的位置**为键，
                        // 而不是 C# 实例，否则每次重新生成都会多出一个 Actor、各自抱着
                        // 一整份散布的副本。
                        var second = new PCG.Nodes.PCGSpawnFoliageNode(new PCGNodeRegistry.PCGNodeSerializationData(
                            PCG.Nodes.PCGSpawnFoliageNode.Id, ValuesOf(model.ID), nodeIndex));
                        second.OnExecute(context, new[] { data });

                        if (CountFoliageActors(volume) != 1)
                            local.Add($"a recompiled spawner created another Foliage actor ({CountFoliageActors(volume)} in total)");
                        if (actor != null && actor.InstancesCount != pointCount)
                            local.Add($"re-running stacked instances: {actor.InstancesCount} instead of {pointCount}");

                        // 预览不能建任何东西。同一张图、同一批点、同一个父节点——只有运行的
                        // 标记不同——而上面摆下的那十二个实例正是"正面孪生"，它挡住
                        // 这条断言退化成"什么都没发生，因为从来什么都不发生"。
                        var dry = new PCG.Nodes.PCGSpawnFoliageNode(new PCGNodeRegistry.PCGNodeSerializationData(
                            PCG.Nodes.PCGSpawnFoliageNode.Id, ValuesOf(model.ID), nodeIndex));
                        var dryPoints = dry.OnExecute(context.AsDryRun(), new[] { data });

                        if (dry.PlacedCount != 0)
                            local.Add($"a preview run reported placing {dry.PlacedCount} instance(s)");
                        if (dryPoints == null || dryPoints.Count != pointCount)
                            local.Add($"a preview run should still pass its points on, got {dryPoints?.Count ?? -1}");
                        if (CountFoliageActors(volume) != 1 || actor == null || actor.InstancesCount != pointCount)
                            local.Add("a preview run changed the scene: the DryRun flag is not honoured");

                        // 分区运行采样的是一个带 padding 的区域；属于
                        // 邻格的那些实例不能在这里也被摆一份。
                        var leftCell = new PCGPartitionCell(
                            region,
                            new BoundingBox(new Vector3(-60, -10, -10), new Vector3(0, 10, 10)),
                            0, 0, false, true);

                        context.ClipCell = leftCell;
                        var clipped = new PCG.Nodes.PCGSpawnFoliageNode(new PCGNodeRegistry.PCGNodeSerializationData(
                            PCG.Nodes.PCGSpawnFoliageNode.Id, ValuesOf(model.ID), nodeIndex));
                        var clippedCount = clipped.OnExecute(context, new[] { data })?.Count ?? -1;
                        context.ClipCell = null;

                        if (clippedCount != pointCount / 2)
                            local.Add($"a cell covering half the region placed {clippedCount} point(s), expected {pointCount / 2}");
                        // 每一格拥有自己的宿主，所以裁剪过的那次写进第二个 Actor，而第一格
                        // 的散布原样不动。共用一个宿主正是这里要抓的缺陷：
                        // 最后一格的 RemoveAllInstances 会静默删掉此前每一格的实例。
                        int hosts = CountFoliageActors(volume);
                        if (hosts != 2)
                            local.Add($"two partition cells should own 2 Foliage hosts, found {hosts}");
                        else
                            expectedHosts = 2;
                        if (actor != null && actor.InstancesCount != pointCount)
                            local.Add($"the first cell's scatter was disturbed by the clipped run: {actor.InstancesCount} instance(s), expected {pointCount}");

                        var secondHost = FindSecondFoliageActor(volume);
                        if (secondHost == null)
                            local.Add("the clipped run did not create its own Foliage host");
                        else if (secondHost.InstancesCount != pointCount / 2)
                            local.Add($"the clipped cell's host holds {secondHost.InstancesCount} instance(s), expected {pointCount / 2}");

                        // 最后一档：**一层的点全被吃掉时不能沉默**（实测某张图 909 点、0 件、0 条警告）。
                        // 这里用一张一个点都不覆盖的裁剪格，让 12 个点全部落在生成区域之外——
                        // 摆放数于是**确定**是 0，不靠密度抽签，断言也就不可能偶发。
                        int hostsBefore = CountFoliageActors(volume);

                        context.ClipCell = new PCGPartitionCell(
                            region,
                            new BoundingBox(new Vector3(300.0f, -10.0f, -10.0f), new Vector3(400.0f, 10.0f, 10.0f)),
                            7, 7, false, true);

                        var starvedPoints = new PCGPoint[pointCount];
                        for (int i = 0; i < pointCount; i++)
                        {
                            starvedPoints[i] = PCGPoint.Create(new Vector3(-55.0f + i * 10.0f, 0.0f, 0.0f), 1.0f, i);
                            starvedPoints[i].UpdateBounds(new Vector3(5.0f, 5.0f, 5.0f));
                        }

                        var starved = new PCG.Nodes.PCGSpawnFoliageNode(new PCGNodeRegistry.PCGNodeSerializationData(
                            PCG.Nodes.PCGSpawnFoliageNode.Id, ValuesOf(model.ID), nodeIndex));
                        starved.OnExecute(context, new[] { new PCGData(starvedPoints) });
                        context.ClipCell = null;

                        if (starved.PlacedCount != 0)
                            local.Add($"a cell covering none of the points placed {starved.PlacedCount} instance(s), expected 0");
                        if (starved.PlacedCount == 0 && starved.LastError == null)
                            local.Add("a foliage layer that placed nothing from 12 points reported no reason at all");
                        if (starved.LastError != null && starved.LastError.IndexOf("outside", System.StringComparison.Ordinal) < 0)
                            local.Add($"the empty-run reason should name which filter ate the points: {starved.LastError}");

                        // 不是空转的反证在上面：同一批点满摆 12 件那次断言过 `LastError == null`，
                        // 所以"0 件必须点名"只可能由这次空摆触发。
                        int hostsAfter = CountFoliageActors(volume);
                        if (hostsAfter != hostsBefore && hostsAfter != hostsBefore + 1)
                            local.Add($"the starved run should add at most one host for its own cell, went {hostsBefore} to {hostsAfter}");
                        if (hostsAfter > hostsBefore)
                            expectedHosts = hostsAfter;

                        // 同一台夹具测**静态模型生成器**的那道守卫——它此前在引擎里一次都没被证过触发。
                        // 复用上面那台体积、那个 context、那份模型：正因为这套夹具刚刚满摆过 12 件，
                        // 这次的 0 件才不可能是"根本没接上"造成的空转（那正是这类判据最容易假绿的地方）。
                        var propPoints = new PCGPoint[pointCount];
                        for (int i = 0; i < pointCount; i++)
                        {
                            propPoints[i] = PCGPoint.Create(new Vector3(-55.0f + i * 10.0f, 0.0f, 0.0f), 1.0f, i);
                            propPoints[i].UpdateBounds(new Vector3(5.0f, 5.0f, 5.0f));
                        }

                        context.ClipCell = new PCGPartitionCell(
                            region,
                            new BoundingBox(new Vector3(300.0f, -10.0f, -10.0f), new Vector3(400.0f, 10.0f, 10.0f)),
                            9, 9, false, true);

                        var prop = new PCG.Nodes.PCGSpawnStaticModelNode(new PCGNodeRegistry.PCGNodeSerializationData(
                            PCG.Nodes.PCGSpawnStaticModelNode.Id,
                            new object[]
                            {
                                model.ID, 0.0f, 1.0f, false, PCG.Nodes.PCGSpawnStaticModelNode.DefaultMaxActors,
                            }, nodeIndex));

                        var propOut = prop.OnExecute(context, new[] { new PCGData(propPoints) });
                        context.ClipCell = null;

                        if (prop.SpawnedCount != 0)
                            local.Add($"a cell covering none of the points spawned {prop.SpawnedCount} actor(s), expected 0");
                        if (prop.SpawnedCount == 0 && prop.LastError == null)
                            local.Add("a static-model layer that spawned nothing from 12 points reported no reason at all");
                        if (prop.LastError != null && prop.LastError.IndexOf("outside", System.StringComparison.Ordinal) < 0)
                            local.Add($"the empty static-model run should name which filter ate the points: {prop.LastError}");
                        if ((volume.GetChildren<StaticModel>()?.Length ?? 0) != 0)
                            local.Add("the starved static-model run left actors behind anyway");
                        if (propOut == null || propOut.Count != 0)
                            local.Add($"a layer that spawned nothing should hand on an empty set, got {propOut?.Count ?? -1}");

                        // 植被那条"薄层也要说话"的判据必须自己跑一遍：它与另两条生成器共用同一句判据，
                        // 但一个从没被执行过的分支在自检里同样是永远绿的。自己的体积 + 自己的图位置，
                        // 免得把上面那些"一个宿主、复用不叠加"的读数搅了。
                        var foliageProbe = new PCGVolume();

                        if (!PCGActors.TrySpawn(foliageProbe, null))
                            local.Add("植被薄层检查起不了自己的体积，那一半根本没跑");
                        else
                        {
                            foliageProbe.Name = "PCG_SelfTest_FoliageThin";

                            object[] ThinOf(Guid id, float threshold) => new object[]
                            {
                                id, threshold, 1.0f, false, PCG.Nodes.PCGSpawnFoliageNode.DefaultMaxInstances, 0.0f,
                            };

                            var thinFoliagePoints = new PCGPoint[3];
                            for (int i = 0; i < thinFoliagePoints.Length; i++)
                            {
                                thinFoliagePoints[i] = PCGPoint.Create(new Vector3(-5.0f + i * 5.0f, 0.0f, 0.0f), 0.3f, i);
                                thinFoliagePoints[i].UpdateBounds(new Vector3(5.0f, 5.0f, 5.0f));
                            }

                            int thinFoliagePlacedRuns = 0;
                            int foliageProbeNodes = nodeIndex + 700;

                            // 12 个种子而不是 3 个：这一层每次期望 0.9 丛，"一件都没摆"的概率本来就有约四成，
                            // 三个种子全空的概率 (~5%) 实测真的发生了——那不等于判据坏了，而是分支没跑到。
                            // 每个种子都是同一层的合法一次重跑，多跑几轮把它压到可以忽略。
                            for (int seed = 7; seed <= 18; seed++)
                            {
                                var thinContext = new PCGExecutionContext
                                {
                                    Seed = seed,
                                    CurrentNodeId = foliageProbeNodes,
                                    GenerationBounds = region,
                                    Source = foliageProbe,
                                };

                                var thin = new PCG.Nodes.PCGSpawnFoliageNode(
                                    new PCGNodeRegistry.PCGNodeSerializationData(
                                        PCG.Nodes.PCGSpawnFoliageNode.Id, ThinOf(model.ID, 0.5f), foliageProbeNodes));

                                thin.OnExecute(thinContext, new[] { new PCGData(thinFoliagePoints) });

                                if (thin.LastError == null)
                                    local.Add($"seed {seed}: 三个点各 0.3 密度（整层期望 0.9 丛）摆了 " +
                                              $"{thin.PlacedCount} 丛却什么都没说");

                                if (thin.PlacedCount > 0)
                                {
                                    thinFoliagePlacedRuns++;

                                    if (thin.LastError != null &&
                                        thin.LastError.IndexOf("expected only", System.StringComparison.Ordinal) < 0)
                                    {
                                        local.Add($"seed {seed}: 摆出了 {thin.PlacedCount} 丛但没报出期望丛数：{thin.LastError}");
                                    }
                                }
                            }

                            if (thinFoliagePlacedRuns == 0)
                                local.Add("12 seeds × 3 points at density 0.3 never placed an instance, so the foliage " +
                                          "\"placed but under-one-expected\" branch went unexercised — raise the " +
                                          "fixture's density a little (keep the sum under 1)");

                            // 正向同夹具对照：密度 1.0 恒过抽签（1.0 >= 任何 [0,1) 的抽签值）⇒ 3 丛、且不许说话。
                            var richFoliagePoints = new PCGPoint[3];
                            for (int i = 0; i < richFoliagePoints.Length; i++)
                            {
                                richFoliagePoints[i] = PCGPoint.Create(
                                    new Vector3(-5.0f + i * 5.0f, 0.0f, 0.0f), 1.0f, i);
                                richFoliagePoints[i].UpdateBounds(new Vector3(5.0f, 5.0f, 5.0f));
                            }

                            int richProbeNodes = foliageProbeNodes + 1;

                            var richContext = new PCGExecutionContext
                            {
                                Seed = 7,
                                CurrentNodeId = richProbeNodes,
                                GenerationBounds = region,
                                Source = foliageProbe,
                            };

                            var richFoliage = new PCG.Nodes.PCGSpawnFoliageNode(
                                new PCGNodeRegistry.PCGNodeSerializationData(
                                    PCG.Nodes.PCGSpawnFoliageNode.Id, ThinOf(model.ID, 0.5f), richProbeNodes));

                            richFoliage.OnExecute(richContext, new[] { new PCGData(richFoliagePoints) });

                            if (richFoliage.PlacedCount != 3)
                                local.Add($"三个密度 1.0 的点该摆 3 丛，实得 {richFoliage.PlacedCount}");
                            if (richFoliage.LastError != null)
                                local.Add($"期望 3 丛的那一层不该说话：{richFoliage.LastError}");

                            // 三个种子同一个图位置 ⇒ 一个宿主反复复用；对照组另一个位置 ⇒ 第二个宿主。
                            // 一共两个，多出来的就是"每次重新生成多出一个 Actor"那个老毛病。
                            if (CountFoliageActors(foliageProbe) != 2)
                                local.Add($"两份探针该共用出 2 个植被宿主，实得 {CountFoliageActors(foliageProbe)}");

                            Actor.Destroy(foliageProbe);
                        }

                        if (local.Count == 0)
                            Debug.Log($"[PCG] PASS foliage spawner: {pointCount} instances on one actor, reused across runs, clipped per cell");
                    }
                }
            }
            catch (Exception ex)
            {
                local.Add($"foliage spawner check threw {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (volume != null)
                {
                    try
                    {
                        // 销毁这个拥有者同时也是在检查：植被 Actor 能被
                        // 体积自己的结构性清理找到——而那正是重新生成时清掉一片散布的机制。
                        var owned = CountFoliageActors(volume);
                        if (expectedHosts >= 0 && owned != expectedHosts)
                            local.Add($"the volume should own {expectedHosts} Foliage hosts before teardown, found {owned}");

                        Actor.Destroy(volume);
                    }
                    catch (Exception)
                    {
                        // 拆掉测试 Actor 不该决定这次运行的结果。
                    }
                }
            }

            foreach (var message in local)
            {
                failures.Add(message);
                Debug.LogError($"[PCG] FAIL foliage spawner: {message}");
            }

            return local.Count;
        }

        private static Foliage FindSecondFoliageActor(Actor parent)
        {
            var children = parent?.GetChildren<Foliage>();
            if (children == null)
                return null;

            Foliage second = null;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] == null)
                    continue;

                if (second != null)
                    return children[i];

                second = children[i];
            }

            return null;
        }

        /// <summary>
        /// 层级生成：粗层只判定不建东西，细层只在
        /// "粗层发现过东西"的那些格子下面跑。
        /// </summary>
        /// <remarks>
        /// 这一条声称的是一个**比较**而不是一个数字：同一张图必须产出与扁平
        /// 分区运行相同的点，同时做**严格更少**的格子运行——按这次运行自己的检视器计数，它
        /// 每格每节点记一条。夹具里的图只留下离某一个角很远的点，所以
        /// 粗网格里真有四分之一是空的，省下来的量是真的、不是指望出来的。
        /// </remarks>
        private static int TestHierarchicalGeneration(List<string> failures)
        {
            var local = new List<string>();
            string graphPath = System.IO.Path.Combine(Globals.TemporaryFolder, "pcg_selftest_hierarchical.json");
            PCGVolume volume = null;

            try
            {
                if (!PCGGraphFile.TrySave(graphPath, CornerFilterGraph("SelfTestHierarchical", 2000),
                        out string saveError))
                {
                    local.Add($"could not save the hierarchical test graph: {saveError}");
                }
                else
                {
                    volume = new PCGVolume();

                    if (!PCGActors.TrySpawn(volume, null))
                    {
                        local.Add("could not spawn a PCGVolume for the hierarchical test");
                    }
                    else
                    {
                        volume.Name = "PCG_SelfTest_Hierarchical";
                        volume.Mode = PCGGenerationMode.Preview;
                        volume.GraphPath = graphPath;
                        volume.Seed = 515;
                        volume.Volume = new BoundingBox(
                            new Vector3(-800.0f, -50.0f, -800.0f), new Vector3(800.0f, 50.0f, 800.0f));
                        volume.Partitioned = true;
                        volume.PartitionCellSize = 200.0f;

                        // 先跑扁平那次，用检视器量：它是两个计数的基线。
                        volume.NodeInspection = new PCGNodeInspection();

                        int flatPoints = volume.Generate() && volume.LastResult != null
                            ? volume.LastResult.PointCount
                            : 0;
                        int flatRuns = volume.NodeInspection.Records.Count;

                        // 再把同一张图跑成两层。
                        volume.Hierarchical = true;
                        volume.HierarchicalCoarseSize = 800.0f;
                        volume.NodeInspection = new PCGNodeInspection();

                        int hierarchicalPoints = volume.Generate() && volume.LastResult != null
                            ? volume.LastResult.PointCount
                            : 0;
                        int hierarchicalRuns = volume.NodeInspection.Records.Count;

                        if (flatPoints <= 0)
                        {
                            local.Add("the flat run produced nothing, so this comparison would be vacuous");
                        }
                        else if (hierarchicalPoints != flatPoints)
                        {
                            local.Add($"hierarchical generation changed the content: " +
                                      $"{hierarchicalPoints} vs {flatPoints} point(s)");
                        }
                        else if (hierarchicalRuns >= flatRuns)
                        {
                            local.Add($"hierarchical generation should make fewer cell runs: " +
                                      $"{hierarchicalRuns} vs {flatRuns}");
                        }
                        else
                        {
                            Debug.Log($"[PCG] PASS hierarchical generation: {hierarchicalPoints} point(s) from " +
                                      $"{hierarchicalRuns} node run(s) where the flat grid needed {flatRuns}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                local.Add($"hierarchical check threw {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (volume != null)
                {
                    try
                    {
                        Actor.Destroy(volume);
                    }
                    catch (Exception)
                    {
                        // 拆解不该决定结果。
                    }
                }

                try
                {
                    if (System.IO.File.Exists(graphPath))
                        System.IO.File.Delete(graphPath);
                }
                catch (Exception)
                {
                    // 一个残留的临时图同理。
                }
            }

            foreach (var message in local)
            {
                failures.Add(message);
                Debug.LogError($"[PCG] FAIL hierarchical generation: {message}");
            }

            return local.Count;
        }

        /// <summary>
        /// 场景里的样条网格生成器：体积之下的一排分块、裁到自己的格子，而预览运行里
        /// 一个都没有。
        /// </summary>
        private static int TestSpawnSplineMesh(List<string> failures, string modelPath)
        {
            const int nodeIndex = 12;

            var local = new List<string>();
            PCGVolume volume = null;

            try
            {
                volume = new PCGVolume();

                if (!PCGActors.TrySpawn(volume, null))
                {
                    local.Add("could not spawn a PCGVolume to own the spline mesh test");
                }
                else
                {
                    volume.Name = "PCG_SelfTest_SplineMesh";

                    var model = LoadFixtureModel(modelPath);

                    if (model == null)
                    {
                        Skip($"spline mesh placement: {modelPath} did not load as a Model.");
                    }
                    else
                    {
                        object[] ValuesOf() => new object[]
                        {
                            model.ID.ToString(), 5.0f, 0.5f, PCG.Nodes.PCGSpawnSplineMeshNode.DefaultMaxActors,
                        };

                        // 一条 20 单位长的线切成 5 单位的分块：四块，这也正是离屏
                        // 布局测试说的数——场景这里检查的是父子关系与命名。
                        var data = new PCGData
                        {
                            Splines = new[]
                            {
                                new PCGSpline(new[]
                                {
                                    new Vector3(-10.0f, 0.0f, 0.0f), new Vector3(10.0f, 0.0f, 0.0f),
                                }, false),
                            },
                        };

                        var context = new PCGExecutionContext
                        {
                            Seed = 6,
                            CurrentNodeId = nodeIndex,
                            GenerationBounds = new BoundingBox(
                                new Vector3(-60, -10, -10), new Vector3(60, 10, 10)),
                            Source = volume,
                        };

                        var spawner = new PCG.Nodes.PCGSpawnSplineMeshNode(
                            new PCGNodeRegistry.PCGNodeSerializationData(
                                PCG.Nodes.PCGSpawnSplineMeshNode.Id, ValuesOf(), nodeIndex));

                        spawner.OnExecute(context, new[] { data });

                        var pieces = volume.GetChildren<StaticModel>();
                        int placed = pieces?.Length ?? 0;

                        if (spawner.LastError != null)
                            local.Add($"the spline mesh run reported: {spawner.LastError}");
                        if (placed != 4)
                            local.Add($"expected 4 piece(s) under the volume, found {placed}");

                        bool named = true;
                        for (int i = 0; pieces != null && i < pieces.Length; i++)
                        {
                            if (!PCG.Execution.PCGSpawnNaming.IsGeneratedName(pieces[i]?.Name))
                                named = false;
                        }

                        if (!named)
                            local.Add("a spawned piece is not named with the plugin's generated prefix");

                        // 线的一半落在格子里：只有站在格子里的那些分块可以被建出来，否则一次
                        // 分区运行会把邻居的那一排也建出来。
                        var leftCell = new PCGPartitionCell(
                            context.GenerationBounds,
                            new BoundingBox(new Vector3(-60, -10, -10), new Vector3(0, 10, 10)),
                            0, 0, false, true);

                        context.ClipCell = leftCell;
                        var clipped = new PCG.Nodes.PCGSpawnSplineMeshNode(
                            new PCGNodeRegistry.PCGNodeSerializationData(
                                PCG.Nodes.PCGSpawnSplineMeshNode.Id, ValuesOf(), nodeIndex));

                        clipped.OnExecute(context, new[] { data });

                        if (clipped.PlacedCount != 2)
                            local.Add($"a cell covering half the line should place 2 piece(s), placed " +
                                      $"{clipped.PlacedCount}");

                        // 预览不建任何东西，与植被那一组检查的是同一条保证。
                        int before = volume.GetChildren<StaticModel>()?.Length ?? 0;
                        var dry = new PCG.Nodes.PCGSpawnSplineMeshNode(
                            new PCGNodeRegistry.PCGNodeSerializationData(
                                PCG.Nodes.PCGSpawnSplineMeshNode.Id, ValuesOf(), nodeIndex));

                        dry.OnExecute(context.AsDryRun(), new[] { data });

                        if ((volume.GetChildren<StaticModel>()?.Length ?? 0) != before)
                            local.Add("a preview run built spline pieces: the DryRun flag is not honoured");

                        if (local.Count == 0)
                        {
                            Debug.Log($"[PCG] PASS spline mesh spawner: {placed} piece(s) owned by the volume, " +
                                      $"{clipped.PlacedCount} in half the cell, none in a preview run");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                local.Add($"spline mesh check threw {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (volume != null)
                {
                    try
                    {
                        Actor.Destroy(volume);
                    }
                    catch (Exception)
                    {
                        // 拆解不该决定结果。
                    }
                }
            }

            foreach (var message in local)
            {
                failures.Add(message);
                Debug.LogError($"[PCG] FAIL spline mesh spawner: {message}");
            }

            return local.Count;
        }

        /// <summary>
        /// 一个采样器，后面接一个只保留"离区域某个角很远"的点的过滤器。
        /// </summary>
        /// <remarks>
        /// 过滤器正是给粗层"可决定"的东西：没有一张**在某些地方什么都不产出**的图，
        /// 每个粗格都是活的，两层就分不出来。
        /// </remarks>
        private static Graph.PCGGraph CornerFilterGraph(string name, int pointCount)
        {
            return new Graph.PCGGraph
            {
                Name = name,
                Nodes = new[]
                {
                    new Graph.PCGNode
                    {
                        TypeId = PCG.Nodes.PCGVolumeSamplerNode.Id,
                        Values = new object[] { pointCount, 0, 1.0f, false, 1.0f },
                    },
                    new Graph.PCGNode
                    {
                        TypeId = PCG.Nodes.PCGDistanceFilterNode.Id,
                        Values = new object[]
                        {
                            new Vector3(-800.0f, 0.0f, -800.0f), 1200.0f, float.PositiveInfinity, true, false,
                        },
                    },
                },
                Edges = new[] { new Graph.PCGEdge { OutputNodeIndex = 0, InputNodeIndex = 1 } },
            };
        }

        /// <summary>
        /// 跟随一个 Actor 的流式体积：站着不动的目标什么都不问，跨一格问
        /// 一次，而一格预算的窗口在反复几帧里填满、然后停止询问。
        /// </summary>
        /// <remarks>
        /// 触发是流式里唯一会每帧跑的部分，所以两个方向的失败都
        /// 要紧：问得太勤会把走路变成一顿一顿的，问得太少会让地平线空着。体积
        /// 是这两半唯一的会合处——格号来自被跟随的 Actor、积压来自
        /// 各层运行、答案来自那个纯触发——而离屏门会编译 PCGVolume 却从不**运行**
        /// 它，所以只有活着的场景能显示这段接线。
        /// </remarks>
        private static int TestStreamingFollow(List<string> failures)
        {
            var local = new List<string>();
            string graphPath = System.IO.Path.Combine(Globals.TemporaryFolder, "pcg_selftest_follow.json");
            PCGVolume volume = null;

            // 任何具体的 Actor 都能当被跟随者；Foliage 是这个插件本来就会构造的
            // 里面最便宜的一个，而触发只读它的 Position。
            Foliage target = null;

            try
            {
                if (!PCGGraphFile.TrySave(graphPath, LayerGraph("SelfTestFollow", 400), out string saveError))
                {
                    local.Add($"could not save the follow test graph: {saveError}");
                }
                else
                {
                    volume = new PCGVolume();
                    target = new Foliage();

                    if (!PCGActors.TrySpawn(volume, null))
                    {
                        local.Add("could not spawn a PCGVolume for the follow test");
                    }
                    else if (!PCGActors.TrySpawn(target, null))
                    {
                        local.Add("could not spawn the follow test's target actor");
                    }
                    else
                    {
                        volume.Name = "PCG_SelfTest_Follow";
                        volume.Mode = PCGGenerationMode.Preview;
                        volume.GraphPath = graphPath;
                        volume.Seed = 909;
                        volume.Volume = new BoundingBox(
                            new Vector3(-1200.0f, -50.0f, -1200.0f), new Vector3(1200.0f, 50.0f, 1200.0f));
                        volume.Partitioned = true;
                        volume.PartitionCellSize = 400.0f;
                        volume.Streaming = true;
                        volume.StreamRadius = 700.0f;
                        volume.StreamKeepRadius = 700.0f;
                        volume.StreamFollowTarget = target;

                        target.Position = new Vector3();

                        if (!volume.UpdateStreaming())
                        {
                            local.Add("the first frame with a followed target asked for nothing");
                        }
                        else
                        {
                            int unbudgeted = volume.LastResult?.PointCount ?? 0;
                            if (unbudgeted <= 0)
                                local.Add("the first follow request produced no points");

                            if (volume.UpdateStreaming())
                                local.Add("a target that has not moved asked for a second generation");

                            // 还是同一个格子，所以还是没事可做——这正是"跟随在普通帧上只花一次
                            // 位置读取、而不是一次生成"所声称的。
                            target.Position = new Vector3(60.0f, 0.0f, 0.0f);
                            if (volume.UpdateStreaming())
                                local.Add("moving within the same cell asked for a generation");

                            // 原点以西半个单位就是另一个格子。一个截断的强转会把它当成
                            // 与 +60 同一个格，于是往西走时窗口永远不会被重切。
                            target.Position = new Vector3(-0.1f, 0.0f, 0.0f);
                            if (!volume.UpdateStreaming())
                                local.Add("crossing west of the origin was not seen as a new cell");

                            // 预算自己那条路：在一个新中心上"每请求一格"必须一直问
                            // 到窗口填满为止、然后停下，并且最终结果与在该中心上
                            // 一次不带预算的请求报出的完全一致。
                            volume.StreamCellsPerRequest = 1;
                            target.Position = new Vector3(-800.0f, 0.0f, 400.0f);

                            if (!volume.UpdateStreaming())
                            {
                                local.Add("a one-cell budget at a new centre asked for nothing");
                            }
                            else
                            {
                                int first = volume.LastResult?.PointCount ?? 0;
                                int requests = 1;
                                int filled = first;

                                while (requests < 60 && volume.UpdateStreaming())
                                {
                                    filled = volume.LastResult?.PointCount ?? 0;
                                    requests++;
                                }

                                if (requests >= 60)
                                    local.Add("the deferred backlog never stopped asking (60 requests and counting)");

                                if (filled <= first)
                                    local.Add($"the deferred backlog never grew the window: {first} then {filled}");

                                volume.StreamCellsPerRequest = 0;
                                int reference = volume.Generate() && volume.LastResult != null
                                    ? volume.LastResult.PointCount
                                    : 0;

                                if (reference != filled)
                                    local.Add($"a budgeted window should end on the unbudgeted count exactly " +
                                              $"({reference}), got {filled} after {requests} request(s)");

                                // 跟随的步长必须来自**最细**的那一层。一个体积同时带着
                                // 800 单位与 100 单位两层，若按粗网格来判断是否跨格，细层的
                                // 窗口会落后玩家最多八格——那读起来就像
                                // 地面细节直到玩家快过去一公里才出现。
                                volume.StreamCellsPerRequest = 0;
                                volume.Layers = new[]
                                {
                                    new PCGPartitionLayer { Name = "coarse", CellSize = 800.0f },
                                    new PCGPartitionLayer { Name = "fine", CellSize = 100.0f },
                                };

                                target.Position = new Vector3(-649.0f, 0.0f, 400.0f);

                                int synced = 0;
                                while (synced < 3 && volume.UpdateStreaming())
                                    synced++;

                                // 往西 52 个单位跨过了一条 100 单位格边，却仍留在同一个
                                // 800 单位格里，所以这只有在步长真的来自细层时才算一次请求。
                                // 先同步是让这条断言有辨别力的关键：不同步的话，
                                // 记下来的格子还停留在上一步的单位里，任何移动都会
                                // 看起来像一次跨格。
                                target.Position = new Vector3(-701.0f, 0.0f, 400.0f);
                                if (!volume.UpdateStreaming())
                                    local.Add("a 52-unit move across a 100-unit layer's cell edge did not " +
                                              "re-cut the window: the follow step is not the finest layer's");

                                // 两个玩家而不是一个。窗口变成他们各自邻里的并集，
                                // 所以它必须覆盖**严格更多**的地面，而不只是一个玩家的——
                                // 而这用一次直接的 Generate 来量、而不是经触发，于是
                                // 被测的是并集选择本身、不是帧逻辑。
                                var second = new Foliage();

                                if (!PCGActors.TrySpawn(second, null))
                                {
                                    local.Add("could not spawn the second follow target");
                                }
                                else
                                {
                                    target.Position = new Vector3(-400.0f, 0.0f, -400.0f);
                                    second.Position = new Vector3(400.0f, 0.0f, 400.0f);

                                    volume.StreamCellsPerRequest = 0;
                                    volume.StreamRadius = 400.0f;
                                    volume.StreamKeepRadius = 400.0f;
                                    volume.StreamFollowTarget = null;
                                    volume.StreamFollowTargets = new Actor[] { target, second };

                                    int pair = volume.Generate() && volume.LastResult != null
                                        ? volume.LastResult.PointCount
                                        : 0;

                                    // 宿主给的 source 正是分块流式器驱动这里的方式，而它必须是
                                    // 一个**输入**而不是第二套机制：同两个位置经这个委托交进来，
                                    // 必须给出与刚才那两个 Actor 一模一样的窗口。
                                    var viewerPositions = new List<Vector3> { target.Position, second.Position };

                                    volume.StreamFollowTargets = null;
                                    volume.StreamViewerSource = list =>
                                    {
                                        list.AddRange(viewerPositions);
                                        return viewerPositions.Count;
                                    };

                                    int viaSource = volume.Generate() && volume.LastResult != null
                                        ? volume.LastResult.PointCount
                                        : 0;

                                    volume.StreamViewerSource = null;

                                    if (viaSource != pair)
                                        local.Add($"a source-driven window should match the actor-driven one: " +
                                                  $"{viaSource} vs {pair}");

                                    volume.StreamFollowTargets = new Actor[] { target };

                                    int single = volume.Generate() && volume.LastResult != null
                                        ? volume.LastResult.PointCount
                                        : 0;

                                    if (single <= 0 || pair <= single)
                                        local.Add($"two viewers should cover more ground than one: " +
                                                  $"pair {pair} vs single {single}");

                                    Actor.Destroy(second);
                                }

                                if (local.Count == 0)
                                {
                                    Debug.Log($"[PCG] PASS streaming follow: idle frames cost nothing, " +
                                              $"one request per cell crossed, a one-cell budget filled " +
                                              $"{unbudgeted} point(s) worth of window in {requests} requests " +
                                              $"({first} -> {filled}), an 800/100 layered volume steps on " +
                                              $"the 100-unit grid, and two viewers cover more ground than one");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                local.Add($"streaming follow check threw {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                foreach (var actor in new Actor[] { volume, target })
                {
                    if (actor == null)
                        continue;

                    try
                    {
                        Actor.Destroy(actor);
                    }
                    catch (Exception)
                    {
                        // 拆解不该决定结果。
                    }
                }

                try
                {
                    if (System.IO.File.Exists(graphPath))
                        System.IO.File.Delete(graphPath);
                }
                catch (Exception)
                {
                    // 一个残留的临时图同理。
                }
            }

            foreach (var message in local)
            {
                failures.Add(message);
                Debug.LogError($"[PCG] FAIL streaming follow: {message}");
            }

            return local.Count;
        }

        /// <summary>
        /// 把一个分区过的体积流式起来：生成它中心周围的窗口、中心离开区域时把窗口清空，
        /// 而窗口再打开时把整片区域拿回来。
        /// </summary>
        /// <remarks>
        /// 选择算术对着距离参照在离屏断言过。只有活着的体积
        /// 能显示的是：收窄后的窗口真的到了运行器手上——一个被切小、请求却
        /// 仍然跑整片区域的计划，在日志里看着一样、在世界里的感受完全不同——
        /// 以及计划是**逐请求重切**而不是沿用上一次的。有意用预览模式：
        /// 这里说的是哪些点回来，而逐格的场景清理是 3b 与 3d 两组的活。
        /// </remarks>
        private static int TestStreamingWindow(List<string> failures)
        {
            var local = new List<string>();
            string graphPath = System.IO.Path.Combine(Globals.TemporaryFolder, "pcg_selftest_stream.json");
            PCGVolume volume = null;

            try
            {
                if (!PCGGraphFile.TrySave(graphPath, LayerGraph("SelfTestStream", 300), out string saveError))
                {
                    local.Add($"could not save the streaming test graph: {saveError}");
                }
                else
                {
                    volume = new PCGVolume();
                    if (!PCGActors.TrySpawn(volume, null))
                    {
                        local.Add("could not spawn a PCGVolume for the streaming test");
                    }
                    else
                    {
                        volume.Name = "PCG_SelfTest_Streaming";
                        volume.Mode = PCGGenerationMode.Preview;
                        volume.GraphPath = graphPath;
                        volume.Seed = 4242;
                        volume.Volume = new BoundingBox(
                            new Vector3(-1200.0f, -50.0f, -1200.0f), new Vector3(1200.0f, 50.0f, 1200.0f));
                        volume.Partitioned = true;
                        volume.PartitionCellSize = 400.0f;

                        int whole = volume.Generate() && volume.LastResult != null
                            ? volume.LastResult.PointCount
                            : 0;
                        if (whole <= 0)
                        {
                            local.Add("the unstreamed partitioned generation produced nothing");
                        }
                        else
                        {
                            volume.Streaming = true;
                            volume.StreamCenter = new Vector3();
                            volume.StreamRadius = 450.0f;
                            volume.StreamKeepRadius = 450.0f;

                            int window = volume.Generate() && volume.LastResult != null
                                ? volume.LastResult.PointCount
                                : 0;
                            if (window <= 0 || window >= whole)
                                local.Add($"a {volume.StreamRadius:F0}-unit window should generate part of the region, got " +
                                          $"{window} of {whole}");

                            volume.StreamCenter = new Vector3(50000.0f, 0.0f, -50000.0f);
                            if (volume.Generate() && volume.LastResult != null && volume.LastResult.PointCount > 0)
                                local.Add("a window outside the region still generated points");

                            // 逐请求重置：一个从上一次请求沿用下来的格子计划会在这里报出
                            // 窗口的计数而不是区域的，恰好相等。
                            volume.StreamCenter = new Vector3();
                            volume.StreamRadius = 5000.0f;
                            volume.StreamKeepRadius = 5000.0f;

                            int reopened = volume.Generate() && volume.LastResult != null
                                ? volume.LastResult.PointCount
                                : 0;
                            if (reopened != whole)
                                local.Add($"opening the window back up should reproduce the whole region exactly " +
                                          $"({whole}), got {reopened}");

                            // 逐请求预算，端到端。两件事以前这里都错了，而它们合起来让这两条断言
                            // 与预算是否生效完全无关：
                            // 一是**度量口径**——window 是半径 450 量出来的（12 格），而预算跑用的是 700
                            //   （16 格），拿小窗口去比大窗口，结果恒为"预算没起效"（实测 4800 对 3600）。
                            // 二是**额度的语义**——额度限的是"这一请求要算的格子"，缓存已经存下的格子免费、
                            //   不占额度；之前跑过的整片区域把每一格都变成了免费，于是预算合法地不删任何格子。
                            // 所以：先用同一个半径 700 量出 full（这一步也正面钉住"窗口比圆心那圈大、比整片小"），
                            // 再换种子把缓存对这次窗口作废，预算才有东西可延后。
                            volume.StreamRadius = 700.0f;
                            volume.StreamKeepRadius = 450.0f;

                            int full = volume.Generate() && volume.LastResult != null
                                ? volume.LastResult.PointCount
                                : 0;
                            if (full <= window || full >= whole)
                                local.Add($"a {volume.StreamRadius:F0}-unit window should sit between the " +
                                          $"450-unit window and the whole region, got {full} " +
                                          $"(450-window {window}, whole {whole})");

                            volume.Seed = 4243;
                            volume.StreamCellsPerRequest = 1;

                            int oneCell = volume.Generate() && volume.LastResult != null
                                ? volume.LastResult.PointCount
                                : 0;
                            if (oneCell <= 0 || oneCell >= full)
                                local.Add($"a one-cell budget should produce part of the window, got {oneCell} (full={full})");

                            // 反复请求必须**收敛到整个窗口**：免费的格子不占额度，所以每多一次请求就多推
                            // 一格，外围因此不会被预算饿死。收敛本身就是这条设计的证据。
                            int filled = oneCell;
                            for (int r = 0; r < 64 && filled != full; r++)
                            {
                                filled = volume.Generate() && volume.LastResult != null
                                    ? volume.LastResult.PointCount
                                    : 0;
                            }

                            volume.StreamCellsPerRequest = 0;

                            if (filled != full)
                                local.Add($"repeated budgeted requests should fill the window to exactly {full} " +
                                          $"point(s), got {filled}");

                            if (local.Count == 0)
                            {
                                Debug.Log($"[PCG] PASS streaming window: {whole} point(s) for the region, " +
                                          $"{window} within 450 of the centre, {full} within 700, nothing " +
                                          $"off-region, {reopened} again when reopened, {oneCell} then {filled} " +
                                          $"on a one-cell request budget");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                local.Add($"streaming window check threw {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (volume != null)
                {
                    try
                    {
                        Actor.Destroy(volume);
                    }
                    catch (Exception)
                    {
                        // 拆解不该决定结果。
                    }
                }

                try
                {
                    if (System.IO.File.Exists(graphPath))
                        System.IO.File.Delete(graphPath);
                }
                catch (Exception)
                {
                    // 一个残留的临时图同理。
                }
            }

            foreach (var message in local)
            {
                failures.Add(message);
                Debug.LogError($"[PCG] FAIL streaming window: {message}");
            }

            return local.Count;
        }

        /// <summary>
        /// 把一个体积生成成两层，并检查设计师否则看不见的层记账。
        /// </summary>
        /// <remarks>
        /// 有意用只含数据的图：带生成器节点的分层体积也会顺便演练逐格清理，
        /// 但那需要每层一个模型资产，而命名隔离已经在离屏断言过了。这里
        /// 检查的是体积级的契约——两层都跑了、都产出了点、报出的总数
        /// 是它们之和，以及再生成一次不会改变其中任何一项。
        /// </remarks>
        private static int TestGenerationLayers(List<string> failures)
        {
            var local = new List<string>();
            string coarsePath = System.IO.Path.Combine(Globals.TemporaryFolder, "pcg_selftest_layer_coarse.json");
            string finePath = System.IO.Path.Combine(Globals.TemporaryFolder, "pcg_selftest_layer_fine.json");
            PCGVolume volume = null;

            try
            {
                string coarseError = null;
                string fineError = null;

                if (!PCGGraphFile.TrySave(coarsePath, LayerGraph("SelfTestLayerCoarse", 120), out coarseError) ||
                    !PCGGraphFile.TrySave(finePath, LayerGraph("SelfTestLayerFine", 400), out fineError))
                {
                    local.Add($"could not save the layer test graphs: {coarseError ?? fineError}");
                }
                else
                {
                    volume = new PCGVolume();
                    if (!PCGActors.TrySpawn(volume, null))
                    {
                        local.Add("could not spawn a PCGVolume for the layer test");
                    }
                    else
                    {
                        volume.Name = "PCG_SelfTest_Layers";
                        volume.Mode = PCGGenerationMode.Preview;
                        volume.GraphPath = coarsePath;
                        volume.Seed = 717;
                        volume.Volume = new BoundingBox(
                            new Vector3(-400.0f, -50.0f, -400.0f), new Vector3(400.0f, 50.0f, 400.0f));
                        volume.Layers = new[]
                        {
                            new PCGPartitionLayer { Name = "coarse", GraphPath = coarsePath, CellSize = 800.0f },
                            new PCGPartitionLayer
                            {
                                Name = "fine", GraphPath = finePath, CellSize = 100.0f, SeedOffset = 1000,
                            },
                        };

                        if (!volume.Generate())
                            local.Add("the layered generation produced nothing");

                        var layers = volume.LayerResults;
                        var labels = volume.LayerLabels;

                        if (layers.Length != 2)
                        {
                            local.Add($"a two-layer volume reported {layers.Length} layer result(s)");
                        }
                        else
                        {
                            if (string.Join(",", labels) != "coarse,fine")
                                local.Add($"layer labels came back as '{string.Join(",", labels)}'");

                            if (layers[0].PointCount <= 0 || layers[1].PointCount <= 0)
                                local.Add($"a layer produced no points (coarse {layers[0].PointCount}, fine {layers[1].PointCount})");

                            int sum = layers[0].PointCount + layers[1].PointCount;
                            if (volume.LastResult == null || volume.LastResult.PointCount != sum)
                                local.Add($"the volume total should be the two layers summed ({sum}), got {volume.LastResult?.PointCount ?? -1}");

                            var again = volume.Generate();
                            var second = volume.LayerResults;

                            if (!again || second.Length != 2)
                            {
                                local.Add("the second layered generation failed");
                            }
                            else if (second[0].PointCount != layers[0].PointCount ||
                                     second[1].PointCount != layers[1].PointCount)
                            {
                                local.Add("repeating a layered generation changed a layer's point count: " +
                                          $"{layers[0].PointCount}+{layers[1].PointCount} then " +
                                          $"{second[0].PointCount}+{second[1].PointCount}");
                            }
                            else
                            {
                                Debug.Log($"[PCG] PASS layered volume: {layers[0].PointCount} + {layers[1].PointCount} " +
                                          $"point(s) across {layers.Length} layer(s), stable on regenerate");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                local.Add($"layered volume check threw {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (volume != null)
                {
                    try
                    {
                        Actor.Destroy(volume);
                    }
                    catch (Exception)
                    {
                        // 拆解不该决定结果。
                    }
                }

                foreach (var path in new[] { coarsePath, finePath })
                {
                    try
                    {
                        if (System.IO.File.Exists(path))
                            System.IO.File.Delete(path);
                    }
                    catch (Exception)
                    {
                        // 一个残留的临时图同理。
                    }
                }
            }

            foreach (var message in local)
            {
                failures.Add(message);
                Debug.LogError($"[PCG] FAIL layered volume: {message}");
            }

            return local.Count;
        }

        /// <summary>一个采样器接一个"全留"的密度过滤器：一张不依赖场景的图。</summary>
        private static Graph.PCGGraph LayerGraph(string name, int pointCount)
        {
            return new Graph.PCGGraph
            {
                Name = name,
                Nodes = new[]
                {
                    new Graph.PCGNode
                    {
                        TypeId = PCG.Nodes.PCGVolumeSamplerNode.Id,
                        Values = new object[] { pointCount, 0, 1.0f, false, 1.0f },
                    },
                    new Graph.PCGNode
                    {
                        TypeId = PCG.Nodes.PCGDensityFilterNode.Id,
                        Values = new object[] { 0.0f, 1.0f, false },
                    },
                },
                Edges = new[] { new Graph.PCGEdge { OutputNodeIndex = 0, InputNodeIndex = 1 } },
            };
        }

        private static int CountFoliageActors(Actor parent)
        {
            var children = parent?.GetChildren<Foliage>();
            return children?.Length ?? 0;
        }

        /// <summary>
        /// 冷资产解析：一个存在、类型正确、只是本会话还没被加载过的模型，必须照样取得到。
        /// </summary>
        /// <remarks>
        /// 起因是引擎日志里那句 <c>Model asset 1b4a643d-… is not loaded or is not a Model</c>——那个 Guid 是引擎图元
        /// <c>Cone.flax</c>，资产与类型都没问题，缺的只是"还没人加载过"：<c>Content.GetAsset</c> 的契约（引擎源码
        /// <c>Content.h</c>）写的是 "The found asset or null if not loaded"，它只翻已经载入的池子。
        /// 所以生成器取资产一律走 <c>PCGAssetLoading.Resolve</c>。
        /// 夹具用 <c>Capsule.flax</c>：工程里那八份 PCG 图与七份演示描述都没引用过它（按资产头对过账），
        /// 所以它是本会话唯一还可能是"冷"的模型。**诚实之处**：冷不冷是当场量的（调用前 <c>GetAsset</c> 的结果），
        /// 不冷就如实报"这一轮只证到快路径"并计入 skipped，而不是把"没吵"报成"验过"。
        /// </remarks>
        private static bool TestColdAssetResolution(out string error)
        {
            error = null;

            var capsule = new Guid("3f82e577-7c28-44f5-b650-51289821ad46");
            var coldAtCheck = Content.GetAsset(capsule) is Model == false;

            var resolved = PCGAssetLoading.Resolve<Model>(capsule);

            if (resolved == null)
            {
                error = $"Resolve<Model> 对引擎图元 Capsule（{capsule}）给了 null——取资产这条路整个不通。";
                return false;
            }

            // 反向对照：一个谁都不指的 Guid 必须取不到东西，否则上面那条"取到了"只证明"什么都能取到"。
            // 这一条会多出一行引擎自己那句"加载不到资产"的错误日志，是预期的。
            if (PCGAssetLoading.Resolve<Model>(new Guid("0b0b0b0b-0b0b-0b0b-0b0b-0b0b0b0b0b0b")) != null)
            {
                error = "Resolve<Model> 对一个不存在的 Guid 也取到了资产 ⇒ 正向那条没守住任何东西。";
                return false;
            }

            // 真加载过的东西必须留在池子里：旧的快路径事后也得看得见同一个实例。
            var after = Content.GetAsset(capsule) as Model;

            if (!ReferenceEquals(after, resolved))
            {
                error = "Resolve 取到的 Capsule 没有落进资产池（事后 GetAsset 给 " +
                        (after == null ? "null" : "另一个实例") + "）⇒ 那次加载没真的发生，或者被立刻卸掉了。";
                return false;
            }

            var note = coldAtCheck
                ? "取之前 GetAsset 读不到它（确实是冷的）⇒ 慢路径真跑过"
                : "取之前它已经在池子里 ⇒ 这一轮只证到快路径";

            if (!coldAtCheck)
                Skip("cold asset resolution: Capsule 在本轮已被加载过，冷的那一半没证到；"
                     + "编辑器重启后、在任何生成之前跑自检才算真证");

            Debug.Log($"[PCG] PASS cold asset resolution: {note}（{resolved.Path}）");
            return true;
        }

        /// <summary>
        /// 载入三条放置检查共用的 Model。字段里可以写工程内资产路径，也可以写资产 Guid。
        /// </summary>
        /// <remarks>
        /// 生成器节点按 Guid 取资产走 <c>PCGAssetLoading.Resolve</c>（先查已载入的池子，取不到再请求一次真加载），
        /// 而 <c>Content.Load(path)</c> 的路径口径
        /// 只对**工程内**资产成立；引擎自带的图元（在工程资产库之外）拿路径取不到。
        /// 所以按 Guid 那条路是通用的，而把路径解析成 Guid 是编辑器的活（`pcg_selftest` 工具
        /// 用资产库或资产头做这件事，再把 Guid 填进这个字段）。
        /// </remarks>
        private static Model LoadFixtureModel(string pathOrId)
        {
            if (string.IsNullOrWhiteSpace(pathOrId))
                return null;

            if (Guid.TryParse(pathOrId, out var id))
            {
                var byId = Content.Load<Model>(id);
                if (byId != null)
                    return byId;
            }

            return Content.Load<Model>(pathOrId);
        }

        /// <summary>
        /// 载入贴花放置检查用的材质。与模型 / Prefab 那两个夹具同一个写法：**认 Guid，也认工程内路径**——
        /// 引擎自带的 Decal 域材质在工程资产库之外，只有 Guid 那条路取得到（`pcg_selftest` 工具把字段里的
        /// 路径解析成 Guid 再填进来）。`Content.Load` 的返回判据是反的（`WaitForLoaded` 返回 false 才算成功），
        /// 所以这里用 `LoadAsync` 拿句柄、由调用方断 `IsLoaded`。
        /// </summary>
        private static MaterialBase LoadFixtureMaterial(string pathOrId)
        {
            if (string.IsNullOrWhiteSpace(pathOrId))
                return null;

            if (Guid.TryParse(pathOrId, out var id))
                return Content.LoadAsync<MaterialBase>(id);

            return Content.LoadAsync<MaterialBase>(pathOrId);
        }

        /// <summary>
        /// 载入预制体放置检查用的 Prefab。工程内资产按路径取（带不带 <c>.prefab</c> 后缀都试一次，
        /// 因为 <c>Content.Load</c> 的路径口径在两版 Flax 文档里写法不一致），也认 Guid。
        /// </summary>
        /// <remarks>
        /// 这里用 <c>LoadAsync</c> 而不是 <c>Load</c>：<c>Load</c> 在"等待加载"那一步失败时**返回 null**
        /// （它的返回判断是"WaitForLoaded 返回 false 才算成功"），于是一个刚写出来的资产会被读成
        /// "根本不是 Prefab"，跳到跳过分支里——而跳过与真相是两回事。把句柄交回调用方、
        /// 由它自己断 <c>IsLoaded</c>，失败才会报成失败。
        /// </remarks>
        private static Prefab LoadFixturePrefab(string pathOrId)
        {
            if (string.IsNullOrWhiteSpace(pathOrId))
                return null;

            if (Guid.TryParse(pathOrId, out var id))
            {
                var byId = Content.LoadAsync<Prefab>(id);
                if (byId != null)
                    return byId;
            }

            var byPath = Content.LoadAsync<Prefab>(pathOrId);
            if (byPath != null)
                return byPath;

            const string extension = ".prefab";
            if (pathOrId.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                return Content.LoadAsync<Prefab>(pathOrId.Substring(0, pathOrId.Length - extension.Length));

            return null;
        }

        private static Foliage FindFoliageActor(Actor parent)
        {
            var children = parent?.GetChildren<Foliage>();
            if (children == null)
                return null;

            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] != null)
                    return children[i];
            }

            return null;
        }

        /// <summary>
        /// 检查 Prefab 生成器的失败路径；给了预制体时，再检查它的产出归
        /// 这个体积所有、并被分区格子裁剪。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 这里有两件事在别处都查不了：<c>SpawnPrefab</c> 把 Actor 作为**父节点**交给
        /// 体积（正是它让 <see cref="PCGVolume.Clear"/> 能精确移除这一次生成、
        /// 而不是设计师手摆的东西），以及一次裁剪过的运行会让邻格的物体
        /// 不被生成、而不是被摆两遍。
        /// </para>
        /// <para>
        /// 放置那一半需要一个真的 Prefab 资产——引擎没有脚本够得到的——所以它在
        /// <see cref="ActorPrefabPath"/> 为空或载不进来时带一句话跳过。
        /// </para>
        /// </remarks>
        private static int TestSpawnActor(List<string> failures, string prefabPath)
        {
            const int nodeIndex = 12;
            const int pointCount = 6;

            var local = new List<string>();
            var volumes = new List<PCGVolume>();

            try
            {
                var points = new PCGPoint[pointCount];
                for (int i = 0; i < pointCount; i++)
                {
                    points[i] = PCGPoint.Create(new Vector3(-25.0f + i * 10.0f, 0.0f, 0.0f), 1.0f, i);
                    points[i].UpdateBounds(new Vector3(2.0f, 2.0f, 2.0f));
                }

                var data = new PCGData(points);
                var region = new BoundingBox(new Vector3(-60, -10, -10), new Vector3(60, 10, 10));

                var owner = SpawnTestVolume(volumes, "PCG_SelfTest_SpawnActor");
                if (owner == null)
                {
                    local.Add("could not spawn a PCGVolume to own the prefab spawn test");
                }
                else
                {
                    var context = new PCGExecutionContext
                    {
                        Seed = 9,
                        CurrentNodeId = nodeIndex,
                        GenerationBounds = region,
                        Source = owner,
                    };

                    // 先跑失败路径；它们不需要资产，而且正是那些否则会静默的。
                    var noPrefab = new PCG.Nodes.PCGSpawnActorNode(SpawnActorData(Guid.Empty, 500));
                    var passthrough = noPrefab.OnExecute(context, new[] { data });
                    if (noPrefab.LastError == null)
                        local.Add("a prefab spawner with no prefab reported no problem");
                    if (passthrough == null || passthrough.Count != pointCount)
                        local.Add($"a failed prefab run should pass the points through, got {passthrough?.Count ?? -1}");
                    if (CountGenerated(owner) != 0)
                        local.Add($"a failed prefab run still put {CountGenerated(owner)} actor(s) in the scene");

                    var prefab = LoadFixturePrefab(prefabPath);

                    if (prefab == null)
                    {
                        Skip(string.IsNullOrEmpty(prefabPath)
                            ? "prefab placement: ActorPrefabPath is empty."
                            : $"prefab placement: {prefabPath} did not load as a Prefab.");
                    }
                    else
                    {
                        prefab.WaitForLoaded(10000.0);
                        if (!prefab.IsLoaded)
                        {
                            local.Add($"the test prefab {prefabPath} would not load");
                        }
                        else
                        {
                            // 上限为零只有手握真预制体时才走得到，因为节点
                            // 在查自己的参数之前就先报"没有资产"。
                            var capped = new PCG.Nodes.PCGSpawnActorNode(SpawnActorData(prefab.ID, 0));
                            capped.OnExecute(context, new[] { data });
                            if (capped.LastError == null || !capped.LastError.Contains("zero"))
                                local.Add("MaxActors = 0 was accepted without saying that nothing can be spawned");
                            if (CountGenerated(owner) != 0)
                                local.Add($"a zero-ceiling run still spawned {CountGenerated(owner)} actor(s)");

                            var node = new PCG.Nodes.PCGSpawnActorNode(SpawnActorData(prefab.ID, 500));
                            var placed = node.OnExecute(context, new[] { data });

                            if (node.LastError != null)
                                local.Add($"the prefab run reported: {node.LastError}");
                            if (node.SpawnedCount != pointCount)
                                local.Add($"expected {pointCount} spawned actors, got {node.SpawnedCount}");
                            if (placed == null || placed.Count != pointCount)
                                local.Add($"the prefab spawner should pass on every point it spawned, got {placed?.Count ?? -1}");

                            // 归属是体积清理的全部依据：挂在体积之下，
                            // 于是 Clear 能精确移除这一次生成、不碰手摆的内容。
                            int generated = CountGenerated(owner);
                            if (generated != pointCount)
                                local.Add($"the volume should own {pointCount} generated actor(s), found {generated}");

                            int removed = owner.Clear();
                            if (removed != pointCount)
                                local.Add($"Clear() removed {removed} actor(s), expected {pointCount}");

                            // 裁剪过的格子只能摆自己那一半——而且摆在自己的体积上，于是上一次
                            // 运行的 Actor 不可能被误当成这一次的残留。
                            var clippedOwner = SpawnTestVolume(volumes, "PCG_SelfTest_SpawnActor_Clipped");
                            if (clippedOwner == null)
                            {
                                local.Add("could not spawn the second volume for the clipped prefab run");
                            }
                            else
                            {
                                var clippedContext = new PCGExecutionContext
                                {
                                    Seed = 9,
                                    CurrentNodeId = nodeIndex,
                                    GenerationBounds = region,
                                    Source = clippedOwner,
                                    ClipCell = new PCGPartitionCell(
                                        region,
                                        new BoundingBox(new Vector3(-60, -10, -10), new Vector3(0, 10, 10)),
                                        0, 0, false, true),
                                };

                                var clippedNode = new PCG.Nodes.PCGSpawnActorNode(SpawnActorData(prefab.ID, 500));
                                var clippedPlaced = clippedNode.OnExecute(clippedContext, new[] { data });

                                if (clippedNode.SpawnedCount != pointCount / 2)
                                    local.Add($"a cell covering half the region spawned {clippedNode.SpawnedCount}, expected {pointCount / 2}");
                                if (clippedPlaced != null && clippedPlaced.Count != pointCount / 2)
                                    local.Add($"the clipped run passed on {clippedPlaced.Count} point(s), expected {pointCount / 2}");
                                if (CountGenerated(clippedOwner) != pointCount / 2)
                                    local.Add($"the clipped volume holds {CountGenerated(clippedOwner)} actor(s), expected {pointCount / 2}");
                            }

                            if (local.Count == 0)
                            {
                                Debug.Log($"[PCG] PASS prefab spawner: {pointCount} owned by the volume, " +
                                          $"{pointCount / 2} per clipped cell");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                local.Add($"prefab spawner check threw {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                for (int i = 0; i < volumes.Count; i++)
                {
                    try
                    {
                        Actor.Destroy(volumes[i]);
                    }
                    catch (Exception)
                    {
                        // 拆解出问题不该决定这次运行的结果。
                    }
                }
            }

            foreach (var message in local)
            {
                failures.Add(message);
                Debug.LogError($"[PCG] FAIL prefab spawner: {message}");
            }

            return local.Count;
        }

        /// <summary>
        /// 检查贴花生成器的各种拒绝，以及在给了 Decal 域材质时的放置。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 这里要紧的守卫是材质**域**：一个带表面材质的 Decal Actor 什么都不会
        /// 渲染，于是一张"生成成功"的图会显示出一片空场，而设计师
        /// 会去怀疑采样器。在运行期点名拒绝，是插件能说这话的最早时刻。
        /// </para>
        /// <para>
        /// 本项目没有 Decal 域材质，所以放置那一半只在
        /// <see cref="DecalMaterialPath"/> 指向一个时才跑。任何材质路径都仍会演练那次拒绝，
        /// 而那一半是最容易被人无意撞上的。
        /// </para>
        /// </remarks>
        private static int TestSpawnDecal(List<string> failures, string materialPath)
        {
            const int nodeIndex = 13;
            const int pointCount = 4;

            var local = new List<string>();
            var volumes = new List<PCGVolume>();

            try
            {
                var owner = SpawnTestVolume(volumes, "PCG_SelfTest_SpawnDecal");
                if (owner == null)
                {
                    local.Add("could not spawn a PCGVolume to own the decal test");
                }
                else
                {
                    var points = new PCGPoint[pointCount];
                    for (int i = 0; i < pointCount; i++)
                    {
                        points[i] = PCGPoint.Create(new Vector3(-15.0f + i * 10.0f, 0.0f, 0.0f), 1.0f, i);
                        points[i].UpdateBounds(new Vector3(2.0f, 2.0f, 2.0f));
                    }

                    var data = new PCGData(points);
                    var context = new PCGExecutionContext
                    {
                        Seed = 11,
                        CurrentNodeId = nodeIndex,
                        GenerationBounds = new BoundingBox(new Vector3(-60, -10, -10), new Vector3(60, 10, 10)),
                        Source = owner,
                    };

                    var unset = new PCG.Nodes.PCGSpawnDecalNode(DecalData(Guid.Empty, 500));
                    var passthrough = unset.OnExecute(context, new[] { data });
                    if (unset.LastError == null)
                        local.Add("a decal spawner with no material reported no problem");
                    if (passthrough == null || passthrough.Count != pointCount)
                        local.Add($"a failed decal run should pass the points through, got {passthrough?.Count ?? -1}");
                    if (CountGenerated(owner) != 0)
                        local.Add($"a failed decal run still put {CountGenerated(owner)} actor(s) in the scene");

                    var material = LoadFixtureMaterial(materialPath);

                    if (material == null)
                    {
                        Skip(string.IsNullOrEmpty(materialPath)
                            ? "decal placement: DecalMaterialPath is empty (the project ships no Decal-domain material)."
                            : $"decal placement: {materialPath} did not load as a material.");
                    }
                    else if (!material.IsDecal)
                    {
                        // 拒绝本身就是断言；一个域错的路径是完全够用的触发方式，
                        // 而设计师会被告知还有哪一半没查。
                        var wrong = new PCG.Nodes.PCGSpawnDecalNode(DecalData(material.ID, 500));
                        wrong.OnExecute(context, new[] { data });

                        if (wrong.LastError == null || wrong.LastError.IndexOf("Decal", StringComparison.Ordinal) < 0)
                            local.Add($"a surface material was not refused by name (error: {wrong.LastError ?? "none"})");
                        if (CountGenerated(owner) != 0)
                            local.Add($"a refused decal material still put {CountGenerated(owner)} actor(s) in the scene");

                        Debug.Log($"[PCG] SKIP decal placement: {materialPath} is not Decal-domain; " +
                                  "the refusal was checked, the placement was not.");
                    }
                    else
                    {
                        var node = new PCG.Nodes.PCGSpawnDecalNode(DecalData(material.ID, 500));
                        var placed = node.OnExecute(context, new[] { data });

                        if (node.LastError != null)
                            local.Add($"the decal run reported: {node.LastError}");
                        if (node.SpawnedCount != pointCount)
                            local.Add($"expected {pointCount} decals, got {node.SpawnedCount}");
                        if (placed == null || placed.Count != pointCount)
                            local.Add($"the decal spawner should pass on every point it placed, got {placed?.Count ?? -1}");
                        if (CountGenerated(owner) != pointCount)
                            local.Add($"the volume should own {pointCount} decal actor(s), found {CountGenerated(owner)}");

                        // 薄层判据：密度合计不足一片时，"整层没长出来"与"这次抽中了"两种结果都得自己说出来。
                        // 静态模型/几何/植被三个生成器早就有这把尺子，贴花是最后一个没有的——它在引擎里才跑得动，
                        // 所以这一半证据只能落在自检，离屏门看不见（贴花要真材质、要往场景里写 Actor）。
                        var thinPoints = new PCGPoint[3];
                        for (int i = 0; i < thinPoints.Length; i++)
                        {
                            thinPoints[i] = PCGPoint.Create(new Vector3(-15.0f + i * 10.0f, 0.0f, 0.0f), 0.2f, i);
                            thinPoints[i].UpdateBounds(new Vector3(2.0f, 2.0f, 2.0f));
                        }

                        var thinInput = new PCGData(thinPoints);
                        int thinSeedsPlaced = 0;
                        int thinSeedsEmpty = 0;
                        string thinEmptyError = null;
                        string thinPlacedError = null;

                        for (int seed = 0; seed < 12; seed++)
                        {
                            context.Seed = 1000 + seed;

                            var thin = new PCG.Nodes.PCGSpawnDecalNode(DecalData(material.ID, 500, 0.1f));
                            thin.OnExecute(context, new[] { thinInput });

                            if (thin.SpawnedCount == 0)
                            {
                                thinSeedsEmpty++;
                                thinEmptyError = thin.LastError;
                            }
                            else
                            {
                                thinSeedsPlaced++;
                                thinPlacedError = thin.LastError;
                            }
                        }

                        context.Seed = 11;

                        // 概率夹具先要证明**自己够得着**两条分支，否则下面两条是在读一句从没执行过的话。
                        if (thinSeedsEmpty == 0 || thinSeedsPlaced == 0)
                        {
                            local.Add($"薄层夹具没跑遍两条分支：{thinSeedsPlaced} 个种子摆出过、" +
                                      $"{thinSeedsEmpty} 个种子空手（期望合计 0.6 片）");
                        }

                        if (thinEmptyError == null ||
                            thinEmptyError.IndexOf("expected 0.6 decal", StringComparison.Ordinal) < 0 ||
                            thinEmptyError.IndexOf("dropped by the density draw", StringComparison.Ordinal) < 0)
                        {
                            local.Add($"整层空手时没报出期望件数与去向：{thinEmptyError ?? "null"}");
                        }

                        if (thinPlacedError == null ||
                            thinPlacedError.IndexOf("expected only 0.6 decal", StringComparison.Ordinal) < 0)
                        {
                            local.Add($"摆出了东西但期望不足一片时没点名『一次抽签』：{thinPlacedError ?? "null"}");
                        }

                        var capped = new PCG.Nodes.PCGSpawnDecalNode(DecalData(material.ID, 0));
                        capped.OnExecute(context, new[] { data });
                        if (capped.LastError == null || !capped.LastError.Contains("zero"))
                            local.Add("MaxDecals = 0 was accepted without saying that nothing can be spawned");

                        Debug.Log($"[PCG] PASS decal spawner: {node.SpawnedCount} decals owned by the volume");
                    }
                }
            }
            catch (Exception ex)
            {
                local.Add($"decal spawner check threw {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                for (int i = 0; i < volumes.Count; i++)
                {
                    try
                    {
                        Actor.Destroy(volumes[i]);
                    }
                    catch (Exception)
                    {
                        // 拆解出问题不该决定这次运行的结果。
                    }
                }
            }

            foreach (var message in local)
            {
                failures.Add(message);
                Debug.LogError($"[PCG] FAIL decal spawner: {message}");
            }

            return local.Count;
        }

        private static PCGNodeRegistry.PCGNodeSerializationData DecalData(Guid materialId, int maxDecals,
            float densityThreshold = 0.0f) =>
            new PCGNodeRegistry.PCGNodeSerializationData(
                PCG.Nodes.PCGSpawnDecalNode.Id,
                new object[] { materialId, densityThreshold, new Vector3(40.0f, 40.0f, 40.0f), 5.0f, 0, maxDecals, false },
                13);

        /// <summary>
        /// 采样一个真的模型资产，并检查只有资产能回答的两件事。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 面积加权、重心映射与退化三角形跳过都在
        /// 离屏 harness 里对着手搭的几何证明过了。那里证明不了的是**适配器**：
        /// 下载下来的顶点与索引缓冲描述的是渲染器看到的那个网格，以及采出的点云
        /// 落在 <em>actor 的</em>坐标系里而不是网格的。第二件是把
        /// Actor 挪一下、要求点云移动**恰好同样的量**来检查的——这个比较不需要
        /// 知道模型的尺寸，所以引擎自带的盒子当被试完全够用。
        /// </para>
        /// </remarks>
        private static int TestMeshSampler(List<string> failures, string modelPath)
        {
            const int pointCount = 300;

            var local = new List<string>();
            StaticModel host = null;
            PCGVolume owner = null;

            try
            {
                var model = LoadFixtureModel(modelPath);

                if (model == null)
                {
                    Skip($"mesh sampling: {modelPath} did not load as a Model.");
                }
                else
                {
                    owner = new PCGVolume();
                    if (!PCGActors.TrySpawn(owner, null))
                    {
                        local.Add("could not spawn a volume to own the mesh sampling test");
                    }
                    else
                    {
                        owner.Name = "PCG_SelfTest_MeshSampler";

                        host = new StaticModel { Model = model, Name = "PCG_SelfTest_MeshHost" };
                        if (!PCGActors.TrySpawn(host, null))
                        {
                            local.Add("could not spawn the model host to sample");
                        }
                        else
                        {
                            host.Transform = new Transform(
                                new Vector3(0.0f, 60.0f, 0.0f), Quaternion.Identity, new Vector3(2.0f, 2.0f, 2.0f));

                            var region = new BoundingBox(new Vector3(-600, -100, -600), new Vector3(600, 300, 600));

                            var here = SampleOnHost(host, region, owner, pointCount, out string firstError);
                            if (firstError != null)
                                local.Add(firstError);

                            if (here != null && here.Count != pointCount)
                                local.Add($"expected {pointCount} points on the host's surface, got {here.Count}");

                            if (here != null && here.Count > 1)
                            {
                                // 挪动宿主：点云必须恰好按这个增量跟随，而这只有在
                                // Actor 的世界变换真的被复合进去、而不是被忽略时才成立。
                                host.Transform = new Transform(
                                    new Vector3(137.0f, 60.0f, 0.0f), Quaternion.Identity, new Vector3(2.0f, 2.0f, 2.0f));

                                var there = SampleOnHost(host, region, owner, pointCount, out string secondError);
                                if (secondError != null)
                                    local.Add(secondError);

                                if (there == null || there.Count != here.Count)
                                {
                                    local.Add("the moved host produced a different point count, so the comparison is void");
                                }
                                else
                                {
                                    int shifted = 0;
                                    for (int i = 0; i < here.Count; i++)
                                    {
                                        var delta = there.Points[i].Position - here.Points[i].Position;
                                        if (Math.Abs(delta.X - 137.0f) < 1e-2f &&
                                            Math.Abs(delta.Y) < 1e-2f && Math.Abs(delta.Z) < 1e-2f)
                                        {
                                            shifted++;
                                        }
                                    }

                                    if (shifted != here.Count)
                                        local.Add($"only {shifted}/{here.Count} sampled points followed the host by the full 137 units");

                                    // 闭合实体上的点应当背向它的中心；一个从错误空间
                                    // 出来的法线会朝内或朝侧面。
                                    var centre = new Vector3(137.0f, 60.0f, 0.0f);
                                    int inward = 0;
                                    for (int i = 0; i < there.Count; i++)
                                    {
                                        var p = there.Points[i].Position;
                                        var outward = p - centre;
                                        if (outward.LengthSquared <= 1e-4f)
                                            continue;

                                        var up = Vector3.Transform(Vector3.Up, there.Points[i].Rotation);
                                        if (Vector3.Dot(up, outward) < 0.0f)
                                            inward++;
                                    }

                                    if (inward > there.Count / 10)
                                        local.Add($"{inward}/{there.Count} sampled points face into the host instead of away from it");

                                    if (local.Count == 0)
                                        Debug.Log($"[PCG] PASS mesh sampler: {there.Count} points, cloud follows the host, " +
                                                  $"{there.Count - inward} facing outward");
                                }
                            }

                            // 没有宿主也没有资产时，必须点名拒绝，而不是悄悄什么都不采。
                            var orphan = new PCG.Nodes.PCGMeshSamplerNode(new PCGNodeRegistry.PCGNodeSerializationData(
                                PCG.Nodes.PCGMeshSamplerNode.Id,
                                new object[] { Guid.Empty, "PCG_SelfTest_Missing_Host", pointCount, 0, 1.0f, 0, Guid.Empty },
                                7));

                            var orphanContext = new PCGExecutionContext
                            {
                                Seed = 3,
                                CurrentNodeId = 7,
                                GenerationBounds = region,
                                Source = owner,
                            };

                            var orphanData = orphan.OnExecute(orphanContext, Array.Empty<PCGData>());
                            if (orphanData.Count != 0)
                                local.Add($"an unresolvable target still generated {orphanData.Count} point(s)");
                            if (orphan.LastError == null || orphan.LastError.IndexOf("Missing_Host", StringComparison.Ordinal) < 0)
                                local.Add($"an unresolvable target was refused without naming it: {orphan.LastError ?? "no error"}");

                            var countless = new PCG.Nodes.PCGMeshSamplerNode(new PCGNodeRegistry.PCGNodeSerializationData(
                                PCG.Nodes.PCGMeshSamplerNode.Id,
                                new object[] { Guid.Empty, host.Name, 0, 0, 1.0f, 0, Guid.Empty },
                                7));
                            if (countless.OnExecute(orphanContext, Array.Empty<PCGData>()).Count != 0 ||
                                countless.LastError == null)
                            {
                                local.Add("a point count of zero should be refused with a reason");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                local.Add($"mesh sampler check threw {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (host != null)
                {
                    try
                    {
                        Actor.Destroy(host);
                    }
                    catch (Exception)
                    {
                        // 拆解不该决定结果。
                    }
                }

                if (owner != null)
                {
                    try
                    {
                        Actor.Destroy(owner);
                    }
                    catch (Exception)
                    {
                        // 拆解不该决定结果。
                    }
                }
            }

            foreach (var message in local)
            {
                failures.Add(message);
                Debug.LogError($"[PCG] FAIL mesh sampler: {message}");
            }

            return local.Count;
        }

        private static PCGData SampleOnHost(
            Actor host, BoundingBox region, Actor owner, int pointCount, out string error)
        {
            error = null;

            var world = new PCGWorldContext
            {
                // 没有解析器时节点找不到宿主 Actor，而那个失败看起来会像
                // 采样缺陷、而不是测试脚手架的缺口。用 PCGVolume 装的那同一个解析。
                ActorResolver = id => Level.FindActor(id),
            };

            var context = new PCGExecutionContext
            {
                Seed = 4242,
                CurrentNodeId = 6,
                GenerationBounds = region,
                Source = owner,
                World = world,
            };

            var node = new PCG.Nodes.PCGMeshSamplerNode(new PCGNodeRegistry.PCGNodeSerializationData(
                PCG.Nodes.PCGMeshSamplerNode.Id,
                new object[] { host.ID, string.Empty, pointCount, 0, 1.0f, 0, Guid.Empty },
                6));

            var data = node.OnExecute(context, Array.Empty<PCGData>());
            if (node.LastError != null)
                error = node.LastError;

            return data;
        }

        private static PCGVolume SpawnTestVolume(List<PCGVolume> created, string name)
        {
            var volume = new PCGVolume();
            if (!PCGActors.TrySpawn(volume, null))
                return null;

            volume.Name = name;
            created.Add(volume);
            return volume;
        }

        private static PCGNodeRegistry.PCGNodeSerializationData SpawnActorData(Guid prefabId, int maxActors) =>
            new PCGNodeRegistry.PCGNodeSerializationData(
                PCG.Nodes.PCGSpawnActorNode.Id,
                new object[] { prefabId, 0.0f, 1.0f, false, maxActors },
                12);

        /// <summary>这个拥有者之下、名字带插件生成前缀的那些 Actor。</summary>
        private static int CountGenerated(Actor owner)
        {
            var children = owner?.GetChildren<Actor>();
            if (children == null)
                return 0;

            int count = 0;
            for (int i = 0; i < children.Length; i++)
            {
                var child = children[i];
                if (child != null && !string.IsNullOrEmpty(child.Name) &&
                    child.Name.StartsWith(PCGSpawnNaming.GeneratedPrefix, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 建一个样条 Actor、采样它，并检查弧长上的分布。
        /// </summary>
        /// <remarks>
        /// 只有引擎里才有：节点从一个活着的 Actor 读结点长度，离屏 runner 做不到。
        /// 一条 1000 单位长的直线样条按 100 单位间距必须给出十一个点，沿曲线均匀分布——
        /// 这条检查抓的是"按结点参数采样"而不是"按弧长采样"。
        /// </remarks>
        private static int TestSplineSampler(List<string> failures)
        {
            var local = new List<string>();
            Spline spline = null;

            try
            {
                spline = new Spline();
                if (!PCGActors.TrySpawn(spline, null))
                {
                    local.Add("could not spawn a spline actor for the sampler test");
                }
                else
                {
                    spline.Name = "PCG_SelfTest_Spline";
                    spline.AddSplinePoint(new Vector3(-500.0f, 0.0f, 0.0f), true);
                    spline.AddSplinePoint(new Vector3(0.0f, 0.0f, 0.0f), true);
                    spline.AddSplinePoint(new Vector3(500.0f, 0.0f, 0.0f), true);

                    var world = new PCGWorldContext
                    {
                        ActorNameResolver = name => name == "PCG_SelfTest_Spline" ? spline : null,
                    };

                    var context = new PCGExecutionContext
                    {
                        Seed = 7,
                        CurrentNodeId = 3,
                        World = world,
                        GenerationBounds = new BoundingBox(new Vector3(-600, -10, -10), new Vector3(600, 10, 10)),
                    };

                    var values = new object[]
                    {
                        Guid.Empty,
                        "PCG_SelfTest_Spline",
                        PCG.Nodes.PCGSplineSampleMode.BySpacing,
                        100.0f,   // spacing
                        0,        // count, unused in spacing mode
                        0.0f,     // arc jitter
                        true,     // align to tangent
                        0.0f, 0.0f,
                        0.0f,     // height offset
                        1.0f,     // scale
                        0,        // seed offset
                    };

                    var node = new PCG.Nodes.PCGSplineSamplerNode(
                        new PCGNodeRegistry.PCGNodeSerializationData(PCG.Nodes.PCGSplineSamplerNode.Id, values, 3));

                    var data = node.OnExecute(context, Array.Empty<PCGData>());

                    if (node.LastError != null)
                        local.Add($"spline sampler reported an error: {node.LastError}");

                    if (data == null || data.Count != 11)
                    {
                        local.Add($"expected 11 points from a 1000-unit spline at 100-unit spacing, " +
                                  $"got {data?.Count ?? -1}");
                    }
                    else
                    {
                        float previousX = float.NegativeInfinity;
                        for (int i = 0; i < data.Count; i++)
                        {
                            var position = data.Points[i].Position;

                            if (position.X < -501.0f || position.X > 501.0f ||
                                Math.Abs(position.Y) > 1.0f || Math.Abs(position.Z) > 1.0f)
                            {
                                local.Add($"point {i} left the curve at {position}");
                                break;
                            }

                            if (position.X <= previousX)
                            {
                                local.Add($"points are not spread along the curve: {position.X} after {previousX}");
                                break;
                            }

                            previousX = position.X;
                        }

                        if (data.Count > 1)
                        {
                            float gap = data.Points[1].Position.X - data.Points[0].Position.X;
                            if (Math.Abs(gap - 100.0f) > 1.0f)
                                local.Add($"spacing came out as {gap:F2} world units instead of 100; count={data.Count}, " +
                                          $"first={data.Points[0].Position.X:F1}, " +
                                          $"second={data.Points[1].Position.X:F1}, " +
                                          $"last={data.Points[data.Count - 1].Position.X:F1}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                local.Add($"spline sampler threw {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (spline != null)
                    FlaxEngine.Object.DestroyNow(spline);
            }

            foreach (var message in local)
            {
                failures.Add(message);
                Debug.LogError($"[PCG] FAIL spline sampler: {message}");
            }

            if (local.Count == 0)
                Debug.Log("[PCG] PASS spline sampler spreads points by arc length");

            return local.Count;
        }

        /// <summary>用代码搭小图，并检查编译器的行为。</summary>
        private static int TestCompiler(List<string> failures, ref int failureCount)
        {
            int checks = 0;

            // 空图：必须编译得过，产出的是警告而不是错误。
            checks++;
            var empty = new PCGGraph();
            if (!PCGGraphCompiler.TryCompile(empty, out var emptyCompiled))
            {
                failureCount++;
                failures.Add("empty graph failed to compile");
                Debug.LogError("[PCG] FAIL empty graph should compile with a warning");
            }
            else
            {
                Debug.Log("[PCG] PASS empty graph compiles");
            }

            // 未知节点类型：必须报成错误，而不是抛异常。
            checks++;
            var badType = new PCGGraph(
                new[] { new Graph.PCGNode { TypeId = "NoSuchNodeType" } },
                Array.Empty<PCGEdge>());

            if (PCGGraphCompiler.TryCompile(badType, out var badCompiled) || badCompiled == null)
            {
                failureCount++;
                failures.Add("unknown node type was not rejected");
                Debug.LogError("[PCG] FAIL unknown node type should be rejected");
            }
            else
            {
                Debug.Log("[PCG] PASS unknown node type rejected with a diagnostic");
            }

            // 环：必须在编译期被抓到，而不是运行期把栈撑爆。
            checks++;
            var cyclic = new PCGGraph(
                new[]
                {
                    new Graph.PCGNode { TypeId = PCGDensityFilterNode.Id, Values = new object[8] },
                    new Graph.PCGNode { TypeId = PCGDensityFilterNode.Id, Values = new object[8] },
                },
                new[]
                {
                    new PCGEdge { OutputNodeIndex = 0, OutputPinIndex = 0, InputNodeIndex = 1, InputPinIndex = 0 },
                    new PCGEdge { OutputNodeIndex = 1, OutputPinIndex = 0, InputNodeIndex = 0, InputPinIndex = 0 },
                });

            bool rejected = !PCGGraphCompiler.TryCompile(cyclic, out var cyclicCompiled);
            bool cycleReported = false;
            if (cyclicCompiled?.Diagnostics != null)
            {
                for (int i = 0; i < cyclicCompiled.Diagnostics.Count; i++)
                {
                    if (cyclicCompiled.Diagnostics[i].Message != null &&
                        cyclicCompiled.Diagnostics[i].Message.Contains("cycle"))
                    {
                        cycleReported = true;
                        break;
                    }
                }
            }

            if (!rejected || !cycleReported)
            {
                failureCount++;
                failures.Add("a two-node cycle was not detected at compile time");
                Debug.LogError("[PCG] FAIL cycle detection did not report a cycle");
            }
            else
            {
                Debug.Log("[PCG] PASS cycle detected at compile time");
            }

            // 缺少必需输入：必须是编译错误，而不是运行期的空引用解引用。
            checks++;
            var missingInput = new PCGGraph(
                new[] { new Graph.PCGNode { TypeId = PCGDensityFilterNode.Id, Values = new object[8] } },
                Array.Empty<PCGEdge>());

            if (PCGGraphCompiler.TryCompile(missingInput, out var missingCompiled) || missingCompiled == null)
            {
                failureCount++;
                failures.Add("unconnected required input was not reported");
                Debug.LogError("[PCG] FAIL unconnected required input should be a compile error");
            }
            else
            {
                Debug.Log("[PCG] PASS unconnected required input reported");
            }

            // 一张没有地形的合法图执行：采样器什么都不产出，而这次运行
            // 不抛地完成——那正是缺少场景设置时该有的行为。
            checks++;
            var executable = new PCGGraph(
                new[]
                {
                    new Graph.PCGNode
                    {
                        TypeId = PCGLandscapeSamplerNode.Id,
                        Values = BuildSamplerValues(64),
                    },
                    new Graph.PCGNode
                    {
                        TypeId = PCGDensityFilterNode.Id,
                        Values = new object[] { 0.0f, 1.0f, false },
                    },
                },
                new[]
                {
                    new PCGEdge { OutputNodeIndex = 0, OutputPinIndex = 0, InputNodeIndex = 1, InputPinIndex = 0 },
                });

            if (!PCGGraphCompiler.TryCompile(executable, out var compiled) || compiled == null)
            {
                failureCount++;
                failures.Add("a valid two-node graph failed to compile");
                Debug.LogError("[PCG] FAIL valid graph failed to compile");
            }
            else
            {
                var context = new PCGExecutionContext
                {
                    Seed = 12345,
                    GenerationBounds = new BoundingBox(new Vector3(-100.0f), new Vector3(100.0f)),
                    World = new PCGWorldContext(),
                };

                try
                {
                    var executor = new PCGGraphExecutor(compiled);
                    var run = executor.Execute(context);

                    if (run.Cancelled)
                    {
                        failureCount++;
                        failures.Add("graph run reported cancellation without a token being set");
                        Debug.LogError("[PCG] FAIL valid graph run was cancelled unexpectedly");
                    }
                    else
                    {
                        Debug.Log($"[PCG] PASS valid graph executed: {run.Outputs.Length} output(s), " +
                                  $"{run.TotalPointsProduced} point(s), {run.Milliseconds:F2} ms" +
                                  (run.Outputs.Length > 0 ? $", primary={run.Outputs[0].Count} point(s)" : string.Empty));
                    }
                }
                catch (Exception ex)
                {
                    failureCount++;
                    failures.Add($"executing a valid graph threw {ex.GetType().Name}: {ex.Message}");
                    Debug.LogError($"[PCG] FAIL valid graph execution threw: {ex}");
                }
            }

            return checks;
        }

        private static object[] BuildSamplerValues(int pointCount)
        {
            // 顺序与 PCGLandscapeSamplerNode 的参数下标一致。
            return new object[]
            {
                pointCount,                                 // PointCount
                0.5f,                                       // Jitter
                0.0f,                                       // HeightOffset
                true,                                       // AlignToNormal
                PCGTerrainSampleMode.RayCast,               // SampleMode
                true,                                       // FallbackToRayCast
                1.0f,                                       // Scale
                90.0f,                                      // MaxSlopeDegrees
                0,                                          // SeedOffset
            };
        }

        /// <summary>
        /// 验证一份交给另一个拥有者的属性集不能经它写入。
        /// </summary>
        /// <remarks>
        /// <b>为什么这里不再演练 Share()/Detach()。</b><c>PCGAttributeSet.Share()</c> 返回
        /// <c>this</c>，而 <c>Detach()</c> 是靠把自己的列换成克隆来就地改的。这两个
        /// 语义无法组合：<c>Share()</c> 之后两个引用指向同一个对象，于是经其中任何一个写
        /// 都是写向唯一存在的那份存储。所以写时复制隔离在这里不是"有缺陷"，
        /// 而是**无法表达**——直到那九个列字典挪到一个
        /// 可共享的后备对象之后。
        ///
        /// 那次重构与 <c>MergePoints</c> / <c>SplitPoints</c> 一起落地，它们是第一批真正
        /// 需要共享存储的节点（design doc §3.2 注 3，Phase 2）。今天每一条生产路径都
        /// 走 <c>Clone()</c> 或 <c>Gather()</c>，而它们本来就深拷贝，所以这个测试断言的是
        /// 那条真正保护 Phase 1 产出的隔离性质。**别在那次重构落地之前
        /// 把它改成对 Share() 的检查。**
        /// </remarks>
        private static bool TestAttributeCopyOnWrite(out string error)
        {
            error = null;

            var source = new PCGAttributeSet(4);
            source.DeclareAttribute("Height", PCGAttributeType.Float);
            var column = source.GetWritableFloatArray("Height");
            for (int i = 0; i < 4; i++)
                column[i] = i * 10.0f;

            // Clone() 必须交回一份独占的深拷贝，而不是别名。
            var copy = source.Clone();
            copy.SetFloat("Height", 0, 999.0f);

            if (Mathf.Abs(source.GetFloat("Height", 0) - 0.0f) > 0.0001f)
            {
                error = $"writing a Clone() changed the source value to {source.GetFloat("Height", 0)}, expected 0";
                return false;
            }

            if (Mathf.Abs(copy.GetFloat("Height", 0) - 999.0f) > 0.0001f)
            {
                error = "the Clone() write did not take effect on the copy itself";
                return false;
            }

            // 没写过的行必须在这次复制里原样活下来。
            for (int i = 1; i < 4; i++)
            {
                if (Mathf.Abs(copy.GetFloat("Height", i) - source.GetFloat("Height", i)) > 0.0001f)
                {
                    error = $"row {i} diverged between a clone and its source";
                    return false;
                }
            }

            // 对同一个来源再 Clone() 一次，不能看到第一次的那些写入。
            var copy2 = source.Clone();
            if (Mathf.Abs(copy2.GetFloat("Height", 0) - 0.0f) > 0.0001f)
            {
                error = $"a later clone inherited an earlier clone's write ({copy2.GetFloat("Height", 0)})";
                return false;
            }

            // Clone() 必须交回我们拥有的存储，否则下一次写会撞上一个空转的屏障。
            if (copy.IsShared || copy2.IsShared)
            {
                error = "Clone() returned storage that is not solely owned";
                return false;
            }

            // 内建通道必须拒绝经这份属性集写入。
            var set = new PCGAttributeSet(1);
            try
            {
                set.SetFloat(PCGAttributeSet.DensityName, 0, 0.5f);
                error = "writing the built-in $Density channel through the attribute set was allowed";
                return false;
            }
            catch (PCGAttributeException ex) when (ex.Error == PCGAttributeError.BuiltInReadOnly)
            {
                // 预期之内。
            }

            return true;
        }

        /// <summary>
        /// 验证一个点单独重算出来的值与它在完整顺序运行里的值一致。
        /// </summary>
        private static bool TestDeterministicUnderReordering(out string error)
        {
            error = null;

            const int Count = 256;
            var forward = new Vector3[Count];
            for (int i = 0; i < Count; i++)
            {
                var rng = new PCGDeterministicRng(4242, 7, i);
                forward[i] = new Vector3(rng.NextRange(-1.0f, 1.0f), rng.NextRange(-1.0f, 1.0f), rng.NextRange(-1.0f, 1.0f));
            }

            // 倒序重算并比较。顺序发生器在这里会发散。
            for (int i = Count - 1; i >= 0; i--)
            {
                var rng = new PCGDeterministicRng(4242, 7, i);
                var again = new Vector3(rng.NextRange(-1.0f, 1.0f), rng.NextRange(-1.0f, 1.0f), rng.NextRange(-1.0f, 1.0f));

                if (again != forward[i])
                {
                    error = $"point {i} recomputed to {again} but the forward run produced {forward[i]}";
                    return false;
                }
            }

            return true;
        }

        /// <summary>验证参数的改动与拓扑的改动被哈希区分开了。</summary>
        private static bool TestStructuralHash(out string error)
        {
            error = null;

            var graph = new PCGGraph(
                new[] { new Graph.PCGNode { TypeId = PCGDensityFilterNode.Id, Values = new object[] { 0.0f, 1.0f, false } } },
                Array.Empty<PCGEdge>());

            int baseline = PCGGraphCompiler.ComputeStructuralHash(graph);

            // 同一拓扑、不同的值：哈希必须变，否则改过的图不会重编。
            var changedValue = new PCGGraph(
                new[] { new Graph.PCGNode { TypeId = PCGDensityFilterNode.Id, Values = new object[] { 0.25f, 1.0f, false } } },
                Array.Empty<PCGEdge>());

            int afterValueChange = PCGGraphCompiler.ComputeStructuralHash(changedValue);
            if (baseline == afterValueChange)
            {
                error = "a parameter change did not alter the structural hash";
                return false;
            }

            // 把一个参数暴露出去会把它从哈希里摘掉，所以两张只在
            // 暴露参数**取值**上不同的图必须哈希相同。那正是让一份已编译图能服务
            // 好几个带不同覆盖的体积、而不必逐体积重编的原因。
            //
            // 注意把一个参数从"未暴露"翻成"暴露"**确实会**改变哈希：
            // 被折叠的输入集合变了。断言跨越那次翻转仍然相等是在测错误的性质
            // （本检查的一个早期版本就是这么写的，然后在一个正确实现上失败了）。
            var exposedGraph = new PCGGraph(
                new[] { new Graph.PCGNode { TypeId = PCGDensityFilterNode.Id, Values = new object[] { 0.0f, 1.0f, false } } },
                Array.Empty<PCGEdge>());

            exposedGraph.ExposedParameters = new[]
            {
                new PCGParameterDefinition
                {
                    Name = "MinDensity",
                    Type = PCGParameterType.Float,
                    NodeIndex = 0,
                    ValueIndex = 0,
                },
            };

            int withExposed = PCGGraphCompiler.ComputeStructuralHash(exposedGraph);

            // 把一个参数暴露出去、再覆盖它的值，不能改变哈希：排除的
            // 全部意义就在于覆盖值不是结构性的。
            exposedGraph.Nodes[0].Values[0] = 0.75f;
            if (PCGGraphCompiler.ComputeStructuralHash(exposedGraph) != withExposed)
            {
                error = "an exposed parameter's value affected the structural hash";
                return false;
            }

            // 稳定性：同一张图哈希两次必须一致，否则复用永远不会触发。
            if (PCGGraphCompiler.ComputeStructuralHash(graph) != baseline)
            {
                error = "structural hash is not stable across calls";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 验证两条地形通路在标定之后结果一致，并报出残差。
        /// </summary>
        /// <remarks>
        /// 这条交叉校验正是让那条快速高度场通路可信的东西：在它于某块地形上
        /// 通过之前，标定都只是个猜测，而猜错会让每个生成物位移一个
        /// 常数倍。
        /// </remarks>
        /// <summary>
        /// 地形两条采样通路（高度场插值 / 射线）在**同一批点**上是否给同一个高度。
        /// </summary>
        /// <remarks>
        /// <paramref name="probesPerAxis"/> 是**显式参数**而不是读实例字段：这个方法要能被静态的
        /// 合成自检组在"地形上真的有一层起伏"的那段窗口里调到（见 <c>TestTerrainSynthesis</c>），
        /// 在平地上做这条检查等于没查。
        /// </remarks>
        private static bool TestTerrainPaths(Terrain terrain, int probesPerAxis, out string error)
        {
            error = null;

            var calibration = PCGTerrainProbe.Calibrate(terrain, probesPerAxis, log: true);

            if (!calibration.IsValid)
            {
                error = "calibration did not validate, so the heightfield path stays disabled on this terrain";
                return false;
            }

            var heightfield = new PCGTerrainSampler(terrain, calibration);

            // 射线那条路是地面真值，它不看标定。
            var truthSampler = new PCGTerrainSampler(terrain, PCGTerrainCalibration.Invalid);

            var box = terrain.Box;
            int probes = Mathf.Max(3, probesPerAxis);
            float worst = 0.0f;
            int compared = 0;
            float minTruth = float.MaxValue;
            float maxTruth = float.MinValue;

            for (int iz = 0; iz < probes; iz++)
            {
                for (int ix = 0; ix < probes; ix++)
                {
                    float tx = (ix + 0.5f) / probes;
                    float tz = (iz + 0.5f) / probes;

                    float x = Mathf.Lerp(box.Minimum.X, box.Maximum.X, tx);
                    float z = Mathf.Lerp(box.Minimum.Z, box.Maximum.Z, tz);

                    var truth = truthSampler.Sample(x, z, PCGTerrainSampleMode.RayCast);
                    if (!truth.Hit)
                        continue;

                    var fast = heightfield.Sample(x, z, PCGTerrainSampleMode.HeightfieldInterpolated, 20000.0f, false);
                    if (!fast.Hit)
                    {
                        error = $"heightfield path missed at ({x:F1}, {z:F1}) while the ray cast hit";
                        return false;
                    }

                    float delta = Mathf.Abs(fast.Height - truth.Height);
                    if (delta > worst)
                        worst = delta;

                    minTruth = Mathf.Min(minTruth, truth.Height);
                    maxTruth = Mathf.Max(maxTruth, truth.Height);

                    compared++;
                }
            }

            if (compared == 0)
            {
                error = "no probe point hit the terrain; is the terrain fully created?";
                return false;
            }

            if (worst > 0.5f)
            {
                error = $"heightfield and ray cast paths disagree by up to {worst:F3} world units over " +
                        $"{compared} point(s)";
                return false;
            }

            // **别让这条检查在平地上空转**（2026-10-11）：地形是全零平面时两条路都返回同一个数，
            // "一致"平凡成立 —— 设计文档附录 B.2 的"高度场 → 世界 Y 语义"盲区就是这么一直没被消掉的
            //（日志里那句 `raw heightmap is constant; treating terrain as flat` 就是它）。
            // 所以先要求"采到的地面真的起伏"：跨度太小就**报失败**，并把两种可能都写进理由。
            float spread = maxTruth - minTruth;

            if (spread < 1.0f)
            {
                error = $"采到的地面高度跨度只有 {spread:F3}（{compared} 个采样点）；两条路'一致'说明不了什么：" +
                        "要么这次没往地形里写起伏、要么**射线那条路没看到刚写下去的高度**（同一帧内几何没重建）。";
                return false;
            }

            Debug.Log($"[PCG] PASS terrain cross-check: 两条路在 {compared} 个点上相差不超过 {worst:F4} 世界单位、" +
                      $"地面跨度 {spread:F2}（不是平地，这条检查不是空转）; {calibration}");

            return true;
        }
    }
}
