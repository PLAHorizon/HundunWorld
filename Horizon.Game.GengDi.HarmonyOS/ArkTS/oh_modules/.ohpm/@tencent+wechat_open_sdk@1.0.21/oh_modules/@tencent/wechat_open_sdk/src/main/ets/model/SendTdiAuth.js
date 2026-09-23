import { SendAuthReq, SendAuthResp } from './SendAuth';
import { Command } from './Constants';
export class SendTdiAuthReq extends SendAuthReq {
    get type() {
        return Command.kCommandSendTdiAuth;
    }
}
export class SendTdiAuthResp extends SendAuthResp {
    serializeTo(y12) {
        super.serializeTo(y12);
        if (this.tdiAuthBuffer != undefined) {
            y12['tdiAuthBuffer'] = Array.from(this.tdiAuthBuffer);
        }
    }
    deserializeFrom(x12) {
        super.deserializeFrom(x12);
        if (Array.isArray(x12['tdiAuthBuffer'])) {
            this.tdiAuthBuffer = new Uint8Array(x12['tdiAuthBuffer']);
        }
    }
    get type() {
        return Command.kCommandSendTdiAuth;
    }
}
