import { BaseReq, BaseResp } from './Base';
import { Command } from './Constants';
export class SendAuthReq extends BaseReq {
    constructor() {
        super(...arguments);
        this.isOption1 = false;
        this.nonAutomatic = false;
    }
    checkArgs() {
        this.scope = this.scope?.trim();
        if ((this.scope?.length ?? 0) === 0) {
            return false;
        }
        return true;
    }
    serializeTo(w12) {
        super.serializeTo(w12);
        w12['scope'] = this.scope;
        w12['state'] = this.state;
        w12['extData'] = this.extData;
        w12['isOption1'] = this.isOption1;
        w12['nonAutomatic'] = this.nonAutomatic;
    }
    deserializeFrom(v12) {
        super.deserializeFrom(v12);
        this.scope = v12['scope'];
        this.state = v12['state'];
        this.extData = v12['extData'];
        this.isOption1 = v12['isOption1'] ?? false;
        this.nonAutomatic = v12['nonAutomatic'] ?? false;
    }
    get type() {
        return Command.kCommandSendAuth;
    }
}
export class SendAuthResp extends BaseResp {
    constructor() {
        super(...arguments);
        this.authResult = false;
    }
    serializeTo(u12) {
        super.serializeTo(u12);
        u12['code'] = this.code;
        u12['state'] = this.state;
        u12['authResult'] = this.authResult;
        u12['url'] = this.url;
        u12['lang'] = this.lang;
        u12['country'] = this.country;
    }
    deserializeFrom(t12) {
        super.deserializeFrom(t12);
        this.code = t12['code'];
        this.state = t12['state'];
        this.authResult = t12['authResult'];
        this.url = t12['url'];
        this.lang = t12['lang'];
        this.country = t12['country'];
    }
    get type() {
        return Command.kCommandSendAuth;
    }
}
