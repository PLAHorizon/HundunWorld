import { BaseReq, BaseResp } from './Base';
/**
 * 医保支付
 */
export declare class PayInsuranceReq extends BaseReq {
    url: string;
    checkArgs(): boolean;
    get type(): number;
    serializeTo(s12: object): void;
    deserializeFrom(r12: object): void;
}
export declare class PayInsuranceResp extends BaseResp {
    wxOrderId: string;
    get type(): number;
    serializeTo(q12: object): void;
    deserializeFrom(p12: object): void;
}
