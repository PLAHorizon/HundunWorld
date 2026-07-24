import {
  Stack, Row, Grid, H1, H2, H3, Text, Divider, Table, Stat, Tag, Pill,
  Progress, Callout, BarChart, Timeline, MetricsGrid, DocsSection,
  useHostTheme,
} from 'qoder/canvas';

export default function NarrativeProRewriteReport() {
  const { tokens } = useHostTheme();

  return (
    <Stack gap={24}>
      <H1>NarrativePro 插件重写报告</H1>
      <Text tone="secondary">
        UE5 → Flax Engine (C#) 完整移植分析 · 生成时间: 2026-07-24
      </Text>

      <Divider />

      <MetricsGrid
        columns={4}
        items={[
          { label: 'C# 源文件', value: '279', tone: 'info' },
          { label: '总代码量', value: '1,351', unit: 'KB' },
          { label: '子模块数', value: '30' },
          { label: '完成度', value: '12/14', tone: 'success', description: '2 个部分完成' },
        ]}
      />

      <Divider />

      <H2>核心重写策略</H2>
      <Table
        headers={['策略', 'UE5 原始方案', 'Flax 适配方案']}
        rows={[
          ['架构模式', '深层继承链 (AActor→APawn→ACharacter)', '组合优于继承 (Actor + Script)'],
          ['数据驱动', 'Blueprint / DataAsset', 'JSON 文件 + 工厂模式加载'],
          ['网络同步', 'UE5 Replication / RPC', 'TouchSocket + MemoryPack + 反射桥接'],
          ['类型引用', 'TSubclassOf<T> / UObject*', '字符串资源路径'],
          ['输入系统', 'Enhanced Input / UInputAction', 'Flax Input 系统 / 字符串占位'],
          ['语义保留', 'UE5 类名/方法名/事件名', '保持一致，降低认知成本'],
        ]}
      />

      <Divider />

      <H2>UE5 → Flax 类型映射</H2>
      <Table
        headers={['UE5 概念', 'Flax 适配', '说明']}
        rows={[
          ['AActor / APawn / ACharacter', 'Actor + Script', '无继承链，改为组合'],
          ['UPlayerController', 'Script（挂载到 Actor）', '无 PlayerController 基类'],
          ['UGameInstance', '单例 [Serializable] class', '无 GameInstance 基类'],
          ['UActorComponent', 'Script', 'Flax 组件即 Script'],
          ['TSubclassOf<T>', 'string 路径', '类型引用改为资源路径'],
          ['UAnimMontage*', 'string 路径', '动画资产引用改为路径'],
          ['UE5 复制/RPC', '本地逻辑 + 事件回调', '移除网络预测'],
          ['FGameplayTag', '自定义 GameplayTag 结构', '完整重写 Tag 系统'],
        ]}
      />

      <Divider />

      <H2>模块代码分布 (Top 10)</H2>
      <BarChart
        data={[
          { label: 'UnrealFramework', value: 163.5 },
          { label: 'Tales', value: 149.7 },
          { label: 'Items', value: 112.5 },
          { label: 'AI', value: 108.7 },
          { label: 'Vehicles', value: 104.6 },
          { label: 'GAS', value: 103.6 },
          { label: 'Navigation', value: 67.3 },
          { label: 'Core', value: 59.3 },
          { label: 'SaveSystem', value: 55.8 },
          { label: 'Interaction', value: 54.6 },
        ]}
        unit="KB"
      />

      <Divider />

      <H2>模块完成状态</H2>
      <Table
        headers={['模块', '状态', '关键文件', '说明']}
        rows={[
          ['插件核心', '✅ 已完成', 'NarrativeProPlugin.cs (209行)', '插件初始化/网络桥接'],
          ['任务系统', '✅ 已完成', 'Tales/Quest/Quest.cs (257行)', '状态机/分支/任务链'],
          ['对话系统', '✅ 已完成', 'Tales/Dialogue/Dialogue.cs (437行)', 'NPC/玩家对话树'],
          ['GAS能力系统', '🟡 部分完成', 'GAS/NarrativeGameplayAbility.cs', 'Cost/Cooldown待完善'],
          ['存档系统', '🟡 部分完成', 'SaveSystem/', '子系统待实现'],
          ['技能树', '✅ 已完成', 'SkillTrees/', 'TreePerk/TreeSkill'],
          ['物品系统', '✅ 已完成', 'Items/', '物品/碎片/弹药'],
          ['载具系统', '✅ 已完成', 'Vehicles/', '载具移动/交通灯'],
          ['时间系统', '✅ 已完成', 'TimeOfDay/', '昼夜循环'],
          ['音乐系统', '✅ 已完成', 'Music/', '动态音乐切换'],
          ['AI系统', '✅ 已完成', 'AI/', 'NPC活动/掩护/目标'],
          ['角色创建器', '✅ 已完成', 'CharacterCreator/', '外观/选项配置'],
          ['导航系统', '✅ 已完成', 'Navigation/', '寻路/标记'],
          ['交互系统', '✅ 已完成', 'Interaction/', '槽位/可交互物'],
        ]}
        rowTone={[undefined, undefined, undefined, 'warning', 'warning', undefined, undefined, undefined, undefined, undefined, undefined, undefined, undefined, undefined]}
      />

      <Divider />

      <H2>网络层重写架构</H2>
      <DocsSection title="三层解耦设计">
        <Stack gap={8}>
          <Text>1. 插件内部：NarrativeSyncManager 管理叙事状态序列化/反序列化与定时刷新（0.5s 间隔）</Text>
          <Text>2. 桥接层：NarrativeProPlugin 通过反射获取 Game 模块的 NarrativeProNetworkAdapter，避免硬依赖</Text>
          <Text>3. 适配器：NarrativeProNetworkAdapter 将叙事消息映射为 Horizon 协议 MessageType</Text>
          <Text>4. 传输：TouchSocket + MemoryPack + Arch ECS 统一链路</Text>
        </Stack>
      </DocsSection>

      <Callout tone="info" title="反射桥接模式">
        插件通过 Type.GetType() + BindingFlags 反射获取 HundunWorldGame.Instance.NetworkManager，
        再调用 GetHandler(Type) 获取适配器实例。这确保了插件模块与主项目的完全解耦。
      </Callout>

      <Divider />

      <H2>开发时间线</H2>
      <Timeline
        events={[
          {
            id: '1',
            timestamp: '2026-06-13',
            title: '插件初始引入',
            description: '新增耕地客户端 (d64436b)，NarrativePro 插件首次提交到仓库',
          },
          {
            id: '2',
            timestamp: '2026-06-19',
            title: 'Session Checkpoint',
            description: '开发会话检查点 (01fa818)',
          },
          {
            id: '3',
            timestamp: '2026-07-18',
            title: '网络同步 0.1',
            description: '角色准确上下线 (2269294)，网络桥接层初步完成',
          },
          {
            id: '4',
            timestamp: '2026-07-23',
            title: '角色上线/离线固定',
            description: '固定角色上线、离线逻辑 (c036354)，最新提交',
          },
        ]}
      />

      <Divider />

      <H2>待完善项 (TODO)</H2>
      <Table
        headers={['模块', '待办事项']}
        rows={[
          ['GAS', 'Cost/Cooldown 资产加载系统待完善'],
          ['GAS', '接入 Flax InputEvent 配置'],
          ['UnrealFramework', '子系统初始化/释放系统'],
          ['Interaction', '会话绑定校验、interactableId 存在性校验'],
          ['SaveSystem', '子系统存档接口待实现'],
        ]}
        rowTone={['warning', 'warning', 'warning', 'warning', 'warning']}
      />

      <Divider />

      <H2>总结</H2>
      <DocsSection title="重写评价">
        <Stack gap={8}>
          <Text>
            NarrativePro 插件的重写是一次从 UE5 C++ 到 Flax C# 的完整移植，覆盖 30 个子模块、279 个源文件。
            核心策略为：去 UE5 依赖、组合优于继承、数据驱动（JSON）、网络解耦（反射桥接）、语义保留。
          </Text>
          <Text>
            14 个主要功能模块中 12 个已完成，2 个部分完成（GAS Cost/Cooldown、SaveSystem 子系统）。
            整体架构清晰，模块边界明确，网络层通过反射桥接实现了插件与主项目的完全解耦。
          </Text>
        </Stack>
      </DocsSection>

      <Text tone="secondary" size="small">
        报告基于源码阅读、文档交叉验证（CLIENT_FEATURES.md / NETCODE.md）及 Git 历史分析生成。
      </Text>
    </Stack>
  );
}
