import { BaseReq, BaseResp } from './Base';
import { Command } from './Constants';
export class InvoiceAuthInsertReq extends BaseReq {
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
        return Command.kCommandInvoiceAuthInsert;
    }
    serializeTo(w8) {
        super.serializeTo(w8);
        w8['url'] = this.url;
    }
    deserializeFrom(v8) {
        super.deserializeFrom(v8);
        this.url = v8['url'] ?? '';
    }
}
export class InvoiceAuthInsertResp extends BaseResp {
    constructor() {
        super(...arguments);
        this.wxOrderId = '';
    }
    get type() {
        return Command.kCommandInvoiceAuthInsert;
    }
    serializeTo(u8) {
        super.serializeTo(u8);
        u8['wxOrderId'] = this.wxOrderId;
    }
    deserializeFrom(t8) {
        super.deserializeFrom(t8);
        this.wxOrderId = t8['wxOrderId'] ?? '';
    }
}
