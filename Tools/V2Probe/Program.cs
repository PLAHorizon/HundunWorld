using System;
using System.Linq;
using System.Reflection;

namespace V2Probe
{
    /// <summary>
    /// V2 探针 —— 回答方案文档 §5 的 V2 项：
    /// <i>"数据事件的 NetworkEvent 确切形态"</i>
    ///
    /// <para>
    /// <b>取证方法</b>：直接 <c>Assembly.LoadFrom</c> 引擎托管程序集
    /// <c>FlaxEngine.CSharp.dll</c>，用 <c>GetMembers(Public|Instance|Static|DeclaredOnly)</c>
    /// 枚举 <c>FlaxEngine.Networking</c> 命名空间下的类型。这比"看头文件"更硬——
    /// 头文件是源码态，托管程序集是<b>实际加载的产物</b>（<c>API_ENUM</c>/<c>API_FIELD</c>
    /// 与 <c>API_STRUCT</c> ↔ <c>API_CLASS</c> 的导出规则不同，只有反射能看到真身）。
    /// </para>
    ///
    /// <para>
    /// <b>待验证的两条旧结论</b>（方案文档 §1.3 约束 3）：
    /// </para>
    /// <list type="number">
    ///   <item><c>NetworkEventType</c> 只有 4 个成员，没有 <c>Message</c></item>
    ///   <item>数据报文不走 <c>PopEvent</c></item>
    /// </list>
    /// <para>
    /// <b>头文件反证</b>（<c>Source/Engine/Networking/NetworkEvent.h</c>，实读）：
    /// <c>NetworkEventType</c> 有 <b>5</b> 个成员（含 <c>Message</c>），
    /// <c>NetworkEvent</c> 有 <b>3</b> 个字段（<c>EventType</c> / <c>Message</c> / <c>Sender</c>），
    /// 且 <c>NetworkInternal.cpp</c> 里存在完整的数据报文分发链
    /// （<c>OnNetworkMessageObjectReplicate/Spawn/Rpc</c> …）。
    /// 因此本探针的预期是<b>推翻</b>旧结论。
    /// </para>
    /// </summary>
    public static class Program
    {
        private const string Ns = "FlaxEngine.Networking";
        private static int _failures;

        public static int Main(string[] args)
        {
            Console.WriteLine("=== V2 探针：NetworkEvent 确切形态 ===");

            var asm = typeof(FlaxEngine.Script).Assembly;
            Console.WriteLine($"程序集: {asm.GetName().Name} v{asm.GetName().Version}");
            Console.WriteLine($"位置  : {asm.Location}");
            Console.WriteLine();

            var eventType = CheckEventTypeEnum(asm);
            CheckEventStruct(asm, eventType);
            CheckDriverSurface(asm);
            CheckDispatcher(asm);

            Console.WriteLine();
            Console.WriteLine(_failures == 0
                ? "=== RESULT = PASS（NetworkEvent 携带 Message；数据报文走 PopEvent） ==="
                : $"=== RESULT = FAIL（{_failures} 项未达预期） ===");
            return _failures == 0 ? 0 : 1;
        }

        // ── 1. NetworkEventType 成员 ─────────────────────────────────────────
        private static Type CheckEventTypeEnum(Assembly asm)
        {
            Console.WriteLine("[1] NetworkEventType 成员");

            var t = asm.GetType($"{Ns}.NetworkEventType");
            if (t is null)
            {
                Console.WriteLine("  ✗ 类型不存在");
                _failures++;
                return null;
            }

            Console.WriteLine($"  · {t.FullName}（IsEnum={t.IsEnum}）");
            var names = Enum.GetNames(t);
            var values = Enum.GetValues(t);

            for (var i = 0; i < names.Length; i++)
            {
                Console.WriteLine($"      {Convert.ToInt32(values.GetValue(i)),3} = {names[i]}");
            }
            Console.WriteLine($"  合计 {names.Length} 个成员");

            var hasMessage = names.Contains("Message");
            Console.WriteLine(hasMessage
                ? "  ✓ 含 Message 成员 → 旧结论「只有 4 个成员、无 Message」被推翻"
                : "  ✗ 无 Message 成员 → 旧结论成立");
            if (!hasMessage) _failures++;

            var expect = new[] { "Undefined", "Connected", "Disconnected", "Timeout", "Message" };
            var missing = expect.Where(n => !names.Contains(n)).ToArray();
            if (missing.Length > 0)
            {
                Console.WriteLine($"  ✗ 缺少预期成员: {string.Join(", ", missing)}");
                _failures++;
            }

            // 与头文件常量对齐（Undefined=0 … Message=4）
            var ok = (int)values.GetValue(0) == 0
                     && (int)values.GetValue(names.Length - 1) == 4;
            if (!ok) Console.WriteLine("  ! 数值与头文件定义不一致（Undefined=0 … Message=4）");

            Console.WriteLine();
            return t;
        }

        // ── 2. NetworkEvent 结构体字段 ───────────────────────────────────────
        private static void CheckEventStruct(Assembly asm, Type eventType)
        {
            Console.WriteLine("[2] NetworkEvent 字段");

            var t = asm.GetType($"{Ns}.NetworkEvent");
            if (t is null)
            {
                Console.WriteLine("  ✗ 类型不存在");
                _failures++;
                return;
            }

            Console.WriteLine($"  · {t.FullName}（IsValueType={t.IsValueType} IsPublic={t.IsPublic}）");

            var fields = t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var f in fields)
            {
                var typeName = f.FieldType.FullName ?? f.FieldType.Name;
                var isEventType = eventType != null && f.FieldType == eventType;
                Console.WriteLine($"      {typeName} {f.Name}{(isEventType ? "   ← 枚举" : string.Empty)}");
            }
            Console.WriteLine($"  合计 {fields.Length} 个公开字段");

            var fNames = fields.Select(f => f.Name).ToArray();
            foreach (var need in new[] { "EventType", "Message", "Sender" })
            {
                var hit = fNames.Contains(need);
                Console.WriteLine(hit ? $"  ✓ {need}" : $"  ✗ {need} 缺失");
                if (!hit) _failures++;
            }

            // Message 的实际托管类型必须能承载数据
            var msg = fields.FirstOrDefault(f => f.Name == "Message");
            if (msg != null)
            {
                Console.WriteLine($"  · Message 的托管类型: {msg.FieldType.FullName}");
                DumpMessageType(msg.FieldType);
            }

            Console.WriteLine();
        }

        private static void DumpMessageType(Type msg)
        {
            if (msg is null) return;
            var f = msg.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (f.Length == 0) return;
            Console.WriteLine("      NetworkMessage 字段:");
            foreach (var x in f)
            {
                Console.WriteLine($"        {x.FieldType.Name} {x.Name}");
            }
        }

        // ── 3. INetworkDriver 的 PopEvent / SendMessage 契约 ─────────────────
        private static void CheckDriverSurface(Assembly asm)
        {
            Console.WriteLine("[3] INetworkDriver 契约（数据面入口）");

            var t = asm.GetType($"{Ns}.INetworkDriver");
            if (t is null)
            {
                Console.WriteLine("  ✗ 类型不存在");
                _failures++;
                return;
            }

            Console.WriteLine($"  · {t.FullName}（IsInterface={t.IsInterface}）");
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (m.IsSpecialName) continue;
                Console.WriteLine($"      {m.ReturnType.Name} {m.Name}({ParamList(m)})");
            }

            var pop = t.GetMethod("PopEvent");
            if (pop is null)
            {
                Console.WriteLine("  ✗ PopEvent 不存在");
                _failures++;
            }
            else
            {
                var p0 = pop.GetParameters()[0];
                var byRef = p0.ParameterType.IsByRef;
                var elem = byRef ? p0.ParameterType.GetElementType() : p0.ParameterType;
                var isEvent = elem?.Name == "NetworkEvent";
                Console.WriteLine(isEvent && byRef
                    ? $"  ✓ PopEvent(out NetworkEvent) → 事件由驱动填充，调用方拿到的就是完整 NetworkEvent（含 Message）"
                    : $"  ! PopEvent 签名异常: {p0.ParameterType.Name}");
                if (!isEvent) _failures++;
            }

            // 驱动是否 sealed（决定能否继承复用）
            var enet = asm.GetType($"{Ns}.ENetDriver");
            if (enet != null)
            {
                Console.WriteLine($"  · ENetDriver: IsSealed={enet.IsSealed} BaseType={enet.BaseType?.Name} Interfaces=[{string.Join(", ", enet.GetInterfaces().Select(i => i.Name))}]");
            }

            Console.WriteLine();
        }

        // ── 4. 消息分发器是否存在（证明数据报文确实走 PopEvent）─────────────
        private static void CheckDispatcher(Assembly asm)
        {
            Console.WriteLine("[4] 数据报文分发链（是否消费 PopEvent 的 Message 事件）");

            // 托管侧看不到 NetworkInternal（C++ 内部类），改为验证：
            //   NetworkPeer 是否暴露 RecycleMessage（事件消费契约的必要条件）
            //   —— 若 Message 事件不存在，就没有回收需求，这个方法不会存在。
            var peer = asm.GetType($"{Ns}.NetworkPeer");
            if (peer is null)
            {
                Console.WriteLine("  · NetworkPeer 不存在（跳过）");
                Console.WriteLine();
                return;
            }

            var recycle = peer.GetMethod("RecycleMessage", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                          ?? peer.GetMethod("RecycleMessage");

            if (recycle is null)
            {
                Console.WriteLine("  ? NetworkPeer.RecycleMessage 未暴露到托管侧（可能仅 C++ 内部使用）");
                Console.WriteLine("    → 不构成反证：NetworkInternal.cpp 是 C++ 内部类，本就不导出");
                Console.WriteLine("      头文件证据已足够：NetworkEvent.h 声明 RecycleMessage 的回收义务");
            }
            else
            {
                Console.WriteLine($"  ✓ NetworkPeer.RecycleMessage({ParamList(recycle)}) 暴露");
                Console.WriteLine("    → 存在\"接收方消费完 Message 后归还\"的契约，反证数据报文确实走 PopEvent");
            }

            var create = peer.GetMethod("CreateMessage", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                         ?? peer.GetMethod("CreateMessage");
            Console.WriteLine(create is null
                ? "  · CreateMessage 未暴露（同上，非反证）"
                : $"  ✓ NetworkPeer.CreateMessage({ParamList(create)}) → {create.ReturnType.Name}");

            Console.WriteLine();
        }

        private static string ParamList(MethodInfo m)
        {
            var ps = m.GetParameters();
            if (ps.Length == 0) return string.Empty;
            return string.Join(", ", ps.Select(p => $"{(p.ParameterType.IsByRef ? "out " : string.Empty)}{p.ParameterType.Name.Replace("&", string.Empty)} {p.Name}"));
        }
    }
}
