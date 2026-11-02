#!/usr/bin/env bash
# 闸门：RootScene 防污染 —— 禁止"无场景参数的 Level.SpawnActor(actor)"出现在白名单之外。
#
# 背景（引擎源码证据）：Flax 的 Level.SpawnActor(actor) 无父级时固定落入
# Level::Scenes[0]（见 FlaxEngine/Source/Engine/Level/Level.cpp spawnActor：
# `parent = Level::Scenes[0]`）。本项目 0 号场景是常驻 RootScene，切场景不卸载；
# 世界对象（NPC/怪物/载具/攀爬物/特效/音效）若依赖该默认落点，会把 RootScene
# 堆成垃圾场并跨会话滞留成幽灵实体。
#
# 口径：RootScene 只允许"全局共享的脚本和宿主"（GameSceneManager、持久 UI Canvas、
# SceneTransitionEffect、NarrativeSaveSubsystemHost、GlobalAudioHost 等）驻留；
# 其余运行期 Actor 必须显式传场景（玩法场景用
# HundunWorldGame.SpawnActorToGameplayScene / NarrativeRuntimeBootstrap.TrySpawnToGameplayScene）。
#
# 用法：bash scripts/verify-rootscene-spawn-routing.sh        # 扫全仓，FAIL 则退出码 1
#       bash scripts/verify-rootscene-spawn-routing.sh <dir>  # 只扫指定目录（自检用）
set -u
cd "$(dirname "$0")/.."

SCAN_TARGET="${1:-}"
if [ -n "$SCAN_TARGET" ]; then
    TARGETS=("$SCAN_TARGET")
else
    TARGETS=(HundunWorld/Source/Game HundunWorld/Plugins)
fi

# 白名单：全局宿主引导与路由辅助函数自身（其内部单参调用是带 LogError 的兜底路径）
is_allowed() {
    case "$1" in
        */HundunWorldGame.cs) return 0 ;;                                # SpawnActorToGameplayScene 兜底
        */NarrativeRuntimeBootstrap.cs) return 0 ;;                      # TrySpawnToGameplayScene 辅助
        */GameSceneManager.cs) return 0 ;;                               # 自身宿主引导（0 号场景兜底，全局）
        */NavMeshBaker.cs) return 0 ;;                                   # 编辑器烘焙工具：落在被编辑的场景，非运行期玩法对象
        */PCGSelfTest.cs) return 0 ;;                                    # PCG 自检夹具：临时 Actor 用完即销毁
        *) return 1 ;;
    esac
}

VIOLATIONS=0
for t in "${TARGETS[@]}"; do
    [ -d "$t" ] || continue
    while IFS= read -r hit; do
        file="${hit%%:*}"
        if is_allowed "$file"; then continue; fi
        echo "VIOLATION: $hit"
        VIOLATIONS=$((VIOLATIONS+1))
    done < <(grep -rn --include='*.cs' -E '(FlaxEngine\.)?Level\.SpawnActor\([A-Za-z_@][A-Za-z0-9_]*\)[[:space:]]*[;)]' "$t" \
             | grep -v '/Binaries/' | grep -v 'TrySpawnToGameplayScene\|SpawnActorToGameplayScene')
done

if [ "$VIOLATIONS" -gt 0 ]; then
    echo "FAIL: $VIOLATIONS 处无场景生成。RootScene 只允许全局共享宿主；世界对象必须显式传玩法场景。"
    echo "修法：Game 用 HundunWorldGame.SpawnActorToGameplayScene(actor, 名字)；"
    echo "      NarrativePro 用 NarrativeRuntimeBootstrap.TrySpawnToGameplayScene(actor, 名字)；"
    echo "      确属全局宿主的，显式传 Level.GetScene(0) 并注释「有意常驻 RootScene」。"
    exit 1
fi
echo "PASS: 无场景版 Level.SpawnActor 仅存在于白名单全局宿主中。"
