import { BaseReq, BaseResp } from './Base';
import { Command } from './Constants';
export class SubscribeMessageReq extends BaseReq {
    constructor() {
        super(...arguments);
        this.scene = 0;
        this.templateId = '';
        this.reserved = '';
    }
    checkArgs() {
        if (!this.templateId || this.templateId.length === 0)
            return false;
        return true;
    }
    get type() {
        return Command.kCommandSubscribeMessage;
    }
    serializeTo(c13) {
        super.serializeTo(c13);
        c13['scene'] = this.scene;
        c13['templateId'] = this.templateId;
        c13['reserved'] = this.reserved;
    }
    deserializeFrom(b13) {
        super.deserializeFrom(b13);
        this.scene = b13['scene'] ?? 0;
        this.templateId = b13['templateId'] ?? '';
        this.reserved = b13['reserved'] ?? '';
    }
}
export class SubscribeMessageResp extends BaseResp {
    constructor() {
        super(...arguments);
        this.templateId = '';
        this.scene = 0;
        this.action = '';
        this.reserved = '';
    }
    get type() {
        return Command.kCommandSubscribeMessage;
    }
    serializeTo(a13) {
        super.serializeTo(a13);
        a13['templateId'] = this.templateId;
        a13['scene'] = this.scene;
        a13['action'] = this.action;
        a13['reserved'] = this.reserved;
    }
    deserializeFrom(z12) {
        super.deserializeFrom(z12);
        this.templateId = z12['templateId'] ?? '';
        this.scene = z12['scene'] ?? 0;
        this.action = z12['action'] ?? '';
        this.reserved = z12['reserved'] ?? '';
    }
}
