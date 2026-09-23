import { AppTheme } from "../theme/AppTheme";
import { UserAvatar } from "../components/CommonWidgets";
import { AppContext } from "../model/AppContext";
import { ApiClient } from "../services/ApiClient";

/**
 * 通行证注册页（对齐 PC 端 RegisterViewModel + RegisterView）。
 * 字段：用户名、邮箱、密码、确认密码；注册成功后服务端返回令牌，自动登录并进入主界面。
 */
@Entry
@Component
struct RegisterPage {
  @State username: string = '';
  @State email: string = '';
  @State password: string = '';
  @State confirmPassword: string = '';
  @State submitting: boolean = false;
  @State errorText: string = '';

  private async doRegister(): Promise<void> {
    if (this.username.trim().length === 0 || this.email.trim().length === 0 || this.password.length === 0 || this.confirmPassword.length === 0) {
      this.errorText = '请输入完整的注册信息';
      return;
    }
    if (this.password !== this.confirmPassword) {
      this.errorText = '两次输入的密码不一致';
      return;
    }
    if (this.password.length < 6) {
      this.errorText = '密码长度至少为6位';
      return;
    }
    this.submitting = true;
    this.errorText = '';
    try {
      const app = AppContext.getInstance();
      const session = await ApiClient.getInstance()
        .registerAsync(this.username.trim(), this.email.trim(), this.password, app.machineId);
      await app.setLoginSession(session);
      await app.setCurrentUser(session.userId, session.nickname, '');
      // 注册成功后展示服务端生成的通行证号，确保用户感知，否则下次登录无法输入
      const displayPassport = session.passportId.trim();
      this.getUIContext().showAlertDialog({
        title: '注册成功',
        message: '您的通行证号为：\n' + displayPassport + '\n\n请牢记，下次登录需使用该通行证号。',
        autoCancel: false,
        confirm: {
          value: '进入平台',
          action: () => {
            this.getUIContext().getRouter().replaceUrl({ url: 'pages/Index' });
          }
        }
      });
    }
    catch (e) {
      this.errorText = (e as Error).message || '注册失败，请检查网络或服务器地址';
    }
    finally {
      this.submitting = false;
    }
  }

  build() {
    Column({ space: AppTheme.spaceLg }) {
      Blank()
      // 品牌区
      Column({ space: AppTheme.spaceMd }) {
        UserAvatar({ name: '耕', avatarSize: 72, bgColor: AppTheme.primary })
        Text('加入耕地')
          .fontSize(24)
          .fontWeight(FontWeight.Bold)
          .fontColor(AppTheme.text)
        Text('创建新账户，开启花卉交易与即时通讯')
          .fontSize(AppTheme.fontBody)
          .fontColor(AppTheme.textSecondary)
      }
      // 表单卡片
      Column({ space: AppTheme.spaceMd }) {
        TextInput({ placeholder: '用户名', text: this.username })
          .height(46)
          .fontSize(AppTheme.fontSub)
          .fontColor(AppTheme.text)
          .placeholderColor(AppTheme.textSecondary)
          .backgroundColor('rgba(255,255,255,0.06)')
          .borderRadius(AppTheme.radiusCard)
          .padding({ left: 14, right: 14 })
          .onChange((v: string) => {
                this.username = v;
            })
        TextInput({ placeholder: '邮箱', text: this.email })
          .height(46)
          .fontSize(AppTheme.fontSub)
          .fontColor(AppTheme.text)
          .placeholderColor(AppTheme.textSecondary)
          .backgroundColor('rgba(255,255,255,0.06)')
          .borderRadius(AppTheme.radiusCard)
          .padding({ left: 14, right: 14 })
          .type(InputType.Email)
          .onChange((v: string) => {
                this.email = v;
            })
        TextInput({ placeholder: '密码（至少6位）', text: this.password })
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
        TextInput({ placeholder: '确认密码', text: this.confirmPassword })
          .height(46)
          .fontSize(AppTheme.fontSub)
          .fontColor(AppTheme.text)
          .placeholderColor(AppTheme.textSecondary)
          .backgroundColor('rgba(255,255,255,0.06)')
          .borderRadius(AppTheme.radiusCard)
          .padding({ left: 14, right: 14 })
          .type(InputType.Password)
          .onChange((v: string) => {
                this.confirmPassword = v;
            })
          .onSubmit(() => {
                this.doRegister();
            })
        if (this.errorText.length > 0) {
          Text(this.errorText)
            .fontSize(AppTheme.fontBody)
            .fontColor(AppTheme.down)
            .width('100%')
            .textAlign(TextAlign.Start)
        }
        Button(this.submitting ? '注册中…' : '注 册')
          .width('100%')
          .height(46)
          .fontSize(AppTheme.fontSub)
          .backgroundColor(AppTheme.primary)
          .fontColor('#ffffff')
          .borderRadius(AppTheme.radiusCard)
          .enabled(!this.submitting)
          .onClick(() => {
                this.doRegister();
            })
        Row() {
          Text('已有账号？')
            .fontSize(AppTheme.fontBody)
            .fontColor(AppTheme.textSecondary)
          Text('返回登录')
            .fontSize(AppTheme.fontBody)
            .fontWeight(FontWeight.Bold)
            .fontColor(AppTheme.primary)
        }
          .justifyContent(FlexAlign.Center)
          .width('100%')
          .padding({ top: 4, bottom: 4 })
          .onClick(() => {
                this.getUIContext().getRouter().back();
            })
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