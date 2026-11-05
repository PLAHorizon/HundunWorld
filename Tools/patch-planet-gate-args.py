import io

p = r"C:/Works/GitHubProjects/HundunWorld/scripts/verify-planet.sh"
s = io.open(p, encoding="utf-8-sig", newline="").read()

# (旧片段, 新片段)：只改 stage 调用的第 3 个实参（最少通过条数）
REPS = [
    ('stage "planet 库全量" "^Passed!" test_proj',
     'stage "planet 库全量" "^Passed!" 300 test_proj'),
    ('stage "PlanetRegion + 契约" "^Passed!" dotnet test',
     'stage "PlanetRegion + 契约" "^Passed!" 100 dotnet test'),
    ('stage "Silo 编译" "Build succeeded" dotnet build',
     'stage "Silo 编译" "Build succeeded" 0 dotnet build'),
    ('stage "客户端脚本" "verdict=PASS" bash',
     'stage "客户端脚本" "verdict=PASS" 0 bash'),
    ('stage "no-orleans 门禁" "^verdict=PASS" bash',
     'stage "no-orleans 门禁" "^verdict=PASS" 0 bash'),
    ('stage "exposure 门禁" "^verdict=PASS" bash',
     'stage "exposure 门禁" "^verdict=PASS" 0 bash'),
    ('stage "manifest 结构一致性" "verdict=PASS" bash',
     'stage "manifest 结构一致性" "verdict=PASS" 0 bash'),
    ('stage "服务端地面通路（真产物）" "GROUND_RESULT PASS" dotnet run',
     'stage "服务端地面通路（真产物）" "GROUND_RESULT PASS" 0 dotnet run'),
]

for old, new in REPS:
    n = s.count(old)
    if n != 1:
        raise SystemExit("锚点命中 %d 次（要恰好 1）：%s" % (n, old[:56]))
    s = s.replace(old, new)

# 全部改完之后自检：每条 stage 调用的第三个实参必须是数字（0 表示这条不查条数）
import re
pattern = re.compile(r'^\s*stage\s+"[^"]*"\s+"[^"]*"\s+(\S+)')
bad = []
for ln in s.splitlines():
    m = pattern.match(ln)
    if m and not m.group(1).isdigit():
        bad.append(ln)
if bad:
    raise SystemExit("还有没带上限的 stage 调用：\n" + "\n".join(bad))
checked = sum(1 for ln in s.splitlines() if pattern.match(ln))
if checked != 8:
    raise SystemExit("stage 调用条数不是 8（实到 %d）：改的东西与预期不符，先别写盘" % checked)

io.open(p, "w", encoding="utf-8", newline="").write(s)
print("八处 stage 调用都已带最少条数；文件其余内容未动")
