import { AppTheme } from "../theme/AppTheme";
import { AddressViewModel, ShippingAddress } from "../viewmodels/OrderViewModels";

/** 地址编辑/新增页面 */
@Entry
@Component
export struct AddressEditPage {
  @State vm: AddressViewModel = new AddressViewModel();
  @State addressId: string = '';
  @State receiverName: string = '';
  @State phone: string = '';
  @State province: string = '';
  @State city: string = '';
  @State detail: string = '';
  @State isDefault: boolean = false;
  @State isEditing: boolean = false;

  aboutToAppear(): void {
    // 从路由参数获取 addressId（空表示新增）
    try {
      const params = this.getUIContext().getRouter().getParams() as Record<string, string>;
      if (params && params['addressId'] !== undefined && params['addressId'] !== null) {
        this.addressId = String(params['addressId']);
      }
    } catch (_e) {
      // 忽略参数解析错误
    }

    // 编辑模式：加载该地址数据回填
    if (this.addressId.length > 0) {
      this.isEditing = true;
      this.vm.loadAddresses().then(() => {
        const addr = this.vm.addresses.find((a: ShippingAddress) => a.addressId === this.addressId);
        if (addr) {
          this.receiverName = addr.receiverName;
          this.phone = addr.phone;
          this.province = addr.province;
          this.city = addr.city;
          this.detail = addr.detail;
          this.isDefault = addr.isDefault;
        }
      });
    }
  }

  private saveAddress(): void {
    if (!this.receiverName.trim()) {
      this.toast('请输入收件人姓名');
      return;
    }
    if (!this.phone.trim() || this.phone.length < 11) {
      this.toast('请输入正确的手机号码');
      return;
    }
    if (!this.province.trim() || !this.city.trim()) {
      this.toast('请填写省/市');
      return;
    }
    if (!this.detail.trim()) {
      this.toast('请输入详细地址');
      return;
    }

    const addr = new ShippingAddress();
    addr.addressId = this.addressId;
    addr.receiverName = this.receiverName.trim();
    addr.phone = this.phone.trim();
    addr.province = this.province.trim();
    addr.city = this.city.trim();
    addr.detail = this.detail.trim();
    addr.isDefault = this.isDefault;

    this.vm.saveAddress(addr).then(() => {
      this.toast('保存成功');
      setTimeout(() => {
        this.getUIContext().getRouter().back();
      }, 500);
    }).catch(() => {
      this.toast('保存失败，请重试');
    });
  }

  private toast(message: string): void {
    this.getUIContext().getPromptAction().showToast({ message: message, duration: 2000 });
  }

  build() {
    Column() {
      // 标题栏
      Row() {
        Text(this.isEditing ? '编辑地址' : '新增地址')
          .fontSize(18)
          .fontWeight(FontWeight.Bold)
          .fontColor(AppTheme.text)
      }
      .width('100%')
      .height(56)
      .padding({ left: 16, right: 16 })
      .backgroundColor(AppTheme.card)
      .alignItems(VerticalAlign.Center)

      Scroll() {
        Column({ space: 16 }) {
          // 收件人
          Column({ space: 8 }) {
            Text('收件人')
              .fontSize(13)
              .fontColor(AppTheme.textSecondary)
            TextInput({ placeholder: '请输入收件人姓名', text: this.receiverName })
              .width('100%')
              .height(48)
              .fontSize(15)
              .fontColor(AppTheme.text)
              .placeholderColor(AppTheme.textSecondary)
              .backgroundColor(AppTheme.card)
              .borderRadius(12)
              .padding({ left: 16, right: 16 })
              .onChange((v: string) => { this.receiverName = v; })
          }
          .width('100%')

          // 手机号
          Column({ space: 8 }) {
            Text('手机号')
              .fontSize(13)
              .fontColor(AppTheme.textSecondary)
            TextInput({ placeholder: '请输入手机号码', text: this.phone })
              .type(InputType.PhoneNumber)
              .width('100%')
              .height(48)
              .fontSize(15)
              .fontColor(AppTheme.text)
              .placeholderColor(AppTheme.textSecondary)
              .backgroundColor(AppTheme.card)
              .borderRadius(12)
              .padding({ left: 16, right: 16 })
              .onChange((v: string) => { this.phone = v; })
          }
          .width('100%')

          // 所在地区
          Column({ space: 8 }) {
            Text('所在地区')
              .fontSize(13)
              .fontColor(AppTheme.textSecondary)
            Row({ space: 8 }) {
              TextInput({ placeholder: '省', text: this.province })
                .layoutWeight(1)
                .height(48)
                .fontSize(15)
                .fontColor(AppTheme.text)
                .placeholderColor(AppTheme.textSecondary)
                .backgroundColor(AppTheme.card)
                .borderRadius(12)
                .padding({ left: 16, right: 16 })
                .onChange((v: string) => { this.province = v; })
              TextInput({ placeholder: '市', text: this.city })
                .layoutWeight(1)
                .height(48)
                .fontSize(15)
                .fontColor(AppTheme.text)
                .placeholderColor(AppTheme.textSecondary)
                .backgroundColor(AppTheme.card)
                .borderRadius(12)
                .padding({ left: 16, right: 16 })
                .onChange((v: string) => { this.city = v; })
            }
          }
          .width('100%')

          // 详细地址
          Column({ space: 8 }) {
            Text('详细地址')
              .fontSize(13)
              .fontColor(AppTheme.textSecondary)
            TextArea({ placeholder: '请输入详细地址（街道、门牌号等）', text: this.detail })
              .width('100%')
              .height(100)
              .fontSize(15)
              .fontColor(AppTheme.text)
              .placeholderColor(AppTheme.textSecondary)
              .backgroundColor(AppTheme.card)
              .borderRadius(12)
              .padding(16)
              .onChange((v: string) => { this.detail = v; })
          }
          .width('100%')

          // 设为默认
          Row() {
            Text('设为默认地址')
              .fontSize(14)
              .fontColor(AppTheme.text)
            Blank()
            Toggle({ type: ToggleType.Switch, isOn: this.isDefault })
              .onChange((isOn: boolean) => { this.isDefault = isOn; })
          }
          .width('100%')
          .height(48)
          .padding({ left: 16, right: 16 })
          .backgroundColor(AppTheme.card)
          .borderRadius(12)

          // 保存按钮
          Button('保存地址')
            .type(ButtonType.Normal)
            .width('100%')
            .height(50)
            .borderRadius(999)
            .backgroundColor(AppTheme.primary)
            .fontSize(16)
            .fontWeight(FontWeight.Medium)
            .margin({ top: 24 })
            .onClick(() => this.saveAddress())
        }
        .padding(16)
      }
      .layoutWeight(1)
      .width('100%')
      .scrollBar(BarState.Off)
    }
    .width('100%')
    .height('100%')
    .backgroundColor(AppTheme.bg)
  }
}