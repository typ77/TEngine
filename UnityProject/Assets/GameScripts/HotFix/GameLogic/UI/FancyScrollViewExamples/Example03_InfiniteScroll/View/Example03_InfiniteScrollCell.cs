using GameLogic.DataBinding;
using GameLogic.UI.FancyScrollView;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// Example03 无限循环 Cell（原版结构：根 Animator + Image/Button + Text）。
    /// 子节点组件由 _Gen.g.cs 通过 UIBindComponent 自动绑定（m_imgBackground/m_textMessage），
    /// 本文件只保留根节点 Animator 手动获取（生成器不遍历根）+ 业务逻辑。
    /// </summary>
    public partial class Example03_InfiniteScrollCell : FancyCellWidget<Example03_InfiniteScrollItemData, FancySelectionContext>
    {
        private Animator m_animator;
        private BindableProperty<string> _displayText;
        private float m_currentPosition;

        private static class AnimatorHash
        {
            public static readonly int Scroll = Animator.StringToHash("scroll");
        }

        private static readonly Color SelectedColor = new Color(0.0f, 0.8f, 0.8f, 0.8f);
        private static readonly Color NormalColor = new Color(1f, 1f, 1f, 0.8f);

        // 根节点 Animator + MVE 绑定，在 ScriptGenerator 之后调用（m_imgBackground/m_textMessage 已由 _Gen 绑定就绪）。
        protected override void BindMemberProperty()
        {
            // 根节点组件（生成器不遍历根）：Animator 手动获取
            m_animator = gameObject.GetComponent<Animator>();

            // 子节点组件（m_textMessage）来自 _Gen.g.cs
            _displayText = CreateProperty<string>("");
            BindText(m_textMessage, _displayText);

            // Button 与 Image 共节点（m_imgBackground 上挂 Button），同节点获取后冒泡点击
            var btn = m_imgBackground.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.AddListener(NotifyItemClicked);
            }
        }

        protected override void BindData(Example03_InfiniteScrollItemData data)
        {
            _displayText.Value = data.DisplayText;
            UpdateSelection();
        }

        public override void UpdatePosition(float normalizedPosition)
        {
            m_currentPosition = normalizedPosition;

            if (m_animator != null && m_animator.isActiveAndEnabled)
            {
                m_animator.Play(AnimatorHash.Scroll, -1, normalizedPosition);
                m_animator.speed = 0f;
            }

            UpdateSelection();
        }

        protected override void OnCellVisible()
        {
            if (m_animator != null && m_animator.isActiveAndEnabled)
            {
                m_animator.Play(AnimatorHash.Scroll, -1, m_currentPosition);
                m_animator.speed = 0f;
            }

            UpdateSelection();
        }

        private void UpdateSelection()
        {
            if (m_imgBackground == null || Context == null)
            {
                return;
            }
            m_imgBackground.color = Index == Context.SelectedIndex ? SelectedColor : NormalColor;
        }
    }
}
