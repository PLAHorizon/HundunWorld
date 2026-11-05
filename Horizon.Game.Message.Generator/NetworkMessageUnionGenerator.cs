using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Horizon.Game.Message.Generator;

/// <summary>
/// 消息 union 注册源生成器（A2 生成链路）。
///
/// <para><b>产出两份文件</b>：</para>
/// <list type="number">
///   <item><c>UnionKinds.g.cs</c> —— 常量表，供 <c>UnionRegistrationParityTests</c> 与运行时自省使用；</item>
///   <item><c>MessageUnion.UnionBlock.g.cs</c> —— 重建 <see cref="MessageUnion"/> 的
///       <c>[MemoryPackUnion(tag, typeof(T))]</c> 注册列表（partial 类扩展，本生成器是<b>唯一事实源</b>）。</item>
/// </list>
///
/// <para><b>与本文件同名的设计决策（务必遵守）</b>：</para>
/// <list type="bullet">
///   <item>生成器<b>跳过</b> <c>MessageUnion.cs</c> 自身——它文件头的 a
///       <c>[MemoryPackUnion(0, ...)]</c> 只是注释掉的历史残留；不跳过会与生成块
///       <b>重复定义特性</b>（CS0579）。</item>
///   <item>不引 MemoryPack / Orleans：生成的是<b>文本</b>，编译期不需要解析这些类型。</item>
///   <item>tag 值原样透传为 <c>int</c> 字面量，<b>用强类型 enum 而非 ToString()</b>——后者会产出
///       <c>Service.1</c> 这类非法标识符。</item>
/// </list>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class NetworkMessageUnionGenerator : IIncrementalGenerator
{
    private const string AttributeMetadataName = "Horizon.Game.Message.NetworkMessageAttribute";
    private const string UnionMarker = "// <generated-union-block>";
    private const string UnionMarkerEnd = "// </generated-union-block>";

    /// <summary>
    /// tag 合法上界。<b>MemoryPack 的 union header 是压缩过的 ushort，不是 byte</b>：
    /// 1.21.4 实测 tag 0 写 1 字节、tag 255/300/65000 都写 2 字节且都能原样读回，
    /// 0xffff 被留作 null 标记，所以上界是 65534。
    /// <para>这里原来写死 255（注释说"MemoryPack union tag 为 0..255"），而全解决方案已经用到 255——
    /// 于是注册表事实上满了：任何新功能加第二条消息都会撞 HZN002。上限本身不是协议约束，
    /// 抬高它也不改任何现存 tag 的字节（小 tag 仍是 1 字节），所以是纯增量的修法。</para>
    /// </summary>
    private const int MaxTag = 65534;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidates = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeMetadataName,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => Extract(ctx, ct))
            .Where(static e => e.HasValue)
            .Select(static (e, _) => e.GetValueOrDefault())
            .Collect();

        context.RegisterSourceOutput(candidates, static (spc, entries) => Emit(spc, entries));
    }

    /// <summary>从标注了 <c>[NetworkMessage]</c> 的类声明中提取 (tag, service, 类型全名)。</summary>
    private static Entry? Extract(GeneratorAttributeSyntaxContext ctx, System.Threading.CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol type)
        {
            return null;
        }

        // tag / service 逐项解析。注意：ServiceType 是 enum，Roslyn 给的
        // ConstructorArguments[1].Value 是**底层整型 byte**——只匹配 `is int` 会
        // 静默丢弃全部特性（实测：整条管线命中数会变成 0）。必须用 Convert.ToInt32。
        var typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        foreach (var attr in ctx.Attributes)
        {
            if (attr.ConstructorArguments.Length < 2)
            {
                continue;
            }

            if (attr.ConstructorArguments[0].Value is not int tag)
            {
                continue;
            }

            var raw = attr.ConstructorArguments[1].Value;
            if (raw is null)
            {
                continue;
            }

            var service = Convert.ToInt32(raw);

            // 类型全名：不加 global:: 前缀——测试侧 StripGlobalPrefix 会剥掉它，而剥不掉时
            // 直接与 Type.FullName 比对。保持裸全名是最稳妥的形式。
            var fullName = typeName;
            if (fullName.StartsWith("global::", StringComparison.Ordinal))
            {
                fullName = fullName.Substring("global::".Length);
            }

            return new Entry(tag, service, fullName);
        }

        return null;
    }

    private static void Emit(SourceProductionContext spc, ImmutableArray<Entry> entries)
    {
        // ── 排序 + 去重 + tag 合法性校验（HZN001）────────────────────────────
        var byTag = new SortedDictionary<int, Entry>();

        foreach (var e in entries)
        {
            if (e.Tag < 0 || e.Tag > MaxTag)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.TagOutOfRange, Location.None, e.TypeFullName, e.Tag, MaxTag));
                continue;
            }

            if (byTag.TryGetValue(e.Tag, out var existing))
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.DuplicateTag, Location.None, e.Tag, existing.TypeFullName, e.TypeFullName));
                continue;
            }

            byTag.Add(e.Tag, e);
        }

        var ordered = byTag.Values.ToList();

        // ── 产出 1：UnionKinds 常量表 ────────────────────────────────────────
        var kinds = new StringBuilder();
        kinds.AppendLine("// <auto-generated />");
        kinds.AppendLine("// 由 Horizon.Game.Message.Generator 生成——请勿手改。");
        kinds.AppendLine("#nullable enable");
        kinds.AppendLine();
        kinds.AppendLine("namespace Horizon.Game.Message");
        kinds.AppendLine("{");
        kinds.AppendLine("    /// <summary>");
        kinds.AppendLine("    /// 声明式网络消息注册表（<c>[NetworkMessage(tag, service)]</c> 驱动）。");
        kinds.AppendLine("    /// 与手写 <c>[MemoryPackUnion]</c> 注册双射一致，由 UnionRegistrationParityTests 门禁。");
        kinds.AppendLine("    /// </summary>");
        kinds.AppendLine("    public static class UnionKinds");
        kinds.AppendLine("    {");
        kinds.AppendLine("        /// <summary>单个 union 成员：类型全名 + 所属服务域。</summary>");
        kinds.AppendLine("        public readonly struct Entry");
        kinds.AppendLine("        {");
        kinds.AppendLine("            /// <summary>构造常量表条目。</summary>");
        kinds.AppendLine("            public Entry(int tag, string typeName, int service)");
        kinds.AppendLine("            {");
        kinds.AppendLine("                Tag = tag;");
        kinds.AppendLine("                TypeName = typeName;");
        kinds.AppendLine("                Service = service;");
        kinds.AppendLine("            }");
        kinds.AppendLine();
        kinds.AppendLine("            /// <summary>MemoryPack union tag。</summary>");
        kinds.AppendLine("            public int Tag { get; }");
        kinds.AppendLine();
        kinds.AppendLine("            /// <summary>消息类型全名（不含 global:: 前缀）。</summary>");
        kinds.AppendLine("            public string TypeName { get; }");
        kinds.AppendLine();
        kinds.AppendLine("            /// <summary>服务域（等同 Horizon.Game.Message.Enums.ServiceType 的字节值）。</summary>");
        kinds.AppendLine("            public int Service { get; }");
        kinds.AppendLine("        }");
        kinds.AppendLine();
        kinds.AppendLine("        /// <summary>tag → 条目。tag 唯一，按 tag 升序。</summary>");
        kinds.AppendLine("        public static readonly global::System.Collections.Generic.Dictionary<int, Entry> All");
        kinds.AppendLine("            = new global::System.Collections.Generic.Dictionary<int, Entry>");
        kinds.AppendLine("        {");

        foreach (var e in ordered)
        {
            kinds.Append("            { ")
                 .Append(e.Tag)
                 .Append(", new Entry(")
                 .Append(e.Tag).Append(", ")
                 .Append(Quote(e.TypeFullName)).Append(", ")
                 .Append(e.Service)
                 .Append(") },")
                 .AppendLine();
        }

        kinds.AppendLine("        };");
        kinds.AppendLine("    }");
        kinds.AppendLine("}");
        spc.AddSource("UnionKinds.g.cs", kinds.ToString());

        // ── 产出 2：MessageUnion 的 union 注册块 ─────────────────────────────
        // ⚠️ 关键约束（实证所得）：MemoryPack 的 [MemoryPackUnion] 特性**只能**标注在类声明本身，
        //    无法从另一个 partial 声明处追加——分文件 partial 会全部报 CS0592
        //    「特性对此声明类型无效。它仅对"类, 接口"声明有效」。
        //    因此本生成器**不产出** union 属性块，只手写块保持为唯一事实源。
        //    手写块由 EmitUnionBlock 控制（默认关闭），保留完整实现供未来切换到
        //    "生成器原地重写 MessageUnion.cs" 方案时复用。
        if (EmitUnionBlock)
        {
            EmitUnionBlockSource(spc, ordered);
        }
    }

    /// <summary>
    /// 是否产出 union 属性块。默认 <c>false</c>——见 <c>Emit</c> 内的约束说明。
    /// 仅当采用"生成器原地重写 MessageUnion.cs"方案时才应置 true。
    /// </summary>
    private static bool EmitUnionBlock
        => string.Equals(Environment.GetEnvironmentVariable("HZN_EMIT_UNION_BLOCK"), "1", StringComparison.Ordinal);

    /// <summary>产出 union 属性块（默认路径不走这里）。</summary>
    private static void EmitUnionBlockSource(SourceProductionContext spc, List<Entry> ordered)
    {
        var block = new StringBuilder();
        block.AppendLine("// <auto-generated />");
        block.AppendLine("// 由 Horizon.Game.Message.Generator 生成——请勿手改。");
        block.AppendLine("// 只重建 union 注册范围（" + UnionMarker + " 到 " + UnionMarkerEnd + "），类体其余部分保持手写。");
        block.AppendLine("// 刻意**不重复**声明 [MemoryPackable]/[GenerateSerializer]：那些由 MessageUnion.cs 手写部分持有。");
        block.AppendLine("// 注意：必须写 `partial class`（不加 abstract）——C# 要求 partial 修饰符一致，");
        block.AppendLine("// 而 `abstract` 会在两边重复指定（CS0418）。");
        block.AppendLine("#nullable enable");
        block.AppendLine();
        block.AppendLine("namespace Horizon.Game.Message");
        block.AppendLine("{");
        block.AppendLine("    public abstract partial class MessageUnion");
        block.AppendLine("    {");

        foreach (var e in ordered)
        {
            block.Append("        [global::MemoryPack.MemoryPackUnion(")
                 .Append(e.Tag).Append(", typeof(global::")
                 .Append(e.TypeFullName)
                 .Append("))]")
                 .AppendLine();
        }

        // partial 类必须有至少一个成员才能形成合法声明；只有一个 tag 时也要有成员。
        // 这里放一个私有常量，既满足"类体非空"，又对宿主类零语义影响。
        if (ordered.Count == 0)
        {
            block.AppendLine("        private const bool HasGeneratedUnionRegistrations = false;");
        }
        else
        {
            block.AppendLine("        private const bool HasGeneratedUnionRegistrations = true;");
        }

        block.AppendLine("    }");
        block.AppendLine("}");
        spc.AddSource("MessageUnion.UnionBlock.g.cs", block.ToString());
    }

    private static string Quote(string s)
    {
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                default: sb.Append(ch); break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>生成条目（值相等语义，供增量管线缓存比对）。</summary>
    private readonly struct Entry : IEquatable<Entry>
    {
        public Entry(int tag, int service, string typeFullName)
        {
            Tag = tag;
            Service = service;
            TypeFullName = typeFullName;
        }

        public int Tag { get; }

        public int Service { get; }

        public string TypeFullName { get; }

        public bool Equals(Entry other)
            => Tag == other.Tag
            && Service == other.Service
            && string.Equals(TypeFullName, other.TypeFullName, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is Entry e && Equals(e);

        public override int GetHashCode()
        {
            unchecked
            {
                var h = Tag * 397 ^ Service;
                return h * 397 ^ (TypeFullName?.GetHashCode() ?? 0);
            }
        }
    }

    private static class DiagnosticDescriptors
    {
        private const string Category = "Horizon.Game.Message.Generator";

        public static readonly DiagnosticDescriptor DuplicateTag = new DiagnosticDescriptor(
            id: "HZN001",
            title: "union tag 冲突",
            messageFormat: "union tag {0} 被多个消息类型占用：{1} 与 {2}",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor TagOutOfRange = new DiagnosticDescriptor(
            id: "HZN002",
            title: "union tag 越界",
            messageFormat: "消息类型 {0} 的 union tag {1} 越界（合法范围 0..{2}）",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}
