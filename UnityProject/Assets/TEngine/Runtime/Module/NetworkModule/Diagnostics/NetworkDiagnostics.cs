using System;

namespace TEngine
{
    /// <summary>
    /// 网络诊断数据聚合。汇总 Ping（RTT/抖动/丢包）、流量（字节/消息收发）、
    /// RPC（pending/completed/failed）、质量等级与分发统计，供业务层观测与调试器展示。
    /// <para>流量计数（<c>TrafficCounter</c>）逻辑内嵌于此，不单独建类。</para>
    /// </summary>
    public sealed class NetworkDiagnostics
    {
        // ---- Ping ----
        /// <summary>最近一次往返时延（毫秒）。</summary>
        public float RTT { get; private set; }

        /// <summary>平均 RTT（滑动窗口，后续完善）。</summary>
        public float AvgRTT { get; private set; }

        /// <summary>抖动（RTT 方差，后续完善）。</summary>
        public float Jitter { get; private set; }

        /// <summary>丢包率（0~1）。</summary>
        public float PacketLoss { get; private set; }

        /// <summary>是否启用 Ping 探测。</summary>
        public bool PingEnabled { get; set; }

        /// <summary>Ping 发送间隔（毫秒）。</summary>
        public int PingIntervalMs { get; set; } = 2000;

        // ---- Traffic（内嵌 TrafficCounter）----
        public long BytesSent { get; private set; }
        public long BytesReceived { get; private set; }
        public int MessagesSent { get; private set; }
        public int MessagesReceived { get; private set; }

        // ---- RPC ----
        public int RpcsPending { get; private set; }
        public int RpcsCompleted { get; private set; }
        public int RpcsFailed { get; private set; }

        // ---- Quality ----
        public NetworkQualityLevel Quality { get; private set; }

        /// <summary>质量等级变化时触发。</summary>
        public event Action<NetworkQualityLevel> OnQualityChanged;

        // ---- Dispatch ----
        /// <summary>最近一次分发统计（由 MessageDispatcher 回填）。</summary>
        public DispatchStats Dispatch { get; set; }

        // ---- 流量记录 ----
        public void RecordSend(int bytes)
        {
            BytesSent += bytes;
            MessagesSent++;
        }

        public void RecordReceive(int bytes)
        {
            BytesReceived += bytes;
            MessagesReceived++;
        }

        // ---- RPC 记录 ----
        public void RecordRpcStarted() { RpcsPending++; }
        public void RecordRpcCompleted() { RpcsPending--; RpcsCompleted++; }
        public void RecordRpcFailed() { RpcsPending--; RpcsFailed++; }

        // ---- Ping 回填（PingMonitor 调用）----
        public void UpdatePing(float rtt)
        {
            RTT = rtt;
            // 滑动窗口平均值 / 抖动可后续实现
        }

        /// <summary>质量等级回填（QualityEstimator 调用，带去重与事件）。</summary>
        internal void UpdateQuality(NetworkQualityLevel quality)
        {
            if (Quality == quality) return;
            Quality = quality;
            OnQualityChanged?.Invoke(quality);
        }

        /// <summary>重置连接相关指标（重连后调用）。</summary>
        public void ResetConnectionMetrics()
        {
            RTT = 0;
            Jitter = 0;
            PacketLoss = 0;
        }
    }
}
