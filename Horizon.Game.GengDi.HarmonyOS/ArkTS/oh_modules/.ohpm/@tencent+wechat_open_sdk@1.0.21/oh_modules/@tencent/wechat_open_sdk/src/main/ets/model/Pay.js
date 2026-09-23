import { BaseReq, BaseResp, Log } from '../../../../Index';
import { Command } from './Constants';
const EXTDATA_MAX_LENGTH = 1024;
export class PayReq extends BaseReq {
    get TAG() {
        return "PaySdk.PayReq";
    }
    checkArgs() {
        if (this.appId?.length === 0) {
            Log.e(this.TAG, "checkArgs fail, invalid appId");
            return false;
        }
        if (this.partnerId?.length === 0) {
            Log.e(this.TAG, "checkArgs fail, invalid partnerId");
            return false;
        }
        if (this.prepayId?.length === 0) {
            Log.e(this.TAG, "checkArgs fail, invalid prepayId");
            return false;
        }
        if (this.nonceStr?.length === 0) {
            Log.e(this.TAG, "checkArgs fail, invalid nonceStr");
            return false;
        }
        if (this.timeStamp?.length === 0) {
            Log.e(this.TAG, "checkArgs fail, invalid timeStamp");
            return false;
        }
        if (this.packageValue?.length === 0) {
            Log.e(this.TAG, "checkArgs fail, invalid packageValue");
            return false;
        }
        if (this.sign?.length === 0) {
            Log.e(this.TAG, "checkArgs fail, invalid sign");
            return false;
        }
        if (this.extData && this.extData.length > EXTDATA_MAX_LENGTH) {
            Log.e(this.TAG, "checkArgs fail, extData length too long");
            return false;
        }
        return true;
    }
    serializeTo(o12) {
        super.serializeTo(o12);
        o12['appId'] = this.appId;
        o12['partnerId'] = this.partnerId;
        o12['prepayId'] = this.prepayId;
        o12['nonceStr'] = this.nonceStr;
        o12['timeStamp'] = this.timeStamp;
        o12['packageValue'] = this.packageValue;
        o12['sign'] = this.sign;
        o12['extData'] = this.extData;
        o12['signType'] = this.signType;
    }
    deserializeFrom(n12) {
        super.deserializeFrom(n12);
        this.appId = n12['appId'];
        this.partnerId = n12['partnerId'];
        this.prepayId = n12['prepayId'];
        this.nonceStr = n12['nonceStr'];
        this.timeStamp = n12['timeStamp'];
        this.packageValue = n12['packageValue'];
        this.sign = n12['sign'];
        this.extData = n12['extData'];
        this.signType = n12['signType'];
    }
    get type() {
        return Command.kCommandPay;
    }
}
export class PayResp extends BaseResp {
    serializeTo(m12) {
        super.serializeTo(m12);
        m12['prepayId'] = this.prepayId;
        m12['returnKey'] = this.returnKey;
        m12['extData'] = this.extData;
    }
    deserializeFrom(l12) {
        super.deserializeFrom(l12);
        this.prepayId = l12['prepayId'];
        this.returnKey = l12['returnKey'];
        this.extData = l12['extData'];
    }
    get type() {
        return Command.kCommandPay;
    }
}
export class JointPayReq extends PayReq {
    get TAG() {
        return "PaySdk.JointPayReq";
    }
    get type() {
        return Command.kCommandJointPay;
    }
}
export class JointPayResp extends PayResp {
    get type() {
        return Command.kCommandJointPay;
    }
}
