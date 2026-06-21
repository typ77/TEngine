using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace TEngine
{
    /// <summary>
    /// 网络模块顶层接口。负责多连接生命周期管理、消息/编解码/传输/拦截器的全局注册，
    /// 以及连接级事件的广播。运行期通过 <c>GameModule.Network</c> 访问。
    /// </summary>
    public interface INetworkModule
    {
        // ---- 连接管理 ----

        /// <summary>按配置创建并注册一条连接。</summary>
        INetworkConnection CreateConnection(ConnectionConfig config);

        /// <summary>按名获取连接（不存在返回 null）。</summary>
        INetworkConnection GetConnection(string name);

        /// <summary>按名获取连接；不存在则返回默认连接。</summary>
        INetworkConnection GetConnectionOrDefault(string name);

        /// <summary>销毁并释放指定连接。</summary>
        void DestroyConnection(string name);

        /// <summary>是否存在指定名称的连接。</summary>
        bool HasConnection(string name);

        // ---- 消息注册 ----

        /// <summary>注册单个消息类型。</summary>
        void RegisterMessage<T>(ushort msgId, MessagePriority priority = MessagePriority.Normal)
            where T : IMessage;

        /// <summary>批量注册消息类型。</summary>
        void RegisterMessages(IDictionary<ushort, (Type messageType, MessagePriority priority)> entries);

        /// <summary>查询 msgId 对应的优先级。</summary>
        MessagePriority GetMessagePriority(ushort msgId);

        // ---- 全局注册 ----

        /// <summary>注册编解码器类型（按 CodecType 选用）。</summary>
        void RegisterCodec<T>(CodecType type) where T : ICodec, new();

        /// <summary>注册平台传输适配器（按平台 Id 选用）。</summary>
        void RegisterPlatformTransport(string platformId, ITransport transport);

        /// <summary>追加全局拦截器（作用于所有连接）。</summary>
        void AddInterceptor(IMessageInterceptor interceptor);

        /// <summary>按名移除全局拦截器。</summary>
        void RemoveInterceptor(string name);

        /// <summary>设置包格式化器（默认使用 <see cref="DefaultPacketFormatter"/>）。</summary>
        void SetPacketFormatter(IPacketFormatter formatter);

        // ---- 全局事件 ----

        /// <summary>任意连接状态变化时触发。</summary>
        event Action<string, ConnectionState> OnConnectionStateChanged;

        /// <summary>任意连接发生错误时触发。</summary>
        event Action<string, Exception> OnError;
    }
}
