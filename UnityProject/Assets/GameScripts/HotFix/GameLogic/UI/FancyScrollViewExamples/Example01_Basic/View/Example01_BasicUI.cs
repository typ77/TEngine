using System.Collections.Generic;
using GameLogic.DataBinding;
using TEngine;
using UnityEngine;

namespace GameLogic
{
    /// <summary>Example01 基础列表示例（MVE 四层架构 + FancyScrollerController + Context 导航）。</summary>
    [Window(UILayer.UI)]
    [DataContext(typeof(Example01_BasicDataContext))]
    public partial class Example01_BasicUI
    {
        private Example01_BasicContext _context;
        private List<Example01_BasicItemData> _items;

        /// <summary>
        /// 程序滚动（点击/按钮 ScrollTo）的目标索引；非空期间抑制「滑动跟随选中」，
        /// 避免 ScrollTo 动画的中间位置覆盖目标选中态。手动滑动时为 null，正常跟随。
        /// </summary>
        private int? _scrollTarget;

        private partial void OnClickRandomBtn()
        {
            int centerIndex = m_scrollerController.GetCenterIndex();
            Singleton<Example01_BasicService>.Instance.RandomizeNeighborIds(centerIndex);
        }

        private partial void OnClickCloseBtn()
        {
            GameModule.UI.CloseUI<Example01_BasicUI>();
        }

        private partial void OnClickPrevBtn() => Navigate(-1);

        private partial void OnClickNextBtn() => Navigate(1);

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

            _context = new Example01_BasicContext { SelectedIndex = 0 };
            m_scrollerController.SetContext(_context);
            m_scrollerController.OnCellItemClicked += OnCellClicked;
            m_scrollerController.OnPositionChanged += OnScrollerPositionChanged;

            await m_scrollerController.LoadCellPrefabAsync("Example01_BasicCell");
            _items = Singleton<Example01_BasicService>.Instance.BuildItems();
            m_scrollerController.UpdateData(_items);
            ScrollToIndex(_context.SelectedIndex);
        }

        private void OnCellClicked(int index)
        {
            var data = (_items != null && index >= 0 && index < _items.Count) ? _items[index] : null;
            Log.Info($"[Example01] 点击 Cell #{index}: {data?.DisplayText ?? "(无数据)"}");

            if (SetSelected(index))
            {
                ScrollToIndex(index);
            }
        }

        // 滚动跟随选中：居中 index 变化时更新 SelectedIndex
        // （Cell.UpdatePosition 每帧调 UpdateSelection，会自动反映新选中态，无需 Refresh）
        private void OnScrollerPositionChanged(float position)
        {
            int center = m_scrollerController.GetCenterIndex();
            if (center < 0 || center >= Example01_BasicModel.ItemCount) return;

            // 程序滚动中：不跟随中间位置（目标已由点击/按钮设定），到达目标后恢复跟随
            if (_scrollTarget.HasValue)
            {
                if (center == _scrollTarget.Value) _scrollTarget = null;
                return;
            }

            SetSelected(center);
        }

        private void Navigate(int delta)
        {
            int target = Mathf.Clamp(_context.SelectedIndex + delta, 0, Example01_BasicModel.ItemCount - 1);
            if (SetSelected(target))
            {
                ScrollToIndex(target);
            }
        }

        /// <summary>程序滚动到指定索引：标记目标，滚动期间抑制滑动跟随。</summary>
        private void ScrollToIndex(int index)
        {
            _scrollTarget = index;
            m_scrollerController.ScrollTo(index);
        }

        /// <summary>
        /// 统一设置选中项：变化时打印切换日志并更新 SelectedIndex。
        /// </summary>
        /// <returns>是否发生了变化（未变化返回 false，调用方据此决定是否 ScrollTo）。</returns>
        private bool SetSelected(int index)
        {
            if (index == _context.SelectedIndex) return false;
            int old = _context.SelectedIndex;
            _context.SelectedIndex = index;
            Log.Info($"[Example01] 选中切换: #{old} → #{index}");
            return true;
        }

        private void RefreshList()
        {
            if (m_scrollerController == null) return;
            _items = Singleton<Example01_BasicService>.Instance.BuildItems();
            m_scrollerController.UpdateData(_items);
        }
    }
}
