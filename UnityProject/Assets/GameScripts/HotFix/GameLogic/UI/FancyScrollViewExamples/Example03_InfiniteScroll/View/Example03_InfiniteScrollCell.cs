using GameLogic.DataBinding;
using GameLogic.UI.FancyScrollView;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// Example03 无限循环 Cell（原版结构：根 Animator + Image/Button + Text）。
    /// 原版 Cell 无 UIBindComponent，节点组件在 BindMemberProperty 中通过 GetComponentInChildren 获取。
    /// </summary>
    public partial class Example03_InfiniteScrollCell : FancyCellWidget<Example03_InfiniteScrollItemData, FancySelectionContext>
    {
        private Animator m_animator;
        private Image m_imgBackground;
        private Text m_textMessage;
        private BindableProperty<string> _displayText;
        private float m_currentPosition;

        private static class AnimatorHash
        {
            public static readonly int Scroll = Animator.StringToHash("scroll");
        }

        private static readonly Color SelectedColor = new Color(0.0f, 0.8f, 0.8f, 0.8f);
        private static readonly Color NormalColor = new Color(1f, 1f, 1f, 0.8f);

        // 根节点 Animator + MVE 绑定，在 ScriptGenerator 之后调用。
        // 原版结构：Image(背景+Button) 与 Text 为根的子节点，故用 GetComponentInChildren。
        protected override void BindMemberProperty()
        {
            m_animator = gameObject.GetComponent<Animator>();
            m_imgBackground = gameObject.GetComponentInChildren<Image>();
            var texts = gameObject.GetComponentsInChildren<Text>();
            m_textMessage = texts != null && texts.Length > 0 ? texts[0] : null;

            _displayText = CreateProperty<string>("");
            if (m_textMessage != null)
            {
                BindText(m_textMessage, _displayText);
            }

            // Button 在 Image 子节点：点击冒泡到 Controller.OnCellItemClicked
            var btn = gameObject.GetComponentInChildren<Button>();
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
