#!/bin/bash
# 在**干净检出**上编译+跑判据：仓库里的提交能不能自己站起来。
#
# 为什么需要它：ec42509 那笔提交漏了 4 个消息注册文件（MessageType/MessageUnion/生成器/特性），
# 而当时的工作区里那几份改动还在（未提交），所以本地一切绿；**提交出去的树是编不过的**。
# 这类错只有"从 HEAD 检出一棵新树再编"才看得见——本地工作区再干净也看不见，
# 因为它测的是"我盘上有什么"，不是"仓库里有什么"。
#
# 用法：bash scripts/verify-planet-clean-checkout.sh [commit]
#   默认 HEAD；给一个提交号可以复现"某一版提交到底能不能编译"（本轮就用它证了 ec42509 编不过）。
# 判据：末行 verdict=PASS（planet 库判据全绿 + 网关星球判据全绿 + Silo 编译成功）。
# 说明：客户端脚本装配那一段依赖 Cache/ 里的 .response（生成物、不入库），故不在此脚本内；
#       真产物两段依赖 Data/GEBCO_2026（大体量二进制、不入库），同理。
set -u
ROOT="C:/Works/GitHubProjects/HundunWorld"
COMMIT="${1:-HEAD}"
export DOTNET_CLI_UI_LANGUAGE=en
STAMP=$(date +%s)
WT="C:/Works/GitHubProjects/_cleancheck_${STAMP}"
FAILED=0

cleanup() { git -C "$ROOT" worktree remove --force "$WT" >/dev/null 2>&1; }
trap cleanup EXIT

echo "=== 检出 $COMMIT 到 $WT ==="
git -C "$ROOT" worktree add --detach "$WT" "$COMMIT" >/dev/null 2>&1 || { echo "verdict=FAIL reason=worktree-add"; exit 2; }
echo "树里的 HEAD：$(git -C "$WT" log --oneline -1)"

run() {           # run <名字> <最少通过条数(0=不查)> <命令...>
  local name="$1" min="$2"; shift 2
  local out code got
  out=$("$@" 2>&1); code=$?
  got=$(printf '%s' "$out" | sed -n 's/.*Passed:[[:space:]]*\([0-9]*\).*/\1/p' | tail -1)
  [ -z "$got" ] && got=0
  if [ "$code" -ne 0 ]; then
    echo "FAIL  $name (exit=$code)"
    printf '%s\n' "$out" | grep -E "error (CS|MSB[0-9]+)|Failed!|\[FAIL\]" | sort -u | head -8 | sed 's/^/        /'
    FAILED=$((FAILED + 1))
  elif [ "$min" -gt 0 ] && [ "$got" -lt "$min" ]; then
    echo "FAIL  $name（只过了 $got 条，少于 $min）"
    FAILED=$((FAILED + 1))
  else
    if [ "$min" -gt 0 ]; then echo "ok    $name（passed=$got）"; else echo "ok    $name"; fi
  fi
}

cd "$WT" || { echo "verdict=FAIL reason=cd"; exit 3; }

# 1) 库判据（先编后测：测试工程会把 Horizon.Planet 当工程引用带起来）
run "planet 库判据" 300 dotnet test Horizon.Planet.Tests/Horizon.Planet.Tests.csproj -v:q --nologo

# 2) 网关里的星球全家（filter 与主闸门保持一致）
run "网关星球判据" 148 dotnet test Horizon.Game.Gateway.Tests/Horizon.Game.Gateway.Tests.csproj -v:q --nologo \
  --filter "FullyQualifiedName~PlanetRegion|FullyQualifiedName~PlanetActorPose|FullyQualifiedName~PlanetReceipt|FullyQualifiedName~PlanetSurfaceMotion|FullyQualifiedName~UnionRegistrationParity|FullyQualifiedName~PlanetWire"

# 3) Silo 编译（服务端装配面：消息注册、grain、DI 都在这条链上）
run "Silo 编译" 0 dotnet build Horizon.Orleans.Silo/Horizon.Orleans.Silo.csproj -v:q --nologo

echo
if [ "$FAILED" -eq 0 ]; then
  echo "failed=0"
  echo "verdict=PASS"
  exit 0
fi
echo "failed=$FAILED"
echo "verdict=FAIL"
exit 1
