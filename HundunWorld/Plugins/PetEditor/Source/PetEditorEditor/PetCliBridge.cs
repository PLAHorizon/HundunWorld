using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using FlaxEngine;

namespace PetEditor
{
    /// <summary>一次 CLI 调用的结果。</summary>
    public sealed class PetCliResult
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
    /// Horizon.PetCli 子进程桥：编辑器的每一次读写都走这里。
    /// <para>
    /// <b>为什么用子进程而不是在 Flax 脚本域里直连 SQL</b>：权威源是 SQL Server，
    /// 校验规则住在服务端（Horizon.Game.Sim.Quests）。把 SqlClient/EF 拖进编辑器进程，
    /// 就等于在客户端再养一份校验器——两份规则一改一漏，最后坏数据照样进库。
    /// </para>
    /// </summary>
    public static class PetCli
    {
        /// <summary>用户手工指定的 exe 路径（自动探测失败时的兜底）。</summary>
        public static string ExePathOverride = "";

        private static string _cachedExePath;

        /// <summary>
        /// 自动定位 CLI 可执行文件：仓库根 = 工程目录的上一级，取 Debug 下的产物。
        /// 没编过时返回 null，让窗口把"先编一次 Horizon.PetCli"这句话显示出来，
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
                    candidates.Add(Path.Combine(repositoryRoot, "Horizon.PetCli", "bin", "Debug", "net10.0", "Horizon.PetCli.exe"));
                    candidates.Add(Path.Combine(repositoryRoot, "Horizon.PetCli", "bin", "Release", "net10.0", "Horizon.PetCli.exe"));
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
                // Globals.ProjectFolder 是引擎给的唯一权威工程目录（FlaxEngine.Project 这个类型在脚本域里不存在）。
                return Globals.ProjectFolder;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 跑一次 CLI。
        /// </summary>
        /// <param name="arguments">动词与参数，如 <c>publish 10001</c>。</param>
        /// <param name="stdin">需要喂草稿 JSON 时传入（<c>validate -</c> / <c>upsert -</c>）。</param>
        public static PetCliResult Run(string arguments, string stdin = null)
        {
            var result = new PetCliResult();
            var exe = DetectExePath();
            if (string.IsNullOrEmpty(exe))
            {
                result.Ok = false;
                result.Message = "没找到 Horizon.PetCli.exe。先在仓库根跑一次：dotnet build Horizon.PetCli/Horizon.PetCli.csproj（或在上方手填路径）。";
                return result;
            }

            try
            {
                // 这两条流是**按需**重定向的：不传 stdin 的动词（catalog/list/publish…）走的是控制台句柄。
                // StandardInputEncoding 只在输入被重定向时才能设——无条件设它会让 Process.Start() 直接抛
                // 「StandardInputEncoding is only supported when standard input is redirected.」，
                // 表现就是"只要不带草稿，所有按钮都报起不了 CLI 进程"。
                // 而且这里**刻意完全不设**它：编码改成手工写字节（见下面写 stdin 那段），
                // 既避开这条只在重定向时成立的限制，又避开 Encoding.UTF8 编码器会写 BOM 的坑。
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
                        if (args.Data == null) return;
                        stderrBuilder.AppendLine(args.Data);
                    };

                    process.Start();
                    process.BeginErrorReadLine();

                    if (redirectInput)
                    {
                        // 手工把 UTF-8 字节写进管道，而不是 writer.Write(string)：
                        // ① Encoding.UTF8 作为 StreamWriter 的编码会在流首写 BOM，子进程 STJ 报
                        //    `'0xEF' is an invalid start of a value`（编辑器现场实测过）；
                        // ② Encoding.UTF8.GetBytes **从不**带 BOM，于是这条路上不再有任何
                        //    "编码配置设没设成"的不确定性。关流即 EOF，子进程的 ReadToEnd 才会返回。
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

                    // 异步 stderr 要等进程退出后才会把最后一块送完，稍等一下再取值。
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
        /// <para>
        /// 为什么必须异步：<see cref="Run"/> 里 `Start → ReadToEnd → WaitForExit(120s)` 是同步阻塞的，
        /// 而 GUI 的每个按钮回调都跑在主线程上——一次 `catalog`（连库 + EF 查询）就能让编辑器僵住
        /// 几百毫秒到数秒，"点一下卡一下"就是这么来的。**"保存/发布"更糟**，它后面还紧跟一次目录刷新，
        /// 等于两次串行阻塞叠在一起。
        /// </para>
        /// <para>
        /// 回调**保证在主线程**上执行（经 <c>Scripting.InvokeOnUpdate</c> 排队到下一帧），
        /// 所以回调里可以直接读写控件，不需要自己再加锁。
        /// </para>
        /// </summary>
        public static void RunAsync(string arguments, string stdin, Action<PetCliResult> onCompleted)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                PetCliResult result;
                try
                {
                    result = Run(arguments, stdin);
                }
                catch (Exception ex)
                {
                    // 后台线程上的异常没人接就会把进程带下去（编辑器里表现为莫名其妙的崩溃）。
                    result = new PetCliResult { Ok = false, Verb = arguments, Message = "CLI 调用异常：" + ex.Message };
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
                            // 回灌发生在编辑器主线程的更新队列里，抛出去会打断这一帧剩下的更新。
                            FlaxEngine.Debug.LogError("宠物编辑器：处理 CLI 结果时出错（" + arguments + "）：" + ex);
                        }
                    });
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError("宠物编辑器：把 CLI 结果排队回主线程失败（" + arguments + "）：" + ex.Message);
                }
            });
        }

        private static void ParseResult(PetCliResult result)
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
