import { AppTheme } from "../../theme/AppTheme";
import { SectionCard, UserAvatar } from "../../components/CommonWidgets";
import { AppContext } from "../../model/AppContext";

@Component
export struct ProfileTab {
  @State userId: number = 0;
  @State nickname: string = '';
  @State baseUrl: string = '';
  @State authToken: string = '';
  @State imHost: string = '';
  @State imPort: string = '';
  @State saved: boolean = false;

  aboutToAppear(): void {
          const app = AppContext.getInstance();
          this.userId = app.user.userId;
          this.nickname = app.user.nickname;
          this.baseUrl = app.baseUrl;
          this.authToken = app.authToken;
          this.imHost = app.imHost;
          this.imPort = `${app.imPort}`;
      }

  private async saveConfig(): Promise<void> {
          const app = AppContext.getInstance();
          await app.setBaseUrl(this.baseUrl);
          await app.setAuthToken(this.authToken);
          await app.setImEndpoint(this.imHost, parseInt(this.imPort, 10) || 31000);
          await app.setCurrentUser(parseInt(`${this.userId}`, 10) || this.userId, this.nickname, '');
          this.saved = true;
          setTimeout(() => {
              this.saved = false;
          }, 2000);
      }

  private async doSignOut(): Promise<void> {
          await AppContext.getInstance().signOut();
          this.getUIContext().getRouter().replaceUrl({ url: 'pages/LoginPage' });
      }

  @Builder Field(label: string, value: string, onChange: (v: string) => void) {
    Column({ space: 4 }) {
      Text(label)
        .fontSize(AppTheme.fontCaption)
        .fontColor(AppTheme.textSecondary)
      TextInput({ text: value })
        .fontSize(AppTheme.fontBody)
        .fontColor(AppTheme.text)
        .backgroundColor('rgba(255,255,255,0.06)')
        .borderRadius(8)
        .height(40)
        .padding({ left: 12, right: 12 })
        .onChange((v: string) => onChange(v))
    }
      .width('100%')
      .alignItems(HorizontalAlign.Start)
  }

  build() {
    Scroll() {
      Column({ space: AppTheme.spaceMd }) {
        Text('我的')
          .fontSize(22)
          .fontWeight(FontWeight.Bold)
          .fontColor(AppTheme.text)
          .width('100%')
          .padding({ top: AppTheme.spaceLg })
        // 用户卡片
        Row({ space: AppTheme.spaceMd }) {
          UserAvatar({ name: this.nickname.length > 0 ? this.nickname : '未登录', avatarSize: 56 })
          Column({ space: 3 }) {
            Text(this.nickname.length > 0 ? this.nickname : '未登录')
              .fontSize(AppTheme.fontTitle)
              .fontWeight(FontWeight.Medium)
              .fontColor(AppTheme.text)
            Text(this.userId > 0 ? `ID: ${this.userId}` : '未登录，点击下方退出后重新登录')
              .fontSize(AppTheme.fontCaption)
              .fontColor(AppTheme.textSecondary)
          }
            .alignItems(HorizontalAlign.Start)
            .layoutWeight(1)
        }
          .width('100%')
          .padding(AppTheme.spaceLg)
          .backgroundColor(AppTheme.card)
          .borderRadius(AppTheme.radiusCard)
        SectionCard({
                        title: '服务配置'
                    }) {
          Column({ space: AppTheme.spaceMd }) {
            this.Field('用户 ID', this.userId > 0 ? `${this.userId}` : '', (v: string) => {
                                this.userId = parseInt(v, 10) || 0;
                            })
            this.Field('昵称', this.nickname, (v: string) => {
                                this.nickname = v;
                            })
            this.Field('API 地址', this.baseUrl, (v: string) => {
                                this.baseUrl = v;
                            })
            this.Field('认证 Token', this.authToken, (v: string) => {
                                this.authToken = v;
                            })
            this.Field('IM 网关地址', this.imHost, (v: string) => {
                                this.imHost = v;
                            })
            this.Field('IM 网关端口', this.imPort, (v: string) => {
                                this.imPort = v;
                            })
            Button(this.saved ? '已保存 ✓' : '保存配置')
              .width('100%')
              .height(42)
              .fontSize(AppTheme.fontSub)
              .backgroundColor(AppTheme.primary)
              .fontColor('#ffffff')
              .borderRadius(AppTheme.radiusCard)
              .margin({ top: 4 })
              .onClick(() => {
                                    this.saveConfig();
                                })
          }
            .width('100%')
        }
        SectionCard({
                        title: '账号'
                    }) {
          Column({ space: AppTheme.spaceMd }) {
            Button('退出登录')
              .width('100%')
              .height(42)
              .fontSize(AppTheme.fontSub)
              .backgroundColor('rgba(255,107,107,0.15)')
              .fontColor(AppTheme.down)
              .borderRadius(AppTheme.radiusCard)
              .onClick(() => {
                                    this.doSignOut();
                                })
            Text('退出后需重新登录以获取 IM 鉴权令牌')
              .fontSize(AppTheme.fontCaption)
              .fontColor(AppTheme.textSecondary)
          }
            .width('100%')
        }
      }
        .padding({ left: AppTheme.spaceLg, right: AppTheme.spaceLg, bottom: 20 })
        .alignItems(HorizontalAlign.Start)
    }
      .layoutWeight(1)
      .width('100%')
      .align(Alignment.Top)
      .scrollBar(BarState.Off)
      .linearGradient({ angle: AppTheme.skyAngle, colors: AppTheme.skyColors })
      .edgeEffect(EdgeEffect.Spring)
  }
}
