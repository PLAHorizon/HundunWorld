import { Stack, Row, Grid, H1, H2, H3, Text, Divider, Stat, Table, Callout, Pill, Card, CardHeader, CardBody, Timeline } from 'qoder/canvas';

export default function ClientProbeLoopFixReport() {
  return (
    <Stack gap={24}>
      <Stack gap={4}>
        <H1>客户端无限探查请求修复报告</H1>
        <Text tone="secondary">混沌世界 · Horizon.Game.Gateway 日志分析 · 2026-07-30</Text>
      </Stack>

      <Grid columns={4} gap={12}>
        <Stat value="2" label="修改文件" />
        <Stat value="0" label="编译错误" tone="success" />
        <Stat value="5" label="心跳阈值" />
        <Stat value="5" label="最大探查轮次" />
      </Grid>

      <Divider />

      <H2>根因分析</H2>
      <Callout tone="danger" title="无限循环根因">
        原实现 _connectFunction = ProbeGatewayAsync + ConnectAsync。RST 探查成功后建立完整 TCP 连接，但客户端未在游戏内不发送任何数据，服务端首包超时(5s)关闭连接，触发 OnClientDisconnected → HandleDisconnect → 重新探查，形成每 11 秒 2 个连接的无限循环。
      </Callout>

      <Timeline
        events={[
          { title: 'RST 探查成功', description: 'ProbeGatewayAsync TCP 三次握手 + RST 关闭', tone: 'info' },
          { title: '建立完整 TCP 连接', description: 'ConnectAsync 创建持久连接（已移除）', tone: 'danger' },
          { title: '服务端首包超时', description: '5s 无数据，服务端关闭连接', tone: 'warning' },
          { title: 'OnClientDisconnected', description: '触发 HandleDisconnect → 重新探查', tone: 'danger' },
          { title: '无限循环', description: '每 11 秒重复：RST 探查 + 首包超时', tone: 'danger' },
        ]}
      />

      <Divider />

      <H2>修复方案</H2>

      <Card>
        <CardHeader>
          <Row gap={8}>
            <Pill tone="success">核心修复</Pill>
            <H3>NetworkManager.cs</H3>
          </Row>
        </CardHeader>
        <CardBody>
          <Table
            headers={['修改项', '说明']}
            rows={[
              ['_connectFunction 仅 RST 探查', '不再调用 ConnectAsync，探查成功即停止循环'],
              ['注入 networkCheckFunction', 'NetworkStateMonitor.IsNetworkAvailableAsync'],
              ['OnNetworkStatusChanged 守卫', '_disconnectTimedOut=true 时跳过自动重连'],
              ['ManualReconnectAsync 重置', '用户手动触发时重置永久停止标记'],
            ]}
          />
        </CardBody>
      </Card>

      <Card>
        <CardHeader>
          <Row gap={8}>
            <Pill tone="success">核心修复</Pill>
            <H3>ReconnectionManager.cs</H3>
          </Row>
        </CardHeader>
        <CardBody>
          <Table
            headers={['修改项', '说明']}
            rows={[
              ['MaxReconnectAttempts = 5', '原值 10，5 轮探查失败即永久停止'],
              ['HeartbeatFailureThreshold = 5', '连续 5 次心跳超时才触发探查'],
              ['_consecutiveHeartbeatFailures', '连续心跳超时计数器，收到数据时重置'],
              ['CheckHeartbeat 检查本地网络', '本地网络不可用则不探查，等 NetworkStateMonitor 通知'],
              ['_disconnectTimedOut 永久守卫', 'HandleDisconnect 顶部守卫，永久阻止自动重连'],
              ['networkCheckFunction 参数', '构造函数新增本地网络检查函数'],
            ]}
          />
        </CardBody>
      </Card>

      <Divider />

      <H2>修复后行为流程</H2>
      <Timeline
        events={[
          { title: '心跳超时', description: '计数 +1，未达 5 次则等待下次检查', tone: 'info' },
          { title: '连续 5 次超时', description: '检查本地网络环境', tone: 'warning' },
          { title: '本地网络不可用', description: '不探查，等 NetworkStateMonitor 通知网络恢复', tone: 'info' },
          { title: '本地网络可用', description: '断开旧连接 → 探查网关（最多 5 轮）', tone: 'info' },
          { title: '探查成功', description: '停止循环，等用户主动进入游戏', tone: 'success' },
          { title: '5 轮全失败', description: '永久停止，等用户手动触发（ManualReconnect / ConnectOnDemand）', tone: 'warning' },
        ]}
      />

      <Divider />

      <H2>验证结果</H2>
      <Grid columns={2} gap={12}>
        <Stat value="0 错误" label="编译结果" tone="success" />
        <Stat value="2 个文件" label="修改范围" />
      </Grid>

      <Text tone="secondary" size="small">修复时间：2026-07-30 · 混沌世界客户端网络探查机制</Text>
    </Stack>
  );
}
