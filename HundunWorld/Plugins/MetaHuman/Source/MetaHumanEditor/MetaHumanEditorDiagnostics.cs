using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FlaxEngine;
using MetaHuman;
using MetaHuman.Core;
using MetaHuman.Runtime;

namespace MetaHumanEditor
{
    /// <summary>
    /// 数字人模板编辑器的环境自检：把「窗口上每个输入框现在到底是什么状态」一次摊开。
    ///
    /// 这层存在的理由：本模块的失败形态是「改了数值模型没反应」，而原因可能落在
    /// 模板根目录没解析、资产没开 blendshape 导入、Id 拼错、内嵌表过期等五六个互不相干的环节上。
    /// 逐个按钮去试一次只能排除一个假设；自检一次给出全部事实。
    /// 这里只读不写：任何落盘动作都不该发生在「我先看看现在什么情况」这一步。
    /// </summary>
    public static class MetaHumanEditorDiagnostics
    {
        /// <summary>自检输入。全部来自窗口上的填写框，允许为空（空就是一种结论，不是错误）。</summary>
        public class Inputs
        {
            public string TemplateId { get; set; }
            public string OutputTemplateId { get; set; }
            public string ModelPaths { get; set; }
            public string UeDirectory { get; set; }
            public string DestinationFolder { get; set; }
        }

        public static string Check(Inputs inputs)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== 环境自检 " + DateTime.Now.ToString("HH:mm:ss") + " ===");

            CheckTemplates(sb, inputs);
            CheckModelAssets(sb, inputs);
            CheckUeDirectory(sb, inputs);
            CheckBakedFreshness(sb);

            sb.AppendLine();
            sb.AppendLine("下一步读法：上面任何一行是「未解析/装载失败/不存在」，就先修那一行，");
            sb.AppendLine("其余按钮的结果在修好之前都不可信。");
            return sb.ToString();
        }

        private static void CheckTemplates(StringBuilder sb, Inputs inputs)
        {
            sb.AppendLine("[模板]");
            string root = MetaHumanTemplateStore.RootDirectory;
            if (string.IsNullOrEmpty(root))
            {
                sb.AppendLine("  根目录：未解析（试过 " + MetaHumanTemplateStore.TemplatesRelativeDir
                              + " 与 " + MetaHumanTemplateStore.ProjectRelativeDir + "）→ 所有「另存模板」都会失败");
                return;
            }

            sb.AppendLine("  根目录：" + root);

            List<string> ids = MetaHumanTemplateStore.AvailableTemplateIds();
            sb.AppendLine("  盘上模板：" + ids.Count + " 个");
            for (int i = 0; i < ids.Count; i++)
            {
                MetaHumanTemplate template = MetaHumanTemplateStore.Get(ids[i]);
                if (template == null)
                {
                    sb.AppendLine("    · " + ids[i] + " —— 装载失败（详见 Error 日志）");
                    continue;
                }

                sb.AppendLine("    · " + ids[i] + " —— 参数 " + template.Parameters.Count
                              + "，morph 名录 " + template.MorphCatalog.Count);
            }

            string currentId = (inputs.TemplateId ?? string.Empty).Trim();
            if (currentId.Length == 0)
            {
                sb.AppendLine("  当前模板：没填");
            }
            else
            {
                MetaHumanTemplate current = MetaHumanTemplateStore.Get(currentId);
                if (current == null)
                {
                    sb.AppendLine("  当前模板：" + currentId + " —— 装载失败（Id 拼错，或该 Id 只在内嵌表里）");
                }
                else
                {
                    List<string> errors = current.Validate();
                    sb.AppendLine("  当前模板：" + currentId + " —— 装载成功，校验"
                                  + (errors.Count == 0 ? "通过" : errors.Count + " 条错误"));
                    for (int i = 0; i < errors.Count && i < 6; i++)
                    {
                        sb.AppendLine("      ✗ " + errors[i]);
                    }
                }
            }

            string outputId = (inputs.OutputTemplateId ?? string.Empty).Trim();
            if (outputId.Length == 0)
            {
                sb.AppendLine("  输出模板 Id：没填 → 「另存模板」「导入并生成模板」都会没地方写");
            }
            else
            {
                string target = Path.Combine(root, "Template_" + outputId + ".json");
                sb.AppendLine("  输出模板 Id：" + outputId + (File.Exists(target)
                    ? " —— 会覆盖已有文件 " + Path.GetFileName(target)
                    : " —— 新文件"));
            }
        }

        private static void CheckModelAssets(StringBuilder sb, Inputs inputs)
        {
            sb.AppendLine();
            sb.AppendLine("[模型资产]");
            List<string> paths = SplitPaths(inputs.ModelPaths);
            if (paths.Count == 0)
            {
                sb.AppendLine("  没填。用「浏览资产…」从 Content 里选，或直接粘一条 Content/... 路径");
                return;
            }

            for (int i = 0; i < paths.Count; i++)
            {
                string path = paths[i];
                string fileNote = ResolveAssetFile(path);
                SkinnedModel model = MetaHumanAssetUtil.LoadSkinnedModel(path);
                if (model == null)
                {
                    sb.AppendLine("  " + path + fileNote + " —— 装载失败：不是 SkinnedModel，或路径不对");
                    continue;
                }

                HashSet<string> bones = new HashSet<string>(MetaHumanRigBinder.AssetBoneNames(model));
                HashSet<string> morphs = MetaHumanRigBinder.AssetMorphNames(model);
                sb.AppendLine("  " + MetaHumanAssetUtil.DisplayName(model) + fileNote
                              + " —— 骨骼 " + bones.Count + "，blendshape " + morphs.Count
                              + (morphs.Count == 0 ? "（导入没开 ImportBlendShapes，形状参数全部会静默失效）" : string.Empty));
            }
        }

        private static void CheckUeDirectory(StringBuilder sb, Inputs inputs)
        {
            sb.AppendLine();
            sb.AppendLine("[UE 导出目录]");
            string directory = (inputs.UeDirectory ?? string.Empty).Trim();
            if (directory.Length == 0)
            {
                sb.AppendLine("  没填 → 只有走「导入并生成模板」时才需要");
                return;
            }

            if (!Directory.Exists(directory))
            {
                sb.AppendLine("  " + directory + " —— 目录不存在（要的是 UE MetaHuman 管线导出物的文件夹本身，不是它的上一级）");
                return;
            }

            MetaHumanUEImportPlan plan = MetaHumanUEImporter.Scan(directory, inputs.DestinationFolder);
            sb.AppendLine("  " + directory);
            sb.AppendLine("  模型候选 " + plan.ModelFiles.Count + "，贴图槽位 " + plan.TexturesBySlot.Count
                          + "，描述 JSON " + plan.DescriptionFiles.Count);
            foreach (KeyValuePair<string, string> slot in plan.TexturesBySlot)
            {
                sb.AppendLine("    · " + slot.Key + " ← " + Path.GetFileName(slot.Value));
            }

            for (int i = 0; i < plan.Warnings.Count; i++)
            {
                sb.AppendLine("    ! " + plan.Warnings[i]);
            }
        }

        /// <summary>
        /// 内嵌表新鲜度：编辑器窗口读的是盘上 JSON，而 PIE 与发布构建读的是烘进
        /// MetaHuman.CSharp.dll 的那一份。两者谁生效不该靠人记，所以这里按修改时间比一次。
        /// </summary>
        private static void CheckBakedFreshness(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("[内嵌表新鲜度]");
            DateTime jsonTime = NewestWriteTime(MetaHumanTemplateStore.RootDirectory, "*.json");
            string pluginRoot = string.IsNullOrEmpty(Globals.ProjectFolder)
                ? null
                : Path.Combine(Globals.ProjectFolder, "Plugins", "MetaHuman", "Binaries");
            DateTime dllTime = NewestWriteTime(pluginRoot, "MetaHuman.CSharp.dll");

            if (jsonTime == DateTime.MinValue)
            {
                sb.AppendLine("  没读到模板 JSON，无从比较");
                return;
            }

            if (dllTime == DateTime.MinValue)
            {
                sb.AppendLine("  没读到 MetaHuman.CSharp.dll（插件还没编译过？）");
                return;
            }

            sb.AppendLine("  最新模板 JSON：" + jsonTime.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine("  最新运行程序集：" + dllTime.ToString("yyyy-MM-dd HH:mm"));
            if (jsonTime > dllTime)
            {
                sb.AppendLine("  ✗ JSON 比程序集新 → 运行期（PIE/发布）用的还是旧内嵌表。");
                sb.AppendLine("    要跑：python Tools/MetaHumanBakeTables.py，再重编 MetaHumanTarget，然后重启编辑器。");
            }
            else
            {
                sb.AppendLine("  ✓ 程序集不早于模板文件，运行期与盘上一致");
            }
        }

        private static DateTime NewestWriteTime(string directory, string pattern)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return DateTime.MinValue;
            }

            try
            {
                DateTime newest = DateTime.MinValue;
                foreach (string file in Directory.GetFiles(directory, pattern, SearchOption.AllDirectories))
                {
                    DateTime time = File.GetLastWriteTime(file);
                    if (time > newest)
                    {
                        newest = time;
                    }
                }

                return newest;
            }
            catch (Exception)
            {
                return DateTime.MinValue;
            }
        }

        /// <summary>把「a; b, c」这类填写拆成路径清单。分隔符与装载侧保持一致。</summary>
        public static List<string> SplitPaths(string raw)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrEmpty(raw))
            {
                return result;
            }

            string[] parts = raw.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string trimmed = parts[i].Trim();
                if (trimmed.Length > 0)
                {
                    result.Add(trimmed);
                }
            }

            return result;
        }

        /// <summary>
        /// 资产路径对应的磁盘文件在不在。返回一句可直接拼进报告的括号说明（不在也说得清为什么）。
        /// </summary>
        public static string ResolveAssetFile(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return "（空）";
            }

            string project = Globals.ProjectFolder;
            string normalized = assetPath.Replace('/', Path.DirectorySeparatorChar)
                                .Replace('\\', Path.DirectorySeparatorChar);
            List<string> candidates = new List<string>();

            if (Path.IsPathRooted(normalized))
            {
                candidates.Add(normalized);
                candidates.Add(normalized + ".flax");
            }
            else if (!string.IsNullOrEmpty(project))
            {
                candidates.Add(Path.Combine(project, normalized));
                candidates.Add(Path.Combine(project, normalized + ".flax"));
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                if (File.Exists(candidates[i]))
                {
                    return string.Empty;
                }
            }

            return "（盘上没有这个文件）";
        }

        /// <summary>把绝对路径换成工程相对路径（Content.Load 认这个形式）。</summary>
        public static string ToAssetPath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath))
            {
                return absolutePath;
            }

            string project = Globals.ProjectFolder;
            if (!string.IsNullOrEmpty(project))
            {
                try
                {
                    string relative = Path.GetRelativePath(project, absolutePath);
                    if (!relative.StartsWith("..", StringComparison.Ordinal))
                    {
                        return relative.Replace('\\', '/');
                    }
                }
                catch (Exception)
                {
                    // 跨盘符等情况：原样给出绝对路径，装载侧仍然可用
                }
            }

            return absolutePath.Replace('\\', '/');
        }
    }
}
