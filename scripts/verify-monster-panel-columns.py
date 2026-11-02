# -*- coding: utf-8 -*-
"""
怪物编辑器「每一格是否真的能改」普查。

上一版这个尺子只问"属性名在插件源码里出现过吗"——那不是可编辑性，那是拼写。
一列只有在这条链上到位才算对策划可达：
  草稿协议带它 → 面板把它灌进控件（装载）→ 面板从控件读出来写回草稿（提交）

第三段是历史上真出过事的那一段：`MinRange/TargetCount/DamageScaleBps/EmergencyHpPercent`
四格曾经"有赋值语句"，右值却是常量（恒 0/1/10000/30），格子里填什么都会被覆盖。
所以这里把提交右值原样念出来，并据此分档：
  面板格   = 右值真的引用了控件/下拉（Box/Drop/IndexOf/TriOf/Parse…，含经由局部变量）
  常量档   = 面板里只有常量/派生赋值（生命周期动作、行序、版本号）
  装配写   = 面板不写，由 CLI 装配时按父行填（外键/派生计数）——证据须在源文件里查得到
  无消费者 = 今天运行期没人读这一列，按设计不给输入框——判据是"真的一次读都没有"
  开口     = 以上都不是

两个不是靠白名单装绿的点：
  * 装配写的证据是源文件里的**字面量接线**，接线被删掉时证据查不到，那一格掉回开口。
  * 无消费者的判据是"运行期目录里 `.Prop` 一次都没被读"，有人接上消费者之后
    这一格会自动变成开口（有消费者却没格子），而不是继续沉默。

用法：python scripts/verify-monster-panel-columns.py
退出码 1 = 存在开口，0 = 每格都有出处。
"""
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

ENTITY_FILES = [
    ("MonsterTemplate", "Horizon.Model/GameModel/MonsterTemplateEntity.cs", r"class\s+MonsterTemplateEntity"),
    ("MonsterBehavior", "Horizon.Model/GameModel/MonsterTemplateEntity.cs", r"class\s+MonsterBehaviorEntity"),
    ("MonsterSkillMount", "Horizon.Model/GameModel/MonsterTemplateEntity.cs", r"class\s+MonsterSkillMountEntity"),
    ("MonsterPatrolRoute", "Horizon.Model/GameModel/MonsterWorldEntity.cs", r"class\s+MonsterPatrolRouteEntity"),
    ("MonsterPatrolPoint", "Horizon.Model/GameModel/MonsterWorldEntity.cs", r"class\s+MonsterPatrolPointEntity"),
    ("MonsterSpawnPoint", "Horizon.Model/GameModel/MonsterWorldEntity.cs", r"class\s+MonsterSpawnPointEntity"),
    ("MonsterLootBinding", "Horizon.Model/GameModel/MonsterWorldEntity.cs", r"class\s+MonsterLootBindingEntity"),
    ("MonsterDropEntry", "Horizon.Model/GameModel/MonsterWorldEntity.cs", r"class\s+MonsterDropEntryEntity"),
]

MODEL = "HundunWorld/Plugins/MonsterEditor/Source/MonsterEditor/MonsterDraftModel.cs"
PANEL = "HundunWorld/Plugins/MonsterEditor/Source/MonsterEditorEditor/MonsterEditorWindow.cs"
STORE = "Horizon.MonsterCli/MonsterStore.cs"

MODEL_CLASSES = [
    "MonsterDraftModel", "MonsterStatsModel", "MonsterXpModel", "MonsterBehaviorModel",
    "MonsterSkillModel", "MonsterLootBindingModel", "MonsterDropModel", "MonsterRouteModel",
    "MonsterPointModel", "MonsterSpawnModel",
]

# 该表在面板里由谁承载（提交语句的接收者前缀 + 对象初始化器的类）
TABLE_RECEIVER = {
    "MonsterTemplate": (r"(?:d|_draft)(?:\.(?:Stats|Xp))?", "MonsterDraftModel"),
    "MonsterBehavior": (r"b", "MonsterBehaviorModel"),
    "MonsterSkillMount": (r"Model", "MonsterSkillModel"),
    "MonsterPatrolRoute": (r"route", "MonsterRouteModel"),
    "MonsterPatrolPoint": (r"(?:point|p)", "MonsterPointModel"),
    "MonsterSpawnPoint": (r"Model", "MonsterSpawnModel"),
    "MonsterLootBinding": (r"Model", "MonsterLootBindingModel"),
    "MonsterDropEntry": (r"Model", "MonsterDropModel"),
}

# 装配写：面板之外的真写方。证据是 CLI 装配里的字面量接线。
OUTSIDE_EVIDENCE = {
    ("MonsterSkillMount", "monster_id"): "SkillId = s.SkillId,",
    ("MonsterLootBinding", "monster_id"): "LootTableId = lb.LootTableId,",
    ("MonsterDropEntry", "binding_id"): "BindingId = bindingRow.Id,",
    ("MonsterPatrolPoint", "route_id"): "RouteId = routeRow.Id,",
    ("MonsterPatrolRoute", "point_count"): "routeRow.PointCount = route.Points.Count;",
    ("MonsterPatrolRoute", "publish_state"): "routeRow.PublishState = 1;",
    ("MonsterSpawnPoint", "monster_id"): "row.MonsterId = draft.MonsterId;",
    ("MonsterSpawnPoint", "publish_state"): "row.PublishState = 1;",
    ("MonsterSpawnPoint", "content_version"): "row.ContentVersion = row.ContentVersion <= 0 ? 1 : row.ContentVersion + 1;",
    ("MonsterTemplate", "publish_state"): "template.PublishState = (byte)Math.Clamp(draft.PublishState, 0, 2);",
    ("MonsterTemplate", "content_version"): "template.ContentVersion = template.ContentVersion + 1;",
}

# 无消费者：人工读过、运行期确实没人读的列。判据是活的（见 expensive 注释）。
NO_CONSUMER = {("MonsterSpawnPoint", "zone_id")}
# 派生常量：这一格本来就该由行序/父行/版本号决定，没有"人填"这一说
DERIVED_CONST = {
    ("MonsterSkillMount", "slot_seq"): "行序即槽位序",
    ("MonsterPatrolPoint", "seq"): "点序由清单位置决定",
    ("MonsterDropEntry", "seq"): "行序即掷骰盐值",
    ("MonsterBehavior", "patrol_route_id"): "由路线是否存在派生（route.RouteId）",
    ("MonsterTemplate", "content_version"): "保存回执里读回，面板不手填",
    ("MonsterSpawnPoint", "content_version"): "保存回执里读回，面板不手填",
}
# 有模型、无控件、但校验器每填必警的列（"不给输入框"本身是个决定，决定要留在代码里）
DELIBERATE_NO_BOX = {
    ("MonsterSkillMount", "start_emote"):
        ("Horizon.MonsterCli/MonsterValidator.cs", "startEmote：客户端起手动画表还没落地"),
}
RUNTIME_DIRS = ["Horizon.Game.Core/Sim/Server", "Horizon.DsHost", "Horizon.Game.Sim/Monsters"]
# 传输 DTO 自己不算"读"；Npc* 是另一条管线的同名外键（同名不同表的字段会让文本对账假阳性）
RUNTIME_SKIP = {"MonsterContentSnapshot.cs"}
RUNTIME_SKIP_PREFIX = ("Npc",)

CONTROL_HINT = re.compile(r"(Box|Drop|IndexOf\(|TriOf\(|ParseInt\(|ParseDouble\(|ParseIntLocal\(|ParseDoubleLocal\()")


def read(rel):
    with open(os.path.join(ROOT, rel), encoding="utf-8-sig") as f:
        return f.read()


def class_body(text, decl_regex):
    m = re.search(decl_regex, text)
    if not m:
        return None
    start = text.find("{", m.end())
    depth = 0
    for i in range(start, len(text)):
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                return text[start:i + 1]
    return None


def columns_of(entity_text, decl_regex):
    body = class_body(entity_text, decl_regex)
    if body is None:
        return None
    return [(m.group(1), m.group(2)) for m in re.finditer(
        r'\[Column\("([a-z0-9_]+)"[^\]]*\]\s*(?:\[[^\]]*\]\s*)*public\s+[\w\.\?<>]+\s+(\w+)\s*\{', body)]


def local_initializers(panel_text):
    """局部量 → 它被赋过的全部右值（含 `out x` 的 TryParse 那一行）。

    为什么要连赋值语句一起收：`point.PauseSeconds = parsed` 里的 `parsed` 只在
    `double.TryParse(raw, …, out parsed)` 里落地，而 `raw` 才是 `_pointPauseBox.Text`。
    只看声明行会把这格误判成"常量"（历史上正是这个形状误判过一次）。
    """
    table = {}
    for m in re.finditer(r"(?:var|double|int|long|float|bool|string)\s+(\w+)\s*=\s*([^;]+);", panel_text):
        table.setdefault(m.group(1), []).append(" ".join(m.group(2).split()))
    for m in re.finditer(r"(?<![\w.])out\s+(\w+)", panel_text):
        line_start = panel_text.rfind(";", 0, m.start()) + 1
        line_end = panel_text.find(";", m.start())
        if line_end > 0:
            table.setdefault(m.group(1), []).append(" ".join(panel_text[line_start:line_end].split()))
    for m in re.finditer(r"(?<![\w.])(\w+)\s*=(?!=)\s*([^;]+);", panel_text):
        table.setdefault(m.group(1), []).append(" ".join(m.group(2).split()))
    # 纯数字不是局部量：`\w` 会把 `0 =` 这种误收成名字，之后它去匹配右值 "0" 时把常量判成控件来源
    return {k: v for k, v in table.items() if not k.isdigit()}


def control_fed_names(panel_text, locals_):
    """从控件流出来的局部量集合（定点：A 由控件来、B=A ⇒ B 也算）。"""
    fed = {name for name, exprs in locals_.items() if any(CONTROL_HINT.search(e) for e in exprs)}
    for _ in range(3):
        grew = False
        for name, exprs in locals_.items():
            if name in fed:
                continue
            for e in exprs:
                if any(re.search(r"\b" + re.escape(f) + r"\b", e) for f in fed):
                    fed.add(name)
                    grew = True
                    break
        if not grew:
            break
    return fed


def panel_assignments(panel_text, receiver, model_class, prop):
    """面板里对 prop 的全部赋值右值（点号赋值 + 对象初始化器；一处多写就都收）。"""
    out = [(" ".join(m.group(1).split()))
           for m in re.finditer(receiver + r"\.\b" + prop + r"\b\s*=(?!=)\s*([^;]+);", panel_text)]
    for init in re.finditer(r"new\s+" + model_class + r"\s*\{([^}]*)\}", panel_text):
        out.extend(" ".join(m.group(1).split())
                   for m in re.finditer(r"\b" + prop + r"\s*=(?!=)\s*([^,}]+)", init.group(1)))
    return out


def panel_assignment(panel_text, receiver, model_class, prop):
    rhs = panel_assignments(panel_text, receiver, model_class, prop)
    return rhs[0] if rhs else None


def read_by_runtime(prop):
    hits = 0
    for rel in RUNTIME_DIRS:
        base = os.path.join(ROOT, rel)
        for dirpath, _, names in os.walk(base):
            for name in names:
                if not name.endswith(".cs") or name in RUNTIME_SKIP or name.startswith(RUNTIME_SKIP_PREFIX):
                    continue
                with open(os.path.join(dirpath, name), encoding="utf-8-sig", errors="ignore") as f:
                    text = f.read()
                hits += len(re.findall(r"\.\b" + prop + r"\b(?!\s*=(?!=))", text))
    return hits


def audit(model_text, panel_text, store_text, validator_texts, verbose=True):
    """跑一遍普查，返回 (总列数, 开口清单, 分档计数)。"""
    locals_ = local_initializers(panel_text)
    fed = control_fed_names(panel_text, locals_)
    total = 0
    holes = []
    counts = {}
    if verbose:
        print("%-20s %-42s %-4s %-4s %-32s %s" % ("表", "列 / 属性", "草稿", "装载", "提交右值", "判类"))
        print("-" * 130)
    for table, rel, decl in ENTITY_FILES:
        cols = columns_of(read(rel), decl)
        if cols is None:
            holes.append((table, "<class>", "类声明没找到"))
            continue
        receiver, model_class = TABLE_RECEIVER[table]
        for col, prop in cols:
            total += 1
            in_model = re.search(r"public\s+[\w\.\?<>]+\s+" + prop + r"\s*\{\s*get;", model_text) is not None
            applied = re.search(r"\.\b" + prop + r"\b(?!\s*=(?!=))", panel_text) is not None
            rhss = panel_assignments(panel_text, receiver, model_class, prop)
            rhs = rhss[0] if rhss else None
            control_rhs = next((r for r in rhss
                                if CONTROL_HINT.search(r)
                                or any(re.search(r"\b" + re.escape(f) + r"\b", r) for f in fed)), None)

            if control_rhs is not None:
                kind = "面板格"
                rhs = control_rhs
            elif (table, col) in OUTSIDE_EVIDENCE:
                needle = OUTSIDE_EVIDENCE[(table, col)]
                kind = "装配写" if needle in store_text else "!! 证据已失效（Store 里找不到接线）"
                if kind.startswith("!!"):
                    holes.append((table, col, kind))
            elif (table, col) in DELIBERATE_NO_BOX:
                path, needle = DELIBERATE_NO_BOX[(table, col)]
                kind = "刻意无格（校验器当警）" if needle in validator_texts.get(path, "") else "!! 那条报警已失效（%s）" % path
                if kind.startswith("!!"):
                    holes.append((table, col, kind))
            elif (table, col) in NO_CONSUMER:
                hits = read_by_runtime(prop)
                kind = "无消费者（运行期 0 读）" if hits == 0 else "开口（有人读了，面板却没格）"
                if hits != 0:
                    holes.append((table, col, "运行期有 %d 处读，但没有输入框" % hits))
            elif (table, col) in DERIVED_CONST:
                kind = "派生常量（%s）" % DERIVED_CONST[(table, col)]
            elif rhs is not None:
                kind = "开口（只有常量赋值：%s）" % rhs
                holes.append((table, col, "这一格只被常量覆盖，填什么都留不下：%s" % rhs))
            else:
                kind = "开口"
                holes.append((table, col, "面板无赋值、也无写方证据"))

            key = kind.split("：")[0].split("（")[0]
            counts[key] = counts.get(key, 0) + 1
            if verbose:
                f = lambda b: "有" if b else "缺"
                print("%-20s %-42s %-4s %-4s %-32s %s" % (
                    table, col + " / " + prop, f(in_model), f(applied), (rhs or "-")[:32], kind))
    return total, holes, counts


SELFTEST_CASES = [
    # (说明, 破坏哪份文本, 原文, 替换成, 期望它掉成的开口列)
    ("删掉分片号的提交语句", "panel", "Model.ShardId = ParseLongLocal(_shardBox.Text);", "", ("MonsterSpawnPoint", "shard_id")),
    ("删掉路线点数的装配接线", "store", "routeRow.PointCount = route.Points.Count;", "", ("MonsterPatrolRoute", "point_count")),
    ("把射程下界改成常量（历史上真发生过的那种）", "panel",
     "Model.MinRange = ParseDoubleLocal(_minRangeBox == null ? null : _minRangeBox.Text, Model.MinRange);",
     "Model.MinRange = 0;", ("MonsterSkillMount", "min_range")),
]


def selftest(model_text, panel_text, store_text, validator_texts):
    """先把尺子自己证伪一遍：破坏一格，它必须从"有出处"变成"开口"。
    等价变异（坏掉的那行本来就不影响判据）会让反证假绿，所以每条都锚在**判据真正读的那一行**上。"""
    ok = True
    for label, which, old, new, expect in SELFTEST_CASES:
        p, s = panel_text, store_text
        if which == "panel":
            if old not in p:
                print("  自检失败：锚点在面板源码里找不到 —— %s" % label)
                ok = False
                continue
            p = p.replace(old, new, 1)
        else:
            if old not in s:
                print("  自检失败：锚点在装配源码里找不到 —— %s" % label)
                ok = False
                continue
            s = s.replace(old, new, 1)
        _, holes, _ = audit(model_text, p, s, validator_texts, verbose=False)
        named = [h for h in holes if h[0] == expect[0]]
        hit = any(expect[1] in h[1] for h in named)
        print("  自检%s：%s → %s" % ("通过" if hit else "失败", label,
                                    ("%s 掉成开口" % expect[1]) if hit else "仍然算有出处（尺子看不见这一格）"))
        ok = ok and hit
    return ok


def main():
    model_text = read(MODEL)
    panel_text = read(PANEL)
    store_text = read(STORE)
    validator_texts = {path: read(path) for path, _ in DELIBERATE_NO_BOX.values()}

    total, holes, counts = audit(model_text, panel_text, store_text, validator_texts)
    print("-" * 130)
    print("总列数 %d 开口 %d" % (total, len(holes)))
    for label in sorted(counts):
        print("  分档：%-24s %d" % (label, counts[label]))
    for h in holes:
        print("  开口：%-20s %-26s %s" % h)

    print("尺子自检（破坏一格必须变红）：")
    selftest_ok = selftest(model_text, panel_text, store_text, validator_texts)
    return 0 if (not holes and selftest_ok) else 1


if __name__ == "__main__":
    sys.exit(main())
