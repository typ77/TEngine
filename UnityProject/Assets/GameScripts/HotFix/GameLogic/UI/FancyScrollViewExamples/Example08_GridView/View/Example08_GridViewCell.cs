using GameLogic.DataBinding;
using GameLogic.UI.FancyScrollView;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// Example08 Cell（继承 <see cref="FancyCellWidget{TItemData, TContext}"/>，走 GridView 路径）。
    /// <para>结构：根节点 Image(背景)+Button(点击)，子节点 m_textMessage(序号)。</para>
    /// <para>子节点 m_textMessage 由 _Gen.g.cs 通过 UIBindComponent 绑定；
    /// 根节点 Image/Button 在 BindMemberProperty 手动获取（生成器不遍历根）。</para>
    /// <para><see cref="UpdatePosition"/> 合并移植自原版 FancyScrollRectCell + FancyGridViewCell：
    /// 先归一化 position 为 localPosition（沿主轴），再按组内索引计算交叉轴偏移，叠加正弦波浪。</para>
    /// </summary>
    public partial class Example08_GridViewCell : FancyCellWidget<Example08_GridViewItemData, Example08_GridViewContext>
    {
        private Image m_imgBackground;
        private Button m_button;
        private BindableProperty<string> _displayText;

        private static readonly Color32 SelectedColor = new Color32(0, 255, 255, 100);
        private static readonly Color32 NormalColor = new Color32(255, 255, 255, 77);

        // 根节点 Image/Button 手动获取（生成器不遍历根）+ 子节点 m_textMessage 来自 _Gen.g.cs。
        protected override void BindMemberProperty()
        {
            // 根节点组件（生成器不遍历根）：Image 背景 + Button 点击
            m_imgBackground = gameObject.GetComponent<Image>();
            m_button = gameObject.GetComponent<Button>();

            // 子节点 m_textMessage 来自 _Gen.g.cs
            _displayText = CreateProperty<string>("");
            BindText(m_textMessage, _displayText);

            if (m_button != null)
            {
                m_button.onClick.AddListener(NotifyItemClicked);
            }
        }

        protected override void BindData(Example08_GridViewItemData data)
        {
            _displayText.Value = data.Id.ToString();
            UpdateSelection();
        }

        /// <summary>
        /// 合并移植 FancyScrollRectCell + FancyGridViewCell 的 UpdatePosition 算法。
        /// </summary>
        public override void UpdatePosition(float position)
        {
            if (Context == null || Context.CalculateScrollSize == null) return;

            // ── 主轴归一化与 localPosition（来自 FancyScrollRectCell）──
            var (scrollSize, reuseMargin) = Context.CalculateScrollSize();
            var normalizedPosition = (Mathf.Lerp(0f, scrollSize, position) - reuseMargin) / (scrollSize - reuseMargin * 2f);

            var start = 0.5f * scrollSize;
            var end = -start;
            var localPosition = Mathf.Lerp(start, end, position);

            // ── 交叉轴组内偏移（来自 FancyGridViewCell）──
            var cellSize = Context.GetCellSize != null ? Context.GetCellSize() : 100f;
            var spacing = Context.GetStartAxisSpacing != null ? Context.GetStartAxisSpacing() : 0f;
            var groupCount = Context.GetGroupCount != null ? Context.GetGroupCount() : 1;

            var indexInGroup = Index % groupCount;
            var positionInGroup = (cellSize + spacing) * (indexInGroup - (groupCount - 1) * 0.5f);

            // 合成最终位置：水平主轴 → (-localPosition, -positionInGroup)；垂直主轴 → (positionInGroup, localPosition)
            var lp = Context.ScrollDirection == ScrollDirection.Horizontal
                ? new Vector3(-localPosition, -positionInGroup, 0f)
                : new Vector3(positionInGroup, localPosition, 0f);

            // 波浪偏移（原版 Example08 特效）
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
