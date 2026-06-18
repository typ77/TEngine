using GameLogic.DataBinding;
using GameLogic.UI.FancyScrollView;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// Example07 Cell（继承 <see cref="FancyCellWidget{TItemData, TContext}"/>，走 ScrollRect 路径）。
    /// <para>子节点组件由 _Gen.g.cs 通过 UIBindComponent 自动绑定（m_imgBackground/m_textMessage/m_btnCell）。</para>
    /// <para><see cref="UpdatePosition"/> 移植自原版 <c>FancyScrollRectCell.UpdatePosition</c> 算法：
    /// 用 Context.CalculateScrollSize 反归一化 position 为 localPosition，并叠加正弦波浪偏移。</para>
    /// </summary>
    public partial class Example07_ScrollRectCell : FancyCellWidget<Example07_ScrollRectItemData, Example07_ScrollRectContext>
    {
        private BindableProperty<string> _displayText;

        private static readonly Color32 SelectedColor = new Color32(0, 255, 255, 100);
        private static readonly Color32 NormalColor = new Color32(255, 255, 255, 77);

        // 子节点组件（m_imgBackground/m_textMessage/m_btnCell）来自 _Gen.g.cs（UIBindComponent 自动绑定）。
        // m_btnCell 的点击由 _Gen 绑到 OnClickCellBtn（见下方 partial 实现），无需在此 AddListener。
        protected override void BindMemberProperty()
        {
            _displayText = CreateProperty<string>("");
            BindText(m_textMessage, _displayText);
        }

        /// <summary>m_btnCell 点击回调（_Gen 的 OnClickCellBtn partial 实现）：冒泡到 Controller.OnCellItemClicked。</summary>
        private partial void OnClickCellBtn() => NotifyItemClicked();

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
