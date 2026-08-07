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
        private static object _scriptsBuilder;
        private static bool _builderSearched;

        public static string SourceRoot => Path.Combine(Globals.ProjectFolder, "Source");

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

        private static string ResolveSourcePath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                throw new ArgumentException("缺少 path");

            var root = SourceRoot;
            var full = Path.GetFullPath(Path.Combine(root, relativePath));

            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("路径越出 Source 白名单: " + relativePath);

            return full;
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
                    .Select(f => f.Substring(root.Length + 1).Replace('\\', '/'))
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

            var builder = await MainThread.InvokeAsync(GetScriptsBuilder);
            if (builder == null)
            {
                // 找不到编译器对象：退化为等待文件被编辑器感知
                await Task.Delay(2000);
                return new
                {
                    status = "unknown",
                    message = "未能定位编辑器脚本编译器（ScriptsBuilder），文件已写入，请在编辑器日志确认编译结果"
                };
            }

            // 等待编辑器感知源码变更并开始编译（最多 15 秒）
            var dirtySeen = await MainThread.WaitUntilAsync(() =>
            {
                var b = GetScriptsBuilder();
                if (b == null) return true;
                var isCompiling = (bool)GetBuilderValue(b, "IsCompiling");
                var dirty = (bool)GetBuilderValue(b, "IsSourceDirty");
                return isCompiling || dirty;
            }, 15000);

            // 触发一次编译检查
            await MainThread.InvokeAsync(() =>
            {
                var b = GetScriptsBuilder();
                if (b != null)
                {
                    var check = b.GetType().GetMethod("CheckForCompile");
                    check?.Invoke(b, null);
                }
            });

            // 等待编译结束
            var done = await MainThread.WaitUntilAsync(() =>
            {
                var b = GetScriptsBuilder();
                if (b == null) return true;
                return !(bool)GetBuilderValue(b, "IsCompiling");
            }, timeoutMs);

            if (!done)
                return new { status = "timeout", message = "编译超时（" + timeoutMs + "ms）" };

            return await MainThread.InvokeAsync<object>(() =>
            {
                var b = GetScriptsBuilder();
                if (b == null)
                    return new { status = "unknown" };

                var failed = (bool)GetBuilderValue(b, "LastCompilationFailed");
                return failed
                    ? (object)new { status = "failed", message = "脚本编译失败，请查看编辑器 Output Log 中的错误" }
                    : new { status = "success", message = "脚本编译成功，热重载已生效" };
            });
        }

        private static Task<object> CodeBuildStatusAsync(JsonElement args)
        {
            return MainThread.InvokeAsync<object>(() =>
            {
                var b = GetScriptsBuilder();
                if (b == null)
                    return new { available = false };

                return new
                {
                    available = true,
                    isCompiling = (bool)GetBuilderValue(b, "IsCompiling"),
                    lastCompilationFailed = (bool)GetBuilderValue(b, "LastCompilationFailed"),
                    compilationsCount = (int)GetBuilderValue(b, "CompilationsCount"),
                    isSourceDirty = (bool)GetBuilderValue(b, "IsSourceDirty")
                };
            });
        }

        // ==================== ScriptsBuilder 反射定位 ====================

        private static object GetScriptsBuilder()
        {
            if (_builderSearched)
                return _scriptsBuilder;

            _builderSearched = true;
            try
            {
                var editor = FlaxEditor.Editor.Instance;
                if (editor == null)
                    return null;

                var builderType = Type.GetType("FlaxEditor.ScriptsBuilder, FlaxEngine.CSharp");
                if (builderType == null)
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        builderType = asm.GetType("FlaxEditor.ScriptsBuilder", false);
                        if (builderType != null)
                            break;
                    }
                }
                if (builderType == null)
                    return null;

                var visited = new HashSet<object>();
                _scriptsBuilder = FindFieldOfType(editor, builderType, 3, visited);
            }
            catch
            {
                _scriptsBuilder = null;
            }

            return _scriptsBuilder;
        }

        private static object FindFieldOfType(object root, Type targetType, int depth, HashSet<object> visited)
        {
            if (root == null || depth < 0 || visited.Contains(root))
                return null;
            visited.Add(root);

            var type = root.GetType();

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                object value;
                try { value = field.GetValue(root); } catch { continue; }
                if (value == null) continue;

                if (targetType.IsInstanceOfType(value))
                    return value;

                if (depth > 0 && value.GetType().Namespace != null &&
                    value.GetType().Namespace.StartsWith("Flax"))
                {
                    var found = FindFieldOfType(value, targetType, depth - 1, visited);
                    if (found != null)
                        return found;
                }
            }

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
                    continue;
                object value;
                try { value = prop.GetValue(root); } catch { continue; }
                if (value == null) continue;

                if (targetType.IsInstanceOfType(value))
                    return value;

                if (depth > 0 && value.GetType().Namespace != null &&
                    value.GetType().Namespace.StartsWith("Flax"))
                {
                    var found = FindFieldOfType(value, targetType, depth - 1, visited);
                    if (found != null)
                        return found;
                }
            }

            return null;
        }

        private static object GetBuilderValue(object builder, string name)
        {
            var prop = builder.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (prop != null)
                return prop.GetValue(builder);
            var field = builder.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field != null)
                return field.GetValue(builder);
            throw new MissingMemberException("ScriptsBuilder." + name);
        }

        private static object RunIo(Func<object> action)
        {
            // 文件 IO 在调用线程执行（工具本身运行于服务器线程），避免占用主线程
            return action();
        }
    }
}
