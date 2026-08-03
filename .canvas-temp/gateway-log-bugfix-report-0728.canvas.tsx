import { Stack, Row, Grid, H1, H2, H3, Text, Divider, Stat, Table, Callout, Card, CardHeader, CardBody, Pill } from 'qoder/canvas';

export default function GatewayLogBugfixReport0728() {
  return (
    <Stack gap={24}>
      <Stack gap={4}>
        <H1>Gateway 日志 BUG 分析与修复报告</H1>
        <Text tone="secondary">Horizon.Game.Gateway · 2026-07-28 · 日志 2700+ 行 · 修复 2 个 BUG</Text>
      </Stack>

      <Grid columns={4} gap={12}>
        <Stat value="2" label="发现 BUG" tone="warning" />
        <Stat value="2" label="已修复" tone="success" />
        <Stat value="2" label="修改文件" />
        <Stat value="0" label="编译错误" tone="success" />
      </Grid>

      <Divider />

      <H2>用户报告的症状</H2>
      <Table
        headers={['症状', '表现']}
        rows={[
          ['静止角色同时离线', '3个客户端角色在线，静止不动一会后同时显示离线（实际并未离线）'],
          ['移动延迟加剧', '另外客户端移动越多，延迟越明显'],
          ['位置不连贯', '伴有移动位置不连贯、不精确的情况'],
        ]}
        rowTone={['danger', 'warning', 'warning']}
      />

      <Divider />

      <H2>BUG 详情与修复</H2>

      <Card>
        <CardHeader>
          <Row gap={8}>
            <Pill tone="danger">核心根因</Pill>
            <H3>BUG 1: 带宽阈值过低导致快照限流</H3>
          </Row>
        </CardHeader>
        <CardBody>
          <Stack gap={12}>
            <Callout tone="danger" title="影响链">
              100kbps 阈值被 3 人触发 → 快照频率 20Hz 降至 10Hz → 移动延迟加倍 + 位置不连贯 + 静止实体客户端超时判定离线
            </Callout>
            <Table
              headers={['项目', '内容']}
              rows={[
                ['日志证据', '[WRN] 带宽超阈值限流：bandwidth=113.56kbps > threshold=100.00kbps，快照频率降为 10Hz'],
                ['根因分析', '3 session × 2 delta × 80B × 20Hz ≈ 96kbps，3人在线即触及 100kbps 阈值'],
                ['修复方案', 'BandwidthThresholdKbps 从 100 提升至 500（可容纳约 15 个并发玩家）'],
                ['修改文件', 'Horizon.Game.Core/Sim/Server/GatewaySyncDispatcher.cs'],
              ]}
            />
          </Stack>
        </CardBody>
      </Card>

      <Card>
        <CardHeader>
          <Row gap={8}>
            <Pill tone="warning">资源耗尽</Pill>
            <H3>BUG 2: 幽灵连接洪泛</H3>
          </Row>
        </CardHeader>
        <CardBody>
          <Stack gap={12}>
            <Callout tone="warning" title="影响">
              10 分钟内产生数百个幽灵连接，每个占用 6 秒首包超时检测资源，严重消耗服务器 CPU 和内存
            </Callout>
            <Table
              headers={['项目', '内容']}
              rows={[
                ['日志证据', '每 3 秒 2-3 个新 TCP 连接，全部"首包超时"或"远程终端主动断开"，持续 10+ 分钟'],
                ['根因分析', '第三个客户端陷入无限重连循环，服务端无连接速率限制'],
                ['修复方案', '新增 IpConnectionTracker 类，per-IP 速率限制（10秒内最多5次，超限冷却30秒）'],
                ['修改文件', 'Horizon.Game.Gateway/Network/GameNetworkServer.cs'],
              ]}
            />
          </Stack>
        </CardBody>
      </Card>

      <Divider />

      <H2>验证结果</H2>
      <Table
        headers={['检查项', '结果']}
        rows={[
          ['CS 编译错误', '0 个'],
          ['MSB 文件锁定错误', '40 个（运行中进程锁定 DLL，非代码问题）'],
          ['修复文件数', '2 个（GatewaySyncDispatcher.cs, GameNetworkServer.cs）'],
          ['带宽阈值', '100kbps → 500kbps'],
          ['连接速率限制', '10秒/5次，超限冷却30秒'],
        ]}
        rowTone={['success', undefined, undefined, undefined, undefined]}
      />

      <Text tone="secondary" size="small">报告生成时间：2026-07-28 · 混沌世界游戏网关日志分析</Text>
    </Stack>
  );
}
