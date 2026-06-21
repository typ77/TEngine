namespace TEngine
{
    /// <summary>
    /// 连接配置。T1 仅占位以保证接口契约可编译；Builder 模式的完整字段（Endpoint/Url/Heartbeat/
    /// Reconnect/Buffer/DispatchBudget/ConnectTimeout 等）由后续任务（T16）填充。
    /// </summary>
    public class ConnectionConfig
    {
        // TODO(T16): 补充 Name、Endpoint、TransportType、CodecType、心跳、重连、缓冲、分发预算等字段与 Builder API。
    }
}
