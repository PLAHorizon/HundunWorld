using System;
using System.Collections.Generic;
using System.IO;
using Horizon.Game.GengDi.Models;
using Horizon.Game.GengDi.Enums;

namespace Horizon.Game.GengDi.Tests.Persistence;

public sealed class CurrentUserAvatarRefreshTests
{
    [Fact]
    public void App_CurrentUser_RaisesChangedEvent_WhenProfileIsReplaced()
    {
        var raised = 0;

        void Handler(object? sender, EventArgs args)
        {
            raised++;
        }

        App.CurrentUserChanged += Handler;

        try
        {
            App.CurrentUser = new User { Id = "u1", Username = "OldName" };
            App.CurrentUser = new User { Id = "u1", Username = "NewName", Avatar = "avatar.png" };

            Assert.True(raised >= 2);
            Assert.Equal("NewName", App.CurrentUser?.Username);
            Assert.Equal("avatar.png", App.CurrentUser?.Avatar);
        }
        finally
        {
            App.CurrentUserChanged -= Handler;
            App.CurrentUser = null;
        }
    }

    [Fact]
    public void App_CurrentUser_RaisesChangedEvent_WhenAvatarMutatesInPlace()
    {
        var raised = 0;
        var user = new User { Id = "u2", Username = "AvatarUser" };

        void Handler(object? sender, EventArgs args)
        {
            raised++;
        }

        App.CurrentUserChanged += Handler;

        try
        {
            App.CurrentUser = user;
            raised = 0;

            user.Avatar = "avatar2.png";

            Assert.True(raised >= 1);
            Assert.Equal("avatar2.png", App.CurrentUser?.Avatar);
        }
        finally
        {
            App.CurrentUserChanged -= Handler;
            App.CurrentUser = null;
        }
    }

    [Fact]
    public void User_AvatarBioAndStatus_RaisePropertyChanged_ForAvatarDisplays()
    {
        var user = new User { Username = "Alice" };
        var changed = new List<string>();
        user.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

        user.Avatar = GetExistingAvatarFixturePath();
        user.Bio = "updated bio";
        user.Status = UserStatus.Away;

        Assert.Contains(nameof(User.Avatar), changed);
        Assert.Contains(nameof(User.HasAvatar), changed);
        Assert.Contains(nameof(User.AvatarImage), changed);
        Assert.Contains(nameof(User.HasAvatarImage), changed);
        Assert.Contains(nameof(User.Bio), changed);
        Assert.Contains(nameof(User.SocialSummary), changed);
        Assert.Contains(nameof(User.Status), changed);
        Assert.Contains(nameof(User.DisplayStatus), changed);
        Assert.Contains(nameof(User.IsAvailable), changed);
    }

    [Fact]
    public void User_AvatarImage_LoadsBitmap_FromLocalFile()
    {
        var avatarPath = GetExistingAvatarFixturePath();
        var user = new User { Username = "BitmapUser", Avatar = avatarPath };

        Assert.True(File.Exists(avatarPath));
        Assert.True(user.HasAvatar);
        Assert.True(user.HasAvatarImage);
        Assert.NotNull(user.AvatarImage);
    }

    private static string GetExistingAvatarFixturePath()
    {
        var workingDirectoryPath = Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            "Horizon.Game.GengDi",
            "Assets",
            "Images",
            "Github.png"));

        if (File.Exists(workingDirectoryPath))
        {
            return workingDirectoryPath;
        }

        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "Horizon.Game.GengDi",
            "Assets",
            "Images",
            "Github.png"));
    }
}
