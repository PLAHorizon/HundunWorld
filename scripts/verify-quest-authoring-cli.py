"""用真的 CLI 走一遍"编辑器会发出去的草稿"，看服务端怎么判（只读：validate 不写库）。

三个案例各自钉住一条今天变了口径的东西：
A) talk_npc 目标 + 库里真有的装备奖励 → 应通过（§11 把对话类目标的事件源接上了），且带一条 Q039 Info
B) talk_npc 引用未登记的 NPC → 应被 Q027 拒（对话有源了不代表可以随便指一个不存在的对象）
C) 奖励指向材料新号段 700001 → 现在应被 Q034 拒（库里还没有材料行，这条正是 015 要补的）
"""
import json
import subprocess
import sys
import os
import tempfile

def _resolve_materials_in_db():
    """材料号段是否已经进物品模板表——**自己查库**，不要靠人记得设环境变量。

    原先这是一行 `os.environ.get(...) == "1"`，而注释写着"不用靠人记得去改断言"——那话是错的：
    015 把 41 件材料入库之后，没人再设这个变量，C 组就一路按"应被 Q034 拒"判红，
    报的是"产品错了"，实际过期的是这条期望值。现在默认查库，环境变量只留作显式覆盖/离线逃生口。
    """
    import subprocess

    override = (os.environ.get("QUEST_MATERIALS_IN_DB") or "").strip()
    if override in ("0", "1"):
        return override == "1", "环境变量覆盖"

    sql = ("SET NOCOUNT ON; SELECT CASE WHEN EXISTS "
           "(SELECT 1 FROM dbo.Game_HunduShijie_ItemTemplate WHERE item_id = 700001) "
           "THEN 1 ELSE 0 END;")
    try:
        proc = subprocess.run(["sqlcmd", "-S", ".", "-d", "Game", "-E", "-C", "-W", "-Q", sql],
                              capture_output=True, timeout=30)
    except Exception as ex:
        print("FAIL: 读不到物品模板表（%s），而 C 组判据依赖它。"
              "确认 sqlcmd 可用，或显式设 QUEST_MATERIALS_IN_DB=0/1。" % ex)
        sys.exit(2)

    # sqlcmd 在 GBK 控制台下可能带 NUL，且会回显列名行——只认输出里的第一个 0/1 数字。
    raw = proc.stdout.replace(b"\x00", b"").decode("utf-8", "replace")
    for token in raw.replace("\r", "\n").split("\n"):
        token = token.strip()
        if token in ("0", "1"):
            return token == "1", "实测库内 item_id=700001"
    print("FAIL: sqlcmd 输出里没有可解析的 0/1（rc=%s）：%r" % (proc.returncode, raw[:200]))
    sys.exit(2)


MATERIALS_IN_DB, MATERIALS_IN_DB_SOURCE = _resolve_materials_in_db()

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def draft(quest_id, objectives, reward_items, npc_ref=3):
    return {
        "questId": quest_id,
        "questName": "核验草稿",
        "description": "由 scripts/ 下的核验流程生成，不落库。",
        "questType": 1,
        "levelRequire": 1,
        "wuxingDomain": 0,
        "repeatLimit": 0,
        "publishState": 1,
        "isValid": True,
        "contentVersion": 1,
        "giver": {"kind": "npc", "templateId": 1, "zoneId": 0, "spawnPointId": 0},
        "turnIn": {"kind": "npc", "templateId": 1, "zoneId": 0, "spawnPointId": 0},
        "prerequisiteQuests": [],
        "objectives": [
            {"kind": kind, "templateId": tid, "count": count,
             "description": desc, "mandatory": True}
            for (kind, tid, count, desc) in objectives
        ],
        "rewards": {
            "exp": 100,
            "gold": 0,
            "items": [{"templateId": tid, "count": 1} for tid in reward_items],
            "favor": [],
        },
    }


CASES = [
    ("A talk_npc + 库里真有的装备(20001)", draft(96001, [("talk_npc", 3, 1, "与守门人说话")], [20001]), "pass", {"Q039"}),
    ("B talk_npc 指向未登记 NPC(999)", draft(96002, [("talk_npc", 999, 1, "不存在的人")], [20001]), "reject", {"Q027"}),
    # C 的期望值随"材料是否已入物品模板表"翻转：015 没入库时它必须被 Q034 拒（奖励发不出的东西
    # 不该能进模板），015 入库后置 QUEST_MATERIALS_IN_DB=1，它必须变成通过——
    # 这条就是"材料进奖励域"这件事的自动对账点，不用靠人记得去改断言。
    ("C 奖励指向材料新号段(700001)", draft(96003, [("kill_monster", 4, 1, "杀 4 号怪")], [700001]),
     "pass" if MATERIALS_IN_DB else "reject",
     {"Q034"} if not MATERIALS_IN_DB else set()),
]


def find_dll():
    for cand in (
        os.path.join(ROOT, "Horizon.QuestCli", "bin", "Verify", "Debug", "net10.0", "Horizon.QuestCli.dll"),
        os.path.join(ROOT, "Horizon.QuestCli", "bin", "Debug", "net10.0", "Horizon.QuestCli.dll"),
    ):
        if os.path.exists(cand):
            return cand
    return None


def main():
    dll = find_dll()
    if dll is None:
        print("先构建：dotnet build Horizon.QuestCli -p:BaseOutputPath=bin/Verify/")
        return 2

    failures = []
    for name, body, expectation, expected_codes in CASES:
        # 走文件而不是管道：CLI 读 stdin 用的是控制台码页，中文草稿会被读坏成 JSON 解析错误；
        # File.ReadAllText 默认 UTF-8，才是编辑器（插件写临时文件再调用 CLI）实际走的那条路。
        with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False, encoding="utf-8") as handle:
            json.dump(body, handle, ensure_ascii=False)
            draft_path = handle.name
        try:
            proc = subprocess.run(
                ["dotnet", dll, "validate", draft_path],
                cwd=ROOT, capture_output=True, text=True,
                encoding="utf-8", errors="replace", timeout=300)
        finally:
            os.unlink(draft_path)
        line = [l for l in proc.stdout.splitlines() if l.startswith("{")]
        if not line:
            failures.append("%s：CLI 没输出 JSON。stderr=%s" % (name, proc.stderr[-200:]))
            continue
        payload = json.loads(line[-1])
        data = payload.get("data")
        if data is None:
            print("%s -> 信封无 data：%s" % (name, json.dumps(payload, ensure_ascii=False)[:300]))
            failures.append(name + "：CLI 返回缺少 data")
            continue
        issues = data.get("issues", []) or []
        codes = set(i["code"] for i in issues)
        errors = [i for i in issues if i["level"] == "Error"]
        ok = payload.get("ok")

        print("%s -> ok=%s errors=%d codes=%s" % (name, ok, len(errors), sorted(codes)))
        for issue in issues:
            print("    %-6s %-7s %s | %s" % (issue["code"], issue["level"], issue["path"], issue["message"][:70]))

        if expectation == "pass":
            if not ok:
                failures.append("%s 应该通过，实际有错误：%s" % (name, [e["message"] for e in errors]))
            missing = expected_codes - codes
            if missing:
                failures.append("%s 缺少预期提示 %s" % (name, sorted(missing)))
        else:
            if ok:
                failures.append("%s 应该被拒，实际通过了" % name)
            missing = expected_codes - codes
            if missing:
                failures.append("%s 缺少预期错误码 %s（实际 %s）" % (name, sorted(missing), sorted(codes)))

    # D) 管道 + 中文列宽判据：这条专测"编辑器 → CLI 走 stdin"那条路（案例 A~C 走的是临时文件）。
    # 名字正好 30 个汉字，列宽是 nvarchar(50)：解码正确就不会触发 Q003；
    # 一旦 stdin 没按 UTF-8 解（回到控制台旧码页），30 字会被读成近百个乱码字符，Q003 立刻冒出来。
    stdin_name = "验" * 30
    stdin_case = draft(96004, [("kill_monster", 4, 1, "杀 4 号怪")], [20001])
    stdin_case["questName"] = stdin_name
    proc = subprocess.run(
        ["dotnet", dll, "validate", "-"],
        cwd=ROOT, input=json.dumps(stdin_case, ensure_ascii=False),
        capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=300)
    line = [l for l in proc.stdout.splitlines() if l.startswith("{")]
    if not line:
        failures.append("D 管道中文：CLI 没输出 JSON。stderr=%s" % proc.stderr[-200:])
    else:
        payload = json.loads(line[-1])
        data = payload.get("data")
        codes = set(i["code"] for i in (data or {}).get("issues", [])) if data else set()
        print("D 管道中文(30 字名) -> ok=%s codes=%s" % (payload.get("ok"), sorted(codes)))
        if data is None:
            failures.append("D 管道中文：草稿没解析成功（stdin 编码没钉在 UTF-8？）：%s"
                            % payload.get("message", "")[:120])
        elif "Q003" in codes:
            failures.append("D 管道中文：30 个汉字被判成超过 50 字 —— stdin 按旧码页解码，中文正在被读坏")
        elif not payload.get("ok"):
            failures.append("D 管道中文：不该有错误却返回失败：%s" % sorted(codes))

    if failures:
        for f in failures:
            print("FAIL: " + f)
        return 1
    print("CLI 实况核验通过：对话类目标已可入库，未登记对象仍被拒；材料段判定=%s（判据来源：%s）"
          % ("通过" if MATERIALS_IN_DB else "拒(Q034)", MATERIALS_IN_DB_SOURCE))
    return 0


if __name__ == "__main__":
    sys.exit(main())
