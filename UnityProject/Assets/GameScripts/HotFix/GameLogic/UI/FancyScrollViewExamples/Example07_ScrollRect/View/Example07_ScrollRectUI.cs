using System.Collections.Generic;
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
    /// Example07 ScrollRect 示例（UIWindow 持有 <see cref="FancyScrollRectController"/>，同 06/09 范式）。
    /// <para>保留原版全部调参 UI：Padding/Spacing/DataCount/SelectIndex/Alignment，实时调整布局。</para>
    /// <para>重构说明：弃用泛型 MonoBehaviour 适配层（FancyScrollRectWidget），改用纯 C# Controller。</para>
    /// </summary>
    [Window(UILayer.UI)]
    [DataContext(typeof(Example07_ScrollRectDataContext))]
    public partial class Example07_ScrollRectUI
    {
        private Example07_ScrollRectContext _context;
        private Example07_Alignment _currentAlignment = Example07_Alignment.Middle;

        private partial void OnClickCloseBtn()
        {
            GameModule.UI.CloseUI<Example07_ScrollRectUI>();
        }

        protected override async void OnCreate()
        {
            _context = new Example07_ScrollRectContext();
            m_scrollerController.SetCellFactory(() => new Example07_ScrollRectCell());
            m_scrollerController.SetContext(_context);
            m_scrollerController.OnCellItemClicked += OnCellClicked;

            // 初始化 Alignment 下拉框
            InitAlignmentDropdown();

            // 绑定调参 InputField
            BindInputField(m_inputPaddingTop, 0, 999, v => m_scrollerController.PaddingTop = v);
            m_inputPaddingTop.text = m_scrollerController.PaddingTop.ToString();

            BindInputField(m_inputPaddingBottom, 0, 999, v => m_scrollerController.PaddingBottom = v);
            m_inputPaddingBottom.text = m_scrollerController.PaddingBottom.ToString();

            BindInputField(m_inputSpacing, 0, 100, v => m_scrollerController.Spacing = v);
            m_inputSpacing.text = m_scrollerController.Spacing.ToString();

            BindInputField(m_inputDataCount, 1, 99999, GenerateCells);
            m_inputDataCount.text = Example07_ScrollRectModel.DefaultItemCount.ToString();

            m_inputSelectIndex.onValueChanged.AddListener(_ => SelectCell());
            m_inputSelectIndex.text = Example07_ScrollRectModel.DefaultSelectIndex.ToString();

            await m_scrollerController.LoadCellPrefabAsync("Example07_ScrollRectCell");
            GenerateCells(Example07_ScrollRectModel.DefaultItemCount);
        }

        private void OnCellClicked(int index)
        {
            if (m_inputSelectIndex != null) m_inputSelectIndex.text = index.ToString();
        }

        private void InitAlignmentDropdown()
        {
            if (m_dropdownAlignment == null) return;
            m_dropdownAlignment.options.Clear();
            m_dropdownAlignment.AddOptions(System.Enum.GetNames(typeof(Example07_Alignment)).ToList());
            m_dropdownAlignment.onValueChanged.AddListener(idx =>
            {
                _currentAlignment = (Example07_Alignment)idx;
                SelectCell();
            });
            m_dropdownAlignment.value = (int)Example07_Alignment.Middle;
        }

        private void BindInputField(InputField input, int min, int max, System.Action<int> onSuccess)
        {
            if (input == null) return;
            input.onValueChanged.AddListener(_ => TryParseValue(input, min, max, onSuccess));
        }

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

        private void GenerateCells(int dataCount)
        {
            if (m_scrollerController == null) return;
            var items = Singleton<Example07_ScrollRectService>.Instance.BuildItems(dataCount);
            m_scrollerController.UpdateData(items);
            m_scrollerController.JumpTo(Example07_ScrollRectModel.DefaultSelectIndex, GetAlignment(_currentAlignment));
            SelectCell();
        }

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
