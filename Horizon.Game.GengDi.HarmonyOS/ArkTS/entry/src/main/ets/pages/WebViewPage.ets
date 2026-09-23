import { router } from '@kit.ArkUI';
import { webview } from '@kit.ArkWeb';
import { hilog } from '@kit.PerformanceAnalysisKit';
import { rawfileUri } from '../model/TabConfig';
import { WebHostProxy } from '../services/WebHostProxy';

/**
 * 次级页面 WebView 容器。
 *
 * 用于展示音乐播放器、市场预报、商家预警等非主标签页面。
 * 通过 router 参数接收 HTML 文件名和页面标题。
 */

interface PageParams {
  page: string;
  title: string;
}

@Entry
@Component
struct WebViewPage {
  /** 页面标题 */
  @State pageTitle: string = '';
  /** HTML 文件名 */
  @State pageFile: string = '';
  /** WebView 加载 URL */
  @State webSrc: string = '';

  private webController: webview.WebviewController = new webview.WebviewController();
  private flowerApi: WebHostProxy = new WebHostProxy();

  private readonly syncMethods: string[] = [
    'setBaseUrl', 'setAuthToken', 'getBaseUrl',
    'loadMusicData', 'getMusicData',
    // 原生音频（AVPlayer）桥：解决 ArkWeb <audio> 在模拟器无声的问题
    'startPlay', 'resumePlay', 'pausePlay', 'seekMs', 'setPlayVol',
    'getPlayPos', 'getPlayDur', 'getPlayState', 'getPlayError', 'nativeReady'
  ];
  private readonly asyncMethods: string[] = [
    'getMarketOverview', 'getPriceForecast', 'getPriceHistory', 'getSpeciesList',
    'getDashboardStats', 'getRegionalTradeData', 'getSupplyDemandData', 'getRecentTransactions',
    'getAlerts', 'getPlantingBatches', 'getSensorData', 'getActiveAdvice',
    'getMerchantDashboard', 'getMerchantOrders',
    'getCurrentWeather', 'getWeatherForecast', 'getAgriAlerts', 'getWeatherCities'
  ];

  aboutToAppear(): void {
    const params = this.getUIContext().getRouter().getParams() as PageParams;
    if (params) {
      this.pageTitle = params.title ?? '';
      this.pageFile = params.page ?? 'harmony-home.html';
      this.webSrc = rawfileUri(this.pageFile);
    }
  }

  aboutToDisappear(): void {
    // 释放原生音频播放器，避免页面退出后继续占用音频资源
    this.flowerApi.releaseAudio();
  }

  build() {
    Column() {
      // ========== 顶部导航栏 ==========
      Row() {
        // 返回按钮
        Text('‹')
          .fontSize(28)
          .fontColor('#ffffff')
          .width(40)
          .textAlign(TextAlign.Center)
          .onClick(() => {
            router.back();
          })

        // 标题
        Text(this.pageTitle)
          .fontSize(17)
          .fontColor('#ffffff')
          .fontWeight(FontWeight.Medium)
          .layoutWeight(1)
          .textAlign(TextAlign.Center)

        // 占位（保持标题居中）
        Text('')
          .width(40)
      }
      .width('100%')
      .height(48)
      .backgroundColor('rgba(23,29,43,0.96)')
      .border({ width: { bottom: 1 }, color: 'rgba(255,255,255,0.08)' })
      .alignItems(VerticalAlign.Center)

      // ========== WebView 内容区 ==========
      Web({ src: this.webSrc, controller: this.webController })
        .layoutWeight(1)
        .width('100%')
        .javaScriptAccess(true)
        .domStorageAccess(true)
        .fileAccess(true)
        .imageAccess(true)
        .onlineImageAccess(true)
        .mixedMode(MixedMode.All)
        .zoomAccess(false)
        .backgroundColor('#171d2b')
        .javaScriptProxy({
          object: this.flowerApi,
          name: 'flowerApi',
          methodList: this.syncMethods,
          asyncMethodList: this.asyncMethods,
          controller: this.webController
        })
        .onPageEnd(() => {
          hilog.info(0x0001, 'GengDi.Web', '次级页面 WebView 加载完成: %{public}s', this.pageFile);
        })
        .onConsole((event) => {
          hilog.info(0x0001, 'GengDi.Web', 'JS: %{public}s', event.message);
          return false;
        })
        .onErrorReceive(() => {
          hilog.error(0x0001, 'GengDi.Web', '次级页面 WebView 加载错误: %{public}s', this.pageFile);
        })
    }
    .width('100%')
    .height('100%')
    .backgroundColor('#171d2b')
  }
}
