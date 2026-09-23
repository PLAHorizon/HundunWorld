export declare enum OAuthErrCode {
    WechatAuth_Err_OK = 0,
    WechatAuth_Err_NormalErr = -1,
    WechatAuth_Err_NetworkErr = -2,
    WechatAuth_Err_JsonDecodeErr = -3,
    WechatAuth_Err_Cancel = -4,
    WechatAuth_Err_Timeout = -5,
    WechatAuth_Err_Auth_Stopped = -6,
    WechatAuth_Err_Param_Err = -7
}
export interface OAuthCallback {
    onGotQRCode: (base64JpegImageBuffer: string) => void;
    onQRCodeScanned: () => void;
    onAuthFinish: (authCode: string) => void;
    onAuthError: (errCode: OAuthErrCode, errMsg: string) => void;
}
export interface IDiffDevOAuth {
    /**
     * 发起OAuth
     *
     * @param appId 待授权appid
     * @param scope 权限
     * @param noncestr
     * @param timestamp
     * @param signature
     * @return 是否成功发起OAuth
     */
    startOAuth(appId: string, scope: string, noncestr: string, timestamp: string, signature: string, callback: OAuthCallback): void;
    stopOAuth(): void;
}
export declare class DiffDevOAuthFactory {
    static readonly VERSION_1 = 1;
    /**
     * 最大支持的IDiffDevOAuth实现版本
     */
    static readonly MAX_SUPPORTED_VERSION = 1;
    private constructor();
    /**
     * 获取IDiffDevOAuth指定版本的实现
     *
     * @param version
     */
    static getDiffDevOAuth(f1?: number): IDiffDevOAuth | null;
}
