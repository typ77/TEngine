using System;
using System.Collections.Generic;
using System.Linq;

namespace TEngine
{
    /// <summary>
    /// 拦截器管道。按 <see cref="IMessageInterceptor.Order"/> 升序串联全局拦截器与连接级拦截器，
    /// 在发送 / 接收两个方向上依次调用；任一拦截器返回 false 或抛异常则中止并整体返回 false。
    /// <para>出站顺序：全局 → 连接级（同 Order 升序）。</para>
    /// <para>入站顺序：连接级 → 全局（同 Order 升序）。</para>
    /// <para>内部对拦截器链做脏标记缓存，仅在变更后重建，避免每条消息都排序。</para>
    /// </summary>
    public class InterceptorPipeline
    {
        private readonly List<IMessageInterceptor> _connectionInterceptors = new List<IMessageInterceptor>();
        private IReadOnlyList<IMessageInterceptor> _globals = Array.Empty<IMessageInterceptor>();
        private bool _dirty = true;
        private List<IMessageInterceptor> _sendCache;
        private List<IMessageInterceptor> _receiveCache;

        /// <summary>追加连接级拦截器（同名抛 <see cref="ArgumentException"/>）。</summary>
        public void Add(IMessageInterceptor interceptor)
        {
            if (_connectionInterceptors.Any(i => i.Name == interceptor.Name))
                throw new ArgumentException($"Interceptor '{interceptor.Name}' already exists");
            _connectionInterceptors.Add(interceptor);
            _dirty = true;
        }

        /// <summary>按名移除连接级拦截器。</summary>
        public void Remove(string name)
        {
            _connectionInterceptors.RemoveAll(i => i.Name == name);
            _dirty = true;
        }

        /// <summary>设置全局拦截器（由 INetworkModule 下发，作用于所有连接）。</summary>
        public void SetGlobalInterceptors(IReadOnlyList<IMessageInterceptor> globals)
        {
            _globals = globals ?? Array.Empty<IMessageInterceptor>();
            _dirty = true;
        }

        private void RebuildCache()
        {
            // 出站: 全局(升序) → 连接级(升序)
            _sendCache = _globals
                .Concat(_connectionInterceptors)
                .OrderBy(i => i.Order)
                .ToList();

            // 入站: 连接级(升序) → 全局(升序)
            _receiveCache = _connectionInterceptors
                .Concat(_globals)
                .OrderBy(i => i.Order)
                .ToList();

            _dirty = false;
        }

        /// <summary>发送方向处理。返回 false 表示消息被拦截器丢弃。</summary>
        public bool ProcessSending(ushort msgId, ref byte[] payload, in MessageTrace trace)
        {
            if (_dirty) RebuildCache();
            foreach (var interceptor in _sendCache)
            {
                try
                {
                    if (!interceptor.OnSending(msgId, ref payload, in trace))
                        return false;
                }
                catch
                {
                    // 单个拦截器异常不应击垮整条管道
                    return false;
                }
            }
            return true;
        }

        /// <summary>接收方向处理。返回 false 表示消息被拦截器丢弃（不进入分发）。</summary>
        public bool ProcessReceiving(ushort msgId, ref byte[] payload, in MessageTrace trace)
        {
            if (_dirty) RebuildCache();
            foreach (var interceptor in _receiveCache)
            {
                try
                {
                    if (!interceptor.OnReceiving(msgId, ref payload, in trace))
                        return false;
                }
                catch
                {
                    return false;
                }
            }
            return true;
        }
    }
}
