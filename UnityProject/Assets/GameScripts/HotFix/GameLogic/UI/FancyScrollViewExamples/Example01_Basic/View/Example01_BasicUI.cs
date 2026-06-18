using System;
using GameLogic.DataBinding;
using GameLogic.UI.FancyScrollView;
using TEngine;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// Example01 基础列表示例：FancyScrollView + MVE 四层架构的最简形态。
    /// </summary>
    /// <remarks>
    /// 数据流：Service 改 Model.ItemIds → DataContext 订阅并推进 ItemsRevision →
    /// View.RefreshList → scroller.UpdateData；选中态经 FancySelectionContext 广播给 Cell。
    /// </remarks>
    [Window(UILayer.UI)]
    [DataContext(typeof(Example01_BasicDataContext))]
    public partial class Example01_BasicUI
    {
        /// <summary>FancyScrollView 的 Cell 共享上下文，持有选中索引并广播给所有 Cell。
        /// 注意：这是 FancyScrollView 的广播机制，与 MVE 的 DataContext 是不同概念。</summary>
        private FancySelectionContext _cellContext;

        /// <summary>程序滚动的目标索引，用于抑制「滑动跟随选中」。
        /// 点击/Prev/Next 触发 ScrollTo 时置为目标；滑动到达目标后由 OnScrollerPositionChanged 清空。
        /// 非空期间忽略居中变化，避免动画中间位置覆盖目标选中态；为 null 时跟随手动滑动。</summary>
        private int? _scrollTarget;

        /// <summary>Random 按钮：随机化当前居中条目及其前后邻居的 ID（触发 DataContext 刷新）。</summary>
        private partial void OnClickRandomBtn()
        {
            int centerIndex = m_scrollerController.GetCenterIndex();
            Singleton<Example01_BasicService>.Instance.RandomizeNeighborIds(centerIndex);
        }

        /// <summary>Close 按钮：关闭本窗口。</summary>
        private partial void OnClickCloseBtn()
        {
            GameModule.UI.CloseUI<Example01_BasicUI>();
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
            var dc = GetDataContext<Example01_BasicDataContext>();
            Bind(dc.ItemsRevision, _ => RefreshList());
        }

        /// <summary>初始化 scroller、Cell 上下文与事件；加载 prefab 后首帧渲染并定位到初始选中项。</summary>
        protected override async void OnCreate()
        {
            var dc = GetDataContext<Example01_BasicDataContext>();

            m_scrollerController.SetCellFactory(() => new Example01_BasicCell());

            _cellContext = new FancySelectionContext { SelectedIndex = 0 };
            m_scrollerController.SetContext(_cellContext);
            m_scrollerController.OnCellItemClicked += OnCellClicked;
            m_scrollerController.OnPositionChanged += OnScrollerPositionChanged;

            try
            {
                await m_scrollerController.LoadCellPrefabAsync("Example01_BasicCell");

                // prefab 就绪后显式首帧渲染（SetupBindings 的首次 RefreshList 因 prefab 未就绪早退）
                m_scrollerController.UpdateData(dc.Items);
                ScrollToIndex(_cellContext.SelectedIndex);
            }
            catch (Exception e)
            {
                // 捕获异步加载/首帧渲染异常：async void 的异常会冒泡到同步上下文难以追踪。
                // prefab 加载失败时跳过渲染，窗口仍可关闭。
                Log.Error($"[Example01] OnCreate 初始化失败: {e}");
            }
        }

        /// <summary>Cell 点击：记录日志，选中该索引（若变化则滚动居中）。</summary>
        private void OnCellClicked(int index)
        {
            var items = GetDataContext<Example01_BasicDataContext>().Items;
            var data = (index >= 0 && index < items.Count) ? items[index] : null;
            Log.Info($"[Example01] 点击 Cell #{index}: {data?.DisplayText ?? "(无数据)"}");

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
            if (center < 0 || center >= Example01_BasicModel.ItemCount) return;

            // 程序滚动中：不跟随中间位置（目标已由点击/按钮设定），到达目标后恢复跟随
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
            int target = Mathf.Clamp(_cellContext.SelectedIndex + delta, 0, Example01_BasicModel.ItemCount - 1);
            if (SetSelected(target))
            {
                ScrollToIndex(target);
            }
        }

        /// <summary>程序滚动到指定索引：置 _scrollTarget 抑制期间的滑动跟随，再交由 scroller 滚动。</summary>
        private void ScrollToIndex(int index)
        {
            _scrollTarget = index;
            m_scrollerController.ScrollTo(index);
        }

        /// <summary>设置选中项：与当前相同返回 false；否则更新 SelectedIndex 并记录切换日志。</summary>
        /// <returns>是否发生变化（调用方据此决定是否 ScrollTo）。</returns>
        private bool SetSelected(int index)
        {
            if (index == _cellContext.SelectedIndex) return false;
            int old = _cellContext.SelectedIndex;
            _cellContext.SelectedIndex = index;
            Log.Info($"[Example01] 选中切换: #{old} → #{index}");
            return true;
        }

        /// <summary>用 DataContext 的最新列表刷新 scroller（_cellPrefab 未就绪时 UpdateData 内部早退，安全）。</summary>
        private void RefreshList()
        {
            if (m_scrollerController == null) return;
            m_scrollerController.UpdateData(GetDataContext<Example01_BasicDataContext>().Items);
        }
    }
}
