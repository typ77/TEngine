using System.Collections.Generic;
using GameLogic.DataBinding;
using TEngine;
using UnityEngine;

namespace GameLogic
{
    /// <summary>Example03 无限循环列表示例（MVE 四层架构 + FancyScrollerController + Loop 循环 + Context 导航）。</summary>
    [Window(UILayer.UI)]
    [DataContext(typeof(Example03_InfiniteScrollDataContext))]
    public partial class Example03_InfiniteScrollUI
    {
        private Example03_InfiniteScrollContext _context;
        private List<Example03_InfiniteScrollItemData> _items;

        /// <summary>
        /// 程序滚动（点击/按钮 ScrollTo）的目标索引；非空期间抑制「滑动跟随选中」，
        /// 避免 ScrollTo 动画的中间位置覆盖目标选中态。手动滑动时为 null，正常跟随。
        /// </summary>
        private int? _scrollTarget;

        private partial void OnClickRandomBtn()
        {
            // 03 不使用随机化，Random 按钮已在 OnCreate 隐藏
        }

        private partial void OnClickCloseBtn()
        {
            GameModule.UI.CloseUI<Example03_InfiniteScrollUI>();
        }

        private partial void OnClickPrevBtn() => Navigate(-1);

        private partial void OnClickNextBtn() => Navigate(1);

        protected override async void OnCreate()
        {
            m_scrollerController.SetCellFactory(() => new Example03_InfiniteScrollCell());
            m_scrollerController.Loop = true; // 03 核心：无限循环
            m_btnRandom.gameObject.SetActive(false); // 03 不使用随机化，隐藏 Random 按钮

            _context = new Example03_InfiniteScrollContext { SelectedIndex = 0 };
            m_scrollerController.SetContext(_context);
            m_scrollerController.OnCellItemClicked += OnCellClicked;
            m_scrollerController.OnPositionChanged += OnScrollerPositionChanged;

            await m_scrollerController.LoadCellPrefabAsync("Example03_InfiniteScrollCell");
            _items = Singleton<Example03_InfiniteScrollService>.Instance.BuildItems();
            m_scrollerController.UpdateData(_items);
            ScrollToIndex(_context.SelectedIndex);
        }

        private void OnCellClicked(int index)
        {
            var data = (_items != null && index >= 0 && index < _items.Count) ? _items[index] : null;
            Log.Info($"[Example03] 点击 Cell #{index}: {data?.DisplayText ?? "(无数据)"}");

            if (SetSelected(index))
            {
                ScrollToIndex(index);
            }
        }

        // 滚动跟随选中：居中 index 变化时更新 SelectedIndex
        private void OnScrollerPositionChanged(float position)
        {
            int center = m_scrollerController.GetCenterIndex();
            if (center < 0 || center >= Example03_InfiniteScrollModel.ItemCount) return;

            // 程序滚动中：不跟随中间位置，到达目标后恢复跟随
            if (_scrollTarget.HasValue)
            {
                if (center == _scrollTarget.Value) _scrollTarget = null;
                return;
            }

            SetSelected(center);
        }

        private void Navigate(int delta)
        {
            int target = Mathf.Clamp(_context.SelectedIndex + delta, 0, Example03_InfiniteScrollModel.ItemCount - 1);
            if (SetSelected(target))
            {
                ScrollToIndex(target);
            }
        }

        /// <summary>程序滚动到指定索引：标记目标，滚动期间抑制滑动跟随。</summary>
        private void ScrollToIndex(int index)
        {
            _scrollTarget = index;
            m_scrollerController.ScrollTo(index,0.35f, Ease.OutBounce);
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
            Log.Info($"[Example03] 选中切换: #{old} → #{index}");
            return true;
        }
    }
}
