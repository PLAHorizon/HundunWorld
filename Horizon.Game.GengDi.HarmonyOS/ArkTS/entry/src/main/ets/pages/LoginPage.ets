import { AppTheme } from "../theme/AppTheme";
import { UserAvatar } from "../components/CommonWidgets";
import { AppContext } from "../model/AppContext";
import { ApiClient } from "../services/ApiClient";

@Entry
@Component
struct LoginPage {
  @State passportId: string = '';
  @State password: string = '';
  @State baseUrl: string = '';
  @State imHost: string = '';
  @State imPort: string = '';
  @State showServerPanel: boolean = false;
  @State submitting: boolean = false;
  @State errorText: string = '';

  async aboutToAppear(): Promise<void> {
          await AppContext.getInstance().whenReady();
          const app = AppContext.getInstance();
          this.baseUrl = app.baseUrl;
          this.imHost = app.imHost;
          this.imPort = `${app.imPort}`;
          this.passportId = app.passportId;
      }

  private async doLogin(): Promise<void> {
          if (this.passportId.trim().length === 0 || this.password.length === 0) {
              this.errorText = '请输入通行证号和密码';
              return;
          }
          this.submitting = true;
          this.errorText = '';
          try {
              const app = AppContext.getInstance();
              await app.setBaseUrl(this.baseUrl.trim());
              await app.setImEndpoint(this.imHost.trim(), parseInt(this.imPort, 10) || 31000);
              const session = await ApiClient.getInstance()
                  .signInAsync(this.passportId.trim(), this.password, app.machineId);
              await app.setLoginSession(session);
              await app.setCurrentUser(session.userId, session.nickname, '');
              this.getUIContext().getRouter().replaceUrl({ url: 'pages/Index' });
          }
          catch (e) {
              this.errorText = (e as Error).message || '登录失败，请检查账号或服务器地址';
          }
          finally {
              this.submitting = false;
          }
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
    Column({ space: AppTheme.spaceLg }) {
      Blank()
      // 品牌区
      Column({ space: AppTheme.spaceMd }) {
        UserAvatar({ name: '耕', avatarSize: 72, bgColor: AppTheme.primary })
        Text('耕地 · 花卉市场')
          .fontSize(24)
          .fontWeight(FontWeight.Bold)
          .fontColor(AppTheme.text)
        Text('花卉交易与即时通讯')
          .fontSize(AppTheme.fontBody)
          .fontColor(AppTheme.textSecondary)
      }
      // 表单卡片
      Column({ space: AppTheme.spaceMd }) {
        TextInput({ placeholder: '通行证号', text: this.passportId })
          .height(46)
          .fontSize(AppTheme.fontSub)
          .fontColor(AppTheme.text)
          .placeholderColor(AppTheme.textSecondary)
          .backgroundColor('rgba(255,255,255,0.06)')
          .borderRadius(AppTheme.radiusCard)
          .padding({ left: 14, right: 14 })
          .onChange((v: string) => {
                this.passportId = v;
            })
        TextInput({ placeholder: '密码', text: this.password })
          .height(46)
          .fontSize(AppTheme.fontSub)
          .fontColor(AppTheme.text)
          .placeholderColor(AppTheme.textSecondary)
          .backgroundColor('rgba(255,255,255,0.06)')
          .borderRadius(AppTheme.radiusCard)
          .padding({ left: 14, right: 14 })
          .type(InputType.Password)
          .onChange((v: string) => {
                this.password = v;
            })
          .onSubmit(() => {
                this.doLogin();
            })
        if (this.errorText.length > 0) {
          Text(this.errorText)
            .fontSize(AppTheme.fontBody)
            .fontColor(AppTheme.down)
            .width('100%')
            .textAlign(TextAlign.Start)
        }
        Button(this.submitting ? '登录中…' : '登 录')
          .width('100%')
          .height(46)
          .fontSize(AppTheme.fontSub)
          .backgroundColor(AppTheme.primary)
          .fontColor('#ffffff')
          .borderRadius(AppTheme.radiusCard)
          .enabled(!this.submitting)
          .onClick(() => {
                this.doLogin();
            })
        Row() {
          Text('还没有账号？')
            .fontSize(AppTheme.fontBody)
            .fontColor(AppTheme.textSecondary)
          Text('立即注册')
            .fontSize(AppTheme.fontBody)
            .fontWeight(FontWeight.Bold)
            .fontColor(AppTheme.primary)
        }
          .justifyContent(FlexAlign.Center)
          .width('100%')
          .padding({ top: 4, bottom: 4 })
          .onClick(() => {
                this.getUIContext().getRouter().pushUrl({ url: 'pages/RegisterPage' });
            })
        // 服务器配置折叠
        Text(this.showServerPanel ? '收起服务器设置 ▴' : '服务器设置 ▾')
          .fontSize(AppTheme.fontBody)
          .fontColor(AppTheme.textSecondary)
          .padding({ top: 4, bottom: 4 })
          .onClick(() => {
                this.showServerPanel = !this.showServerPanel;
            })
        if (this.showServerPanel) {
          Column({ space: AppTheme.spaceMd }) {
            this.Field('API 地址（模拟器默认 http://10.0.2.2:5100/）', this.baseUrl, (v: string) => {
                        this.baseUrl = v;
                    })
            this.Field('IM 网关地址', this.imHost, (v: string) => {
                        this.imHost = v;
                    })
            this.Field('IM 网关端口', this.imPort, (v: string) => {
                        this.imPort = v;
                    })
          }
            .width('100%')
        }
      }
        .width('100%')
        .padding(AppTheme.spaceLg)
        .backgroundColor(AppTheme.card)
        .borderRadius(16)
      Blank()
      Text(`设备标识 ${AppContext.getInstance().machineId.slice(0, 8)}…`)
        .fontSize(AppTheme.fontCaption)
        .fontColor(AppTheme.textSecondary)
        .opacity(0.6)
    }
      .width('100%')
      .height('100%')
      .padding({ left: 28, right: 28 })
      .backgroundColor(AppTheme.bg)
  }
}
