#!/bin/bash
# 星球（10× 地球）这条链的一站式回归闸门。
#
# 为什么要它：这些判据此前每次都要手拼一遍，拼错一次的后果已经吃过两次——
#   ① 少了"按默认输出重建共享库"这一段，客户端编译会拿着陈旧的 bin/Debug 报"新类型不存在"
#      （§13.59 环境结论 ①；用 -p:BaseOutputPath 验证时最容易踩）；
#   ② 少跑一段就"全绿"，而那一端其实没被这次改动碰过。
# 所以这里把顺序钉死，每段单独报 verdict，最后汇总。任何一段红 ⇒ 退出码非 0。
#
# 用法：bash scripts/verify-planet.sh            # 全跑（含真实产物那两段，数据不在盘上会念 SKIP）
#      SKIP_DATA=1 bash scripts/verify-planet.sh  # 连真实产物两段也不跑（只要快）
#      bash scripts/verify-planet-clean-checkout.sh [commit]  # 另一件事：在干净检出上编一次
#          （本脚本证明"这盘上的东西是对的"，那条证明"仓库里提交的东西能自己站起来"；见 §13.71）
# 依赖：dotnet 10 SDK（仓库根有 global.json，从根跑）、bash、真产物在 Data/GEBCO_2026/derived（可选）
set -u
cd "C:/Works/GitHubProjects/HundunWorld" || exit 9
export DOTNET_CLI_UI_LANGUAGE=en
DEM="Data/GEBCO_2026/derived"
RESULTS=()
FAILED=0
SKIPPED=0

note() { RESULTS+=("$1"); echo "$1"; }

# 每段的判据都是"看到那一行成功标记"，不是"退出码 0"：退出码 0 而日志里没有产物行，就是那条绿从没跑过
# （实测：`dotnet test` 的 filter 一个都没匹配上时它照样退 0）。
# 只认标记还不够——`Passed!  - Failed: 0, Passed: 0` 也是那一行的形状，所以带 min_passed 的段
# 还要数字<em>大于等于</em>它，否则"跑到零条判据"就是最安静的一种假绿。
stage() {           # stage <名字> <期望成功标记> <最少通过条数(0=不查)> <命令...>
  local name="$1" needle="$2" min_passed="$3"; shift 3
  local out code got
  out=$("$@" 2>&1); code=$?
  got=$(printf '%s' "$out" | sed -n 's/.*Passed:[[:space:]]*\([0-9]*\).*/\1/p' | tail -1)
  [ -z "$got" ] && got=0
  if [ "$code" -ne 0 ] || ! printf '%s' "$out" | grep -q "$needle"; then
    note "FAIL  $name (exit=$code)"
    printf '%s\n' "$out" | grep -E "error (CS|MSB[0-9]+)|Failed!|\[FAIL\]|verdict=FAIL" | head -6 | sed 's/^/        /'
    FAILED=$((FAILED + 1))
  elif [ "$min_passed" -gt 0 ] && [ "$got" -lt "$min_passed" ]; then
    note "FAIL  $name（只过了 $got 条，少于 $min_passed：判据没跑起来，不是"跑绿了"）"
    FAILED=$((FAILED + 1))
  else
    if [ "$min_passed" -gt 0 ]; then note "ok    $name（passed=$got）"; else note "ok    $name"; fi
  fi
}

build_lib() { dotnet build "$1/$1.csproj" -v:q --nologo 2>&1; }
test_proj() { dotnet test "$1/$1.csproj" -p:BaseOutputPath=bin/Verify/ -v:q --nologo 2>&1; }

echo "=== 0 客户端可见的共享库按默认输出重建 ==="
for asm in Horizon.Planet Horizon.Game.Message Horizon.Game.Sim.Contracts Horizon.Game.World.Contracts \
           Horizon.Share.Client Horizon.Game.ECS Horizon.Game.ECS.Arch; do
  out=$(dotnet build "$asm/$asm.csproj" -v:q --nologo 2>&1)
  if printf '%s' "$out" | grep -qE "error CS"; then
    echo "FAIL  重建 $asm"; printf '%s\n' "$out" | grep -E "error CS" | head -3
    FAILED=$((FAILED + 1))
  fi
done
note "ok    默认输出重建完毕"

echo "=== 1 库判据（Horizon.Planet.Tests 全量）==="
stage "planet 库全量" "^Passed!" 300 test_proj Horizon.Planet.Tests

echo "=== 2 区域链 + 消息契约（网关测试工程）==="
# 这条 filter 已经两次静默漏掉新测试类（PlanetActorPoseRulesTests、PlanetReceiptMirrorTests、
# PlanetSurfaceMotionRulesTests 都不含 "PlanetRegion"）：合闸照样念 PASS，而那些判据一次都没跑。
# 所以先自检：测试工程里每个 Planet*Tests.cs 的类名必须命中下面某个"名字族"。
PLANET_FAMILIES="PlanetRegion PlanetActorPose PlanetReceipt PlanetSurfaceMotion PlanetWire"
uncovered=""
for f in Horizon.Game.Gateway.Tests/Planet*Tests.cs; do
  [ -e "$f" ] || continue
  cls=$(basename "$f" .cs)
  hit=0
  for fam in $PLANET_FAMILIES; do
    case "$cls" in *"$fam"*) hit=1 ;; esac
  done
  [ "$hit" = 1 ] || uncovered="$uncovered $cls"
done
if [ -n "$uncovered" ]; then
  note "FAIL  filter 没覆盖这些测试类：$uncovered"
  FAILED=$((FAILED + 1))
else
  note "ok    filter 覆盖了所有 Planet*Tests.cs"
fi
stage "PlanetRegion + 契约" "^Passed!" 148 dotnet test Horizon.Game.Gateway.Tests/Horizon.Game.Gateway.Tests.csproj \
  -p:BaseOutputPath=bin/Verify/ -v:q --nologo \
  --filter "FullyQualifiedName~PlanetRegion|FullyQualifiedName~PlanetActorPose|FullyQualifiedName~PlanetReceipt|FullyQualifiedName~PlanetSurfaceMotion|FullyQualifiedName~UnionRegistrationParity|FullyQualifiedName~PlanetWire"

echo "=== 3 Silo 编译 ==="
stage "Silo 编译" "Build succeeded" 0 dotnet build Horizon.Orleans.Silo/Horizon.Orleans.Silo.csproj \
  -p:BaseOutputPath=bin/Verify/ -v:q --nologo

echo "=== 4 客户端脚本装配（引擎外的另一条编译通路）==="
stage "客户端脚本" "verdict=PASS" 0 bash scripts/verify-client-scripts.sh

echo "=== 5 客户端暴露面（IDE 工程必须与 Game.Build.cs 一致）==="
stage "no-orleans 门禁" "^verdict=PASS" 0 bash scripts/verify-client-no-orleans.sh
stage "exposure 门禁" "^verdict=PASS" 0 bash scripts/verify-client-exposure.sh

echo "=== 6 真实基场产物 ==="
if [ "${SKIP_DATA:-0}" = "1" ]; then
  note "SKIP  产物自检与地面通路（SKIP_DATA=1）"; SKIPPED=$((SKIPPED + 2))
elif [ ! -f "$DEM/manifest.json" ]; then
  note "SKIP  产物自检与地面通路（$DEM 不在盘上；大体量二进制按本地保留策略不入库）"
  SKIPPED=$((SKIPPED + 2))
else
  stage "manifest 结构一致性" "verdict=PASS" 0 bash scripts/verify-planet-dem-product.sh "$DEM"
  stage "服务端地面通路（真产物）" "GROUND_RESULT PASS" 0 dotnet run \
    --project Tools/DemProbe/DemProbe.csproj --no-build -- ground "$DEM"
fi

echo "=== 7 实机回执判据的自证（不跑引擎，只验这套断言自己会不会红）==="
# 这一段管的是 #25/#30 那两条只能靠引擎窗口的验收：判据本身先在这儿证完，
# 窗口打开时剩下的只有"跑一次并拿回执喂给它"，而不是临场对着文本 eyeball。
stage "回执判据自证" "SELFTEST PASS" 0 python Tools/planet-session-check.py --self-test

echo
echo "=== 汇总 ==="
printf '%s\n' "${RESULTS[@]}"
echo "failed=$FAILED skipped=$SKIPPED"
if [ "$FAILED" -ne 0 ]; then echo "verdict=FAIL"; exit 1; fi
if [ "$SKIPPED" -ne 0 ]; then echo "verdict=PASS（有 $SKIPPED 段被跳过，见上）"; exit 0; fi
echo "verdict=PASS"
