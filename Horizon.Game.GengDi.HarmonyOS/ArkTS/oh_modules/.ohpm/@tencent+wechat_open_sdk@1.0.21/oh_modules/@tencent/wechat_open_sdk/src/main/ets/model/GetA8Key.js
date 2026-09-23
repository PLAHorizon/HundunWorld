import { BaseReq, BaseResp } from './Base';
import { Command } from './Constants';
export class GetA8KeyReq extends BaseReq {
    checkArgs() {
        return this.url !== undefined && this.url.trim().length > 0;
    }
    serializeTo(s8) {
        super.serializeTo(s8);
        s8['url'] = this.url;
    }
    deserializeFrom(r8) {
        super.deserializeFrom(r8);
        this.url = r8['url'];
    }
    get type() {
        return Command.kCommandGetA8Key;
    }
}
export class GetA8KeyResp extends BaseResp {
    serializeTo(q8) {
        super.serializeTo(q8);
        q8['redirectUrl'] = this.redirectUrl;
    }
    deserializeFrom(p8) {
        super.deserializeFrom(p8);
        this.redirectUrl = p8['redirectUrl'];
    }
    get type() {
        return Command.kCommandGetA8Key;
    }
}
