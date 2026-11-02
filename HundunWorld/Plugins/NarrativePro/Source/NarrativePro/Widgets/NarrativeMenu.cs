using System;
using FlaxEngine;
using NarrativePro.CommonUI;

namespace NarrativePro.Widgets
{
    /// <summary>
    /// Narrative 菜单基类。移植自 UE5 NarrativeArsenal: <c>Widgets/NarrativeMenu.h</c>
    /// （<c>UNarrativeMenu : UNarrativeActivatableWidget</c> —— "可以激活展示内容、之后再关闭的界面，由 GameplayHUD 添加"）。
    /// <para>
    /// UE5 源类只有构造函数（真正的内容由蓝图子类提供），并且它正是
    /// <c>UNarrativeGameplayHUD::OpenMenu</c> 的返回类型 —— 那里的重复菜单判定用的是
    /// <c>CurrentWidget-&gt;GetClass() == MenuClass</c>，所以 Flax 侧必须是一个可实例化的具体类，
    /// 由子类覆写 <see cref="NarrativeActivatableWidget.RegisterActions"/> 来接线输入。
    /// </para>
    /// <para>
    /// 这里在 UE5 语义之上补了菜单必备的两件实事：默认按 Back/Esc 关闭
    /// （UE5 基类默认 false，蓝图菜单普遍自己开；菜单关不掉是明显缺陷，故基类给出正确默认，子类可关），
    /// 以及 <see cref="Close"/> —— 把自己从所在激活栈上弹出（等价 UE5 里栈的 RemoveWidget）。
    /// </para>
    /// </summary>
    public class NarrativeMenu : NarrativeActivatableWidget
    {
        public NarrativeMenu()
        {
            // UE5 UNarrativeActivatableWidget 构造函数的默认值由基类提供，这里只补菜单自身的行为
            bDeactivateOnBack = true;
            bFocusDesiredTargetOnActivate = true;
        }

        /// <summary>
        /// 关闭菜单：停用并从所在激活栈移除。
        /// 对应 UE5 里 <c>ContainerToUse-&gt;RemoveWidget(Widget)</c> 的蓝图做法。
        /// </summary>
        public virtual void Close()
        {
            var container = Parent as NarrativeActivatableWidgetContainer;
            if (container != null)
            {
                container.RemoveWidget(this);
            }
            else
            {
                Deactivate();
                Detach();
            }
        }

        /// <summary>菜单被压入激活栈时调用（对应 UE5 的激活时机），子类可覆写刷新内容。</summary>
        public override void NativeOnActivated()
        {
            base.NativeOnActivated();
            OnMenuOpened();
        }

        /// <summary>菜单从栈上退出时调用，子类可覆写做清理。</summary>
        public virtual void NativeOnDeactivatedMenu()
        {
            OnMenuClosed();
        }

        /// <summary>菜单打开事件（对应 UE5 蓝图菜单的 OnOpened）。</summary>
        public event Action<NarrativeMenu> OnOpened;

        /// <summary>菜单关闭事件（对应 UE5 蓝图菜单的 OnClosed）。</summary>
        public event Action<NarrativeMenu> OnClosed;

        protected virtual void OnMenuOpened()
        {
            OnOpened?.Invoke(this);
        }

        protected virtual void OnMenuClosed()
        {
            OnClosed?.Invoke(this);
        }

        /// <summary>停用时派发菜单关闭事件。对应 UE5 NativeOnDeactivated 的蓝图配套。</summary>
        public override void Deactivate()
        {
            if (!IsActivated) return;
            base.Deactivate();
            NativeOnDeactivatedMenu();
        }
    }
}
