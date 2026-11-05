import io, subprocess, sys, os

ROOT = r"C:/Works/GitHubProjects/HundunWorld"
GRAIN = os.path.join(ROOT, "Horizon.Orleans.Grains/PlanetRegionGrain.cs")
SILO = os.path.join(ROOT, "Horizon.Orleans.Silo/Program.cs")
DRIVER = os.path.join(ROOT, "HundunWorld/Source/Game/Planet/PlanetNearFieldDriver.cs")
RULES = os.path.join(ROOT, "HundunWorld/Source/Game/Planet/PlanetRegionTransferRules.cs")

FILTER = "FullyQualifiedName~PlanetRegionGroundGateTests"

def read(p):
    return io.open(p, encoding="utf-8-sig", newline="").read()

def anchor(s, text):
    """把锚点里的 \\n 换成文件真实使用的行尾：CRLF 文件里 '\\n' 锚点会静默匹配不到，
    那条变异就成了'没变异却报绿'的假反证。"""
    return text.replace("\r\n", "\n").replace("\n", "\r\n") if "\r\n" in s else text

def write(p, s):
    io.open(p, "w", encoding="utf-8", newline="").write(s)

def run_tests():
    env = dict(os.environ, DOTNET_CLI_UI_LANGUAGE="en")
    r = subprocess.run(
        ["dotnet", "test", "Horizon.Game.Gateway.Tests/Horizon.Game.Gateway.Tests.csproj",
         "-p:BaseOutputPath=bin/Verify/", "-v:q", "--nologo",
         "--logger", "console;verbosity=normal", "--filter", FILTER],
        cwd=ROOT, env=env, capture_output=True, text=True, encoding="utf-8", errors="replace",
        timeout=1800)
    out = (r.stdout or "") + (r.stderr or "")
    if "error CS" in out:
        lines = [l for l in out.splitlines() if "error CS" in l]
        return "COMPILE-ERROR", lines[:3]
    # 红的到底是哪几条：只报数字的话，"红了几条"与"该红的那条红了"是两件事
    names = [l.split("Tests.")[-1].split(" [")[0].strip()
             for l in out.splitlines() if "[FAIL]" in l]
    summary = next((l.strip() for l in out.splitlines()
                    if l.startswith("Failed!") or l.startswith("Passed!")), "NO-SUMMARY")
    return summary, names

MUTATIONS = [
    ("M1 服务端没挂地面来源时把海拔当 0", [(GRAIN,
        "        if (_groundSource is not { } ground) return null;",
        "        if (_groundSource is not { } ground)\n"
        "            return PlanetGround.BelowGroundCm(0.0, globalCm.Length - PlanetConstants.PlanetRadiusCm)\n"
        "                   > BelowGroundToleranceCm ? \"没挂来源，按海平面算的埋深超了容差\" : null;")]),
    ("M2 客户端抬位用自己的门槛（比服务端宽）", [(RULES,
        "            if (PlanetGround.BelowGroundCm(ground, pointElevation) <= PlanetGround.BelowToleranceCm) return globalCm;",
        "            if (PlanetGround.BelowGroundCm(ground, pointElevation) <= 400000.0) return globalCm;")]),
    ("M3 Silo 不再注册地面来源", [(SILO,
        "            services.AddSingleton<Horizon.Planet.IPlanetGroundSource>(sp =>\n"
        "                new Horizon.Planet.DemPlanetGroundSource(demRootOf(sp)));",
        "            // removed by mutation")]),
    ("M4 客户端不接探针", [(DRIVER,
        "                    PlanetRegionTransfers.GroundElevationCm = p =>\n"
        "                        _field is { } f && f.TryGetGroundElevationCm(p, out double elev) ? elev : (double?)null;",
        "                    // 变异：这一版不接探针（驱动里留着 Say，但 transfers 那侧永远是 null）")]),
    ("M5 落位那条地面判定永不触发", [(GRAIN,
        "        if (GroundRejection(new PlanetVector3(xCm, yCm, zCm)) is { } groundWhy)",
        "        if (GroundRejection(new PlanetVector3(xCm, yCm, zCm)) is { } groundWhy && false)")]),
    ("M6 落位那条地面判定整块删掉", [(GRAIN,
        "        if (GroundRejection(new PlanetVector3(xCm, yCm, zCm)) is { } groundWhy)\n"
        "        {\n"
        "            _logger.LogWarning(\"区域落位被拒（地面约束）：{Why} 角色号={CharacterId}\", groundWhy, characterId);\n"
        "            return false;\n"
        "        }\n",
        "")]),
]

if __name__ == "__main__":
    print("=== 基线（未变异）：期望 13/13 绿 ===")
    print(run_tests()[0])
    survivors = []
    for name, edits in MUTATIONS:
        backups = {p: read(p) for p, _, _ in edits}
        try:
            for p, old, new in edits:
                s = read(p)
                old, new = anchor(s, old), anchor(s, new)
                n = s.count(old)
                if n != 1:
                    print("!! 锚点不是恰好一处（%d）：%s / 变异未生效，结果不可信" % (n, name))
                    survivors.append(name + "（锚点没匹配，未真正变异）")
                    break
                write(p, s.replace(old, new))
            else:
                verdict, detail = run_tests()
                print("%s\n    -> %s" % (name, verdict))
                for d in detail:
                    print("       ", d)
                if verdict.startswith("Passed!"):
                    survivors.append(name)
        finally:
            for p, s in backups.items():
                write(p, s)
    print("\n=== 存活（判据没抓到的变异）===")
    print("\n".join(survivors) if survivors else "（无：每条变异都被对应判据拦下）")
