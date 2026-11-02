#!/bin/bash
# 客户端暴露面核查：MMORPG 客户端只允许拿「共享且无算法」的代码。
#
# 三条判据，全部按**清单/闭包/产物**判定，不看注释：
#   ① 服务端权威程序集不得出现在客户端引用面（Sim / World / Core / Entities / Model / Share / Orleans.* / DsHost…）；
#   ② 客户端必须有对应的 Contracts（Sim.Contracts / World.Contracts / Share.Client）；
#   ③ Contracts 自身不得含权威算法类型（它们是"无算法共享层"的承诺，必须可机械核验）。
#
# 用法：bash scripts/verify-client-exposure.sh
# 判据：末行 verdict=PASS。FAIL 必处理；WARN 是陈旧产物，重建/重发布后消失。
set -u
ROOT="C:/Works/GitHubProjects/HundunWorld"
GB="$ROOT/HundunWorld/Source/Game/Game.Build.cs"
GC="$ROOT/HundunWorld/Source/Game.csproj"
GD="$ROOT/Horizon.Game.GengDi/Horizon.Game.GengDi.csproj"
PC="$ROOT/Horizon.Game.GengDi.PC/Horizon.Game.GengDi.PC.csproj"
GDA="$ROOT/Horizon.Game.GengDi/obj/project.assets.json"
fail=0
warns=0
bad() { echo "FAIL  $*"; fail=$((fail + 1)); }
warn() { echo "WARN  $*"; warns=$((warns + 1)); }
good() { echo "ok    $*"; }

# 服务端权威/内部程序集（任何客户端都不得引用）
SERVER_ONLY="Horizon.Game.Sim Horizon.Game.World Horizon.Game.Core Horizon.Core Horizon.Core.Abstract Horizon.DsHost Horizon.Entities Horizon.Model Horizon.Share Horizon.Orleans.Interface Horizon.IoT.MQTT"

echo "── 1. Flax 客户端模块清单（Game.Build.cs 是唯一权威引用面）──"
for a in $SERVER_ONLY; do
    if grep -q "\"$a.dll\"" "$GB"; then
        bad "Game.Build.cs 引用了服务端程序集 $a.dll"
    fi
done
[ "$fail" -eq 0 ] && good "Game.Build.cs 无服务端权威程序集"
echo "── 2. 客户端应装的无算法共享件 ──"
for a in Horizon.Game.Sim.Contracts Horizon.Game.World.Contracts Horizon.Share.Client; do
    if grep -q "\"$a.dll\"" "$GB"; then good "Game.Build.cs 含 $a.dll"; else bad "Game.Build.cs 缺 $a.dll（客户端会缺类型）"; fi
done

echo "── 3. Flax IDE 工程（Game.csproj）──"
for a in Horizon.DsHost Horizon.Game.Core Horizon.Game.Sim Horizon.Game.World Horizon.Core.Abstract; do
    n=$(grep -c "Include=\"$a\"" "$GC")
    if [ "$n" -gt 0 ]; then bad "Game.csproj 仍有 $n 条 $a 引用"; fi
done
[ "$fail" -eq 0 ] && good "Game.csproj 无服务端程序集引用"

echo "── 4. Avalonia 客户端依赖边与解析闭包 ──"
for a in Horizon.Core Horizon.Orleans.Interface Horizon.IoT.MQTT Horizon.Entities Horizon.Model Horizon.Share; do
    if grep -q "ProjectReference Include=..\\\\$a" "$GD"; then bad "GengDi.csproj 直接引用 $a"; fi
done
if [ -f "$GDA" ]; then
    # 排除客户端自身工程名（assets 里必然出现），只判"有没有把服务端工程拖进来"
    leaked=$(grep -o '"Horizon[A-Za-z.]*"' "$GDA" | tr -d '"' | sort -u \
        | grep -vE '^Horizon\.(Game\.Message|IM\.Message|Share\.Client)$' \
        | grep -vE '^Horizon\.Game\.GengDi' | tr '\n' ' ')
    if [ -n "$leaked" ]; then bad "GengDi 解析闭包含非共享工程：$leaked"; else good "GengDi 闭包只剩 Game.Message / IM.Message / Share.Client"; fi
else
    warn "缺 $GDA（未还原），闭包未判定"
fi

echo "── 5. Contracts 里不得有权威算法（无算法共享层的机械核验）──"
# 只判代码行：doc 注释里的 <see cref="MovementValidator"/> 是说明性引用，不是算法实现
code_hit() { # $1=目录 $2=正则
    grep -rnE --include=*.cs "$2" "$1" 2>/dev/null \
        | grep -vE ':[0-9]+: *///|:[0-9]+: *//' | head -3
}
if [ -d "$ROOT/Horizon.Game.Sim.Contracts" ]; then
    hit=$(code_hit "$ROOT/Horizon.Game.Sim.Contracts" "MovementValidator|CombatCore|MonsterAiCore|SpawnDirectorCore|SimWorld|SyncSessionDispatch|GoldenTrace|QuestTemplateValidator|FormulaVersionPolicy")
    [ -n "$hit" ] && bad "Sim.Contracts 出现权威类型：$hit" || good "Sim.Contracts 无权威算法类型"
fi
if [ -d "$ROOT/Horizon.Game.World.Contracts" ]; then
    hit=$(code_hit "$ROOT/Horizon.Game.World.Contracts" "ZoneShardAoi|WorldChunkAoi|GetChunksInView|FillChunksInView")
    [ -n "$hit" ] && bad "World.Contracts 出现 AOI 订阅算法：$hit" || good "World.Contracts 无 AOI 算法"
fi
if [ -d "$ROOT/Horizon.Share.Client" ]; then
    hit=$(code_hit "$ROOT/Horizon.Share.Client" "PlayerIdGenerator|UserAuthTokenProvider|SecurePasswordHasher|SensitiveWordFilter|OrleansClient|GrainState|StackExchange")
    [ -n "$hit" ] && bad "Share.Client 出现服务端实现：$hit" || good "Share.Client 无服务端实现"
fi

echo "── 6. 产物面：客户端输出目录 ──"
for d in "$ROOT/HundunWorld/Binaries/GameTarget/Windows/x64/Debug" \
         "$ROOT/HundunWorld/Binaries/GameTarget/Windows/x64/Development" \
         "$ROOT/HundunWorld/Binaries/GameEditorTarget/Windows/x64/Debug" \
         "$ROOT/HundunWorld/Binaries/GameEditorTarget/Windows/x64/Development"; do
    [ -d "$d" ] || continue
    rel=${d#"$ROOT"/}
    for a in $SERVER_ONLY; do
        [ -f "$d/$a.dll" ] && warn "$rel 里仍有 $a.dll —— 清单已不引用，陈旧产物；重建脚本后消失"
    done
done

echo "── 7. 发布面：安装包载荷 ──"
for payload in "$ROOT/publish/GengDi" "$ROOT/dist/GengDi-StandaloneInstaller/payload"; do
    [ -d "$payload" ] || continue
    heavy=$(ls "$payload" 2>/dev/null | grep -E '^(Horizon\.(Game\.Core|Orleans\.Interface|IoT\.MQTT|Entities|Model|Share|Core)\.dll|Microsoft\.EntityFrameworkCore|Microsoft\.Data\.SqlClient|StackExchange\.Redis|DotNetty|Orleans\.(Runtime|Persistence|Streaming|Clustering))' | tr '\n' ' ')
    if [ -n "$heavy" ]; then
        if [ "$payload" -nt "$GD" ]; then bad "${payload#"$ROOT"/} 改引用后发布却仍含服务端件：$heavy"
        else warn "${payload#"$ROOT"/} 是改引用前的旧产物，仍含：$heavy —— 重跑 build-installer.ps1 后消失"; fi
    else good "${payload#"$ROOT"/} 载荷干净"
    fi
done

echo "── 8. 已知保留项（非自由项）──"
if grep -q '"Orleans.Serialization.dll"' "$GB"; then
    echo "note  客户端仍装 Orleans.Serialization/.Abstractions + Horizon.Game.Message 的 OrleansCodeGen.*："
    echo "      实测缺这两个 DLL 时 GetTypes() 会整批失败（Message 1328 个类型 / ECS.Arch 1 个），"
    echo "      而客户端走 SystemRegistry 的裸 GetTypes() 扫系统 → ECS 同步系统会整批注册失败。"
    echo "      彻底清零的前置条件：契约层与 Orleans 代码生成解耦（客户端 DTO 独立成无特性程序集）。"
fi

echo ""
if [ "$fail" -eq 0 ]; then
    [ "$warns" -gt 0 ] && echo "verdict=PASS（$warns 项陈旧产物待重建）" || echo "verdict=PASS"
else
    echo "verdict=FAIL ($fail 项，另有 $warns 项 WARN)"
fi
exit "$fail"
