#!/usr/bin/env bash
# 阶段 2 端到端验证 —— 本地网关 + DS 一键启动环境
#
# 用法：
#   source tools/e2e/phase2-env.sh     # 加载环境变量
#   gw_start                            # 起网关（后台）
#   ds_start                            # 起 DS（后台）
#   gw_stop / ds_stop                   # 停
#
# 设计说明：
# - Redis 8.2.1 已在 127.0.0.1:9379 运行且**无密码**（实测 AUTH 会报 "called without any password"）。
#   因此所有 Redis 连接串都不带 password。
# - Orleans Silo 未启动（11111 未监听）。网关的 Orleans 客户端会连不上——
#   这是**可接受的**：本用例的判别式恰恰是"客户端收到的快照里有怪物实体"，
#   而怪物只有 DS 才有（ZoneShardGrain 无刷怪系统）。Orleans 不可用反而排除了回退路径。
# - 但 ConnectionStrings.Require 要求 Redis:ConnectionString 非空，故必须注入。

# ── 基础环境 ────────────────────────────────────────────────────────────────
export PATH="/usr/bin:/bin:/usr/local/bin:$PATH"
export APPDATA="C:\\Users\\33011\\AppData\\Roaming"
export LOCALAPPDATA="C:\\Users\\33011\\AppData\\Local"
export ProgramData="C:\\ProgramData"

REPO_ROOT="/c/Works/GitHubProjects/HundunWorld"
DOTNET="/c/Program Files/dotnet/dotnet.exe"

# ── Redis（实测无密码） ─────────────────────────────────────────────────────
export GATEWAY_REDIS_CONNECTION_STRING="127.0.0.1:9379"
export REDIS_HOST="127.0.0.1"
export REDIS_PASSWORD=""

# ── 网关绑定 ────────────────────────────────────────────────────────────────
export GATEWAY_BIND_IP="127.0.0.1"
export GATEWAY_PUBLIC_IP="127.0.0.1"

# ── Orleans（Silo 未起；仅需非空以通过配置校验） ────────────────────────────
export ORLEANS_SILO_ENDPOINTS="127.0.0.1:11111"
export ORLEANS_SILO_HOST="127.0.0.1"

# ── DS 控制面（网关 DsUplinkPort=19871；DS 数据面 19870） ────────────────────
export HORIZON_GATEWAY_HOST="127.0.0.1"
export HORIZON_GATEWAY_DS_UPLINK_PORT="19871"
export HORIZON_DS_LISTEN_PORT="19870"
export HORIZON_DS_ADVERTISE_HOST="127.0.0.1"
export HORIZON_DS_ID="ds-e2e-0"
export HORIZON_DS_SHARD_IDS="0"
export HORIZON_DS_TICK_RATE="60"
export HORIZON_DS_REGION=""

# ── 端到端测试目标（GatewayDsFullLoopLiveTests 的门控） ─────────────────────
export HORIZON_GATEWAY_TCP="127.0.0.1:7789"

# ── 日志目录 ────────────────────────────────────────────────────────────────
E2E_LOG_DIR="$REPO_ROOT/.dbg/e2e-phase2"
mkdir -p "$E2E_LOG_DIR"

gw_start() {
  echo "[e2e] 启动网关（日志 → .dbg/e2e-phase2/gateway.log）…"
  cd "$REPO_ROOT/Horizon.Game.Gateway/bin/Debug/net10.0" && \
    "$DOTNET" Horizon.Game.Gateway.dll > "$E2E_LOG_DIR/gateway.log" 2>&1 &
  echo $! > "$E2E_LOG_DIR/gateway.pid"
  echo "[e2e] 网关 PID=$(cat "$E2E_LOG_DIR/gateway.pid")"
}

ds_start() {
  echo "[e2e] 启动 DS（日志 → .dbg/e2e-phase2/ds.log）…"
  cd "$REPO_ROOT/Horizon.DsHost/bin/Debug/net10.0" && \
    "$DOTNET" Horizon.DsHost.dll > "$E2E_LOG_DIR/ds.log" 2>&1 &
  echo $! > "$E2E_LOG_DIR/ds.pid"
  echo "[e2e] DS PID=$(cat "$E2E_LOG_DIR/ds.pid")"
}

gw_stop() { [ -f "$E2E_LOG_DIR/gateway.pid" ] && kill "$(cat "$E2E_LOG_DIR/gateway.pid")" 2>/dev/null; rm -f "$E2E_LOG_DIR/gateway.pid"; echo "[e2e] 网关已停"; }
ds_stop() { [ -f "$E2E_LOG_DIR/ds.pid" ] && kill "$(cat "$E2E_LOG_DIR/ds.pid")" 2>/dev/null; rm -f "$E2E_LOG_DIR/ds.pid"; echo "[e2e] DS 已停"; }

echo "[e2e] 环境已加载。Redis=$GATEWAY_REDIS_CONNECTION_STRING 网关TCP=$GATEWAY_BIND_IP:7789 DS上行=127.0.0.1:19871 数据面=19870"
