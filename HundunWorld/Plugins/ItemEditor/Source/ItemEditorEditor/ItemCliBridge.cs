using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using FlaxEngine;

namespace ItemEditor
{
    /// <summary>一次 CLI 调用的结果。</summary>
    public sealed class ItemCliResult
    {
        public bool Ok;
        public string Verb = "";
        public string Message = "";
        public JsonElement Data;
        public bool HasData;
        public string Raw = "";
        public string StdErr = "";
        public int ExitCode;

        /// <summary>把结果压成一行给用户看的文本。</summary>
        public string Display
        {
            get
            {
                var builder = new StringBuilder();
                builder.Append(Ok ? "OK" : "失败").Append(" ｜ ").Append(Message);
                if (!string.IsNullOrWhiteSpace(StdErr))
                    builder.AppendLine().Append("stderr: ").Append(StdErr.Trim());
                return builder.ToString();
            }
        }
    }

    /// <summary>
    /// Horizon.ItemCli 子进程桥：编辑器的每一次读写都走这里。
    /// <para>
    /// <b>为什么用子进程而不是在 Flax 脚本域里直连 SQL</b>：权威源是 SQL Server，
    /// 校验规则住在服务端（Horizon.Game.Sim.Items）。把 SqlClient/EF 拖进编辑器进程，
    /// 就等于在客户端再养一份校验器——两份规则一改一漏，最后坏数据照样进库。
    /// </para>
    /// </summary>
    public static class ItemCli
    {
        /// <summary>用户手工指定的 exe 路径（自动探测失败时的兜底）。</summary>
        public static string ExePathOverride = "";

        private static string _cachedExePath;

        /// <summary>
        /// 自动定位 CLI 可执行文件：仓库根 = 工程目录的上一级，取 Debug 下的产物。
        /// 没编过时返回 null，让窗口把"先编一次 Horizon.ItemCli"这句话显示出来，
        /// 而不是抛一个看不懂的 Win32Exception。
        /// </summary>
        public static string DetectExePath()
        {
            if (!string.IsNullOrWhiteSpace(ExePathOverride) && File.Exists(ExePathOverride))
                return ExePathOverride;

            if (!string.IsNullOrWhiteSpace(_cachedExePath) && File.Exists(_cachedExePath))
                return _cachedExePath;

            var candidates = new System.Collections.Generic.List<string>();
            var projectDirectory = TryGetProjectDirectory();
            if (projectDirectory != null)
            {
                var repositoryRoot = Path.GetDirectoryName(projectDirectory);
                if (!string.IsNullOrEmpty(repositoryRoot))
                {
                    candidates.Add(Path.Combine(repositoryRoot, "Horizon.ItemCli", "bin", "Debug", "net10.0", "Horizon.ItemCli.exe"));
                    candidates.Add(Path.Combine(repositoryRoot, "Horizon.ItemCli", "bin", "Release", "net10.0", "Horizon.ItemCli.exe"));
                }
            }

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    _cachedExePath = candidate;
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>清掉缓存的 exe 路径（用户改过覆盖值后调用）。</summary>
        public static void ResetCache() => _cachedExePath = null;

        private static string TryGetProjectDirectory()
        {
            try
            {
                // Globals.ProjectFolder 是引擎给的唯一权威工程目录。
                return Globals.ProjectFolder;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>跑一次 CLI（同步；GUI 回调里请用 <see cref="RunAsync"/>）。</summary>
        public static ItemCliResult Run(string arguments, string stdin = null)
        {
            var result = new ItemCliResult();
            var exe = DetectExePath();
            if (string.IsNullOrEmpty(exe))
            {
                result.Ok = false;
                result.Message = "没找到 Horizon.ItemCli.exe。先在仓库根跑一次：dotnet build Horizon.ItemCli/Horizon.ItemCli.csproj（或在上方手填路径）。";
                return result;
            }

            try
            {
                // StandardInputEncoding 只在输入被重定向时才能设；不传 stdin 的动词不重定向。
                // 编码改成手工写字节（见下面写 stdin 那段）：既避开限制，也避开 Encoding.UTF8 写 BOM 的坑。
                var redirectInput = !string.IsNullOrEmpty(stdin);
                var startInfo = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = arguments,
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = redirectInput,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };

                using (var process = new Process { StartInfo = startInfo })
                {
                    var stderrBuilder = new StringBuilder();
                    var errorReadDone = new ManualResetEventSlim(false);
                    process.ErrorDataReceived += (sender, args) =>
                    {
                        if (args.Data == null)
                        {
                            errorReadDone.Set();
                            return;
                        }
                        stderrBuilder.AppendLine(args.Data);
                    };

                    process.Start();
                    process.BeginErrorReadLine();

                    if (redirectInput)
                    {
                        var payload = Encoding.UTF8.GetBytes(stdin);
                        using (var pipe = process.StandardInput.BaseStream)
                        {
                            pipe.Write(payload, 0, payload.Length);
                            pipe.Flush();
                        }
                    }

                    var stdout = process.StandardOutput.ReadToEnd();
                    if (!process.WaitForExit(120000))
                    {
                        try { process.Kill(true); } catch { /* 已经退了就算 */ }
                        result.Ok = false;
                        result.Message = "CLI 超时（120 秒）。检查数据库是否可达、连接串是否就位。";
                        return result;
                    }

                    errorReadDone.Wait(TimeSpan.FromMilliseconds(200));
                    result.ExitCode = process.ExitCode;
                    result.Raw = stdout ?? string.Empty;
                    result.StdErr = stderrBuilder.ToString();
                }
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.Message = "起不了 CLI 进程：" + ex.Message;
                return result;
            }

            ParseResult(result);
            return result;
        }

        /// <summary>
        /// 异步跑一次 CLI：**进程启动与读流都在线程池上**，编辑器主线程一秒都不被占住。
        /// 回调**保证在主线程**上执行（经 <c>Scripting.InvokeOnUpdate</c> 排队到下一帧）。
        /// </summary>
        public static void RunAsync(string arguments, string stdin, Action<ItemCliResult> onCompleted)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                ItemCliResult result;
                try
                {
                    result = Run(arguments, stdin);
                }
                catch (Exception ex)
                {
                    result = new ItemCliResult { Ok = false, Verb = arguments, Message = "CLI 调用异常：" + ex.Message };
                }

                if (onCompleted == null) return;

                try
                {
                    FlaxEngine.Scripting.InvokeOnUpdate(() =>
                    {
                        try
                        {
                            onCompleted(result);
                        }
                        catch (Exception ex)
                        {
                            FlaxEngine.Debug.LogError("物品编辑器：处理 CLI 结果时出错（" + arguments + "）：" + ex);
                        }
                    });
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError("物品编辑器：把 CLI 结果排队回主线程失败（" + arguments + "）：" + ex.Message);
                }
            });
        }

        private static void ParseResult(ItemCliResult result)
        {
            // 顺带剥掉可能存在的 U+FEFF：它既不是空白也不算 `{` 开头，留着会让下面按 "{" 找行的循环
            // 一行都匹配不上，最后报成"CLI 输出不是 JSON"——把编码问题伪装成协议问题。
            var text = (result.Raw ?? string.Empty).Trim().TrimStart('\uFEFF');
            if (text.Length == 0)
            {
                result.Ok = false;
                result.Message = "CLI 没有任何输出（退出码 " + result.ExitCode + "）。";
                return;
            }

            // 只取最后一行 JSON：万一有人以后往 stdout 里加了进度文本，也不至于整段解析失败。
            var lines = text.Split('\n');
            for (var index = lines.Length - 1; index >= 0; index--)
            {
                var line = lines[index].Trim();
                if (!line.StartsWith("{", StringComparison.Ordinal)) continue;
                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    result.Ok = root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
                    result.Verb = ReadString(root, "verb");
                    result.Message = ReadString(root, "message");
                    if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                    {
                        // JsonDocument 释放后其内部元素不可再用，先克隆一份留到调用方。
                        result.Data = data.Clone();
                        result.HasData = true;
                    }
                    return;
                }
                catch (JsonException)
                {
                    // 继续往前找一行能解析的。
                }
            }

            result.Ok = result.ExitCode == 0;
            result.Message = "CLI 输出不是 JSON：" + Truncate(text, 200);
        }

        private static string ReadString(JsonElement element, string name)
            => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : string.Empty;

        private static string Truncate(string text, int max)
            => text.Length <= max ? text : text.Substring(0, max) + "…";
    }
}
