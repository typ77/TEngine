namespace GameLogic
{
    /// <summary>Example07 数据模型，持有默认数据数量与对齐方式。</summary>
    public class Example07_ScrollRectModel : Singleton<Example07_ScrollRectModel>
    {
        /// <summary>默认数据条目数。</summary>
        public const int DefaultItemCount = 20;

        /// <summary>默认跳转索引。</summary>
        public const int DefaultSelectIndex = 10;
    }

    /// <summary>对齐方式（对应原版 Alignment 枚举）。</summary>
    public enum Example07_Alignment
    {
        Upper = 0,
        Middle = 1,
        Lower = 2,
    }
}
