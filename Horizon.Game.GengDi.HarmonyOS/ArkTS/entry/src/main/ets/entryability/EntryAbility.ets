import { UIAbility, AbilityConstant, Want } from '@kit.AbilityKit';
import { window } from '@kit.ArkUI';
import { hilog } from '@kit.PerformanceAnalysisKit';
import { abilityAccessCtrl, Permissions } from '@kit.AbilityKit';
import { AppContext } from '../model/AppContext';
import { ImLocalStore } from '../services/im/ImLocalStore';
import { PaymentService } from '../services/PaymentService';

let domain: number = 0x0001;
let tag: string = 'GengDi.EntryAbility';

/**
 * 耕地 HarmonyOS 入口 Ability。
 *
 * 加载 ArkUI 页面，设置沉浸式暗色状态栏。
 * onCreate 初始化全局 AppContext（配置持久化）与 IM 本地缓存库，
 * 页面加载后申请通话所需的麦克风/摄像头权限（用户授予一次即可）。
 */
export default class EntryAbility extends UIAbility {
  onCreate(want: Want, launchParam: AbilityConstant.LaunchParam): void {
    hilog.info(domain, tag, 'onCreate: 耕地 HarmonyOS 应用创建');
    AppContext.getInstance().initialize(this.context)
      .then(() => {
        return ImLocalStore.getInstance().initialize(this.context);
      })
      .then(() => {
        hilog.info(domain, tag, 'AppContext 与 IM 本地库初始化完成');
      })
      .catch((e: Error) => {
        hilog.error(domain, tag, '初始化失败: %{public}s', e.message);
      });
  }

  /** 申请语音/视频通话所需敏感权限（拒绝时通话媒体降级为无声/无画面，不影响信令） */
  private requestCallPermissions(): void {
    try {
      const atManager = abilityAccessCtrl.createAtManager();
      const permissions: Array<Permissions> = [
        'ohos.permission.MICROPHONE',
        'ohos.permission.CAMERA'
      ];
      atManager.requestPermissionsFromUser(this.context, permissions).then((result) => {
        hilog.info(domain, tag, '通话权限申请结果: authCount=%{public}d', result.authResults.length);
      }).catch((e: Error) => {
        hilog.warn(domain, tag, '通话权限申请失败: %{public}s', e.message);
      });
    } catch (e) {
      hilog.warn(domain, tag, '权限管理器不可用: %{public}s', (e as Error).message);
    }
  }

  onDestroy(): void {
    hilog.info(domain, tag, 'onDestroy: 应用销毁');
  }

  onWindowStageCreate(windowStage: window.WindowStage): void {
    hilog.info(domain, tag, 'onWindowStageCreate: 加载主页面');

    // 先加载页面内容，再设置窗口属性
    windowStage.loadContent('pages/Index', (err) => {
      if (err.code) {
        hilog.error(domain, tag, '加载页面失败: %{public}s', JSON.stringify(err));
        return;
      }
      hilog.info(domain, tag, '页面加载完成');

      // 页面加载完成后设置窗口属性
      windowStage.getMainWindow((err, mainWindow) => {
        if (err.code) {
          hilog.error(domain, tag, '获取主窗口失败: %{public}s', JSON.stringify(err));
          return;
        }

        mainWindow.setWindowBackgroundColor('#171d2b');
        hilog.info(domain, tag, '窗口背景色设置完成');

        mainWindow.setWindowSystemBarProperties({
          statusBarContentColor: '#ffffff',
          navigationBarContentColor: '#ffffff'
        }).then(() => {
          hilog.info(domain, tag, '状态栏属性设置成功');
        }).catch((err: Error) => {
          hilog.error(domain, tag, '设置状态栏失败: %{public}s', err.message);
        });

        // 窗口就绪后申请通话权限（弹窗需要窗口载体）
        this.requestCallPermissions();
      });
    });
  }

  onWindowStageDestroy(): void {
    hilog.info(domain, tag, 'onWindowStageDestroy: 窗口销毁');
  }

  onForeground(): void {
    hilog.info(domain, tag, 'onForeground: 应用进入前台');
  }

  onBackground(): void {
    hilog.info(domain, tag, 'onBackground: 应用进入后台');
  }

  /** 微信支付等通过 scheme/隐式Want拉起本应用时回调 */
  onNewWant(want: Want, launchParam: AbilityConstant.LaunchParam): void {
    hilog.info(domain, tag, 'onNewWant: 收到拉起请求 uri=%{public}s', JSON.stringify(want?.uri ?? ''));
    try {
      PaymentService.getInstance().handleWxWant(want);
    }
    catch (e) {
      hilog.error(domain, tag, '微信回调处理异常: %{public}s', (e as Error).message);
    }
  }
}
