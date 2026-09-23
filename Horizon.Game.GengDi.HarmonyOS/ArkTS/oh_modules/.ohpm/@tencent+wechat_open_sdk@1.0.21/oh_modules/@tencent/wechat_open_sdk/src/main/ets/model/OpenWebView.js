import { BaseReq, BaseResp } from "./Base";
import { Command } from "./Constants";
export class OpenWebViewReq extends BaseReq {
    checkArgs() {
        return this.url !== undefined && this.url.length > 0;
    }
    serializeTo(k12) {
        super.serializeTo(k12);
        k12['url'] = this.url;
    }
    deserializeFrom(j12) {
        super.deserializeFrom(j12);
        this.url = j12['url'];
    }
    get type() {
        return Command.kCommandOpenWebView;
    }
}
export class OpenWebViewResp extends BaseResp {
    serializeTo(i12) {
        super.serializeTo(i12);
        i12['result'] = this.result;
    }
    deserializeFrom(h12) {
        super.deserializeFrom(h12);
        this.result = h12['result'];
    }
    get type() {
        return Command.kCommandOpenWebView;
    }
}
