using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MetaHuman.Core
{
    /// <summary>
    /// 模板与脸部控制表的加载与缓存。
    /// 旧实现（CharacterAppearanceEditor）用相对路径 "Content/Presets/..." 读预设，
    /// 那是相对进程 CWD 解析的，编辑器里恰好能用、打包后静默回落默认值；
    /// 这里显式枚举候选根目录并在都找不到时抛错，不再无声吞掉。
    /// </summary>
    public static class MetaHumanTemplateStore
    {
        private static readonly Dictionary<string, MetaHumanTemplate> Cache = new Dictionary<string, MetaHumanTemplate>();
        private static MetaHumanFaceControlTable _faceControls;
        private static MetaHumanExpressionRig _expressionRig;
        private static MetaHumanGuiToRawMap _guiToRaw;
        private static MetaHumanMorphGuiMap _morphGui;
        private static MetaHumanArkitMap _arkitMap;
        private static string _resolvedRoot;

        public const string TemplatesRelativeDir = "Plugins/MetaHuman/Content/Templates";
        public const string ProjectRelativeDir = "Content/Templates/MetaHuman";

        public static event Action<string> OnLoadFailed;

        /// <summary>已解析出的模板根目录（可能为 null，表示尚未成功解析）。</summary>
        public static string RootDirectory
        {
            get
            {
                if (_resolvedRoot == null)
                {
                    _resolvedRoot = ResolveRoot();
                }

                return _resolvedRoot;
            }
        }

        private static string ResolveRoot()
        {
            List<string> candidates = new List<string>();

            // 1) 显式覆盖（编辑器工具与打包脚本可注入）
            if (!string.IsNullOrEmpty(OverrideRoot) && Directory.Exists(OverrideRoot))
            {
                candidates.Add(OverrideRoot);
            }

            // 2) 工程根（FlaxEngine.Globals.ProjectFolder，编辑器与 PIE 都可取）
            TryAdd(candidates, ProjectRoot, TemplatesRelativeDir);
            TryAdd(candidates, ProjectRoot, ProjectRelativeDir);

            // 4) 进程基目录（旧行为，兜底）
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            for (int up = 0; up < 5 && !string.IsNullOrEmpty(baseDir); up++)
            {
                TryAdd(candidates, baseDir, TemplatesRelativeDir);
                TryAdd(candidates, baseDir, ProjectRelativeDir);
                baseDir = Path.GetDirectoryName(baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }

            foreach (string candidate in candidates)
            {
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static void TryAdd(List<string> list, string root, string relative)
        {
            if (string.IsNullOrEmpty(root))
            {
                return;
            }

            string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!list.Contains(path))
            {
                list.Add(path);
            }
        }

        /// <summary>测试与编辑器工具注入用的覆盖根目录。</summary>
        public static string OverrideRoot { get; set; }

        /// <summary>工程根目录；Globals 在极端场景（纯服务端目标）可能不可用，退回空并让候选枚举继续。</summary>
        private static string ProjectRoot
        {
            get
            {
                try
                {
                    return FlaxEngine.Globals.ProjectFolder;
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <summary> cooked 构建下的模板载体：由 RawDataAsset 注入字节，避免依赖散文件。</summary>
        public static void RegisterRaw(string templateId, byte[] jsonBytes)
        {
            string json = System.Text.Encoding.UTF8.GetString(jsonBytes);
            MetaHumanTemplate template = MetaHumanTemplate.FromJson(json);
            Cache[template.TemplateId] = template;
        }

        /// <summary>回退到内嵌表这件事本身要能被看见：只有 Debug.LogWarning 的话，发布版没人会去翻。</summary>
        public static event Action<string> OnBakedFallbackUsed;

        private static int _bakedFallbackCount;

        public static int BakedFallbackCount
        {
            get { return _bakedFallbackCount; }
        }

        private static void NoteBakedFallback(string what)
        {
            _bakedFallbackCount++;
            FlaxEngine.Debug.LogWarning("MetaHuman 装载回退： " + what);
            Action<string> handler = OnBakedFallbackUsed;
            if (handler != null)
            {
                handler(what);
            }
        }

        /// <summary>编译进程序集的三份模板表（Tools/MetaHumanBakeTables.py 烘焙），cooked 构建下的兜底载体。</summary>
        public static bool TryGetBaked(string templateId, out MetaHumanTemplate template)
        {
            template = Baked(templateId);
            return template != null;
        }

        private static MetaHumanTemplate Baked(string templateId)
        {
            string json = null;
            if (templateId == "metahuman.face_archetype")
            {
                json = MetaHumanBakedTables.FaceTemplateJson;
            }
            else if (templateId == "metahuman.body_base")
            {
                json = MetaHumanBakedTables.BodyTemplateJson;
            }

            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            try
            {
                MetaHumanTemplate template = MetaHumanTemplate.FromJson(json);
                List<string> errors = template.Validate();
                if (errors.Count > 0)
                {
                    Fail("内嵌模板 " + templateId + " 校验失败：" + string.Join(" | ", errors.ToArray()));
                    return null;
                }

                return template;
            }
            catch (Exception exception)
            {
                Fail("内嵌模板 " + templateId + " 解析失败：" + exception.Message);
                return null;
            }
        }

        public static MetaHumanTemplate Get(string templateId)
        {
            MetaHumanTemplate template;
            if (Cache.TryGetValue(templateId, out template))
            {
                return template;
            }

            string root = RootDirectory;
            if (string.IsNullOrEmpty(root))
            {
                MetaHumanTemplate baked = Baked(templateId);
                if (baked != null)
                {
                    NoteBakedFallback("模板根目录未找到，改用内嵌表：" + templateId);
                    Cache[baked.TemplateId] = baked;
                    return baked;
                }

                Fail("模板根目录未找到（试过 " + TemplatesRelativeDir + " 与 " + ProjectRelativeDir + "），且没有 " + templateId + " 的内嵌表");
                return null;
            }

            string path = FindTemplateFile(root, templateId);
            if (path == null)
            {
                MetaHumanTemplate baked = Baked(templateId);
                if (baked != null)
                {
                    NoteBakedFallback("模板文件不存在：" + templateId + "（在 " + root + "），改用内嵌表");
                    Cache[baked.TemplateId] = baked;
                    return baked;
                }

                Fail("模板文件不存在：" + templateId + "（在 " + root + "）");
                return null;
            }

            MetaHumanTemplate loaded = MetaHumanTemplate.LoadFromFile(path);
            // 文件名与内容不一致时以内容为准并大声提醒：否则「改了文件没生效」会找不到原因
            if (loaded.TemplateId != templateId)
            {
                Fail("模板文件 " + Path.GetFileName(path) + " 里的 TemplateId 是 " + loaded.TemplateId + "，与请求的 " + templateId + " 不一致");
            }

            List<string> errors = loaded.Validate();
            if (errors.Count > 0)
            {
                Fail("模板 " + templateId + " 校验失败：" + string.Join(" | ", errors.ToArray()));
                return null;
            }

            Cache[templateId] = loaded;
            return loaded;
        }

        private static string FindTemplateFile(string root, string templateId)
        {
            if (!Directory.Exists(root))
            {
                return null;
            }

            // 先按 TemplateId 派生的文件名找，再全目录扫内容匹配
            string[] expected = {
                "Template_" + templateId + ".json",
                templateId + ".json",
            };

            foreach (string name in expected)
            {
                string direct = Path.Combine(root, name);
                if (File.Exists(direct))
                {
                    return direct;
                }
            }

            foreach (string file in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        MetaHumanTemplate probe = MetaHumanTemplate.FromJson(System.Text.Encoding.UTF8.GetString(ReadAll(fs)));
                        if (probe.TemplateId == templateId)
                        {
                            return file;
                        }
                    }
                }
                catch
                {
                    // 目录里可能混有别的 JSON；扫描继续，最终以「没找到」报错
                }
            }

            return null;
        }

        private static byte[] ReadAll(Stream stream)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }

        public static MetaHumanFaceControlTable FaceControls
        {
            get
            {
                if (_faceControls != null)
                {
                    return _faceControls;
                }

                string root = RootDirectory;
                if (string.IsNullOrEmpty(root))
                {
                    if (_faceControls == null && !string.IsNullOrEmpty(MetaHumanBakedTables.FaceControlsJson))
                    {
                        _faceControls = MetaHumanFaceControlTable.FromJson(MetaHumanBakedTables.FaceControlsJson);
                        NoteBakedFallback("模板根目录未找到，表情控制表改用内嵌那份");
                        return _faceControls;
                    }

                    return null;
                }

                string path = Path.Combine(root, "Face_Control_Table.json");
                if (!File.Exists(path))
                {
                    if (!string.IsNullOrEmpty(MetaHumanBakedTables.FaceControlsJson))
                    {
                        _faceControls = MetaHumanFaceControlTable.FromJson(MetaHumanBakedTables.FaceControlsJson);
                        NoteBakedFallback("表情控制表文件不存在（" + path + "），改用内嵌那份");
                        return _faceControls;
                    }

                    Fail("脸部控制表不存在：" + path);
                    return null;
                }

                _faceControls = MetaHumanFaceControlTable.FromJson(File.ReadAllText(path));
                return _faceControls;
            }
        }

        /// <summary>
        /// 表情骨骼表（控制值 → 眼睑骨绕眼球转多少度）。缺失时表情退回"只有 morph"，
        /// 而那个量级实测只有 0.1mm，等于看不见——所以这里失败要显式记诊断，不静默。
        /// </summary>
        public static MetaHumanExpressionRig ExpressionRig
        {
            get
            {
                if (_expressionRig != null)
                {
                    return _expressionRig;
                }

                string root = RootDirectory;
                string path = string.IsNullOrEmpty(root) ? null : Path.Combine(root, "Expression_Rig.json");
                if (path != null && File.Exists(path))
                {
                    _expressionRig = MetaHumanExpressionRig.FromJson(File.ReadAllText(path));
                    return _expressionRig;
                }

                if (!string.IsNullOrEmpty(MetaHumanBakedTables.ExpressionRigJson))
                {
                    _expressionRig = MetaHumanExpressionRig.FromJson(MetaHumanBakedTables.ExpressionRigJson);
                    NoteBakedFallback("表情骨骼表文件不存在（" + (path ?? "<无根目录>") + "），改用内嵌那份");
                    return _expressionRig;
                }

                Fail("表情骨骼表不存在：" + (path ?? "<无根目录>"));
                return null;
            }
        }

        /// <summary>
        /// UE 的 GUI→raw 全量换算表（174→251）。控制表里已经带上了每个控制的换算段，
        /// 这份全量表供「一次收到整套 GUI 控制」的外部驱动（LiveLink/追踪）与编辑器核对用。
        /// </summary>
        public static MetaHumanGuiToRawMap GuiToRaw
        {
            get
            {
                if (_guiToRaw != null)
                {
                    return _guiToRaw;
                }

                string root = RootDirectory;
                if (string.IsNullOrEmpty(root))
                {
                    if (!string.IsNullOrEmpty(MetaHumanBakedTables.GuiToRawJson))
                    {
                        _guiToRaw = MetaHumanGuiToRawMap.FromJson(MetaHumanBakedTables.GuiToRawJson);
                        NoteBakedFallback("模板根目录未找到，GUI→raw 换算表改用内嵌那份");
                        return _guiToRaw;
                    }

                    return null;
                }

                string path = Path.Combine(root, "Gui_To_Raw_Map.json");
                if (!File.Exists(path))
                {
                    if (!string.IsNullOrEmpty(MetaHumanBakedTables.GuiToRawJson))
                    {
                        _guiToRaw = MetaHumanGuiToRawMap.FromJson(MetaHumanBakedTables.GuiToRawJson);
                        NoteBakedFallback("GUI→raw 换算表文件不存在（" + path + "），改用内嵌那份");
                        return _guiToRaw;
                    }

                    Fail("GUI→raw 换算表不存在：" + path + "（跑 Tools/MetaHumanExtractGuiMap.py 生成）");
                    return null;
                }

                _guiToRaw = MetaHumanGuiToRawMap.LoadFromFile(path);
                return _guiToRaw;
            }
        }

        private static MetaHumanMorphGuiMap _ueMorphGui;

        /// <summary>
        /// 第二张 morph→GUI 表：**左岸是引擎里实读的 UE 网格 morph 名**（`head_lod0_mesh__brow_down_L`），
        /// 而 <see cref="MorphGui"/> 的左岸是 .dna 族名（`head_wm1_blink_L`）。两者同名命中实测 0/37，
        /// 是两套命名族，所以必须并存而不是一张表改来改去。
        /// 判据与 MorphGui 一致：装载即过 Validate，散文件不在就用内嵌那份并计数，两处都没有就大声报错。
        /// </summary>
        public static MetaHumanMorphGuiMap UeMorphGui
        {
            get
            {
                if (_ueMorphGui != null)
                {
                    return _ueMorphGui;
                }

                string root = RootDirectory;
                string path = string.IsNullOrEmpty(root) ? null : Path.Combine(root, "Ue_Morph_To_Gui_Map.json");
                if (path == null || !File.Exists(path))
                {
                    if (!string.IsNullOrEmpty(MetaHumanBakedTables.UeMorphGuiJson))
                    {
                        _ueMorphGui = MetaHumanMorphGuiMap.FromJson(MetaHumanBakedTables.UeMorphGuiJson);
                        NoteBakedFallback(path == null
                            ? "模板根目录未找到，UE 网格 morph→GUI 表改用内嵌那份"
                            : "UE 网格 morph→GUI 表文件不存在（" + path + "），改用内嵌那份");
                    }
                    else
                    {
                        Fail("UE 网格 morph→GUI 表不存在：" + (path ?? "<模板根目录未找到>")
                             + "，内嵌那份也没有（跑 Tools/MetaHumanBuildUeMorphMap.py 再用 Tools/MetaHumanBakeTables.py 烘焙）");
                        return null;
                    }
                }
                else
                {
                    _ueMorphGui = MetaHumanMorphGuiMap.FromJson(File.ReadAllText(path));
                }

                List<string> problems = _ueMorphGui.Validate(GuiToRaw);
                if (problems.Count > 0)
                {
                    for (int i = 0; i < problems.Count && i < 8; i++)
                    {
                        Fail("UE 网格 morph→GUI 表校验未过：" + problems[i]);
                    }
                }

                return _ueMorphGui;
            }
        }

        /// <summary>
        /// 第三命名族对照表：控制表里的 warp 形变器名（`head_wm1_blink_L`）→ 资产里真有的 blendshape 名。
        /// 两张 morph→GUI 表的左岸都不是这一族，所以控制驱动表情时那条路只能靠这张表走通。
        /// 判据与另两张一致：装载即过 Validate；散文件不在就用内嵌那份并计数；两处都没有就大声报错，
        /// 因为空表会让 29 条表情控制看起来"合法地不动"。
        /// </summary>
        public static MetaHumanWarpShapeMap WarpShapes
        {
            get
            {
                if (_warpShapes != null)
                {
                    return _warpShapes;
                }

                string root = RootDirectory;
                string path = string.IsNullOrEmpty(root) ? null : Path.Combine(root, "Warp_To_Shape_Map.json");
                if (path == null || !File.Exists(path))
                {
                    if (!string.IsNullOrEmpty(MetaHumanBakedTables.WarpShapeJson))
                    {
                        _warpShapes = MetaHumanWarpShapeMap.FromJson(MetaHumanBakedTables.WarpShapeJson);
                        NoteBakedFallback(path == null
                            ? "模板根目录未找到，warp→形状对照表改用内嵌那份"
                            : "warp→形状对照表文件不存在（" + path + "），改用内嵌那份");
                    }
                    else
                    {
                        Fail("warp→形状对照表不存在：" + (path ?? "<模板根目录未找到>")
                             + "，内嵌那份也没有（跑 Tools/MhWarpShapeMapGen.py 再用 Tools/MetaHumanBakeTables.py 烘焙）");
                        return null;
                    }
                }
                else
                {
                    _warpShapes = MetaHumanWarpShapeMap.FromJson(File.ReadAllText(path));
                }

                List<string> problems = _warpShapes.Validate();
                if (problems.Count > 0)
                {
                    for (int i = 0; i < problems.Count && i < 8; i++)
                    {
                        Fail("warp→形状对照表校验未过：" + problems[i]);
                    }
                }

                return _warpShapes;
            }
        }

        private static MetaHumanWarpShapeMap _warpShapes;

        /// <summary>
        /// morph→GUI 对照表（头部 blendshape 那一侧的名字桥）。
        /// </summary>
        /// <remarks>
        /// 装载时就过一遍 <see cref="MetaHumanMorphGuiMap.Validate"/>：这张表被人手改坏
        /// （写了个不存在的 GUI 名、侧标写反）如果不在这里报出来，运行期就只剩"某根滑杆推不动脸"
        /// 这种没有信息量的表现。表不在盘上时报错而不是静默返回空表——空表会让所有 morph 看起来"合法地不生效"。
        /// </remarks>
        public static MetaHumanMorphGuiMap MorphGui
        {
            get
            {
                if (_morphGui != null)
                {
                    return _morphGui;
                }

                string root = RootDirectory;
                string path = string.IsNullOrEmpty(root) ? null : Path.Combine(root, "Morph_To_Gui_Map.json");
                if (path == null || !File.Exists(path))
                {
                    // 与其余表同一条规矩：散文件不在（cooked 包里就没有）就用内嵌那份，
                    // 并且回退要计数——"用了内嵌"与"表根本不存在"必须是两种能区分的表现。
                    if (!string.IsNullOrEmpty(MetaHumanBakedTables.MorphGuiJson))
                    {
                        _morphGui = MetaHumanMorphGuiMap.FromJson(MetaHumanBakedTables.MorphGuiJson);
                        NoteBakedFallback(path == null
                            ? "模板根目录未找到，morph→GUI 表改用内嵌那份"
                            : "morph→GUI 表文件不存在（" + path + "），改用内嵌那份");
                    }
                    else
                    {
                        Fail("morph→GUI 表不存在：" + (path ?? "<模板根目录未找到>")
                             + "，内嵌那份也没有（跑 Tools/MetaHumanBuildMorphGuiMap.py 再用 Tools/MetaHumanBakeTables.py 烘焙）");
                        return null;
                    }
                }
                else
                {
                    _morphGui = MetaHumanMorphGuiMap.FromJson(File.ReadAllText(path));
                }

                List<string> problems = _morphGui.Validate(GuiToRaw);
                if (problems.Count > 0)
                {
                    for (int i = 0; i < problems.Count && i < 8; i++)
                    {
                        Fail("morph→GUI 表校验未过：" + problems[i]);
                    }

                    _morphGui = null;
                    return null;
                }

                return _morphGui;
            }
        }

        public static void RegisterGuiToRawJson(string json)
        {
            _guiToRaw = MetaHumanGuiToRawMap.FromJson(json);
        }

        /// <summary>
        /// ARKit 通道名 → GUI 控制的对照层（外部面部追踪数据的入口词汇表）。
        /// 表里逐条标了依据，exact 是同名、authored 是人为指定的语义对应。
        /// </summary>
        public static MetaHumanArkitMap ArkitMap
        {
            get
            {
                if (_arkitMap != null)
                {
                    return _arkitMap;
                }

                string root = RootDirectory;
                string path = string.IsNullOrEmpty(root) ? null : Path.Combine(root, "Arkit_To_Gui_Map.json");
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    _arkitMap = MetaHumanArkitMap.FromJson(File.ReadAllText(path));
                    return _arkitMap;
                }

                if (!string.IsNullOrEmpty(MetaHumanBakedTables.ArkitMapJson))
                {
                    _arkitMap = MetaHumanArkitMap.FromJson(MetaHumanBakedTables.ArkitMapJson);
                    NoteBakedFallback("ARKit 对照表改用内嵌那份（散文件在 " + path + "）");
                    return _arkitMap;
                }

                Fail("ARKit 对照表不存在：" + path + "（跑 Tools/MetaHumanBuildArkitMap.py 生成）");
                return null;
            }
        }

        /// <summary>列出可用模板 Id（编辑器窗口与调试用）。</summary>
        public static List<string> AvailableTemplateIds()
        {
            List<string> ids = new List<string>();
            string root = RootDirectory;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                return ids;
            }

            foreach (string file in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    MetaHumanTemplate probe = MetaHumanTemplate.LoadFromFile(file);
                    if (!string.IsNullOrEmpty(probe.TemplateId))
                    {
                        ids.Add(probe.TemplateId);
                    }
                }
                catch
                {
                }
            }

            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        public static void ClearCache()
        {
            Cache.Clear();
            _faceControls = null;
            _guiToRaw = null;
            _morphGui = null;
            _arkitMap = null;
            _bakedFallbackCount = 0;
            _resolvedRoot = null;
        }

        private static void Fail(string message)
        {
            FlaxEngine.Debug.LogError("MetaHuman 模板装载： " + message);
            Action<string> handler = OnLoadFailed;
            if (handler != null)
            {
                handler(message);
            }
        }
    }

    /// <summary>表情控制表条目（来自 SKM_Face.dna 的 174 个 GUI 控制）。</summary>
    public class MetaHumanFaceControl
    {
        public string ControlName { get; set; }
        public string Id { get; set; }
        public string Region { get; set; }
        public string Side { get; set; }
        public string Axis { get; set; }
        public string MirrorOf { get; set; }
        public string Resolution { get; set; }
        public List<string> MorphTargets { get; set; }
        public List<string> Joints { get; set; }

        /// <summary>GUI 控制自己的量程，[下限, 上限]；174 个里有 70 个是 [-1,1] 的双极控制。</summary>
        public List<float> GuiRange { get; set; }

        /// <summary>输入表里没给这个控制时用的缺省值（UE 侧只有两个非零，都是 1）。</summary>
        public float GuiDefault { get; set; }

        /// <summary>换算后会写到的 raw 表情控制及其线性段（来自 UE 的权威映射，不是命名猜测）。</summary>
        public List<MetaHumanFaceRawTarget> RawTargets { get; set; }

        public float RangeLow
        {
            get { return GuiRange != null && GuiRange.Count == 2 ? GuiRange[0] : 0f; }
        }

        public float RangeHigh
        {
            get { return GuiRange != null && GuiRange.Count == 2 ? GuiRange[1] : 1f; }
        }
    }

    /// <summary>
    /// 一个 GUI 控制经线性换算写向某个 raw 表情控制的关系。
    /// Segments 的每一条是 [From, To, Slope, Cut]：GUI 值落在 [From,To) 时贡献 Slope*值+Cut；
    /// 同一个 raw 有多段就是折线（阶段间的交叉淡入淡出），逐段贡献相加。
    /// </summary>
    public class MetaHumanFaceRawTarget
    {
        public string Raw { get; set; }
        public List<string> MorphTargets { get; set; }
        public List<List<float>> Segments { get; set; }

        public MetaHumanFaceRawTarget()
        {
            MorphTargets = new List<string>();
            Segments = new List<List<float>>();
        }
    }

    /// <summary>
    /// 脸部表情控制表：动画/LiveLink 的控制名 → 资产里真实的 blendshape 与面部关节。
    /// 与捏脸参数分开，是因为表情属于动画通道、形状属于自定义通道，二者的消费方不同。
    /// </summary>
    public class MetaHumanFaceControlTable
    {
        public int GuiControlCount { get; set; }
        public List<string> MorphCatalog { get; set; }
        public List<MetaHumanFaceControl> Controls { get; set; }

        private Dictionary<string, MetaHumanFaceControl> _byId;
        private Dictionary<string, MetaHumanFaceControl> _byName;

        public MetaHumanFaceControlTable()
        {
            Controls = new List<MetaHumanFaceControl>();
            MorphCatalog = new List<string>();
        }

        public static MetaHumanFaceControlTable FromJson(string json)
        {
            MetaHumanFaceControlTable table = JsonSerializer.Deserialize<MetaHumanFaceControlTable>(json, Opts);
            if (table == null)
            {
                throw new InvalidDataException("脸部控制表反序列化得到 null");
            }

            table.BuildIndex();
            return table;
        }

        private static readonly JsonSerializerOptions Opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        public void BuildIndex()
        {
            _byId = new Dictionary<string, MetaHumanFaceControl>();
            _byName = new Dictionary<string, MetaHumanFaceControl>();
            if (Controls == null)
            {
                Controls = new List<MetaHumanFaceControl>();
            }

            for (int i = 0; i < Controls.Count; i++)
            {
                MetaHumanFaceControl control = Controls[i];
                if (control == null)
                {
                    continue;
                }

                if (control.MorphTargets == null)
                {
                    control.MorphTargets = new List<string>();
                }

                if (control.Joints == null)
                {
                    control.Joints = new List<string>();
                }

                if (!string.IsNullOrEmpty(control.Id))
                {
                    _byId[control.Id] = control;
                }

                if (!string.IsNullOrEmpty(control.ControlName))
                {
                    _byName[control.ControlName] = control;
                }
            }
        }

        public int Count { get { return Controls == null ? 0 : Controls.Count; } }

        public int ResolvedCount()
        {
            int n = 0;
            for (int i = 0; i < Controls.Count; i++)
            {
                if (Controls[i].Resolution != "unresolved")
                {
                    n++;
                }
            }

            return n;
        }

        public MetaHumanFaceControl Find(string idOrControlName)
        {
            if (_byId == null)
            {
                BuildIndex();
            }

            MetaHumanFaceControl control;
            if (_byId.TryGetValue(idOrControlName, out control))
            {
                return control;
            }

            return _byName.TryGetValue(idOrControlName, out control) ? control : null;
        }

        public List<string> UnresolvedControls()
        {
            List<string> list = new List<string>();
            for (int i = 0; i < Controls.Count; i++)
            {
                if (Controls[i].Resolution == "unresolved")
                {
                    list.Add(Controls[i].ControlName);
                }
            }

            return list;
        }
    }
}
