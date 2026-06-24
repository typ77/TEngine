using TEngine;

/// <summary>
/// 示例网络消息定义（占位版）。
/// </summary>
/// <remarks>
/// 当前为纯 <see cref="IMessage"/> 标记类型，不含序列化特性；
/// 待 T6b 安装 MessagePack 后，再补 <c>[MessagePackObject]</c>/<c>[Key]</c> 特性以支持真实序列化。
/// </remarks>
public class LoginReq : IMessage
{
    public string Account { get; set; }

    public string Token { get; set; }
}

/// <summary>登录响应。</summary>
public class LoginResp : IMessage
{
    public int Code { get; set; }

    public long Uid { get; set; }
}
