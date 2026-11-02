// PCG 插件 - 内建图元的名字表
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.39。

using System;
using System.Collections.Generic;

namespace PCG.Graph
{
    /// <summary>
    /// 描述层里可以直接写的六个**引擎内建图元**名字（<c>model "Cone"</c>），以及它们对应的资产 Guid。
    /// </summary>
    /// <remarks>
    /// 为什么要有这张表：描述里此前只有两种写法，两种都有代价——
    /// ① 写工程内相对路径（<c>Content/PCG/Models/Pine.flax</c>），本仓库没有这些资产，于是那几份描述
    /// 编得出图、长不出东西；② 写引擎安装目录下的绝对路径，能长东西，但**整份描述就此钉死在这台机器上**
    /// （编译期哨兵点名的就是它）。把"引擎自带的图元"收成六个可以在描述里直接写的名字，两个代价一起去掉：
    /// 换机器、换引擎安装目录都不影响，而随仓库交出去的描述第一次是真能跑的。
    ///
    /// **这是一条被检查的假设，不是一句信仰**：Guid 是从引擎自带 <c>.flax</c> 的资产头里逐条量出来的
    /// （不是猜的），而引擎自检第 29 组那条 <c>TestColdAssetResolution</c> 走的就是其中一枚；
    /// 表里六个名字另有离屏断言钉住"能解析成 Guid、彼此不同"。引擎换版本如果动了某枚 Guid，
    /// 后果是那一层的模型取不到 → 生成器报「没有指定模型」并且**点数照旧**，
    /// 所以运行侧的守卫（摆出 0 件时点名）会把这件事说出来，而不是静默少长一片树。
    ///
    /// 名字**不需要宿主解析器**：<c>PCGSpecCompiler</c> 先查这张表，查不到才交给注入的
    /// <c>Func&lt;string, Guid&gt;</c>——所以编辑器和命令行两个入口对同一份描述给出同一个结果。
    /// </remarks>
    public static class PCGBuiltinModels
    {
        /// <summary>表里六个名字的书写顺序（断言与报错都按它，别按字典序改动）。</summary>
        public static readonly string[] Names =
        {
            "Capsule", "Cone", "Cube", "Cylinder", "Plane", "Sphere",
        };

        private static readonly Dictionary<string, Guid> Map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
        {
            { "Capsule", new Guid("3f82e577-7c28-44f5-b650-51289821ad46") },
            { "Cone", new Guid("1b4a643d-6750-4470-a45e-e3b263361ee1") },
            { "Cube", new Guid("b43f0f8f-a3f3-4aab-a596-681593a42ba2") },
            { "Cylinder", new Guid("223aa989-9737-4e90-8650-ab8026755121") },
            { "Plane", new Guid("54a1ff4a-9f01-42af-b3f7-3482b366139b") },
            { "Sphere", new Guid("dd86c472-34c4-4bf7-b323-9d9505aa4fc7") },
        };

        /// <summary>这个名字是内建图元吗：是就把 Guid 交出去。</summary>
        public static bool TryResolve(string name, out Guid id)
        {
            id = Guid.Empty;

            if (string.IsNullOrEmpty(name))
                return false;

            return Map.TryGetValue(name, out id);
        }

        /// <summary>表里这一枚 Guid；名字不在表里给 <see cref="Guid.Empty"/>。</summary>
        public static Guid IdOf(string name)
        {
            return TryResolve(name, out var id) ? id : Guid.Empty;
        }
    }
}
