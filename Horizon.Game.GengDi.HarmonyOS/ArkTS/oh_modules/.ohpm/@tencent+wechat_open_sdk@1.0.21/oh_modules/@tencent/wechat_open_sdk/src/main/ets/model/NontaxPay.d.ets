import { BaseReq, BaseResp } from './Base';
/**
 * 非税支付
 */
export declare class NontaxPayReq extends BaseReq {
    url: string;
    checkArgs(): boolean;
    get type(): number;
    serializeTo(w11: object): void;
    deserializeFrom(v11: object): void;
}
export declare class NontaxPayResp extends BaseResp {
    wxOrderId: string;
    get type(): number;
    serializeTo(u11: object): void;
    deserializeFrom(t11: object): void;
}
