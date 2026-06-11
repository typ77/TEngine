namespace GameLogic.UI.FancyScrollView
{
    /// <summary>
    /// Cell 内容异步加载状态。
    /// </summary>
    public enum ContentLoadState
    {
        /// <summary>仅骨架占位，尚未加载。</summary>
        Skeleton,
        /// <summary>正在加载。</summary>
        Loading,
        /// <summary>加载完成。</summary>
        Loaded,
        /// <summary>加载失败。</summary>
        Error,
    }
}
