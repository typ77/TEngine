using System;

namespace TEngine
{
    /// <summary>
    /// 消息序列化 / 反序列化失败。
    /// </summary>
    public class SerializationException : NetworkException
    {
        /// <summary>出错的消息 Id。</summary>
        public ushort MsgId { get; }

        /// <summary>出错方向（"Send"/"Receive" 等）。</summary>
        public string Direction { get; }

        public SerializationException(string connectionName, ushort msgId,
            string direction, Exception inner)
            : base(connectionName, $"{direction} failed for msgId={msgId}")
        {
            MsgId = msgId;
            Direction = direction;
        }
    }
}
