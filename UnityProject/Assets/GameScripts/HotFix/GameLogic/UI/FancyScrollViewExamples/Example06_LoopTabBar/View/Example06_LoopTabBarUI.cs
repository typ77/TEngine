using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GameLogic.DataBinding;
using GameLogic.UI.FancyScrollView;
using TEngine;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// Example06 LoopTabBar 示例：FancyScrollView + MVE 架构（Tab 切换 + 内容面板滑动过渡）。
    /// </summary>
    /// <remarks>
    /// 数据流：DataContext 构建 Tab 列表（静态）→ View.UpdateData；选中态经 FancySelectionContext 广播给 Cell。
    /// 复用 Example03 的 UI prefab 骨架：m_textRandom 复用为选中信息位，m_btnRandom 复用为「回到首个 Tab」。
    /// </remarks>
    [Window(UILayer.UI)]
    [DataContext(typeof(Example06_LoopTabBarDataContext))]
    public partial class Example06_LoopTabBarUI
    {
        /// <summary>FancyScrollView 的 Cell 共享上下文，持有选中索引并广播给所有 Cell。
        /// 注意：这是 FancyScrollView 的广播机制，与 MVE 的 DataContext 是不同概念。</summary>
        private FancySelectionContext _context;

        private readonly List<Example06_SlideScreenTransition> _windows = new();
        private int _currentWindowIndex = -1;

        /// <summary>程序滚动的目标索引，用于抑制「滑动跟随选中」。
        /// 点击/Prev/Next 触发 ScrollTo 时置为目标；滑动到达目标后由 OnScrollerPositionChanged 清空。</summary>
        private int? _scrollTarget;

        /// <summary>Random 按钮：复用为「回到首个 Tab」。</summary>
        private partial void OnClickRandomBtn() => NavigateTo(0);

        /// <summary>Close 按钮：关闭本窗口。</summary>
        private partial void OnClickCloseBtn() => GameModule.UI.CloseUI<Example06_LoopTabBarUI>();

        /// <summary>Prev 按钮：选中上一项。</summary>
        private partial void OnClickPrevBtn() => Navigate(-1);

        /// <summary>Next 按钮：选中下一项。</summary>
        private partial void OnClickNextBtn() => Navigate(1);

        /// <summary>初始化 scroller、Cell 上下文与事件；加载 prefab 后首帧渲染、加载内容面板并定位到初始选中项。</summary>
        protected override async void OnCreate()
        {
            var dc = GetDataContext<Example06_LoopTabBarDataContext>();

            m_scrollerController.SetCellFactory(() => new Example06_LoopTabBarCell());

            _context = new FancySelectionContext { SelectedIndex = 0 };
            m_scrollerController.SetContext(_context);
            m_scrollerController.OnCellItemClicked += OnCellClicked;
            m_scrollerController.OnPositionChanged += OnScrollerPositionChanged;

            if (m_textRandom != null) m_textRandom.text = "First";

            try
            {
                await m_scrollerController.LoadCellPrefabAsync("Example06_LoopTabBarCell");

                // prefab 就绪后首帧渲染（数据静态，DataContext 构建一次）
                m_scrollerController.UpdateData(dc.Items);

                await CollectContentWindowsAsync();
                ShowWindow(0, MovementDirection.Right);
                ScrollToIndex(_context.SelectedIndex);
            }
            catch (Exception e)
            {
                // 捕获异步加载/首帧渲染异常：async void 的异常会冒泡到同步上下文难以追踪。
                Log.Error($"[Example06] OnCreate 初始化失败: {e}");
            }
        }

        /// <summary>异步加载内容面板 Window prefab 为 SlideScreenTransition Widget，挂到 m_tf_Windows 下。</summary>
        private async UniTask CollectContentWindowsAsync()
        {
            _windows.Clear();
            var parent = FindChild("m_tf_Windows");
            for (int i = 0; i < Example06_LoopTabBarModel.TabCount; i++)
            {
                var w = await CreateWidgetByPathAsync<Example06_SlideScreenTransition>(parent, "Example06_Window", false);
                w.SetLabel(Utility.Text.Format("Window {0}", i));
                _windows.Add(w);
            }
        }

        /// <summary>Cell 点击：记录日志，选中该索引并切换内容面板（若变化则滚动居中）。</summary>
        private void OnCellClicked(int index)
        {
            var items = GetDataContext<Example06_LoopTabBarDataContext>().Items;
            var data = (index >= 0 && index < items.Count) ? items[index] : null;
            Log.Info($"[Example06] 点击 Tab #{index}: {data?.DisplayText ?? "(无数据)"}");

            if (SetSelected(index, out var direction))
            {
                ShowWindow(index, direction);
                ScrollToIndex(index);
            }
        }

        /// <summary>
        /// 滑动跟随选中：居中 index 变化时更新选中态。
        /// </summary>
        /// <remarks>
        /// 程序滚动（_scrollTarget 非空）期间不跟随，到达目标后清空并恢复跟随。
        /// </remarks>
        private void OnScrollerPositionChanged(float position)
        {
            int center = m_scrollerController.GetCenterIndex();
            if (center < 0 || center >= Example06_LoopTabBarModel.TabCount) return;

            if (_scrollTarget.HasValue)
            {
                if (center == _scrollTarget.Value) _scrollTarget = null;
                return;
            }

            if (SetSelected(center, out var direction))
            {
                ShowWindow(center, direction);
            }
        }

        /// <summary>相对导航：从当前选中项偏移 delta（-1/1），越界自动钳制。</summary>
        private void Navigate(int delta)
        {
            int target = Mathf.Clamp(_context.SelectedIndex + delta, 0, Example06_LoopTabBarModel.TabCount - 1);
            NavigateTo(target);
        }

        /// <summary>导航到指定 Tab：变化则切换面板 + 滚动居中；未变化仍滚动居中。</summary>
        private void NavigateTo(int target)
        {
            if (SetSelected(target, out var direction))
            {
                ShowWindow(target, direction);
                ScrollToIndex(target);
            }
            else if (target == _context.SelectedIndex)
            {
                // 已是当前项，仍滚动居中
                ScrollToIndex(target);
            }
        }

        /// <summary>程序滚动到指定索引：置 _scrollTarget 抑制期间的滑动跟随（OutCubic 缓动）。</summary>
        private void ScrollToIndex(int index)
        {
            _scrollTarget = index;
            m_scrollerController.ScrollTo(index, 0.35f, Ease.OutCubic);
        }

        /// <summary>设置选中项：与当前相同返回 false；否则更新 SelectedIndex、计算移动方向并记录日志。</summary>
        /// <returns>是否发生变化（调用方据此决定是否切换面板/滚动）。</returns>
        private bool SetSelected(int index, out MovementDirection direction)
        {
            direction = MovementDirection.Right;
            if (index == _context.SelectedIndex) return false;
            int old = _context.SelectedIndex;
            _context.SelectedIndex = index;
            direction = m_scrollerController.GetMovementDirection(old, index);
            Log.Info($"[Example06] 选中切换: #{old} → #{index} ({direction})");
            return true;
        }

        /// <summary>切换内容面板：旧面板滑出，新面板滑入。</summary>
        private void ShowWindow(int index, MovementDirection direction)
        {
            if (index < 0 || index >= _windows.Count) return;

            if (_currentWindowIndex >= 0 && _currentWindowIndex < _windows.Count && _currentWindowIndex != index)
            {
                _windows[_currentWindowIndex].PlayOut(direction);
            }

            _currentWindowIndex = index;
            _windows[index].PlayIn(direction);

            if (m_textClose != null)
            {
                m_textClose.text = Utility.Text.Format("Selected tab info: index {0}", index);
            }
        }
    }
}
