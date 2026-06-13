using GameLogic.UI.FancyScrollView;

namespace GameLogic
{
    /// <summary>
    /// Example02 聚焦 ScrollView Widget（非泛型）。
    /// 处理 Cell 点击后的聚焦滚动。
    /// </summary>
    public class Example02_FocusOnScrollViewWidget : FancyScrollerWidget
    {
        private Example02_FocusOnContext Ctx => (Example02_FocusOnContext)SharedContext;

        protected override IFancyCell CreateCell() => new Example02_FocusOnCell();

        protected override void OnCellClicked(int index)
        {
            Ctx.SelectedIndex = index;
            ScrollTo(index, 0.3f);
            Refresh();
        }
    }
}
