import { Command } from "./Constants";
import { BaseReq, BaseResp } from "./Base";
export class OpenBusinessWebViewReq extends BaseReq {
    constructor() {
        super(...arguments);
        this.businessType = 0;
    }
    checkArgs() {
        return true;
    }
    serializeTo(e12) {
        super.serializeTo(e12);
        e12['queryInfo'] = this.queryInfo;
        e12['businessType'] = this.businessType;
    }
    deserializeFrom(d12) {
        super.deserializeFrom(d12);
        this.queryInfo = d12['queryInfo'];
        this.businessType = d12['businessType'];
    }
    get type() {
        return Command.kCommandOpenBusinessWebView;
    }
}
export class OpenBusinessWebViewResp extends BaseResp {
    constructor() {
        super(...arguments);
        this.businessType = 0;
    }
    serializeTo(c12) {
        super.serializeTo(c12);
        c12['resultInfo'] = this.resultInfo;
        c12['businessType'] = this.businessType;
    }
    deserializeFrom(b12) {
        super.deserializeFrom(b12);
        this.resultInfo = b12['resultInfo'];
        this.businessType = b12['businessType'];
    }
    get type() {
        return Command.kCommandOpenBusinessWebView;
    }
}
