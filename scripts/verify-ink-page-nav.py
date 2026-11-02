#!/usr/bin/env python3
"""Ink UI 页面框架与导航的离线闸门。

守住四条在"沉浸式全屏化 + 导航统一"里定下的不变量（每条都对应过真实缺陷）：

  A. 每个 RegisterPage 都必须声明框架描述符（InkPageFrame.*）——否则路由器只能靠猜。
  B. 被当作导航目标引用的 dom-id 必须已注册（或属于别名/动作白名单）——防"点不到的按钮"。
  C. 只有显式登记为"自铺舞台页"的页面才允许自绘 InkBackgroundLayer/InkVignette——
     其余页面的墨底与暗角由外壳统一提供，自绘会叠成双份。
  D. 页面代码里不得出现硬编码 dom-id 字面量，必须走 InkPageDomIds 常量。

用法（仓库根目录执行）：python scripts/verify-ink-page-nav.py
退出码 0 = 全绿；非 0 = 有违规，逐条打印 file:line。
"""

import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
UI = os.path.join(ROOT, "HundunWorld", "Source", "Game", "UI")
PAGES = os.path.join(UI, "Ink", "Pages")
MAIN_UI = os.path.join(UI, "MainUIManager.cs")
DOM_IDS = os.path.join(UI, "Ink", "InkPageRouter.cs")  # InkPageDomIds 常量类就定义在路由器文件里

# 非页面 id：返回/别名/跨页动作/结果通知，不参与"必须已注册"的判定。
NON_PAGE_IDS = {
    "BackHud", "BackOneLevel", "EquipmentChanged", "CtaEnterWorld",
    "ToggleTraditional", "ToggleImmersive",
    "EnterDungeon", "NavBattleStart",
}
NON_PAGE_PREFIXES = ("Action",)

# 允许出现自绘氛围层的"自铺舞台页"文件（登记为 SelfLit* 框架；新增必须显式加到这里）。
SELF_LIT_PAGES = {"LoadingPages.cs", "PhotoModePage.cs"}

# 调试画廊有意维护一份全量页面清单，豁免 D 检查。
HARDCODED_DOM_ID_EXEMPT = {"UIGalleryPage.cs"}

# 形如 "nav-xxx" / "popup-xxx" / "combat-hud" 的 dom-id 字面量。
DOM_ID_LITERAL = re.compile(
    r'"(combat-hud|combat-hud-v2|combat-hud-traditional|back-hud|qte|death-screen|'
    r'dialogue-confirm|acupoint|loading-1|loading-2|chapter-transition|ui-gallery|'
    r'menu-char-attributes|(?:nav|popup|reward|cc|action)-[a-z0-9-]+)"'
)

failures = []
notes = []


def read(path):
    with io.open(path, encoding="utf-8-sig") as f:
        return f.read()


def iter_cs(folder):
    for base, _dirs, files in os.walk(folder):
        for name in files:
            if name.endswith(".cs"):
                yield os.path.join(base, name)


def to_rel(path):
    return os.path.relpath(path, ROOT).replace("\\", "/")


def line_of(text, index):
    return text.count("\n", 0, index) + 1


def strip_comments(text):
    """去掉行注释与块注释，保留行号（注释替换为等长空白）。"""
    out = []
    i = 0
    n = len(text)
    in_block = False
    while i < n:
        if in_block:
            if text.startswith("*/", i):
                in_block = False
                out.append("  ")
                i += 2
            else:
                out.append("\n" if text[i] == "\n" else " ")
                i += 1
            continue
        if text.startswith("/*", i):
            in_block = True
            out.append("  ")
            i += 2
            continue
        if text.startswith("//", i):
            while i < n and text[i] != "\n":
                out.append(" ")
                i += 1
            continue
        out.append(text[i])
        i += 1
    return "".join(out)


def check_a_frames(main_text):
    """A. 每个 RegisterPage 都带框架描述符。"""
    calls = re.findall(r"RegisterPage\s*\(([^;]*?)\)\s*;", main_text, re.S)
    total = len(calls)
    bad = []
    for call in calls:
        # 顶层逗号切分（本文件的实参不含嵌套括号内的逗号，形如 () => CreateX()）。
        if "InkPageFrame." not in call:
            bad.append(" ".join(call.split())[:90])
    notes.append("A RegisterPage total=%d missing-frame=%d" % (total, len(bad)))
    for b in bad:
        failures.append("[A] RegisterPage without InkPageFrame: %s" % b)
    if total == 0:
        failures.append("[A] parsed 0 RegisterPage calls -- anchor broken (update this script with the new call syntax)")


def check_b_targets(registered):
    """B. 导航目标必须已注册，或在别名/动作白名单里。"""
    ids_text = read(DOM_IDS)
    names = set(re.findall(r"public\s+const\s+string\s+(\w+)\s*=", ids_text))
    constants = dict(re.findall(r"public\s+const\s+string\s+(\w+)\s*=\s*\"([^\"]+)\"", ids_text))

    referenced = {}
    for path in list(iter_cs(PAGES)) + [MAIN_UI]:
        clean = strip_comments(read(path))
        for m in re.finditer(r"InkPageDomIds\.(\w+)", clean):
            name = m.group(1)
            if name not in referenced:
                referenced[name] = (path, line_of(clean, m.start()))

    missing = []
    for name in sorted(referenced):
        if name not in names:
            path, line = referenced[name]
            failures.append("[B] %s:%d 引用了 InkPageDomIds 里不存在的常量: %s"
                            % (to_rel(path), line, name))
            continue
        if name in NON_PAGE_IDS or name.startswith(NON_PAGE_PREFIXES):
            continue
        if name not in registered:
            missing.append("%s (\"%s\") @ %s:%d"
                           % (name, constants.get(name, "?"),
                              to_rel(referenced[name][0]), referenced[name][1]))

    notes.append("B referenced=%d registered=%d missing=%d"
                 % (len(referenced), len(registered), len(missing)))
    for m in missing:
        failures.append("[B] nav target not registered: %s" % m)


def check_c_atmosphere_layers():
    """C. 自绘氛围层只允许出现在自铺舞台页。"""
    hits = 0
    for path in iter_cs(PAGES):
        clean = strip_comments(read(path))
        for m in re.finditer(r"new\s+(InkBackgroundLayer|InkVignette)\s*\(", clean):
            hits += 1
            if os.path.basename(path) not in SELF_LIT_PAGES:
                failures.append(
                    "[C] %s:%d paints %s -- background/vignette must come from the shell "
                    "(if this really is a self-lit stage page, register a SelfLit* frame and "
                    "add the file to SELF_LIT_PAGES in this script)"
                    % (to_rel(path), line_of(clean, m.start()), m.group(1))
                )
    notes.append("C page-painted atmosphere layers=%d (allowlist %s)" % (hits, sorted(SELF_LIT_PAGES)))


def check_d_hardcoded_ids():
    """D. 页面代码不得硬编码 dom-id 字面量。"""
    hits = 0
    for path in iter_cs(PAGES):
        base = os.path.basename(path)
        if base in HARDCODED_DOM_ID_EXEMPT:
            continue
        clean = strip_comments(read(path))
        for m in DOM_ID_LITERAL.finditer(clean):
            hits += 1
            failures.append(
                "[D] %s:%d hardcoded dom-id \"%s\" -- use InkPageDomIds constants"
                % (to_rel(path), line_of(clean, m.start()), m.group(1))
            )
    notes.append("D hardcoded dom-ids=%d (exempt %s)" % (hits, sorted(HARDCODED_DOM_ID_EXEMPT)))


def iter_factories(main_text):
    """从 MainUIManager 抓 private <Type> Create<Name>() 的返回类型与方法体。"""
    out = {}
    for m in re.finditer(r"private\s+([A-Za-z_][\w\.<>]*)\s+Create(\w+)\s*\([^)]*\)\s*\{", main_text):
        ret, name = m.group(1), m.group(2)
        i, depth = m.end(), 1
        while i < len(main_text) and depth:
            if main_text[i] == "{":
                depth += 1
            elif main_text[i] == "}":
                depth -= 1
            i += 1
        out[ret.split(".")[-1]] = (name, main_text[m.end():i])
    return out


def check_e_nav_interface():
    """E. 声明 NavigationRequested 的页面必须实现 IInkNavigationSource（编译期契约）。"""
    declaring = 0
    for path in iter_cs(PAGES):
        text = read(path)
        if "event Action<string> NavigationRequested" not in text:
            continue
        declaring += 1
        if "IInkNavigationSource" not in text:
            failures.append(
                "[E] %s declares NavigationRequested but not IInkNavigationSource "
                "-- the manager can no longer subscribe it (reflection hook is gone)" % to_rel(path)
            )
    notes.append("E pages declaring NavigationRequested=%d" % declaring)


def check_f_factories_hook(main_text):
    """F. 实现接口的页面，其工厂必须真的订阅（hook 或 inline），否则事件永无订阅方=死导航。"""
    factories = iter_factories(main_text)
    checked = 0
    for path in iter_cs(PAGES):
        text = read(path)
        if "event Action<string> NavigationRequested" not in text:
            continue
        for m in re.finditer(r"class\s+(\w+)[^{]*?IInkNavigationSource", text):
            cls = m.group(1)
            checked += 1
            fac = factories.get(cls)
            if fac is None:
                failures.append("[F] %s: no factory in MainUIManager returns %s" % (to_rel(path), cls))
            elif "HookNavigationRequested(" not in fac[1] and "NavigationRequested +=" not in fac[1]:
                failures.append(
                    "[F] %s: factory %s never subscribes NavigationRequested "
                    "-- its nav buttons would be dead" % (to_rel(path), fac[0])
                )
    notes.append("F nav-source pages checked=%d factories=%d" % (checked, len(factories)))


def check_g_esc_ownership(main_text):
    """G. Esc 归属唯一化：中央必须有 Esc 处理并改派给路由器（弹层优先关、否则 Back()）。

    这条是回归护栏：没有它，后续很容易又回到"每个页面自己在 Update 里处理 Esc"，
    同一次按键被页面与路由器各处理一次，退两层。
    """
    handler = re.search(r"KeyboardKeys\.Escape", main_text)
    if handler is None:
        failures.append("[G] MainUIManager has no central Escape handling")
    else:
        window = main_text[handler.start():handler.start() + 700]
        if "_inkPageRouter.CloseOverlay()" not in window:
            failures.append("[G] central Escape does not close the overlay first")
        if "_inkPageRouter.Back()" not in window:
            failures.append("[G] central Escape does not fall back to InkPageRouter.Back()")
    if "_inkPageRouter.CurrentPageOwnsBackKey" not in main_text:
        failures.append("[G] central Escape no longer honours CurrentPageOwnsBackKey "
                        "(pages owning their own back key would double-handle Esc)")
    notes.append("G central Esc handler present=%s" % (handler is not None))


def check_h_page_root_fill(main_text):
    """H. 页根全屏铺底只允许 Modal 页自绘；沉浸式全屏页的铺底必须来自外壳。

    锚点：`AnchorPreset = AnchorPresets.StretchAll;` 之后紧跟 `BackgroundColor = InkWashTheme.Scrim/Void;`
    （中间允许夹注释/空行）。这种"页根铺满 + 不透明底"的写法就是页面自铺的全屏底：
    沉浸式页自铺会与外壳墨底叠成两层，只有模态页需要它当遮罩。
    """
    reg = re.findall(r"RegisterPage\s*\(\s*InkPageDomIds\.(\w+)\s*,\s*\(\)\s*=>\s*(\w+)\(\),\s*InkPageFrame\.(\w+)",
                     main_text)
    factories = iter_factories(main_text)
    factory_kind = {fac_name: kind for _dom, fac_name, kind in reg}
    class_kind = {}
    for ret_type, (fac_name, _body) in factories.items():
        # iter_factories 存的是去掉 Create 前缀的名字，注册表里是完整方法名。
        if ("Create" + fac_name) in factory_kind:
            class_kind[ret_type] = factory_kind["Create" + fac_name]

    fill_re = re.compile(
        r"AnchorPreset = AnchorPresets\.StretchAll;\s*(?:\r?\n\s*(?://[^\n]*)?)*?"
        r"\r?\n\s*BackgroundColor = InkWashTheme\.(Scrim|Void);")
    hits = 0
    for path in iter_cs(PAGES):
        text = read(path)
        found = list(fill_re.finditer(text))
        if not found:
            continue
        classes = [m.group(1) for m in re.finditer(r"\bclass\s+(\w+)", text)]
        for m in found:
            hits += 1
            line = line_of(text, m.start())
            known = [c for c in classes if c in class_kind]
            bad = [c for c in known if class_kind[c] != "Modal"]
            if bad or not known:
                detail = (",".join("%s(%s)" % (c, class_kind[c]) for c in bad)
                          if bad else "unknown/not registered")
                failures.append(
                    "[H] %s:%d page-root full-screen fill but page kind is %s "
                    "-- immersive pages must let the shell paint the base"
                    % (to_rel(path), line, detail))
    notes.append("H page-root fills=%d (only Modal allowed)" % hits)


def check_i_router_dispatch(router_text):
    """I. 路由器的呈现分派必须只由框架描述符驱动（回归护栏）。

    HUD/弹层两种形态暂时只能在源码层断言：这里钉住 NavigateTo 的形状——
    Modal 走叠加层(ShowOverlay)、其余走页面槽(LoadPage)，铺底/暗角开关的实参必须来自 frame；
    并确保那套"按 dom-id 前缀猜 + 两张手写白名单"的老做法没有回来。
    """
    required = [
        ("ShowOverlay(page)", "Modal 必须挂到叠加层"),
        ("_shell.LoadPage(page)", "非 Modal 必须挂到页面槽"),
        ("InkPageKind.Modal", "必须显式判 Modal 类型"),
        ("ShowBackgroundLayer(!frame.IsBare)", "铺底开关必须由 frame 推导"),
        ("ShowVignette(!frame.IsBare && !frame.SelfLitStage)", "暗角开关必须由 frame 推导"),
        ("ShowBackButton(!isHud && !frame.OwnBack)", "返回按钮开关必须由 frame 推导"),
    ]
    for needle, why in required:
        if needle not in router_text:
            failures.append("[I] InkPageRouter 缺少分派锚点 `%s`（%s）" % (needle, why))

    banned = [
        ('StartsWith("popup-"', "不得再按 popup- 前缀判弹层"),
        ("ShellBackSuppressedPages", "不得再用手写返回白名单"),
        ("SelfDressedPages", "不得再用手写自铺舞台白名单"),
        ("GetEvent(\"NavigationRequested\")", "不得再用反射找导航事件"),
    ]
    codes = strip_comments(router_text) + "\n" + strip_comments(read(MAIN_UI))
    for needle, why in banned:
        if needle in codes:
            failures.append("[I] 仍在用约定式判定 `%s`（%s）" % (needle, why))

    notes.append("I router dispatch anchors checked=%d banned-patterns=%d" % (len(required), len(banned)))


def check_j_no_orphan_pages(main_text):
    """J. 页面不得成为"注册表外的孤儿"：实现 IInkPage 的类必须能经某个已注册工厂创建。

    孤儿 = 有页面类、却没有导航入口（本轮就抓到过一个：`InventoryPage` —— 背包合并进角色面板后，
    工厂 `CreateInventoryPage` 没被任何 RegisterPage 调用，页面永远打不开）。
    基类与组件（不直接当页面挂载的）显式列在下面的白名单里。
    """
    base_or_component = {"CcStagePage", "InkAnimationRefPageBase", "CharacterPreview3D"}

    named = {}
    for m in re.finditer(r"private\s+([A-Za-z_][\w\.<>]*)\s+(Create\w+)\s*\([^)]*\)", main_text):
        named[m.group(2)] = m.group(1).split(".")[-1]
    reachable = {named[f] for f in re.findall(r"=>\s*(Create\w+)\(\)", main_text) if f in named}

    checked = 0
    scan_roots = [PAGES, os.path.join(UI, "Ink", "Components")]
    for path in [p for root in scan_roots for p in iter_cs(root)]:
        text = read(path)
        for m in re.finditer(r"class\s+(\w+)\s*:\s*[^{]*\bIInkPage\b", text):
            cls = m.group(1)
            if cls in base_or_component:
                continue
            checked += 1
            if cls not in reachable:
                failures.append(
                    "[J] %s: class %s implements IInkPage but no registered factory reaches it "
                    "-- unreachable page (delete it or wire a RegisterPage)" % (to_rel(path), cls))
    notes.append("J IInkPage classes checked=%d reachable=%d allowlisted=%d"
                 % (checked, len(reachable), len(base_or_component)))


def check_k_no_nav_bypass(main_text):
    """K. 页面只能通过 NavigationRequested 发起跳转，不得绕过路由器。

    两种绕过方式各对应一个真实风险：直接 `new 另一个页面类()` 会绕过返回栈/框架分派（底页被顶掉、
    返回语义丢失）；直接调 `MainUIManager.Instance.XXX` 做导航会把"谁负责导航"重新打散。
    """
    page_types = set()
    for m in re.finditer(r"private\s+([A-Za-z_][\w\.<>]*)\s+(Create\w+)\s*\([^)]*\)", main_text):
        page_types.add(m.group(1).split(".")[-1])

    # 只读查询不算导航，避免把无害调用一起拦（闸门红了就该是真问题）。
    read_only_members = {"IsOnCombatHudPage", "InkUiRoot", "IsOverlayOpen"}

    new_hits = 0
    mgr_hits = 0
    for path in iter_cs(PAGES):
        text = strip_comments(read(path))
        own = set(re.findall(r"class\s+(\w+)", text))
        for m in re.finditer(r"new\s+([A-Z]\w+)\s*\(", text):
            if m.group(1) in page_types and m.group(1) not in own:
                new_hits += 1
                failures.append("[K] %s:%d 直接 new 另一个页面 %s —— 必须发 NavigationRequested 走路由器"
                                % (to_rel(path), line_of(text, m.start()), m.group(1)))
        for m in re.finditer(r"MainUIManager\.Instance\s*\.\s*(\w+)", text):
            if m.group(1) in read_only_members:
                continue
            mgr_hits += 1
            failures.append("[K] %s:%d 页面直接调 MainUIManager.Instance.%s —— 导航须经 NavigationRequested"
                            % (to_rel(path), line_of(text, m.start()), m.group(1)))
    notes.append("K bypass new=%d direct-manager=%d" % (new_hits, mgr_hits))


def check_l_modal_close_uses_back(main_text):
    """L. 弹层页的关闭/返回不得直跳枢纽：必须发 BackOneLevel（走返回栈、关掉弹层回到下层页面）。

    锚点：注册为 `Modal` 的页面，其工厂方法体里不得出现 `NavigateToPage(InkPageDomIds.BackHud)`。
    直跳枢纽会让"在角色面板上弹出的确认框"关掉后整页被顶掉，也会丢掉返回栈。
    """
    reg = re.findall(r"RegisterPage\s*\(\s*InkPageDomIds\.(\w+)\s*,\s*\(\)\s*=>\s*(Create\w+)\(\),\s*InkPageFrame\.(\w+)",
                     main_text)
    factories = iter_factories(main_text)
    checked = 0
    for dom, fac_name, kind in reg:
        if kind != "Modal":
            continue
        checked += 1
        short = fac_name[6:] if fac_name.startswith("Create") else fac_name
        body = (factories.get(fac_name[6:]) or factories.get(short) or (None, ""))[1]
        if "NavigateToPage(InkPageDomIds.BackHud)" in body:
            failures.append(
                "[L] Modal page %s (factory %s) still closes via BackHud -- use BackOneLevel so the "
                "overlay closes onto the page underneath" % (dom, fac_name))
    notes.append("L modal factories checked=%d" % checked)


def check_m_hub_follows_variant(router_text):
    """M. 枢纽身份必须跟随激活的 HUD 变体。

    锚点（三处缺一不可，都对应过真实缺陷）：`NavigateTo` 里进入 HUD 变体时更新 `_hudDomId`；
    `Back()` 弹栈时把 `_hudDomId` 也算作根而停下；入栈条件排除 `_hudDomId`。
    否则切到沉浸式(V2) 之后，按 Esc/返回/back-hud 会被静默送回传统式。
    """
    anchors = [
        ("_hudDomId = domId;", "进入 HUD 变体时要更新枢纽身份"),
        ("target == _hudDomId) break;", "Back() 要把当前枢纽当根停下"),
        ("_currentPageDomId != _hudDomId", "入栈条件要排除当前枢纽"),
        ("public string HudDomId => _hudDomId;", "要有只读出口便于诊断/取证"),
    ]
    for needle, why in anchors:
        if needle not in router_text:
            failures.append("[M] InkPageRouter 缺少枢纽变体锚点 `%s`（%s）" % (needle, why))
    notes.append("M hub-variant anchors checked=%d" % len(anchors))


def check_n_ownback_has_exit(main_text):
    """N. 声明了 OwnBack（隐藏外壳返回）的页面必须自带出口，否则玩家进去出不来。

    信号取"能退出去"的功能接线：返回/关闭/退出事件，或直接发 BackOneLevel/BackHud，或进入世界的推进钮。
    三处特例（都已在别处核实过，不是漏洞）：
      - 创角五页的出口在基类 `CcStagePage`（返回按钮/箭头/Esc 都发 `BackRequested`），派生文件里看不到，
        故 `Cc*` 页把 `CcStagePage.cs` 也算作信号来源；
      - `loading-1`/`loading-2` 是自动推进的过场，本就不该有返回；
      - `chapter-transition` 同为定时过场（`Update` 里自己推进，由路由器导航进战斗 HUD），刻意不可跳。
    """
    reg = re.findall(r"RegisterPage\s*\(\s*InkPageDomIds\.(\w+)\s*,\s*\(\)\s*=>\s*(Create\w+)\(\),\s*InkPageFrame\.(\w+)",
                     main_text)
    named = {}
    for m in re.finditer(r"private\s+([A-Za-z_][\w\.<>]*)\s+(Create\w+)\s*\([^)]*\)", main_text):
        named[m.group(2)] = m.group(1).split(".")[-1]

    loc = {}
    for root in (PAGES,):
        for path in iter_cs(root):
            for m in re.finditer(r"class\s+(\w+)", read(path)):
                loc.setdefault(m.group(1), path)

    sig = re.compile(r"BackOneLevel|BackHud|BackRequested|CloseRequested|ExitRequested|EnterWorld|Closed\s*\+=|Cancelled\s*\+=")
    auto_pages = {"Loading1", "Loading2", "ChapterTransition"}
    cc_base = os.path.join(PAGES, "CharacterCreation", "CcStagePage.cs")
    cc_base_text = read(cc_base) if os.path.exists(cc_base) else ""

    checked = 0
    for dom, fac, kind in reg:
        if not kind.endswith("OwnBack"):
            continue
        checked += 1
        if dom in auto_pages:
            continue
        cls = named.get(fac, "")
        path = loc.get(cls)
        if path is None:
            failures.append("[N] %s: 找不到 %s 的源文件，无法核对自带出口" % (dom, cls))
            continue
        text = read(path)
        if cls.startswith("Cc"):
            text += cc_base_text
        body = strip_comments(text)
        if not sig.search(body):
            failures.append("[N] %s (%s, %s) 隐藏了外壳返回却看不到自带出口 —— 进去会出不来"
                            % (dom, cls, to_rel(path)))
        # OwnBack 的另一半含义是"中央 Esc 不改派，本自己接"。声明了却一行 Escape 都不处理，
        # 结果就是外壳返回钮被藏、Esc 又没人接 —— 整屏页盖住游戏而键盘上没有任何出口。
        if "KeyboardKeys.Escape" not in body:
            failures.append("[N] %s (%s, %s) 声明 OwnBack 却不处理 KeyboardKeys.Escape —— 中央 Esc 会跳过它，Esc 变死键"
                            % (dom, cls, to_rel(path)))
    notes.append("N OwnBack pages checked=%d (auto-advance exempt: %s)" % (checked, sorted(auto_pages)))


def check_o_pages_use_back_stack():
    """O. 非 HUD 页面的返回/关闭一律走返回栈（`BackOneLevel`），不得再出现直跳枢纽的 `BackHud`。

    这条把 S7 的结果钉死：以前"页面自带返回按钮 → 直跳枢纽"会跳过返回栈，二级链路里丢上一层。
    战斗 HUD 是根，它自己的"退出"热区用 `BackHud` 是允许的。
    """
    hud_pages = {"CombatHudPage.cs", "CombatHudV2Page.cs", "CombatHudTraditionalPage.cs"}
    offenders = 0
    for path in iter_cs(PAGES):
        base = os.path.basename(path)
        if base in hud_pages:
            continue
        text = strip_comments(read(path))
        for m in re.finditer(r"InkPageDomIds\.BackHud", text):
            offenders += 1
            failures.append("[O] %s:%d 非 HUD 页面仍直跳枢纽（BackHud）—— 应发 BackOneLevel 走返回栈"
                            % (to_rel(path), line_of(text, m.start())))
    notes.append("O non-HUD pages jumping to hub=%d (HUD 页豁免 %s)" % (offenders, sorted(hud_pages)))


def check_p_sidebar_covers_pages(main_text):
    """P. 侧边栏必须能到达所有"功能页"：凡注册为 Immersive 且不是流程/事件/开发页的，都要在 InkSidebarNav 清单里。

    对应需求"侧边栏可以操作所有除弹框和事件 UI 之外的 UI"。排除集是显式口径：
    创角/选角流程、加载与章节过场、引导/灵台/元素视野（游戏内叠加或事件页）、UI 画廊与水墨动画参考页（开发用），
    以及同一页的别名（只保留正名，别重复列）。
    """
    excluded = {
        # 流程页
        "CcRoleList", "CcGender", "CcFacePreset", "CcFaceSculpt", "CcName",
        "Loading1", "Loading2", "ChapterTransition",
        # 开发/参考页
        "UIGallery", "InkAnimRefSpread", "InkAnimRefBrush",
        # 游戏内叠加 / 事件页
        "GuideHud", "Acupoint", "NavElementVision",
        # 同页别名（正名已在清单里）
        "NavInventory", "NavCharacterV2", "MenuCharAttributes", "NavSocialMail", "NavSocialShop",
        "NavQuestLog",
    }

    model_path = os.path.join(UI, "Ink", "InkSidebarNav.cs")
    if not os.path.exists(model_path):
        failures.append("[P] 缺少侧边栏清单 Ink/InkSidebarNav.cs")
        return
    listed = set(re.findall(r"InkPageDomIds\.(\w+)", strip_comments(read(model_path))))

    reg = re.findall(r"RegisterPage\s*\(\s*InkPageDomIds\.(\w+)\s*,\s*\(\)\s*=>\s*(Create\w+)\(\),\s*InkPageFrame\.(\w+)",
                     main_text)
    missing = [d for d, _f, k in reg if k == "Immersive" and d not in excluded and d not in listed]

    notes.append("P sidebar entries=%d immersive functional pages missing=%d" % (len(listed), len(missing)))
    for d in missing:
        failures.append("[P] 功能页 %s 不在侧边栏清单里 —— 侧边栏将无法到达该页" % d)


def check_q_sidebar_is_shared():
    """Q. 有侧边栏的页面必须用共用的 InkSidebarList，不许再各写一套（否则"侧边栏可达所有 UI"就守不住）。

    例外（显式待接入）：
      - `MenuAppearancePage`：文件正被并行会话编辑，本轮刻意未动，等其稳定后接入；
      - `UIGalleryPage`：开发用页面，不做导航侧边栏。
    """
    pending = {"MenuAppearancePage.cs", "UIGalleryPage.cs"}
    # 侧边栏特征：同时存在左栏容器与一组导航行类/列表
    sidebar_markers = ("private const float SidebarWidth", "SidebarWidth =", "LeftNavWidth =")
    offenders = 0
    for path in iter_cs(PAGES):
        base = os.path.basename(path)
        text = read(path)
        if not any(mk in text for mk in sidebar_markers):
            continue
        if "InkSidebarList" in text:
            continue
        if base in pending:
            continue
        offenders += 1
        failures.append("[Q] %s 有侧边栏但未使用共用 InkSidebarList —— 会与统一清单脱节" % to_rel(path))
    notes.append("Q pages with sidebar not using shared list=%d (pending %s)" % (offenders, sorted(pending)))


def check_r_no_unregistered_id_literals(main_text):
    """R. 页面里不得出现"看着像 dom-id 但注册表里没有"的字面量 —— 这类入口点了没反应（死导航）。

    锚点是真实的坑：`MenuTeamPage` 的侧边栏传的是 `"menu-quests"` / `"menu-team"` / `"settings"` 这类
    字面量，而注册的 id 是 `nav-quests` / `nav-team` / `nav-settings`，于是整条侧边栏 10 个入口全点不动。
    """
    ids_text = read(DOM_IDS)
    registered = set(re.findall(r"public\s+const\s+string\s+\w+\s*=\s*\"([^\"]+)\"", ids_text))
    registered |= NON_PAGE_IDS

    looks_like_id = re.compile(r'"([a-z][a-z0-9]*(?:-[a-z0-9]+)+)"')
    suspicious_single = {
        "settings", "inventory", "shop", "quests", "mail", "team", "sect", "gacha",
        "bestiary", "compass", "crafting", "achievement",
    }

    hits = 0
    for path in iter_cs(PAGES):
        text = strip_comments(read(path))
        for m in looks_like_id.finditer(text):
            lit = m.group(1)
            if lit in registered or lit.startswith(("action-", "popup-", "reward-", "cc-")):
                continue
            hits += 1
            failures.append("[R] %s:%d 字面量 \"%s\" 不是已注册 dom-id —— 该入口点了没反应"
                            % (to_rel(path), line_of(text, m.start()), lit))
        for lit in suspicious_single:
            for m in re.finditer(r'"%s"' % lit, text):
                if lit in registered:
                    continue
                hits += 1
                failures.append("[R] %s:%d 字面量 \"%s\" 不是已注册 dom-id —— 该入口点了没反应"
                                % (to_rel(path), line_of(text, m.start()), lit))
    notes.append("R unregistered id literals=%d" % hits)


def main():
    main_text = read(MAIN_UI)
    registered = set(re.findall(r"RegisterPage\s*\(\s*InkPageDomIds\.(\w+)", main_text))
    if not registered:
        failures.append("[init] parsed 0 registered pages -- anchor broken")

    check_a_frames(main_text)
    check_b_targets(registered)
    check_c_atmosphere_layers()
    check_d_hardcoded_ids()
    check_e_nav_interface()
    check_f_factories_hook(main_text)
    check_g_esc_ownership(main_text)
    check_h_page_root_fill(main_text)
    check_i_router_dispatch(read(os.path.join(UI, "Ink", "InkPageRouter.cs")))
    check_j_no_orphan_pages(main_text)
    check_k_no_nav_bypass(main_text)
    check_l_modal_close_uses_back(main_text)
    check_m_hub_follows_variant(read(os.path.join(UI, "Ink", "InkPageRouter.cs")))
    check_n_ownback_has_exit(main_text)
    check_o_pages_use_back_stack()
    check_p_sidebar_covers_pages(main_text)
    check_q_sidebar_is_shared()
    check_r_no_unregistered_id_literals(main_text)

    for note in notes:
        print("* " + note)
    if failures:
        print("\n%d violation(s):" % len(failures))
        for f in failures:
            print("  " + f)
        return 1
    print("\nInk page-frame & navigation gate: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
