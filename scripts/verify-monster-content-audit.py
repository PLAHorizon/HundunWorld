#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把 `MonsterCli audit` 挂进仓内既有的"自动跑的清单"里（scripts/verify-*.py 那一族）。

为什么要单独一个脚本：普查本身已经能把"机制就绪、内容为零"逐格念出来，
但**念给谁听**是另一件事——今天它只在有人手动敲 `audit` 时才响一次，
响完就过去了。放进 `scripts/` 这条清单里，它才变成"跑一次全套检查就会顺带问一遍"的东西。

判据分成两档，刻意**不把"内容为零"当失败**（那是设计文档 §11.2 的 backlog，
生产库里今天确实还是零；一条每填必响的红会把真问题埋成背景噪声）：
  · 硬失败（退出码非 0）：连不上库 / 怪物表不存在（019 没执行）/ 普查 0 只已发布怪 /
    有已发布的怪过不了编辑器自己那套校验器。这四种是"这条检查根本没跑成"或"库里躺着坏数据"。
  · `--require-content`（可选，给排练库或用完后守门）：任何一句"这一格全仓为零"也算失败。

用法：
  python scripts/verify-monster-content-audit.py                    # 默认连 DB_GAME_CONNECTION_STRING 指的那个库
  python scripts/verify-monster-content-audit.py --db Game_MonsterRT # 显式指定库（排练库）
  python scripts/verify-monster-content-audit.py --require-content
"""
import argparse
import json
import os
import subprocess
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CLI = os.path.join(REPO, "Horizon.MonsterCli", "bin", "Debug", "net10.0", "Horizon.MonsterCli.exe")

# 普查里"这一格没内容"那几句的特征串（判据住在 MonsterAuditCensus，别在这里改写成同义句，
# 否则产品侧改了文案这里静默不再命中 ⇒ 这条守门自己变成空转）
ZERO_MARKERS = (
    "全仓为零", "没有一只怪", "这些行全部是死数据", "普查什么都没看到",
)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--db", default=None, help="显式数据库名（默认用环境里的连接串）")
    ap.add_argument("--require-content", action="store_true",
                    help="把「内容为零」也算失败（排练库/配完内容之后用）")
    ap.add_argument("--cli", default=CLI, help="Horizon.MonsterCli.exe 的路径")
    args = ap.parse_args()

    if args.db:
        os.environ["DB_GAME_CONNECTION_STRING"] = (
            "Server=localhost;Database=" + args.db + ";Integrated Security=True;TrustServerCertificate=True")
    if not os.environ.get("DB_GAME_CONNECTION_STRING"):
        print("FAIL: 既没有 --db 也没有 DB_GAME_CONNECTION_STRING ⇒ 不知道该普查哪个库（不猜）")
        return 2
    if not os.path.exists(args.cli):
        print(f"FAIL: 找不到 CLI：{args.cli} ⇒ 先编 Horizon.MonsterCli（没编成就等于没跑）")
        return 2

    # 装配陈旧检查：这条闸门一半靠**源码里的文案**（下面的零内容判据对照）、一半靠**已编好的二进制**的读数。
    # 有人改了 MonsterAuditCensus 的措辞却没重编，两边就会各说一套——那正是"回执来自上一次装配"那一族。
    census_time_path = os.path.join(REPO, "Horizon.MonsterCli", "MonsterAuditCensus.cs")
    if os.path.exists(census_time_path):
        src_mtime = os.path.getmtime(census_time_path)
        bin_mtime = max(os.path.getmtime(args.cli),
                        os.path.getmtime(os.path.join(os.path.dirname(args.cli), "Horizon.MonsterCli.dll")))
        if src_mtime > bin_mtime:
            print("FAIL: Horizon.MonsterCli 比 MonsterAuditCensus.cs 旧 ⇒ 这次跑的是上一次装配的普查，"
                  "读数与判据文本可能不同源。先 dotnet build Horizon.MonsterCli 再跑。")
            return 1

    p = subprocess.run([args.cli, "audit"], capture_output=True)
    raw = p.stdout.decode("utf-8", "replace")
    try:
        receipt = json.loads(raw)
    except Exception:
        # 非 JSON 的 stdout 里最常见的是"表不存在/连不上"，那条本身就是要紧话
        print("FAIL: audit 的回执不是 JSON（前 300 字）：", raw[:300], p.stderr.decode("utf-8", "replace")[:300])
        return 2

    data = receipt.get("data") or {}
    findings = receipt.get("warnings") or []
    published = int(data.get("published") or 0)
    invalid = int(data.get("invalid") or 0)
    where = args.db or os.environ["DB_GAME_CONNECTION_STRING"]

    print(f"库：{where}")
    print(f"已发布 {published} 只、校验不过 {invalid} 只、发现 {len(findings)} 条")
    keys = ("skillMounts", "monstersWithSkills", "skillCapable", "navCapable", "assistCapable",
            "lootBindings", "lootEntries", "monstersWithLoot", "moneyCapable",
            "routeCapable", "corpseCapable", "textCapable", "orphanRows",
            "lootWindowClosedWithDrops", "lootCandidatesOverCap",
            "lootGroupEntries", "lootConditionalEntries",
            "distinctAiProfiles", "distinctFollowTypes", "npcTableReadable")
    print("内容读数：" + "  ".join(f"{k}={data.get(k)}" for k in keys))

    # 降级项：判据本身还能跑，但它读的那张表不存在 ⇒ 读数不成立。
    # 今天唯一一条是 npcTableReadable=False：孤儿判定只能并上怪物表，挂在敌对 NPC 上的合法内容会被误报，
    # 真孤儿反而查不全。所以 orphanRows=0 **不能**当"没有孤儿"的证据——这句必须自己念出来，
    # 并且在 --require-content（"每一格都有内容、每个判据都可信"那一档）里算失败。
    degraded = []
    # 防空转：这一格必须**存在且是布尔**。缺格时 `not None` 恒真 ⇒ "降级"会永远成立、
    # 而表补上之后也没人发现这一格根本没在报（读数永远停在"降级 1 项"）。
    if not isinstance(data.get("npcTableReadable"), bool):
        print("FAIL: 回执里没有 npcTableReadable 这个布尔格 ⇒ 孤儿判据的降级状态无从判断（不猜）")
        return 2
    if not data["npcTableReadable"]:
        degraded.append("NPC 模板表读不到（021/022 未应用？）⇒ orphanRows 判据降级："
                        "挂在敌对 NPC 上的合法内容会被误报成孤儿，真孤儿也查不全")
    print(f"降级 {len(degraded)} 项" + ("：" if degraded else ""))
    for d in degraded:
        print("  ! " + d)
    for f in findings:
        print("  · " + f)

    hard_fail = []
    if not receipt.get("ok"):
        hard_fail.append("audit 自己判失败（见上面那句 message 与 findings）")
    if published == 0:
        hard_fail.append("普查什么都没看到：0 只已发布怪 ⇒ 连错库/019 没执行/发布状态没写，都不该被读成\"内容没问题\"")
    if invalid > 0:
        hard_fail.append(f"{invalid} 只已发布的怪过不了编辑器自己那套校验器（库里躺着坏数据）")
    # 两格"配了却拿不到 / 多出来的被丢掉"：与"内容为零"不同，它们在**任何档位**都算失败——
    # 窗口 0 是"内容配了、玩家永远拿不到"，候选超上限是"跑一次掉 25 件、实际只给 18 件"。
    # 缺格（键不在回执里）也判失败：那说明跑的是上一版装配，这两条闸门等于没跑。
    for key, label in (("lootWindowClosedWithDrops", "有掉落但可拾取窗口为 0"), ("lootCandidatesOverCap", "普通件候选超过一具尸体 18 件上限")):
        if key not in data:
            hard_fail.append(f"回执里没有 {key} 这一格 ⇒ 这两条判据没跑成（先重编 Horizon.MonsterCli）")
            continue
        n = int(data.get(key) or 0)
        if n > 0:
            hard_fail.append(f"{n} 只怪{label}（见回执里的 findings，运行期会取不到或被丢弃）")
    if hard_fail:
        print("FAIL:")
        for h in hard_fail:
            print("  - " + h)
        return 1

    if args.require_content:
        # 空转防护（这条比判据本身更容易坏）：这把这个开关靠的是"按中文字串命中"，
        # 而文案住在 MonsterAuditCensus.cs——有人改一句措辞，这里就会**永远一句都不命中**、
        # 于是把"内容还是零"报成 PASS。⇒ 跑绿之前先证明这几串在判据源码里真的存在。
        census_src_path = os.path.join(REPO, "Horizon.MonsterCli", "MonsterAuditCensus.cs")
        if not os.path.exists(census_src_path):
            print("FAIL: 找不到 MonsterAuditCensus.cs ⇒ 命中的那几串没处对照，这个开关不可信")
            return 2
        census_src = open(census_src_path, encoding="utf-8").read()
        drifted = [m for m in ZERO_MARKERS if m not in census_src]
        if drifted:
            print("FAIL: 这些「零内容」句在判据源码里已经找不到了（文案漂了 ⇒ 命中集合会静默变空）：", drifted)
            return 1
        zero = [f for f in findings if any(m in f for m in ZERO_MARKERS)]
        if zero:
            print(f"FAIL(--require-content)：还有 {len(zero)} 句「这一格没内容」")
            for f in zero:
                print("  - " + f)
            return 1
        # 内容形状：条件件（AC needs_quest）一件都没有 ⇒ 那条链在真内容里没有触发者。
        # 只在严格档算失败（它不坏，只是"这半边魔兽口径还没被内容用到"）。
        if "lootConditionalEntries" not in data:
            print("FAIL(--require-content)：回执里没有 lootConditionalEntries ⇒ 要么跑的是上一版装配，要么这条判据没跑成")
            return 1
        if int(data.get("lootConditionalEntries") or 0) == 0:
            print("FAIL(--require-content)：掉落里没有一件按条件出账（needs_quest 一族）⇒ "
                  "LootConditional → 任务持有者判定那条链在真内容里没有触发者")
            return 1
        if degraded:
            # 严格档的承诺是"每个判据都可信"。判据降级时它给出的"没问题"是没有覆盖面的，
            # 所以这一档必须红——红的原因指名到那张缺的表，别让人以为是怪数据坏了。
            print(f"FAIL(--require-content)：{len(degraded)} 项判据降级，它的结论不可信")
            for d in degraded:
                print("  - " + d)
            return 1
        print(f"PASS：{published} 只已发布怪，逐格内容读数见上；"
              f"四句零内容判据都在源码里、且一句都没命中（= 每一格都有内容）")
        return 0

    extra = f"；**{len(degraded)} 项判据降级**（读数当不得真，见上面的「!」）" if degraded else ""
    print(f"PASS（默认档）：{published} 只已发布怪全部过校验器；"
          f"零内容那几句仍会打印但不判失败（那是 §11.2 的 backlog，守它请用 --require-content）{extra}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
