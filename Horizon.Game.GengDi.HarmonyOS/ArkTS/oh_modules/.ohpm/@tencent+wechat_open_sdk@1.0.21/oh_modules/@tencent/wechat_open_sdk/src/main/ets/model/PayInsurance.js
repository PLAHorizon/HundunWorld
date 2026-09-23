import { BaseReq, BaseResp } from './Base';
import { Command } from './Constants';
export class PayInsuranceReq extends BaseReq {
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
        return Command.kCommandPayInsurance;
    }
    serializeTo(s12) {
        super.serializeTo(s12);
        s12['url'] = this.url;
    }
    deserializeFrom(r12) {
        super.deserializeFrom(r12);
        this.url = r12['url'] ?? '';
    }
}
export class PayInsuranceResp extends BaseResp {
    constructor() {
        super(...arguments);
        this.wxOrderId = '';
    }
    get type() {
        return Command.kCommandPayInsurance;
    }
    serializeTo(q12) {
        super.serializeTo(q12);
        q12['wxOrderId'] = this.wxOrderId;
    }
    deserializeFrom(p12) {
        super.deserializeFrom(p12);
        this.wxOrderId = p12['wxOrderId'] ?? '';
    }
}
