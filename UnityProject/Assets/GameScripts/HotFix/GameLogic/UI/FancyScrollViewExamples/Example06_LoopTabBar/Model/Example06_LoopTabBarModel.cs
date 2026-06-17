using GameLogic.DataBinding;

namespace GameLogic
{
    /// <summary>Example06 数据模型，持有 Tab 数量与选中索引。</summary>
    public class Example06_LoopTabBarModel : Singleton<Example06_LoopTabBarModel>
    {
        /// <summary>Tab 总数（与内容面板数量一致）。</summary>
        public const int TabCount = 4;

        /// <summary>当前选中的 Tab 索引，响应式。</summary>
        public readonly BindableProperty<int> SelectedIndex = new(0);

        protected override void OnRelease()
        {
            SelectedIndex.Dispose();
            base.OnRelease();
        }
    }
}
