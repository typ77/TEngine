namespace TEngine
{
    /// <summary>
    /// 重连次数耗尽仍失败。
    /// </summary>
    public class ReconnectFailedException : NetworkException
    {
        /// <summary>已尝试的重连次数。</summary>
        public int AttemptCount { get; }

        public ReconnectFailedException(string connectionName, int attempts)
            : base(connectionName, $"Reconnect failed after {attempts} attempts")
        {
            AttemptCount = attempts;
        }
    }
}
