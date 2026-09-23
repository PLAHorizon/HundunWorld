using System;
using System.Reflection;
using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.Game.UI.Prefabs
{
    /// <summary>
    /// Prefab 组件库代码侧入口：一行 Spawn 预制体并取回 Profile / EventBinder / 控件，
    /// 便于现有纯代码页面渐进复用编辑器 Prefab 资产。
    /// </summary>
    public static class HundunUIPrefab
    {
        /// <summary>
        /// 运行时 Spawn 一个 Prefab。
        /// </summary>
        /// <param name="prefab">Prefab 资产</param>
        /// <param name="parent">父 Actor（UI Prefab 应传 UICanvas 或 UIControl Actor）；空则挂场景根</param>
        /// <param name="name">实例名（同时决定 Profile 登记名，若 Profile 未显式配置 ControlName）</param>
        public static Actor Spawn(Prefab prefab, Actor parent = null, string name = null)
        {
            if (prefab == null)
            {
                Debug.LogWarning("[HundunUIPrefab] prefab 为空，无法 Spawn");
                return null;
            }

            Actor instance = parent != null
                ? PrefabManager.SpawnPrefab(prefab, parent)
                : PrefabManager.SpawnPrefab(prefab);

            if (instance == null)
            {
                Debug.LogWarning($"[HundunUIPrefab] SpawnPrefab 失败: {prefab.Path}");
                return null;
            }

            if (!string.IsNullOrEmpty(name))
                instance.Name = name;
            return instance;
        }

        /// <summary>
        /// Spawn 并取回根 Actor 上的参数档案（若无则深度搜索子 Actor）。
        /// </summary>
        public static T SpawnProfiled<T>(Prefab prefab, Actor parent = null, string name = null) where T : UIComponentProfile
        {
            var instance = Spawn(prefab, parent, name);
            return instance == null ? null : FindProfile<T>(instance);
        }

        /// <summary>在 Actor（含子 Actor）上查找指定类型的 Profile 脚本。</summary>
        public static T FindProfile<T>(Actor actor) where T : UIComponentProfile
        {
            if (actor == null)
                return null;

            var profile = actor.GetScript<T>();
            if (profile != null)
                return profile;

            for (var i = 0; i < actor.ChildrenCount; i++)
            {
                profile = FindProfile<T>(actor.GetChild(i));
                if (profile != null)
                    return profile;
            }
            return null;
        }

        /// <summary>取 Actor（或其子 Actor）上 UIControl 绑定的控件实例。</summary>
        public static Control ResolveControl(Actor actor)
        {
            if (actor == null)
                return null;

            if (actor is UIControl uiControl && uiControl.HasControl)
                return uiControl.Control;

            for (var i = 0; i < actor.ChildrenCount; i++)
            {
                var found = ResolveControl(actor.GetChild(i));
                if (found != null)
                    return found;
            }
            return null;
        }

        /// <summary>
        /// 订阅实例控件的 <c>Clicked</c> 事件（Button.Clicked / InkBackButton.Clicked 等）。
        /// </summary>
        public static bool HookClicked(Actor instance, Action callback)
        {
            return HookEvent(instance, "Clicked", callback);
        }

        /// <summary>订阅实例控件的指定无参/单参 Action 事件。</summary>
        public static bool HookEvent(Actor instance, string eventName, Action callback)
        {
            if (instance == null || callback == null)
                return false;

            var control = ResolveControl(instance);
            if (control == null)
                return false;

            var eventInfo = control.GetType().GetEvent(eventName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (eventInfo == null)
                return false;

            if (eventInfo.EventHandlerType == typeof(Action))
            {
                eventInfo.AddEventHandler(control, new Action(callback));
                return true;
            }

            if (eventInfo.EventHandlerType.IsGenericType &&
                eventInfo.EventHandlerType.GetGenericTypeDefinition() == typeof(Action<>))
            {
                typeof(HundunUIPrefab)
                    .GetMethod(nameof(HookEventT), BindingFlags.NonPublic | BindingFlags.Static)
                    .MakeGenericMethod(eventInfo.EventHandlerType.GetGenericArguments()[0])
                    .Invoke(null, new object[] { eventInfo, control, callback });
                return true;
            }
            return false;
        }

        private static void HookEventT<TArg>(EventInfo eventInfo, Control control, Action callback)
        {
            eventInfo.AddEventHandler(control, new Action<TArg>(_ => callback()));
        }
    }
}
