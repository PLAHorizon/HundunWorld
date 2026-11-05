import io, subprocess, os

ROOT = r"C:/Works/GitHubProjects/HundunWorld"
GRID = os.path.join(ROOT, "Horizon.Planet/Dem/DemGrid.cs")
SAMPLER = os.path.join(ROOT, "Horizon.Planet/HeightSampler.cs")
FILTER = "FullyQualifiedName~DemStencilTests|FullyQualifiedName~HeightSamplerTests|FullyQualifiedName~DemGridTests"


def read(p):
    return io.open(p, encoding="utf-8-sig", newline="").read()


def anchor(s, text):
    return text.replace("\r\n", "\n").replace("\n", "\r\n") if "\r\n" in s else text


def write(p, s):
    io.open(p, "w", encoding="utf-8", newline="").write(s)


def run_tests():
    env = dict(os.environ, DOTNET_CLI_UI_LANGUAGE="en")
    r = subprocess.run(["dotnet", "test", "Horizon.Planet.Tests/Horizon.Planet.Tests.csproj",
                        "-p:BaseOutputPath=bin/Verify/", "-v:q", "--nologo",
                        "--logger", "console;verbosity=normal", "--filter", FILTER],
                       cwd=ROOT, env=env, capture_output=True, text=True,
                       encoding="utf-8", errors="replace", timeout=1800)
    out = (r.stdout or "") + (r.stderr or "")
    if "error CS" in out:
        return "COMPILE-ERROR", [l for l in out.splitlines() if "error CS" in l][:3]
    names = [l.split("Tests.")[-1].split(" [")[0].strip()
             for l in out.splitlines() if "[FAIL]" in l]
    total = next((l.strip() for l in out.splitlines() if "Total:" in l), "(没有汇总行)")
    return total, names


MUTATIONS = [
    ("S1 片读丢掉绕回后的第二段（接缝那半边不读）", GRID,
     "                if (tail > 0) ReadInto(stream, rowCells * 2, buffer, tail * 2, data, r * cols, head);",
     "                // 变异：绕回段没读"),
    ("S2 片读的行偏移用列数（行主序写错）", GRID,
     "                long rowCells = (long)(topRow + r) * info.Cols;",
     "                long rowCells = (long)(topRow + r) * info.Rows;"),
    ("S3 双线性抽头的行映射差一格", GRID,
     "            int tapRow = (int)row - fetchTop;",
     "            int tapRow = (int)row - fetchTop + 1;"),
    ("S4 双三次抽头的行映射差一格", GRID,
     "            int tapRow = (int)row - top;",
     "            int tapRow = (int)row - top + 1;"),
    ("S5 近景模板把片读的行/列转置", SAMPLER,
     "                    _cache[r, c] = stencil[r * Window + c];",
     "                    _cache[r, c] = stencil[c * Window + r];"),
]

if __name__ == "__main__":
    print("=== 基线（未变异）===")
    print(run_tests()[0])
    survivors = []
    for name, path, old, new in MUTATIONS:
        backup = read(path)
        s = anchor(backup, "")
        try:
            cur = read(path)
            old_a, new_a = anchor(cur, old), anchor(cur, new)
            n = cur.count(old_a)
            if n != 1:
                print("%s\n    !! 锚点命中 %d 次，变异没有真的装上 —— 这一条不算数" % (name, n))
                survivors.append(name + "（锚点没装上）")
                continue
            write(path, cur.replace(old_a, new_a))
            total, names = run_tests()
            print("%s\n    -> %s" % (name, total))
            for nm in names:
                print("        红：", nm)
            if not names:
                survivors.append(name)
        finally:
            write(path, backup)
    print("\n=== 存活（判据没抓到的变异）===")
    print("\n".join(survivors) if survivors else "（无）")
