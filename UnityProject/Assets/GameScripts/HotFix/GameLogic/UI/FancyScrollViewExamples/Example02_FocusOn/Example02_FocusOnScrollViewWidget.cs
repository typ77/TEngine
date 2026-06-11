using GameLogic.UI.FancyScrollView;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// Example02 聚焦 ScrollView Widget。
    /// 处理 Cell 点击后的聚焦滚动。
    /// </summary>
    public class Example02_FocusOnScrollViewWidget : FancyScrollerWidget<Example02_FocusOnItemData, Example02_FocusOnContext>
    {
        protected override void ScriptGenerator()
        {
            var scroller = FindChildComponent<Scroller>("m_scroller");
            var cellContainer = FindChild("m_cellContainer");
            BindScroller(scroller, cellContainer);
        }

        protected override FancyCellWidget<Example02_FocusOnItemData, Example02_FocusOnContext> CreateCellWidget()
            => new Example02_FocusOnCell();

        protected override void OnCellClicked(int index)
        {
            SharedContext.SelectedIndex = index;
            ScrollTo(index, 0.3f);
            Refresh();
        }
    }
}
