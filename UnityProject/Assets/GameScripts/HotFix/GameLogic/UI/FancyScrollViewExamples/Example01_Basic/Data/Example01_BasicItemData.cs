namespace GameLogic
{
    /// <summary>Example01 列表条目数据，由 Service 构建后传递给 Cell。</summary>
    public class Example01_BasicItemData
    {
        /// <summary>随机 ID。</summary>
        public int Id { get; set; }

        /// <summary>展示文本，如 "Cell 0 id:1234"。</summary>
        public string DisplayText { get; set; }

        public Example01_BasicItemData(int id)
        {
            Id = id;
        }
    }
}
