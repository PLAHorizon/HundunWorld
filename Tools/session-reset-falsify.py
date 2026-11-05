import io, subprocess, sys, os, re

sys.stdout.reconfigure(encoding="utf-8", line_buffering=True)   # 否则重定向到文件时全程缓冲，看不到推进

ROOT = r"C:/Works/GitHubProjects/HundunWorld"
RULES = os.path.join(ROOT, "HundunWorld/Source/Game/Planet/PlanetRegionTransferRules.cs")
TRANSFERS = os.path.join(ROOT, "HundunWorld/Source/Game/Planet/PlanetRegionTransfers.cs")
WATCHES = os.path.join(ROOT, "HundunWorld/Source/Game/Planet/PlanetRegionWatches.cs")
DRIVER = os.path.join(ROOT, "HundunWorld/Source/Game/Planet/PlanetNearFieldDriver.cs")
GATE = os.path.join(ROOT, "Horizon.Game.Gateway.Tests/PlanetRegionSessionResetGateTests.cs")
TRACKER_TESTS = os.path.join(ROOT, "Horizon.Game.Gateway.Tests/PlanetRegionTransferTrackerTests.cs")

FILTER = "FullyQualifiedName~PlanetRegionSessionResetGateTests|FullyQualifiedName~PlanetRegionTransferTrackerTests"


def read(p):
    return io.open(p, encoding="utf-8-sig", newline="").read()


def anchor(s, text):
    """把锚点里的 \\n 换成文件真实使用的行尾：CRLF 文件里 '\\n' 锚点会静默匹配不到，
    那条变异就成了'没变异却报绿'的假反证。"""
    return text.replace("\r\n", "\n").replace("\n", "\r\n") if "\r\n" in s else text


def write(p, s):
    io.open(p, "w", encoding="utf-8", newline="").write(s)


def run_tests():
    # VSLANG=1033 才是 vstest console logger 认的那个开关（DOTNET_CLI_UI_LANGUAGE 管不到它）。
    # 但两种都留着并<em>双语解析</em>：上一版只认英文前缀，加上子进程按 UTF-8 解码（真实字节是 GBK），
    # 中文汇总行变成一串 U+FFFD，于是每条变异都报 NO-SUMMARY——"存活"那一栏空着的理由
    # 是解析失败，不是"都被拦下"。解码交给区域设置（这台机器是 cp936），英文行照样能过。
    env = dict(os.environ, DOTNET_CLI_UI_LANGUAGE="en", VSLANG="1033")
    r = subprocess.run(
        ["dotnet", "test", "Horizon.Game.Gateway.Tests/Horizon.Game.Gateway.Tests.csproj",
         "-p:BaseOutputPath=bin/Verify/", "-v:q", "--nologo",
         "--logger", "console;verbosity=normal", "--filter", FILTER],
        cwd=ROOT, env=env, capture_output=True, text=True, encoding="utf-8", errors="replace",
        timeout=1800)
    out = (r.stdout or "") + (r.stderr or "")
    if "error CS" in out:
        lines = [l for l in out.splitlines() if "error CS" in l]
        return "COMPILE-ERROR", lines[:1], (None, None, None)
    # 红的到底是哪几条：只报数字的话，"红了几条"与"该红的那条红了"是两件事
    names = [l.split("Tests.")[-1].split(" [")[0].strip()
             for l in out.splitlines() if "[FAIL]" in l]
    counts = (None, None, None)
    m = re.search(r"(?:Failed|失败)[:：]\s*(\d+)[^\n]*(?:Passed|通过)[:：]\s*(\d+)"
                  r"[^\n]*(?:Total|总计)[:：]\s*(\d+)", out)
    if m:
        counts = (int(m.group(1)), int(m.group(2)), int(m.group(3)))
    else:
        # verbosity=normal 压根不打那行汇总，它打的是"测试运行成功。/ 测试总数: 16 / 通过数: 16"。
        # 前两版都只认汇总行的形状，于是每条都报 NO-SUMMARY——"存活"那一栏空着的理由是解析失败。
        tot = re.search(r"(?:测试总数|Total tests?)[:：]?\s*(\d+)", out)
        pas = re.search(r"(?:通过数|Passed)[:：]?\s*(\d+)", out)
        if tot and pas:
            total, passed = int(tot.group(1)), int(pas.group(1))
            counts = (total - passed, passed, total)
    failed, passed, total = counts
    verdict = "NO-SUMMARY" if failed is None else ("RED" if failed else "GREEN")
    return "%s 失败=%s 通过=%s 总计=%s" % (verdict, failed, passed, total), names, counts


MUTATIONS = [
    ("M1 Tracker.Reset 少清一项（确认归属留下上一轮的）", [(RULES,
        "            NeedsRebind = false;\n            ConfirmedRegionId = 0;\n",
        "            NeedsRebind = false;\n")]),
    ("M2 BeginSession 少清一项（口径留下上一轮的）", [(TRANSFERS,
        "            Caliber = default;\n",
        "")]),
    ("M3 Watches.Reset 少清节流基准（LastRequestMs）", [(WATCHES,
        "            LastRequestMs = -1;\n",
        "")]),
    ("M4 驱动不再清账", [(DRIVER,
        "            PlanetRegionTransfers.BeginSession();\n",
        "")]),
    ("M5 新增一个 public static 状态而没人清它", [(TRANSFERS,
        "        public static long LastReceiptAtMs = -1;\n",
        "        public static long LastReceiptAtMs = -1;\n"
        "        public static long SessionLeakProbe;   // 变异：假装日后新加了一个状态位\n")]),
    ("M6 清账不再叫 Tracker.Reset（只写在注释里）", [(TRANSFERS,
        "            Tracker.Reset();\n",
        "            // 变异：这里本来该调 Tracker.Reset()\n")]),
    ("M7 清账挪到装口径之后（装上就被自己抹掉）", [(DRIVER,
        "            PlanetRegionTransfers.BeginSession();\n",
        ""),
        (DRIVER,
        "                    PlanetRegionTransfers.SetCaliber(field.Caliber, DemDir);\n",
        "                    PlanetRegionTransfers.SetCaliber(field.Caliber, DemDir);\n"
        "                    PlanetRegionTransfers.BeginSession();\n")]),
    ("M8 夹具其实没把 NeedsRebind 弄脏（并集覆盖检查该红）", [(TRACKER_TESTS,
        "t.OnReceipt(true, false, false, (int)PlanetRegionRejectCode.UnknownEntity, 0, 1);\n",
        "t.OnReceipt(true, false, false, (int)PlanetRegionRejectCode.SameRegion, Home.ToId(), 1);\n")]),
    ("M9 排除名单里留一个已经不存在的成员", [(GATE,
        "                (\"CharacterIdProvider\", null),\n",
        "                (\"CharacterIdProvider\", null),\n"
        "                (\"NoSuchMemberAnymore\", null),\n")]),
]

if __name__ == "__main__":
    print("=== 基线（未变异）：期望 GREEN 且 总计=16 ===")
    base_verdict, base_names, (bf, bp, bt) = run_tests()
    print(base_verdict, base_names)
    if bf != 0 or bt is None or bt < 16:
        # 基线不成立时后面全都不可信：总计<16 意味着 filter 没匹配上（dotnet test 那样也退 0）
        print("!! 基线不成立，停在这里。存活清单未定义。")
        sys.exit(1)
    survivors = []
    for name, edits in MUTATIONS:
        touched = [p for p, _, _ in edits]
        backups = {p: read(p) for p in touched}
        try:
            for p, old, new in edits:
                s = read(p)
                old, new = anchor(s, old), anchor(s, new)
                n = s.count(old)
                if n != 1:
                    print("!! 锚点不是恰好一处（%d）：%s / 变异未生效，结果不可信" % (n, name))
                    survivors.append(name + "（锚点没匹配，未真正变异）")
                    break
                write(p, s.replace(old, new, 1))
            else:
                verdict, detail, counts = run_tests()
                print("%s\n    -> %s" % (name, verdict))
                for d in detail:
                    print("       ", d)
                # "活下来"的定义要说死：红的条数为 0，或者压根读不到红（解析失败/编译失败）
                if counts[0] is None or counts[0] == 0:
                    survivors.append(name)
        finally:
            for p, s in backups.items():
                write(p, s)
    print("\n=== 存活（判据没抓到的变异）===")
    print("\n".join(survivors) if survivors else "（无：每条变异都被对应判据拦下）")
