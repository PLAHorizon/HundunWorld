#!/bin/bash
# 星球基场产物自检：只读 manifest.json 反算全球覆盖与金字塔一致性（不碰 7.4 GB 的大文件）。
#
# 为什么要它：Horizon.Planet.Tests 用的是合成小格子夹具，结构上测不到"这份产物是不是全球严丝合缝"。
# §13.60 就为这个红过一次——夹具把 256 列配 1.0° 像元（只盖住 256° 经度），
# 于是"接缝那一侧的经度"被折回网格中间，用例以为在测环绕、其实在读另一个格。
# 同样的形状落到真产物上 = 海岸线附近错开半个像元，而运行期一声不响。
#
# 用法：bash scripts/verify-planet-dem-product.sh [demRoot]
#   判据：verdict=PASS（结构一致 + 口径七项不退化）；数据不在盘上时 verdict=SKIP（**看得见**，不算通过）
set -u
ROOT="C:/Works/GitHubProjects/HundunWorld"
DEM="${1:-$ROOT/Data/GEBCO_2026/derived}"

if [ ! -f "$DEM/manifest.json" ]; then
  echo "demRoot=$DEM"
  echo "verdict=SKIP reason=manifest-absent  基场产物不在盘上（大体量二进制按本地保留策略不入库），这条自检没有跑"
  exit 0
fi

OUT=$(DOTNET_CLI_UI_LANGUAGE=en dotnet run --project "$ROOT/Tools/DemProbe/DemProbe.csproj" -- manifest "$DEM" 2>&1)
code=$?
printf '%s\n' "$OUT" | grep -vE "^\s*$"
if [ "$code" -ne 0 ]; then
  echo "verdict=FAIL reason=manifest-gate-exit-$code"
  exit 1
fi
if ! printf '%s' "$OUT" | grep -q "MANIFEST_RESULT PASS"; then
  # 退出码 0 但没看到那句成功标记 = 根本没跑到判据本体（例如 dotnet run 自己失败了却被 grep 吃掉）
  echo "verdict=FAIL reason=no-receipt-line"
  exit 1
fi
echo "verdict=PASS"
