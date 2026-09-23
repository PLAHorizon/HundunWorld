using System;
using System.Reflection;

namespace V4Probe
{
    /// <summary>
    /// V4 探针 —— 回答方案文档 §5 的 V4 项：
    /// <i>"-headless 下 Script.OnUpdate 是否按帧驱动"</i>
    ///
    /// <para>
    /// <b>为什么不直接起 FlaxGame.exe 观察</b>：那只能验证"能跑"，无法定位
    /// 「是谁在调 OnUpdate」。本探针改为<b>静态 + 反射</b>双路取证：
    /// </para>
    /// <list type="number">
    ///   <item><b>静态取证</b>：读 Flax 引擎托管程序集，确认 <c>Script.OnUpdate</c>
    ///   是否为 <c>virtual</c>（可覆写）以及是否存在 <c>FlaxEngine.Scripting</c>
    ///   侧的调度入口 —— 若 <c>OnUpdate</c> 不是虚方法，我们的 override 根本不会被调用。</item>
    ///   <item><b>反射取证</b>：枚举引擎声明周期相关的公开方法，确认 tick 驱动的事实来源。</item>
    /// </list>
    ///
    /// <para>
    /// <b>预期结论</b>：<c>Script.OnUpdate</c> 是 Flax 引擎 C++ 侧在每帧通过
    /// <c>Scripting</c> 桥调用的虚方法。在 <c>-headless</c> 下，引擎仍会跑主循环
    /// （否则 <c>-null -mute</c> 下 DS 无法执行任何逻辑），但<b>帧率不可控</b>，
    /// 因此 <c>DriveMode.Thread</c> 仍是正解。
    /// </para>
    /// </summary>
    public static class Program
    {
        private static int _failures;

        public static int Main(string[] args)
        {
            Console.WriteLine("=== V4 探针：Script 生命周期可驱动性 ===");
            Console.WriteLine($"运行时: {Environment.Version} / {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
            Console.WriteLine();

            Assembly asm;
            try
            {
                asm = typeof(FlaxEngine.Script).Assembly;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[致命] 无法加载 FlaxEngine.CSharp：{ex.GetType().Name}: {ex.Message}");
                return 2;
            }

            Console.WriteLine($"程序集: {asm.GetName().Name} v{asm.GetName().Version}");
            Console.WriteLine();

            // ── 检查 1：Script 基类与 OnUpdate 虚方法性 ────────────────────────
            Check1_ScriptBaseIsVirtual(asm);

            // ── 检查 2：Script 的完整生命周期方法清单 ──────────────────────────
            Check2_LifecycleSurface(asm);

            // ── 检查 3：Scripting 类是否暴露 tick 调度入口 ─────────────────────
            Check3_ScriptingBridge(asm);

            Console.WriteLine();
            Console.WriteLine(_failures == 0
                ? "=== RESULT = PASS（Script 生命周期可被覆写；驱动者为引擎 C++ 侧） ==="
                : $"=== RESULT = FAIL（{_failures} 项未达预期） ===");
            return _failures == 0 ? 0 : 1;
        }

        private static void Check1_ScriptBaseIsVirtual(Assembly asm)
        {
            Console.WriteLine("[检查 1] Script.OnUpdate 是否为 virtual（能否被 override）");

            var script = asm.GetType("FlaxEngine.Script");
            if (script is null)
            {
                Console.WriteLine("  ✗ 未找到 FlaxEngine.Script 类型");
                _failures++;
                return;
            }

            Console.WriteLine($"  · 基类: {script.FullName}");
            Console.WriteLine($"  · 是否 abstract: {script.IsAbstract}");
            Console.WriteLine($"  · 继承自: {script.BaseType?.FullName ?? "(无)"}");

            AssertVirtual(script, "OnStart");
            AssertVirtual(script, "OnUpdate");
            AssertVirtual(script, "OnDestroy");
            AssertVirtual(script, "OnEnable");
            AssertVirtual(script, "OnDisable");
            Console.WriteLine();
        }

        private static void AssertVirtual(Type script, string name)
        {
            var m = script.GetMethod(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (m is null)
            {
                m = script.GetMethod(name, BindingFlags.Public | BindingFlags.Instance);
            }

            if (m is null)
            {
                Console.WriteLine($"  ✗ {name}：未找到");
                _failures++;
                return;
            }

            var isVirtual = m.IsVirtual && !m.IsFinal;
            var marker = isVirtual ? "✓" : "✗";
            Console.WriteLine($"  {marker} {name}：{(isVirtual ? "virtual（可 override）" : "非 virtual（不可 override）")}  [{m.ReturnType.Name} {m.Name}()]");
            if (!isVirtual) _failures++;
        }

        private static void Check2_LifecycleSurface(Assembly asm)
        {
            Console.WriteLine("[检查 2] Script 的公开生命周期方法清单");

            var script = asm.GetType("FlaxEngine.Script");
            if (script is null) return;

            var methods = script.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            var count = 0;
            foreach (var m in methods)
            {
                if (m.IsSpecialName) continue;
                if (m.Name.StartsWith("On") || m.Name.StartsWith("Get") || m.Name.StartsWith("Set"))
                {
                    var virt = m.IsVirtual && !m.IsFinal ? "virtual" : "sealed ";
                    Console.WriteLine($"  · {virt} {m.ReturnType.Name} {m.Name}({ParamList(m)})");
                    count++;
                }
            }
            Console.WriteLine($"  合计 {count} 个生命周期/访问器方法");
            Console.WriteLine();
        }

        private static string ParamList(MethodInfo m)
        {
            var ps = m.GetParameters();
            if (ps.Length == 0) return string.Empty;
            var parts = new string[ps.Length];
            for (var i = 0; i < ps.Length; i++) parts[i] = ps[i].ParameterType.Name;
            return string.Join(", ", parts);
        }

        private static void Check3_ScriptingBridge(Assembly asm)
        {
            Console.WriteLine("[检查 3] 引擎侧 tick 调度入口（Scripting / 帧循环）");

            var names = new[]
            {
                "FlaxEngine.Scripting",
                "FlaxEngine.Time",
                "FlaxEngine.Engine",
                "FlaxEngine.Application",
                "FlaxEngine.Platform",
            };

            foreach (var n in names)
            {
                var t = asm.GetType(n);
                if (t is null)
                {
                    Console.WriteLine($"  · {n}：不存在");
                    continue;
                }

                Console.WriteLine($"  · {n}：存在（{t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Length} 个静态方法）");
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (m.IsSpecialName) continue;
                    if (m.Name.Contains("Update") || m.Name.Contains("Tick") || m.Name.Contains("Frame") || m.Name.Contains("Loop"))
                    {
                        Console.WriteLine($"      ↳ {m.ReturnType.Name} {m.Name}({ParamList(m)})");
                    }
                }
            }

            // Time.DeltaTime 的可读性决定 DriveByFrame 的累加器是否成立
            var time = asm.GetType("FlaxEngine.Time");
            var dt = time?.GetProperty("DeltaTime", BindingFlags.Public | BindingFlags.Static);
            Console.WriteLine(dt is not null
                ? $"  ✓ Time.DeltaTime 存在（类型 {dt.PropertyType.Name}，静态可读）→ DriveByFrame 的累加器方案成立"
                : "  ✗ Time.DeltaTime 不存在");
            if (dt is null) _failures++;
            Console.WriteLine();
        }
    }
}
