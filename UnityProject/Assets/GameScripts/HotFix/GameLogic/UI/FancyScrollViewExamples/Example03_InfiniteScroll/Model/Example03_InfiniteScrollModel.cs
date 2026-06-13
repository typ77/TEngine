using GameLogic.DataBinding;

namespace GameLogic
{
    /// <summary>Example03 数据模型，持有响应式 ID 属性。</summary>
    public class Example03_InfiniteScrollModel : Singleton<Example03_InfiniteScrollModel>
    {
        public const int ItemCount = 20;

        /// <summary>每个条目的 ID，保留响应式结构供后续扩展。</summary>
        public readonly BindableProperty<int>[] ItemIds = new BindableProperty<int>[ItemCount];

        protected override void OnInit()
        {
            for (int i = 0; i < ItemCount; i++)
            {
                ItemIds[i] = new BindableProperty<int>(i);
            }
        }

        protected override void OnRelease()
        {
            for (int i = 0; i < ItemCount; i++)
            {
                ItemIds[i]?.Dispose();
            }
        }
    }
}
