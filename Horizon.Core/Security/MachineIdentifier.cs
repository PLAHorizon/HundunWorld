using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Horizon.Core.Security
{
    /// <summary>
    /// 跨平台机器唯一标识符工具类。
    /// 读取操作系统内置的机器GUID，支持 Windows、Linux 和 macOS。
    /// 结果在进程生命周期内缓存，不会重复读取文件或执行子进程。
    /// </summary>
    public static class MachineIdentifier
    {
        private static readonly Lazy<string> _machineGuid = new(ResolveGuid, isThreadSafe: true);

        /// <summary>
        /// 获取当前机器的唯一标识符（GUID 字符串，小写，不含花括号）。
        /// 在各平台的读取来源：
        /// <list type="bullet">
        ///   <item>Windows：注册表 HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid</item>
        ///   <item>Linux：/etc/machine-id（systemd）或 /var/lib/dbus/machine-id</item>
        ///   <item>macOS：sysctl -n kern.uuid（硬件 UUID）</item>
        ///   <item>其他平台/读取失败：主机名的 SHA-256 哈希值</item>
        /// </list>
        /// </summary>
        public static string GetMachineGuid() => _machineGuid.Value;

        private static string ResolveGuid()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                    return GetWindowsMachineGuid();

                if (OperatingSystem.IsLinux())
                    return GetLinuxMachineId();

                if (OperatingSystem.IsMacOS())
                    return GetMacOsMachineUuid();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MachineIdentifier] 读取系统机器ID失败，将使用主机名回退方案: {ex.Message}");
            }

            return HostnameFallback();
        }

        /// <summary>
        /// Windows：从注册表读取 MachineGuid。
        /// </summary>
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static string GetWindowsMachineGuid()
        {
#pragma warning disable CA1416
            using var key = Microsoft.Win32.Registry.LocalMachine
                .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");

            var value = key?.GetValue("MachineGuid")?.ToString();
            if (!string.IsNullOrWhiteSpace(value))
                return Normalize(value);
#pragma warning restore CA1416

            throw new InvalidOperationException("Windows 注册表中未找到 MachineGuid 值");
        }

        /// <summary>
        /// Linux：读取 /etc/machine-id 或 /var/lib/dbus/machine-id。
        /// </summary>
        private static string GetLinuxMachineId()
        {
            foreach (var path in new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" })
            {
                if (!File.Exists(path))
                    continue;

                var content = File.ReadAllText(path).Trim();
                if (!string.IsNullOrWhiteSpace(content))
                    return Normalize(content);
            }

            throw new InvalidOperationException("Linux machine-id 文件不存在或内容为空");
        }

        /// <summary>
        /// macOS：通过 sysctl 读取硬件 UUID。
        /// </summary>
        private static string GetMacOsMachineUuid()
        {
            var output = RunCommand("sysctl", "-n kern.uuid");
            if (!string.IsNullOrWhiteSpace(output))
                return Normalize(output.Trim());

            throw new InvalidOperationException("macOS sysctl kern.uuid 未返回有效值");
        }

        /// <summary>
        /// 降级方案：对主机名和机器域名求 SHA-256，以 GUID 形式表示。
        /// </summary>
        private static string HostnameFallback()
        {
            var seed = $"{Environment.MachineName}|{Environment.UserDomainName}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));

            // 取前16字节生成确定性 GUID
            var guidBytes = new byte[16];
            Array.Copy(hash, guidBytes, 16);
            // 设置版本4（随机）字段以符合 UUID 规范
            guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x40);
            guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
            return new Guid(guidBytes).ToString("D");
        }

        /// <summary>
        /// 规范化 GUID 字符串：去掉花括号、转为小写。
        /// </summary>
        private static string Normalize(string raw)
        {
            return raw.Trim('{', '}', ' ').ToLowerInvariant();
        }

        /// <summary>
        /// 执行外部命令并返回标准输出内容，超时时间 3 秒。
        /// </summary>
        private static string RunCommand(string executable, string arguments)
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = executable,
                        Arguments = arguments,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(3000);
                return output;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
