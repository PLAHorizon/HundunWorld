"""星球这条链的"接线普查"：哪些 public 类型有实现、有判据，但没有任何生产代码引用它。

**为什么要有它**：本轮已经两次抓到同一个形状——`PlanetRegionWatches.Reset()` 全仓零调用方、
§6.2 的 `DescribeMismatch` 在服务端一侧一直是空的。库里写得对、判据也绿，但活路径上没有入口，
等于那条判定/那段清理从未存在。这种缺陷离线判据看不见（判据只知道自己调用它），
只能按"生产引用数"逐条普查。

口径：
- 声明侧 = Horizon.Planet 与客户端 Source/Game/Planet 里所有 public 类型（含接口/结构/枚举）。
- 引用侧分三堆分别计数，避免"只有库内互相引用"被当成"接上了"：
    client = HundunWorld/Source + Plugins/**/Source（玩家打得到的那一侧）
    server = Horizon.* 的非测试工程（Silo / Grains / Gateway / Core / Message / Sim / World …）
    lib    = Horizon.Planet 内部（除声明文件本身）
- 注释与文档行不计（`//` 之后整段抹掉、以 `*` 开头的续行不算）：本轮的教训是
  针既能命中代码也能命中说明书。
- 输出按 (client+server) 升序：0 的就是"没有生产入口"的头，需要逐条判它是死码还是漏接线。

用法：python Tools/planet-unwired-audit.py [--members]

**这把尺子的三处盲区（读完结论前先对着看一眼，否则会像我第一次那样把工具读数当成产品缺陷）**：
1. 返回类型与被 `var` 推导接住的名字永远不会出现在生产源码里，所以类型级那半截会把
   `TransferResult` / `MeshReport` 这类正常 API 报成"零引用"；
2. 成员级那一截把"声明文件自己"排除掉了，于是<em>同文件内</em>的互调（库里的常态）看起来也是零引用；
3. 平行的第二份实现看不出来：`PlanetHandshakePolicy` 自带一份与 `PlanetHandshake.DescribeMismatch`
   逐字同数的说法（由 `PlanetHandshakePolicyTests.cs:88` 断言相等），所以 `DescribeMismatch`
   报"只有测试在调"是<em>对的</em>，但它不是漏接线，而是"用判据钉住的重复"。

因此这份普查的用途是<em>分诊</em>（把候选按"生产引用数"排好，附上引用处），不是闸门；
每一条都要人来判：死码 / 引擎反射挂载 / 链头在别处 / 用判据钉住的重复 / 真的漏接线。
"""
import io
import os
import re
import sys

sys.stdout.reconfigure(encoding="utf-8")
ROOT = r"C:/Works/GitHubProjects/HundunWorld"

LIB = "Horizon.Planet"
DECL_DIRS = [os.path.join(ROOT, "Horizon.Planet"),
             os.path.join(ROOT, "HundunWorld", "Source", "Game", "Planet")]
CLIENT_ROOTS = [os.path.join(ROOT, "HundunWorld", "Source"), os.path.join(ROOT, "HundunWorld", "Plugins")]
SERVER_ROOT = ROOT

SKIP_DIR_NAMES = {"bin", "obj", ".vs", ".git", "Cache", "Intermediate", "Saved", "Output",
                  "node_modules", "logs", "Logs"}
TEST_HINTS = (".tests", ".Tests")


def walk_csharp(root):
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIR_NAMES]
        for f in filenames:
            if f.endswith(".cs"):
                yield os.path.join(dirpath, f)


def strip_comments(text):
    out = []
    for line in text.split("\n"):
        s = line.strip()
        if s.startswith("*") or s.startswith("/*"):
            continue
        cut = line.find("//")
        out.append(line if cut < 0 else line[:cut])
    return "\n".join(out)


def load(paths):
    docs = {}
    for p in paths:
        try:
            raw = io.open(p, encoding="utf-8-sig", errors="replace").read()
        except OSError:
            continue
        docs[p] = strip_comments(raw)
    return docs


# ---------- 声明侧 ----------
declared = []          # (type, kind, declaring file)
decl_files = set()
for d in DECL_DIRS:
    for p in walk_csharp(d):
        decl_files.add(p)
        code = strip_comments(io.open(p, encoding="utf-8-sig", errors="replace").read())
        for m in re.finditer(r"\b(public|internal)\s+(?:static\s+|sealed\s+|abstract\s+|partial\s+|readonly\s+)*"
                             r"(class|struct|enum|interface|record)\s+([A-Za-z_]\w*)", code):
            declared.append((m.group(3), m.group(2), p))

# ---------- 引用侧 ----------
client_paths, server_paths = [], []
for r in CLIENT_ROOTS:
    client_paths += [p for p in walk_csharp(r)]
for entry in sorted(os.listdir(SERVER_ROOT)):
    full = os.path.join(SERVER_ROOT, entry)
    if not os.path.isdir(full) or not entry.startswith("Horizon"):
        continue
    if any(h in entry for h in TEST_HINTS):
        continue
    server_paths += [p for p in walk_csharp(full)]
# 服务端那一侧要排除 Horizon.Planet 自己（它单独算 lib）
lib_prefix = os.path.normcase(os.path.join(ROOT, LIB) + os.sep)
server_paths = [p for p in server_paths if not os.path.normcase(os.path.normpath(p) + os.sep).startswith(lib_prefix)]

client_docs = load(set(client_paths))
server_docs = load(set(server_paths))
lib_paths = set(walk_csharp(os.path.join(ROOT, LIB)))
lib_docs = load(lib_paths)

# 测试堆单独计：prod=0 而 test>0 就是"正确但只有测试在调"那种形状——
# 它不是死码（判据确实跑着它），但活路径上没有入口，屏幕上表现为"那条规矩不存在"。
test_paths = []
for entry in sorted(os.listdir(ROOT)):
    full = os.path.join(ROOT, entry)
    if os.path.isdir(full) and ".Tests" in entry:
        test_paths += [p for p in walk_csharp(full)]
test_docs = load(set(test_paths))

# Flax 的 Script 子类由引擎按场景里存的脚本 id 反射挂载，源码里本来就没有 `new` 它的地方。
# 把它们算进"没有入口"会把普查变成噪声源，所以先按基类认出来，再单独一档。
script_names = set()
for p, text in client_docs.items():
    for m in re.finditer(r"class\s+(\w+)\s*:\s*Script\b", text):
        script_names.add(m.group(1))


def count(name, docs, exclude):
    pat = re.compile(r"\b" + re.escape(name) + r"\b")
    hits = []
    for p, text in docs.items():
        if exclude and os.path.normcase(p) == os.path.normcase(exclude):
            continue
        n = len(pat.findall(text))
        if n:
            hits.append((n, os.path.relpath(p, ROOT).replace("\\", "/")))
    return sum(h[0] for h in hits), hits


rows = []
seen = set()
for name, kind, decl in declared:
    if name in seen:
        continue
    seen.add(name)
    c, ch = count(name, client_docs, decl)
    s, sh = count(name, server_docs, decl)
    l, lh = count(name, lib_docs, decl)
    t, th = count(name, test_docs, decl)
    rows.append((c + s, c, s, l, t, name, kind, os.path.relpath(decl, ROOT).replace("\\", "/"),
                 [h for h in (lh + th)[:2]]))

rows.sort(key=lambda r: (r[0], -r[3]))
print("声明的 public 类型数 = %d" % len(rows))
print("")
print("读法（三种 prod=0 完全不同，别混着看）：")
print("  lib>0        入口在链头上——链头被生产代码引用，这个名字只是被 var/返回值推导藏住了，不算未接线；")
print("  lib=0 test>0 只有判据在调它：活路径上没有入口，屏幕上「那条规矩不存在」；")
print("  lib=0 test=0 除声明文件外零引用——要么真是死码，要么靠反射/序列化/引擎挂载到达，需人判（下面附上引用处）。")
print("")
print("%-6s %-7s %-7s %-5s %-6s  %s" % ("prod", "client", "server", "lib", "test", "类型 / 声明处"))
print("-" * 122)
script_count = 0
for prod, c, s, l, t, name, kind, where, refs in rows:
    if prod > 0:
        continue
    tail = "   引用处：" + "; ".join("%d×%s" % (n, f) for n, f in refs) if refs else "   引用处：（无）"
    if name in script_names:
        script_count += 1
        print("%-6d %-7d %-7d %-5d %-6d  %s (%s)  ← %s  【Flax Script：引擎按场景里的脚本 id 反射挂载，"
              "源码里没有 new 它的那一处是常态】" % (prod, c, s, l, t, name, kind, where))
    elif l > 0:
        print("%-6d %-7d %-7d %-5d %-6d  %s (%s)  ← %s  【只在库内被引用：%s】"
              % (prod, c, s, l, t, name, kind, where, tail))
    else:
        print("%-6d %-7d %-7d %-5d %-6d  %s (%s)  ← %s  【库外零引用：%s】"
              % (prod, c, s, l, t, name, kind, where, tail))
print("")
zero = [r for r in rows if r[0] == 0]
print("没有生产入口的类型：%d / %d（其中 Flax Script %d 个、只在库内被引用 %d 个、库外零引用 %d 个）"
      % (len(zero), len(rows), script_count,
         len([r for r in zero if r[5] not in script_names and r[3] > 0]),
         len([r for r in zero if r[5] not in script_names and r[3] == 0])))


# ==================== 成员级普查 ====================
#
# 类型级那半截回答不了真正的问题：本轮抓到的两个形状（`PlanetRegionWatches.Reset()` 零调用方、
# §6.2 的 `DescribeMismatch` 服务端一侧一直是空的）都是**成员**级的，而类型名会因为 `var`、
# 返回类型推导、引擎反射挂载而大量误报。所以这一段按成员名数引用，并把"引擎会自己调"的那几个
# 生命周期名字排掉。
if "--members" in sys.argv:
    lifecycle = {"Start", "Update", "FixedUpdate", "OnEnable", "OnDisable", "OnDestroy", "Awake",
                 "Initialize", "Dispose", "ToString", "GetHashCode", "Equals", "Main", "Run",
                 "Configure", "Execute", "OnReceive", "Handle", "HandleAsync"}
    members = []
    for d in DECL_DIRS:
        for p in walk_csharp(d):
            code = strip_comments(io.open(p, encoding="utf-8-sig", errors="replace").read())
            own = os.path.relpath(p, ROOT).replace("\\", "/")
            cls = re.findall(r"\bclass\s+(\w+)", code)
            for m in re.finditer(r"^\s*(?:public|internal)\s+(?:static\s+|readonly\s+|virtual\s+|"
                                 r"override\s+|async\s+|sealed\s+|unsafe\s+)*"
                                 r"([\w\.<>\[\]\?,\s]+?)\s+([A-Za-z_]\w*)\s*(\(|\{)", code, re.M):
                kind, name, sep = m.group(1).strip(), m.group(2), m.group(3)
                if kind.split()[-1] in ("class", "struct", "enum", "interface", "record"):
                    continue
                if name in lifecycle or name in cls:      # 构造函数与引擎生命周期
                    continue
                if sep == "{" and kind.endswith("=>"):
                    continue
                members.append((name, kind, sep == "(" and "方法" or "属性", own))

    out = []
    for name, kind, form, own in members:
        c, _ = count(name, client_docs, None)
        s, _ = count(name, server_docs, None)
        # 库内引用要排掉声明它的那一份文件：自己文件里出现这个名字，多数是声明本身与重载之间互调
        l, lh = count(name, {k: v for k, v in lib_docs.items()
                             if os.path.normcase(k) != os.path.normcase(os.path.join(ROOT, own))}, None)
        t, th = count(name, test_docs, None)
        if c + s + l > 0:
            continue
        if t == 0:
            continue        # 生产、库内、测试都不提这个名字：那多半是反射/序列化到达，交给类型级那半截判
        out.append((t, name, form, kind, own, th[:1]))
    out.sort(reverse=True)
    print("")
    print("=== 成员级：只有判据在调、生产与库内都不提这个名字 ===")
    for t, name, form, kind, own, ref in out:
        print("  test=%-4d %-4s %-28s (%s)  ← %s" % (t, form, name, kind, own))
    print("合计 %d 条" % len(out))

