import { AppTheme } from '../theme/AppTheme';
import { HomeTab } from './tabs/HomeTab';
import { WeatherTab } from './tabs/WeatherTab';
import { MarketTab } from './tabs/MarketTab';
import { DiscoverTab } from './tabs/DiscoverTab';
import { ProfileTab } from './tabs/ProfileTab';
import { AppContext } from '../model/AppContext';
import { CallOverlay } from '../components/CallOverlay';
import { CallSessionManager } from '../services/im/CallSessionManager';
import { ImSession } from '../services/im/ImSession';

/**
 * 耕地 HarmonyOS 主页面（原生 MVVM 重写版）。
 *
 * - 底部 TabBar 切换 5 个原生 Tab（数据经 ViewModel 绑定真实 WebAPI）
 * - 次级功能入口（IM/联系人/订单/音乐等）统一收纳在"发现"页金刚区
 */

class TabDef {
  title: string = '';
  icon: string = '';
  /** 非空时用系统符号库（SymbolGlyph）渲染，优先级高于 icon */
  symbol: string = '';

  constructor(title: string, icon: string, symbol: string = '') {
    this.title = title;
    this.icon = icon;
    this.symbol = symbol;
  }
}

const TABS: TabDef[] = [
  new TabDef('首页', '⌂'),
  new TabDef('天气', '☀'),
  new TabDef('市场', '✿'),
  new TabDef('发现', '◎'),
  new TabDef('我的', '◙', 'person')
];

@Entry
@Component
struct Index {
  // @StorageProp：AppStorage → 组件单向同步（本地点击仍正常切页）。
  // 首页通过 AppStorage.setOrCreate('currentTab', n) 实现"去采购/看天气"跨 Tab 跳转。
  @StorageProp('currentTab') currentTab: number = 0;
  /** 会话恢复完成前不挂载任何 Tab（避免空 token 请求） */
  @State authReady: boolean = false;

  async aboutToAppear(): Promise<void> {
    // 鉴权门禁：先等 AppContext 从本地恢复会话，避免初始化竞态误判未登录
    await AppContext.getInstance().whenReady();
    if (!AppContext.getInstance().isSignedIn) {
      this.getUIContext().getRouter().replaceUrl({ url: 'pages/LoginPage' });
      return;
    }
    this.authReady = true;
    // 初始化通话管理器（挂接 IM 网关信令推送，接收全局来电）
    CallSessionManager.getInstance();
    // 立即建立 IM 网关连接（连接即发首跳完成用户绑定）。
    // ImSession 连接原本是懒建立——只有打开聊天/联系人界面才会 ensureConnected；
    // 若用户从未进入这些页面，连接/心跳不存在，网关不会注册推送通道，
    // 任何界面都收不到来电信令。此处主动建立后，重连也由 ImSession 自愈。
    ImSession.getInstance().ensureConnected().catch(() => {
    });
  }

  build() {
    Stack({ alignContent: Alignment.Bottom }) {
      Column() {
        // ========== 内容区（原生 Tabs；会话未就绪时显示加载态）==========
        if (!this.authReady) {
          Column() {
            LoadingProgress().width(36).height(36).color(AppTheme.primary)
          }
          .layoutWeight(1)
          .width('100%')
          .justifyContent(FlexAlign.Center)
        } else {
        Tabs({ barPosition: BarPosition.End, index: this.currentTab }) {
          TabContent() {
            HomeTab()
          }
          TabContent() {
            WeatherTab()
          }
          TabContent() {
            MarketTab()
          }
          TabContent() {
            DiscoverTab()
          }
          TabContent() {
            ProfileTab()
          }
        }
        .layoutWeight(1)
        .width('100%')
        .scrollable(false)
        .barMode(BarMode.Fixed)
        .barHeight(0)
        .onChange((index: number) => {
          this.currentTab = index;
        })
        }
      }
      .width('100%')
      .height('100%')

      // ========== 底部 TabBar（5 Tab 均分，天气风格半透明底）==========
      Column() {
        Row() {
          ForEach(TABS, (tab: TabDef, index: number) => {
            Column() {
              if (tab.symbol.length > 0) {
                // 系统符号库渲染（单色，随激活态变色）
                SymbolGlyph($r(`sys.symbol.${tab.symbol}`))
                  .fontSize(24)
                  .fontColor([index === this.currentTab ? AppTheme.primary : AppTheme.textLow])
              } else {
                Text(tab.icon)
                  .fontSize(22)
                  .lineHeight(24)
                  .fontColor(index === this.currentTab ? AppTheme.primary : AppTheme.textLow)
              }
              Text(tab.title)
                .fontSize(11)
                .margin({ top: 2 })
                .fontColor(index === this.currentTab ? AppTheme.primary : AppTheme.textLow)
            }
            .layoutWeight(1)
            .height('100%')
            .justifyContent(FlexAlign.Center)
            .onClick(() => {
              this.currentTab = index;
            })
          }, (tab: TabDef) => tab.title)
        }
        .width('100%')
        .height(56)
        .backgroundColor('rgba(23,29,43,0.96)')
        .border({ width: { top: 1 }, color: AppTheme.cardBorder })
      }
      .width('100%')

      // ========== 全局来电/通话浮层（主页面存活期间可接收来电）==========
      CallOverlay()
    }
    .width('100%')
    .height('100%')
    .linearGradient({
      angle: AppTheme.skyAngle,
      colors: AppTheme.skyColors
    })
  }
}
