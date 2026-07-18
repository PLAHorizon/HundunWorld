namespace Horizon.Game.Message.Sync;

/// <summary>
/// 交互状态位编码的单一事实源。
/// 下行状态位（服务端→客户端）：Start/End/Stolen
/// 上行意图位（客户端→服务端）：RequestStartFlag/RequestStopFlag（使用高位避免与下行状态位冲突）
/// </summary>
public static class InteractionStateBits
{
    // 下行状态位（StateBits 低 3 位）
    public const byte Start = 0x01;           // bit0 = 交互开始/占用
    public const byte End = 0x02;             // bit1 = 交互结束
    public const byte Stolen = 0x04;          // bit2 = 被抢占

    // 上行意图位（StateBits 高位，与下行状态位不冲突）
    public const byte RequestStartFlag = 0x80; // bit7 = 客户端请求开始交互
    public const byte RequestStopFlag = 0x40;  // bit6 = 客户端请求停止交互

    // 下行状态位掩码
    public const byte StateMask = 0x07;        // 低 3 位为状态位
    public const byte IntentMask = 0xC0;       // 高 2 位为意图位

    // 辅助方法
    public static bool IsStart(byte stateBits) => (stateBits & Start) != 0;
    public static bool IsEnd(byte stateBits) => (stateBits & End) != 0;
    public static bool IsStolen(byte stateBits) => (stateBits & Stolen) != 0;
    public static bool IsTerminal(byte stateBits) => (stateBits & (End | Stolen)) != 0;
    public static bool IsRequestStart(byte stateBits) => (stateBits & RequestStartFlag) != 0;
    public static bool IsRequestStop(byte stateBits) => (stateBits & RequestStopFlag) != 0;
}
