namespace GameLogic
{
    /// <summary>Example09 列表条目数据：标题、描述、贴图 URL。</summary>
    public class Example09_LoadTextureItemData
    {
        /// <summary>索引 ID。</summary>
        public int Id { get; set; }

        /// <summary>标题。</summary>
        public string Title { get; set; }

        /// <summary>描述。</summary>
        public string Description { get; set; }

        /// <summary>贴图网络 URL。</summary>
        public string Url { get; set; }

        public Example09_LoadTextureItemData(int id)
        {
            Id = id;
        }
    }
}
