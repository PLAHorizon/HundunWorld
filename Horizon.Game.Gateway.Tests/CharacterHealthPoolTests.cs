using System;
using System.IO;
using Horizon.Game.Core.Combat;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 生命上限基值的两端一致性。
/// <para>
/// 起因是查"施法者已死亡"为什么挡下所有技能：角色表 <c>max_health</c> 一直是 0
/// （建号时写的是 100，而客户端按 1000 起算，且没有任何路径把聚合结果写回那一列），
/// 于是服务端的死亡/复活/克制全都在读一个不存在的血池，而玩家看到的血条是满的。
/// 这类"同一个数字两处各写一份"的偏差不会因为某一边改对而消失——
/// 只有把基值收进 <see cref="AttributeDerivation"/> 并让两端可被对账，才算收口。
/// </para>
/// </summary>
public class CharacterHealthPoolTests
{
    private static readonly string[] ClientAttributeFiles =
    {
        Path.Combine("HundunWorld", "Source", "Game", "Character", "Attributes", "CharacterAttributesComponent.cs"),
    };

    [Fact]
    public void HealthBase_IsNamed_AndMatchesTheClientPreviewMirror()
    {
        Assert.True(AttributeDerivation.HealthBaseValue > 0f, "服务端必须有一个命名的生命基值");
        Assert.Equal(AttributeDerivation.EnergyBaseValue, AttributeDerivation.HealthBaseValue,
            2);   // 血与蓝同为"基值 + 加点 + 装备"结构，量级不该一边 100 一边 1000

        var root = FindRepoRoot();
        var clientFile = Path.Combine(root, ClientAttributeFiles[0]);
        if (!File.Exists(clientFile)) return;      // 客户端源码不在工作区时不断言（服务端侧的命名仍是主证据）

        var text = File.ReadAllText(clientFile);
        var match = System.Text.RegularExpressions.Regex.Match(text, @"_maxHealthBase\s*=\s*([0-9]+(?:\.[0-9]+)?)f");
        Assert.True(match.Success,
            "客户端里找不到 _maxHealthBase 的初值了：这一端的存在是本测试的全部意义，改名请同步本测试与 AttributeDerivation 的注释");

        var clientBase = float.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(Math.Abs(clientBase - AttributeDerivation.HealthBaseValue) < 0.01f,
            $"生命基值两端分叉：客户端 {clientBase} vs 服务端 {AttributeDerivation.HealthBaseValue}。" +
            "改哪一端都行，但只能有一处定义（服务端是权威，客户端那份是预览镜像）。");
    }

    [Fact]
    public void ServerSide_HealthPoolIsNotHardcodedAnywhereElse()
    {
        // 100f 这类字面量散在 grains 里，就是这次"血池三个数"的成因。新增硬编码要在这里被拦下。
        var root = FindRepoRoot();
        var offenders = new System.Collections.Generic.List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "Horizon.Orleans.Grains"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Replace('\\', '/').Contains("/obj/")) continue;
            foreach (var raw in File.ReadAllLines(file))
            {
                var line = raw.Trim();
                if (line.StartsWith("//", StringComparison.Ordinal)) continue;
                if (line.Contains("MaxHealth = 100f", StringComparison.Ordinal)
                    || line.Contains("Health = 100f", StringComparison.Ordinal))
                {
                    offenders.Add(Path.GetRelativePath(root, file));
                }
            }
        }

        Assert.True(offenders.Count == 0,
            $"这些文件里还在把生命上限硬编码成 100：{string.Join(", ", offenders)}。改用 AttributeDerivation.HealthBaseValue。");
    }

    [Fact]
    public void DerivedHealthPool_FollowsBasePlusAllocation_AndHonoursAnAuthoritativeRow()
    {
        var empty = new Horizon.Game.Message.Network.AttributeAllocationData();
        Assert.Equal(AttributeDerivation.HealthBaseValue,
            AttributeDerivation.DeriveMaxHealth(empty), 3);

        var alloc = new Horizon.Game.Message.Network.AttributeAllocationData
        {
            Strength = 2, Health = 3, Wood = 1,
        };
        var expected = AttributeDerivation.HealthBaseValue
                       + 2 * AttributeDerivation.StrengthHealthPerPoint
                       + 3 * AttributeDerivation.ConstitutionHealthPerPoint
                       + 1 * AttributeDerivation.WoodHealthPerPoint;
        Assert.Equal(expected, AttributeDerivation.DeriveMaxHealth(alloc), 3);

        // 表里已有正数上限 = 那一列被权威写过，基值不能再叠第二遍（只加加成与装备）
        Assert.Equal(5000f + 2 * AttributeDerivation.StrengthHealthPerPoint
                         + 3 * AttributeDerivation.ConstitutionHealthPerPoint
                         + 1 * AttributeDerivation.WoodHealthPerPoint + 250f,
                     AttributeDerivation.DeriveMaxHealth(alloc, rowMaxHealth: 5000f, equipmentMaxHealth: 250f), 3);

        // 表里的 0 是"没写过"，不是"上限为 0"
        Assert.Equal(AttributeDerivation.HealthBaseValue,
            AttributeDerivation.DeriveMaxHealth(null, rowMaxHealth: 0f), 3);
    }

    [Fact]
    public void TheDerivation_IsActuallyConsumedByTheGrains()
    {
        // 纯函数存在不等于有人调它：本仓已经因为"数据里有、代码不判"断过两次链
        // （§6.2 的计数器、天阶书的 worldBoss 通道）。算法收口之后必须确认调用点也在。
        var root = FindRepoRoot();
        var grainFiles = new[]
        {
            Path.Combine(root, "Horizon.Orleans.Grains", "CharacterGrain.cs"),
            Path.Combine(root, "Horizon.Orleans.Grains", "CombatGrain.cs"),
        };

        var consumed = 0;
        foreach (var file in grainFiles)
        {
            if (!File.Exists(file)) continue;
            var text = File.ReadAllText(file);
            if (text.Contains("DeriveMaxHealth", StringComparison.Ordinal)
                || text.Contains("AttributeDerivation.HealthBaseValue", StringComparison.Ordinal)) consumed++;
        }

        Assert.True(consumed == grainFiles.Length,
            $"生命池算法只有一个定义点，但 {grainFiles.Length - consumed} 个 Grain 没在用它：" +
            "各自写一份缺省值，就是三个血池数那次缺陷复发的前兆");
    }

    [Fact]
    public void 血池重算必须把装备生命算进去()
    {
        // 真实缺陷：装备写着 +5000 生命，血条仍 100/100。
        // 纯函数支持 equipmentMaxHealth 早就有了，坏在调用点只传了加点。
        var root = FindRepoRoot();
        var grain = File.ReadAllText(Path.Combine(root, "Horizon.Orleans.Grains", "CharacterGrain.cs"));
        Assert.Contains("GatherEquipmentAsync", grain);
        Assert.Contains("DeriveMaxHealth(allocation, 0f, equipment.MaxHealth)", grain);

        // 穿戴/卸下必须通知角色侧重算，否则服务端一直用旧上限判死。
        var handler = File.ReadAllText(Path.Combine(root, "Horizon.Game.Core", "Handlers", "ItemHandler.cs"));
        Assert.Contains("if (ok) InvalidateCombatProfile(equipItemMessage.CharacterId)", handler);
    }

    [Fact]
    public void 客户端装备桥不能重复加生命与内力上限()
    {
        // 服务端血池现在已含装备生命并下行；客户端再按硬编码装备表加一次就是双倍。
        var root = FindRepoRoot();
        var bridge = File.ReadAllText(Path.Combine(root, "HundunWorld", "Source", "Game", "Character", "EquipmentAttributeBridge.cs"));
        Assert.DoesNotContain("bonus.MaxHealth +=", bridge);
        Assert.DoesNotContain("bonus.MaxEnergy +=", bridge);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Horizon.sln"))) dir = dir.Parent;
        Assert.True(dir != null, "找不到仓库根（Horizon.sln）");
        return dir!.FullName;
    }
}
