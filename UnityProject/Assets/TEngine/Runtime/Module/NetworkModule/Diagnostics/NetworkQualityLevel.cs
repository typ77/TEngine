namespace TEngine
{
    /// <summary>
    /// 网络质量等级（由 <see cref="QualityEstimator"/> 根据 RTT / 丢包率估算）。
    /// </summary>
    public enum NetworkQualityLevel
    {
        /// <summary>优秀（RTT&lt;50ms, 丢包&lt;1%）。</summary>
        Excellent,

        /// <summary>良好（RTT&lt;100ms, 丢包&lt;3%）。</summary>
        Good,

        /// <summary>一般（RTT&lt;200ms, 丢包&lt;10%）。</summary>
        Fair,

        /// <summary>较差（RTT&lt;500ms, 丢包&lt;30%）。</summary>
        Poor,

        /// <summary>很差（超出上述阈值）。</summary>
        Bad,
    }
}
