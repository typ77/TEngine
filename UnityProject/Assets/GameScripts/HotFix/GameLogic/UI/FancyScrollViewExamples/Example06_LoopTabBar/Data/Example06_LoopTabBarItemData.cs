namespace GameLogic
{
    /// <summary>Example06 列表条目数据，对应一个 Tab。</summary>
    public class Example06_LoopTabBarItemData
    {
        /// <summary>展示文本，如 "Tab 0"。</summary>
        public string DisplayText { get; set; }

        public Example06_LoopTabBarItemData(string displayText)
        {
            DisplayText = displayText;
        }
    }
}
