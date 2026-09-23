using System;
using Horizon.Game.GengDi;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Models;
using Xunit;

namespace Horizon.Game.GengDi.Tests.Services;

/// <summary>
/// D1 — <see cref="UserSession"/>（IUserSession 门面封装）契约测试。
/// <para>
/// 验证：App.CurrentUser 静态门面的双轨转发 + 变更事件（含属性级变更）。
/// 不依赖 Avalonia 平台初始化（仅访问 App 静态成员）。
/// </para>
/// </summary>
public class UserSessionTests : IDisposable
{
    private readonly User? _saved;

    public UserSessionTests()
    {
        _saved = App.CurrentUser;
        App.CurrentUser = null!;
    }

    public void Dispose()
    {
        App.CurrentUser = _saved!;
    }

    [Fact]
    public void Get_Set_ForwardsToAppStaticCurrentUser()
    {
        var user = new User { Nickname = "alice" };
        var session = new UserSession();

        session.CurrentUser = user;

        Assert.Same(user, App.CurrentUser);
        Assert.Same(user, session.CurrentUser);
    }

    [Fact]
    public void Set_Null_Clears()
    {
        var session = new UserSession();
        session.CurrentUser = new User { Nickname = "bob" };

        session.CurrentUser = null!;

        Assert.Null(App.CurrentUser);
    }

    [Fact]
    public void Changed_RaisesOnUserSwap()
    {
        var session = new UserSession();
        var raised = 0;
        session.Changed += (_, _) => raised++;

        session.CurrentUser = new User { Nickname = "u1" };
        session.CurrentUser = new User { Nickname = "u2" };

        Assert.Equal(2, raised);
    }
}