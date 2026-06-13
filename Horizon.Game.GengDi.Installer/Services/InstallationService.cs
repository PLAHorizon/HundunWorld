using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Horizon.Game.GengDi.Installer.Services
{
    /// <summary>
    /// 安装服务：负责 .NET 10 检测/安装、应用文件复制、快捷方式创建和卸载项注册。
    /// </summary>
    public static class InstallationService
    {
        // .NET 10 运行时下载地址（当正式版发布后更新版本号）
        // aka.ms 短链始终指向最新补丁版本
        private const string DotNet10DownloadUrl =
            "https://aka.ms/dotnet/10.0/dotnet-runtime-win-x64.exe";

        // 应用主 EXE 名称（与 GengDi.PC 项目输出名一致）
        private const string AppExeName = "GengDi.exe";

        // ══════════════════════════════════════════════════════
        // .NET 10 检测
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 检测当前机器是否已安装 .NET 10 运行时。
        /// 通过检查 %ProgramFiles%\dotnet\shared\Microsoft.NETCore.App\10.* 目录实现。
        /// </summary>
        public static bool IsDotNet10Installed()
        {
            string runtimeBase = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "dotnet", "shared", "Microsoft.NETCore.App");

            if (!Directory.Exists(runtimeBase))
                return false;

            return Directory.GetDirectories(runtimeBase, "10.*").Length > 0;
        }

        // ══════════════════════════════════════════════════════
        // .NET 10 静默安装
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 下载并静默安装 .NET 10 运行时。
        /// 安装需要管理员权限，会触发 UAC 弹窗。
        /// </summary>
        public static async Task InstallDotNet10Async(
            IProgress<(string Message, double Percent)> progress)
        {
            string tempFile = Path.Combine(
                Path.GetTempPath(), "dotnet-runtime-10-win-x64.exe");

            try
            {
                // 阶段 1：下载（占 0–85%）
                progress.Report(("正在下载 .NET 运行环境 (0%)...", 0.0));

                using (var client = new WebClient())
                {
                    client.DownloadProgressChanged += (_, e) =>
                        progress.Report((
                            $"正在下载 .NET 运行环境 ({e.ProgressPercentage}%)...",
                            e.ProgressPercentage * 0.85 / 100.0));

                    await client.DownloadFileTaskAsync(
                        new Uri(DotNet10DownloadUrl), tempFile);
                }

                // 阶段 2：安装前校验 Authenticode 签名，防止供应链攻击
                progress.Report(("正在验证安装包签名...", 0.86));
                VerifyAuthenticodeSignature(tempFile);

                // 阶段 3：安装（占 86–100%）
                progress.Report(("正在安装 .NET 运行环境...", 0.88));

                var psi = new ProcessStartInfo(tempFile)
                {
                    Arguments        = "/install /quiet /norestart",
                    UseShellExecute  = true,
                    Verb             = "runas",  // 请求管理员权限
                    CreateNoWindow   = true,
                    WindowStyle      = ProcessWindowStyle.Hidden
                };

                var proc = Process.Start(psi)
                    ?? throw new InvalidOperationException(".NET 安装程序启动失败");
                await Task.Run(() => proc.WaitForExit());

                if (proc.ExitCode != 0)
                    throw new InvalidOperationException(
                        $".NET 安装程序返回错误代码 {proc.ExitCode}");

                progress.Report((".NET 运行环境安装完成", 1.0));
            }
            finally
            {
                // 清理临时文件
                try { if (File.Exists(tempFile)) File.Delete(tempFile); }
                catch { /* 删除失败不阻断流程 */ }
            }
        }

        // ══════════════════════════════════════════════════════
        // Authenticode 签名校验（WinVerifyTrust）
        // ══════════════════════════════════════════════════════

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustFileInfo
        {
            public uint   cbStruct;
            public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WinTrustData
        {
            public uint   cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint   dwUIChoice;       // 2 = WTD_UI_NONE
            public uint   fdwRevocationChecks;
            public uint   dwUnionChoice;    // 1 = WTD_CHOICE_FILE
            public IntPtr pFile;
            public uint   dwStateAction;    // 0 = WTD_STATEACTION_IGNORE
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint   dwProvFlags;      // 0x1040 = OFFLINE_CHECK | CACHE_ONLY_URL_RETRIEVAL
            public uint   dwUIContext;
        }

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
        private static extern uint WinVerifyTrust(
            IntPtr hwnd,
            ref Guid pgActionID,
            ref WinTrustData pWVTData);

        private static readonly Guid WintrustActionGenericVerifyV2 =
            new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        /// <summary>
        /// 验证文件的 Authenticode 数字签名（需要微软根证书信任链）。
        /// 校验失败时抛出 <see cref="InvalidOperationException"/>。
        /// </summary>
        private static void VerifyAuthenticodeSignature(string filePath)
        {
            var fileInfo = new WinTrustFileInfo
            {
                cbStruct     = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                pcwszFilePath = filePath,
                hFile        = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero
            };

            IntPtr pFileInfo = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, pFileInfo, false);

            var trustData = new WinTrustData
            {
                cbStruct        = (uint)Marshal.SizeOf<WinTrustData>(),
                dwUIChoice      = 2,    // WTD_UI_NONE
                fdwRevocationChecks = 1, // WTD_REVOKE_WHOLECHAIN
                dwUnionChoice   = 1,    // WTD_CHOICE_FILE
                pFile           = pFileInfo,
                dwStateAction   = 0,    // WTD_STATEACTION_IGNORE
                dwProvFlags     = 0x1000 // WTD_CACHE_ONLY_URL_RETRIEVAL（使用缓存吊销列表）
            };

            Guid actionId = WintrustActionGenericVerifyV2;
            uint result;
            try
            {
                result = WinVerifyTrust(IntPtr.Zero, ref actionId, ref trustData);
            }
            finally
            {
                Marshal.FreeHGlobal(pFileInfo);
            }

            if (result != 0) // 0 = ERROR_SUCCESS
                throw new InvalidOperationException(
                    $"下载文件签名验证失败（错误码：0x{result:X8}）。文件可能已被篡改，安装已中止。");
        }

        // ══════════════════════════════════════════════════════
        // 卸载
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 执行卸载流程：删除应用文件、快捷方式及注册表卸载条目。
        /// </summary>
        public static async Task UninstallAsync(
            string installPath,
            IProgress<(string Message, double Percent)> progress)
        {
            await Task.Run(() =>
            {
                // 步骤 1：移除桌面快捷方式
                progress.Report(("正在删除快捷方式...", 0.1));
                TryDeleteShortcut(
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                        "耕地.lnk"));

                // 步骤 2：移除开始菜单快捷方式
                string startDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                    "Programs", "耕地");
                TryDeleteShortcut(Path.Combine(startDir, "耕地.lnk"));
                try { Directory.Delete(startDir); } catch { /* 目录非空时忽略 */ }

                // 步骤 3：删除注册表卸载条目
                progress.Report(("正在清理注册表...", 0.3));
                const string keyPath =
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\GengDi";
                try { Registry.LocalMachine.DeleteSubKey(keyPath, throwOnMissingSubKey: false); }
                catch { /* 权限不足则忽略 */ }
                try { Registry.CurrentUser.DeleteSubKey(keyPath, throwOnMissingSubKey: false); }
                catch { /* 忽略 */ }

                // 步骤 4：删除应用文件
                progress.Report(("正在删除应用文件...", 0.5));
                if (Directory.Exists(installPath))
                {
                    string[] files = Directory.GetFiles(
                        installPath, "*", SearchOption.AllDirectories);
                    for (int i = 0; i < files.Length; i++)
                    {
                        try { File.Delete(files[i]); } catch { /* 部分文件锁定时跳过 */ }
                        progress.Report((
                            $"正在删除... {Path.GetFileName(files[i])}",
                            0.5 + 0.45 * (double)(i + 1) / files.Length));
                    }
                    // 删除空目录
                    try { Directory.Delete(installPath, recursive: true); } catch { /* 忽略 */ }
                }

                progress.Report(("卸载完成！", 1.0));
            });
        }

        private static void TryDeleteShortcut(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* 忽略 */ }
        }


        /// <summary>
        /// 将安装程序同目录下的 payload 文件夹（或当前目录）中的应用文件
        /// 复制到用户选择的安装路径。文件复制在后台线程中执行。
        /// </summary>
        public static async Task InstallApplicationAsync(
            string installPath,
            IProgress<(string Message, double Percent)> progress)
        {
            // 源文件夹：优先 payload 子目录，否则使用安装程序所在目录
            string exeDir = Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location)!;
            string payloadDir = Path.Combine(exeDir, "payload");
            string sourceDir  = Directory.Exists(payloadDir) ? payloadDir : exeDir;

            Directory.CreateDirectory(installPath);

            // 收集需要复制的文件，排除安装程序自身
            string[] allFiles = Directory.GetFiles(
                sourceDir, "*", SearchOption.AllDirectories);

            // 缓存安装程序文件名，避免在 LINQ 查询中重复调用 Path.GetFileName
            const string setupExeName = "GengDi.Setup.exe";
            var filesToCopy = allFiles
                .Where(f => !string.Equals(
                    Path.GetFileName(f), setupExeName,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (filesToCopy.Length == 0)
            {
                progress.Report(("未找到应用文件，跳过复制步骤", 1.0));
                return;
            }

            // 在后台线程执行同步文件复制，避免阻塞 UI 线程。
            // Progress<T> 捕获了 UI 线程的 SynchronizationContext，
            // 所以 progress.Report 会自动回到 UI 线程更新界面。
            await Task.Run(() =>
            {
                for (int i = 0; i < filesToCopy.Length; i++)
                {
                    string srcFile      = filesToCopy[i];
                    string relativePath = MakeRelative(sourceDir, srcFile);
                    string destFile     = Path.Combine(installPath, relativePath);
                    string destDir      = Path.GetDirectoryName(destFile)!;

                    Directory.CreateDirectory(destDir);
                    File.Copy(srcFile, destFile, overwrite: true);

                    double pct = (double)(i + 1) / filesToCopy.Length;
                    progress.Report((
                        $"正在安装... {Path.GetFileName(srcFile)}", pct));
                }
            });
        }

        // ══════════════════════════════════════════════════════
        // 快捷方式
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 创建桌面和开始菜单快捷方式。
        /// </summary>
        public static void CreateShortcuts(string installPath)
        {
            string exePath = Path.Combine(installPath, AppExeName);

            // 桌面
            string desktopDir = Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory);
            CreateShortcut(
                Path.Combine(desktopDir, "耕地.lnk"),
                exePath, installPath, "耕地 – 游戏发现与启动平台");

            // 开始菜单
            string startDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                "Programs", "耕地");
            Directory.CreateDirectory(startDir);
            CreateShortcut(
                Path.Combine(startDir, "耕地.lnk"),
                exePath, installPath, "耕地 – 游戏发现与启动平台");
        }

        // ══════════════════════════════════════════════════════
        // 卸载注册
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 在注册表"控制面板 → 程序和功能"中注册卸载条目。
        /// 优先写入 HKLM（需管理员），失败则写入 HKCU。
        /// </summary>
        public static void RegisterUninstallEntry(
            string installPath,
            string version = "1.0.0")
        {
            const string keyPath =
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\GengDi";

            string uninstaller = Path.Combine(installPath, "GengDi.Setup.exe");
            string appExe      = Path.Combine(installPath, AppExeName);

            RegistryKey? key = null;
            try
            {
                key = Registry.LocalMachine.CreateSubKey(keyPath);
            }
            catch
            {
                // 没有管理员权限则写入当前用户
                key = Registry.CurrentUser.CreateSubKey(keyPath);
            }

            using (key)
            {
                if (key == null) return;
                key.SetValue("DisplayName",      "耕地 游戏中心");
                key.SetValue("DisplayVersion",   version);
                key.SetValue("Publisher",        "HundunWorld");
                key.SetValue("InstallLocation",  installPath);
                key.SetValue("DisplayIcon",      appExe);
                key.SetValue("UninstallString",  $"\"{uninstaller}\" /uninstall");
                key.SetValue("NoModify",  1, RegistryValueKind.DWord);
                key.SetValue("NoRepair",  1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", EstimateSizeKb(installPath),
                    RegistryValueKind.DWord);
            }
        }

        // ══════════════════════════════════════════════════════
        // 内部工具方法
        // ══════════════════════════════════════════════════════

        private static void CreateShortcut(
            string shortcutPath, string targetPath,
            string workingDir,   string description)
        {
            // 通过 WScript.Shell COM 对象创建 .lnk 文件
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;

            dynamic shell    = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath       = targetPath;
            shortcut.WorkingDirectory = workingDir;
            shortcut.Description      = description;
            shortcut.Save();

            Marshal.ReleaseComObject(shortcut);
            Marshal.ReleaseComObject(shell);
        }

        /// <summary>计算目录大小（KB），用于注册表 EstimatedSize 字段。</summary>
        private static int EstimateSizeKb(string dir)
        {
            try
            {
                long bytes = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                    .Sum(f => new FileInfo(f).Length);
                return (int)(bytes / 1024);
            }
            catch { return 0; }
        }

        /// <summary>获取相对路径（兼容 .NET Framework 4.8）。</summary>
        private static string MakeRelative(string baseDir, string fullPath)
        {
            if (!baseDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
                baseDir += Path.DirectorySeparatorChar;
            return fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase)
                ? fullPath.Substring(baseDir.Length)
                : Path.GetFileName(fullPath);
        }
    }
}
