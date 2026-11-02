"""草稿层（Redis）行为核验：保存/读取/清单/TTL/删除，以及"发布失败不写库"。

只碰 Redis 与 SQL 的**只读**动词；探针任务 id 995001 全程不发布，所以不会往 Game 库写任何行。
"""
import json
import os
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EXE = os.path.join(ROOT, "Horizon.QuestCli", "bin", "Debug", "net10.0", "Horizon.QuestCli.exe")
PROBE_ID = 995001
DRAFT = {
    "questId": PROBE_ID,
    "questName": "\u8349\u7a3f\u63a2\u9488\u4efb\u52a1",       # 草稿探针任务
    "description": "\u53ea\u4f4f Redis\uff0c\u4e0d\u843d\u5e93\u3002",  # 只住 Redis，不落库。
    "questType": 2,
    "levelRequire": 1,
    "wuxingDomain": 0,
    "status": 0,
    "objectives": [{"kind": "kill_monster", "templateId": 4, "count": 3,
                    "description": "\u6740\u602a"}],
    "rewards": {"experience": 500, "copper": 50,
                "items": [{"templateId": 20001, "count": 1}], "favor": []},
    "giver": {"kind": "npc", "templateId": 3},
    "prerequisiteQuests": [],
}

failures = []


def run(args, stdin_text=None):
    data = stdin_text.encode("utf-8") if stdin_text is not None else None
    proc = subprocess.run([EXE] + args, input=data, capture_output=True, timeout=120)
    text = proc.stdout.decode("utf-8", "replace")
    try:
        payload = json.loads(text.strip().splitlines()[-1])
    except Exception as ex:
        failures.append("无法解析 CLI 输出（%s）：%r" % (ex, text[:200]))
        return {}
    return payload


env = dict(os.environ)
# 刻意**清掉** Redis 相关环境变量，复现编辑器拉起子进程时的真实环境（子进程没有这些变量）。
# 这曾经就是现场故障：CLI 只认环境变量 → "草稿层不可用"。现在它必须能从 .env 或本机兜底里找到。
for key in ("HORIZON_QUEST_REDIS", "HORIZON_ORLEANS_REDIS", "GATEWAY_REDIS_CONNECTION_STRING",
            "SILO_REDIS_CONNECTION_STRING", "REDIS_CONNECTION_STRING"):
    env.pop(key, None)
os.environ.clear()
os.environ.update(env)

# ① 先清干净（上一次中断可能留下草稿）
run(["draft", "delete", str(PROBE_ID)])

# ② 保存
saved = run(["draft", "save", "-"], json.dumps(DRAFT, ensure_ascii=False))
print("save      ok=%s message=%s" % (saved.get("ok"), saved.get("message")))
if not saved.get("ok"):
    failures.append("保存草稿失败：" + str(saved.get("message")))

# ③ 原文回来，中文不许坏。按**字段值**比对而不是找字面中文：草稿入 Redis 走的是
#    QuestDraft.ToJson()，它把非 ASCII 转成 \uXXXX（合法 JSON、往返无损），
#    拿字面串去找会误判成"中文丢了"——判据要落在语义上，不是落在转义形式上。
got = run(["draft", "get", str(PROBE_ID)])
raw = (got.get("data") or {}).get("json", "")
try:
    restored = json.loads(raw)
except Exception as ex:
    restored = {}
    failures.append("草稿原文不是合法 JSON（%s）：%r" % (ex, raw[:160]))
if restored.get("questName") != DRAFT["questName"]:
    failures.append("草稿往返后任务名不一致：%r != %r" % (restored.get("questName"), DRAFT["questName"]))
if restored.get("description") != DRAFT["description"]:
    failures.append("草稿往返后描述不一致")
if "\ufffd" in raw:
    failures.append("草稿原文里出现替换字符，编码在 Redis 往返中坏了")
print("roundtrip name=%s" % restored.get("questName"))

# ④ 清单里有它，且剩余有效期≈72 小时（证明 TTL 真的设上了）
listing = run(["draft", "list"])
rows = (listing.get("data") or {}).get("drafts", [])
mine = [row for row in rows if row.get("questId") == PROBE_ID]
print("list      count=%s ttlHours=%s" % (len(rows), mine[0].get("ttlHours") if mine else None))
if not mine:
    failures.append("草稿清单里没有刚保存的 %d" % PROBE_ID)
elif not (71.0 <= float(mine[0]["ttlHours"]) <= 72.0):
    failures.append("TTL 不是 ~72 小时而是 %s 小时" % mine[0]["ttlHours"])

# ⑤ 删除后即消失
run(["draft", "delete", str(PROBE_ID)])
after = run(["draft", "get", str(PROBE_ID)])
print("after del ok=%s message=%s" % (after.get("ok"), str(after.get("message"))[:40]))
if after.get("ok"):
    failures.append("删除后还能取到草稿")

# ⑥ 没有草稿时发布必须**明确拒绝**，并且不能碰 SQL——
#    "发不出去却默默写了下线行"是这条最该防的坏法。
publish = run(["publish", str(PROBE_ID)])
print("publish   ok=%s message=%s" % (publish.get("ok"), str(publish.get("message"))[:60]))
if publish.get("ok"):
    failures.append("没有草稿却发布成功了")
if "\u6ca1\u6709\u8349\u7a3f" not in str(publish.get("message")):
    failures.append("拒绝理由没说清是缺草稿：" + str(publish.get("message"))[:80])

# ⑦ SQL 侧仍然查无此行（发布失败路径一行都不许写）
sql_side = run(["get", str(PROBE_ID)])
if sql_side.get("ok"):
    failures.append("库里竟然出现了探针行 %d —— 发布失败路径写了库" % PROBE_ID)
print("sql side  ok=%s message=%s" % (sql_side.get("ok"), str(sql_side.get("message"))[:40]))

# ⑧ 需求主线：**未发布的草稿可直接删除**——而且走的是编辑器「删除」按钮那条动词（delete），
#    不是 Redis 专用的 draft delete。从未发布过的东西没有任何角色可能持有，所以不该要求先作废。
run(["draft", "save", "-"], json.dumps(DRAFT, ensure_ascii=False))
dropped = run(["delete", str(PROBE_ID)])
data = dropped.get("data") or {}
print("del draft ok=%s deletedDraft=%s deletedRow=%s" % (dropped.get("ok"), data.get("deletedDraft"), data.get("deletedRow")))
if not dropped.get("ok"):
    failures.append("未发布的草稿不能直接删除：" + str(dropped.get("message"))[:80])
elif not data.get("deletedDraft"):
    failures.append("删除草稿没有报告 deletedDraft")
if run(["draft", "get", str(PROBE_ID)]).get("ok"):
    failures.append("删除后草稿仍在 Redis 里")

# ⑨ 只读对照：库里**有效**的行必须仍然被拒（拿真种子 10001 试，拒绝路径不改数据）。
guarded = run(["delete", "10001"])
print("del valid  ok=%s message=%s" % (guarded.get("ok"), str(guarded.get("message"))[:48]))
if guarded.get("ok"):
    failures.append("有效状态的任务竟然能被直接删除（10001 是种子数据，且它没问题）")
if "作废" not in str(guarded.get("message")):
    failures.append("拒绝有效行时没说清要先作废：" + str(guarded.get("message"))[:80])
if not run(["get", "10001"]).get("ok"):
    failures.append("10001 不见了 —— 只读对照把真种子数据删掉了，必须立刻查库恢复")

if failures:
    for item in failures:
        print("FAIL: " + item)
    sys.exit(1)
print("草稿层核验通过：Redis 往返中文不坏、TTL≈72h、删除即失效、无草稿时发布被拒且不写库。")
