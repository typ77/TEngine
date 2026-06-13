using System.Collections.Generic;
using GameLogic.DataBinding;
using GameLogic.UI.FancyScrollView;
using TEngine;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// Example01 基础列表示例（MVE 四层架构 + 非泛型 FancyScrollerController）。
    /// </summary>
    [Window(UILayer.UI)]
    [DataContext(typeof(Example01_BasicDataContext))]
    public partial class Example01_BasicUI : UIWindow
    {
        private List<Example01_BasicItemData> _items;

        #region 事件

        private partial void OnClickRandomBtn()
        {
            int centerIndex = m_scrollerController.GetCenterIndex();
            Singleton<Example01_BasicService>.Instance.RandomizeNeighborIds(centerIndex);
        }

        #endregion

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
            _items = Singleton<Example01_BasicService>.Instance.BuildItems();
            m_scrollerController.UpdateData(_items);
        }

        private void RefreshList()
        {
            if (m_scrollerController == null) return;
            _items = Singleton<Example01_BasicService>.Instance.BuildItems();
            m_scrollerController.UpdateData(_items);
        }
    }
}
