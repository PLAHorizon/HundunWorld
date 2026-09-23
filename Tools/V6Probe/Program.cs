using System;
using System.Linq;
using System.Reflection;

// V6Probe：静态反射判定 FlaxEngine 的 Plugin / Script 类型契约。
// 目的：确认「无场景的 ServerTarget 里，Plugin 子类能否被引擎实例化」。
internal static class Program
{
    private static int Main()
    {
        Console.WriteLine("=== V6Probe: Plugin vs Script 承载契约判定 ===\n");

        // 直接复用进程已加载的引擎程序集（csproj 已 Reference），
        // 避免 LoadFrom 的相对路径解析歧义。
        Assembly asm = typeof(FlaxEngine.Object).Assembly;

        Console.WriteLine($"程序集: {asm.FullName}\n");

        Dump("FlaxEngine.Plugin");
        Console.WriteLine(new string('-', 70));
        Dump("FlaxEngine.Script");
        Console.WriteLine(new string('-', 70));
        Dump("FlaxEngine.GamePlugin");
        Console.WriteLine(new string('-', 70));
        Dump("FlaxEngine.ScriptingObject");
        Console.WriteLine(new string('-', 70));
        Dump("FlaxEngine.PluginManager");

        return 0;
    }

    private static void Dump(string typeName)
    {
        Type t = null;
        try
        {
            t = typeof(FlaxEngine.Object).Assembly.GetType(typeName, false);
        }
        catch { }

        if (t == null)
        {
            Console.WriteLine($"[{typeName}]  ★ 类型不存在");
            return;
        }

        Console.WriteLine($"[{typeName}]");
        Console.WriteLine($"  基类      : {t.BaseType?.FullName ?? "<none>"}");
        Console.WriteLine($"  是抽象类  : {t.IsAbstract}");
        Console.WriteLine($"  是密封类  : {t.IsSealed}");
        Console.WriteLine($"  可见性    : {(t.IsPublic ? "public" : "internal")}");

        // 沿继承链打印，识别「是否需要场景」的决定性祖先
        var chain = new System.Collections.Generic.List<string>();
        for (var c = t; c != null && c != typeof(object); c = c.BaseType)
            chain.Add(c.Name);
        Console.WriteLine($"  继承链    : {string.Join(" -> ", chain)}");

        // 是否可达 SceneObject（= 需要场景挂载）
        bool reachesSceneObject = false;
        try { reachesSceneObject = typeof(FlaxEngine.SceneObject).IsAssignableFrom(t); } catch { }
        Console.WriteLine($"  ★ 可达 SceneObject（需场景）: {reachesSceneObject}");

        // 生命周期虚方法
        var virt = t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.IsVirtual && !m.IsFinal && m.DeclaringType == t)
                    .Select(m => m.Name)
                    .Distinct().OrderBy(x => x).ToArray();
        Console.WriteLine($"  自身可重写虚方法: {(virt.Length == 0 ? "<none>" : string.Join(", ", virt))}");

        // 构造函数可见性（引擎需要能 new 出来）
        var ctors = t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var c in ctors)
            Console.WriteLine($"  构造函数  : {(c.IsPublic ? "public" : c.IsFamily ? "protected" : "non-public")} .ctor({c.GetParameters().Length} params)");

        Console.WriteLine();
    }
}
