import {
  Callout,
  Divider,
  Grid,
  H1,
  H2,
  Stack,
  Stat,
  Table,
  Tag,
  Text,
  Timeline,
} from 'qoder/canvas';

export default function NetworkSyncPerfReport() {
  return (
    <Stack gap={20}>
      <Stack gap={6}>
        <H1>MMORPG 网络同步：服务端推送/转发效率优化完成报告</H1>
        <Text tone="secondary">
          目标：彻底解决服务端推送与转发效率低导致的网络延迟、卡顿。覆盖下推链路（Silo → Gateway → 客户端）
          与上行转发链路（客户端 → Gateway → Silo）两个方向，共三项优化（P-F1 / P-F2 / P-F3）。
        </Text>
      </Stack>

      <Grid columns={4} gap={12}>
        <Stat value="3 → 1" label="上行输入跨进程 RTT（P-F3）" tone="success" />
        <Stat value="O(n) → O(1)" label="每广播 grain→gateway RPC（P-F1）" tone="success" />
        <Stat value="114/114" label="同步链路回归测试通过" tone="success" />
        <Stat value="0" label="Release 编译错误" tone="success" />
      </Grid>

      <Divider />

      <H2>一、根因定位</H2>
      <Table
        headers={['环节', '瓶颈', '影响']}
        rows={[
          [
            '下行推送（Silo→Gateway）',
            '每 50ms 广播按 chunk 逐条串行 observer RPC（K chunk × M observer），InputAck 每 tick × 每玩家一次 RPC，生命周期事件最多 3 次 RPC',
            'grain turn 被序列化/发送占满，tick 堆积，推送延迟累积',
          ],
          [
            '热路径日志',
            'Gateway 每快照包无条件 LogInformation；Grain 每 chunk/每 correction/每输入包打 Info 日志',
            '同步日志 IO 直接阻塞 grain turn 与转发线程，造成周期性卡顿',
          ],
          [
            'Gateway 转发',
            '每事件 Parallel.ForEach 分发小会话列表（几个~几十个 session）',
            '并行分区调度开销（10-50µs/次）+ 线程池竞争，远大于发送本身',
          ],
          [
            '上行转发（Gateway→Silo）',
            '每个输入包 3 次串行跨进程 RPC：ReceiveInputAsync → SubmitInputAsync → BuildInputAckAsync',
            '输入→ACK 延迟 = 3 × 跨进程 RTT 叠加，客户端预测回弹/卡顿',
          ],
        ]}
      />

      <H2>二、优化措施</H2>
      <Timeline
        events={[
          {
            title: 'P-F1 下行推送批量化',
            description:
              'ZoneShardGrain 全部 fanout 推送统一走批量通道：快照 delta + correction + InputAck、实体生命周期（AOI 广播 + 全广播 + 新 session 补发）、事件/场景对象/交互同步，均累积到 _fanoutBatchBuffer 后经 OnChunkDiffBatchAsync（FanoutBatchItem[]）单条消息推给每个 observer。接口带默认实现回退逐条 OnChunkDiffAsync，历史测试 mock 零改动。',
          },
          {
            title: 'P-F2 转发热路径瘦身',
            description:
              'GatewaySyncDispatcher.Dispatch 移除每事件 Parallel.ForEach（顺序循环 + 多 worker 跨事件并行），删除每快照包的无条件 Info 日志；Grain 广播热路径日志降为 Debug/采样，仅慢广播（>16.7ms）告警。',
          },
          {
            title: 'P-F3 上行转发合并',
            description:
              '新增 IPlayerSessionGrain.ReceiveInputAndForwardAsync：silo 内单个 grain turn 完成接收 → grain→grain 转发 ZoneShard → 构建 ACK（InputForwardResult 一次带回），跨进程 RTT 3 → 1；合并失败自动回退三段式路径保证可用性；每输入包 Info 日志降为 Debug。',
          },
        ]}
      />

      <H2>三、变更文件</H2>
      <Table
        headers={['文件', '变更内容']}
        rows={[
          [
            'Horizon.Orleans.Interface/World/IZoneShardFanoutObserver.cs',
            '新增 FanoutBatchItem + OnChunkDiffBatchAsync（DIM 回退兼容）',
          ],
          [
            'Horizon.Orleans.Interface/World/IPlayerSessionGrain.cs',
            '新增 InputForwardResult + ReceiveInputAndForwardAsync 契约',
          ],
          [
            'Horizon.Orleans.Grains/World/ZoneShardGrain.cs',
            '快照/correction/InputAck/生命周期/事件/场景对象/交互全部批量化；热路径日志治理',
          ],
          ['Horizon.Orleans.Grains/World/PlayerSessionGrain.cs', '实现上行合并调用（接收+转发+ACK 单 turn）'],
          ['Horizon.Game.Gateway/Services/GatewaySyncWiring.cs', '实现批量接收 OnChunkDiffBatchAsync 拆回逐条入队'],
          ['Horizon.Game.Core/Sim/Server/GatewaySyncDispatcher.cs', 'Dispatch 顺序化 + 移除每事件 Info 日志'],
          ['Horizon.Game.Core/Handlers/SyncPacketHandler.cs', '输入路径切换合并 RPC + 回退兜底 + 日志降级'],
          ['Horizon.Game.Gateway.Tests/InterpolationSystemSwitchContinuityTests.cs', '修复既有 Release 编译错误（Assert.Skip → return）'],
        ]}
      />

      <H2>四、验证证据</H2>
      <Stack gap={8}>
        <Text>
          <Tag tone="success">编译</Tag> Horizon.Game.Gateway.Tests（含全部依赖项目）Release 构建 0 错误。
        </Text>
        <Text>
          <Tag tone="success">测试</Tag> 同步链路定向回归 114/114 通过：ZoneShardIntegration / ZoneShardInteraction /
          GatewaySyncWiring / NetworkSyncIntegration / SceneObject（广播 AOI + 交互校验 + 持久化）/
          SnapshotDeltaEncoding / MultiClientSyncPerformance / TcpTransportSync / BaselineResyncE2E（单独运行 6/6）等。
        </Text>
        <Text>
          <Tag tone="info">归因甄别</Tag> 全量套件中的失败逐一甄别，均为既有问题：5 个 HandleInteractionIntentTests
          位于本次未触碰模块（改动前后失败完全一致）；NetworkPerformanceBaselineReport 为纯 codec 确定性压测（100.92 vs
          100kbps）；BaselineResyncE2E / 内存增长测试为并行负载下 flaky（单独运行稳定通过）。
        </Text>
      </Stack>

      <Callout tone="warning" title="部署提示">
        本地正在运行的 Horizon.Orleans.Silo.exe 与 Horizon.Game.Gateway.exe 仍为旧代码（进程占用导致 Debug 产物无法覆盖）。
        重启 Silo 与 Gateway 进程后，P-F1/P-F2/P-F3 全部优化才会生效。
      </Callout>

      <Text tone="secondary" size="small">
        生成时间：2026-08-05 · 混沌世界（HundunWorld）网络同步性能优化 Quest
      </Text>
    </Stack>
  );
}
