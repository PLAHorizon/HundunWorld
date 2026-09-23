import { BaseReq } from "./Base";
import { Command } from "./Constants";
import { WXMediaMessage } from "./message/SendMessageToWX";
export class LaunchFromWXReq extends BaseReq {
    serializeTo(y8) {
        super.serializeTo(y8);
        if (this.message) {
            let z8 = {};
            this.message.serializeTo(z8);
            y8['message'] = z8;
        }
        y8['lang'] = this.lang;
        y8['country'] = this.country;
    }
    deserializeFrom(x8) {
        super.deserializeFrom(x8);
        if (typeof x8['message'] !== 'undefined') {
            this.message = new WXMediaMessage;
            this.message.deserializeFrom(x8['message']);
        }
        this.lang = x8['lang'];
        this.country = x8['country'];
    }
    checkArgs() {
        return true;
    }
    get type() {
        return Command.kCommandLaunchFromWX;
    }
}
