export declare abstract class BaseReq {
    id: number;
    readonly name: string;
    transaction?: string;
    openId?: string;
    /**
     * 微信跳回宿主App时拉起的ability名字，如果不填则默认是'EntryAbility'
     */
    callbackAbility?: string;
    abstract checkArgs(): boolean;
    abstract get type(): number;
    serializeTo(l8: object): void;
    deserializeFrom(k8: object): void;
}
export declare enum ErrCode {
    ERR_OK = 0,
    ERR_COMM = -1,
    ERR_USER_CANCEL = -2,
    ERR_SENT_FAILED = -3,
    ERR_AUTH_DENIED = -4,
    ERR_UNSUPPORTED = -5,
    ERR_BAN = -6,
    ERR_SCOPE_SNSAPI_WXAAPP_INFO_CAN_ONLY_AUTHORIZED_SEPARATELY = -1000
}
export declare abstract class BaseResp {
    id: number;
    readonly name: string;
    errCode: number;
    errStr?: string;
    transaction?: string;
    openId?: string;
    abstract get type(): number;
    serializeTo(i8: object): void;
    deserializeFrom(h8: object): void;
}
export type SendReqResult = boolean;
export type SendReqResultWrap = SendReqResult | Promise<SendReqResult>;
