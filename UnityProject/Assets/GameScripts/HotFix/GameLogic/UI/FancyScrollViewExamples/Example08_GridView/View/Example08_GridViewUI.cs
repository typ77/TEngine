using System;
using System.Linq;
using GameLogic.DataBinding;
using GameLogic.UI.FancyScrollView;
using GameLogic.UI.FancyScrollView.EasingCore;
using TEngine;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// Example08 GridView 示例（UIWindow 持有 <see cref="FancyGridViewController"/>）。
    /// </summary>
    /// <remarks>
    /// 数据流：UI 输入 dataCount → <see cref="Example08_GridViewDataContext.Rebuild"/> 构建 Items → View.UpdateData。
    /// 保留原版全部调参 UI：Padding/SpacingX/SpacingY/DataCount/SelectIndex/Alignment，实时调整布局。
    /// </remarks>
    [Window(UILayer.UI)]
    [DataContext(typeof(Example08_GridViewDataContext))]
    public partial class Example08_GridViewUI
    {
        private Example08_GridViewContext _context;
        private Example08_Alignment _currentAlignment = Example08_Alignment.Middle;

        /// <summary>Close 按钮：关闭本窗口。</summary>
        private partial void OnClickCloseBtn() => GameModule.UI.CloseUI<Example08_GridViewUI>();

        /// <summary>初始化 grid、Cell 上下文与事件、调参 InputField；加载 prefab 后首次生成数据并定位。</summary>
        protected override async void OnCreate()
        {
            _context = new Example08_GridViewContext();
            m_gridController.StartAxisCellCount = Example08_GridViewModel.StartAxisCellCount;
            m_gridController.CellSize2D = new Vector2(100f, 100f);
            m_gridController.SetCellFactory(() => new Example08_GridViewCell());
            m_gridController.SetContext(_context);
            m_gridController.OnCellItemClicked += OnCellClicked;

            InitAlignmentDropdown();

            BindInputField(m_inputPaddingTop, 0, 999, v => m_gridController.PaddingTop = v);
            m_inputPaddingTop.text = m_gridController.PaddingTop.ToString();

            BindInputField(m_inputPaddingBottom, 0, 999, v => m_gridController.PaddingBottom = v);
            m_inputPaddingBottom.text = m_gridController.PaddingBottom.ToString();

            m_gridController.Spacing = 10;
            // SpacingX 调整后需 Relayout（主轴间距影响布局）
            BindInputField(m_inputSpacingX, 0, 99, v => { m_gridController.Spacing = v; m_gridController.Relayout(); });
            m_inputSpacingX.text = m_gridController.Spacing.ToString();

            m_gridController.StartAxisSpacing = 10;
            // SpacingY 调整需 Relayout（交叉轴间距在 Initialize 时固定到 Context，Relayout 重算）
            BindInputField(m_inputSpacingY, 0, 99, v => { m_gridController.StartAxisSpacing = v; m_gridController.Relayout(); });
            m_inputSpacingY.text = m_gridController.StartAxisSpacing.ToString();

            BindInputField(m_inputDataCount, 1, 99999, GenerateCells);
            m_inputDataCount.text = Example08_GridViewModel.DefaultItemCount.ToString();

            m_inputSelectIndex.onValueChanged.AddListener(_ => SelectCell());
            m_inputSelectIndex.text = Example08_GridViewModel.DefaultSelectIndex.ToString();

            try
            {
                await m_gridController.LoadCellPrefabAsync("Example08_GridViewCell");
                GenerateCells(Example08_GridViewModel.DefaultItemCount);
            }
            catch (Exception e)
            {
                // 捕获异步加载/首帧渲染异常：async void 的异常会冒泡到同步上下文难以追踪。
                Log.Error($"[Example08] OnCreate 初始化失败: {e}");
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
            m_dropdownAlignment.AddOptions(System.Enum.GetNames(typeof(Example08_Alignment)).ToList());
            m_dropdownAlignment.onValueChanged.AddListener(idx =>
            {
                _currentAlignment = (Example08_Alignment)idx;
                SelectCell();
            });
            m_dropdownAlignment.value = (int)Example08_Alignment.Middle;
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
            if (m_gridController == null) return;
            var dc = GetDataContext<Example08_GridViewDataContext>();
            dc.Rebuild(dataCount);
            m_gridController.UpdateData(dc.Items);
            m_gridController.JumpTo(Example08_GridViewModel.DefaultSelectIndex, GetAlignment(_currentAlignment));
            SelectCell();
        }

        /// <summary>选中指定索引的 Cell（滚动居中 + 更新选中态）。</summary>
        private void SelectCell()
        {
            if (m_gridController == null || m_gridController.DataCount == 0) return;
            TryParseValue(m_inputSelectIndex, 0, m_gridController.DataCount - 1, index =>
            {
                UpdateSelection(index);
                m_gridController.ScrollTo(index, 0.4f, Ease.InOutQuint, GetAlignment(_currentAlignment));
            });
        }

        private void UpdateSelection(int index)
        {
            if (_context == null || _context.SelectedIndex == index) return;
            _context.SelectedIndex = index;
        }

        /// <summary>对齐方式 → ScrollTo/JumpTo 的 alignment 参数（0=上/左，1=下/右，0.5=居中）。</summary>
        private static float GetAlignment(Example08_Alignment alignment)
        {
            switch (alignment)
            {
                case Example08_Alignment.Upper: return 0.0f;
                case Example08_Alignment.Lower: return 1.0f;
                default: return 0.5f;
            }
        }
    }
}
