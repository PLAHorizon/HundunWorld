import io, subprocess, os, sys, hashlib

ROOT = r"C:/Works/GitHubProjects/HundunWorld"
GRID = os.path.join(ROOT, "Horizon.Planet/Dem/DemGrid.cs")
SAMPLER = os.path.join(ROOT, "Horizon.Planet/HeightSampler.cs")
ARGS = ["patch", "Data/GEBCO_2026/derived", "27.98", "86.92", "40"]

PAIRS = [
    (GRID,
     "            int tapRow = (int)row - fetchTop;\n"
     "            double wr = dr == 0 ? 1.0 - fy : fy;",
     "            double wr = dr == 0 ? 1.0 - fy : fy;"),
    (GRID,
     "                short v = taps[tapRow * 2 + dc];",
     "                short v = ReadCell(level, (int)row, WrapCol(col0 + dc, info.Cols));"),
    (GRID,
     "            int tapRow = (int)row - top;\n"
     "            for (int ix = 0; ix < 4; ix++)",
     "            for (int ix = 0; ix < 4; ix++)"),
    (GRID,
     "                short v = taps[tapRow * 4 + ix];",
     "                short v = ReadCell(level, (int)row, WrapCol(col0 - 1 + ix, info.Cols));"),
    (SAMPLER,
     "            var stencil = _grid.ReadStencil(_level.Level, (int)topRow, leftCol, Window, Window);\n"
     "            for (int r = 0; r < Window; r++)\n"
     "                for (int c = 0; c < Window; c++)\n"
     "                    _cache[r, c] = stencil[r * Window + c];",
     "            for (int r = 0; r < Window; r++)\n"
     "                for (int c = 0; c < Window; c++)\n"
     "                {\n"
     "                    long wrapped = (leftCol + c) % _level.Cols;\n"
     "                    if (wrapped < 0) wrapped += _level.Cols;\n"
     "                    _cache[r, c] = _grid.ReadCell(_level.Level, (int)(topRow + r), (int)wrapped);\n"
     "                }"),
]


def read(p):
    return io.open(p, encoding="utf-8-sig", newline="").read()


def an(s, t):
    return t.replace("\r\n", "\n").replace("\n", "\r\n") if "\r\n" in s else t


def digest():
    return hashlib.sha256((read(GRID) + read(SAMPLER)).encode("utf-8")).hexdigest()


def revert_to_blockcache():
    """把三处插值/模板调用改回逐格 ReadCell——语义相同，只是又去装 131 KB 的块。"""
    for p, new, old in PAIRS:
        s = read(p)
        new_a, old_a = an(s, new), an(s, old)
        assert s.count(new_a) == 1, "回退锚点没命中：%s" % new[:60]
        s = s.replace(new_a, old_a)
        # 前两处会留下没用了的 taps 变量：把它去掉，否则编译报未使用不影响，但行映射会残留
        io.open(p, "w", encoding="utf-8", newline="").write(s)
    # 片读本身还在，只是没人调用
    for p, frag in [(GRID, "short[] taps = fetchRows > 0 ? ReadStencil"), (GRID, "short[] taps = ReadStencil"),
                    (GRID, "int fetchTop"), (GRID, "int top = (int)Math.Clamp(row0 - 1")]:
        s = read(p)
        if frag in s:
            print("  注意：还留着 %s" % frag)


def measure(label):
    env = dict(os.environ, DOTNET_CLI_UI_LANGUAGE="en")
    r = subprocess.run(["dotnet", "run", "--project", "Tools/DemProbe/DemProbe.csproj", "--"] + ARGS,
                       cwd=ROOT, env=env, capture_output=True, text=True,
                       encoding="utf-8", errors="replace", timeout=1800)
    line = next((l for l in (r.stdout or "").splitlines() if "ms_per_patch" in l), "(没量到)")
    print("%s -> %s" % (label, line.strip()))
    return line


if __name__ == "__main__":
    # 备份取在内存里、复原也写回同一份：/tmp 在 Git Bash 里不是 Windows 原生路径，
    # 让 python 去读它会静默读到别处的文件（这一类坑已经踩过一次）
    originals = {p: read(p) for p in (GRID, SAMPLER)}
    base = digest()
    print("基线指纹", base[:16])
    measure("A/B 之后（片读）第一次")
    try:
        revert_to_blockcache()
        measure("改回逐格 ReadCell（块缓存）")
    finally:
        for path, text in originals.items():
            io.open(path, "w", encoding="utf-8", newline="").write(text)
    after = digest()
    print("复原指纹", after[:16], "一致" if after == base else "不一致！！")
    measure("A/B 之后（片读）第二次")
