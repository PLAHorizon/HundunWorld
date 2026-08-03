import { Stack, Row, Grid, H1, H2, H3, Text, Divider, Stat, Table, Callout, Card, CardHeader, CardBody, Pill } from 'qoder/canvas';

export default function GatewayLogBugfixReport() {
  return (
    <Stack gap={24}>
      <Stack gap={4}>
        <H1>Gateway 日志 BUG 分析与修复报告</H1>
        <Text tone="secondary">Horizon.Game.Gateway · 2026-07-27 · 日志 36,170 行</Text>
      </Stack>

      <Grid columns={4} gap={12}>
        <Stat value="3" label="发现 BUG" tone="warning" />
        <Stat value="2" label="已修复" tone="success" />
        <Stat value="3" label="修改文件" />
        <Stat value="0" label="编译错误" tone="success" />
      </Grid>

      <Divider />

      <H2>BUG 详情</H2>

      <Card>
        <CardHeader>
          <Row gap={8}>
            <Pill tone="danger">严重</Pill>
            <H3>PlayerDespawnScheduler — Despawn 无重试导致幽灵角色</H3>
          </Row>
        </CardHeader>
        <CardBody>
          <Stack gap={12}>
            <Callout tone="danger" title="影响">
              角色在线状态永久卡 true（幽灵角色）+ ZoneShardGrain AOI 订阅未清理 → GatewaySyncDispatcher 持续向已离线 session 发包（totalDropped=1988）
            </Callout>
            <Table
              headers={['项目', '内容']}
              rows={[
                ['日志表现', '[ERR] 角色 22/24 UnregisterEntity/RemoveSession 失败, GoOfflineAsync 异常, Despawn 完成（goOfflineCompleted=False）'],
                ['根因', 'Orleans Silo 短暂不可达（ConnectionFailedException）时，DoDespawnCoreAsync 中所有 grain 调用无重试直接失败'],
                ['修复方案', '为所有 Orleans grain 调用增加 ExecuteWithRetryAsync（3 次重试，指数退避 1s→2s→4s），覆盖 ConnectionFailedException + OrleansMessageRejectionException'],
                ['修改文件', 'PlayerDespawnScheduler.cs'],
              ]}
            />
          </Stack>
        </CardBody>
      </Card>

      <Card>
        <CardHeader>
          <Row gap={8}>
            <Pill tone="warning">中等</Pill>
            <H3>KifaMarketDataFetcher / FlowerWeatherFetcher — 重试覆盖不全</H3>
          </Row>
        </CardHeader>
        <CardBody>
          <Stack gap={12}>
            <Callout tone="warning" title="影响">
              Silo 崩溃/网络中断时整批 KIFA 拍卖数据和天气数据丢失，无法写入 DataPool
            </Callout>
            <Table
              headers={['项目', '内容']}
              rows={[
                ['日志表现', '[ERR] 转发KIFA拍卖数据失败 / 写入天气数据到DataPool失败，OrleansMessageRejectionException + ConnectionFailedException'],
                ['根因', 'ExecuteGrainCallWithRetryAsync 只捕获 OrleansMessageRejectionException，未覆盖 ConnectionFailedException（Silo 进程崩溃/网络中断）'],
                ['修复方案', '新增 IsTransientOrleansException 判定方法，统一覆盖 OrleansMessageRejectionException、ConnectionFailedException 及内部异常包装'],
                ['修改文件', 'KifaMarketDataFetcher.cs, FlowerWeatherFetcher.cs'],
              ]}
            />
          </Stack>
        </CardBody>
      </Card>

      <Card>
        <CardHeader>
          <Row gap={8}>
            <Pill tone="info">已有防护</Pill>
            <H3>GameConnection.SendAsync — 向已关闭连接写入</H3>
          </Row>
        </CardHeader>
        <CardBody>
          <Stack gap={8}>
            <Text>日志表现：Writing is not allowed after writer was completed / SocketException (10054)</Text>
            <Callout tone="info" title="结论">
              代码已有完善的 _sendLock 串行化 + IsFatalSendException 分类 + MarkAsBroken 机制，日志中的告警是正常防护行为，无需额外修复。
            </Callout>
          </Stack>
        </CardBody>
      </Card>

      <Divider />

      <H2>验证结果</H2>
      <Table
        headers={['检查项', '结果']}
        rows={[
          ['dotnet build Horizon.Game.Gateway', '0 个错误，106 个警告（均为既有 nullable 警告）'],
          ['修复文件数', '3 个（KifaMarketDataFetcher.cs, FlowerWeatherFetcher.cs, PlayerDespawnScheduler.cs）'],
          ['重试策略', '最多 3 次，指数退避（Fetcher: 2s→4s→8s / Despawn: 1s→2s→4s）'],
          ['异常覆盖', 'OrleansMessageRejectionException + ConnectionFailedException + OrleansException 内部包装'],
        ]}
        rowTone={['success', undefined, undefined, undefined]}
      />

      <Text tone="secondary" size="small">报告生成时间：2026-07-27 · 混沌世界游戏网关日志分析</Text>
    </Stack>
  );
}
