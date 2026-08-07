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

export default function GengDiImCallFeatureReport() {
  return (
    <Stack gap={20}>
      <Stack gap={6}>
        <H1>GengDi IM 语音/视频通话功能 · 完成报告</H1>
        <Text tone="secondary">
          最高约束：不得破坏现有功能。信令复用 IM 网关长连接与 Orleans 链路（纯增量），媒体走客户端间 UDP
          直连；现有文本聊天、会话、登录态、鉴权、通知链路零改动。
        </Text>
      </Stack>

      <Grid columns={4} gap={12}>
        <Stat value="6 / 6" label="受影响工程构建通过" tone="success" />
        <Stat value="30 / 30" label="新增单元测试通过" tone="success" />
        <Stat value="17" label="新增/修改文件" />
        <Stat value="0" label="新增 NuGet 依赖" />
      </Grid>

      <Divider />

      <H2>功能覆盖</H2>
      <Table
        headers={['需求项', '覆盖内容', '实现要点']}
        rows={[
          [
            '通话状态流转',
            '发起 / 接听 / 拒绝 / 取消 / 忙线 / 超时 / 挂断 / 异常断开 / 重连',
            'CallStateMachine 纯逻辑状态机过滤乱序信令；主叫 30s、被叫 45s、服务端 90s 三级超时；媒体看门狗 15s 警告、30s 判失联；15s KeepAlive 随 IM 自动重连续传',
          ],
          [
            '语音聊天',
            '麦克风采集、播放、静音/取消静音、设备异常提示',
            'NAudio WASAPI 采集 → 自研零依赖线性重采样（16kHz 单声道 PCM16）；抖动缓冲播放、积压 >500ms 自动裁剪控延迟；无麦克风/无声卡/采集中断均 Toast 提示',
          ],
          [
            '视频聊天',
            '摄像头采集、本地预览、远端画面、开/关摄像头、画面异常提示',
            '手写 DirectShow COM 互操作（零第三方依赖）；320×240 JPEG ≈5fps 自动分片重组；本地画中画预览；MediaState 信令双端同步静音/摄像头状态；无摄像头/被占用/解码失败降级提示且语音不中断',
          ],
          [
            '忙线判定',
            '被叫通话中再被呼叫返回忙线',
            'IMUserGrain 瞬态通话状态 + Busy 应答，客户端双重兜底；客户端崩溃/断电后由服务端懒清理释放忙线',
          ],
          [
            '兼容性',
            '文本聊天、会话列表、登录态、权限、通知不受影响',
            '全部为增量改动；信令复用现有 AuthToken 鉴权并校验信令发送者身份防伪造',
          ],
        ]}
      />

      <H2>关键步骤</H2>
      <Timeline
        events={[
          {
            id: 'step1',
            timestamp: '阶段 1',
            title: '梳理现有 IM 链路',
            description:
              '摸清协议层（IMMessageType/Union）、网关 Handler 反射注册、Orleans Grain 观察者推送、客户端长连接请求-响应关联与事件分发，确定纯增量接入边界。',
          },
          {
            id: 'step2',
            timestamp: '阶段 2',
            title: '协议层：CallSignal/CallSignalAck',
            description:
              '新增 IMServiceType.Call=7、消息类型 600/601、Union 72/73 与信令/结束原因枚举，全部追加式注册，旧客户端收到未知 Union 仅丢弃该帧。',
          },
          {
            id: 'step3',
            timestamp: '阶段 3',
            title: '服务端：IMCallHandler + Grain 忙线状态',
            description:
              '新 Handler 做身份校验与转发；IMUserGrain 维护瞬态通话会话并按序推送给网关订阅，不落库。',
          },
          {
            id: 'step4',
            timestamp: '阶段 4',
            title: '客户端通话子系统',
            description:
              'CallService 编排（状态机/超时/保活/看门狗）、UDP 媒体传输（分片重组+会话哈希过滤）、AudioCallEngine、DirectShow 采集、VideoCallEngine。',
          },
          {
            id: 'step5',
            timestamp: '阶段 5',
            title: '通话 UI 与集成',
            description:
              'CallWindow 独立窗口（呼叫/来电/通话中/视频画面），CallWindowHost 全局托管；聊天头部语音/视频占位按钮接线，仅私聊可用。',
          },
          {
            id: 'step6',
            timestamp: '阶段 6',
            title: '测试与构建验证',
            description:
              '30 个新测试（含真实 UDP 回环音频往返与 8KB 视频帧分片重组）全部通过；6 个受影响工程逐一构建 0 错误；存量测试失败项确认为改动前已存在。',
          },
        ]}
      />

      <H2>变更文件</H2>
      <Table
        headers={['层级', '文件', '变更性质']}
        rows={[
          ['协议', 'Horizon.IM.Message/Network/IMCallMessages.cs', '新增'],
          ['协议', 'IMMessageUnion.cs / IMMessageType.cs / IMServiceType.cs / IMEnums.cs', '增量修改'],
          ['服务端', 'Horizon.IM.Core/Handlers/IMCallHandler.cs', '新增（反射自动注册）'],
          ['服务端', 'Horizon.Orleans.Interface/IIMGrains.cs、Orleans.Grains/IMUserGrain.cs', '新增 Grain 方法'],
          ['客户端·通话', 'Core/Services/Call/（CallService、CallStateMachine、CallMediaTransport、AudioCallEngine、VideoCallEngine、DirectShowVideoCapture、CallModels）', '新增 7 个文件'],
          ['客户端·UI', 'Core/Views/CallWindow.axaml(.cs)、CallWindowHost.cs', '新增'],
          ['客户端·集成', 'ImGatewayContactClient.cs、SocialViewModel.cs、SocialView.axaml(.cs)、csproj', '仅新增成员/接线'],
          ['测试', 'GengDi.Tests/Social/CallStateMachineTests.cs、CallMediaTransportTests.cs', '新增 30 个测试'],
        ]}
      />

      <H2>验证证据</H2>
      <Stack gap={10}>
        <Table
          headers={['验证项', '结果']}
          rows={[
            ['Horizon.IM.Message / IM.Core / Orleans.Grains / IM.Gateway / Orleans.Silo / Game.GengDi 构建', '全部 BUILD OK，0 错误'],
            ['CallStateMachineTests（24）', '通过：全状态流转、乱序/过期信令过滤、忙线拒绝、重置'],
            ['CallMediaTransportTests（6）', '通过：UDP 回环音频往返、视频分片重组、跨会话过滤、端点解析'],
            ['GengDi 存量测试套件', '104 通过；4 个失败为改动前已存在（头像位图/环境变量/链接解析，涉及模块未被触及）'],
            ['IM.Gateway.Tests 失败项', 'GameInfoDto 序列化器缺失的存量问题（工作区含其他未提交改动），与本次无关'],
          ]}
          rowTone={['success', 'success', 'success', undefined, undefined]}
        />
        <Stack gap={6}>
          <Text size="small" weight="medium">回归检查点</Text>
          <Text size="small" tone="secondary">
            文本私聊/群聊收发与回执 · 会话列表与未读计数 · 心跳/断线重连/离线补拉 · 群邀请与好友申请通知 ·
            账号切换后通话服务重绑定 · 忙线/超时/异常断开场景 · 静音与摄像头双端状态同步。
          </Text>
        </Stack>
      </Stack>

      <Callout tone="warning" title="已知限制与风险">
        <Stack gap={4}>
          <Text size="small">1. 媒体 UDP 为局域网直连模型，跨 NAT/公网需后续引入 TURN 中继或 ICE。</Text>
          <Text size="small">2. IIMUserGrain 接口追加方法：Orleans Silo 与 IM 网关需同批部署。</Text>
          <Text size="small">3. 旧版本客户端收到通话 Union（72/73）会丢弃该帧（已有异常兜底），不影响其余消息。</Text>
          <Text size="small">4. 视频约 30–80KB/s、5fps，可后续升级为硬件编码；暂不支持群通话（UI 已明确提示）。</Text>
        </Stack>
      </Callout>

      <Callout tone="success" title="最终结论">
        <Stack gap={6}>
          <Text>
            语音/视频通话功能已完整落地：信令（发起/接听/拒绝/取消/忙线/超时/挂断/异常断开/重连）、语音（采集/播放/静音/设备异常）、视频（采集/预览/远端画面/开关摄像头/画面异常）全部覆盖。
          </Text>
          <Text>
            以纯增量方式接入，现有文本聊天、消息收发、会话列表、登录态、鉴权、通知与界面交互保持兼容，构建与新测试全绿。
          </Text>
          <Tag tone="success">目标已完成</Tag>
        </Stack>
      </Callout>

      <Text tone="secondary" size="small">
        报告生成于 2026-08-05 · 工作区 c:\Works\GitHubProjects\HundunWorld
      </Text>
    </Stack>
  );
}
