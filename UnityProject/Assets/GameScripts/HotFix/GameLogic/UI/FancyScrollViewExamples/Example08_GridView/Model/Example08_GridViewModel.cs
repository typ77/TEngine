using GameLogic.DataBinding;

namespace GameLogic
{
    /// <summary>Example08 数据模型，持有默认配置。</summary>
    public class Example08_GridViewModel : Singleton<Example08_GridViewModel>
    {
        /// <summary>默认数据条目数。</summary>
        public const int DefaultItemCount = 100;

        /// <summary>默认跳转索引。</summary>
        public const int DefaultSelectIndex = 50;

        /// <summary>每行/列 Cell 数量。</summary>
        public const int StartAxisCellCount = 4;
        
    }

    /// <summary>对齐方式（复用 07 的定义，08 独立声明避免跨 Example 依赖）。</summary>
    public enum Example08_Alignment
    {
        Upper = 0,
        Middle = 1,
        Lower = 2,
    }
}
