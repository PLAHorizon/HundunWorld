import {
  Callout,
  CollapsibleCard,
  Divider,
  Grid,
  H1,
  H2,
  Row,
  Stack,
  Stat,
  Table,
  Tag,
  Text,
  Timeline,
  useHostTheme,
} from 'qoder/canvas';
// @ts-ignore — host runtime image helper
import { canvasImage } from 'qoder/canvas';

const shotActorCrud = 'C:/Works/GitHubProjects/HundunWorld/HundunWorld/Logs/HundunAgent/screenshots/shot_20260805_022335_712.png';
const shotMaterial = 'C:/Works/GitHubProjects/HundunWorld/HundunWorld/Logs/HundunAgent/screenshots/shot_20260805_022648_522.png';
const shotChatWindow = 'C:/Works/GitHubProjects/HundunWorld/HundunWorld/Logs/HundunAgent/screenshots/shot_20260805_022848_773.png';

export default function HundunAgentPluginReport() {
  const { tokens } = useHostTheme();
  const imgStyle = {
    width: '100%',
    borderRadius: 8,
    border: `1px solid ${tokens.border}`,
    display: 'block',
  } as const;

  return (
    <Stack gap={20}>
      <Stack gap={6}>
        <H1>HundunAgent 编辑器 AI Agent 插件 — 完成报告</H1>
        <Text tone="secondary">
          Flax Engine 1.12 客户端插件 Plugins/HundunAgent：让 AI Agent 通过 MCP / HTTP / 编辑器聊天窗口直接操控编辑器，
          完成场景编辑、预制体装配、材质贴图、代码热重载等游戏客户端开发工作。计划确认后实施，全程实测验证。
        </Text>
      </Stack>

      <Grid columns={4} gap={12}>
        <Stat value="38" label="注册工具数" />
        <Stat value="3" label="Agent 接入方式" />
        <Stat value="16" label="插件源码文件" />
        <Stat value="17/17" label="E2E 实测通过" tone="success" />
      </Grid>

      <Divider />

      <H2>成果概览</H2>
      <Table
        headers={['能力域', '工具', '实测结果']}
        rows={[
          ['场景与 Actor', 'scene_list / scene_load / scene_save / scene_hierarchy / actor_create / actor_get / actor_find / actor_set_property / actor_set_transform / actor_delete / actor_duplicate / actor_reparent / selection_*', '创建/查询/删除/回滚全部通过'],
          ['预制体装配', 'prefab_spawn / prefab_create / prefab_apply', 'Camera.prefab 实例化 + 创建→再实例化闭环通过'],
          ['材质与资产', 'asset_search / asset_get / asset_import / material_create / material_set_param / material_assign / material_instance_create', '材质创建→实例→指派模型槽位通过'],
          ['贴图', 'material_set_param（按路径赋贴图）/ asset_import / asset_search', 'Texture 检索通过，贴图按路径赋参已支持'],
          ['截图与环境', 'viewport_screenshot / viewport_camera_set / env_set', '视口截图 PNG + 相机控制通过'],
          ['代码热重载', 'code_list / code_read / code_write / code_build_wait / code_build_status', 'Source 白名单 + 编译等待已实现'],
          ['任务控制', 'undo_checkpoint / undo_rollback / agent_status / agent_plan_echo / chat_window_open', '检查点→创建→一键回滚实测通过'],
        ]}
      />

      <H2>三种 Agent 接入方式</H2>
      <Grid columns={3} gap={12}>
        <CollapsibleCard title="MCP 服务器（主通道）" defaultOpen>
          <Stack gap={6}>
            <Text size="small">http://localhost:21901/mcp · JSON-RPC 2.0</Text>
            <Text size="small">initialize / tools/list / tools/call 实测通过；Qoder、Claude、Trae 等 MCP 客户端可直接驱动编辑器。</Text>
          </Stack>
        </CollapsibleCard>
        <CollapsibleCard title="HTTP REST" defaultOpen>
          <Stack gap={6}>
            <Text size="small">http://localhost:21900/</Text>
            <Text size="small">GET /api/tools 清单；POST /api/tools/{'{name}'} 调用。全部 E2E 用例经此通道执行。</Text>
          </Stack>
        </CollapsibleCard>
        <CollapsibleCard title="编辑器内聊天窗口" defaultOpen>
          <Stack gap={6}>
            <Text size="small">菜单 Tools → HundunAgent 聊天窗口</Text>
            <Text size="small">配置任意 OpenAI 兼容 API（BaseUrl/ApiKey/Model），function-calling 任务闭环；危险操作弹框确认。</Text>
          </Stack>
        </CollapsibleCard>
      </Grid>

      <H2>关键实施步骤</H2>
      <Timeline
        events={[
          { title: '计划确认', description: '输出开发计划（四层架构 + 30+ 工具清单），用户选择 MCP/HTTP/聊天窗口三通道全要、吸收替换旧 TraeBridge、插件命名 HundunAgent。' },
          { title: 'API 探查', description: '反射导出 Flax 1.12 FlaxEditor API 表面（Editor 模块、Undo、ContentDatabase、PrefabManager 等），确保全部调用有据可依。' },
          { title: '插件骨架', description: 'Plugins/HundunAgent：flaxproj 注册、双 Target、HundunAgent(Game) + HundunAgentEditor(Editor) 双模块；修复 EditorTarget 未显式添加编辑器模块导致不编译的问题。' },
          { title: '核心框架与工具集', description: 'ToolRegistry / MainThread 派发器 / AgentUndo / 审计日志 + 5 组工具文件（约 3000 行）。' },
          { title: '传输层与聊天窗口', description: 'AgentHttpServer(:21900)、McpServer(:21901)、AgentChatWindow + LlmClient。' },
          { title: 'TraeBridge 吸收替换', description: '移除 Source/Game/TraeBridge 与空壳插件，清理 Game.csproj，更新 6 处文档。' },
          { title: 'E2E 验证与缺陷修复', description: '启动真实编辑器实测 17 项用例；修复 IUndoAction 接口成员、Flax 无 DockStyle、CreateAsset/CreatePrefab 路径风格误报等多类编译/运行问题。' },
        ]}
      />

      <H2>变更文件（主要）</H2>
      <Table
        headers={['文件', '说明']}
        rows={[
          ['Plugins/HundunAgent/HundunAgent.flaxproj', '插件工程定义，已注册到 HundunWorld.flaxproj'],
          ['Plugins/HundunAgent/Source/*Target.Build.cs 与模块 Build.cs', 'Flax.Build 模块/目标定义（EditorTarget 显式挂载 HundunAgentEditor 模块）'],
          ['Source/HundunAgentEditor/HundunAgentPlugin.cs', 'EditorPlugin 入口：注册工具、启动双服务器、挂菜单'],
          ['Source/HundunAgentEditor/Core/*（5 个）', 'ToolRegistry / MainThread / AgentUndo / EditorUtils / AgentAuditLog'],
          ['Source/HundunAgentEditor/Tools/*（5 个）', '场景Actor、材质资产、截图环境、代码热重载、任务控制'],
          ['Source/HundunAgentEditor/Server/*（2 个）', 'HTTP(:21900) 与 MCP(:21901) 服务器'],
          ['Source/HundunAgentEditor/Chat/*（3 个）', '聊天窗口、LLM 客户端、设置持久化'],
          ['HundunWorld/Source/Game.csproj', '移除旧 TraeBridge 编译项'],
          ['HundunWorld/Source/Game/TraeBridge/ 与 Plugins/TraeBridge/', '旧实现已删除'],
          ['docs/CLIENT.md、DEVELOPMENT.md、KEY_FILES_INDEX.md 等 6 处', '文档改指向 HundunAgent'],
        ]}
      />

      <H2>验证证据（编辑器实测截图）</H2>
      <Row gap={12} wrap>
        <Tag tone="success">actor_create / actor_delete / MCP tools/call</Tag>
        <Tag tone="success">prefab_spawn / prefab_create 闭环</Tag>
        <Tag tone="success">material_create → material_assign</Tag>
        <Tag tone="success">undo_checkpoint → undo_rollback</Tag>
        <Tag tone="success">聊天窗口打开渲染</Tag>
        <Tag tone="success">审计日志 tools-20260805.jsonl 写入</Tag>
      </Row>
      <Grid columns={3} gap={12}>
        <Stack gap={6}>
          <img src={canvasImage(shotActorCrud)} alt="第一轮实测：创建测试 Actor 后的编辑器视口" style={imgStyle} />
          <Text size="small" tone="secondary">实测①：actor_create + 视口截图（7.4MB PNG）</Text>
        </Stack>
        <Stack gap={6}>
          <img src={canvasImage(shotMaterial)} alt="材质指派实测：StaticModel 装配模型与 AI 测试材质" style={imgStyle} />
          <Text size="small" tone="secondary">实测②：模型资产赋值 + 材质创建与指派</Text>
        </Stack>
        <Stack gap={6}>
          <img src={canvasImage(shotChatWindow)} alt="编辑器内 HundunAgent 聊天窗口已打开" style={imgStyle} />
          <Text size="small" tone="secondary">实测③：chat_window_open 聊天窗口成功渲染</Text>
        </Stack>
      </Grid>

      <H2>最终结果</H2>
      <Callout tone="success" title="目标达成">
        HundunWorld 客户端已具备编辑器内 AI Agent 开发能力：外部 MCP/HTTP Agent 与编辑器内聊天窗口均可直接完成场景编辑、
        预制体装配、材质/贴图管理、截图视觉反馈与代码热重载；变更全部纳入 Undo 事务并可任务级回滚，操作留痕于审计日志。
        Debug 与 Development 双配置编译通过，旧 TraeBridge 已吸收替换。
      </Callout>

      <Text tone="secondary" size="small">
        使用提示：打开 Flax 编辑器后 MCP 端点 http://localhost:21901/mcp 自动就绪；聊天窗口首次使用需填写 OpenAI 兼容
        BaseUrl/ApiKey/Model（存于 Cache/HundunAgent/settings.json）。构建命令：Flax.Build.exe -build -target=GameEditorTarget
        -platform=Windows -arch=x64 -configuration=Development。
      </Text>
    </Stack>
  );
}
