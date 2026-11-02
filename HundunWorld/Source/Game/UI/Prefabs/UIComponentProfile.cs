using System;
using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.Game.UI.Prefabs
{
    /// <summary>
    /// Prefab 组件参数档案基类。
    /// 挂在 <see cref="UIControl"/> Actor（或其子 Actor）上，把 Inspector 中配置的序列化参数
    /// 应用到宿主控件；宿主无控件时可按 <see cref="CreateDefaultControl"/> 自动创建。
    /// 编辑器模式（<c>[ExecuteInEditMode]</c>）下按 <see cref="EditorRefreshInterval"/> 轮询刷新，
    /// 实现「改属性 → 预览即时更新」；运行时在 <c>OnEnable/OnStart</c> 各绑定一次。
    /// </summary>
    [ExecuteInEditMode]
    public abstract class UIComponentProfile : Script
    {
        /// <summary>控件登记名（事件绑定 / 动画目标按此名解析）。留空则使用宿主 Actor 名。</summary>
        [EditorOrder(0), EditorDisplay("Prefab 组件"), Tooltip("控件登记名，事件绑定与动画目标按此名解析；留空则使用宿主 Actor 名")]
        public string ControlName = string.Empty;

        /// <summary>宿主 UIControl 上没有控件时是否自动创建默认控件。</summary>
        [EditorOrder(1), EditorDisplay("Prefab 组件"), Tooltip("宿主 UIControl 上没有控件时自动创建默认控件")]
        public bool AutoCreateControl = true;

        /// <summary>编辑器模式下参数自动刷新间隔（秒），0 表示关闭。</summary>
        [EditorOrder(2), EditorDisplay("Prefab 组件"), Tooltip("编辑器模式下参数自动刷新间隔（秒），0 表示关闭")]
        public float EditorRefreshInterval = 0.2f;

        private float _refreshAccum = float.MaxValue;

        /// <summary>当前绑定的目标控件（可能为 null）。</summary>
        public Control BoundControl { get; protected set; }

        /// <summary>实际生效的登记名。</summary>
        public string ResolvedName
        {
            get
            {
                if (!string.IsNullOrEmpty(ControlName))
                    return ControlName;
                if (Actor != null)
                    return Actor.Name;
                return string.Empty;
            }
        }

        /// <inheritdoc />
        public override void OnEnable()
        {
            BindControl();
        }

        /// <inheritdoc />
        public override void OnStart()
        {
            BindControl();
        }

        /// <inheritdoc />
        public override void OnUpdate()
        {
#if FLAX_EDITOR
            if (!Engine.IsEditor || EditorRefreshInterval <= 0f)
                return;
            _refreshAccum += Time.DeltaTime;
            if (_refreshAccum < EditorRefreshInterval)
                return;
            _refreshAccum = 0f;
            if (BoundControl == null || BoundControl.IsDisposing)
            {
                BindControl();
                return;
            }
            UIPrefabRegistry.Register(ResolvedName, BoundControl);
            ApplyParameters(BoundControl);
#endif
        }

        /// <inheritdoc />
        public override void OnDisable()
        {
            UnregisterControl();
        }

        /// <inheritdoc />
        public override void OnDestroy()
        {
            UnregisterControl();
        }

        /// <summary>外部（编辑器工具 / 代码）强制重新绑定并应用参数。</summary>
        public void RefreshComponent()
        {
            BindControl();
        }

        /// <summary>该组件类型的默认控件（宿主无控件时自动创建）。返回 null 表示不自动创建。</summary>
        protected virtual Control CreateDefaultControl()
        {
            return null;
        }

        /// <summary>把序列化参数应用到目标控件。子类必须实现且不得抛异常（用防御式判断）。</summary>
        protected abstract void ApplyParameters(Control control);

        /// <summary>绑定宿主控件（有则复用，无则按需创建），登记名字并应用参数。</summary>
        protected void BindControl()
        {
            var host = UIPrefabUtils.FindHostUIControl(Actor);
            Control control = null;

            if (host != null && host.HasControl)
                control = host.Control;

            if (control == null && AutoCreateControl)
            {
                control = CreateDefaultControl();
                if (control != null)
                {
                    if (host != null)
                    {
                        // 赋值即挂接到宿主画布（Flax UIControl 语义）
                        host.Control = control;
                    }
                    else
                    {
                        // 无宿主 UIControl：直接挂到父链上的 UICanvas 根
                        var canvas = UIPrefabUtils.FindCanvasActor(Actor);
                        if (canvas != null && canvas.GUI != null)
                            canvas.GUI.AddChild(control);
                    }
                }
            }

            BoundControl = control;
            if (control != null)
            {
                TryNameControl(control);
                UIPrefabRegistry.Register(ResolvedName, control);
                ApplyParameters(control);
            }
        }

        /// <summary>控件支持 Name 属性时同步登记名（反射写入，规避引擎版本差异）。</summary>
        private void TryNameControl(Control control)
        {
            try
            {
                var nameProp = control.GetType().GetProperty("Name");
                if (nameProp != null && nameProp.CanWrite && string.IsNullOrEmpty(nameProp.GetValue(control) as string))
                    nameProp.SetValue(control, ResolvedName);
            }
            catch
            {
                // 名字仅供登记用，写入失败不影响功能
            }
        }

        private void UnregisterControl()
        {
            UIPrefabRegistry.Unregister(ResolvedName, BoundControl);
        }
    }
}
