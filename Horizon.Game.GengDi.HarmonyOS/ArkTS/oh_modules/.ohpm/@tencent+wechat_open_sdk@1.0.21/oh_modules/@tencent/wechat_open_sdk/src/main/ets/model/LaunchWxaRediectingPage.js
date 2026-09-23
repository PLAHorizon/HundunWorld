import { Command } from "./Constants";
import { BaseReq, BaseResp } from "./Base";
export class LaunchWxaRedirectingPageReq extends BaseReq {
    checkArgs() {
        if (!this.invokeTicket || this.invokeTicket.length === 0) {
            return false;
        }
        return true;
    }
    serializeTo(h9) {
        super.serializeTo(h9);
        h9['invokeTicket'] = this.invokeTicket;
        h9['mediaUri'] = this.mediaUri;
    }
    deserializeFrom(g9) {
        super.deserializeFrom(g9);
        this.invokeTicket = g9['invokeTicket'];
        this.mediaUri = g9['mediaUri'];
    }
    get type() {
        return Command.kCommandWxaRedirectingPage;
    }
}
export class LaunchWxaRedirectingPageResp extends BaseResp {
    serializeTo(f9) {
        super.serializeTo(f9);
    }
    deserializeFrom(e9) {
        super.deserializeFrom(e9);
    }
    get type() {
        return Command.kCommandWxaRedirectingPage;
    }
}
