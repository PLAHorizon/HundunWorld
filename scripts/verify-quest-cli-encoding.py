"""一次性核验：CLI 的 stdin 读取必须容忍 BOM、UTF-8 输出必须无 BOM 且中文不坏。

做这条是因为编辑器现场报了 `'0xEF' is an invalid start of a value`——
编辑器的 stdin 写了 UTF-8 BOM，而 CLI 侧因 `Console.InputEncoding` 在重定向句柄上抛异常
被吞掉后退回控制台码页（936），BOM 被解成 'ï'，STJ 报出的正是 0xEF。
只用 ASCII 源码（中文走 \\uXXXX）避免 shell 编码干扰。
"""
import json
import os
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EXE = os.path.join(ROOT, "Horizon.QuestCli", "bin", "Debug", "net10.0", "Horizon.QuestCli.exe")
TMP = os.path.join(ROOT, ".qcli_probe")

DRAFT = {
    "questId": 10000,
    "questName": "\u4e2d\u6587\u4efb\u52a1\u540d",
    "description": "\u7531\u63a2\u9488\u811a\u672c\u751f\u6210\uff0c\u4e0d\u843d\u5e93\u3002",
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


def run(args, payload_path=None, bom=False):
    data = None
    if payload_path is not None:
        body = json.dumps(DRAFT, ensure_ascii=False).encode("utf-8")
        with open(payload_path, "wb") as handle:
            handle.write((b"\xef\xbb\xbf" if bom else b"") + body)
        with open(payload_path, "rb") as handle:
            data = handle.read()
    proc = subprocess.run([EXE] + args, input=data, capture_output=True, timeout=120)
    return proc


def check(label, proc, expect_bom_input, want_zh=True):
    raw = proc.stdout
    text = raw.decode("utf-8", "replace")
    if raw.startswith(b"\xef\xbb\xbf"):
        failures.append(label + "：stdout 带 BOM，编辑器的 `{` 行匹配会一行都命中不了")
    try:
        payload = json.loads(text.strip().splitlines()[-1])
    except Exception as ex:
        failures.append(label + "：stdout 不是 JSON（%s）：%r" % (ex, text[:200]))
        return None
    message = str(payload.get("message", ""))
    if "\u89e3\u6790\u5931\u8d25" in message:      # 解析失败
        failures.append(label + "：仍然报草稿解析失败 -> " + message)
    if want_zh and "\ufffd" in message:
        failures.append(label + "：输出里有替换字符，中文在管道里坏了 -> " + message)
    print("%-28s ok=%-5s exit=%d message=%s" % (label, payload.get("ok"), proc.returncode, message[:70]))
    return payload


os.makedirs(TMP, exist_ok=True)
if not os.path.isfile(EXE):
    print("FAIL: 找不到 CLI：" + EXE)
    sys.exit(1)

# ① 不带 stdin（编辑器"刷新目录/发布"这类按钮的形状）：曾经在编辑器侧直接抛
#    「StandardInputEncoding is only supported when standard input is redirected.」
check("no-stdin: help", run(["help"]), False, want_zh=False)

# ② 带 BOM 的 UTF-8 草稿（编辑器现场的形状）
check("stdin+BOM: validate -", run(["validate", "-"], os.path.join(TMP, "bom.json"), bom=True), True)

# ③ 不带 BOM 的 UTF-8 草稿
check("stdin plain: validate -", run(["validate", "-"], os.path.join(TMP, "plain.json"), bom=False), True)

# ④ 关于"反向对照"的一句实话：本想用 GBK 草稿来证明探针能发现编码坏掉，但**这条对照无效**——
#    JSON 的结构字符全是 ASCII，中文坏成 U+FFFD 之后结构照样能解析，而 validate 的回执里
#    不含草稿正文，所以从 stdout 上看不出区别（实测：GBK 草稿判红=False）。
#    真正的"红"要靠变异对照：把 Program.cs 的 stdin 解码器临时改成 936，上面第 ② 条必须报解析失败/替换字符。
#    这条留作记录与提醒：判"编码没坏"不能只看回执文本，得看有没有把内容回显出来的通道。
gbk_path = os.path.join(TMP, "gbk.json")
with open(gbk_path, "wb") as handle:
    handle.write(json.dumps(DRAFT, ensure_ascii=False).encode("gbk"))
proc = subprocess.run([EXE, "validate", "-"], input=open(gbk_path, "rb").read(),
                      capture_output=True, timeout=120)
text = proc.stdout.decode("utf-8", "replace")
print("%-28s 回执里可见替换字符=%s（GBK 草稿；此条不作为判据）"
      % ("note: GBK payload", "\ufffd" in text))

if failures:
    for item in failures:
        print("FAIL: " + item)
    sys.exit(1)
print("CLI stdin/stdout 编解码核验通过：BOM 容忍、输出无 BOM、中文未坏。")
