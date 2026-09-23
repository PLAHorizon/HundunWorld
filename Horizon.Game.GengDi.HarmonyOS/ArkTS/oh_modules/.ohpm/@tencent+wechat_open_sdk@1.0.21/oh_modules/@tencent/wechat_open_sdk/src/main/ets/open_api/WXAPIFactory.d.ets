import common from '@ohos.app.ability.common';
import { WXAppSupportFunc } from '../model/Constants';
import Want from "@ohos.app.ability.Want";
import { BaseReq, BaseResp, SendReqResultWrap } from '../model/Base';
export interface WXApi {
    sendReq(context: common.UIAbilityContext, req: BaseReq): SendReqResultWrap;
    openWechat(context: common.UIAbilityContext): SendReqResultWrap;
    handleWant(want: Want, eventHandler: WXApiEventHandler): boolean;
    setPayReportEnabled(enabled: boolean): void;
    isWXAppInstalled(): boolean;
    isWXAppSupportFunc(func: WXAppSupportFunc): boolean;
}
export interface WXApiEventHandler {
    onReq(req: BaseReq): void;
    onResp(resp: BaseResp): void;
}
export declare class WXAPIFactory {
    static createWXAPI(r17: string, s17?: common.UIAbilityContext): WXApi;
    static createReq(o17: object): BaseReq | undefined;
    static createResp(l17: object): BaseResp | undefined;
}
