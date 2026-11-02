using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using NarrativePro.Core;

namespace NarrativePro.CommonUI
{
    /// <summary>
    /// Narrative 通用 HUD 宿主。对应 UE5 UNarrativeCommonHUD（继承 UCommonUserWidget）。
    /// <para>
    /// UE5 里它只是一个 UMG 根控件，实例由 CommonUISubsystem 缓存，子控件在 WBP 里搭好；
    /// Flax 没有 UMG，因此这里把它实现为"GUI 根容器 + 控件栈"的宿主脚本：
    /// 由场景提供（或本组件创建）一个 <see cref="ContainerControl"/> 根，
    /// 在其上建立 WidgetStack / PromptStack 两个激活栈，供 GameplayHUD 分层使用。
    /// </para>
    /// </summary>
    public class NarrativeCommonHUD : Script
    {
        private static NarrativeCommonHUD _instance;

        /// <summary>当前 HUD 实例（对应 UE5 子系统缓存的 CommonHUD）。</summary>
        public static NarrativeCommonHUD Instance => _instance;

        /// <summary>GUI 根控件。由 <see cref="Initialize"/> 传入，或在场景里自动创建。</summary>
        public ContainerControl RootControl { get; private set; }

        /// <summary>主控件栈（菜单等全屏 UI）。对应 UE5 WidgetStack。</summary>
        public NarrativeActivatableWidgetContainer WidgetStack { get; private set; }

        /// <summary>提示栈（短时提示/确认）。对应 UE5 PromptStack。</summary>
        public NarrativeActivatableWidgetContainer PromptStack { get; private set; }

        /// <summary>是否已初始化（根控件已就绪）。</summary>
        public bool IsInitialized { get; private set; }

        /// <summary>
        /// 自动创建画布时使用的排序层（数字越大越靠上）。
        /// </summary>
        public int CanvasOrder = 100;

        /// <summary>是否在根控件缺失时自动创建一个 Screen 空间的 UICanvas Actor。</summary>
        public bool bAutoCreateCanvas = true;

        /// <summary>自动创建的画布 Actor（仅在 bAutoCreateCanvas 生效时存在，销毁时一并删除）。</summary>
        private Actor _ownedCanvasActor;

        /// <summary>控件构造时的初始化。对应 UE5 NativeConstruct。</summary>
        public override void OnEnable()
        {
            base.OnEnable();

            _instance = this;

            if (NarrativeCommonUISubsystem.Instance != null)
            {
                NarrativeCommonUISubsystem.Instance.CommonHUD = this;
            }
            else
            {
                NarrativeLog.LogWarning("NarrativeCommonHUD 启用时 NarrativeCommonUISubsystem 尚未初始化，无法注册 CommonHUD。");
            }

            // 允许场景里只挂脚本不手工接根控件：延迟到首帧再建画布，此时 Scene 已就绪
            if (!IsInitialized && RootControl == null)
            {
                _bPendingInitialize = true;
            }
        }

        /// <summary>控件销毁时清理。对应 UE5 NativeDestruct。</summary>
        public override void OnDisable()
        {
            TeardownStacks();

            if (NarrativeCommonUISubsystem.Instance != null &&
                NarrativeCommonUISubsystem.Instance.CommonHUD == this)
            {
                NarrativeCommonUISubsystem.Instance.CommonHUD = null;
            }

            if (_instance == this) _instance = null;

            base.OnDisable();
        }

        private bool _bPendingInitialize;

        public override void OnUpdate()
        {
            base.OnUpdate();

            if (_bPendingInitialize)
            {
                _bPendingInitialize = false;
                EnsureRootControl();
            }
        }

        /// <summary>用外部提供的 GUI 根控件初始化 HUD。对应 UE5 中在 WBP 里放好栈控件。</summary>
        public virtual void Initialize(ContainerControl root)
        {
            RootControl = root;
            EnsureRootControl();
        }

        /// <summary>
        /// 建立根控件与两个栈。若调用方没有提供根控件，则自动创建 Screen 空间 UICanvas。
        /// GUI 只能在主线程创建 —— 本方法只在脚本生命周期/主线程调用。
        /// </summary>
        protected virtual void EnsureRootControl()
        {
            if (IsInitialized) return;
            if (RootControl == null && !bAutoCreateCanvas)
            {
                NarrativeLog.LogWarning("NarrativeCommonHUD：没有可用的 GUI 根控件，且已关闭自动创建画布。");
                return;
            }

            if (RootControl == null)
            {
                RootControl = CreateScreenRootControl();
            }

            if (RootControl == null)
            {
                NarrativeLog.LogWarning("NarrativeCommonHUD：GUI 根控件创建失败（场景无相机或画布不可用）。");
                return;
            }

            WidgetStack = new NarrativeActivatableWidgetContainer { Name = "WidgetStack" };
            PromptStack = new NarrativeActivatableWidgetContainer { Name = "PromptStack" };

            RootControl.AddChild(PromptStack);
            RootControl.AddChild(WidgetStack);

            LayoutStack(WidgetStack);
            LayoutStack(PromptStack);

            // 根尺寸变化时重排栈（TopLeft 锚 + 显式重排，规避非 TopLeft 锚的构造期重算问题）
            RootControl.SizeChanged += _ =>
            {
                LayoutStack(WidgetStack);
                LayoutStack(PromptStack);
                if (WidgetStack != null) WidgetStack.RelayoutAll();
                if (PromptStack != null) PromptStack.RelayoutAll();
            };

            IsInitialized = true;
        }

        private void LayoutStack(NarrativeActivatableWidgetContainer stack)
        {
            if (stack == null || RootControl == null) return;

            stack.AnchorMin = Float2.Zero;
            stack.AnchorMax = Float2.Zero;
            stack.Location = Float2.Zero;
            stack.Size = RootControl.Size;
            stack.BringToFront();
        }

        /// <summary>
        /// 创建一个铺满屏幕的根控件：优先复用场景里已有的 UICanvas，没有则新建一个屏幕空间画布。
        /// <para>
        /// Flax 没有 UE5 的 World::SpawnActor：脚本里挂子 Actor 用 <see cref="Actor.AddChild{T}"/>，
        /// 独立 Actor 用 <see cref="Level.SpawnActor(Actor)"/>（注意该 API 返回 true 才是失败）。
        /// </para>
        /// </summary>
        private ContainerControl CreateScreenRootControl()
        {
            // 1) 复用场景已有画布，避免多个 HUD 抢同一个屏幕根
            try
            {
                var canvases = Level.GetActors<UICanvas>(true);
                if (canvases != null && canvases.Length > 0)
                {
                    _ownedCanvasActor = null;
                    return canvases[0].GUI;
                }
            }
            catch
            {
                // 场景查询失败则走新建
            }

            // 2) 新建画布 Actor
            try
            {
                UICanvas canvas;
                if (Actor != null)
                {
                    // 挂在宿主 Actor 下，随宿主一起进入场景层级
                    canvas = Actor.AddChild<UICanvas>();
                }
                else
                {
                    canvas = new UICanvas { Name = "NarrativeCommonHUDCanvas" };
                    // 无宿主兜底：显式生成进玩法场景，不靠"无父级默认落 RootScene"
                    if (NarrativePro.CommonUI.NarrativeRuntimeBootstrap.TrySpawnToGameplayScene(canvas, "NarrativeCommonHUDCanvas"))
                    {
                        // Flax: SpawnActor 返回 true 表示无法完成（例如非主线程）
                        NarrativeLog.LogWarning("NarrativeCommonHUD：UICanvas 生成失败。");
                        FlaxEngine.Object.Destroy(canvas);
                        return null;
                    }
                }

                if (canvas != null)
                {
                    canvas.RenderMode = CanvasRenderMode.ScreenSpace;
                    canvas.Order = CanvasOrder;
                    canvas.ReceivesEvents = true;
                    _ownedCanvasActor = canvas;
                    return canvas.GUI;
                }
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning("NarrativeCommonHUD：创建 UICanvas 失败：" + ex.Message);
            }

            return null;
        }

        /// <summary>销毁两个栈并解除根控件绑定。</summary>
        private void TeardownStacks()
        {
            if (WidgetStack != null)
            {
                WidgetStack.ClearWidgets();
                WidgetStack.Dispose();
                WidgetStack = null;
            }

            if (PromptStack != null)
            {
                PromptStack.ClearWidgets();
                PromptStack.Dispose();
                PromptStack = null;
            }

            if (_ownedCanvasActor != null)
            {
                // Flax 的销毁入口是静态的 Object.Destroy（Actor 自身没有无参 Destroy）
                FlaxEngine.Object.Destroy(_ownedCanvasActor);
                _ownedCanvasActor = null;
            }

            RootControl = null;
            IsInitialized = false;
        }

        /// <summary>
        /// 向主控件栈压入一个可激活控件。对应 UE5 的 WidgetStack->AddWidget。
        /// </summary>
        public NarrativeActivatableWidget PushWidget(NarrativeActivatableWidget widget, bool bActivate = true)
        {
            EnsureRootControl();
            if (WidgetStack == null) return null;
            return WidgetStack.AddWidget(widget, bActivate);
        }

        /// <summary>从主控件栈弹出一个控件。</summary>
        public bool PopWidget(NarrativeActivatableWidget widget)
        {
            if (WidgetStack == null) return false;
            return WidgetStack.RemoveWidget(widget);
        }

        /// <summary>
        /// 弹出栈顶控件（返回上一级菜单）。对应 UE5 CommonActivatableWidgetStack 的返回行为。
        /// </summary>
        public bool PopTopWidget()
        {
            if (WidgetStack == null || WidgetStack.Count == 0) return false;
            return WidgetStack.RemoveWidget(WidgetStack.ActiveWidget);
        }

        /// <summary>当前主栈栈顶控件。</summary>
        public NarrativeActivatableWidget GetActiveWidget() => WidgetStack != null ? WidgetStack.ActiveWidget : null;

        /// <summary>
        /// 按栈名查找本 HUD 的激活栈。对应 UE5 蓝图里按控件名（WidgetStack / PromptStack）取栈的用法。
        /// </summary>
        public NarrativeActivatableWidgetContainer FindStackByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (WidgetStack != null && WidgetStack.Name == name) return WidgetStack;
            if (PromptStack != null && PromptStack.Name == name) return PromptStack;
            return null;
        }
    }
}
