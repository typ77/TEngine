using GameLogic.UI.FancyScrollView;

namespace GameLogic
{
    /// <summary>
    /// Example03 无限循环 ScrollView Widget（非泛型）。
    /// 启用 Loop 模式实现无限滚动。
    /// </summary>
    public class Example03_InfiniteScrollViewWidget : FancyScrollerWidget
    {
        protected override float CellInterval => 0.2f;
        protected override float ScrollOffset => 0.5f;
        protected override bool Loop => true;

        protected override IFancyCell CreateCell() => new Example03_InfiniteScrollCell();
    }
}
