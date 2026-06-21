using System;
using System.Buffers;

namespace TEngine
{
    /// <summary>
    /// 字节缓冲池。包装 <see cref="ArrayPool{T}"/>，额外统计活跃计数与字节数，
    /// 供网络模块的包组装 / 解析复用，减少 GC 压力。
    /// <para>使用约定：<c>Rent</c> 与 <c>Return</c> 必须配对；<c>Return</c> 对 null / 重复归还安全。</para>
    /// </summary>
    public static class BufferPool
    {
        private static int _activeCount;
        private static long _activeBytes;

        /// <summary>当前未归还的缓冲数量。</summary>
        public static int ActiveCount => _activeCount;

        /// <summary>当前未归还的缓冲总字节数。</summary>
        public static long ActiveBytes => _activeBytes;

        /// <summary>租用一个不小于 <paramref name="size"/> 的缓冲。</summary>
        public static byte[] Rent(int size)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(size);
            _activeCount++;
            _activeBytes += buffer.Length;
            return buffer;
        }

        /// <summary>租用并清零。</summary>
        public static byte[] RentCleared(int size)
        {
            var buffer = Rent(size);
            Array.Clear(buffer, 0, buffer.Length);
            return buffer;
        }

        /// <summary>归还缓冲。null 安全；重复归还不会使计数为负。</summary>
        public static void Return(byte[] buffer)
        {
            if (buffer == null) return;
            _activeCount = Math.Max(0, _activeCount - 1);
            _activeBytes = Math.Max(0, _activeBytes - buffer.Length);
            ArrayPool<byte>.Shared.Return(buffer);
        }

        /// <summary>重置统计计数（仅用于测试 / 模块销毁；不回收已租出的缓冲）。</summary>
        public static void Clear()
        {
            _activeCount = 0;
            _activeBytes = 0;
        }
    }
}
