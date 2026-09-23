using System;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.Services;

/// <summary>
/// <see cref="IUserSession"/> 的 App 门面实现（D1 阶段 1）。
/// <para>
/// 内部转发 <see cref="App.CurrentUser"/> / <see cref="App.CurrentUserChanged"/>，
/// 保持与既有静态门面双轨兼容：迁移期 ViewModel/Service 逐步改注入本接口，
/// 现有代码仍可直接读 <see cref="App.CurrentUser"/>。
/// </para>
/// </summary>
public sealed class UserSession : IUserSession
{
    public User? CurrentUser
    {
        get => App.CurrentUser;
        set => App.CurrentUser = value!;
    }

    public event EventHandler? Changed
    {
        add => App.CurrentUserChanged += value;
        remove => App.CurrentUserChanged -= value;
    }
}