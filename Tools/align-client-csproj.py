import io, re, sys

P = r"C:/Works/GitHubProjects/HundunWorld/HundunWorld/Source/Game.csproj"
# 客户端不该装的 5 件服务端程序集（依据是权威清单 Game.Build.cs 的 dlls[]，不是这份 csproj）
SERVER = ["Horizon.DsHost", "Horizon.Game.Sim", "Horizon.Game.World",
          "Horizon.Game.Core", "Horizon.Core.Abstract"]
# 必须原样留下的同名家族：只装 Contracts / Message / Share.Client
MUST_KEEP = ["Horizon.Game.Sim.Contracts", "Horizon.Game.World.Contracts",
             "Horizon.Share.Client", "Horizon.Game.Message", "Horizon.Planet"]

s = io.open(P, encoding="utf-8-sig", newline="").read()
before = s

# 一个 ItemGroup 恰好包着一条 Reference：按这种形状整块删，避免留下空壳 ItemGroup
pat = re.compile(
    r'[ \t]*<ItemGroup Condition="[^"]*">\r?\n'
    r'[ \t]*<Reference Include="(' + "|".join(re.escape(n) for n in SERVER) + r')">\r?\n'
    r'[ \t]*<HintPath>[^<]*</HintPath>\r?\n'
    r'[ \t]*</Reference>\r?\n'
    r'[ \t]*</ItemGroup>\r?\n')

removed = pat.findall(before)
print("将被删除的 ItemGroup 条数 =", len(removed))
assert len(removed) == 15, "预期 15 块（5 条 × 3 个配置块），实到 %d —— 停下来看形状" % len(removed)
for n in SERVER:
    assert removed.count(n) == 3, "%s 应当出现 3 次，实际 %d" % (n, removed.count(n))

s = pat.sub("", s)

# 删完必须仍然装着的：同名家族一条都不许被顺手带走（Horizon.Game.Sim ⊂ Horizon.Game.Sim.Contracts 这种子串陷阱）
for n in MUST_KEEP:
    assert ('Include="%s"' % n) in s, "%s 被误删了" % n
for n in SERVER:
    assert ('Include="%s"' % n) not in s, "%s 没删干净" % n
# HintPath 层面再确认一次：客户端工程里不许再有指向服务端宿主输出的引用
assert "Horizon.DsHost\\bin" not in s, "仍有 HintPath 指向 Horizon.DsHost\\bin"
# 结构完整性：ItemGroup 开合配对，Reference 开合配对
assert s.count("<ItemGroup") == s.count("</ItemGroup>"), "ItemGroup 开合不配对"
assert s.count("<Reference ") == s.count("</Reference>"), "Reference 开合不配对"
# 除这些块以外，其余内容逐字节不变
residue = pat.sub("", before)
assert s == residue, "删块之外还动了别处"
assert len(before) - len(s) > 0

io.open(P, "w", encoding="utf-8-sig" if before.startswith("") else "utf-8", newline="").write(s)
print("csproj 已按权威清单对齐；删掉 %d 块，字节差 %d" % (len(removed), len(before) - len(s)))
