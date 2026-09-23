import { AppTheme } from '../theme/AppTheme';
import { ChatAvatar } from '../components/CommonWidgets';
import { ImViewModel } from '../viewmodels/ImViewModel';
import { CallSessionManager } from '../services/im/CallSessionManager';
import { ImCallType } from '../services/im/ImMessages';

/**
 * 联系人资料页（对标微信联系人资料页）。
 * 路由参数：contactJson（ContactsPage 传入的联系人快照）。
 * 操作：发消息（进聊天详情）、语音/视频通话（私聊好友）、删除好友（二次确认）。
 */

class ContactProfile {
  userId: number = 0;
  nickname: string = '';
  avatar: string = '';
  remarkName: string = '';
  onlineStatus: number = 0;
  signature: string = '';
  /** 关系：0 好友 / 1 陌生人 / 2 群组 */
  relation: number = 0;
  groupName: string = '';
}

interface ChatDetailParams {
  conversationJson: string;
}

@Entry
@Component
struct ContactProfilePage {
  vm: ImViewModel = ImViewModel.getInstance();
  private callMgr: CallSessionManager = CallSessionManager.getInstance();
  @State contact: ContactProfile = new ContactProfile();
  @State removing: boolean = false;

  aboutToAppear(): void {
    try {
      const params = this.getUIContext().getRouter().getParams() as Record<string, string>;
      if (params && params.contactJson) {
        this.contact = JSON.parse(params.contactJson) as ContactProfile;
      }
    } catch (_e) {
      // 参数解析失败保持默认空资料
    }
  }

  private displayName(): string {
    return this.contact.remarkName.length > 0 ? this.contact.remarkName : this.contact.nickname;
  }

  private statusText(): string {
    switch (this.contact.onlineStatus) {
      case 1:
        return '在线';
      case 2:
        return '离开';
      case 3:
        return '忙碌';
      case 4:
        return '隐身';
      default:
        return '离线';
    }
  }

  private showToast(message: string): void {
    this.getUIContext().getPromptAction().showToast({ message: message, duration: 2000 });
  }

  /** 发消息：进聊天详情（会话 ID 与服务端 IMUserGrain p_{对端ID} 格式一致） */
  private openChat(): void {
    const conversationId = this.contact.relation === 2 ? `g_${this.contact.userId}` : `p_${this.contact.userId}`;
    this.getUIContext().getRouter().pushUrl({
      url: 'pages/ChatDetailPage',
      params: {
        conversationJson: JSON.stringify({
          conversationId: conversationId,
          peerId: this.contact.userId,
          displayName: this.displayName(),
          isGroup: this.contact.relation === 2
        })
      } as ChatDetailParams
    });
  }

  /** 发起通话（仅好友私聊） */
  private startCall(callType: number): void {
    if (this.contact.relation === 2 || this.contact.userId <= 0) {
      return;
    }
    if (this.callMgr.busy) {
      this.showToast('当前已有通话进行中');
      return;
    }
    this.callMgr.startCall(this.contact.userId, this.displayName(), '', callType);
  }

  /** 删除好友（二次确认；群组关系不提供删除） */
  private confirmRemoveFriend(): void {
    this.getUIContext().showAlertDialog({
      title: '删除好友',
      message: `确定删除好友「${this.displayName()}」吗？\n删除后将解除双方好友关系，聊天记录保留。`,
      autoCancel: true,
      alignment: DialogAlignment.Center,
      primaryButton: {
        value: '取消',
        action: () => {
        }
      },
      secondaryButton: {
        value: '删除',
        fontColor: AppTheme.down,
        action: () => {
          this.doRemoveFriend();
        }
      }
    });
  }

  private async doRemoveFriend(): Promise<void> {
    if (this.removing) {
      return;
    }
    this.removing = true;
    try {
      await this.vm.removeFriend(this.contact.userId);
      this.showToast('已删除好友');
      this.getUIContext().getRouter().back();
    } catch (e) {
      this.showToast((e as Error).message || '删除好友失败');
    } finally {
      this.removing = false;
    }
  }

  /** 信息行 */
  @Builder
  infoRow(label: string, value: string) {
    Row() {
      Text(label)
        .fontSize(14)
        .fontColor(AppTheme.textSecondary)
        .width(72)
      Text(value.length > 0 ? value : '-')
        .fontSize(14)
        .fontColor(AppTheme.text)
        .layoutWeight(1)
    }
    .width('100%')
    .padding({ left: 16, right: 16, top: 12, bottom: 12 })
  }

  build() {
    Column() {
      // ========== 顶部导航栏 ==========
      Row() {
        Button() {
          SymbolGlyph($r('sys.symbol.chevron_left'))
            .fontSize(20)
            .fontColor([AppTheme.text])
        }
        .width(40)
        .height(40)
        .borderRadius(20)
        .backgroundColor(Color.Transparent)
        .padding(0)
        .onClick(() => {
          this.getUIContext().getRouter().back();
        })

        Blank()
      }
      .width('100%')
      .height(52)
      .padding({ left: 8, right: 8 })
      .backgroundColor(AppTheme.chatHeaderBg)
      .backgroundBlurStyle(BlurStyle.Thin)
      .border({ width: { bottom: 1 }, color: AppTheme.chatBorderFaint })

      Scroll() {
        Column() {
          // ========== 头像 + 昵称区 ==========
          Row({ space: 16 }) {
            Stack({ alignContent: Alignment.BottomEnd }) {
              ChatAvatar({
                name: this.displayName(),
                avatar: this.contact.avatar,
                userId: this.contact.userId,
                isGroup: this.contact.relation === 2,
                avatarSize: 64
              })
              if (this.contact.relation !== 2 && this.contact.onlineStatus === 1) {
                Circle({ width: 14, height: 14 })
                  .fill(AppTheme.up)
                  .stroke(AppTheme.chatDotStroke)
                  .strokeWidth(2)
                  .margin(2)
              }
            }
            .width(64)
            .height(64)

            Column({ space: 4 }) {
              Text(this.displayName())
                .fontSize(20)
                .fontWeight(FontWeight.Medium)
                .fontColor(AppTheme.text)
                .maxLines(1)
                .textOverflow({ overflow: TextOverflow.Ellipsis })
              Text(this.contact.relation === 2 ? '群聊' : this.statusText())
                .fontSize(12)
                .fontColor(this.contact.onlineStatus === 1 ? AppTheme.up : AppTheme.textSecondary)
            }
            .alignItems(HorizontalAlign.Start)
            .layoutWeight(1)
          }
          .width('100%')
          .padding({ left: 20, right: 20, top: 20, bottom: 16 })

          // ========== 资料卡 ==========
          Column() {
            this.infoRow('昵称', this.contact.nickname)
            Row()
              .height(1)
              .width('100%')
              .backgroundColor(AppTheme.chatBorderFaint)
              .margin({ left: 88 })
            if (this.contact.remarkName.length > 0) {
              this.infoRow('备注', this.contact.remarkName)
              Row()
                .height(1)
                .width('100%')
                .backgroundColor(AppTheme.chatBorderFaint)
                .margin({ left: 88 })
            }
            this.infoRow('耕地号', this.contact.userId > 0 ? `${this.contact.userId}` : '-')
            Row()
              .height(1)
              .width('100%')
              .backgroundColor(AppTheme.chatBorderFaint)
              .margin({ left: 88 })
            this.infoRow('签名', this.contact.signature)
          }
          .width('100%')
          .borderRadius(14)
          .backgroundColor(AppTheme.card)
          .border({ width: 1, color: AppTheme.chatBorderFaint })
          .margin({ left: 16, right: 16 })

          // ========== 操作区（对标微信资料页底部按钮） ==========
          if (this.contact.relation !== 2) {
            Row({ space: 10 }) {
              Button() {
                Text('语音通话')
                  .fontSize(14)
                  .fontColor(AppTheme.text)
              }
              .layoutWeight(1)
              .height(44)
              .borderRadius(12)
              .backgroundColor(AppTheme.card)
              .border({ width: 1, color: AppTheme.chatBorderFaint })
              .onClick(() => {
                this.startCall(ImCallType.Audio);
              })

              Button() {
                Text('视频通话')
                  .fontSize(14)
                  .fontColor(AppTheme.text)
              }
              .layoutWeight(1)
              .height(44)
              .borderRadius(12)
              .backgroundColor(AppTheme.card)
              .border({ width: 1, color: AppTheme.chatBorderFaint })
              .onClick(() => {
                this.startCall(ImCallType.Video);
              })
            }
            .width('100%')
            .margin({ left: 16, right: 16, top: 14 })
            .padding({ left: 0, right: 0 })
          }

          Button('发消息')
            .fontSize(16)
            .fontWeight(FontWeight.Medium)
            .fontColor('#ffffff')
            .backgroundColor(AppTheme.primary)
            .borderRadius(12)
            .width('100%')
            .height(48)
            .margin({ left: 16, right: 16, top: 14 })
            .onClick(() => {
              this.openChat();
            })

          // 删除好友（仅好友关系；对标微信资料页"删除联系人"）
          if (this.contact.relation !== 2) {
            Button(this.removing ? '删除中…' : '删除好友')
              .fontSize(14)
              .fontColor(AppTheme.down)
              .backgroundColor(Color.Transparent)
              .borderRadius(12)
              .width('100%')
              .height(44)
              .margin({ left: 16, right: 16, top: 10, bottom: 30 })
              .enabled(!this.removing)
              .onClick(() => {
                this.confirmRemoveFriend();
              })
          } else {
            Row().height(30)
          }
        }
        .width('100%')
      }
      .layoutWeight(1)
      .width('100%')
      .scrollBar(BarState.Off)
      .edgeEffect(EdgeEffect.Spring)
    }
    .width('100%')
    .height('100%')
    .backgroundColor(AppTheme.bg)
  }
}
