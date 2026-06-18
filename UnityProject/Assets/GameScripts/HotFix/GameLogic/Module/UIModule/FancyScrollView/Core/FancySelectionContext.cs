namespace GameLogic.UI.FancyScrollView
{
    /// <summary>含选中索引的 Cell 共享上下文基类，供需要选中态的 FancyScrollView 列表复用。
    /// 纯选中态场景可直接作为 TContext 使用；需要额外字段（布局/回调等）的列表可继承本类扩展。</summary>
    public class FancySelectionContext
    {
        /// <summary>当前选中索引（-1 表示未选中）。</summary>
        public int SelectedIndex = -1;
    }
}
