using GameLogic.DataBinding;
using GameLogic.UI.FancyScrollView;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// Example07 Cell（继承 <see cref="FancyCellWidget{TItemData, TContext}"/>，走 ScrollRect 路径）。
    /// <para>
    /// <see cref="UpdatePosition"/> 移植自原版 <c>FancyScrollRectCell.UpdatePosition</c> 算法：
    /// 用 Context.CalculateScrollSize 反归一化 position 为 localPosition，并叠加正弦波浪偏移。
    /// </para>
    /// </summary>
    public partial class Example07_ScrollRectCell : FancyCellWidget<Example07_ScrollRectItemData, Example07_ScrollRectContext>
    {
        private Text m_textMessage;
        private Image m_imgBackground;
        private Button m_button;
        private BindableProperty<string> _displayText;

        private static readonly Color32 SelectedColor = new Color32(0, 255, 255, 100);
        private static readonly Color32 NormalColor = new Color32(255, 255, 255, 77);

        protected override void BindMemberProperty()
        {
            m_textMessage = FindChildComponent<Text>("m_text_Message");
            m_imgBackground = FindChildComponent<Image>("m_img_Background");
            m_button = FindChildComponent<Button>("m_btn_Cell");

            _displayText = CreateProperty<string>("");
            if (m_textMessage != null) BindText(m_textMessage, _displayText);

            if (m_button != null)
            {
                m_button.onClick.AddListener(NotifyItemClicked);
            }
        }

        protected override void BindData(Example07_ScrollRectItemData data)
        {
            _displayText.Value = data.DisplayText;
            UpdateSelection();
        }

        /// <summary>
        /// 移植自 FancyScrollRectCell.UpdatePosition：根据滚动尺寸归一化 position 并定位。
        /// 额外叠加正弦波浪偏移（原版 Example07 特效）。
        /// </summary>
        public override void UpdatePosition(float position)
        {
            if (Context == null || Context.CalculateScrollSize == null) return;

            var (scrollSize, reuseMargin) = Context.CalculateScrollSize();
            var normalizedPosition = (Mathf.Lerp(0f, scrollSize, position) - reuseMargin) / (scrollSize - reuseMargin * 2f);

            var start = 0.5f * scrollSize;
            var end = -start;
            var localPosition = Mathf.Lerp(start, end, position);

            // 基础定位（垂直：y = localPosition；水平：x = -localPosition）
            var lp = Context.ScrollDirection == ScrollDirection.Horizontal
                ? new Vector3(-localPosition, 0f, 0f)
                : new Vector3(0f, localPosition, 0f);

            // 波浪偏移（原版 Example07 特效）
            var wave = Mathf.Sin(normalizedPosition * Mathf.PI * 2f) * 65f;
            lp += Vector3.right * wave;

            transform.localPosition = lp;

            UpdateSelection();
        }

        private void UpdateSelection()
        {
            if (m_imgBackground == null || Context == null) return;
            m_imgBackground.color = Index == Context.SelectedIndex ? (Color)SelectedColor : (Color)NormalColor;
        }
    }
}
