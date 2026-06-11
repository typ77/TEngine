using System.Collections.Generic;
using GameLogic.UI.FancyScrollView;
using TEngine;

namespace GameLogic
{
    /// <summary>
    /// Example01 基础列表示例。
    /// 显示 20 个带滚动动画的 Cell。
    /// </summary>
    [Window(UILayer.UI)]
    public class Example01_BasicUI : UIWindow
    {
        private Example01_BasicScrollViewWidget _scrollViewWidget;

        protected override void ScriptGenerator()
        {
            _scrollViewWidget = CreateWidget<Example01_BasicScrollViewWidget>("m_tf_ScrollView");
        }

        protected override async void OnCreate()
        {
            await _scrollViewWidget.InitializeAsync("Example01_BasicCell");

            var items = new List<Example01_BasicItemData>();
            for (int i = 0; i < 20; i++)
            {
                items.Add(new Example01_BasicItemData($"Cell {i}"));
            }
            _scrollViewWidget.UpdateData(items);
        }

        protected override void OnDestroy()
        {
            Log.Info("[Example01_BasicUI] Destroyed.");
        }
    }
}
