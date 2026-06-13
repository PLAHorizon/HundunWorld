using System;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Styling;
using Horizon.Game.GengDi.Core.Services.Database;
using LiteDB;

namespace Horizon.Game.GengDi.Core.Services;

public enum AppThemePreference
{
    System,
    Light,
    Dark
}

public enum GameInstallPathMode
{
    Default,
    Custom
}

public sealed class ClientAppSettings
{
    public AppThemePreference ThemePreference { get; set; } = AppThemePreference.System;

    public GameInstallPathMode InstallPathMode { get; set; } = GameInstallPathMode.Default;

    public string CustomInstallPath { get; set; } = string.Empty;

    public int MaxConcurrentDownloads { get; set; } = 2;

    public long DownloadSpeedLimit { get; set; }

    /// <summary>
    /// 游戏更新检查服务端 URL（<see cref="UpdateService"/> 使用）。为空时更新检查返回空列表。
    /// 实际请求会追加 <c>/{gameId}?from={currentVersion}</c>。
    /// </summary>
    public string UpdateCheckUrl { get; set; } = string.Empty;

    public string WeatherApiKey { get; set; } = string.Empty;

    public string AmapApiKey { get; set; } = string.Empty;

    public string ResolveInstallRoot()
    {
        if (InstallPathMode == GameInstallPathMode.Custom && !string.IsNullOrWhiteSpace(CustomInstallPath))
        {
            return CustomInstallPath.Trim();
        }

        return AppSettingsService.GetDefaultInstallDirectory();
    }
}

internal sealed class AppSettingsRecord
{
    public int Id { get; set; } = 1;

    public string ThemePreference { get; set; } = AppThemePreference.System.ToString();

    public string InstallPathMode { get; set; } = GameInstallPathMode.Default.ToString();

    public string CustomInstallPath { get; set; } = string.Empty;

    public int MaxConcurrentDownloads { get; set; } = 2;

    public long DownloadSpeedLimit { get; set; }

    public string UpdateCheckUrl { get; set; } = string.Empty;

    public string WeatherApiKey { get; set; } = string.Empty;

    public string AmapApiKey { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AppSettingsService
{
    private const string CollectionName = "app_settings";
    private const int SettingsRecordId = 1;
    private static readonly Lazy<AppSettingsService> s_instance = new(() => new AppSettingsService());
    private readonly object _lock = new();
    private ClientAppSettings _currentSettings;

    public static AppSettingsService Instance => s_instance.Value;

    public ClientAppSettings CurrentSettings => _currentSettings ??= LoadSettings();

    public Task<ClientAppSettings> LoadSettingsAsync()
    {
        return ClientAsyncDispatcher.RunConfigAsync(LoadSettings);
    }

    public ClientAppSettings LoadSettings()
    {
        lock (_lock)
        {
            try
            {
                using var db = OpenDatabase();
                var collection = db.GetCollection<AppSettingsRecord>(CollectionName);
                var record = collection.FindById(SettingsRecordId);

                _currentSettings = record == null
                    ? CreateDefaultSettings()
                    : new ClientAppSettings
                    {
                        ThemePreference = ParseThemePreference(record.ThemePreference),
                        InstallPathMode = ParseInstallPathMode(record.InstallPathMode, record.CustomInstallPath),
                        CustomInstallPath = record.CustomInstallPath ?? string.Empty,
                        MaxConcurrentDownloads = Math.Max(1, record.MaxConcurrentDownloads),
                        DownloadSpeedLimit = Math.Max(0, record.DownloadSpeedLimit),
                        UpdateCheckUrl = record.UpdateCheckUrl ?? string.Empty,
                        WeatherApiKey = record.WeatherApiKey ?? string.Empty,
                        AmapApiKey = record.AmapApiKey ?? string.Empty
                    };

                _currentSettings = MergeConfigurationDefaults(_currentSettings);
                return CloneSettings(_currentSettings);
            }
            catch
            {
                _currentSettings = CreateDefaultSettings();
                return CloneSettings(_currentSettings);
            }
        }
    }

    public void SaveSettings(ClientAppSettings settings)
    {
        lock (_lock)
        {
            var normalized = NormalizeSettings(settings);

            try
            {
                EnsureDirectory();

                using var db = OpenDatabase();
                var collection = db.GetCollection<AppSettingsRecord>(CollectionName);
                collection.Upsert(new AppSettingsRecord
                {
                    Id = SettingsRecordId,
                    ThemePreference = normalized.ThemePreference.ToString(),
                    InstallPathMode = normalized.InstallPathMode.ToString(),
                    CustomInstallPath = normalized.CustomInstallPath,
                    MaxConcurrentDownloads = normalized.MaxConcurrentDownloads,
                    DownloadSpeedLimit = normalized.DownloadSpeedLimit,
                    UpdateCheckUrl = normalized.UpdateCheckUrl,
                    WeatherApiKey = normalized.WeatherApiKey,
                    AmapApiKey = normalized.AmapApiKey,
                    UpdatedAt = DateTime.UtcNow
                });

                _currentSettings = normalized;
            }
            catch
            {
                _currentSettings = normalized;
            }
        }
    }

    public Task SaveSettingsAsync(ClientAppSettings settings)
    {
        return ClientAsyncDispatcher.RunConfigAsync(() => SaveSettings(settings));
    }

    public void ApplyThemePreference(Application application = null)
    {
        var targetApplication = application ?? Application.Current;
        if (targetApplication == null)
        {
            return;
        }

        targetApplication.RequestedThemeVariant = CurrentSettings.ThemePreference switch
        {
            AppThemePreference.Light => ThemeVariant.Light,
            AppThemePreference.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    public async Task ApplyThemePreferenceAsync(Application application = null)
    {
        var settings = await LoadSettingsAsync().ConfigureAwait(true);
        var targetApplication = application ?? Application.Current;
        if (targetApplication == null)
        {
            return;
        }

        targetApplication.RequestedThemeVariant = settings.ThemePreference switch
        {
            AppThemePreference.Light => ThemeVariant.Light,
            AppThemePreference.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    public string GetResolvedInstallDirectory()
    {
        return CurrentSettings.ResolveInstallRoot();
    }

    public async Task<string> GetResolvedInstallDirectoryAsync()
    {
        var settings = await LoadSettingsAsync().ConfigureAwait(false);
        return settings.ResolveInstallRoot();
    }

    public static string GetDefaultInstallDirectory()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HorizonGames");
    }

    private static ClientAppSettings NormalizeSettings(ClientAppSettings settings)
    {
        settings ??= CreateDefaultSettings();

        return new ClientAppSettings
        {
            ThemePreference = settings.ThemePreference,
            InstallPathMode = settings.InstallPathMode,
            CustomInstallPath = (settings.CustomInstallPath ?? string.Empty).Trim(),
            MaxConcurrentDownloads = Math.Max(1, settings.MaxConcurrentDownloads),
            DownloadSpeedLimit = Math.Max(0, settings.DownloadSpeedLimit),
            UpdateCheckUrl = (settings.UpdateCheckUrl ?? string.Empty).Trim(),
            WeatherApiKey = (settings.WeatherApiKey ?? string.Empty).Trim(),
            AmapApiKey = (settings.AmapApiKey ?? string.Empty).Trim()
        };
    }

    private static ClientAppSettings CloneSettings(ClientAppSettings settings)
    {
        return new ClientAppSettings
        {
            ThemePreference = settings.ThemePreference,
            InstallPathMode = settings.InstallPathMode,
            CustomInstallPath = settings.CustomInstallPath,
            MaxConcurrentDownloads = settings.MaxConcurrentDownloads,
            DownloadSpeedLimit = settings.DownloadSpeedLimit,
            UpdateCheckUrl = settings.UpdateCheckUrl,
            WeatherApiKey = settings.WeatherApiKey,
            AmapApiKey = settings.AmapApiKey
        };
    }

    private static ClientAppSettings CreateDefaultSettings()
    {
        var settings = new ClientAppSettings();
        return MergeConfigurationDefaults(settings);
    }

    private static ClientAppSettings MergeConfigurationDefaults(ClientAppSettings settings)
    {
        var merged = new ClientAppSettings
        {
            ThemePreference = settings.ThemePreference,
            InstallPathMode = settings.InstallPathMode,
            CustomInstallPath = settings.CustomInstallPath,
            MaxConcurrentDownloads = settings.MaxConcurrentDownloads,
            DownloadSpeedLimit = settings.DownloadSpeedLimit,
            UpdateCheckUrl = string.IsNullOrWhiteSpace(settings.UpdateCheckUrl)
                ? LoadAppSettingsValue("UpdateCheckUrl")
                : settings.UpdateCheckUrl,
            WeatherApiKey = string.IsNullOrWhiteSpace(settings.WeatherApiKey)
                ? LoadAppSettingsValue("WeatherApiKey")
                : settings.WeatherApiKey,
            AmapApiKey = string.IsNullOrWhiteSpace(settings.AmapApiKey)
                ? LoadAppSettingsValue("Amap", "ApiKey")
                : settings.AmapApiKey
        };

        return merged;
    }

    private static string LoadAppSettingsValue(string sectionName, string keyName = null)
    {
        try
        {
            var filePath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (!File.Exists(filePath))
            {
                return string.Empty;
            }

            using var stream = File.OpenRead(filePath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;

            if (string.IsNullOrWhiteSpace(keyName))
            {
                if (root.TryGetProperty(sectionName, out var valueElement) && valueElement.ValueKind == JsonValueKind.String)
                {
                    return valueElement.GetString() ?? string.Empty;
                }
            }
            else if (root.TryGetProperty(sectionName, out var sectionElement) && sectionElement.ValueKind == JsonValueKind.Object)
            {
                if (sectionElement.TryGetProperty(keyName, out var valueElement) && valueElement.ValueKind == JsonValueKind.String)
                {
                    return valueElement.GetString() ?? string.Empty;
                }
            }
        }
        catch
        {
            // Ignore malformed or missing configuration file.
        }

        return string.Empty;
    }

    private static AppThemePreference ParseThemePreference(string value)
    {
        return Enum.TryParse<AppThemePreference>(value, true, out var parsed)
            ? parsed
            : AppThemePreference.System;
    }

    private static GameInstallPathMode ParseInstallPathMode(string value, string customInstallPath)
    {
        if (!Enum.TryParse<GameInstallPathMode>(value, true, out var parsed))
        {
            return GameInstallPathMode.Default;
        }

        if (parsed == GameInstallPathMode.Custom && string.IsNullOrWhiteSpace(customInstallPath))
        {
            return GameInstallPathMode.Default;
        }

        return parsed;
    }

    private static void EnsureDirectory()
    {
        var directory = GetDbDirectory();
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static LiteDatabase OpenDatabase()
    {
        return new LiteDatabase(new ConnectionString
        {
            Filename = GetDbPath(),
            Connection = ConnectionType.Shared
        });
    }

    private static string GetDbPath()
    {
        return Path.Combine(GetDbDirectory(), "client_config.db");
    }

    private static string GetDbDirectory()
    {
        return !string.IsNullOrWhiteSpace(LocalPassportStore.DbDirectoryOverride)
            ? LocalPassportStore.DbDirectoryOverride
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HundunWorld");
    }
}