"""星球近景实机回执的判据器（离线可自证，真机上一次跑完验收）。

**为什么要它**：任务 #25/#30 的验收要看 `Saved/planet_near_field.result.txt` 里那一串键，
而人眼读文本会犯两类错：把"这一行在"当成"这一行的值对"，以及拿一份<em>上一版装配</em>的回执
当这一版的证据（§13.59 环境结论：编译过≠跑过，跑过≠跑的是这一版）。所以把验收写成代码，
并且让它自己证明每条断言都真会红。

用法：
  python Tools/planet-session-check.py --self-test
  python Tools/planet-session-check.py --receipt <planet_near_field.result.txt> [--silo-log <silo日志>]

判定分三档，SKIP 必须进汇总（跑了看不见的检查等于没跑）：
  ok    这一项对
  FAIL  这一项错或缺（verdict 直接 FAIL）
  SKIP  这一项没法判（缺输入），计数进汇总行

`build_tag` 的期望值<em>从源码里读</em>（`PlanetNearFieldDriver.cs` 的 `BuildTag` 常量），
不在本文件里抄第二份——抄了就会允许"拿旧装配的回回执蒙新一次的验收"这件事发生。
"""
import argparse
import io
import os
import re
import sys

sys.stdout.reconfigure(encoding="utf-8")
ROOT = r"C:/Works/GitHubProjects/HundunWorld"
DRIVER = os.path.join(ROOT, "HundunWorld/Source/Game/Planet/PlanetNearFieldDriver.cs")


def expected_build_tag():
    """从源码取当前 BuildTag。取不到就大声失败——它是"这份回执是不是这一版装配跑出来的"唯一凭据。"""
    text = io.open(DRIVER, encoding="utf-8-sig", errors="replace").read()
    m = re.search(r'BuildTag\s*=\s*"([^"]+)"', text)
    if not m:
        raise SystemExit("FAIL  源码里读不到 BuildTag 常量：%s —— 组装新鲜度判据没有参照了" % DRIVER)
    return m.group(1)


def find(lines, prefix):
    for l in lines:
        if l.strip().startswith(prefix):
            return l.strip()
    return None


def kv(line):
    """把 `key a=1 b=2` 这种行拆成字典；值里带空格的一律取到行尾，不做二次切分。"""
    out = {}
    if line is None:
        return out
    for m in re.finditer(r"(\w+)=(\S+)", line):
        # 先到先得：同一份行里 `plan=diff +regions=1 -regions=0 +cells=2 -cells=0` 这种差集表示法
        # 会把同一个键写三遍，后写的会盖掉真正那两个结构化字段，读出来的数就变了形状
        out.setdefault(m.group(1), m.group(2))
    return out


def check_receipt(text, silo_log_text, want_tag):
    """返回 [(状态, 键, 说明)]。每条都要能被 --self-test 里的一次变异证伪。"""
    lines = text.splitlines()
    rows = []
    add = rows.append

    def number(line, key):
        raw = kv(line).get(key)
        if raw is None:
            return None
        m = re.match(r"^-?\d+", raw)
        return int(m.group(0)) if m else None

    # 1. 装配新鲜度：回执必须是当前这一版 BuildTag 跑出来的
    tag_line = find(lines, "build_tag=")
    got = kv(tag_line).get("build_tag")
    if got is None:
        add(("FAIL", "build_tag", "回执里没有 build_tag 这一行：这份文件不是近景驱动写的"))
    elif got != want_tag:
        add(("FAIL", "build_tag", "回执是 %s，源码是 %s —— 装配没换，别拿这份当本轮证据" % (got, want_tag)))
    else:
        add(("ok", "build_tag", got))

    # 2. 会话清账：开局那行的账必须全是 0（跨 PIE 会话的静态账没漏清）
    reset = find(lines, "session_reset=")
    if reset is None:
        add(("FAIL", "session_reset", "没有这一行：BeginSession 没跑，上一轮的在飞标记/探针可能还在"))
    else:
        bad = {k: v for k, v in kv(reset).items()
               if k.startswith("sent") or k.startswith("receipts") or k.startswith("expired")
               or k.startswith("pending") or k.startswith("confirmed")}
        if any(v not in ("0",) for v in bad.values()):
            add(("FAIL", "session_reset", "清账之后账不是 0：%s" % reset))
        else:
            add(("ok", "session_reset", reset))

    # 3. 基场真的在用（analytic_ramp 那一档没有真地形，口径与地面判定都不会成立）
    height = find(lines, "request_lat_lon=")
    src = kv(height).get("height_source")
    if src != "real_gebco":
        add(("FAIL", "height_source", "是 %r，不是 real_gebco：DEM 没加载成功，后面的口径/地面判定都不算" % src))
    else:
        add(("ok", "height_source", src))

    # 4 & 5. 口径的两个出处必须逐字相同：dem_loaded 算出来的版本 == planet_caliber 带出去的版本
    dem = find(lines, "dem_loaded")
    cal = find(lines, "planet_caliber")
    dem_v = re.search(r"terrain_?version=([0-9A-Fa-f]{8})", dem or "", re.I)
    cal_v = re.search(r"terrain_?version=([0-9a-fA-F]{8})", cal or "", re.I)
    if dem_v is None:
        add(("FAIL", "dem_loaded", "没有这一行或没有 terrain_version：基场没装配"))
    else:
        add(("ok", "dem_loaded", "terrain_version=%s" % dem_v.group(1)))
    if cal_v is None:
        add(("FAIL", "planet_caliber", "没有 terrain_version：口径没装上，换区上行会全部被客户端自己拦下"))
    else:
        add(("ok", "planet_caliber", "terrain_version=%s" % cal_v.group(1)))
    if dem_v is not None and cal_v is not None:
        if dem_v.group(1).lower() != cal_v.group(1).lower():
            add(("FAIL", "caliber_matches_dem", "地形=%s 而口径=%s：装配分成了两份真相"
                 % (dem_v.group(1), cal_v.group(1))))
        else:
            add(("ok", "caliber_matches_dem", dem_v.group(1)))

    # 6. 地面探针接上了（没接上时穿模抬位那条路是静默关掉的）
    if find(lines, "ground_probe on") is None:
        add(("FAIL", "ground_probe", "没有 ground_probe on：客户端不会把地下点抬到地面"))
    else:
        add(("ok", "ground_probe", "on"))

    # 6.5 角色位姿口径：探针必须真的量过一次，而且量出来的结论与用的口径必须一致。
    #     这一条不许"缺席也算过"：#25 的取样要么有证据支持，要么根本没人知道角色在哪。
    pose = find(lines, "actor_pose ")
    p = kv(pose)
    if pose is None:
        add(("FAIL", "actor_pose", "没有这一行：没探过角色位姿，换区上行的点就没有口径证据"))
    else:
        skipped = re.search(r"skipped=([^\s]+)", pose)
        verdict = p.get("verdict")
        used = p.get("pose_mode")
        if skipped:
            add(("FAIL", "actor_pose", "skipped=%s：这一次跑没有本地角色或没有基场，口径没法测"
                 % skipped.group(1)))
        elif verdict in ("FlaxYUpSupported", "EcsZUpSupported"):
            want = "flax" if verdict == "FlaxYUpSupported" else "ecs"
            if used != want:
                add(("FAIL", "actor_pose", "测出来该用 %s 档，而这一轮用的是 pose_mode=%s —— 有证据却没用"
                     % (want, used)))
            else:
                add(("ok", "actor_pose", "%s 且已按 %s 档取样（alt %s/%s）"
                     % (verdict, used, p.get("alt_flax"), p.get("alt_ecs"))))
        elif verdict in ("Ambiguous", "Neither"):
            if used not in (None, "probe"):
                add(("FAIL", "actor_pose", "口径分不开（%s）却用了 pose_mode=%s：这是猜的" % (verdict, used)))
            else:
                add(("ok", "actor_pose", "%s（两档 alt=%s/%s，先按合成走查点取样）"
                     % (verdict, p.get("alt_flax"), p.get("alt_ecs"))))
        else:
            add(("FAIL", "actor_pose", "verdict=%r 不认识（UnusableInput 也算：输入退化就是没量成）" % verdict))

    # 7. 换区上行真的往返过
    transfers = find(lines, "transfers ")
    t = kv(transfers)
    if transfers is None:
        add(("FAIL", "transfers", "没有这一行：驱动没跑换区那条链"))
    else:
        sent, receipts = number(transfers, "sent"), number(transfers, "receipts")
        if not sent:
            add(("FAIL", "transfers_sent", "sent=%s（一条都没发；caliber_status=%s character=%s session=%s）"
                 % (sent, t.get("caliber_status"), t.get("character"), t.get("session"))))
        elif not receipts:
            add(("FAIL", "transfers_receipts", "发了 %s 条但回执 0 条：服务端没答，或答了没人接" % sent))
        else:
            add(("ok", "transfers", "sent=%s receipts=%s last_action=%s confirmed=%s"
                 % (sent, receipts, t.get("last_action"), t.get("confirmed"))))
        ack = t.get("ack_check")
        if ack != "consistent":
            add(("FAIL", "ack_check", "是 %r（应为 consistent）：ack 坐标系那一半没被解开或两端切平面不一致" % ack))
        else:
            add(("ok", "ack_check", "consistent axis_deg=%s" % t.get("ack_axis_deg")))
        # 抬位次数只报不判：真地形上角色控制器报的点本来可能在地下，抬了才是对的
        add(("note", "ground_lifts", "%s 次，最后 %s cm（非 0 说明控制器给的高于/低于基场，两端读的数据不同才抬）"
             % (t.get("ground_lifts"), t.get("ground_lift_cm"))))

    # 8. 快照订阅真的拿到了归属
    watches = find(lines, "watches ")
    w = kv(watches)
    if watches is None:
        add(("FAIL", "watches", "没有这一行：快照那条链没跑"))
    elif w.get("action") != "Apply" or number(watches, "owner") in (None, 0):
        add(("FAIL", "watches", "action=%s owner=%s why=%s（服务端没给订阅集或被拒）"
             % (w.get("action"), w.get("owner"), w.get("why"))))
    else:
        add(("ok", "watches", "action=%s owner=%s subs=%s cells=%s"
             % (w.get("action"), w.get("owner"), w.get("subs"), w.get("cells"))))

    # 9 & 10. 驱动自己的结论
    verdict_line = find(lines, "driver_verdict=")
    if kv(verdict_line).get("driver_verdict") != "NEAR_FIELD_DRIVER_RUNS_IN_ENGINE":
        add(("FAIL", "driver_verdict", "%s（附 fail_reason=%s）"
             % (verdict_line, kv(find(lines, "fail_reason=")).get("fail_reason"))))
    else:
        add(("ok", "driver_verdict", "NEAR_FIELD_DRIVER_RUNS_IN_ENGINE"))
    passed = find(lines, "passed=")
    if kv(passed).get("passed") != "1":
        add(("FAIL", "passed", "%r" % (kv(passed).get("passed") if passed else "没有这一行")))
    else:
        add(("ok", "passed", "1"))

    # 11. 服务端两侧闸门：口径与地面必须都 on，而且 terrainVersion 与客户端同一个数
    if silo_log_text is None:
        add(("SKIP", "silo_gates", "没给 --silo-log：服务端那侧的 口径闸门/地面闸门 没判"))
    else:
        cal_on = re.search(r"口径闸门=on\(terrain_?version=([0-9a-fA-F]{8})", silo_log_text, re.I)
        grd_on = re.search(r"地面闸门=on\(terrain_?version=([0-9a-fA-F]{8})", silo_log_text, re.I)
        if not cal_on or not grd_on:
            add(("FAIL", "silo_gates", "服务端日志里没同时出现两个 on：口径=%s 地面=%s"
                 % ("on" if cal_on else "off", "on" if grd_on else "off")))
        elif cal_on.group(1).lower() != grd_on.group(1).lower():
            add(("FAIL", "silo_gates", "服务端自己就不一致：口径=%s 地面=%s" % (cal_on.group(1), grd_on.group(1))))
        elif cal_v and cal_on.group(1).lower() != cal_v.group(1).lower():
            add(("FAIL", "silo_gates", "服务端 terrainVersion=%s 与客户端 %s 不同：两端读的不是同一份基场"
                 % (cal_on.group(1), cal_v.group(1))))
        else:
            add(("ok", "silo_gates", "两闸门同 on 且 terrainVersion=%s" % cal_on.group(1)))
    return rows


def render(rows):
    fails = skips = 0
    for status, key, detail in rows:
        if status == "FAIL":
            fails += 1
        if status == "SKIP":
            skips += 1
        print("%-5s %-20s %s" % (status, key, detail))
    print("failed=%d skipped=%d" % (fails, skips))
    print("verdict=" + ("FAIL" if fails else "PASS"))
    return 1 if fails else 0


# ==================== 自证：每条断言都得能被一次变异翻红 ====================

def good_receipt(tag):
    return "\n".join([
        "build_tag=%s" % tag,
        "session_reset=clean transfers sent=0 inflight_refused=0 clock_refused=0 expired=0 receipts=0 pending=0",
        "request_lat_lon=30/120 height_source=real_gebco",
        "dem_loaded levels=4 content_seed=123456789ABCDEF0 terrain_version=ABCD1234",
        "planet_caliber root=Data/GEBCO_2026/derived terrainVersion=abcd1234 seed=1234 basefield=1",
        "ground_probe on",
        "actor_pose at=crossing pose_mode=probe raw_actor=120000,40000,60000 ground_cm=0.0 "
        "verdict=Neither alt_flax=39998.4 alt_ecs=59998.4 region_flax=140737488355328 "
        "region_ecs=140737488355329 why=两档都不在带内",
        "transfers sent=3 inflight_refused=1 clock_refused=0 expired=0 receipts=3 pending=0 "
        "last_action=AdoptServerRegion last_receipt_ms=1234 confirmed=555 needs_rebind=0 "
        "ack=1@2,3,4 character=7 session=live caliber_status=ok ack_check=consistent ack_axis_deg=0.00003 "
        "ground_probe=on ground_lifts=0 ground_lift_cm=0",
        "watches pending=0 subs=7 cells=12 owner=555 plan=diff +regions=1 -regions=0 +cells=2 -cells=0 "
        "action=Apply why=已发出 last_req_ms=100 last_resp_ms=200",
        "driver_verdict=NEAR_FIELD_DRIVER_RUNS_IN_ENGINE",
        "passed=1"])


MUTATIONS = [
    ("装配没换（回执是上一版驱动写的）", lambda s, tag: s.replace("build_tag=%s" % tag, "build_tag=near_field_driver_v9")),
    ("会话没清账（少了那一行）", lambda s, tag: "\n".join(l for l in s.splitlines() if not l.startswith("session_reset="))),
    ("清账后账不是 0（上一轮的还在）", lambda s, tag: s.replace("session_reset=clean transfers sent=0", "session_reset=clean transfers sent=2")),
    ("DEM 没加载，退成解析坡面", lambda s, tag: s.replace("height_source=real_gebco", "height_source=analytic_ramp")),
    ("口径与基场是两个版本（装配分成两份真相）", lambda s, tag: s.replace("terrainVersion=abcd1234", "terrainVersion=99999999")),
    ("没有 dem_loaded 那一行", lambda s, tag: "\n".join(l for l in s.splitlines() if not l.startswith("dem_loaded"))),
    ("地面探针没接（抬位那条路静默关掉）", lambda s, tag: "\n".join(l for l in s.splitlines() if l.strip() != "ground_probe on")),
    ("一条换区都没发出去", lambda s, tag: re.sub(r"(transfers sent=)3", r"\g<1>0", s)),
    ("发了但一条回执都没回来", lambda s, tag: re.sub(r"(receipts=)3", r"\g<1>0", s)),
    ("ack 坐标系没被解过（协议那一半没人读）", lambda s, tag: s.replace("ack_check=consistent", "ack_check=not run")),
    ("角色位姿压根没探（口径没证据）", lambda s, tag: "\n".join(l for l in s.splitlines() if not l.startswith("actor_pose "))),
    ("探针被跳过（没有本地角色）", lambda s, tag: re.sub(r"actor_pose at=crossing [^\n]*",
                                                    "actor_pose at=crossing skipped=no_local_actor", s)),
    ("测出来该用 flax 档却还在 probe", lambda s, tag: s.replace("verdict=Neither", "verdict=FlaxYUpSupported")),
    ("口径分不开却硬选了一档", lambda s, tag: s.replace("pose_mode=probe raw_actor", "pose_mode=ecs raw_actor")),
    ("快照被拒（没有归属）", lambda s, tag: s.replace("action=Apply", "action=Rebind").replace("owner=555", "owner=0")),
    ("驱动自己判出问题", lambda s, tag: s.replace("driver_verdict=NEAR_FIELD_DRIVER_RUNS_IN_ENGINE", "driver_verdict=DRIVER_PROBLEM")),
    ("passed=0", lambda s, tag: s.replace("passed=1", "passed=0")),
]


def silo_log(version="abcd1234", both_on=True):
    cal = "口径闸门=on(terrainVersion=%s)" % version if both_on else "口径闸门=off(未注册地面来源)"
    grd = "地面闸门=on(terrainVersion=%s)" % version
    return "region grain activated ... %s %s\n" % (cal, grd)


def self_test():
    tag = expected_build_tag()
    base = good_receipt(tag)
    survivors = []
    rows = check_receipt(base, silo_log(), tag)
    ok_base = all(r[0] != "FAIL" for r in rows)
    print("=== 正向孪生：完好回执必须 PASS ===")
    for status, key, detail in rows:
        print("  %-5s %-20s %s" % (status, key, detail))
    print("  -> %s" % ("PASS" if ok_base else "FAIL"))
    if not ok_base:
        print("SELFTEST FAIL 完好回执自己不通过：判据写错了，后面的变异结论都不成立")
        return 1

    print("=== 变异：每条都必须翻红 ===")
    for label, mutate in MUTATIONS:
        got = check_receipt(mutate(base, tag), silo_log(), tag)
        bad = [r for r in got if r[0] == "FAIL"]
        print("%-40s -> %s" % (label, ",".join(r[1] for r in bad) if bad else "（没红：这条断言是假的）"))
        if not bad:
            survivors.append(label)

    # 服务端那侧的三条也各证一次：日志缺、单闸门、版本不同
    for label, log in [("服务端日志没给", None),
                       ("服务端只有口径闸门 on", silo_log(both_on=False)),
                       ("服务端与客户端基场版本不同", silo_log("11112222"))]:
        got = check_receipt(base, log, tag)
        bad = [r[1] for r in got if r[0] == "FAIL"]
        skip = [r[1] for r in got if r[0] == "SKIP"]
        hit = "silo_gates" in bad or (label == "服务端日志没给" and "silo_gates" in skip)
        print("%-40s -> %s" % (label, "红/如实跳过" if hit else "（没红：这条断言是假的）"))
        if not hit:
            survivors.append(label)

    print("=== 存活 ===")
    print("\n".join(survivors) if survivors else "（无：每条变异都被对应断言拦下）")
    print("SELFTEST " + ("PASS" if not survivors else "FAIL"))
    return 1 if survivors else 0


def runbook(dem):
    """把实机那一步的<em>输入形状</em>打出来，而不是靠记忆里的那句"DemDir=..."。
    标记文件是<em>按位置</em>解析的（`PlanetNearFieldDriver.ParseMarker`：
    `Lat Lon [LoadsPerFrame [CollisionLod [StepFrames [MaxFrames [DemDir]]]]]`，另认 `inline=N`），
    写成 `DemDir=路径` 会被当成第 7 个位置参数以外的东西——那种错的表现是"坡面又回来了"，不是报错。"""
    tag = expected_build_tag()
    print("# 实机验收（#25 换区上行往返 / #30 服务端闸门）——照这三步，判据由本文件负责")
    print("1) 让 Silo 与 Gateway 跑当前这一版（现在在跑的那两个进程自 2026-11-03 23:01 起没换过，")
    print("   而 bin/Debug 被它们锁着：重启它们要用户点头，不要替用户杀）。服务端侧要么设环境变量")
    print("   PLANET_DEM_ROOT，要么配 Planet:DemRoot，两者都指向基场产物目录：")
    print("     %s" % os.path.abspath(dem))
    marker = os.path.abspath(os.path.join(ROOT, "HundunWorld/Saved/planet_near_field.on"))
    print("2) 写标记文件（第 7 个位置参数是<基场目录>，不是标记文件自己的路径——写成路径的表现为")
    print("   Directory.Exists 假、退成 analytic_ramp，而 height_source 那格会替我们喊出来）：")
    print("     printf '30 120 1 -1 60 1800 %s pose=probe' > '%s'" % (os.path.abspath(dem), marker))
    print("   pose=probe 是「只探不猜」：把两种轴序口径的离地高度都打进 actor_pose 那行。")
    print("   第一次跑就用 probe，读完 verdict 再改成 pose=flax 或 pose=ecs 跑第二次——")
    print("   判据会盯着\"测出来该用哪档\"与\"这一轮用了哪档\"是不是同一档。")
    print("   然后在编辑器里跑一次 PIE（场景加载后驱动自己开始；不要另起第二个编辑器实例）。")
    print("   角色号：要么在驱动那个 Actor 上显式设 TransferCharacterId，要么等真登录后 NetworkManager 给的号；")
    print("   两者都没有时回执写着 character=0，判据会红在 transfers_sent ——那条红的理由是「没进世界」，")
    print("   不是「换区坏了」，同一行里 caliber_status/session/character 三个数就是用来分这两件事的。")
    print("3) 跑完用本文件判，别用眼睛读：")
    print("     python Tools/planet-session-check.py --receipt %s \\" % os.path.abspath(os.path.join(ROOT, "HundunWorld/Saved/planet_near_field.result.txt")))
    print("         --silo-log <服务端日志文件>          # 不给就是 SKIP silo_gates，汇总里看得见")
    print("   期望：verdict=PASS 且 skipped=0；build_tag 必须是 %s（这一版），否则它就是在拿旧装配的回执。" % tag)
    print("4) 收尾删标记（留着会让下一次启动自己就跑这条自测）：")
    print("     rm -f %s" % os.path.abspath(os.path.join(ROOT, "HundunWorld/Saved/planet_near_field.on")))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--receipt")
    ap.add_argument("--silo-log")
    ap.add_argument("--self-test", action="store_true")
    ap.add_argument("--runbook", nargs="?", const="Data/GEBCO_2026/derived", default=None,
                    help="打印实机验收的输入形状（参数是基场目录，默认 Data/GEBCO_2026/derived）")
    a = ap.parse_args()
    if a.runbook:
        runbook(a.runbook)
        return
    if a.self_test:
        sys.exit(self_test())
    if not a.receipt:
        raise SystemExit("要么 --self-test / --runbook，要么 --receipt <回执文件>")
    text = io.open(a.receipt, encoding="utf-8-sig", errors="replace").read()
    log = io.open(a.silo_log, encoding="utf-8-sig", errors="replace").read() if a.silo_log else None
    sys.exit(render(check_receipt(text, log, expected_build_tag())))


main()
