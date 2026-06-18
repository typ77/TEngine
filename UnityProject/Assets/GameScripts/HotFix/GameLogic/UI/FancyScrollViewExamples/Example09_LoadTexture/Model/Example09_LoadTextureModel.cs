namespace GameLogic
{
    /// <summary>Example09 数据模型，持有条目数量。</summary>
    public class Example09_LoadTextureModel : Singleton<Example09_LoadTextureModel>
    {
        /// <summary>贴图条目数量（固定 10 条，对应原版 9 个 Example + 主图）。</summary>
        public const int ItemCount = 10;

        /// <summary>贴图 URL 前缀（原版 setchi.jp 资源站）。</summary>
        public const string ImageUrlPrefix = "https://setchi.jp/FancyScrollView/09_LoadTexture/Images/";
    }
}
