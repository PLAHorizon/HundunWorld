using System;
using System.IO;
using NarrativePro.Core;

namespace NarrativePro.Data
{
    /// <summary>
    /// NarrativePro JSON 数据资产的统一路径解析器。
    /// <para>
    /// 背景：插件内的资产路径全部写成工程相对形式（如 "Content/NarrativePro/Quests/Q001.json"），
    /// 而 <see cref="File.ReadAllText"/>/<see cref="Directory.Exists"/> 按进程的当前工作目录解析相对路径。
    /// 编辑器、PIE、打包后的exe 三者的工作目录并不一致（打包后甚至根本没有工程目录），
    /// 导致相对路径在不同运行环境里指向不同位置甚至解析失败。
    /// </para>
    /// <para>解析规则（<see cref="Resolve"/>）：</para>
    /// <list type="number">
    /// <item>已是绝对路径 → 原样返回。</item>
    /// <item>以 "Content/"、"Plugins/"、"Saved/"、"Config/" 开头 → 拼到工程根目录（编辑器/PIE 下即 .flaxproj 所在目录）。</item>
    /// <item>其余相对路径 → 视为 Content 目录下的相对路径。</item>
    /// </list>
    /// <para>
    /// 工程根目录探测顺序：环境变量 HUNDUN_PROJECT_DIR（可选覆盖）→ 当前工作目录向上找 *.flaxproj
    /// → AppContext.BaseDirectory（脚本 DLL 所在目录，通常位于工程 Binaries 下）向上找 *.flaxproj
    /// → 打包产物布局（exe 旁有 Content 目录）→ 兜底当前工作目录并写一条警告。
    /// </para>
    /// </summary>
    public static class NarrativeDataPaths
    {
        /// <summary>可选：用环境变量强制指定工程根目录（CI/打包旁路数据用）。</summary>
        public const string ProjectDirEnvironmentVariable = "HUNDUN_PROJECT_DIR";

        private static string _projectDirectory;
        private static bool _bResolved;

        /// <summary>工程根目录（.flaxproj 所在目录；打包构建下为含 Content 目录的根）。</summary>
        public static string ProjectDirectory
        {
            get
            {
                EnsureResolved();
                return _projectDirectory;
            }
        }

        /// <summary>工程 Content 目录（编辑器/PIE 下的数据落盘位置）。</summary>
        public static string ContentDirectory
        {
            get
            {
                string dir = Path.Combine(ProjectDirectory, "Content");
                // 打包构建里 Content 可能就在 exe 旁边
                if (Directory.Exists(dir)) return dir;
                string packed = Path.Combine(AppContext.BaseDirectory, "Content");
                return Directory.Exists(packed) ? packed : dir;
            }
        }

        private static void EnsureResolved()
        {
            if (_bResolved) return;
            _bResolved = true;
            _projectDirectory = FindProjectDirectory();
        }

        private static string FindProjectDirectory()
        {
            // ① 显式覆盖
            try
            {
                string fromEnv = Environment.GetEnvironmentVariable(ProjectDirEnvironmentVariable);
                if (!string.IsNullOrEmpty(fromEnv) && Directory.Exists(fromEnv))
                    return Path.GetFullPath(fromEnv);
            }
            catch
            {
                // 环境变量不可读时忽略
            }

            // ② 从候选起点逐级向上找 *.flaxproj（编辑器与 PIE：CWD 或脚本 DLL 都在工程树内）
            string contentFallback = null;
            foreach (var start in new[] { SafeCurrentDirectory(), AppContext.BaseDirectory })
            {
                var dir = new DirectoryInfo(start);
                for (int hop = 0; dir != null && hop < 12; hop++, dir = dir.Parent)
                {
                    try
                    {
                        if (Directory.Exists(dir.FullName))
                        {
                            var proj = Directory.GetFiles(dir.FullName, "*.flaxproj");
                            if (proj.Length > 0)
                                return dir.FullName;

                            if (contentFallback == null &&
                                (Directory.Exists(Path.Combine(dir.FullName, "Content")) ||
                                 File.Exists(Path.Combine(dir.FullName, "GameSettings.json"))))
                            {
                                contentFallback = dir.FullName;
                            }
                        }
                    }
                    catch
                    {
                        // 无权限的层级直接跳过
                    }
                }
            }

            if (contentFallback != null)
                return contentFallback;

            // ③ 打包产物：exe 同级的 Content
            string packed = Path.Combine(AppContext.BaseDirectory, "Content");
            if (Directory.Exists(packed))
                return AppContext.BaseDirectory;

            NarrativeLog.LogWarning(
                "[NarrativeDataPaths] 未能定位工程根目录（未找到 *.flaxproj），相对数据路径将按当前工作目录解析。" +
                "可通过环境变量 " + ProjectDirEnvironmentVariable + " 指定。");
            return SafeCurrentDirectory();
        }

        private static string SafeCurrentDirectory()
        {
            try { return Directory.GetCurrentDirectory(); }
            catch { return AppContext.BaseDirectory ?? "."; }
        }

        /// <summary>路径是否已是绝对路径。</summary>
        public static bool IsRooted(string path) => !string.IsNullOrEmpty(path) && Path.IsPathRooted(path);

        /// <summary>
        /// 把插件内的相对资产路径解析为文件系统绝对路径。
        /// 空串与绝对路径原样返回（保持调用方现有短路行为）。
        /// </summary>
        public static string Resolve(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (Path.IsPathRooted(path)) return path;

            // 统一分隔符后再判断前缀，兼容 "Content\NarrativePro\..." 写法
            string unix = path.Replace('\\', '/');
            if (unix.StartsWith("Content/", StringComparison.OrdinalIgnoreCase) ||
                unix.StartsWith("Plugins/", StringComparison.OrdinalIgnoreCase) ||
                unix.StartsWith("Saved/", StringComparison.OrdinalIgnoreCase) ||
                unix.StartsWith("Config/", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(Path.Combine(ProjectDirectory, unix.Replace('/', Path.DirectorySeparatorChar)));
            }

            return Path.GetFullPath(Path.Combine(ContentDirectory, unix.Replace('/', Path.DirectorySeparatorChar)));
        }

        /// <summary>解析目录路径（等价 <see cref="Resolve"/>，语义别名，便于调用方自检）。</summary>
        public static string ResolveDirectory(string directoryPath) => Resolve(directoryPath);

        /// <summary>
        /// 解析并校验文件存在。不存在时按 UE5 资产缺失诊断口径写一条 Error（"Has it been renamed?"），
        /// 返回解析后的绝对路径供调用方继续走原有的抛异常/返回 null 分支。
        /// </summary>
        public static string ResolveForRead(string path, string label)
        {
            string resolved = Resolve(path);
            if (string.IsNullOrEmpty(resolved) || File.Exists(resolved)) return resolved;

            NarrativeLog.LogError(
                $"[Data] Tried loading {label} file but it is missing: '{resolved}'. Has it been renamed or moved? " +
                $"(调用方给出的原始路径: {path}；工程目录: {ProjectDirectory})");
            return resolved;
        }

        /// <summary>数据文件是否存在（相对路径按本解析器口径）。</summary>
        public static bool DataFileExists(string path)
        {
            string resolved = Resolve(path);
            return !string.IsNullOrEmpty(resolved) && File.Exists(resolved);
        }
    }
}
