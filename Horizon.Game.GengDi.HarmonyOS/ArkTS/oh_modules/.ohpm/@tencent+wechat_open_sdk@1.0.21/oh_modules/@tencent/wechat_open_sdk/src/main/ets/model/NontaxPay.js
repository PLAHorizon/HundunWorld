import { BaseReq, BaseResp } from './Base';
import { Command } from './Constants';
export class NontaxPayReq extends BaseReq {
    constructor() {
        super(...arguments);
        this.url = '';
    }
    checkArgs() {
        if (!this.url || this.url.length === 0)
            return false;
        return true;
    }
    get type() {
        return Command.kCommandNontaxPay;
    }
    serializeTo(w11) {
        super.serializeTo(w11);
        w11['url'] = this.url;
    }
    deserializeFrom(v11) {
        super.deserializeFrom(v11);
        this.url = v11['url'] ?? '';
    }
}
export class NontaxPayResp extends BaseResp {
    constructor() {
        super(...arguments);
        this.wxOrderId = '';
    }
    get type() {
        return Command.kCommandNontaxPay;
    }
    serializeTo(u11) {
        super.serializeTo(u11);
        u11['wxOrderId'] = this.wxOrderId;
    }
    deserializeFrom(t11) {
        super.deserializeFrom(t11);
        this.wxOrderId = t11['wxOrderId'] ?? '';
    }
}
