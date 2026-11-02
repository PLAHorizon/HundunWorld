#!/bin/bash
# 客户端脚本离线编译自检（不写 Program Files，不动在跑的进程）。
#
# 为什么要它：Flax.Build 解析脚本程序集时**优先用引擎 Binaries\Tools 里那份副本**，
# 改了 Horizon.Game.Message 之后如果没提权覆盖那份副本，编辑器里编译会报
# CS0246/CS0117「类型不存在」——而那个类型在源码里确实存在。这条通路直接拿仓库刚编出来的
# 新 dll 当第一顺位引用，把"代码本身对不对"和"Tools 副本新旧"这两个问题分开答。
#
# 用法：bash scripts/verify-client-scripts.sh [id]
#   判据：末行 errors=0；非 0 时列出的才是真代码错。
set -u
ID="${1:-main}"
ROOT="C:/Works/GitHubProjects/HundunWorld"
CLIENT="$ROOT/HundunWorld"
RESP="$CLIENT/Cache/Intermediate/GameEditorTarget/Windows/x64/Development/Game.CSharp.response"
CSC="C:/Program Files/dotnet/sdk/10.0.401/Roslyn/bincore/csc.dll"
OUT="$CLIENT/Cache/_verify_client_$ID"
mkdir -p "$OUT"

if [ ! -f "$RESP" ]; then
  echo "找不到响应文件：$RESP"
  echo "先跑一次 Flax.Build -genproject（或让编辑器成功编一次脚本）才会有它。"
  exit 9
fi

RSP="$OUT/Game.rsp"

# 仓库里新鲜编出的共享程序集（按默认 net10.0 布局找；被旁路 OutputPath 挪走时两种都试）。
FRESH_REFS=""
# 客户端实际编译面：Message + ECS/ECS.Arch + Sim + Sim.Contracts + World.Contracts + Share.Client
# Horizon.Game.Sim 必须在列：客户端直接消费它的确定性内核（如怪物让分色 MonsterDifficultyCore），
# 少了它就会拿到引擎 Tools 里那份旧副本，报出"新类型不存在"这种与代码无关的假错。
for asm in Horizon.Game.Message Horizon.Game.ECS Horizon.Game.ECS.Arch Horizon.Game.Sim \
           Horizon.Game.Sim.Contracts Horizon.Game.World.Contracts Horizon.Share.Client; do
  for layout in "bin/Debug/net10.0" "bin/Debug" "bin/BuildCheck" "bin/Verify"; do
    candidate="$ROOT/$asm/$layout/$asm.dll"
    if [ -f "$candidate" ]; then
      FRESH_REFS="$FRESH_REFS/r:\"$candidate\"
"
      break
    fi
  done
done

{
  printf '%s' "$FRESH_REFS"
  # 原始响应里同名程序集的那几条 /r: 必须**剔除**：引擎会把 Flax\Binaries\Tools 里的陈旧副本也引进来，
  # 而 csc 对同名程序集只认其中一个，认到的若是旧副本就会报 CS0246「新类型不存在」——
  # 明明新鲜 dll 里就有。留自己列在最前的那份，判据才是"代码对不对"，不是"副本新旧"。
  # 响应文件首行可能带 UTF-8 BOM（Flax 重新生成后就这样），BOM 会把 `/nologo` 变成
  # "一个叫 <BOM>/nologo 的源文件"，csc 报 CS2001 而跟代码无关。先剥掉再过滤。
  sed '1s/^\xEF\xBB\xBF//' "$RESP" \
    | grep -vE '^".*\.cs"$|^/out:|^/doc:|^/generatedfilesout:' \
    | grep -vE '(^|/)(r|reference):"[^"]*(Horizon\.Game\.Message|Horizon\.Game\.ECS|Horizon\.Game\.ECS\.Arch|Horizon\.Game\.Sim|Horizon\.Game\.Sim\.Contracts|Horizon\.Game\.World|Horizon\.Game\.World\.Contracts|Horizon\.Share\.Client)\.dll"'
  echo "/langversion:14.0"
  echo "/doc:\"$OUT/out.xml\""
  echo "/out:\"$OUT/out.dll\""
  echo "/generatedfilesout:\"$OUT\""
  find "$CLIENT/Source/Game" -name "*.cs" ! -name "*.Gen.cs" ! -name "*.Build.cs" | sed 's|^|\"|; s|$|\"|'
} > "$RSP"

dotnet exec "$CSC" "@$RSP" > "$OUT/csc.log" 2>&1
code=$?
errors=$(grep -c "error CS" "$OUT/csc.log")
echo "exit=$code errors=$errors sources=$(grep -cE '^\".*\.cs\"$' "$RSP")"
if [ "$errors" -gt 0 ]; then
  grep "error CS" "$OUT/csc.log" | sed "s|$CLIENT/||" | sort -u | head -30
fi
