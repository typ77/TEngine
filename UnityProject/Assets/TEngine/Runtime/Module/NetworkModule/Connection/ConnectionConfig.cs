using System;

namespace TEngine
{
    /// <summary>
    /// 连接配置（Builder 模式）。通过链式 UseXxx / SetXxx 方法组装一条连接的全部参数，
    /// 由 <see cref="INetworkModule.CreateConnection"/> 消费。构造后字段不可变（private set），
    /// <see cref="Validate"/> 在创建连接时校验必填项。
    /// </summary>
    public class ConnectionConfig
    {
        // ---- 基本 ----
        /// <summary>连接名称（模块内唯一标识，必填）。</summary>
        public string Name { get; }

        // ---- 传输 ----
        public TransportType Transport { get; private set; }
        public string PlatformId { get; private set; }
        public string Host { get; private set; }
        public int Port { get; private set; }
        public string Url { get; private set; }

        // ---- 编解码 ----
        public CodecType Codec { get; private set; }

        // ---- 心跳 ----
        /// <summary>心跳间隔（秒）。</summary>
        public int HeartbeatInterval { get; private set; } = 10;
        /// <summary>心跳超时（秒，连续丢失阈值）。</summary>
        public int HeartbeatTimeout { get; private set; } = 30;

        // ---- 重连 ----
        public bool AutoReconnect { get; private set; } = true;
        public int ReconnectInterval { get; private set; } = 1000;
        public int MaxReconnectAttempts { get; private set; } = 5;
        public float BackoffMultiplier { get; private set; } = 1.0f;
        public int MaxReconnectInterval { get; private set; } = 30000;

        // ---- 缓冲（KB）----
        public int SendBufferSize { get; private set; } = 64;
        public int RecvBufferSize { get; private set; } = 64;

        // ---- 协议安全 ----
        /// <summary>单包负载上限（字节）。入站包声明长度超过此值即判定为恶意/异常，触发断连，防御 OOM 与内存放大。默认 1 MiB（1048576）。</summary>
        public int MaxPacketSize { get; private set; } = 1048576;

        // ---- 分发预算 ----
        public int MaxDispatchPerFrame { get; private set; } = 50;
        public int MaxDispatchTimeMs { get; private set; } = 4;

        // ---- 连接超时（毫秒）----
        public int ConnectTimeout { get; private set; } = 5000;

        // ---- 必填项标志 ----
        private bool _transportSet;
        private bool _codecSet;
        private bool _endpointSet;

        public ConnectionConfig(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        // ---- 传输方式 ----
        public ConnectionConfig UseTcp() { Transport = TransportType.Tcp; _transportSet = true; return this; }
        public ConnectionConfig UseKcp() { Transport = TransportType.Kcp; _transportSet = true; return this; }
        public ConnectionConfig UseWebSocket() { Transport = TransportType.WebSocket; _transportSet = true; return this; }
        public ConnectionConfig UsePlatform(string platformId) { Transport = TransportType.Platform; PlatformId = platformId; _transportSet = true; return this; }

        // ---- 编解码 ----
        public ConnectionConfig UseMessagePack() { Codec = CodecType.MessagePack; _codecSet = true; return this; }
        public ConnectionConfig UseProtobuf() { Codec = CodecType.Protobuf; _codecSet = true; return this; }

        // ---- 端点 ----
        public ConnectionConfig SetEndpoint(string host, int port) { Host = host; Port = port; _endpointSet = true; return this; }
        public ConnectionConfig SetUrl(string url) { Url = url; _endpointSet = true; return this; }

        // ---- 心跳 / 重连 / 缓冲 / 分发 / 超时 ----
        public ConnectionConfig SetHeartbeat(int intervalSec, int timeoutSec) { HeartbeatInterval = intervalSec; HeartbeatTimeout = timeoutSec; return this; }
        public ConnectionConfig SetReconnect(bool auto, int intervalMs, int maxAttempts, float backoff = 1.0f, int maxIntervalMs = 30000) { AutoReconnect = auto; ReconnectInterval = intervalMs; MaxReconnectAttempts = maxAttempts; BackoffMultiplier = backoff; MaxReconnectInterval = maxIntervalMs; return this; }
        public ConnectionConfig SetBuffer(int sendKB, int recvKB) { SendBufferSize = sendKB; RecvBufferSize = recvKB; return this; }
        /// <summary>设置单包负载上限（字节）。入站包超过此长度将触发断连，防御恶意超大包导致的 OOM。</summary>
        public ConnectionConfig SetMaxPacketSize(int maxBytes) { MaxPacketSize = maxBytes; return this; }
        public ConnectionConfig SetDispatchBudget(int maxPerFrame, int maxTimeMs = 4) { MaxDispatchPerFrame = maxPerFrame; MaxDispatchTimeMs = maxTimeMs; return this; }
        public ConnectionConfig SetConnectTimeout(int ms) { ConnectTimeout = ms; return this; }

        /// <summary>
        /// 校验必填项。由 NetworkModule / NetworkConnection 在创建连接时调用。
        /// </summary>
        internal void Validate()
        {
            if (string.IsNullOrEmpty(Name)) throw new ArgumentException("连接名称（Name）不能为空", nameof(Name));
            if (!_transportSet) throw new ArgumentException("必须指定传输方式（UseTcp/UseKcp/UseWebSocket/UsePlatform）");
            if (!_codecSet) throw new ArgumentException("必须指定编解码方式（UseMessagePack/UseProtobuf）");
            if (!_endpointSet) throw new ArgumentException("必须指定端点（SetEndpoint 或 SetUrl）");

            // 端点边界：host:port 模式（Host 非空）校验 Host 非空 + 端口范围；url 模式（Host 为 null）校验 Url 非空
            if (Host != null)
            {
                if (string.IsNullOrWhiteSpace(Host)) throw new ArgumentException("Host 不能为空", nameof(Host));
                if (Port < 0 || Port > 65535) throw new ArgumentException($"Port 越界：{Port}（合法范围 0–65535）", nameof(Port));
            }
            else if (string.IsNullOrEmpty(Url))
            {
                throw new ArgumentException("端点不完整：须通过 SetEndpoint 设置 host:port 或 SetUrl 设置 url");
            }

            // 心跳 / 重连 / 退避边界
            if (HeartbeatInterval <= 0) throw new ArgumentException("HeartbeatInterval 必须为正", nameof(HeartbeatInterval));
            if (HeartbeatTimeout < HeartbeatInterval)
                throw new ArgumentException($"HeartbeatTimeout（{HeartbeatTimeout}）须 >= HeartbeatInterval（{HeartbeatInterval}）", nameof(HeartbeatTimeout));
            if (ReconnectInterval <= 0) throw new ArgumentException("ReconnectInterval 必须为正", nameof(ReconnectInterval));
            if (MaxReconnectInterval < ReconnectInterval)
                throw new ArgumentException($"MaxReconnectInterval（{MaxReconnectInterval}）须 >= ReconnectInterval（{ReconnectInterval}）", nameof(MaxReconnectInterval));
            if (BackoffMultiplier < 1.0f)
                throw new ArgumentException($"BackoffMultiplier（{BackoffMultiplier}）须 >= 1.0（防反向退避轰炸对端）", nameof(BackoffMultiplier));

            // 分发 / 协议安全
            if (MaxDispatchPerFrame <= 0) throw new ArgumentException("MaxDispatchPerFrame 必须为正", nameof(MaxDispatchPerFrame));
            if (MaxPacketSize <= 0) throw new ArgumentException("MaxPacketSize 必须为正", nameof(MaxPacketSize));
        }
    }
}
