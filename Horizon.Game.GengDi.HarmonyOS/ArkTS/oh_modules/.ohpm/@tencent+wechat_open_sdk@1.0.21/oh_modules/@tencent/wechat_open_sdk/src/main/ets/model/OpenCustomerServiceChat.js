import { BaseReq, BaseResp } from './Base';
import { Command } from './Constants';
export class OpenCustomerServiceChatReq extends BaseReq {
    checkArgs() {
        if (this.corpId == undefined || this.corpId.length === 0) {
            return false;
        }
        if (this.url == undefined || this.url.length === 0) {
            return false;
        }
        return true;
    }
    get type() {
        return Command.kCommandOpenCustomerServiceChat;
    }
    serializeTo(g12) {
        super.serializeTo(g12);
        g12["wx_open_customer_service_chat_corpId"] = this.corpId;
        g12["wx_open_customer_service_chat_url"] = this.url;
    }
    deserializeFrom(f12) {
        super.deserializeFrom(f12);
        this.corpId = f12["wx_open_customer_service_chat_corpId"];
        this.url = f12["wx_open_customer_service_chat_url"];
    }
}
export class OpenCustomerServiceChatResp extends BaseResp {
    get type() {
        return Command.kCommandOpenCustomerServiceChat;
    }
}
