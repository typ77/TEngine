using System;
using System.Linq;
using GameLogic.DataBinding;
using GameLogic.UI.FancyScrollView;
using TEngine;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// Example07 ScrollRect 示例（UIWindow 持有 <see cref="FancyScrollRectController"/>）。
    /// </summary>
    /// <remarks>
    /// 数据流：UI 输入 dataCount → <see cref="Example07_ScrollRectDataContext.Rebuild"/> 构建 Items → View.UpdateData。
    /// 保留原版全部调参 UI：Padding/Spacing/DataCount/SelectIndex/Alignment，实时调整布局。
    /// </remarks>
    [Window(UILayer.UI)]
    [DataContext(typeof(Example07_ScrollRectDataContext))]
    public partial class Example07_ScrollRectUI
    {
        private Example07_ScrollRectContext _context;
        private Example07_Alignment _currentAlignment = Example07_Alignment.Middle;

        /// <summary>Close 按钮：关闭本窗口。</summary>
        private partial void OnClickCloseBtn() => GameModule.UI.CloseUI<Example07_ScrollRectUI>();

        /// <summary>初始化 scroller、Cell 上下文与事件、调参 InputField；加载 prefab 后首次生成数据并定位。</summary>
        protected override async void OnCreate()
        {
            _context = new Example07_ScrollRectContext();
            m_scrollerController.SetCellFactory(() => new Example07_ScrollRectCell());
            m_scrollerController.SetContext(_context);
            m_scrollerController.OnCellItemClicked += OnCellClicked;

            InitAlignmentDropdown();

            BindInputField(m_inputPaddingTop, 0, 999, v => m_scrollerController.PaddingTop = v);
            m_inputPaddingTop.text = m_scrollerController.PaddingTop.ToString();

            BindInputField(m_inputPaddingBottom, 0, 999, v => m_scrollerController.PaddingBottom = v);
            m_inputPaddingBottom.text = m_scrollerController.PaddingBottom.ToString();

            // Spacing 默认 10（Controller 原默认 0），让初始布局即有可见间距
            m_scrollerController.Spacing = 10;
            BindInputField(m_inputSpacing, 0, 100, v => m_scrollerController.Spacing = v);
            m_inputSpacing.text = m_scrollerController.Spacing.ToString();

            BindInputField(m_inputDataCount, 1, 99999, GenerateCells);
            m_inputDataCount.text = Example07_ScrollRectModel.DefaultItemCount.ToString();

            m_inputSelectIndex.onValueChanged.AddListener(_ => SelectCell());
            m_inputSelectIndex.text = Example07_ScrollRectModel.DefaultSelectIndex.ToString();

            try
            {
                await m_scrollerController.LoadCellPrefabAsync("Example07_ScrollRectCell");
                GenerateCells(Example07_ScrollRectModel.DefaultItemCount);
            }
            catch (Exception e)
            {
                // 捕获异步加载/首帧渲染异常：async void 的异常会冒泡到同步上下文难以追踪。
                Log.Error($"[Example07] OnCreate 初始化失败: {e}");
            }
        }

        /// <summary>Cell 点击：同步选中索引到 SelectIndex 输入框。</summary>
        private void OnCellClicked(int index)
        {
            if (m_inputSelectIndex != null) m_inputSelectIndex.text = index.ToString();
        }

        /// <summary>初始化 Alignment 下拉框（补 Dropdown Template 兜底 + 绑定枚举选项）。</summary>
        private void InitAlignmentDropdown()
        {
            if (m_dropdownAlignment == null) return;
            // 兜底：prefab 漏配 Dropdown Template 时运行时展开报 "template is not assigned"，补一个标准 Template。
            FancyExampleUIUtil.EnsureDropdownTemplate(m_dropdownAlignment);
            m_dropdownAlignment.options.Clear();
            m_dropdownAlignment.AddOptions(System.Enum.GetNames(typeof(Example07_Alignment)).ToList());
            m_dropdownAlignment.onValueChanged.AddListener(idx =>
            {
                _currentAlignment = (Example07_Alignment)idx;
                SelectCell();
            });
            m_dropdownAlignment.value = (int)Example07_Alignment.Middle;
        }

        /// <summary>绑定 InputField 的整数值变更（带范围钳制）。</summary>
        private void BindInputField(InputField input, int min, int max, System.Action<int> onSuccess)
        {
            if (input == null) return;
            input.onValueChanged.AddListener(_ => TryParseValue(input, min, max, onSuccess));
        }

        /// <summary>解析 InputField 整数值，超范围则钳制。</summary>
        private static void TryParseValue(InputField input, int min, int max, System.Action<int> success)
        {
            if (input == null || !int.TryParse(input.text, out int value)) return;
            if (value < min || value > max)
            {
                input.text = Mathf.Clamp(value, min, max).ToString();
                return;
            }
            success?.Invoke(value);
        }

        /// <summary>dataCount 变化时重建列表（DataContext 承担 count→Items 转换）并刷新 + 定位。</summary>
        private void GenerateCells(int dataCount)
        {
            if (m_scrollerController == null) return;
            var dc = GetDataContext<Example07_ScrollRectDataContext>();
            dc.Rebuild(dataCount);
            m_scrollerController.UpdateData(dc.Items);
            m_scrollerController.JumpTo(Example07_ScrollRectModel.DefaultSelectIndex, GetAlignment(_currentAlignment));
            SelectCell();
        }

        /// <summary>选中指定索引的 Cell（滚动居中 + 更新选中态）。</summary>
        private void SelectCell()
        {
            if (m_scrollerController == null || m_scrollerController.DataCount == 0) return;
            TryParseValue(m_inputSelectIndex, 0, m_scrollerController.DataCount - 1, index =>
            {
                UpdateSelection(index);
                m_scrollerController.ScrollTo(index, 0.3f, Ease.InOutQuint, GetAlignment(_currentAlignment));
            });
        }

        private void UpdateSelection(int index)
        {
            if (_context == null || _context.SelectedIndex == index) return;
            _context.SelectedIndex = index;
        }

        /// <summary>对齐方式 → ScrollTo/JumpTo 的 alignment 参数（0=上/左，1=下/右，0.5=居中）。</summary>
        private static float GetAlignment(Example07_Alignment alignment)
        {
            switch (alignment)
            {
                case Example07_Alignment.Upper: return 0.0f;
                case Example07_Alignment.Lower: return 1.0f;
                default: return 0.5f;
            }
        }
    }
}
