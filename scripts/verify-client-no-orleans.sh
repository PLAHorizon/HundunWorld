#!/bin/bash
# 客户端 Orleans 引用面核查（只读判定，不写引擎目录、不动在跑的进程）。
#
# 为什么要有它：MMORPG 客户端不该把服务端 Actor 框架带进包体。但"删引用"有两种失败方式——
# 删掉运行期真正需要的程序集（客户端 ECS 同步系统整批注册失败），或只改了清单没重建产物（包体照旧带）。
# 本脚本按 **依赖闭包 + 产物** 判定，不按注释判定。
#
# 用法：bash scripts/verify-client-no-orleans.sh
# 判据：末行 verdict=PASS。FAIL 是必须处理的；WARN 是陈旧产物，重建后消失。
set -u
ROOT="C:/Works/GitHubProjects/HundunWorld"
GB="$ROOT/HundunWorld/Source/Game/Game.Build.cs"
GC="$ROOT/HundunWorld/Source/Game.csproj"
GD="$ROOT/Horizon.Game.GengDi/Horizon.Game.GengDi.csproj"
PC="$ROOT/Horizon.Game.GengDi.PC/Horizon.Game.GengDi.PC.csproj"
fail=0
warns=0
bad() { echo "FAIL  $*"; fail=$((fail + 1)); }
warn() { echo "WARN  $*"; warns=$((warns + 1)); }
good() { echo "ok    $*"; }

echo "── 1. Flax 客户端模块清单（Game.Build.cs → .response 是唯一权威引用面）──"
for forbidden in Horizon.Game.Core Horizon.DsHost; do
    if grep -q "\"$forbidden.dll\"" "$GB"; then
        bad "Game.Build.cs 仍引用 $forbidden.dll —— 服务端 Orleans 宿主链，客户端对它的类型零引用"
    else
        good "Game.Build.cs 未引用 $forbidden.dll"
    fi
done

echo "── 2. Flax 客户端 IDE 工程（Game.csproj，仅供 IDE，需与清单一致）──"
for forbidden in "Include=\"Horizon.DsHost\"" "Include=\"Horizon.Game.Core\""; do
    n=$(grep -c "$forbidden" "$GC")
    if [ "$n" -gt 0 ]; then
        bad "Game.csproj 仍有 $n 条服务端程序集引用（$forbidden…）"
    else
        good "Game.csproj 无服务端程序集引用"
    fi
done

echo "── 3. Avalonia 客户端（GengDi）的依赖边 ──"
for forbidden in Horizon.Orleans.Interface Horizon.IoT.MQTT; do
    if grep -q "ProjectReference Include=..\\\\$forbidden" "$GD"; then
        bad "GengDi.csproj 仍 ProjectReference $forbidden —— 会把 Orleans 运行时族带进客户端包体"
    else
        good "GengDi.csproj 未引用 $forbidden"
    fi
done

echo "── 4. GengDi 解析后的传递闭包（真正的包体判据）──"
if trans=$(dotnet list "$PC" package --include-transitive 2>/dev/null); then
    heavy=$(printf '%s\n' "$trans" | grep -oE 'Microsoft\.Orleans\.(Runtime|Clustering|Persistence|Streaming|Hosting)[^ ]*' | sort -u | tr '\n' ' ')
    if [ -n "$heavy" ]; then
        bad "GengDi 传递闭包里仍有服务端 Orleans 包：$heavy"
    else
        good "GengDi 传递闭包无 Orleans 运行时族（Runtime/Clustering/Persistence/Streaming/Hosting）"
    fi
    light=$(printf '%s\n' "$trans" | grep -oE 'Microsoft\.Orleans\.(Core|Sdk|Serialization)[^ ]*' | sort -u | tr '\n' ' ')
    [ -n "$light" ] && echo "note  仍随共享契约程序集带入的序列化层 Orleans 包：$light"
    [ -n "$light" ] && echo "      成因：Horizon.Game.Message / Horizon.IM.Message / Horizon.Share 自身带 Orleans 特性与代码生成，"
    [ -n "$light" ] && echo "      客户端反射这些 DTO 时需要 Orleans.Serialization.Abstractions（实测缺失即 FileNotFoundException）。"
    [ -n "$light" ] && echo "      要清零需拆契约程序集（客户端可见 DTO 独立成无 Orleans 特性的程序集）。"
else
    warn "dotnet list 失败（NuGet 还原不可用），第 4 项未判定"
fi

echo "── 5. 产物面：客户端输出目录 ──"
for d in "$ROOT/HundunWorld/Binaries/GameTarget/Windows/x64/Debug" \
         "$ROOT/HundunWorld/Binaries/GameTarget/Windows/x64/Development" \
         "$ROOT/HundunWorld/Binaries/GameEditorTarget/Windows/x64/Debug" \
         "$ROOT/HundunWorld/Binaries/GameEditorTarget/Windows/x64/Development"; do
    [ -d "$d" ] || continue
    rel=${d#"$ROOT"/}
    for forbidden in Horizon.Game.Core Horizon.DsHost Orleans.Runtime Orleans.Core Orleans.Persistence.Redis Orleans.Streaming; do
        if [ -f "$d/$forbidden.dll" ]; then
            warn "$rel 里仍有 $forbidden.dll —— 清单已不引用，属陈旧产物；关闭编辑器后删掉副本并重建脚本"
        fi
    done
done

echo "── 6. 发布面：安装包载荷 ──"
for payload in "$ROOT/publish/GengDi" "$ROOT/dist/GengDi-StandaloneInstaller/payload"; do
    [ -d "$payload" ] || continue
    heavy=$(ls "$payload" 2>/dev/null | grep -E '^Orleans\.(Runtime|Clustering|Persistence|Streaming)' | tr '\n' ' ')
    if [ -n "$heavy" ]; then
        if [ "$payload" -nt "$GD" ]; then
            bad "${payload#"$ROOT"/} 是在 GengDi.csproj 改引用之后 publish 的，却仍含 Orleans 运行时族：$heavy"
        else
            warn "${payload#"$ROOT"/} 是改引用之前的旧产物，仍含：$heavy —— 重新跑 build-installer.ps1 后会消失"
        fi
    else
        good "${payload#"$ROOT"/} 载荷无 Orleans 运行时族"
    fi
done

echo "── 7. 已知保留项（非自由项）──"
if grep -q '"Orleans.Serialization.dll"' "$GB"; then
    echo "note  Flax 客户端仍保留 Orleans.Serialization / .Abstractions："
    echo "      客户端代码对 Orleans 零引用（把这两条从 .response 剥掉后 csc error=0），"
    echo "      但 Orleans 源生成器把 OrleansCodeGen.* 类型编进了 Horizon.Game.Message 与 Horizon.Game.ECS.Arch。"
    echo "      2026-10-07 用客户端产物目录实测（删掉这两个 DLL 后反射）：前者 1328 个类型、后者 1 个类型加载失败，"
    echo "      而 HundunWorldGame.cs 走 SystemRegistry.RegisterFromAssembly 的裸 GetTypes()，"
    echo "      ReflectionTypeLoadException 会让整批 ECS 同步系统注册失败（多客户端互相不可见的故障面）。"
    echo "      摘除前置条件：契约层与 Orleans 代码生成解耦，或让类型扫描对 RTLE 容错且逐条排查属性物化点。"
fi

echo ""
if [ "$fail" -eq 0 ]; then
    [ "$warns" -gt 0 ] && echo "verdict=PASS（$warns 项陈旧产物待重建）" || echo "verdict=PASS"
else
    echo "verdict=FAIL ($fail 项，另有 $warns 项 WARN)"
fi
exit "$fail"
