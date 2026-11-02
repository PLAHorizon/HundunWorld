using System.Reflection;
using System.Runtime.Loader;
using Horizon.Game.Message.Network;
using MemoryPack;

// 目的：判断"新客户端读旧服务端载荷（缺尾部 Revision）"会不会炸。
// 旧 DLL 用独立 ALC 载入，序列化出旧格式字节；再用新类型反序列化。
string oldPath = args.Length > 0 ? args[0] : "";
if (string.IsNullOrEmpty(oldPath) || !File.Exists(oldPath))
{
    Console.WriteLine("FAIL: 缺少旧版 Horizon.Game.Message.dll 路径");
    return 1;
}

var alc = new AssemblyLoadContext("old-message", isCollectible: true);
Assembly asm = alc.LoadFromAssemblyPath(Path.GetFullPath(oldPath));
Type oldType = asm.GetType("Horizon.Game.Message.Network.MetaHumanLookPayload", throwOnError: true);
object old = Activator.CreateInstance(oldType);

void Set(string name, object value) => oldType.GetProperty(name).SetValue(old, value);
Set("TemplateId", "metahuman.face_archetype");
Set("TemplateVersion", "1");
Set("TemplateParamCount", 3);
Set("EntryCount", 1);
Set("Blob", new byte[] { 1, 2, 3, 4 });
Set("BlobChecksum", 0xABCD1234u);
Set("Published", true);
Set("SavedAtUtcTicks", 638000000000000000L);

byte[] oldBytes = MemoryPackSerializer.Serialize(oldType, old);
Console.WriteLine($"old payload bytes = {oldBytes.Length}");

// ① 新读旧：这就是"服务端还没重启"时的方向。
try
{
    object round = MemoryPackSerializer.Deserialize(typeof(MetaHumanLookPayload), oldBytes);
    var typed = (MetaHumanLookPayload)round;
    Console.WriteLine($"NEW<-OLD ok: ticks={typed.SavedAtUtcTicks}, published={typed.Published}");
}
catch (Exception ex)
{
    Console.WriteLine($"NEW<-OLD FAIL: {ex.GetType().Name}: {ex.Message}");
}

// ② 旧读新：服务端重启前的反方向（旧服务端读新客户端载荷）。
var mine = new MetaHumanLookPayload
{
    TemplateId = "t",
    SavedAtUtcTicks = 5L,
    Blob = new byte[] { 9 },
};
byte[] newBytes = MemoryPackSerializer.Serialize(mine);
try
{
    object back = MemoryPackSerializer.Deserialize(oldType, newBytes);
    Console.WriteLine($"OLD<-NEW ok: {oldType.GetProperty("TemplateId").GetValue(back)} (header slots=8)");
}
catch (Exception ex)
{
    Console.WriteLine($"OLD<-NEW FAIL: {ex.GetType().Name}: {ex.Message}");
}

return 0;
