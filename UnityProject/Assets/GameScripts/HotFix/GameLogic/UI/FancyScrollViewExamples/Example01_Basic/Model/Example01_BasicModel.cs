using GameLogic.DataBinding;

namespace GameLogic
{
    /// <summary>Example01 数据模型，持有响应式 ID 属性。</summary>
    public class Example01_BasicModel : Singleton<Example01_BasicModel>
    {
        public const int ItemCount = 20;

        /// <summary>每个条目的随机 ID，Service 修改后通知 View 刷新。</summary>
        public readonly BindableProperty<int>[] ItemIds = new BindableProperty<int>[ItemCount];

        protected override void OnInit()
        {
            for (int i = 0; i < ItemCount; i++)
            {
                ItemIds[i] = new BindableProperty<int>(0);
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
