using System.Collections.Generic;
using GameLogic.UI.FancyScrollView;
using TEngine;

namespace GameLogic
{
    /// <summary>
    /// Example03 无限循环滚动示例。
    /// 滚动到末尾后循环回到开头，默认选中第一个 Cell。
    /// </summary>
    [Window(UILayer.UI)]
    public class Example03_InfiniteScrollUI : UIWindow
    {
        private Example03_InfiniteScrollViewWidget _scrollViewWidget;

        protected override void ScriptGenerator()
        {
            _scrollViewWidget = CreateWidget<Example03_InfiniteScrollViewWidget>("m_tf_ScrollView");
        }

        protected override async void OnCreate()
        {
            await _scrollViewWidget.InitializeAsync("Example03_InfiniteScrollCell");

            var items = new List<Example03_InfiniteScrollItemData>();
            for (int i = 0; i < 20; i++)
            {
                items.Add(new Example03_InfiniteScrollItemData($"Cell {i}"));
            }
            _scrollViewWidget.UpdateData(items);

            // 默认选中第一个
            _scrollViewWidget.JumpTo(0);
        }

        protected override void OnDestroy()
        {
            Log.Info("[Example03_InfiniteScrollUI] Destroyed.");
        }
    }
}
