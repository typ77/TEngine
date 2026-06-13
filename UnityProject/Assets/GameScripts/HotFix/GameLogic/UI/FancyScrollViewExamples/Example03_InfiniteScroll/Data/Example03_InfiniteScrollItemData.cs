namespace GameLogic
{
    /// <summary>Example03 列表条目数据，由 Service 构建后传递给 Cell。</summary>
    public class Example03_InfiniteScrollItemData
    {
        /// <summary>索引 ID。</summary>
        public int Id { get; set; }

        /// <summary>展示文本，如 "Cell 0"。</summary>
        public string DisplayText { get; set; }

        public Example03_InfiniteScrollItemData(int id)
        {
            Id = id;
        }
    }
}
