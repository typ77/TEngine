namespace TEngine
{
    /// <summary>
    /// 网络消息标记接口。
    /// <remarks>所有走网络模块收发的消息类型都必须实现此接口，以便 Codec 序列化与 Dispatcher 分发。</remarks>
    /// </summary>
    public interface IMessage { }
}
