using System;
using System.IO;
using System.Reflection;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Core.Services.Database;

namespace Horizon.Game.GengDi.Tests.Persistence;

public sealed class ClientPersistenceTests
{
    [Fact]
    public void AppSettingsService_RoundTrips_ClientPreferences()
    {
        using var scope = new TestDbScope();

        var settings = new ClientAppSettings
        {
            ThemePreference = AppThemePreference.Dark,
            InstallPathMode = GameInstallPathMode.Custom,
            CustomInstallPath = Path.Combine(scope.DirectoryPath, "Games"),
            MaxConcurrentDownloads = 4,
            DownloadSpeedLimit = 2048
        };

        AppSettingsService.Instance.SaveSettings(settings);

        var loaded = AppSettingsService.Instance.LoadSettings();

        Assert.Equal(AppThemePreference.Dark, loaded.ThemePreference);
        Assert.Equal(GameInstallPathMode.Custom, loaded.InstallPathMode);
        Assert.Equal(settings.CustomInstallPath, loaded.CustomInstallPath);
        Assert.Equal(4, loaded.MaxConcurrentDownloads);
        Assert.Equal(2048, loaded.DownloadSpeedLimit);
    }

    [Fact]
    public void LocalPassportStore_SaveLoadAndClear_Works()
    {
        using var scope = new TestDbScope();

        LocalPassportStore.SavePassport("tester", "secret", true);

        var loaded = LocalPassportStore.TryLoadPassport(out var username, out var password);

        Assert.True(loaded);
        Assert.Equal("tester", username);
        Assert.Equal("secret", password);

        LocalPassportStore.ClearPassport();

        Assert.False(LocalPassportStore.TryLoadPassport(out _, out _));
    }

    [Fact]
    public void LocalPassportStore_DoesNotPersist_WhenRememberLoginDisabled()
    {
        using var scope = new TestDbScope();

        LocalPassportStore.SavePassport("tester", "secret", false);

        Assert.False(LocalPassportStore.TryLoadPassport(out _, out _));
    }

    private sealed class TestDbScope : IDisposable
    {
        private static readonly PropertyInfo OverrideProperty = typeof(LocalPassportStore)
            .GetProperty("DbDirectoryOverride", BindingFlags.Static | BindingFlags.NonPublic)!;

        private readonly string? _originalValue;

        public TestDbScope()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "Horizon.Game.GengDi.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            _originalValue = OverrideProperty.GetValue(null) as string;
            OverrideProperty.SetValue(null, DirectoryPath);
        }

        public string DirectoryPath { get; }

        public void Dispose()
        {
            OverrideProperty.SetValue(null, _originalValue);

            if (Directory.Exists(DirectoryPath))
            {
                try
                {
                    Directory.Delete(DirectoryPath, true);
                }
                catch
                {
                }
            }
        }
    }
}