using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Horizon.Game.GengDi.Core.Services;
using LiteDB;

namespace Horizon.Game.GengDi.Core.Services.Database
{
    internal sealed class PassportRecord
    {
        public int Id { get; set; } = 1;
        public string PassportId { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool RememberPassword { get; set; }
        public DateTime SavedAt { get; set; }
    }

    public static class LocalPassportStore
    {
        private const string CollectionName = "passports";
        private const int RecordId = 1;

        private static string _dbDirectoryOverride;

        internal static string DbDirectoryOverride
        {
            get => _dbDirectoryOverride;
            set => _dbDirectoryOverride = value;
        }

        private static string GetDbDirectory() =>
            _dbDirectoryOverride
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HundunWorld");

        private static string GetDbPath() =>
            Path.Combine(GetDbDirectory(), "client_config.db");

        private static readonly object _lock = new();

        public static void SavePassport(string passportId, string password)
            => SavePassport(passportId, password, true);

        public static Task SavePassportAsync(string passportId, string password)
            => SavePassportAsync(passportId, password, true);

        public static void SavePassport(string passportId, string password, bool rememberPassword)
        {
            if (!rememberPassword)
            {
                ClearPassport();
                return;
            }

            if (string.IsNullOrEmpty(passportId))
                return;

            lock (_lock)
            {
                try
                {
                    var dir = GetDbDirectory();
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    using var db = OpenDatabase();
                    var col = db.GetCollection<PassportRecord>(CollectionName);
                    col.Upsert(new PassportRecord
                    {
                        Id = RecordId,
                        PassportId = passportId,
                        Password = password ?? string.Empty,
                        RememberPassword = true,
                        SavedAt = DateTime.UtcNow
                    });
                }
                catch (Exception ex)
                {
                    Debug.Print($"[LocalPassportStore] SavePassport failed: {ex.Message}");
                }
            }
        }

        public static Task SavePassportAsync(string passportId, string password, bool rememberPassword)
        {
            return ClientAsyncDispatcher.RunConfigAsync(() => SavePassport(passportId, password, rememberPassword));
        }

        public static bool TryLoadPassport(out string passportId, out string password)
        {
            passportId = string.Empty;
            password = string.Empty;

            lock (_lock)
            {
                try
                {
                    var dbPath = GetDbPath();
                    if (!File.Exists(dbPath))
                        return false;

                    using var db = OpenDatabase();
                    var col = db.GetCollection<PassportRecord>(CollectionName);
                    var record = col.FindById(RecordId);
                    if (record == null || !record.RememberPassword || string.IsNullOrEmpty(record.PassportId))
                        return false;

                    passportId = record.PassportId;
                    password = record.Password;
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.Print($"[LocalPassportStore] TryLoadPassport failed: {ex.Message}");
                    return false;
                }
            }
        }

        public static async Task<(bool Success, string PassportId, string Password)> TryLoadPassportAsync()
        {
            return await ClientAsyncDispatcher.RunConfigAsync(() =>
            {
                var success = TryLoadPassport(out var passportId, out var password);
                return (success, passportId, password);
            }).ConfigureAwait(false);
        }

        public static void ClearPassport()
        {
            lock (_lock)
            {
                try
                {
                    var dbPath = GetDbPath();
                    if (!File.Exists(dbPath))
                        return;

                    using var db = OpenDatabase();
                    var col = db.GetCollection<PassportRecord>(CollectionName);
                    col.Delete(RecordId);
                }
                catch (Exception ex)
                {
                    Debug.Print($"[LocalPassportStore] ClearPassport failed: {ex.Message}");
                }
            }
        }

        public static Task ClearPassportAsync()
        {
            return ClientAsyncDispatcher.RunConfigAsync(ClearPassport);
        }

        private static LiteDatabase OpenDatabase()
            => new LiteDatabase(new ConnectionString { Filename = GetDbPath(), Connection = ConnectionType.Shared });
    }
}
