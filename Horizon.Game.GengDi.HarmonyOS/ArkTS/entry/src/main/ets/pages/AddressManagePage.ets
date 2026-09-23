import { AppTheme } from "../theme/AppTheme";
import { AddressViewModel, ShippingAddress } from "../viewmodels/OrderViewModels";
import { EmptyView } from "../components/CommonWidgets";

/** 地址管理列表页（编辑/新增地址见 AddressEditPage.ets） */
@Entry
@Component
export struct AddressManagePage {
  @State vm: AddressViewModel = new AddressViewModel();
  @State selectedAddressId: string = '';
  /** 选择模式：'select' 表示选择返回，'manage' 表示管理模式 */
  @State mode: string = 'manage';
  private onAddressSelected: ((addr: ShippingAddress) => void) | null = null;

  aboutToAppear(): void {
    this.vm.loadAddresses();
  }

  private openEdit(addressId: string): void {
    this.getUIContext().getRouter().pushUrl({
      url: 'pages/AddressEditPage',
      params: { addressId: addressId }
    });
  }

  private addNewAddress(): void {
    this.getUIContext().getRouter().pushUrl({
      url: 'pages/AddressEditPage',
      params: { addressId: '' }
    });
  }

  private selectAddress(address: ShippingAddress): void {
    if (this.mode === 'select') {
      this.selectedAddressId = address.addressId;
      if (this.onAddressSelected) {
        this.onAddressSelected(address);
      }
      else {
        // 返回上一页并传递地址
        setTimeout(() => {
          this.getUIContext().getRouter().replaceUrl({
            url: 'pages/OrderConfirmPage',
            params: { addressId: address.addressId }
          });
        }, 300);
      }
    }
  }

  private setDefault(addressId: string): void {
    this.vm.loadAddresses().then(() => {
      const addr = this.vm.addresses.find((a: ShippingAddress) => a.addressId === addressId);
      if (addr) {
        addr.isDefault = true;
        this.vm.saveAddress(addr);
      }
    });
  }

  private deleteAddress(addressId: string): void {
    this.vm.deleteAddress(addressId).then(() => {
      this.vm.loadAddresses();
    });
  }

  build() {
    Column() {
      // 标题栏
      Row() {
        Text('收货地址')
          .fontSize(18)
          .fontWeight(FontWeight.Bold)
          .fontColor(AppTheme.text)
        Blank()
        Button('+ 新增')
          .type(ButtonType.Normal)
          .fontSize(14)
          .backgroundColor(AppTheme.primary)
          .height(36)
          .padding({ left: 14, right: 14 })
          .borderRadius(999)
          .onClick(() => this.addNewAddress())
      }
      .width('100%')
      .height(56)
      .padding({ left: 16, right: 16 })
      .backgroundColor(AppTheme.card)
      .alignItems(VerticalAlign.Center)

      if (this.vm.addresses.length === 0) {
        Column() {
          EmptyView({ message: '暂无收货地址，请添加' })
        }
        .layoutWeight(1)
        .width('100%')
        .justifyContent(FlexAlign.Center)
      }
      else {
        Scroll() {
          Column({ space: 12 }) {
            ForEach(this.vm.addresses, (addr: ShippingAddress) => {
              Column() {
                Row({ space: 12 }) {
                  Column({ space: 4 }) {
                    Row({ space: 8 }) {
                      Text(addr.receiverName)
                        .fontSize(16)
                        .fontWeight(FontWeight.Bold)
                        .fontColor(AppTheme.text)
                      Text(addr.phone)
                        .fontSize(13)
                        .fontColor(AppTheme.textSecondary)
                    }
                    Text(addr.fullAddress)
                      .fontSize(14)
                      .fontColor(AppTheme.text)
                      .maxLines(2)
                      .textOverflow({ overflow: TextOverflow.Ellipsis })
                  }
                  .layoutWeight(1)
                  .alignItems(HorizontalAlign.Start)

                  if (addr.isDefault) {
                    Text('默认')
                      .fontSize(10)
                      .fontColor(AppTheme.primary)
                      .backgroundColor('rgba(59,130,246,0.15)')
                      .padding({ left: 6, right: 6, top: 2, bottom: 2 })
                      .borderRadius(4)
                  }
                }
                .width('100%')

                Row({ space: 16 }) {
                  if (!addr.isDefault) {
                    Text('设为默认')
                      .fontSize(13)
                      .fontColor(AppTheme.textSecondary)
                      .onClick(() => this.setDefault(addr.addressId))
                  }
                  else {
                    Blank()
                  }
                  Text('编辑')
                    .fontSize(13)
                    .fontColor(AppTheme.primary)
                    .onClick(() => this.openEdit(addr.addressId))
                  Text('删除')
                    .fontSize(13)
                    .fontColor(AppTheme.down)
                    .onClick(() => this.deleteAddress(addr.addressId))
                }
                .width('100%')
                .margin({ top: 12 })
              }
              .width('100%')
              .padding(16)
              .backgroundColor(AppTheme.card)
              .borderRadius(16)
              .border({ width: 1, color: AppTheme.divider })
              .onClick(() => this.selectAddress(addr))
            }, (addr: ShippingAddress) => addr.addressId)
          }
          .padding(16)
        }
        .layoutWeight(1)
        .width('100%')
        .scrollBar(BarState.Off)
      }
    }
    .width('100%')
    .height('100%')
    .backgroundColor(AppTheme.bg)
  }
}