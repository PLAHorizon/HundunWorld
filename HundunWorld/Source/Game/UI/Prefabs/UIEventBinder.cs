using System;
using System.Collections.Generic;
using System.Reflection;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Components;

namespace HundunWorld.Game.UI.Prefabs
{
    /// <summary>事件触发后执行的动作类型。</summary>
    public enum UIPrefabEventAction
    {
        /// <summary>调用目标 Actor 上脚本的公开方法（0 参 / 1 参 Control / 1 参 string）</summary>
        InvokeScriptMethod = 0,

        /// <summary>在目标 Actor（缺省为本 Actor）下 Spawn 一个 Prefab</summary>
        SpawnPrefab = 1,

        /// <summary>弹出 Toast 提示。消息前缀支持 "info:"、"success:"、"warning:"、"error:"，缺省 info</summary>
        ShowToast = 2,

        /// <summary>销毁目标 Actor（缺省为本 Actor），用于关闭按钮等</summary>
        DestroyTarget = 3
    }

    /// <summary>
    /// 声明式事件绑定器：在 Prefab / 场景的 Inspector 中配置
    /// 「目标控件名 + 事件名 + 动作 + 回调参数」，运行时反射订阅控件 C# 事件，
    /// 实现无代码的事件绑定；同时暴露 <see cref="BindingTriggered"/> 供代码侧监听。
    /// 各列表按下标一一对应，长度不一致时以最短者为准。
    /// </summary>
    public class UIEventBinder : Script
    {
        /// <summary>目标控件登记名（由 UIComponentProfile 注册；留空表示本 Actor 上 UIControl 的控件）</summary>
        [EditorOrder(0), EditorDisplay("事件绑定"), Tooltip("目标控件登记名（UIComponentProfile.ControlName）；留空表示本 Actor 上 UIControl 的控件")]
        public List<string> TargetNames = new List<string>();

        /// <summary>控件事件名（如 Clicked / ButtonClicked / TextChanged / Confirmed / Cancelled）</summary>
        [EditorOrder(1), EditorDisplay("事件绑定"), Tooltip("控件事件名，如 Clicked / ButtonClicked / TextChanged / Confirmed / Cancelled")]
        public List<string> EventNames = new List<string>();

        /// <summary>触发后执行的动作</summary>
        [EditorOrder(2), EditorDisplay("事件绑定")]
        public List<UIPrefabEventAction> Actions = new List<UIPrefabEventAction>();

        /// <summary>动作目标 Actor（InvokeScriptMethod / SpawnPrefab 的父节点 / DestroyTarget；可空）</summary>
        [EditorOrder(3), EditorDisplay("事件绑定"), Tooltip("动作目标 Actor；留空使用本脚本所在 Actor")]
        public List<Actor> TargetActors = new List<Actor>();

        /// <summary>目标脚本类型名（全名或短名，留空则在所有脚本中查找方法）</summary>
        [EditorOrder(4), EditorDisplay("事件绑定"), Tooltip("目标脚本类型名（全名或短名），留空则遍历所有脚本")]
        public List<string> TargetTypeNames = new List<string>();

        /// <summary>要调用的方法名（InvokeScriptMethod 用）</summary>
        [EditorOrder(5), EditorDisplay("事件绑定")]
        public List<string> MethodNames = new List<string>();

        /// <summary>要 Spawn 的 Prefab（SpawnPrefab 用）</summary>
        [EditorOrder(6), EditorDisplay("事件绑定")]
        public List<Prefab> SpawnPrefabs = new List<Prefab>();

        /// <summary>附加消息（ShowToast 的文本；InvokeScriptMethod 的 string 参数）</summary>
        [EditorOrder(7), EditorDisplay("事件绑定"), Tooltip("ShowToast 的文本（可带 info:/success:/warning:/error: 前缀）")]
        public List<string> Messages = new List<string>();

        /// <summary>任一绑定被触发时回调（参数为绑定下标），供代码侧扩展。</summary>
        public event Action<int> BindingTriggered;

        private bool _wired;

        /// <inheritdoc />
        public override void OnEnable()
        {
            _wired = false;
        }

        /// <inheritdoc />
        public override void OnStart()
        {
            WireAll();
        }

        /// <summary>执行全部绑定订阅（Spawn 出的实例若错过 OnStart 时机可由代码补调）。</summary>
        public void WireAll()
        {
            if (_wired)
                return;
            _wired = true;

            var count = ShortestLength();
            for (var i = 0; i < count; i++)
            {
                try
                {
                    WireOne(i);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[UIEventBinder] 绑定 #{i} 订阅失败: {ex.Message}（Actor={GetNameSafe()}）");
                }
            }
        }

        private void WireOne(int index)
        {
            var control = ResolveControl(index);
            if (control == null)
            {
                Debug.LogWarning($"[UIEventBinder] 找不到目标控件: \"{GetString(TargetNames, index)}\"，绑定 #{index} 跳过");
                return;
            }

            var eventName = GetString(EventNames, index);
            if (string.IsNullOrEmpty(eventName))
                return;

            var eventInfo = control.GetType().GetEvent(eventName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (eventInfo == null)
            {
                Debug.LogWarning($"[UIEventBinder] 控件 {control.GetType().Name} 不存在事件 \"{eventName}\"，绑定 #{index} 跳过");
                return;
            }

            var handlerType = eventInfo.EventHandlerType;
            if (handlerType == typeof(Action))
            {
                eventInfo.AddEventHandler(control, new Action(() => Trigger(index)));
            }
            else if (handlerType.IsGenericType &&
                     handlerType.GetGenericTypeDefinition() == typeof(Action<>))
            {
                var argType = handlerType.GetGenericArguments()[0];
                typeof(UIEventBinder)
                    .GetMethod(nameof(SubscribeActionT), BindingFlags.NonPublic | BindingFlags.Instance)
                    .MakeGenericMethod(argType)
                    .Invoke(this, new object[] { eventInfo, control, index });
            }
            else
            {
                Debug.LogWarning($"[UIEventBinder] 事件 \"{eventName}\" 的签名 {handlerType.Name} 暂不支持，绑定 #{index} 跳过");
            }
        }

        private void SubscribeActionT<T>(EventInfo eventInfo, Control control, int index)
        {
            eventInfo.AddEventHandler(control, new Action<T>(_ => Trigger(index)));
        }

        /// <summary>按配置执行绑定动作。</summary>
        private void Trigger(int index)
        {
            BindingTriggered?.Invoke(index);

            var action = index < Actions.Count ? Actions[index] : UIPrefabEventAction.InvokeScriptMethod;
            var actor = index < TargetActors.Count ? TargetActors[index] : null;
            if (actor == null)
                actor = Actor;

            switch (action)
            {
                case UIPrefabEventAction.InvokeScriptMethod:
                    InvokeScriptMethod(actor, index);
                    break;

                case UIPrefabEventAction.SpawnPrefab:
                    var prefab = index < SpawnPrefabs.Count ? SpawnPrefabs[index] : null;
                    if (prefab != null)
                        HundunUIPrefab.Spawn(prefab, actor);
                    else
                        Debug.LogWarning($"[UIEventBinder] 绑定 #{index} 未配置 SpawnPrefabs");
                    break;

                case UIPrefabEventAction.ShowToast:
                    ShowToast(index);
                    break;

                case UIPrefabEventAction.DestroyTarget:
                    if (actor != null)
                        FlaxEngine.Object.Destroy(actor);
                    break;
            }
        }

        private void InvokeScriptMethod(Actor actor, int index)
        {
            if (actor == null)
                return;

            var methodName = GetString(MethodNames, index);
            if (string.IsNullOrEmpty(methodName))
                return;

            var typeName = GetString(TargetTypeNames, index);
            Script target = null;
            var scripts = actor.Scripts;
            if (scripts == null)
                return;
            foreach (var script in scripts)
            {
                if (script == null)
                    continue;
                var st = script.GetType();
                if (string.IsNullOrEmpty(typeName) ||
                    st.FullName.Equals(typeName, StringComparison.OrdinalIgnoreCase) ||
                    st.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase))
                {
                    target = script;
                    break;
                }
            }

            if (target == null)
            {
                Debug.LogWarning($"[UIEventBinder] Actor \"{GetNameSafe(actor)}\" 上找不到脚本类型 \"{typeName}\"");
                return;
            }

            var method = target.GetType().GetMethod(methodName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (method == null)
            {
                Debug.LogWarning($"[UIEventBinder] 脚本 {target.GetType().Name} 上找不到公开方法 \"{methodName}\"");
                return;
            }

            var parameters = method.GetParameters();
            object[] args = null;
            if (parameters.Length == 1)
            {
                if (parameters[0].ParameterType == typeof(string))
                    args = new object[] { GetString(Messages, index) };
                else if (typeof(Control).IsAssignableFrom(parameters[0].ParameterType))
                    args = new object[] { ResolveControl(index) };
                else
                    args = new object[] { null };
            }

            try
            {
                method.Invoke(target, args);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UIEventBinder] 调用 {target.GetType().Name}.{methodName} 异常: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        private void ShowToast(int index)
        {
            var message = GetString(Messages, index);
            if (string.IsNullOrEmpty(message))
                message = "Prefab 事件触发";

            var duration = 3f;
            var kind = "info";
            var colon = message.IndexOf(':');
            // 仅识别英文冒号紧跟类型前缀的场景（避免误伤正文中的冒号）
            if (colon > 0 && colon <= 7)
            {
                var prefix = message.Substring(0, colon).Trim().ToLowerInvariant();
                if (prefix == "info" || prefix == "success" || prefix == "warning" || prefix == "error")
                {
                    kind = prefix;
                    message = message.Substring(colon + 1).Trim();
                }
            }

            switch (kind)
            {
                case "success":
                    ToastMessage.ShowSuccess(message, duration);
                    break;
                case "warning":
                    ToastMessage.ShowWarning(message, duration);
                    break;
                case "error":
                    ToastMessage.ShowError(message, duration);
                    break;
                default:
                    ToastMessage.ShowInfo(message, duration);
                    break;
            }
        }

        private Control ResolveControl(int index)
        {
            var name = index < TargetNames.Count ? TargetNames[index] : null;
            if (!string.IsNullOrEmpty(name))
            {
                var registered = UIPrefabRegistry.Resolve(name);
                if (registered != null)
                    return registered;
            }

            // 留空或未命中：取本 Actor（或父链）上 UIControl 的控件
            var host = UIPrefabUtils.FindHostUIControl(Actor);
            if (host != null && host.HasControl)
                return host.Control;
            return null;
        }

        private int ShortestLength()
        {
            var count = int.MaxValue;
            if (TargetNames != null) count = Math.Min(count, TargetNames.Count);
            if (EventNames != null) count = Math.Min(count, EventNames.Count);
            return count == int.MaxValue ? 0 : count;
        }

        private static string GetString(List<string> list, int index)
        {
            return list != null && index >= 0 && index < list.Count ? list[index] : null;
        }

        private string GetNameSafe(Actor actor = null)
        {
            actor = actor ?? Actor;
            return actor != null ? actor.Name : "<null>";
        }
    }
}
