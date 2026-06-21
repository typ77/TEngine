namespace TEngine
{
    /// <summary>
    /// 系统消息 Id 约定。预留两端区间：
    /// <list type="bullet">
    /// <item>[0x0000, 0x0001] 与 [0xFFFC, 0xFFFF]：系统消息（心跳 / Ping / RPC 恢复）。</item>
    /// <item>[0x0002, 0xFFFB]：业务用户消息区间。</item>
    /// </list>
    /// </summary>
    public static class SystemMessageIds
    {
        // ---- 心跳 ----
        public const ushort HeartbeatRequest = 0x0000;
        public const ushort HeartbeatResponse = 0x0001;

        // ---- Ping（网络质量探测）----
        public const ushort PingResponse = 0xFFFC;
        public const ushort PingRequest = 0xFFFD;

        // ---- RPC 断线恢复 ----
        public const ushort RpcRecoverRequest = 0xFFFE;
        public const ushort RpcRecoverResponse = 0xFFFF;

        // ---- 业务消息区间 ----
        public const ushort UserMin = 0x0002;
        public const ushort UserMax = 0xFFFB;

        /// <summary>判断 msgId 是否落在业务用户消息区间。</summary>
        public static bool IsUserMsgId(ushort msgId)
        {
            return msgId >= UserMin && msgId <= UserMax;
        }
    }
}
