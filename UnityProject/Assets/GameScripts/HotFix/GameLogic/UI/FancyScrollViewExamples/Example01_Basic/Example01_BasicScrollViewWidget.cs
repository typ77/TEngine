using GameLogic.UI.FancyScrollView;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// Example01 基础 ScrollView Widget。
    /// </summary>
    public class Example01_BasicScrollViewWidget : FancyScrollerWidget<Example01_BasicItemData, NullContext>
    {
        protected override void ScriptGenerator()
        {
            var scroller = FindChildComponent<Scroller>("m_scroller");
            var cellContainer = FindChild("m_cellContainer");
            BindScroller(scroller, cellContainer);
        }

        protected override FancyCellWidget<Example01_BasicItemData, NullContext> CreateCellWidget()
            => new Example01_BasicCell();
    }
}
