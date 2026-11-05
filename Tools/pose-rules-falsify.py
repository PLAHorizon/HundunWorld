import io, os, re, subprocess, sys

sys.stdout.reconfigure(encoding="utf-8", line_buffering=True)
ROOT = r"C:/Works/GitHubProjects/HundunWorld"
RULES = os.path.join(ROOT, "HundunWorld/Source/Game/Planet/PlanetActorPoseRules.cs")
DRIVER = os.path.join(ROOT, "HundunWorld/Source/Game/Planet/PlanetNearFieldDriver.cs")
CHECKER = os.path.join(ROOT, "Tools/planet-session-check.py")
MIRROR = os.path.join(ROOT, "Horizon.Game.Gateway.Tests/PlanetReceiptMirrorTests.cs")
FILTER = ("FullyQualifiedName~PlanetActorPoseRulesTests"
          "|FullyQualifiedName~PlanetReceiptMirrorTests")
BASELINE_TOTAL = 15     # 12 位姿判据 + 3 回执镜像判据；基线读不到这个数就停，别拿"没跑"当"跑绿"


def read(p):
    return io.open(p, encoding="utf-8-sig", newline="").read()


def anchor(s, text):
    """把锚点里的 \\n 换成文件真实行尾：CRLF 文件里 '\\n' 锚点会静默匹配不到，那条变异就成了
    '没变异却报绿'的假反证。"""
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
    # 两种 verbosity 的形状不一样，先认 --logger console;verbosity=normal 那种
    # （"测试总数: 12 / 通过数: 12"），它压根不打"失败: x，通过: y，已跳过: z，总计: w"那行汇总。
    counts = (None, None, None)
    tot = re.search(r"(?:测试总数|Total tests?)[:：]?\s*(\d+)", out)
    pas = re.search(r"(?:通过数|Passed)[:：]?\s*(\d+)", out)
    if tot and pas:
        t, p = int(tot.group(1)), int(pas.group(1))
        counts = (t - p, p, t)
    else:
        m = re.search(r"(?:Failed|失败)[:：]\s*(\d+)[^\n]*(?:Passed|通过)[:：]\s*(\d+)"
                      r"[^\n]*(?:Skipped|已跳过)[:：]\s*(\d+)[^\n]*(?:Total|总计)[:：]\s*(\d+)", out)
        if m:
            counts = (int(m.group(1)), int(m.group(2)), int(m.group(4)))
    failed, passed, total = counts
    verdict = "NO-SUMMARY" if failed is None else ("RED" if failed else "GREEN")
    return "%s 失败=%s 通过=%s 总计=%s" % (verdict, failed, passed, total), names, counts


MUTATIONS = [
    ("P1 两档用同一个映射（置换没生效）", [(RULES,
        "                : new PlanetVector3(axis0, axis2, axis1);",
        "                : new PlanetVector3(axis0, axis1, axis2);   // 变异：第二档不换了")]),
    ("P2 半径从全局常量取（而不是从帧）", [(RULES,
        "            return (global.Length - frame.RadiusCm) - groundElevationCm;",
        "            return (global.Length - PlanetConstants.PlanetRadiusCm) - groundElevationCm;")]),
    ("P3 两档都在带里时静默挑 Flax", [(RULES,
        "                verdict = PoseVerdict.Ambiguous;",
        "                verdict = PoseVerdict.FlaxYUpSupported;")]),
    ("P4 两档都不在带里时冒充有证据", [(RULES,
        "                verdict = PoseVerdict.Neither;",
        "                verdict = PoseVerdict.EcsZUpSupported;")]),
    ("P5 地面海拔不参与（只用海平面）", [(RULES,
        "            return (global.Length - frame.RadiusCm) - groundElevationCm;",
        "            return (global.Length - frame.RadiusCm);")]),
    ("P6 退化输入直接抛（一次探测能打断换区）", [(RULES,
        "                return new PoseProbe(PoseVerdict.UnusableInput, double.NaN, double.NaN, 0, 0,\n"
        "                    \"输入不可用（非有限值）：连算一遍都做不到\");",
        "                throw new ArgumentException(\"变异：退化输入直接抛\");")]),
    ("P7 驱动不再打 actor_pose 那行", [(DRIVER,
        "                \"actor_pose at={0} pose_mode={1}",
        "                \"pose_probe at={0} pose_mode={1}")]),
    ("P8 驱动里手写第二份换轴", [(DRIVER,
        "            PlanetVector3 global = _frame.GlobalFromLocalCm(PlanetActorPoseRules.ToLocalCm(x, y, z, conv));",
        "            PlanetVector3 global = _frame.GlobalFromLocalCm(new PlanetVector3(z, x, y));")]),
    ("P9 marker 里的 pose= 开关不解析了", [(DRIVER,
        "                    if (part.StartsWith(\"pose=\", StringComparison.OrdinalIgnoreCase))\n"
        "                        PoseMode = part.Substring(5).Trim().ToLowerInvariant();",
        "                    // 变异：不认 pose=")]),
    # ---- 回执键的两侧对账（PlanetReceiptMirrorTests）----
    ("R1 驱动新加一条没人读也没登记的键", [(DRIVER,
        "            Say(\"build_tag=\" + BuildTag);",
        "            Say(\"build_tag=\" + BuildTag);\n"
        "            Say(\"brand_new_ledger=1\");")]),
    ("R2 判据器改找一个驱动不再打的键", [(CHECKER,
        "    pose = find(lines, \"actor_pose \")",
        "    pose = find(lines, \"pose_probe \")")]),
    ("R3 驱动不再打 watches 那行（判据器还在找）", [(DRIVER,
        "            Say(\"watches \" + PlanetRegionWatches.Describe());\n",
        "")]),
    ("R4 诊断名单里留一个已经不打的键", [(MIRROR,
        "\"prime\", \"pump\",", "\"prime_typo\", \"pump\",")]),
    ("R5 一个键既被判据器读又被登记成诊断", [(MIRROR,
        "\"actor_pose_send\", \"cache\",", "\"actor_pose_send\", \"transfers\", \"cache\",")]),
]

if __name__ == "__main__":
    # --only P7 用来复跑单独一条变异（改了判据之后想知道"那条现在到底红不红"，不必再等九轮）
    only = None
    if "--only" in sys.argv:
        only = sys.argv[sys.argv.index("--only") + 1]
    print("=== 基线（未变异）：期望 GREEN 且总计=%d ===" % BASELINE_TOTAL)
    verdict, names, counts = run_tests()
    print(verdict, names)
    if counts[0] != 0 or (counts[2] or 0) < BASELINE_TOTAL:
        print("!! 基线不成立，停在这里（读不到汇总或条数不足时，后面的'全被拦下'都不作数）")
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
                    print("!! 锚点不是恰好一处（%d）：%s / 变异未生效，结果不可信" % (n, label))
                    survivors.append(label + "（锚点没匹配）")
                    break
                write(p, s.replace(old, new, 1))
            else:
                verdict, names, counts = run_tests()
                print("%-34s -> %s" % (label, verdict))
                for nm in names:
                    print("        ", nm)
                if counts[0] is None or counts[0] == 0:
                    survivors.append(label)
        finally:
            for p, s in backups.items():
                write(p, s)
    print("\n=== 存活（判据没抓到的变异）===")
    print("\n".join(survivors) if survivors else "（无：每条变异都被对应判据拦下）")
