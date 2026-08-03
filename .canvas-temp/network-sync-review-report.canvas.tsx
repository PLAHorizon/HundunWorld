import { Stack, Row, Grid, H1, H2, H3, Text, Divider, Stat, Table, Callout, Pill, Card, CardHeader, CardBody } from 'qoder/canvas';

export default function NetworkSyncReviewReport() {
  return (
    <Stack gap={24}>
      <Stack gap={4}>
        <H1>MMORPG 网络同步审查报告</H1>
        <Text tone="secondary">混沌世界 · 远程角色不可见 / 闪现 / 闪移 / 莫名离线 BUG 修复</Text>
      </Stack>

      <Grid columns={4} gap={12}>
        <Stat value="3" label="审查系统" />
        <Stat value="2" label="修复 BUG" tone="warning" />
        <Stat value="7" label="确认无问题" tone="success" />
        <Stat value="0" label="编译错误" tone="success" />
      </Grid>

      <Divider />

      <H2>审查的系统</H2>
      <Table
        headers={['系统', '文件', '职责']}
        rows={[
          ['SnapshotApplySystem', 'Horizon.Game.ECS.Arch/Systems/SnapshotApplySystem.cs', '快照应用：Spawn/Update/Despawn 写入 ECS'],
          ['InterpolationSystem', 'Horizon.Game.ECS.Arch/Systems/InterpolationSystem.cs', '远程实体位置插值，平滑网络抖动'],
          ['FlaxActorSyncSystem', 'HundunWorld/Source/Game/FlaxActorSyncSystem.cs', 'ECS 插值位置 → Flax Actor 视觉桥接'],
        ]}
      />

      <Divider />

      <H2>发现并修复的 BUG</H2>

      <Card>
        <CardHeader>
          <Row gap={8}>
            <Pill tone="danger">闪移</Pill>
            <H3>BUG 1: 远程角色移动卡顿/闪移</H3>
          </Row>
        </CardHeader>
        <CardBody>
          <Stack gap={8}>
            <Callout tone="danger" title="根因">
              InterpolationSystem 中实体追赶上目标位置后（distSq ≈ 0）完全静止等待下一个快照。LastVelocityXZ_X/Y 已在 HandleUpdate 中采集但从未被使用，导致"走一步停一步"的卡顿感，快照到达时位置突变表现为闪移。
            </Callout>
            <Table
              headers={['项目', '内容']}
              rows={[
                ['修复方案', '添加 Dead Reckoning 惯性外推：使用最后已知速度做指数衰减外推'],
                ['参数', 'decayRate=3（0.5s 后衰减到 22%），maxExtrapolation=0.5s'],
                ['修改文件', 'InterpolationSystem.cs'],
              ]}
            />
          </Stack>
        </CardBody>
      </Card>

      <Card>
        <CardHeader>
          <Row gap={8}>
            <Pill tone="warning">不可见</Pill>
            <H3>BUG 2: 进入游戏时观测不到远程角色</H3>
          </Row>
        </CardHeader>
        <CardBody>
          <Stack gap={8}>
            <Callout tone="warning" title="根因">
              FlaxActorSyncSystem 的 ReconcileMissingActors 补偿间隔为 120 帧（≈ 2 秒）。新玩家进入游戏后首批 Spawn 事件若因时序竞态被错过，需等待 2 秒才能补创建 Actor，表现为"进入场景后看不到其他玩家"。
            </Callout>
            <Table
              headers={['项目', '内容']}
              rows={[
                ['修复方案', '补偿间隔从 120 帧缩短到 30 帧（≈ 0.5 秒）'],
                ['效果', '不可见窗口从 2 秒缩短到 0.5 秒'],
                ['修改文件', 'FlaxActorSyncSystem.cs'],
              ]}
            />
          </Stack>
        </CardBody>
      </Card>

      <Divider />

      <H2>已确认无问题的部分</H2>
      <Table
        headers={['检查项', '状态']}
        rows={[
          ['HandleSpawn 本地玩家保护（不销毁重建）', '通过'],
          ['远程实体重复 Spawn 转插值目标更新', '通过'],
          ['Yaw 环绕使用弧度制 MathF.PI 最短路径归一化', '通过'],
          ['FlaxActorSyncSystem 朝向来源从 interp.Yaw 读取', '通过'],
          ['事件订阅竞态：SubscribeToSnapshotEvents 后立即补创建', '通过'],
          ['本地玩家 Despawn 保护（AOI 离开视野不销毁）', '通过'],
          ['远程实体状态机（Active→Idle→Stale→TimeoutDespawn, 90s）', '通过'],
        ]}
        rowTone={['success', 'success', 'success', 'success', 'success', 'success', 'success']}
      />

      <Divider />

      <H2>验证结果</H2>
      <Grid columns={2} gap={12}>
        <Stat value="0 错误" label="编译结果" tone="success" />
        <Stat value="2 个文件" label="修改范围" />
      </Grid>

      <Text tone="secondary" size="small">审查时间：2026-07-29 · 混沌世界 MMORPG 网络同步系统</Text>
    </Stack>
  );
}
