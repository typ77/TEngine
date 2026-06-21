namespace TEngine
{
    /// <summary>
    /// 网络诊断数据聚合（RTT/抖动/丢包/流量/质量等级等）。T1 仅占位以保证
    /// <see cref="INetworkConnection.Diagnostics"/> 可编译；具体字段由 PingMonitor/TrafficCounter/
    /// QualityEstimator（T15）填充。
    /// </summary>
    public class NetworkDiagnostics
    {
        // TODO(T15): 接入 PingMonitor/TrafficCounter/QualityEstimator 的实时数据。
    }
}
