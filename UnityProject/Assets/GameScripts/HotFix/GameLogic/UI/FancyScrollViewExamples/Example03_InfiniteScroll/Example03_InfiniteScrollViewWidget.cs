using GameLogic.UI.FancyScrollView;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// Example03 无限循环 ScrollView Widget。
    /// 启用 Loop 模式实现无限滚动。
    /// </summary>
    public class Example03_InfiniteScrollViewWidget : FancyScrollerWidget<Example03_InfiniteScrollItemData, NullContext>
    {
        protected override float CellInterval => 0.2f;
        protected override float ScrollOffset => 0.5f;
        protected override bool Loop => true;

        protected override void ScriptGenerator()
        {
            var scroller = FindChildComponent<Scroller>("m_scroller");
            var cellContainer = FindChild("m_cellContainer");
            BindScroller(scroller, cellContainer);
        }

        protected override FancyCellWidget<Example03_InfiniteScrollItemData, NullContext> CreateCellWidget()
            => new Example03_InfiniteScrollCell();
    }
}
