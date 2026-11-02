using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using FlaxEngine;

namespace NpcEditor
{
    /// <summary>一次 CLI 调用的结果（与 MonsterCliResult 同构，编辑器解析逻辑可照搬）。</summary>
    public sealed class NpcCliResult
    {
        public bool Ok;
        public string Verb = "";
        public string Message = "";
        public JsonElement Data;
        public bool HasData;
        public string Raw = "";
        public string StdErr = "";
        public int ExitCode;

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
    /// Horizon.NpcCli 子进程桥：编辑器的每一次读写都走这里。
    /// <para>
    /// <b>为什么不在 Flax 脚本域里直连 SQL</b>：权威源是 SQL Server，校验判据在
    /// <c>Horizon.Game.Sim/Npcs</c> 与 <c>NpcContentLoader</c> 里各只有一份。
    /// 把 EF 拖进编辑器进程就等于在客户端再养一份校验器——两份规则一改一漏，坏数据照样进库。
    /// </para>
    /// <para>
    /// <b>每次调用都是短命周期进程</b>：接收一行 JSON，退出。编辑器不缓存句柄，
    /// 所以"CLI 刚重编过"不需要重启编辑器。
    /// </para>
    /// </summary>
    public static class NpcCli
    {
        /// <summary>用户手工指定的 exe 路径（自动探测失败时的兜底）。</summary>
        public static string ExePathOverride = "";

        private static string _cachedExePath;

        /// <summary>
        /// 自动定位 CLI 可执行文件：仓库根 = 工程目录的上一级，取 Debug 下的产物。
        /// 没编过时返回 null，让窗口把"先编一次 Horizon.NpcCli"这句话显示出来，
        /// 而不是抛一个看不懂的 Win32Exception。
        /// </summary>
        public static string DetectExePath()
        {
            if (!string.IsNullOrWhiteSpace(ExePathOverride) && File.Exists(ExePathOverride))
                return ExePathOverride;

            if (!string.IsNullOrWhiteSpace(_cachedExePath) && File.Exists(_cachedExePath))
                return _cachedExePath;

            var projectDirectory = TryGetProjectDirectory();
            if (projectDirectory == null) return null;

            var repositoryRoot = Path.GetDirectoryName(projectDirectory);
            if (string.IsNullOrEmpty(repositoryRoot)) return null;

            var candidates = new List<string>
            {
                Path.Combine(repositoryRoot, "Horizon.NpcCli", "bin", "Debug", "net10.0", "Horizon.NpcCli.exe"),
                Path.Combine(repositoryRoot, "Horizon.NpcCli", "bin", "Release", "net10.0", "Horizon.NpcCli.exe"),
            };

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

        public static void ResetCache() => _cachedExePath = null;

        private static string TryGetProjectDirectory()
        {
            try
            {
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
        /// <param name="arguments">动词与参数，如 <c>publish 400001</c>。</param>
        /// <param name="stdin">需要喂草稿 JSON 时传入（<c>validate -</c> / <c>save -</c> / <c>preview-* -</c>）。</param>
        public static NpcCliResult Run(string arguments, string stdin = null)
        {
            var result = new NpcCliResult();
            var exe = DetectExePath();
            if (string.IsNullOrEmpty(exe))
            {
                result.Ok = false;
                result.Message = "没找到 Horizon.NpcCli.exe。先在仓库根跑一次：dotnet build Horizon.NpcCli/Horizon.NpcCli.csproj（或在工具条里手填路径）。";
                return result;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = false,
                // 用 Encoding.UTF8 而不是 UTF8Encoding：编辑器脚本域的引用集里没有
                // System.Text.Encoding.Extensions（离线 csc 门会报 CS1069）。
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            try
            {
                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        result.Ok = false;
                        result.Message = "子进程启动失败（Process.Start 返回 null）。";
                        return result;
                    }

                    if (stdin != null)
                    {
                        process.StandardInput.Write(stdin);
                    }

                    process.StandardInput.Close();

                    result.Raw = process.StandardOutput.ReadToEnd();
                    result.StdErr = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    result.ExitCode = process.ExitCode;
                }
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.Message = "调用 CLI 失败：" + ex.GetType().Name + ": " + ex.Message;
                return result;
            }

            if (string.IsNullOrWhiteSpace(result.Raw))
            {
                result.Ok = false;
                result.Message = "CLI 没有输出（exit=" + result.ExitCode + "）。stderr: " + result.StdErr;
                return result;
            }

            try
            {
                using (var document = JsonDocument.Parse(result.Raw))
                {
                    var root = document.RootElement;
                    result.Ok = root.TryGetProperty("ok", out var ok) && ok.GetBoolean();
                    result.Verb = root.TryGetProperty("verb", out var verb) ? verb.GetString() : string.Empty;
                    result.Message = root.TryGetProperty("message", out var message) ? message.GetString() : string.Empty;
                    if (root.TryGetProperty("data", out var data) && data.ValueKind != JsonValueKind.Null)
                    {
                        result.Data = data.Clone();
                        result.HasData = true;
                    }
                }
            }
            catch (JsonException ex)
            {
                result.Ok = false;
                result.Message = "CLI 输出不是合法 JSON：" + ex.Message;
            }

            return result;
        }
    }
}
