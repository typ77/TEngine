using System;
using GameLogic.DataBinding;
using GameLogic.UI.FancyScrollView;
using TEngine;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// Example03 无限循环列表示例：FancyScrollView + MVE 架构（无业务层，纯循环演示）。
    /// </summary>
    /// <remarks>
    /// 数据流：DataContext 订阅 Model.ItemIds → 推进 ItemsRevision → View.RefreshList → scroller.UpdateData；
    /// 选中态经 FancySelectionContext 广播给 Cell。核心特性：Loop = true 实现首尾循环。
    /// </remarks>
    [Window(UILayer.UI)]
    [DataContext(typeof(Example03_InfiniteScrollDataContext))]
    public partial class Example03_InfiniteScrollUI
    {
        /// <summary>FancyScrollView 的 Cell 共享上下文，持有选中索引并广播给所有 Cell。
        /// 注意：这是 FancyScrollView 的广播机制，与 MVE 的 DataContext 是不同概念。</summary>
        private FancySelectionContext _context;

        /// <summary>程序滚动的目标索引，用于抑制「滑动跟随选中」。
        /// 点击/Prev/Next 触发 ScrollTo 时置为目标；滑动到达目标后由 OnScrollerPositionChanged 清空。
        /// 非空期间忽略居中变化，避免动画中间位置覆盖目标选中态；为 null 时跟随手动滑动。</summary>
        private int? _scrollTarget;

        /// <summary>Random 按钮：Example03 为纯循环演示，不使用随机化（按钮在 OnCreate 隐藏）。</summary>
        private partial void OnClickRandomBtn()
        {
        }

        /// <summary>Close 按钮：关闭本窗口。</summary>
        private partial void OnClickCloseBtn()
        {
            GameModule.UI.CloseUI<Example03_InfiniteScrollUI>();
        }

        /// <summary>Prev 按钮：选中上一项。</summary>
        private partial void OnClickPrevBtn() => Navigate(-1);

        /// <summary>Next 按钮：选中下一项。</summary>
        private partial void OnClickNextBtn() => Navigate(1);

        /// <summary>
        /// 声明数据绑定：订阅 DataContext 的列表变更信号刷新 scroller。
        /// </summary>
        /// <remarks>
        /// ItemsRevision 由 DataContext 在 Model.ItemIds 变化时推进（BatchScheduler 同帧合并）。
        /// Bind 首次触发时 cell prefab 尚未加载，UpdateData 内部因 _cellPrefab == null 早退，安全；
        /// 首帧渲染由 OnCreate 在 prefab 就绪后显式触发。
        /// </remarks>
        protected override void SetupBindings()
        {
            var dc = GetDataContext<Example03_InfiniteScrollDataContext>();
            Bind(dc.ItemsRevision, _ => RefreshList());
        }

        /// <summary>初始化 scroller（开启 Loop 循环）、Cell 上下文与事件；加载 prefab 后首帧渲染并定位到初始选中项。</summary>
        protected override async void OnCreate()
        {
            var dc = GetDataContext<Example03_InfiniteScrollDataContext>();

            m_scrollerController.SetCellFactory(() => new Example03_InfiniteScrollCell());
            m_scrollerController.Loop = true; // 03 核心：无限循环
            m_btnRandom.gameObject.SetActive(false); // 不使用随机化，隐藏 Random 按钮

            _context = new FancySelectionContext { SelectedIndex = 0 };
            m_scrollerController.SetContext(_context);
            m_scrollerController.OnCellItemClicked += OnCellClicked;
            m_scrollerController.OnPositionChanged += OnScrollerPositionChanged;

            try
            {
                await m_scrollerController.LoadCellPrefabAsync("Example03_InfiniteScrollCell");

                // prefab 就绪后显式首帧渲染（SetupBindings 的首次 RefreshList 因 prefab 未就绪早退）
                m_scrollerController.UpdateData(dc.Items);
                ScrollToIndex(_context.SelectedIndex);
            }
            catch (Exception e)
            {
                // 捕获异步加载/首帧渲染异常：async void 的异常会冒泡到同步上下文难以追踪。
                Log.Error($"[Example03] OnCreate 初始化失败: {e}");
            }
        }

        /// <summary>Cell 点击：记录日志，选中该索引（若变化则滚动居中）。</summary>
        private void OnCellClicked(int index)
        {
            var items = GetDataContext<Example03_InfiniteScrollDataContext>().Items;
            var data = (index >= 0 && index < items.Count) ? items[index] : null;
            Log.Info($"[Example03] 点击 Cell #{index}: {data?.DisplayText ?? "(无数据)"}");

            if (SetSelected(index))
            {
                ScrollToIndex(index);
            }
        }

        /// <summary>
        /// 滑动跟随选中：居中 index 变化时更新选中态。
        /// </summary>
        /// <remarks>
        /// 程序滚动（_scrollTarget 非空）期间不跟随，到达目标后清空并恢复跟随。
        /// Cell.UpdatePosition 每帧调 UpdateSelection，选中色自动反映新选中态，无需手动 Refresh。
        /// </remarks>
        private void OnScrollerPositionChanged(float position)
        {
            int center = m_scrollerController.GetCenterIndex();
            if (center < 0 || center >= Example03_InfiniteScrollModel.ItemCount) return;

            if (_scrollTarget.HasValue)
            {
                if (center == _scrollTarget.Value) _scrollTarget = null;
                return;
            }

            SetSelected(center);
        }

        /// <summary>相对导航：从当前选中项偏移 delta（-1/1），越界自动钳制，变化则滚动居中。</summary>
        private void Navigate(int delta)
        {
            int target = Mathf.Clamp(_context.SelectedIndex + delta, 0, Example03_InfiniteScrollModel.ItemCount - 1);
            if (SetSelected(target))
            {
                ScrollToIndex(target);
            }
        }

        /// <summary>程序滚动到指定索引：置 _scrollTarget 抑制期间的滑动跟随，再交由 scroller 滚动（OutBounce 缓动）。</summary>
        private void ScrollToIndex(int index)
        {
            _scrollTarget = index;
            m_scrollerController.ScrollTo(index, 0.35f, Ease.OutBounce);
        }

        /// <summary>设置选中项：与当前相同返回 false；否则更新 SelectedIndex 并记录切换日志。</summary>
        /// <returns>是否发生变化（调用方据此决定是否 ScrollTo）。</returns>
        private bool SetSelected(int index)
        {
            if (index == _context.SelectedIndex) return false;
            int old = _context.SelectedIndex;
            _context.SelectedIndex = index;
            Log.Info($"[Example03] 选中切换: #{old} → #{index}");
            return true;
        }

        /// <summary>用 DataContext 的最新列表刷新 scroller（_cellPrefab 未就绪时 UpdateData 内部早退，安全）。</summary>
        private void RefreshList()
        {
            if (m_scrollerController == null) return;
            m_scrollerController.UpdateData(GetDataContext<Example03_InfiniteScrollDataContext>().Items);
        }
    }
}
