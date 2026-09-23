using System;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.Services;

/// <summary>
/// 用户会话抽象（D1 阶段 1）。
/// <para>
/// 封装"当前登录用户"这一进程状态：把 <see cref="App.CurrentUser"/> 的静态门面
/// 收敛为可注入接口，使 ViewModel/Service 可测试（无需挂载 UI 线程与 Avalonia App）。
/// 实现 <see cref="UserSession"/> 内部转发 <see cref="App.CurrentUser"/>（双轨兼容）。</para>
/// </summary>
public interface IUserSession
{
    /// <summary>当前登录用户；null = 未登录。设置时触发 <see cref="Changed"/>。</summary>
    User? CurrentUser { get; set; }

    /// <summary>当前用户（或用户属性）变化事件。</summary>
    event EventHandler? Changed;
}