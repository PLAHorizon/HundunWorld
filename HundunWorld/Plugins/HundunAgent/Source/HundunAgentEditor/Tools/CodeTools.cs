using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using FlaxEngine;
using HundunAgent.Core;

namespace HundunAgent.Tools
{
    /// <summary>
    /// 代码编辑与热重载工具：仅限客户端 Source 目录白名单。
    /// 写入 .cs 后 Flax 编辑器文件监视会自动触发脚本重编译。
    /// </summary>
    public static class CodeTools
    {
        /// <summary>
        /// Absolute, normalized client Source root.
        /// </summary>
        /// <remarks>
        /// <c>Globals.ProjectFolder</c> arrives with mixed separators (e.g. <c>C:\Works/.../HundunWorld</c>) and
        /// no guaranteed trailing separator. <see cref="Path.GetFullPath"/> normalizes both the separator style
        /// and any trailing separator, so it must be applied before any prefix comparison - see
        /// <see cref="ResolveSourcePath"/>.
        /// </remarks>
        public static string SourceRoot => Path.GetFullPath(Path.Combine(Globals.ProjectFolder, "Source"));

        public static void Register()
        {
            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "code_list",
                Description = "列出客户端源码目录下的 .cs 文件（相对 Source 的路径）。dir 可选子目录。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"dir\":{\"type\":\"string\",\"description\":\"可选子目录，如 Game/UI\"},\"limit\":{\"type\":\"integer\"}}}",
                Execute = CodeListAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "code_read",
                Description = "读取客户端源码文件内容。path 相对 Source 目录。可用 startLine/endLine 截取片段。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"startLine\":{\"type\":\"integer\"},\"endLine\":{\"type\":\"integer\"}},\"required\":[\"path\"]}",
                Execute = CodeReadAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "code_write",
                Description = "写入/覆盖客户端源码文件（仅限 Source 目录白名单）。写入前自动备份旧内容到 Logs/HundunAgent/code-backup。写入后编辑器会自动触发脚本重编译，可用 code_build_wait 等待编译结果。危险操作。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"相对 Source 的路径\"},\"content\":{\"type\":\"string\",\"description\":\"完整文件内容\"}},\"required\":[\"path\",\"content\"]}",
                Dangerous = true,
                Execute = CodeWriteAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "code_build_wait",
                Description = "等待编辑器脚本编译完成并返回结果。在 code_write 之后调用，确认热重载是否成功。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"timeoutMs\":{\"type\":\"integer\",\"description\":\"超时毫秒，默认120000\"}}}",
                Execute = CodeBuildWaitAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "code_build_status",
                Description = "查询当前脚本编译状态（是否编译中、上次编译是否失败）。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{}}",
                Execute = CodeBuildStatusAsync
            });
        }

        // ==================== 白名单校验 ====================

        /// <summary>
        /// Resolves a Source-relative path and asserts it stays inside the Source root.
        /// </summary>
        /// <remarks>
        /// Both sides are run through <see cref="Path.GetFullPath"/> before comparing. Previously the root was
        /// left as raw <c>Path.Combine</c> output while the candidate was normalized, so the mixed separators in
        /// <c>Globals.ProjectFolder</c> made every comparison fail and code_read / code_write / code_list
        /// rejected even plainly correct paths such as <c>Game/Game.Build.cs</c>.
        ///
        /// The trailing-separator test is not cosmetic: without it <c>...\SourceEvil\x.cs</c> would be accepted
        /// as a child of <c>...\Source</c>.
        /// </remarks>
        private static string ResolveSourcePath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                throw new ArgumentException("缺少 path");

            var root = SourceRoot;
            var full = Path.GetFullPath(Path.Combine(root, relativePath));

            var inside = string.Equals(full, root, StringComparison.OrdinalIgnoreCase) ||
                         full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                         full.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

            if (!inside)
                throw new UnauthorizedAccessException("路径越出 Source 白名单: " + relativePath);

            return full;
        }

        /// <summary>Root-relative, forward-slashed path for display. Robust to either separator style.</summary>
        private static string ToRelativeDisplay(string fullPath)
        {
            return Path.GetRelativePath(SourceRoot, fullPath).Replace('\\', '/');
        }

        // ==================== Handlers ====================

        private static Task<object> CodeListAsync(JsonElement args)
        {
            var subDir = EditorUtils.GetString(args, "dir", "");
            var limit = EditorUtils.GetInt(args, "limit", 500);

            return Task.FromResult<object>(RunIo(() =>
            {
                var root = SourceRoot;
                var scanRoot = string.IsNullOrEmpty(subDir) ? root : ResolveSourcePath(subDir);
                if (!Directory.Exists(scanRoot))
                    throw new ArgumentException("目录不存在: " + subDir);

                var files = Directory.GetFiles(scanRoot, "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                                !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                    .OrderBy(f => f)
                    .Take(limit)
                    .Select(ToRelativeDisplay)
                    .ToList();

                return new { count = files.Count, files };
            }));
        }

        private static Task<object> CodeReadAsync(JsonElement args)
        {
            var relPath = EditorUtils.GetString(args, "path");
            var startLine = EditorUtils.GetInt(args, "startLine", 0);
            var endLine = EditorUtils.GetInt(args, "endLine", 0);

            return Task.FromResult<object>(RunIo(() =>
            {
                var full = ResolveSourcePath(relPath);
                if (!File.Exists(full))
                    throw new ArgumentException("文件不存在: " + relPath);

                var lines = File.ReadAllLines(full);
                int from = Math.Max(0, startLine - 1);
                int to = endLine > 0 ? Math.Min(lines.Length, endLine) : lines.Length;
                if (from >= lines.Length)
                    from = 0;
                if (to <= from)
                    to = lines.Length;

                // 防止超大文件
                if (to - from > 3000)
                    to = from + 3000;

                var content = string.Join("\n", lines, from, to - from);
                return new
                {
                    path = relPath,
                    totalLines = lines.Length,
                    range = new { from = from + 1, to },
                    content
                };
            }));
        }

        private static Task<object> CodeWriteAsync(JsonElement args)
        {
            var relPath = EditorUtils.GetString(args, "path");
            var content = EditorUtils.GetString(args, "content", "");

            return Task.FromResult<object>(RunIo(() =>
            {
                var full = ResolveSourcePath(relPath);
                if (!full.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("仅允许写入 .cs 文件: " + relPath);

                var dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // 备份旧内容
                if (File.Exists(full))
                {
                    var backupDir = Path.Combine(Globals.ProjectFolder, "Logs", "HundunAgent", "code-backup");
                    Directory.CreateDirectory(backupDir);
                    var backupName = relPath.Replace('/', '_').Replace('\\', '_') + "." +
                                     DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".bak";
                    File.Copy(full, Path.Combine(backupDir, backupName), true);
                }

                File.WriteAllText(full, content);

                return new
                {
                    status = "written",
                    path = relPath,
                    bytes = System.Text.Encoding.UTF8.GetByteCount(content),
                    hint = "编辑器将自动重编译，调用 code_build_wait 确认结果"
                };
            }));
        }

        private static async Task<object> CodeBuildWaitAsync(JsonElement args)
        {
            var timeoutMs = EditorUtils.GetInt(args, "timeoutMs", 120000);

            // Polling rather than event subscriptions: CompilationBegin/End are static events on the same
            // assembly we are about to unload during a reload, so a handler here would be revoked by the very
            // compile it is waiting for.

            // Let the editor notice the file change and enter compiling state (it may already be done).
            var dirtySeen = await MainThread.WaitUntilAsync(() => FlaxEditor.ScriptsBuilder.IsSourceDirty
                                                                || FlaxEditor.ScriptsBuilder.IsCompiling,
                                                            15000);

            // Nudge the check so a build is not left waiting on the editor's own polling interval. Only
            // trigger it when the sources actually changed: CheckForCompile on a clean tree still runs a
            // project regeneration, which would reload the editor for no reason.
            if (dirtySeen)
                await MainThread.InvokeAsync(() => FlaxEditor.ScriptsBuilder.CheckForCompile());

            var done = await MainThread.WaitUntilAsync(() => !FlaxEditor.ScriptsBuilder.IsCompiling, timeoutMs);

            if (!done)
            {
                return new
                {
                    status = "timeout",
                    message = "编译超时（" + timeoutMs + "ms）",
                    isCompiling = FlaxEditor.ScriptsBuilder.IsCompiling
                };
            }

            var failed = FlaxEditor.ScriptsBuilder.LastCompilationFailed;
            return new
            {
                status = failed ? "failed" : "success",
                dirtyChangeSeen = dirtySeen,
                compilations = FlaxEditor.ScriptsBuilder.CompilationsCount,
                message = failed
                    ? "脚本编译失败，请查看编辑器 Output Log 或 log_errors 工具"
                    : "脚本编译成功，热重载已生效"
            };
        }

        private static Task<object> CodeBuildStatusAsync(JsonElement args)
        {
            // FlaxEditor.ScriptsBuilder is a static class, so there is no instance to locate and no reason
            // for the status probe to ever be unavailable. An earlier revision reached it by walking the
            // Editor object graph with reflection, latched a failed search into a permanent flag, and so
            // reported "unknown" forever after one miss.
            return Task.FromResult<object>(new
            {
                available = true,
                isCompiling = FlaxEditor.ScriptsBuilder.IsCompiling,
                isReady = FlaxEditor.ScriptsBuilder.IsReady,
                lastCompilationFailed = FlaxEditor.ScriptsBuilder.LastCompilationFailed,
                compilationsCount = FlaxEditor.ScriptsBuilder.CompilationsCount,
                isSourceDirty = FlaxEditor.ScriptsBuilder.IsSourceDirty,
                isWorkspaceDirty = FlaxEditor.ScriptsBuilder.IsSourceWorkspaceDirty
            });
        }

        private static object RunIo(Func<object> action)
        {
            // 文件 IO 在调用线程执行（工具本身运行于服务器线程），避免占用主线程
            return action();
        }
    }
}
