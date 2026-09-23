import { SendAuthReq, SendAuthResp } from './SendAuth';
import { Command } from './Constants';
export declare class SendTdiAuthReq extends SendAuthReq {
    get type(): Command;
}
export declare class SendTdiAuthResp extends SendAuthResp {
    tdiAuthBuffer?: Uint8Array;
    serializeTo(y12: object): void;
    deserializeFrom(x12: object): void;
    get type(): Command;
}
