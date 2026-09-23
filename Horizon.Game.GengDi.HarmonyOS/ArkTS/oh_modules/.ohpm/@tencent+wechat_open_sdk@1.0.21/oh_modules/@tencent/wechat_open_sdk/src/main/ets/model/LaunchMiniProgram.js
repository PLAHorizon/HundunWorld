import { Log } from '../log/Log';
import { BaseReq, BaseResp } from './Base';
import { Command } from './Constants';
export class LaunchMiniProgramReq extends BaseReq {
    constructor() {
        super(...arguments);
        this.kTag = 'wxopensdk::LaunchMiniProgramReq';
    }
    checkArgs() {
        if (!this.userName || this.userName.length === 0) {
            Log.e(this.kTag, "userName is invalid");
            return false;
        }
        return true;
    }
    serializeTo(d9) {
        super.serializeTo(d9);
        d9['userName'] = this.userName;
        d9['path'] = this.path;
        d9['miniprogramType'] = this.miniprogramType;
        d9['extraData'] = this.extraData;
    }
    deserializeFrom(c9) {
        super.deserializeFrom(c9);
        this.userName = c9['userName'];
        this.path = c9['path'];
        this.miniprogramType = c9['miniprogramType'];
        this.extraData = c9['extraData'];
    }
    get type() {
        return Command.kCommandLaunchMiniProgram;
    }
}
export class LaunchMiniProgramResp extends BaseResp {
    serializeTo(b9) {
        super.serializeTo(b9);
        b9['extMsg'] = this.extMsg;
    }
    deserializeFrom(a9) {
        super.deserializeFrom(a9);
        this.extMsg = a9['extMsg'];
    }
    get type() {
        return Command.kCommandLaunchMiniProgram;
    }
}
