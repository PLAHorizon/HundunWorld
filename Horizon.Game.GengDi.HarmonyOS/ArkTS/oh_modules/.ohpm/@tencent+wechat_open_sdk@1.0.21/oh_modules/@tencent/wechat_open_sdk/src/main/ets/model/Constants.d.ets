export declare const kWechatBundleName = "com.tencent.wechat";
export declare const kWechatEntryAbility = "EntryAbility";
export declare enum Command {
    kCommandSendAuth = 1,
    kCommandSendMessageToWX = 2,
    kCommandPay = 5,
    kCommandLaunchFromWX = 6,
    kCommandOpenWebView = 12,
    kCommandLaunchMiniProgram = 19,
    kCommandOpenBusinessWebView = 25,
    kCommandOpenBusinessView = 26,
    kCommandJointPay = 27,
    kCommandWxaRedirectingPage = 30,
    kCommandSendTdiAuth = 31,
    kCommandOpenCustomerServiceChat = 37,
    kCommandSubscribeMessage = 18,
    kCommandInvoiceAuthInsert = 20,
    kCommandNontaxPay = 21,
    kCommandPayInsurance = 22,
    kCommandGetA8Key = 43
}
export declare enum WXAppSupportFunc {
    GetA8Key = "get_a8_key",
    WeWorkObjectMulti = "wework_object_multi"
}
export declare namespace WXMiniProgramConstants {
    const KEY_ACTIVITY_ID = "wx_miniprogram_key_activity_id";
    const KEY_PARAMETER_INFO_LIST = "wx_miniprogram_key_parameter_info_list";
}
