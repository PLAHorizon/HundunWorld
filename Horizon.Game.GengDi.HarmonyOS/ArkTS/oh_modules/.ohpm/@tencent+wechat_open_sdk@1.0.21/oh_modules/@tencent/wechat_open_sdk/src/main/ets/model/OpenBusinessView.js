import { Log } from '../log/Log';
import { BaseReq, BaseResp } from "./Base";
import { Command } from "./Constants";
export class OpenBusinessViewReq extends BaseReq {
    constructor() {
        super(...arguments);
        this.kTag = 'wxopensdk::OpenBusinessView';
    }
    checkArgs() {
        if (!this.businessType || this.businessType.length === 0) {
            Log.e(this.kTag, 'businessType is invalid');
            return false;
        }
        return true;
    }
    serializeTo(a12) {
        super.serializeTo(a12);
        a12['businessType'] = this.businessType;
        a12['query'] = this.query;
        a12['extInfo'] = this.extInfo;
    }
    deserializeFrom(z11) {
        super.deserializeFrom(z11);
        this.businessType = z11['businessType'];
        this.query = z11['query'];
        this.extInfo = z11['extInfo'];
    }
    get type() {
        return Command.kCommandOpenBusinessView;
    }
}
export class OpenBusinessViewResp extends BaseResp {
    serializeTo(y11) {
        super.serializeTo(y11);
        y11['extMsg'] = this.extMsg;
        y11['businessType'] = this.businessType;
    }
    deserializeFrom(x11) {
        super.deserializeFrom(x11);
        this.extMsg = x11['extMsg'];
        this.businessType = x11['businessType'];
    }
    get type() {
        return Command.kCommandOpenBusinessView;
    }
}
