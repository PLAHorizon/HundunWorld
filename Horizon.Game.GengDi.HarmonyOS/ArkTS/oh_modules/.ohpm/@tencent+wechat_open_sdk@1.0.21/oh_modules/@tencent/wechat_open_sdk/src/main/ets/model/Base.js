export class BaseReq {
    constructor() {
        this.id = 0;
        this.name = this.constructor.name;
    }
    serializeTo(l8) {
        l8['__type__'] = this.type;
        l8['transaction'] = this.transaction;
        l8['openId'] = this.openId;
        l8['callbackAbility'] = this.callbackAbility;
    }
    deserializeFrom(k8) {
        this.transaction = k8['transaction'];
        this.openId = k8['openId'];
        this.callbackAbility = k8['callbackAbility'];
    }
}
export var ErrCode;
(function (j8) {
    j8[j8["ERR_OK"] = 0] = "ERR_OK";
    j8[j8["ERR_COMM"] = -1] = "ERR_COMM";
    j8[j8["ERR_USER_CANCEL"] = -2] = "ERR_USER_CANCEL";
    j8[j8["ERR_SENT_FAILED"] = -3] = "ERR_SENT_FAILED";
    j8[j8["ERR_AUTH_DENIED"] = -4] = "ERR_AUTH_DENIED";
    j8[j8["ERR_UNSUPPORTED"] = -5] = "ERR_UNSUPPORTED";
    j8[j8["ERR_BAN"] = -6] = "ERR_BAN";
    j8[j8["ERR_SCOPE_SNSAPI_WXAAPP_INFO_CAN_ONLY_AUTHORIZED_SEPARATELY"] = -1000] = "ERR_SCOPE_SNSAPI_WXAAPP_INFO_CAN_ONLY_AUTHORIZED_SEPARATELY";
})(ErrCode || (ErrCode = {}));
export class BaseResp {
    constructor() {
        this.id = 0;
        this.name = this.constructor.name;
        this.errCode = ErrCode.ERR_OK;
    }
    serializeTo(i8) {
        i8['__type__'] = this.type;
        i8['errCode'] = this.errCode;
        i8['errStr'] = this.errStr;
        i8['transaction'] = this.transaction;
        i8['openId'] = this.openId;
    }
    deserializeFrom(h8) {
        this.errCode = h8['errCode'] ?? ErrCode.ERR_COMM;
        this.errStr = h8['errStr'];
        this.transaction = h8['transaction'];
        this.openId = h8['openId'];
    }
}
