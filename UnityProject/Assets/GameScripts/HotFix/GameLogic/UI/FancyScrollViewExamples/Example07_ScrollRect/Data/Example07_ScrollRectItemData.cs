namespace GameLogic
{
    /// <summary>Example07 列表条目数据。</summary>
    public class Example07_ScrollRectItemData
    {
        /// <summary>索引 ID。</summary>
        public int Id { get; set; }

        /// <summary>展示文本。</summary>
        public string DisplayText { get; set; }

        public Example07_ScrollRectItemData(int id)
        {
            Id = id;
        }
    }
}
