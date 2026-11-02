using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading.Tasks;
using FlaxEditor;
using FlaxEditor.Content;
using FlaxEngine;
using HundunAgent.Core;

namespace HundunAgent.Tools
{
    /// <summary>
    /// PCG 工具集：把「设定描述」编译成图、在场景里建 PCG 体积、按体积生成/清空。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 为什么需要它：PCG 的三步（描述→图、建体积、生成）在编辑器里各有一个按钮，但按钮**只能人点**。
    /// 于是"让 AI 在场景里搭一份 PCG 演示"这件事卡在没有入口上——图文件可以由工具写，体积也可以由工具写，
    /// 唯独「生成」只有 <c>PCGVolume.Generate()</c> 一个入口，而它只被属性面板的按钮和流式驱动调用。
    /// 这组工具就是把这个入口暴露出来，顺带把前面两步也接上，好让整条链路一次跑完。
    /// </para>
    /// <para>
    /// <b>为什么按类型名找而不是直接引用。</b>直接写 <c>PCG.Runtime.PCGVolume</c> 更短也更快，
    /// 但那要在 <c>HundunAgentEditor.Build.cs</c> 里加一条跨插件依赖，而**编辑器会话里缓存的构建图不会重新
    /// 读它**：加完这句，编辑器下一次增量编译依然拿不到 <c>PCG.CSharp.dll</c>（实测 CS0246），
    /// 只有一次完整的 Flax.Build 才会生效——而那条路要重编整个游戏脚本并重载编辑器。
    /// 按类型名找把这件事降到零成本，代价是这层没有编译期检查：所以每个入口都先 <see cref="RequireType"/>
    /// 而不是静默地拿 null 往下走。
    /// </para>
    /// </remarks>
    public static class PCGTools
    {
        private const BindingFlags Statics = BindingFlags.Public | BindingFlags.Static;
        private const BindingFlags Members = BindingFlags.Public | BindingFlags.Instance;

        private const string SpecFileType = "PCG.Graph.PCGSpecFile";
        private const string GraphFileType = "PCG.Runtime.PCGGraphFile";
        private const string GraphType = "PCG.Graph.PCGGraph";
        private const string VolumeTypeName = "PCG.Runtime.PCGVolume";

        /// <summary>.flax 资产头里那 16 字节 id 的偏移（"CFWF" 魔数 + 版本 + 若干保留字段之后）。</summary>
        private const int AssetIdOffset = 0x1C;

        public static void Register()
        {
            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "pcg_spec_compile",
                Description = "把一份 PCG 设定描述（.pcgspec）编译成图文件（.json）。描述里的模型路径按资产库解析，" +
                              "工程外的模型（例如引擎自带的图元）回退到读资产头；输出省略时用描述里的图名。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{" +
                                  "\"spec\":{\"type\":\"string\",\"description\":\"描述文件的裸名或相对路径，裸名落在 Content/PCG/Specs\"}," +
                                  "\"output\":{\"type\":\"string\",\"description\":\"可选，输出图名（裸名落在 Content/PCG/Graphs）\"}}," +
                                  "\"required\":[\"spec\"]}",
                Execute = SpecCompileAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "pcg_volume_create",
                Description = "在场景里建一个 PCG 体积并挂上指定的图，设置种子、范围与分区/流式开关。可撤销。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{" +
                                  "\"name\":{\"type\":\"string\",\"description\":\"体积名\"}," +
                                  "\"graph\":{\"type\":\"string\",\"description\":\"图名或路径（裸名落在 Content/PCG/Graphs）\"}," +
                                  "\"seed\":{\"type\":\"integer\"}," +
                                  "\"position\":{\"type\":\"object\",\"description\":\"{x,y,z}\"}," +
                                  "\"volumeMin\":{\"type\":\"object\",\"description\":\"生成范围最小角 {x,y,z}，缺省 -500\"}," +
                                  "\"volumeMax\":{\"type\":\"object\",\"description\":\"生成范围最大角 {x,y,z}，缺省 +500\"}," +
                                  "\"parent\":{\"type\":\"string\",\"description\":\"父级 Actor 名称或 Guid，缺省场景根\"}," +
                                  "\"mode\":{\"type\":\"string\",\"description\":\"Bake / Runtime / Preview，缺省 Bake\"}," +
                                  "\"partitioned\":{\"type\":\"boolean\"}," +
                                  "\"cellSize\":{\"type\":\"number\",\"description\":\"分区格子边长\"}," +
                                  "\"hierarchical\":{\"type\":\"boolean\"}," +
                                  "\"hierarchicalLevels\":{\"type\":\"integer\"}," +
                                  "\"streaming\":{\"type\":\"boolean\"}," +
                                  "\"streamRadius\":{\"type\":\"number\"}," +
                                  "\"streamChunkSize\":{\"type\":\"number\",\"description\":\">0 时窗口按世界 chunk 对齐\"}}," +
                                  "\"required\":[\"name\",\"graph\"]}",
                Undoable = true,
                Execute = VolumeCreateAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "pcg_volume_list",
                Description = "列出场景里每个 PCG 体积的字段实参（图名、种子、生成范围、分区/流式开关）与产出 Actor 数。" +
                              "用来确认写进场景的东西真的被引擎读进来了，而不是只写进了文件。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{}}",
                Execute = VolumeListAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "pcg_volume_generate",
                Description = "对场景里的 PCG 体积跑一次生成（等价于属性面板的「生成」按钮），报每个体积的点数、" +
                              "Actor 数、耗时与告警。省略 volume 时对场景里所有体积跑一遍。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"volume\":{\"type\":\"string\",\"description\":\"可选，体积名或 Guid\"}}}",
                Execute = VolumeGenerateAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "pcg_volume_clear",
                Description = "销毁 PCG 体积生成出来的全部 Actor（手工摆的物件不受影响）。省略 volume 时清空所有体积。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"volume\":{\"type\":\"string\",\"description\":\"可选，体积名或 Guid\"}}}",
                Execute = VolumeClearAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "pcg_selftest",
                Description = "跑一遍 PCG 自检（引擎侧那几十组：生成/分区/采样/丛集/流式/多层/描述到场景/地形写入端）。" +
                              "自检本体是个 Script，本来只有把它挂到 Actor 上再进播放模式才会跑；这里直接建实例调 RunAll()，" +
                              "不需要播放模式。逐组 PASS/FAIL 落在编辑器日志里（用 log_scan 找 '[PCG]'）。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{" +
                                  "\"force\":{\"type\":\"boolean\",\"description\":\"缺省 true：先 ResetToRunAgain（自检带一次性守卫）\"}}}",
                Execute = SelfTestAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "pcg_pipeline",
                Description = "把「改了描述 → 场景里看到结果」压成一步：逐个编译描述（输出同名图）→ 生成体积" +
                              "（省略 volume 时场景里全部）→ 保存场景。编译有任何一个失败就停在那里、不去生成——" +
                              "拿旧图去生成会把「描述写错了」掩盖成「场景看着没问题」。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{" +
                                  "\"specs\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"描述名列表（裸名落在 Content/PCG/Specs）\"}," +
                                  "\"volume\":{\"type\":\"string\",\"description\":\"可选：只重建这一个体积；省略则场景里全部体积\"}}," +
                                  "\"required\":[\"specs\"]}",
                Execute = PipelineAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "pcg_terrain_restore",
                Description = "把 PCG 地形合成写进过的高度场还原成原样（依据是写入端留下的备份：Logs/PCG/TerrainBackup），" +
                              "返回还原了几格 patch。改动地形之前写入端会自动备份，这就是它的回退口。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{}}",
                Execute = TerrainRestoreAsync
            });
        }

        // ==================== 类型与成员的反射入口 ====================

        private static Type RequireType(string fullName)
        {
            var type = EditorUtils.FindType(fullName);

            if (type == null)
                throw new InvalidOperationException("找不到类型 " + fullName + "（PCG 插件没加载，或者脚本域正在重载）");

            return type;
        }

        /// <summary>
        /// 拿 PCG 体积的**当前**托管类型：场景里有现成体积就用它的类型，没有才按名字去找。
        /// </summary>
        /// <remarks>
        /// 按名字在"进程里所有程序集"中找，可能命中上一次脚本重载留下的旧程序集里的同名类型。
        /// 旧类型读字段没问题（字段布局一样），但**建 Actor 不行**：<c>Object.New&lt;T&gt;</c> 会把托管类型映到
        /// 原生类型，旧程序集的类型映不过去，静默返回空。场景里那个活着的体积一定是当前脚本域造的，
        /// 所以它的类型是唯一可靠的来源。
        /// </remarks>
        private static Type VolumeType()
        {
            foreach (var volume in Volumes())
                return volume.GetType();

            return RequireType(VolumeTypeName);
        }

        private static MethodInfo RequireMethod(Type type, string name, params Type[] signature)
        {
            var method = type.GetMethod(name, Statics, null, signature, null);

            if (method == null)
                throw new InvalidOperationException(type.Name + " 上找不到静态方法 " + name + "（签名对不上？）");

            return method;
        }

        /// <summary>找一个**实例**的无参方法（Generate / Clear 都是实例方法，静态那一版找不到它们）。</summary>
        private static MethodInfo RequireInstanceMethod(Type type, string name)
        {
            var method = type.GetMethod(name, Members, null, Type.EmptyTypes, null);

            if (method == null)
                throw new InvalidOperationException(type.Name + " 上找不到无参实例方法 " + name);

            return method;
        }

        /// <summary>
        /// 调用一个反射拿到的方法，并把被调方法的异常**原样**抛出来。
        /// </summary>
        /// <remarks>
        /// <c>MethodInfo.Invoke</c> 会把被调方法抛的一切（包括 Refused to compile 这类真正的错因）
        /// 包成 <see cref="TargetInvocationException"/>，而工具层只报 <c>Exception.Message</c>——
        /// 于是日志里只剩"目标调用抛出了异常"这句没有任何信息量的话。这里拆开它。
        /// </remarks>
        private static object Call(MethodInfo method, object instance, params object[] arguments)
        {
            try
            {
                return method.Invoke(instance, arguments);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw; // 上面那行一定会抛出去，这行只是让编译器满意
            }
        }

        /// <summary>读一个成员，属性优先、字段兜底。</summary>
        private static object Member(object target, string name)
        {
            var type = target.GetType();

            var property = type.GetProperty(name, Members);
            if (property != null)
                return property.GetValue(target);

            var field = type.GetField(name, Members);
            if (field != null)
                return field.GetValue(target);

            throw new InvalidOperationException(type.Name + " 上找不到成员 " + name);
        }

        /// <summary>写一个成员，字段优先（PCG 体积的可配置项大多是字段）。</summary>
        private static void SetMember(object target, string name, object value)
        {
            var type = target.GetType();

            var field = type.GetField(name, Members);
            if (field != null)
            {
                field.SetValue(target, value);
                return;
            }

            var property = type.GetProperty(name, Members);
            if (property != null && property.CanWrite)
            {
                property.SetValue(target, value);
                return;
            }

            throw new InvalidOperationException(type.Name + " 上找不到可写成员 " + name);
        }

        // ==================== 描述 → 图 ====================

        private static Task<object> SpecCompileAsync(JsonElement args)
        {
            string spec = EditorUtils.GetString(args, "spec");
            string output = EditorUtils.GetString(args, "output");

            if (string.IsNullOrWhiteSpace(spec))
                throw new ArgumentException("缺少 spec 参数");

            var specFile = PcgType(SpecFileType);
            var graphFile = PcgType(GraphFileType);
            var graphType = PcgType(GraphType);

            return MainThread.InvokeAsync<object>(() =>
            {
                var resolved = new Dictionary<string, string>();

                // 解析器签名是 Func<string, Guid>；这是框架类型，可以正常构造后从反射递过去。
                var tryLoad = RequireMethod(specFile, "TryLoad", typeof(string), typeof(Func<string, Guid>));
                var result = Call(tryLoad, null, spec, new Func<string, Guid>(path => ResolveModelId(path, resolved)));

                if (Member(result, "Ok") is not bool ok || !ok)
                    throw new ArgumentException("描述编译失败: " + Member(result, "Error"));

                object graph = Member(result, "Graph");

                // 缺省目标名取**描述文件的裸名**，不取图里的 Name：Name 是给人看的显示名（描述第一行，
                // 例如「演示·温带山谷」），拿它当文件名就会另开一份"同图不同文件"的资产——体积是按
                // 文件名找图的，于是这份成了没人引用的孤儿，而两份图的 Name 字段还互相打架。
                var specPath = (string)Call(RequireMethod(specFile, "ResolvePath", typeof(string)), null, spec);
                string target = string.IsNullOrWhiteSpace(output)
                    ? Path.GetFileNameWithoutExtension(specPath)
                    : output.Trim();

                // 图里的 Name 是给人看的（描述第一行），而体积是**按文件名**找图的。
                // 两个名字不一致时以文件名为准，免得日志与状态行里出现两个名字互相打架。
                SetMember(graph, "Name", Path.GetFileNameWithoutExtension(target));

                var trySave = RequireMethod(graphFile, "TrySave", typeof(string), graphType, typeof(string).MakeByRefType());
                object[] saveArgs = { target, graph, null };

                if (Call(trySave, null, saveArgs) is not bool saved || !saved)
                    throw new InvalidOperationException("写图失败: " + saveArgs[2]);

                var resolvePath = RequireMethod(graphFile, "ResolvePath", typeof(string));

                return new
                {
                    spec = specPath,
                    graph = Member(graph, "Name"),
                    nodes = Member(graph, "NodeCount"),
                    edges = Member(graph, "EdgeCount"),
                    path = Call(resolvePath, null, target),
                    models = resolved,
                    warnings = ((System.Collections.IEnumerable)Member(result, "Warnings")).Cast<object>().Select(w => w?.ToString()).ToArray(),
                };
            });
        }

        /// <summary>
        /// 描述里的资产路径 → 资产 id。先问资产库；工程外的模型（引擎自带的图元就是这种）资产库找不到，
        /// 回退到读 .flax 资产头里的那 16 字节 id。
        /// </summary>
        /// <remarks>
        /// 回退这一支是必需的：资产库只认工程 Content 下的东西，而"用一个引擎自带的立方体当占位模型"
        /// 是搭演示最省事的做法。解析不到时给空 id，让生成器在运行期如实报「没有指定模型」——
        /// 这里不猜、也不静默造一个假 id。
        /// </remarks>
        private static Guid ResolveModelId(string assetPath, Dictionary<string, string> report = null)
        {
            // 用下标赋值而不是 Add：同一份描述里同一个模型路径出现两次（例如路与水面都用 Plane）是常态，
            // 而 Add 遇到重复键会抛——那份描述会因此整份编译不出来，报的还是"键重复"这种与描述无关的话。
            void Note(string source)
            {
                if (report != null)
                    report[assetPath] = source;
            }

            if (string.IsNullOrWhiteSpace(assetPath))
                return Guid.Empty;

            try
            {
                if (Editor.Instance?.ContentDatabase?.Find(assetPath) is AssetItem item)
                {
                    Note("资产库");
                    return item.ID;
                }
            }
            catch (Exception)
            {
                // 资产库查询失败不该让整份描述编译不过，落到下面的文件回退即可。
            }

            foreach (var candidate in Candidates(assetPath))
            {
                // 资产库也要按"候选路径"再问一次：`.prefab` / 引擎资产在库里的登记形式与调用方
                // 顺手写下的相对路径并不一致（实测编辑器里按 "Content/…" 查不到、按绝对路径查得到），
                // 而 `.prefab` 是 **JSON** 资产，读资产头那条回退对它根本不适用。
                try
                {
                    if (Editor.Instance?.ContentDatabase?.Find(candidate) is AssetItem found)
                    {
                        Note("资产库 " + candidate);
                        return found.ID;
                    }
                }
                catch (Exception)
                {
                    // 同上，继续往下试。
                }

                var id = ReadAssetIdFromHeader(candidate);
                if (id != Guid.Empty)
                {
                    Note("资产头 " + candidate);
                    return id;
                }
            }

            Note("解析不到");
            return Guid.Empty;
        }

        /// <summary>一个资产路径可能落在哪儿：原样、工程相对、工作区相对。</summary>
        private static IEnumerable<string> Candidates(string assetPath)
        {
            if (Path.IsPathRooted(assetPath))
            {
                yield return assetPath;
                yield break;
            }

            yield return Path.Combine(EditorUtils.ProjectRoot, assetPath);
            yield return Path.Combine(EditorUtils.WorkspaceRoot, assetPath);
        }

        /// <summary>读资产头里的 id；不是 .flax 或读不满就返回空。</summary>
        private static Guid ReadAssetIdFromHeader(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return Guid.Empty;

                using var stream = File.OpenRead(path);
                var head = new byte[AssetIdOffset + 16];

                if (stream.Read(head, 0, head.Length) < head.Length)
                    return Guid.Empty;

                if (head[0] != (byte)'C' || head[1] != (byte)'F' || head[2] != (byte)'W' || head[3] != (byte)'F')
                    return Guid.Empty;

                // 资产头里的 id 是四个**小端** u32，而场景文件里那串 32 位十六进制就是它们依次拼起来。
                // 造一个 Guid 再按同样的形式拼字符串，得到的值与引擎侧解析出来的那个是同一个。
                string text = string.Concat(
                    BitConverter.ToUInt32(head, AssetIdOffset).ToString("x8"),
                    BitConverter.ToUInt32(head, AssetIdOffset + 4).ToString("x8"),
                    BitConverter.ToUInt32(head, AssetIdOffset + 8).ToString("x8"),
                    BitConverter.ToUInt32(head, AssetIdOffset + 12).ToString("x8"));

                return Guid.TryParse(text, out var id) ? id : Guid.Empty;
            }
            catch (Exception)
            {
                return Guid.Empty;
            }
        }

        // ==================== 建体积 ====================

        private static Task<object> VolumeCreateAsync(JsonElement args)
        {
            string name = EditorUtils.GetString(args, "name");
            string graph = EditorUtils.GetString(args, "graph");
            string parentRef = EditorUtils.GetString(args, "parent");

            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("缺少 name 参数");
            if (string.IsNullOrWhiteSpace(graph))
                throw new ArgumentException("缺少 graph 参数");

            var volumeType = VolumeType();
            var graphFile = PcgType(GraphFileType);

            return MainThread.InvokeAsync<object>(() =>
            {
                var editor = Editor.Instance;

                // Actor.New<T>() 是引擎建 Actor 的正门（它负责把托管对象与原生对象配上）。泛型约束在
                // MakeGenericMethod 这里就会被检查，所以给错类型是当场报错，不是运行时空引用。
                //
                // 两个坑：这个方法声明在基类 FlaxEngine.Object 上，而**反射拿继承来的静态成员必须带
                // FlattenHierarchy**（少了它就只在 Actor 自己声明的方法里找，于是"找不到"）。
                var newActor = typeof(FlaxEngine.Object)
                    .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                    .FirstOrDefault(m => m.Name == "New" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);

                if (newActor == null)
                    throw new InvalidOperationException("找不到 Actor.New<T>()（引擎版本变了？）");

                var volume = Call(newActor.MakeGenericMethod(volumeType), null);
                if (volume == null)
                    throw new InvalidOperationException("Actor.New<" + volumeType.Name + ">() 返回了空");

                SetMember(volume, "Name", name);
                SetMember(volume, "GraphPath", graph);
                SetMember(volume, "Seed", EditorUtils.GetInt(args, "seed", 0));

                // 图还没编译出来时先放它过去：体积是惰性的（没图就什么都不做），
                // 而"先摆好体积、再补图"是正常顺序。这里只报一句，不拦。
                bool graphFound = (bool)Call(RequireMethod(graphFile, "Exists", typeof(string)), null, graph);

                string mode = EditorUtils.GetString(args, "mode", "Bake");
                var modeField = volumeType.GetField("Mode", Members);
                if (modeField != null && Enum.TryParse(modeField.FieldType, mode, true, out var parsedMode))
                    modeField.SetValue(volume, parsedMode);

                if (args.TryGetProperty("position", out var position))
                    SetMember(volume, "Position", EditorUtils.GetVector3(position));

                bool hasMin = args.TryGetProperty("volumeMin", out var min);
                bool hasMax = args.TryGetProperty("volumeMax", out var max);

                if (hasMin || hasMax)
                {
                    SetMember(volume, "Volume", new BoundingBox(
                        EditorUtils.GetVector3(min, new Vector3(-500.0f)),
                        EditorUtils.GetVector3(max, new Vector3(500.0f))));
                }

                SetMember(volume, "Partitioned", EditorUtils.GetBool(args, "partitioned", false));
                SetMember(volume, "Hierarchical", EditorUtils.GetBool(args, "hierarchical", false));
                SetMember(volume, "Streaming", EditorUtils.GetBool(args, "streaming", false));

                if (args.TryGetProperty("cellSize", out _))
                    SetMember(volume, "PartitionCellSize", EditorUtils.GetNum(args, "cellSize", 500.0f));
                if (args.TryGetProperty("hierarchicalLevels", out _))
                    SetMember(volume, "HierarchicalLevels", EditorUtils.GetInt(args, "hierarchicalLevels", 2));
                if (args.TryGetProperty("streamRadius", out _))
                    SetMember(volume, "StreamRadius", EditorUtils.GetNum(args, "streamRadius", 1000.0f));
                if (args.TryGetProperty("streamChunkSize", out _))
                    SetMember(volume, "StreamChunkSize", EditorUtils.GetNum(args, "streamChunkSize", 0.0f));

                Actor parent = string.IsNullOrEmpty(parentRef)
                    ? EditorUtils.RequireScene()
                    : EditorUtils.FindActor(parentRef);

                if (parent == null)
                    throw new ArgumentException("父级 Actor 不存在: " + parentRef);

                // 走编辑器自己的 Spawn：撤销记录、场景树刷新都由它负责，与手工在场景树里新建一个 Actor 同路。
                editor.SceneEditing.Spawn((Actor)volume, parent, -1, false);

                ToolRegistry.OnUndoableAction();

                return new
                {
                    id = ((Actor)volume).ID.ToString(),
                    name,
                    graph,
                    graphFound,
                    graphPath = Call(RequireMethod(graphFile, "ResolvePath", typeof(string)), null, graph),
                    parent = parent.Name,
                    position = Describe((Vector3)Member(volume, "Position")),
                    volume = new
                    {
                        min = Describe(((BoundingBox)Member(volume, "Volume")).Minimum),
                        max = Describe(((BoundingBox)Member(volume, "Volume")).Maximum),
                    },
                    mode = Member(volume, "Mode").ToString(),
                    partitioned = Member(volume, "Partitioned"),
                    streaming = Member(volume, "Streaming"),
                };
            });
        }

        // ==================== 列出体积 ====================

        private static Task<object> VolumeListAsync(JsonElement args)
        {
            return MainThread.InvokeAsync<object>(() =>
            {
                var volumes = Volumes();
                var items = volumes.Select(DescribeVolume).ToArray();

                return new { count = volumes.Count, volumes = items };
            });
        }

        private static object DescribeVolume(Actor volume)
        {
            var box = (BoundingBox)Member(volume, "Volume");

            return new
            {
                id = volume.ID.ToString(),
                name = volume.Name,
                graph = Member(volume, "GraphPath"),
                seed = Member(volume, "Seed"),
                mode = Member(volume, "Mode").ToString(),
                boxMin = Describe(box.Minimum),
                boxMax = Describe(box.Maximum),
                partitioned = Member(volume, "Partitioned"),
                cellSize = Member(volume, "PartitionCellSize"),
                hierarchical = Member(volume, "Hierarchical"),
                streaming = Member(volume, "Streaming"),
                generatedActors = Member(volume, "GeneratedActorCount"),
                lastPoints = (Member(volume, "LastResult") is object last) ? Member(last, "PointCount") : null,
            };
        }

        private static object Describe(Vector3 value) => new { x = value.X, y = value.Y, z = value.Z };

        // ==================== 生成 / 清空 ====================

        private static Task<object> VolumeGenerateAsync(JsonElement args)
        {
            return MainThread.InvokeAsync<object>(() =>
            {
                var volumes = Targets(args);
                var generate = RequireInstanceMethod(volumes[0].GetType(), "Generate");
                var results = new List<object>();
                int points = 0, actors = 0, warnings = 0;

                foreach (var volume in volumes)
                {
                    Call(generate, volume);

                    // 生成物是走运行期接口建出来的，编辑器不会因此把场景标脏；不标这一下，
                    // 随后的 scene_save 会认为"没有改动"而跳过写入，产出全丢。
                    if (volume.Scene != null)
                        Editor.Instance?.Scene?.MarkSceneEdited(volume.Scene);

                    object result = Member(volume, "LastResult");
                    int volumePoints = result == null ? 0 : (int)Member(result, "PointCount");
                    int volumeActors = result == null ? 0 : Count(Member(result, "SpawnedActors"));
                    string[] volumeWarnings = result == null ? Array.Empty<string>() : (string[])Member(result, "Warnings");

                    points += volumePoints;
                    actors += volumeActors;
                    warnings += volumeWarnings.Length;

                    results.Add(new
                    {
                        name = volume.Name,
                        graph = Member(volume, "GraphPath"),
                        points = volumePoints,
                        actors = volumeActors,
                        warnings = volumeWarnings.Length,
                        milliseconds = result == null ? 0.0 : (double)Member(result, "Milliseconds"),
                        firstWarnings = volumeWarnings.Take(3).ToArray(),

                        // 格子级读数：分区体积上"这次为什么少了一半东西"只能靠它们回答（磁盘日志的落盘还会滞后）。
                        cells = new
                        {
                            active = Member(volume, "LastActiveCells"),
                            executed = Member(volume, "LastExecutedCells"),
                            reused = Member(volume, "LastReusedCells"),
                            deferred = Member(volume, "LastDeferredCells"),
                        },
                    });
                }

                return new
                {
                    generated = volumes.Count,
                    totalPoints = points,
                    totalActors = actors,
                    totalWarnings = warnings,
                    volumes = results,
                };
            });
        }

        private static Task<object> VolumeClearAsync(JsonElement args)
        {
            return MainThread.InvokeAsync<object>(() =>
            {
                var volumes = Targets(args);
                var clear = RequireInstanceMethod(volumes[0].GetType(), "Clear");
                int cleared = 0;

                foreach (var volume in volumes)
                    cleared += (int)Call(clear, volume);

                return new { clearedVolumes = volumes.Count, clearedActors = cleared };
            });
        }

        /// <summary>要处理的体积：给了名字就那一个（找不到就报错），没给就是场景里所有体积。</summary>
        private static List<Actor> Targets(JsonElement args)
        {
            string reference = EditorUtils.GetString(args, "volume");

            if (!string.IsNullOrEmpty(reference))
            {
                var actor = EditorUtils.FindActor(reference);

                if (IsVolume(actor))
                    return new List<Actor> { actor };

                throw new ArgumentException(actor == null
                    ? "Actor 不存在: " + reference
                    : actor.Name + " 不是 PCG 体积（类型是 " + actor.GetType().Name + "）");
            }

            var volumes = Volumes();

            if (volumes.Count == 0)
                throw new InvalidOperationException("场景里没有 PCG 体积。先用 pcg_volume_create 建一个。");

            return volumes;
        }

        /// <summary>场景里所有 PCG 体积。按类型名认，而不是按类型对象——重载脚本域之后旧类型对象会失效。</summary>
        private static List<Actor> Volumes()
        {
            var volumes = new List<Actor>();

            foreach (var scene in Level.Scenes)
            {
                if (scene != null)
                    CollectVolumes(scene, volumes);
            }

            return volumes;
        }

        private static void CollectVolumes(Actor root, List<Actor> output)
        {
            foreach (var child in root.Children)
            {
                if (IsVolume(child))
                    output.Add(child);
                else
                    CollectVolumes(child, output);
            }
        }

        private static bool IsVolume(Actor actor)
        {
            return actor != null && string.Equals(actor.GetType().FullName, VolumeTypeName, StringComparison.Ordinal);
        }

        /// <summary>数一个集合的条数；不是集合就当零，别在这里抛。</summary>
        private static int Count(object collection)
        {
            return collection is System.Collections.ICollection list ? list.Count : 0;
        }

        // ==================== 引擎自检 ====================

        /// <summary>
        /// 按名字拿 PCG 类型，但**优先从场景里活着的体积所属的那个程序集**里取。
        /// </summary>
        /// <remarks>
        /// 脚本域换过之后，`AppDomain.GetAssemblies()` 里可能同时躺着旧、新两份 PCG 程序集，
        /// 而按名字遍历拿到的很可能是**旧**的那一份——于是"改了代码却没反应"。实测栽过：
        /// 描述编译器加了落地投影，引擎里编译同一份描述仍返回旧的节点数（17/9 而不是 21/13），
        /// 一度被当成"跨插件模块的重载没生效"。活着的体积一定属于当前脚本域，从它那里取类型最可靠。
        /// </remarks>
        private static Type PcgType(string fullName)
        {
            var volumes = Volumes();

            if (volumes.Count > 0)
            {
                var live = volumes[0].GetType().Assembly.GetType(fullName);

                if (live != null)
                    return live;
            }

            return RequireType(fullName);
        }

        /// <summary>
        /// 直接驱动 <c>PCG.Tests.PCGSelfTest</c>。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 自检本体是一个 <c>Script</c>：它只有在被挂到某个 Actor 上、并且进了播放模式（<c>OnStart</c>）时才会跑。
        /// 而 <c>RunAll()</c> 只碰插件自己的代码与引擎全局量（临时目录、场景、生成物件），**不读它自己的 Actor**，
        /// 所以一个没挂到任何 Actor 上的实例就能跑完——这就是"不进播放模式也拿得到引擎侧证据"的关键，
        /// 也是这份仓库里几十组引擎侧断言此前一直没被跑过的原因。
        /// </para>
        /// <para>
        /// 按名字找类型、按名字调方法，与这个文件其余部分同一个理由：HundunAgentEditor 不在 PCG 的构建图里，
        /// 加一条编译期依赖要等一次完整的 Flax.Build 才生效。
        /// </para>
        /// </remarks>
        private static Task<object> SelfTestAsync(JsonElement args)
        {
            bool force = EditorUtils.GetBool(args, "force", true);

            return MainThread.InvokeAsync<object>(() =>
            {
                // 类型必须来自**当前脚本域**。脚本域重载之后 `GetAssemblies()` 里可能还留着旧程序集，
                // 而按名字找到的第一个往往就是它——旧类型在 `Object.New(type)` 那里映不回原生对象，
                // `Actor.AddScript` 内部拿到 null 再执行 `script.Parent = this`，抛出来的是
                // 一句没有任何信息的 NullReference。场景里活着的 PCG 体积一定属于当前脚本域，
                // 所以优先从**它的程序集**里取同名类型。
                var volumes = Volumes();

                Type selfTestType = volumes.Count > 0
                    ? volumes[0].GetType().Assembly.GetType("PCG.Runtime.PCGSelfTest")
                    : null;

                if (selfTestType == null)
                    selfTestType = EditorUtils.FindType("PCG.Runtime.PCGSelfTest") ??
                                   EditorUtils.FindType("PCG.Tests.PCGSelfTest");

                if (selfTestType == null)
                    throw new InvalidOperationException("找不到 PCGSelfTest 类型（PCG 插件没加载？）");

                // Script 必须有宿主：它的构造走的是引擎那条"托管对象配原生对象"的路，用 Activator 直接
                // new 会抛。给它一个临时空 Actor 当宿主，跑完连宿主一起销毁——自检自己建的东西它自己会清。
                var host = new EmptyActor { Name = "PCG_SelfTest_Host" };
                Editor.Instance.SceneEditing.Spawn(host, EditorUtils.RequireScene(), -1, false);

                var script = host.AddScript(selfTestType);

                if (script == null)
                    throw new InvalidOperationException("挂不上 PCGSelfTest 脚本（AddScript 返回空）");

                try
                {
                    // 三条放置检查（植被/样条网格/网格采样）共用 FoliageModelPath。它的默认值是一条**引擎自带图元**
                    // 的绝对路径，而生成器按 Guid 取资产，所以这里先把路径解析成 Guid 再填进去——
                    // 不做这一步，那三项会因为取不到资产而静默跳过（"跳过的检查"在结果里和"跑绿了"长得一样）。
                    // 解析不出来就保持原样，自检会把跳过理由写进 skipped。
                    // 放置检查用的夹具资产（模型 / Prefab / 贴花材质）：字段里写的是**路径**，
                    // 而生成器按 Guid 取资产，所以这里把路径解析成 Guid 再填进去。
                    // 不做这一步，那几项会因为取不到资产而静默跳过——而"跳过的检查"在结果里和"跑绿了"长得一样，
                    // 所以同时把理由留在 skipped 里（自检自己负责那一半）。
                    // 解析不出来就保持原样，让自检把跳过理由写出来，不在这里编一个假 id。
                    foreach (var fixtureField in new[] { "FoliageModelPath", "ActorPrefabPath", "DecalMaterialPath" })
                    {
                        var field = selfTestType.GetField(fixtureField, Members);

                        if (field == null || field.GetValue(script) is not string spec ||
                            string.IsNullOrWhiteSpace(spec) || Guid.TryParse(spec, out _))
                        {
                            continue;
                        }

                        var fixtureId = ResolveModelId(spec);

                        if (fixtureId != Guid.Empty)
                            field.SetValue(script, fixtureId.ToString());
                    }

                    if (force)
                        Call(RequireInstanceMethod(selfTestType, "ResetToRunAgain"), script);

                    int failures = (int)Call(RequireInstanceMethod(selfTestType, "RunAll"), script);

                    return new
                    {
                        failures,
                        checks = Member(script, "LastCheckCount"),
                        lastFailures = Member(script, "LastFailureCount"),
                        reasons = Member(script, "LastFailures"),
                        skipped = Member(script, "LastSkipped"),
                        hint = failures == 0
                            ? "全绿。逐组的 [PCG] PASS 行在编辑器日志里（log_scan pattern '[PCG]'）；skipped 里是这一轮没跑的检查。"
                            : "有失败项：reasons 里就是逐条理由；skipped 里是这一轮没跑的检查。",
                    };
                }
                finally
                {
                    Actor.Destroy(host);
                }
            });
        }

        /// <summary>
        /// 「改了描述 → 场景里看到结果」压成一步：编译描述 → 生成体积 → 保存场景。
        /// </summary>
        /// <remarks>
        /// 它不重写任何一条链路，而是**按名字调用已经注册好的那几个工具**——于是"编译用哪个资产解析器、
        /// 生成怎么记账、保存怎么标脏"始终只有一处实现。编译一旦有失败就停在这里：
        /// 拿旧图去生成会把「描述写错了」掩盖成「场景看着没问题」，那正是最难发现的一类假绿。
        /// </remarks>
        private static async Task<object> PipelineAsync(JsonElement args)
        {
            var specs = EditorUtils.GetStringArray(args, "specs");
            var volume = EditorUtils.GetString(args, "volume");

            if (specs.Count == 0)
                throw new ArgumentException("缺少 specs：给一串描述名（裸名落在 Content/PCG/Specs），例如 [\"demo_valley\"]");

            var compiled = new List<object>();
            bool anyFailed = false;

            foreach (var spec in specs)
            {
                using var request = JsonDocument.Parse(
                    "{\"spec\":" + JsonSerializer.Serialize(spec) + ",\"output\":" + JsonSerializer.Serialize(spec) + "}");

                var result = await ToolRegistry.ExecuteAsync("pcg_spec_compile", request.RootElement);
                compiled.Add(result);

                if (result.TryGetValue("success", out var ok) && ok is bool okValue && !okValue)
                    anyFailed = true;
            }

            if (anyFailed)
                return new { status = "compile-failed", compiled };

            Dictionary<string, object> generated;
            Dictionary<string, object> saved;

            using (var request = JsonDocument.Parse(string.IsNullOrEmpty(volume)
                       ? "{}"
                       : "{\"volume\":" + JsonSerializer.Serialize(volume) + "}"))
            {
                generated = await ToolRegistry.ExecuteAsync("pcg_volume_generate", request.RootElement);
            }

            using (var request = JsonDocument.Parse("{}"))
            {
                saved = await ToolRegistry.ExecuteAsync("scene_save", request.RootElement);
            }

            return new { status = "rebuilt", compiled, generated, saved };
        }

        /// <summary>
        /// 用写入端留下的备份把地形还原回去。
        /// </summary>
        /// <remarks>
        /// 写入端改的是**场景里那张地形资产**（决定 C 的第二种落地：写 Main 那张），所以回退口是它的一部分。
        /// 备份一格 patch 一个文件、命名里带原地形 id，于是这里不需要额外参数：场景里有谁就还原谁，
        /// 名字对不上的文件自然跳过。
        /// </remarks>
        private static Task<object> TerrainRestoreAsync(JsonElement args)
        {
            var writerType = PcgType("PCG.Runtime.PCGTerrainWriter");

            return MainThread.InvokeAsync<object>(() =>
            {
                var scene = EditorUtils.RequireScene();

                var restore = RequireMethod(writerType, "TryRestore",
                    typeof(Actor), typeof(int).MakeByRefType(), typeof(string).MakeByRefType());

                object[] call = { scene, 0, null };
                bool ok = Call(restore, null, call) is bool value && value;

                return new
                {
                    ok,
                    restored = call[1],
                    error = call[2],
                    backupFolder = StaticMember(writerType, "BackupFolder"),
                };
            });
        }

        /// <summary>读一个**静态**成员（属性优先、字段兜底）；<see cref="Member"/> 那个是给实例用的。</summary>
        private static object StaticMember(Type type, string name)
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Static);

            if (property != null)
                return property.GetValue(null);

            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Static);

            return field?.GetValue(null);
        }
    }
}
