// PCG 插件 - 磁盘上的图文件
// FlaxEngine PCG 插件的一部分。见 docs/PCG_PLUGIN_DESIGN.md §3.3 注 4。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FlaxEngine;
using PCG.Graph;

namespace PCG.Runtime
{
    /// <summary>
    /// 把图读写成插件自己拥有的普通 JSON 文件。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 托管插件无法注册资产类型，所以图不能活在一个定制的 <c>.pcg</c> 资产里，
    /// 而经引擎的资产管线写回也没有暴露给脚本。于是
    /// 本项目选定的形状是"文件归插件所有"：它自己写这个文件，
    /// 用 <see cref="System.IO"/> 按 <see cref="PCGGraphJson"/> 的格式写，而设计者编辑的是同一个文件，
    /// 通过插件的图工具来编辑。刻意只留一条真值通道——引擎资产里不存在第二份副本，
    /// 不会有它和画布存下的那个文件互相不一致的情况。
    /// </para>
    /// <para>
    /// 文件放在 <c>&lt;project content&gt;/PCG/Graphs</c> 下，这样它们就在 Content 树里，
    /// 会进版本控制、也能按文本 diff。打包后的构建没有项目目录，
    /// 所以随构建发布图就意味着把这些文件放到构建产物旁边；<see cref="ResolvePath"/> 会去常见的几处找，
    /// 并把它试过哪些地方一起报出来，而不是只丢一句"文件找不到"。
    /// </para>
    /// </remarks>
    public static class PCGGraphFile
    {
        /// <summary>图文件在项目 Content 树里所在的子目录。</summary>
        public const string GraphsFolder = "PCG/Graphs";

        /// <summary>图文件使用的扩展名。</summary>
        public const string Extension = ".json";

        /// <summary>
        /// 载入一个图文件。
        /// </summary>
        /// <param name="path">绝对路径，或由 <see cref="ResolvePath"/> 解析的名字/相对路径。</param>
        /// <param name="graph">解析出的图，或 null。</param>
        /// <param name="error">图为什么没能载入，并列出搜索过的位置。</param>
        public static bool TryLoad(string path, out PCGGraph graph, out string error)
        {
            graph = null;
            error = null;

            if (string.IsNullOrWhiteSpace(path))
            {
                error = "No graph file path is set.";
                return false;
            }

            if (!TryResolveExisting(path, out string fullPath, out string findError))
            {
                error = findError;
                return false;
            }

            string text;
            try
            {
                text = File.ReadAllText(fullPath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                error = $"Could not read '{fullPath}': {ex.GetType().Name}: {ex.Message}";
                return false;
            }

            if (!PCGGraphJson.TryParse(text, out graph, out error))
            {
                error = $"'{fullPath}': {error}";
                graph = null;
                return false;
            }

            return true;
        }

        /// <summary>
        /// 写出一个图文件，必要时创建所在目录。
        /// </summary>
        /// <remarks>
        /// 写入先落到一个临时名字，再移动覆盖目标。写了一半的图文件
        /// 下次载入会解析失败，看上去就像设计者的图丢了，所以这一步
        /// 刻意不是就地覆写。
        /// </remarks>
        public static bool TrySave(string path, PCGGraph graph, out string error)
        {
            error = null;

            if (graph == null)
            {
                error = "Refusing to write a null graph.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                error = "No graph file path given.";
                return false;
            }

            string fullPath = ResolvePath(path);

            try
            {
                string folder = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(folder))
                    Directory.CreateDirectory(folder);

                string temp = fullPath + ".tmp";
                File.WriteAllText(temp, PCGGraphJson.ToJson(graph), Encoding.UTF8);

                if (File.Exists(fullPath))
                    File.Delete(fullPath);

                File.Move(temp, fullPath);
            }
            catch (Exception ex)
            {
                error = $"Could not write '{fullPath}': {ex.GetType().Name}: {ex.Message}";
                return false;
            }

            return true;
        }

        /// <summary>解析出的路径上确实有图文件时为真。</summary>
        public static bool Exists(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && File.Exists(ResolvePath(path));
        }

        /// <summary>列出项目图目录里的图名，不带扩展名。</summary>
        public static string[] ListGraphs()
        {
            try
            {
                string folder = DefaultFolder();
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                    return Array.Empty<string>();

                var names = new List<string>();
                foreach (var file in Directory.GetFiles(folder, "*" + Extension, SearchOption.TopDirectoryOnly))
                    names.Add(Path.GetFileNameWithoutExtension(file));

                names.Sort(StringComparer.OrdinalIgnoreCase);
                return names.ToArray();
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>新图写进的那个目录。</summary>
        public static string DefaultFolder()
        {
            string content = ProjectContentFolder();
            return string.IsNullOrEmpty(content)
                ? GraphsFolder
                : Path.Combine(content, GraphsFolder);
        }

        /// <summary>
        /// 把图名或相对路径变成完整路径。裸名字解析进图目录。
        /// </summary>
        /// <remarks>
        /// 结果要过一道 <see cref="Path.GetFullPath"/>，因为 <c>GraphsFolder</c> 为了可移植
        /// 写成斜杠，而 <see cref="Path.Combine"/> 用平台分隔符：混着写的形式
        /// （"PCG/Graphs\name.json"）能用，但在列出搜索过的路径的错误信息里读起来很难看，
        /// 设计者分不清自己是不是打错了什么。
        /// </remarks>
        public static string ResolvePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            string trimmed = path.Trim();
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

            try
            {
                return EnsureExtension(Path.GetFullPath(resolved));
            }
            catch (Exception)
            {
                // 不可用的路径（比如含非法字符）在使用它的地方报告，不在这里报告。
                return EnsureExtension(resolved);
            }
        }

        private static bool TryResolveExisting(string path, out string fullPath, out string error)
        {
            var candidates = new List<string>(4);
            string direct = ResolvePath(path);

            if (!string.IsNullOrEmpty(direct))
                candidates.Add(direct);

            string project = ProjectFolder();
            if (!string.IsNullOrEmpty(project))
            {
                candidates.Add(EnsureExtension(Path.Combine(project, GraphsFolder, path)));
                candidates.Add(EnsureExtension(Path.Combine(project, path)));
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                // 同一个候选可能被解析两次（裸名字本来就等于图目录里的路径）；
                // 多一次存在性探测很便宜，所以在这里去重不值得那份复杂度。
                if (File.Exists(candidates[i]))
                {
                    fullPath = candidates[i];
                    error = null;
                    return true;
                }
            }

            fullPath = null;
            var builder = new StringBuilder();
            builder.Append("No graph file found for '").Append(path).Append("'. Looked in:");

            for (int i = 0; i < candidates.Count; i++)
                builder.Append(i == 0 ? " " : ", ").Append('<').Append(candidates[i]).Append('>');

            builder.Append(". New graphs go into ").Append(DefaultFolder()).Append('.');
            error = builder.ToString();
            return false;
        }

        private static string EnsureExtension(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            return path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) ? path : path + Extension;
        }

        /// <summary>项目的 Content 目录；读不到时为空字符串。</summary>
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

        private static string ProjectFolder()
        {
            try
            {
                return Globals.ProjectFolder;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
