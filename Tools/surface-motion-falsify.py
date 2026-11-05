import io, os, re, subprocess, sys

sys.stdout.reconfigure(encoding="utf-8", line_buffering=True)
ROOT = r"C:/Works/GitHubProjects/HundunWorld"
RULES = os.path.join(ROOT, "HundunWorld/Source/Game/Planet/PlanetSurfaceMotionRules.cs")
FILTER = "FullyQualifiedName~PlanetSurfaceMotionRulesTests"
BASELINE_TOTAL = 13


def read(p):
    return io.open(p, encoding="utf-8-sig", newline="").read()


def anchor(s, text):
    """锚点里的 \\n 换成文件真实行尾：CRLF 文件里 '\\n' 锚点会静默匹配不到，那条变异就成了假反证。"""
    return text.replace("\r\n", "\n").replace("\n", "\r\n") if "\r\n" in s else text


def write(p, s):
    io.open(p, "w", encoding="utf-8", newline="").write(s)


def run_tests():
    env = dict(os.environ, DOTNET_CLI_UI_LANGUAGE="en", VSLANG="1033")
    r = subprocess.run(
        ["dotnet", "test", "Horizon.Game.Gateway.Tests/Horizon.Game.Gateway.Tests.csproj",
         "-p:BaseOutputPath=bin/Verify/", "-v:q", "--nologo",
         "--logger", "console;verbosity=normal", "--filter", FILTER],
        cwd=ROOT, env=env, capture_output=True, text=True, encoding="utf-8", errors="replace",
        timeout=1800)
    out = (r.stdout or "") + (r.stderr or "")
    if "error CS" in out:
        return "COMPILE-ERROR " + str([l for l in out.splitlines() if "error CS" in l][:2]), [], (None, None, None)
    names = sorted({l.split("Tests.")[-1].split(" [")[0].strip()
                    for l in out.splitlines() if "[FAIL]" in l})
    counts = (None, None, None)
    tot = re.search(r"(?:测试总数|Total tests?)[:：]?\s*(\d+)", out)
    pas = re.search(r"(?:通过数|Passed)[:：]?\s*(\d+)", out)
    if tot and pas:
        t, p = int(tot.group(1)), int(pas.group(1))
        counts = (t - p, p, t)
    failed, passed, total = counts
    verdict = "NO-SUMMARY" if failed is None else ("RED" if failed else "GREEN")
    return "%s 失败=%s 通过=%s 总计=%s" % (verdict, failed, passed, total), names, counts


MUTATIONS = [
    ("S1 空中状态写回旧速度（不走 v−g·dt）", [(RULES,
        "            next = new State(newPosition, facing, nextVerticalSpeed, grounded: false);",
        "            next = new State(newPosition, facing, verticalSpeed, grounded: false);")]),
    ("S2 半隐式 Euler（去掉 −½g·dt²）", [(RULES,
        "            double dropThisStep = verticalSpeed * dtSeconds - 0.5 * g * dtSeconds * dtSeconds;",
        "            double dropThisStep = verticalSpeed * dtSeconds;")]),
    ("S3 落地判定放宽到地面之上 100 cm", [(RULES,
        "            if (ground is { } gAt && targetAltitude <= gAt && nextVerticalSpeed <= 0)",
        "            if (ground is { } gAt && targetAltitude <= gAt + 100.0 && nextVerticalSpeed <= 0)")]),
    ("S4 朝向不跟移动方向", [(RULES,
        "                if (turned.LengthSquared > 1e-24) facing = turned;",
        "                if (false) facing = turned;")]),
    ("S5 下台阶阈值大到永不走出边沿", [(RULES,
        "        public const double StepDownCm = 10.0;",
        "        public const double StepDownCm = 1e6;")]),
    ("S6 站在地上且地面取不出时按海平面处理", [(RULES,
        "            if (current.Grounded && ground is null)\n"
        "            {\n"
        "                // 站在地上却不知道地面在哪：既不抬也不落，如实拒绝而不是把 0 当海平面用\n"
        "                why = \"站在地上但地面取不出来（基场没接上）：不知道该贴着哪一层\";\n"
        "                return false;\n"
        "            }",
        "            if (current.Grounded && ground is null) ground = 0.0;   // 变异：把 0 当海平面")]),
]

if __name__ == "__main__":
    only = None
    if "--only" in sys.argv:
        only = sys.argv[sys.argv.index("--only") + 1]
    print("=== 基线（未变异）：期望 GREEN 且总计=%d ===" % BASELINE_TOTAL)
    verdict, names, counts = run_tests()
    print(verdict, names)
    if counts[0] != 0 or (counts[2] or 0) < BASELINE_TOTAL:
        print("!! 基线不成立，停在这里")
        sys.exit(1)
    survivors = []
    for label, edits in MUTATIONS:
        if only and not label.startswith(only):
            continue
        backups = {p: read(p) for p, _, _ in edits}
        try:
            for p, old, new in edits:
                s = read(p)
                old, new = anchor(s, old), anchor(s, new)
                n = s.count(old)
                if n != 1:
                    print("!! 锚点不是恰好一处（%d）：%s" % (n, label))
                    survivors.append(label + "（锚点没匹配）")
                    break
                write(p, s.replace(old, new, 1))
            else:
                verdict, names, counts = run_tests()
                print("%-40s -> %s" % (label, verdict))
                for nm in names:
                    print("        ", nm)
                if counts[0] is None or counts[0] == 0:
                    survivors.append(label)
        finally:
            for p, s in backups.items():
                write(p, s)
    print("\n=== 存活（判据没抓到的变异）===")
    print("\n".join(survivors) if survivors else "（无：每条变异都被对应判据拦下）")
