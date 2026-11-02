// PCG 插件 - 设定描述文件（读一段描述文本，编译成图）
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.39。

using System;
using System.IO;
using FlaxEngine;

namespace PCG.Graph
{
    /// <summary>
    /// 把一段**描述文件**变成图：读文本 → 解析 → 编译，路径约定与图表文件一致。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 描述为什么放在文件里而不是只在界面里敲：它是**设计的输入**，要能版本管理、能互相传、能用任意编辑器改，
    /// 而且一张图往往配一份描述。"贴一段文字"最自然的落地形状就是一个文本文件。
    /// </para>
    /// <para>
    /// 这一层是纯的：只做文件读写与调用解析器/编译器，不认识引擎、不认识编辑器。资产路径 → 资产 id 的解析
    /// 由调用方注入（编辑器里用资产数据库，离屏断言里用假表），所以整条链路可以离线跑。
    /// </para>
    /// </remarks>
    public static class PCGSpecFile
    {
        /// <summary>描述文件所在的 Content 子目录。</summary>
        public const string SpecsFolder = "PCG/Specs";

        /// <summary>描述文件的扩展名。</summary>
        public const string Extension = ".pcgspec";

        /// <summary>新描述默认落在哪个目录。</summary>
        public static string DefaultFolder()
        {
            string content = ProjectContentFolder();

            return string.IsNullOrEmpty(content) ? SpecsFolder : Path.Combine(content, SpecsFolder);
        }

        /// <summary>
        /// 项目的 Content 目录；读不到就给空串。
        /// </summary>
        /// <remarks>
        /// 自己查一次而不是借用 <c>PCGGraphFile</c> 的同名方法：那一层在 `PCG.Runtime`，而这是 `PCG.Graph`，
        /// 让图表模型反过来依赖运行时工具是把依赖方向拧了——而这个查询本体只有一次 try/catch。
        /// </remarks>
        private static string ProjectContentFolder()
        {
            try
            {
                return Globals.ProjectContentFolder;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 把裸名或相对路径变成完整路径。裸名落在 <see cref="DefaultFolder"/> 里，带分隔符的路径相对 Content。
        /// </summary>
        public static string ResolvePath(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            string trimmed = name.Trim();
            string resolved;

            if (Path.IsPathRooted(trimmed))
            {
                resolved = trimmed;
            }
            else if (trimmed.IndexOf('/') >= 0 || trimmed.IndexOf('\\') >= 0)
            {
                string content = ProjectContentFolder();
                resolved = string.IsNullOrEmpty(content) ? trimmed : Path.Combine(content, trimmed);
            }
            else
            {
                resolved = Path.Combine(DefaultFolder(), trimmed);
            }

            if (!resolved.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
                resolved += Extension;

            return Path.GetFullPath(resolved);
        }

        /// <summary>这个描述文件在不在。</summary>
        public static bool Exists(string name)
        {
            try
            {
                string full = ResolvePath(name);
                return full.Length > 0 && File.Exists(full);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 读一个描述文件并编译成图。
        /// </summary>
        /// <param name="name">裸名或相对路径。</param>
        /// <param name="modelResolver">资产路径 → 资产 id；为 null 时所有模型路径都算解析不出来。</param>
        /// <returns>编译结果；读不到文件或描述写错时 <see cref="PCGSpecCompileResult.Error"/> 有原因。</returns>
        public static PCGSpecCompileResult TryLoad(string name, Func<string, Guid> modelResolver = null)
        {
            var result = new PCGSpecCompileResult();

            if (string.IsNullOrWhiteSpace(name))
            {
                result.Error = "没有给描述文件名。";
                return result;
            }

            string path = ResolvePath(name);
            string text;

            try
            {
                // 描述文件是给人看的，带 BOM 是常见事；不剥掉的话第一行会多出一个不可见字符，指令就认不出来。
                text = File.ReadAllText(path).TrimStart('\uFEFF');
            }
            catch (Exception ex)
            {
                result.Error = $"读不到描述文件 {path}：{ex.GetType().Name}: {ex.Message}";
                return result;
            }

            if (!PCGSpecParser.TryParse(text, out var spec, out string parseError))
            {
                result.Error = $"{Path.GetFileName(path)}：{parseError}";
                return result;
            }

            var compiled = PCGSpecCompiler.Compile(spec, modelResolver);

            if (!compiled.Ok)
            {
                result.Error = $"{Path.GetFileName(path)}：{compiled.Error}";
                return result;
            }

            result.Graph = compiled.Graph;
            result.Warnings.AddRange(compiled.Warnings);
            return result;
        }
    }
}
