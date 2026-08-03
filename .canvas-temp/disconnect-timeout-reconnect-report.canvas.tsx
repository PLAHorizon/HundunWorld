import { Stack, Row, Grid, H1, H2, H3, Text, Divider, Stat, Table, Callout, Timeline, Pill } from 'qoder/canvas';

export default function DisconnectTimeoutReconnectReport() {
  return (
    <Stack gap={24}>
      <Stack gap={4}>
        <H1>网关离线超时退回 + 按需重连</H1>
        <Text tone="secondary">HundunWorld 客户端断线处理流程重构 · 2026-07-28</Text>
      </Stack>

      <Grid columns={3} gap={12}>
        <Stat value="4" label="修改文件" />
        <Stat value="0" label="编译错误" tone="success" />
        <Stat value="60s" label="超时阈值" tone="warning" />
      </Grid>

      <Divider />

      <H2>新断线处理流程</H2>
      <Timeline
        events={[
          {
            title: '阶段1: 断线检测与自动重连',
            description: '心跳超时30s或TCP断开 → HandleDisconnect → 延迟5s后指数退避重连（最多10次）',
            tone: 'info',
          },
          {
            title: '阶段2: 超时退回（新增）',
            description: '断线超过60s仍未重连 → 停止所有自动重连 → 触发OnDisconnectTimeout → 退回角色选择界面 → 不再发起任何网络请求',
            tone: 'warning',
          },
          {
            title: '阶段3: 按需连接（新增）',
            description: '用户选择进入游戏 → ConnectOnDemandAsync() → 探查网关 → 不可达则提示用户 / 可达则建立连接进入游戏世界',
            tone: 'success',
          },
        ]}
      />

      <Divider />

      <H2>修改的文件</H2>
      <Table
        headers={['文件', '修改内容']}
        rows={[
          ['ReconnectionManager.cs', '新增 DisconnectTimeoutMs(60s)、OnDisconnectTimeout 事件、超时计时器、ResetDisconnectTimeout()'],
          ['NetworkManager.cs', '新增 DisconnectTimedOut 事件、ConnectOnDemandAsync() 按需连接方法'],
          ['HundunWorldGame.cs', 'Failed 状态退回 CharacterSelection 场景（而非 Login）'],
          ['CharacterManager.cs', 'EnterGameAsync 中网关不在线时调用 ConnectOnDemandAsync() 按需拉起连接'],
        ]}
      />

      <Divider />

      <H2>关键设计决策</H2>
      <Stack gap={8}>
        <Callout tone="info" title="退回角色选择而非登录">
          断线超时后退回 CharacterSelection 场景，保留用户已选择的角色信息，无需重新登录。
        </Callout>
        <Callout tone="info" title="完全停止网络请求">
          退回后 _disconnectTimedOut=true 阻止所有自动重连路径（延迟重连、指数退避、NetworkStateMonitor），直到用户主动触发。
        </Callout>
        <Callout tone="info" title="按需连接先探查后正式连接">
          ConnectOnDemandAsync 先用原始 TCP Socket（LingerOption RST）探查网关可达性，避免失败的连接在服务端留下幽灵会话。
        </Callout>
      </Stack>

      <Divider />

      <H2>验证结果</H2>
      <Table
        headers={['检查项', '结果']}
        rows={[
          ['编译 (Game.csproj)', '0 错误, 0 警告'],
          ['超时退回逻辑', 'DisconnectTimeoutMs=60000 → OnDisconnectTimeout → CharacterSelection'],
          ['停止自动重连', '_disconnectTimedOut + CancelDelayedReconnect + _reconnectCts.Cancel'],
          ['按需连接', 'ConnectOnDemandAsync → ProbeGatewayAsync → ConnectAsync'],
          ['网关不可达提示', 'HandleError("服务器当前不在线，无法进入游戏。请稍后重试。")'],
        ]}
        rowTone={['success', undefined, undefined, undefined, undefined]}
      />

      <Text tone="secondary" size="small">报告生成时间：2026-07-28 · 混沌世界客户端断线处理重构</Text>
    </Stack>
  );
}
