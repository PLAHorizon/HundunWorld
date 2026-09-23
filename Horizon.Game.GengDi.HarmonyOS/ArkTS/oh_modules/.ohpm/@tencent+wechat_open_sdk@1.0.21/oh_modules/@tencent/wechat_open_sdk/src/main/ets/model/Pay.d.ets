import { BaseReq, BaseResp } from '../../../../Index';
export declare class PayReq extends BaseReq {
    protected get TAG(): string;
    appId?: string;
    partnerId?: string;
    prepayId?: string;
    nonceStr?: string;
    timeStamp?: string;
    packageValue?: string;
    sign?: string;
    extData?: string;
    signType?: string;
    checkArgs(): boolean;
    serializeTo(o12: object): void;
    deserializeFrom(n12: object): void;
    get type(): number;
}
export declare class PayResp extends BaseResp {
    prepayId?: string;
    returnKey?: string;
    extData?: string;
    serializeTo(m12: object): void;
    deserializeFrom(l12: object): void;
    get type(): number;
}
export declare class JointPayReq extends PayReq {
    protected get TAG(): string;
    get type(): number;
}
export declare class JointPayResp extends PayResp {
    get type(): number;
}
