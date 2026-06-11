using GameLogic.UI.FancyScrollView;

namespace GameLogic
{
    /// <summary>
    /// Example02 聚焦滚动的 Context。
    /// 通过 SharedContext 在 Widget 和 Cell 之间共享选中状态。
    /// </summary>
    public class Example02_FocusOnContext
    {
        public int SelectedIndex = -1;
    }

    /// <summary>
    /// Example02 聚焦滚动的 ItemData。
    /// </summary>
    public class Example02_FocusOnItemData
    {
        public string Message { get; }

        public Example02_FocusOnItemData(string message)
        {
            Message = message;
        }
    }
}
