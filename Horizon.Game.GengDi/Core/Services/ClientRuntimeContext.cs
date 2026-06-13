using System;
using System.IO;

using Horizon.Game.GengDi.Core.Services.Database;

namespace Horizon.Game.GengDi.Core.Services
{
    public static class ClientRuntimeContext
    {
        private const string InstanceArgumentPrefix = "--instance=";
        private const string InstanceEnvironmentVariable = "HUNDUNWORLD_INSTANCE";
        private const string SharedRootFolderName = "HundunWorld";
        private const string PrimaryInstanceSlotName = "primary";
        private const string InstanceLockFolderName = "InstanceLocks";

        private static string _instanceTag = string.Empty;
        private static FileStream _instanceLockHandle;

        public static string InstanceTag => _instanceTag;

        public static bool HasInstanceIsolation => !string.IsNullOrWhiteSpace(_instanceTag);

        public static void Initialize(string[] args)
        {
            _instanceTag = ResolveEffectiveInstanceTag(SanitizeInstanceTag(ResolveInstanceTag(args)));

            LocalPassportStore.DbDirectoryOverride = HasInstanceIsolation
                ? ResolveSharedConfigDirectory()
                : null;

            if (HasInstanceIsolation)
            {
                LiteDataContext.SetDatabasePath(ResolveProductDataDirectory("HorizonGame"));
            }
        }

        public static string ResolveProductDataDirectory(string defaultFolderName)
        {
            if (string.IsNullOrWhiteSpace(defaultFolderName))
            {
                throw new ArgumentException("默认目录名不能为空。", nameof(defaultFolderName));
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!HasInstanceIsolation)
            {
                return Path.Combine(localAppData, defaultFolderName);
            }

            return Path.Combine(localAppData, SharedRootFolderName, "Instances", _instanceTag, defaultFolderName);
        }

        private static string ResolveSharedConfigDirectory()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, SharedRootFolderName, "Instances", _instanceTag);
        }

        private static string ResolveInstanceTag(string[] args)
        {
            foreach (var arg in args ?? Array.Empty<string>())
            {
                if (arg.StartsWith(InstanceArgumentPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return arg.Substring(InstanceArgumentPrefix.Length).Trim();
                }
            }

            return Environment.GetEnvironmentVariable(InstanceEnvironmentVariable)?.Trim() ?? string.Empty;
        }

        private static string ResolveEffectiveInstanceTag(string requestedInstanceTag)
        {
            if (!string.IsNullOrWhiteSpace(requestedInstanceTag))
            {
                return ReserveInstanceTagOrVariant(requestedInstanceTag);
            }

            if (TryReserveInstanceSlot(PrimaryInstanceSlotName))
            {
                return string.Empty;
            }

            return ReserveInstanceTagOrVariant($"instance-{Environment.ProcessId}");
        }

        private static string ReserveInstanceTagOrVariant(string instanceTag)
        {
            var candidate = instanceTag;
            var suffix = 1;

            while (!TryReserveInstanceSlot(candidate))
            {
                candidate = $"{instanceTag}-{suffix++}";
            }

            return candidate;
        }

        private static bool TryReserveInstanceSlot(string slotName)
        {
            if (_instanceLockHandle != null)
            {
                return true;
            }

            try
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var lockDirectory = Path.Combine(localAppData, SharedRootFolderName, InstanceLockFolderName);
                Directory.CreateDirectory(lockDirectory);

                var lockPath = Path.Combine(lockDirectory, $"{slotName}.lock");
                _instanceLockHandle = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static string SanitizeInstanceTag(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = value.Trim();

            foreach (var invalidChar in invalidChars)
            {
                sanitized = sanitized.Replace(invalidChar, '_');
            }

            return sanitized;
        }
    }
}