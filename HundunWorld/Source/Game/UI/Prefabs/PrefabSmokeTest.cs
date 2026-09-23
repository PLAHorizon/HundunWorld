using System;
using System.Reflection;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;

namespace HundunWorld.Game.UI.Prefabs
{
    /// <summary>
    /// Prefab 组件库冒烟测试（仅测试场景挂载）：
    /// 验证 Prefab 资产加载 → Spawn 还原控件/Profile → UIEventBinder 事件链路 → Toast 弹出。
    /// 结果以 [PREFAB-SMOKE] 前缀输出到日志。
    /// </summary>
    public class PrefabSmokeTest : Script
    {
        private const string PrefabPath = "Content/Prefabs/UI/Btn_InkButton.prefab";

        public override void OnStart()
        {
            try
            {
                Run();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PREFAB-SMOKE] FAIL: {ex}");
            }
        }

        private void Run()
        {
            // 1. 加载 Prefab 资产
            var prefab = Content.Load<Prefab>(PrefabPath);
            if (prefab == null || !prefab.IsLoaded)
            {
                Debug.LogError($"[PREFAB-SMOKE] FAIL: Prefab 加载失败 {PrefabPath}");
                return;
            }
            Debug.Log("[PREFAB-SMOKE] PASS: Prefab 资产加载成功");

            // 2. 找场景画布；无则运行时创建
            var canvas = Level.FindActor<FlaxEngine.UICanvas>("GalleryCanvas");
            if (canvas == null)
            {
                canvas = new FlaxEngine.UICanvas { Name = "SmokeRuntimeCanvas" };
                Level.SpawnActor(canvas);
                Debug.Log("[PREFAB-SMOKE] INFO: 场景无 GalleryCanvas，使用运行时画布");
            }
            else
            {
                Debug.Log("[PREFAB-SMOKE] INFO: 使用场景序列化画布 GalleryCanvas");
            }

            // 3. Spawn Prefab 到画布
            var instance = HundunUIPrefab.Spawn(prefab, canvas, "SmokeBtn");
            if (instance == null)
            {
                Debug.LogError("[PREFAB-SMOKE] FAIL: Prefab Spawn 失败");
                return;
            }

            var control = HundunUIPrefab.ResolveControl(instance);
            if (control is not InkButton button)
            {
                Debug.LogError($"[PREFAB-SMOKE] FAIL: 控件未还原，实际 {control?.GetType().Name ?? "null"}");
                return;
            }
            Debug.Log($"[PREFAB-SMOKE] PASS: 控件还原 InkButton Text={button.Text} Size=({button.Width},{button.Height}) BG={button.BackgroundColor}");

            // 4. Profile 脚本还原并应用参数
            var profile = HundunUIPrefab.FindProfile<InkButtonProfile>(instance);
            if (profile == null)
            {
                Debug.LogError("[PREFAB-SMOKE] FAIL: InkButtonProfile 未随 Prefab 还原");
                return;
            }
            Debug.Log($"[PREFAB-SMOKE] PASS: Profile 还原 Variant={profile.Variant} Width={profile.Width} BoundControl={(profile.BoundControl != null ? "ok" : "null")}");

            // 5. 注册表按名解析
            var registered = UIPrefabRegistry.Resolve("BtnInk");
            Debug.Log(registered != null
                ? "[PREFAB-SMOKE] PASS: UIPrefabRegistry 按名解析成功"
                : "[PREFAB-SMOKE] WARN: 注册表未命中 BtnInk（Profile 可能未绑定）");

            // 6. 事件链路：HookClicked 订阅 + 反射触发 Button.OnClick（无参）
            var clicked = false;
            HundunUIPrefab.HookClicked(instance, () => clicked = true);
            InvokeClick(button);
            Debug.Log(clicked
                ? "[PREFAB-SMOKE] PASS: Clicked 事件链路触发"
                : "[PREFAB-SMOKE] WARN: Clicked 未触发（可能 OnClick 反射签名不匹配）");

            // 6.5 声明式事件绑定：UIEventBinder（若 Prefab 配了 binder）订阅 Clicked → BindingTriggered
            var binder = FindBinder(instance);
            if (binder == null)
            {
                Debug.Log("[PREFAB-SMOKE] INFO: 实例未配置 UIEventBinder，跳过声明式绑定验证");
            }
            else
            {
                var triggeredIndex = -1;
                binder.BindingTriggered += index => triggeredIndex = index;
                binder.WireAll();
                InvokeClick(button);
                Debug.Log(triggeredIndex == 0
                    ? "[PREFAB-SMOKE] PASS: UIEventBinder 声明式绑定触发（含 Toast 动作）"
                    : "[PREFAB-SMOKE] FAIL: UIEventBinder 未触发");
            }

            // 7. Toast 通道
            Components.ToastMessage.ShowInfo("Prefab 冒烟测试成功", 3f);
            Debug.Log("[PREFAB-SMOKE] DONE: 全部步骤执行完毕");

            // 8. 全量 Prefab 加载 + Gallery 生成
            RunGallery(canvas);
        }

        private void RunGallery(FlaxEngine.UICanvas canvas)
        {
            var dir = System.IO.Path.Combine(Globals.ProjectFolder, "Content/Prefabs/UI");
            if (!System.IO.Directory.Exists(dir))
            {
                Debug.LogError("[PREFAB-SMOKE] FAIL: Prefabs 目录不存在");
                return;
            }

            var files = System.IO.Directory.GetFiles(dir, "*.prefab");
            int okCount = 0, failCount = 0;
            float y = 60f;
            foreach (var file in files)
            {
                var rel = "Content/Prefabs/UI/" + System.IO.Path.GetFileName(file);
                try
                {
                    var prefab = Content.Load<Prefab>(rel);
                    if (prefab == null || !prefab.IsLoaded)
                    {
                        Debug.LogError($"[PREFAB-SMOKE] GALLERY FAIL: 加载失败 {rel}");
                        failCount++;
                        continue;
                    }
                    var instance = HundunUIPrefab.Spawn(prefab, canvas,
                        "Gallery_" + System.IO.Path.GetFileNameWithoutExtension(file));
                    if (instance == null)
                    {
                        Debug.LogError($"[PREFAB-SMOKE] GALLERY FAIL: Spawn 失败 {rel}");
                        failCount++;
                        continue;
                    }
                    // 平铺排列便于截图观察
                    var control = HundunUIPrefab.ResolveControl(instance);
                    if (control != null)
                    {
                        control.AnchorPreset = AnchorPresets.TopLeft;
                        control.Location = new Float2(10f, y);
                        y += 50f;
                        if (y > 1500f)
                            y = 60f;
                    }
                    okCount++;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[PREFAB-SMOKE] GALLERY FAIL: {rel} -> {ex.Message}");
                    failCount++;
                }
            }
            Debug.Log($"[PREFAB-SMOKE] GALLERY: 成功 {okCount} / 失败 {failCount} / 总数 {files.Length}");
        }

        private static void InvokeClick(Button button)
        {
            typeof(Button)
                .GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                ?.Invoke(button, null);
        }

        private static UIEventBinder FindBinder(Actor actor)
        {
            if (actor == null)
                return null;
            var binder = actor.GetScript<UIEventBinder>();
            if (binder != null)
                return binder;
            for (var i = 0; i < actor.ChildrenCount; i++)
            {
                binder = FindBinder(actor.GetChild(i));
                if (binder != null)
                    return binder;
            }
            return null;
        }
    }
}
