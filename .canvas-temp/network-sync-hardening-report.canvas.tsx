import { Stack, Row, Grid, H1, H2, H3, Text, Table, Stat, Divider, Tag, Callout, Progress } from 'qoder/canvas';

export default function NetworkSyncHardeningReport() {
  return (
    <Stack gap={24}>
      <H1>网络同步加固完成报告</H1>
      <Text tone="secondary">
        按照"04-网络同步与多人游戏"文档 §12 生产环境风险 和 §14 后续研发路线，系统性加固自研网络同步代码（TouchSocket + MemoryPack + Orleans + Arch ECS）。
      </Text>

      <Grid columns={4} gap={12}>
        <Stat value="6" label="加固项完成" tone="success" />
        <Stat value="9" label="新增测试通过" tone="success" />
        <Stat value="1943" label="总测试通过" tone="success" />
        <Stat value="0" label="编译错误" tone="success" />
      </Grid>

      <Divider />

      <H2>本轮完成的加固项</H2>

      <H3>1. ZoneShardGrain 动态 RTT 阈值集成</H3>
      <Table
        headers={['改动', '说明']}
        rows={[
          ['SimulatedEntity.EstimatedRttMs', '新增字段，存储 EMA 平滑后的单向延迟估算'],
          ['SubmitInputAsync EMA 计算', 'α=0.3，从 ClientTick（Unix ms）估算延迟，过滤异常值'],
          ['TickAsync → Validate(7参数)', '将 entity.EstimatedRttMs 传入 MovementValidator 动态放宽阈值'],
          ['公式', 'effectiveEpsilon = PositionEpsilon + RttScalingFactor × rttMs（上限 MaxDynamicEpsilon）'],
        ]}
      />

      <H3>2. 断线重连全缓冲区重置增强</H3>
      <Table
        headers={['组件', '新增方法', '作用']}
        rows={[
          ['CorrectionReceiveBuffer', 'Clear()', '清空旧修正包，避免重连后无意义吸附'],
          ['InputAckReceiveBuffer', 'Clear()', '清空旧 ACK，避免错误清理新会话输入历史'],
          ['ReconciliationSystem', 'ResetState()', '重置风暴检测/冷却/统计，新会话从零开始'],
          ['ClearLocalEntitiesOnDisconnect', '补全 6 个缓冲区', 'InputHistory + InputSendQueue + EventReceive + Correction + InputAck + Snapshot'],
        ]}
      />

      <H3>3. 网络同步加固测试套件（9 个测试）</H3>
      <Table
        headers={['测试', '覆盖']}
        rows={[
          ['MovementValidator_HighRtt_RelaxesThreshold', 'RTT=200ms 放宽阈值容纳 drift'],
          ['MovementValidator_RttScaling_CappedAtMaxDynamicEpsilon', '阈值上限不超过 MaxDynamicEpsilon'],
          ['ZoneShardGrain_SubmitInput_EstimatesRttViaEma', 'EMA 首次赋值约等于实际延迟'],
          ['SnapshotReceiveBuffer_Overflow_DropsOldest', '队列满时 DropOldest'],
          ['InputSendQueue_Overflow_DropsOldest', '队列满时 DropOldest'],
          ['EventReceiveBuffer_Overflow_DropsOldest', '队列满时 DropOldest'],
          ['DisconnectReset_AllBuffersCleared', '6 个缓冲区全部清空'],
          ['Reconnect_StaleAck_DoesNotCorruptNewSessionHistory', '旧 ACK 不污染新会话'],
          ['ZoneShardGrain_HighLatencyClient_NoCorrectionWithRttRelaxation', '端到端高延迟无修正'],
        ]}
      />

      <Divider />

      <H2>短期路线完成状态</H2>
      <Table
        headers={['行动项', '状态', '备注']}
        rows={[
          ['补全 Reconciliation 集成测试', '✅ 完成', '9 个新测试'],
          ['MovementValidator 动态阈值', '✅ 完成', 'epsilon = base + k × RTT'],
          ['TCP 发送退避优化', '✅ 完成', '指数退避 5ms→200ms'],
          ['修正风暴抑制 + 平滑修正', '✅ 完成', '2s/5次→1s冷却 + lerp'],
          ['缓冲区溢出保护', '✅ 完成', 'DropOldest 策略'],
          ['断线重连重置增强', '✅ 完成', '6 缓冲区 + 系统状态'],
          ['CI/CD DLL 部署自动化', '⬜ 待定', 'DevOps 项，非代码'],
        ]}
        rowTone={[undefined, undefined, undefined, undefined, undefined, undefined, 'secondary']}
      />

      <Divider />

      <H2>修改文件清单</H2>
      <Table
        headers={['文件', '操作']}
        rows={[
          ['Horizon.Orleans.Grains/World/ZoneShardGrain.cs', '编辑：RTT EMA + Validate 7参数'],
          ['Horizon.Game.ECS.Arch/Network/CorrectionReceiveBuffer.cs', '编辑：+Clear()'],
          ['Horizon.Game.ECS.Arch/Network/InputAckReceiveBuffer.cs', '编辑：+Clear()'],
          ['Horizon.Game.ECS.Arch/Systems/ReconciliationSystem.cs', '编辑：+ResetState()'],
          ['HundunWorld/Source/Game/HundunWorldGame.cs', '编辑：补全断线清理'],
          ['Horizon.Game.Gateway.Tests/NetworkSyncHardeningTests.cs', '新建：9 个加固测试'],
        ]}
      />

      <Callout tone="info" title="中期路线（待后续规划）">
        多 ZoneShard 分片、动态 AOI LOD、带宽自适应、服务端 GroundHeightSampler 属于架构级变更，需独立设计。
      </Callout>

      <Text tone="secondary" size="small">
        验证环境：.NET 10 / xUnit / Moq / Orleans TestingHost | 1943 通过 / 3 预先存在的无关失败
      </Text>
    </Stack>
  );
}
