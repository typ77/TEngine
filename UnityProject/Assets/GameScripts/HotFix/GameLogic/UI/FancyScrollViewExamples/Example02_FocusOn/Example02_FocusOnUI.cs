using System.Collections.Generic;
using GameLogic.UI.FancyScrollView;
using TEngine;

namespace GameLogic
{
    /// <summary>
    /// Example02 聚焦滚动示例。
    /// 点击 Cell 后平滑滚动到选中位置并居中高亮。
    /// </summary>
    [Window(UILayer.UI)]
    public class Example02_FocusOnUI : UIWindow
    {
        private Example02_FocusOnScrollViewWidget _scrollViewWidget;

        protected override void ScriptGenerator()
        {
            _scrollViewWidget = CreateWidget<Example02_FocusOnScrollViewWidget>("m_tf_ScrollView");
        }

        protected override async void OnCreate()
        {
            await _scrollViewWidget.InitializeAsync("Example02_FocusOnCell");

            var items = new List<Example02_FocusOnItemData>();
            for (int i = 0; i < 20; i++)
            {
                items.Add(new Example02_FocusOnItemData($"Cell {i}"));
            }
            _scrollViewWidget.UpdateData(items);
        }

        protected override void OnDestroy()
        {
            Log.Info("[Example02_FocusOnUI] Destroyed.");
        }
    }
}
