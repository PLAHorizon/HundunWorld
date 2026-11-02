using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;

namespace NarrativePro.CommonUI
{
    /// <summary>
    /// 可激活控件容器（栈）。对应 UE5 UCommonActivatableWidgetContainerBase /
    /// UCommonActivatableWidgetStack —— UE5 的 HUD 层与菜单打开都建立在它之上。
    /// <para>
    /// Flax 侧实现为真正的 <see cref="ContainerControl"/>：入栈的控件成为本容器的子控件并
    /// 铺满容器（TopLeft + 显式重排，避开非 TopLeft 锚在构造期按父尺寸 0 重算的坑），
    /// 同时只激活栈顶控件，其余保持停用 —— 这就是 UE5 的激活栈语义。
    /// </para>
    /// </summary>
    public class NarrativeActivatableWidgetContainer : ContainerControl
    {
        /// <summary>栈内控件（末尾为栈顶）。</summary>
        private readonly List<NarrativeActivatableWidget> _widgets = new List<NarrativeActivatableWidget>();

        /// <summary>
        /// 栈名称。UE5 在 WBP 里给栈控件命名（WidgetStack / PromptStack），蓝图按名字取用；
        /// Flax 的 Control 没有名字，这里显式保存一份，供按名查找与日志使用。
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>栈内容变化时触发（供 HUD 反应）。</summary>
        public event Action<NarrativeActivatableWidget> OnStackChanged;

        /// <summary>当前栈顶（活跃）控件。对应 UE5 GetActiveWidget。</summary>
        public NarrativeActivatableWidget ActiveWidget => _widgets.Count > 0 ? _widgets[_widgets.Count - 1] : null;

        /// <summary>栈内控件数量。</summary>
        public int Count => _widgets.Count;

        /// <summary>只读栈内容（自底向上）。</summary>
        public IReadOnlyList<NarrativeActivatableWidget> Widgets => _widgets;

        public NarrativeActivatableWidgetContainer()
        {
            AnchorPreset = AnchorPresets.TopLeft;
            // 铺满父容器由宿主（HUD）在布局时显式赋值
            ClipChildren = true;
            // 空栈不参与命中测试：Flax 的 ContainerControl 即使没有子控件、背景全透明，
            // 整块 Bounds 仍会吃掉鼠标（UE 的 CommonUI 栈是 SelfHitTestInvisible）。
            // 实测：Ink 画布上两个空 Narrative 栈叠在页面之上，所有点击都落在它们身上，
            // 捏脸面板的滑杆/色板完全点不到。
            Visible = false;
        }

        /// <summary>栈里有没有东西就决定可见性：可见的容器会命中整块矩形。</summary>
        private void SyncVisibility()
        {
            Visible = _widgets.Count > 0;
        }

        /// <summary>
        /// 创建并压入一个控件。对应 UE5 ContainerToUse->AddWidget&lt;T&gt;(MenuClass)。
        /// </summary>
        /// <typeparam name="T">控件类型（须有公共无参构造）</typeparam>
        /// <param name="bActivate">是否立即激活</param>
        public T AddWidget<T>(bool bActivate = true) where T : NarrativeActivatableWidget, new()
        {
            var widget = new T();
            AddWidget(widget, bActivate);
            return widget;
        }

        /// <summary>
        /// 压入一个已构造的控件。对应 UE5 AddWidget。
        /// </summary>
        public virtual NarrativeActivatableWidget AddWidget(NarrativeActivatableWidget widget, bool bActivate = true)
        {
            if (widget == null) return null;
            if (_widgets.Contains(widget))
            {
                return widget;
            }

            _widgets.Add(widget);
            AddChild(widget);
            LayoutWidget(widget);
            SyncVisibility();

            if (bActivate)
            {
                // 只让栈顶活跃，其余退出（UE5 的 stack 语义）
                for (int i = 0; i < _widgets.Count - 1; i++)
                {
                    _widgets[i].Deactivate();
                }
                widget.Activate();
            }

            OnStackChanged?.Invoke(widget);
            return widget;
        }

        /// <summary>从栈中移除指定控件。对应 UE5 RemoveWidget。</summary>
        public virtual bool RemoveWidget(NarrativeActivatableWidget widget)
        {
            if (widget == null) return false;

            int index = _widgets.IndexOf(widget);
            if (index < 0) return false;

            _widgets.RemoveAt(index);
            widget.Deactivate();
            widget.NativeDestruct();
            RemoveChild(widget);
            SyncVisibility();

            // 恢复新的栈顶
            var newTop = ActiveWidget;
            if (newTop != null) newTop.Activate();

            OnStackChanged?.Invoke(newTop);
            return true;
        }

        /// <summary>清空栈。对应 UE5 ClearChildren。</summary>
        public virtual void ClearWidgets()
        {
            for (int i = _widgets.Count - 1; i >= 0; i--)
            {
                var widget = _widgets[i];
                widget.Deactivate();
                widget.NativeDestruct();
                RemoveChild(widget);
            }
            _widgets.Clear();
            SyncVisibility();
            OnStackChanged?.Invoke(null);
        }

        /// <summary>挂起当前栈顶的输入与显示（不销毁）。对应 UE5 SuspendInput。</summary>
        public virtual void SuspendInput()
        {
            var top = ActiveWidget;
            if (top != null)
            {
                top.Deactivate();
            }
        }

        /// <summary>恢复栈顶激活。对应 UE5 RestoreInput。</summary>
        public virtual void RestoreInput()
        {
            var top = ActiveWidget;
            if (top != null && !top.IsActivated)
            {
                top.Activate();
            }
        }

        /// <summary>把栈内控件按索引重新激活（Tab 切换等）。对应 UE5 的按索引切换。</summary>
        public bool ActivateWidgetAtIndex(int index)
        {
            if (index < 0 || index >= _widgets.Count) return false;

            for (int i = 0; i < _widgets.Count; i++)
            {
                if (i == index) continue;
                _widgets[i].Deactivate();
            }

            _widgets[index].Activate();
            OnStackChanged?.Invoke(_widgets[index]);
            return true;
        }

        /// <summary>
        /// 按 OptionalNameID 查找栈内控件（Tab 切换器需要）。对应 UE5 的 OptionalNameID 用途。
        /// </summary>
        public NarrativeActivatableWidget FindWidgetByNameID(string nameID)
        {
            if (string.IsNullOrEmpty(nameID)) return null;

            for (int i = 0; i < _widgets.Count; i++)
            {
                if (_widgets[i] != null && _widgets[i].OptionalNameID == nameID) return _widgets[i];
            }
            return null;
        }

        /// <summary>父容器尺寸变化时重排所有栈内控件。</summary>
        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            RelayoutAll();
        }

        /// <summary>重新铺满所有栈内控件。</summary>
        public void RelayoutAll()
        {
            for (int i = 0; i < _widgets.Count; i++)
            {
                LayoutWidget(_widgets[i]);
            }
        }

        /// <summary>
        /// 让栈内控件铺满容器。一律 TopLeft + 显式 Offsets，避免构造期父尺寸为 0 导致错位。
        /// </summary>
        private void LayoutWidget(NarrativeActivatableWidget widget)
        {
            if (widget == null) return;

            widget.AnchorMin = Float2.Zero;
            widget.AnchorMax = Float2.Zero;
            widget.Location = Float2.Zero;
            widget.Size = Size;
        }
    }
}
