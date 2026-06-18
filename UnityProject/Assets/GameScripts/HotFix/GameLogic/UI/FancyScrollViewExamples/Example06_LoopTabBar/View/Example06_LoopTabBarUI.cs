using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GameLogic.DataBinding;
using GameLogic.UI.FancyScrollView;
using TEngine;
using UnityEngine;

namespace GameLogic
{
    /// <summary>Example06 LoopTabBar 示例（Scroller 路径 + Tab 切换 + 内容面板滑动过渡）。</summary>
    /// <remarks>
    /// 复用 Example03 的 UI prefab 骨架：m_textRandom 复用为选中信息显示位，
    /// m_btnRandom 复用为"回到首个 Tab"。内容面板 Window_0..3 为 06 专属节点。
    /// </remarks>
    [Window(UILayer.UI)]
    [DataContext(typeof(Example06_LoopTabBarDataContext))]
    public partial class Example06_LoopTabBarUI
    {
        private FancySelectionContext _context;
        private List<Example06_LoopTabBarItemData> _items;
        private readonly List<Example06_SlideScreenTransition> _windows = new();
        private int _currentWindowIndex = -1;

        /// <summary>
        /// 程序滚动（Tab 点击/按钮）的目标索引；非空期间抑制「滑动跟随选中」，
        /// 避免 ScrollTo 动画的中间位置覆盖目标选中态。
        /// </summary>
        private int? _scrollTarget;

        // m_btnRandom 复用为"回到首个 Tab"
        private partial void OnClickRandomBtn() => NavigateTo(0);

        private partial void OnClickCloseBtn()
        {
            GameModule.UI.CloseUI<Example06_LoopTabBarUI>();
        }

        private partial void OnClickPrevBtn() => Navigate(-1);

        private partial void OnClickNextBtn() => Navigate(1);

        protected override async void OnCreate()
        {
            m_scrollerController.SetCellFactory(() => new Example06_LoopTabBarCell());

            _context = new FancySelectionContext { SelectedIndex = 0 };
            m_scrollerController.SetContext(_context);
            m_scrollerController.OnCellItemClicked += OnCellClicked;
            m_scrollerController.OnPositionChanged += OnScrollerPositionChanged;

            // m_textRandom 复用为 Random 按钮的标签（原版语义），改为 06 的"首个 Tab"提示
            if (m_textRandom != null) m_textRandom.text = "First";

            await m_scrollerController.LoadCellPrefabAsync("Example06_LoopTabBarCell");
            _items = Singleton<Example06_LoopTabBarService>.Instance.BuildItems();
            m_scrollerController.UpdateData(_items);

            await CollectContentWindowsAsync();
            ShowWindow(0, MovementDirection.Right);
            ScrollToIndex(_context.SelectedIndex);
        }

        /// <summary>异步加载内容面板 Window prefab 为 SlideScreenTransition Widget，挂到 m_tf_Windows 下。</summary>
        private async UniTask CollectContentWindowsAsync()
        {
            _windows.Clear();
            var parent = FindChild("m_tf_Windows");
            for (int i = 0; i < Example06_LoopTabBarModel.TabCount; i++)
            {
                var w = await CreateWidgetByPathAsync<Example06_SlideScreenTransition>(parent, "Example06_Window", false);
                w.SetLabel($"Window {i}");
                _windows.Add(w);
            }
        }

        private void OnCellClicked(int index)
        {
            var data = (_items != null && index >= 0 && index < _items.Count) ? _items[index] : null;
            Log.Info($"[Example06] 点击 Tab #{index}: {data?.DisplayText ?? "(无数据)"}");

            if (SetSelected(index, out var direction))
            {
                ShowWindow(index, direction);
                ScrollToIndex(index);
            }
        }

        // 滚动跟随选中：居中 index 变化时更新选中态
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

        private void Navigate(int delta)
        {
            int target = Mathf.Clamp(_context.SelectedIndex + delta, 0, Example06_LoopTabBarModel.TabCount - 1);
            NavigateTo(target);
        }

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

        private void ScrollToIndex(int index)
        {
            _scrollTarget = index;
            m_scrollerController.ScrollTo(index, 0.35f, Ease.OutCubic);
        }

        /// <summary>
        /// 统一设置选中项：变化时打印切换日志并更新 SelectedIndex，并计算移动方向。
        /// </summary>
        /// <returns>是否发生了变化（未变化返回 false）。</returns>
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
                m_textClose.text = $"Selected tab info: index {index}";
            }
        }
    }
}
