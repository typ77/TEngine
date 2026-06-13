using GameLogic.DataBinding;
using TEngine;

namespace GameLogic
{
    /// <summary>Example01 基础列表示例（MVE 四层架构 + FancyScrollerController）。</summary>
    [Window(UILayer.UI)]
    [DataContext(typeof(Example01_BasicDataContext))]
    public partial class Example01_BasicUI
    {
        private partial void OnClickRandomBtn()
        {
            int centerIndex = m_scrollerController.GetCenterIndex();
            Singleton<Example01_BasicService>.Instance.RandomizeNeighborIds(centerIndex);
        }

        private partial void OnClickCloseBtn()
        {
            GameModule.UI.CloseUI<Example01_BasicUI>();
        }

        protected override void SetupBindings()
        {
            var model = Singleton<Example01_BasicModel>.Instance;
            for (int i = 0; i < Example01_BasicModel.ItemCount; i++)
            {
                int idx = i;
                Bind(model.ItemIds[idx], _ => RefreshList());
            }
        }

        protected override async void OnCreate()
        {
            m_scrollerController.SetCellFactory(() => new Example01_BasicCell());
            await m_scrollerController.LoadCellPrefabAsync("Example01_BasicCell");
            m_scrollerController.UpdateData(Singleton<Example01_BasicService>.Instance.BuildItems());
        }

        private void RefreshList()
        {
            if (m_scrollerController == null) return;
            m_scrollerController.UpdateData(Singleton<Example01_BasicService>.Instance.BuildItems());
        }
    }
}
