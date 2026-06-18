using GameLogic.DataBinding;
using GameLogic.UI.FancyScrollView;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// Example06 LoopTabBar 的 Tab Cell（原版结构：根 Animator + Text + Button）。
    /// 原版 Cell 无 UIBindComponent，节点组件在 BindMemberProperty 中通过 GetComponentInChildren 获取。
    /// </summary>
    public partial class Example06_LoopTabBarCell : FancyCellWidget<Example06_LoopTabBarItemData, FancySelectionContext>
    {
        private Animator m_animator;
        private Text m_textMessage;
        private Button m_button;
        private BindableProperty<string> _displayText;
        private float m_currentPosition;

        private static class AnimatorHash
        {
            public static readonly int Scroll = Animator.StringToHash("scroll");
        }

        // 根节点 Animator/Button + MVE 绑定，在 ScriptGenerator 之后调用。
        protected override void BindMemberProperty()
        {
            m_animator = gameObject.GetComponent<Animator>();
            m_textMessage = gameObject.GetComponentInChildren<Text>();
            m_button = gameObject.GetComponentInChildren<Button>();

            _displayText = CreateProperty<string>("");
            if (m_textMessage != null)
            {
                BindText(m_textMessage, _displayText);
            }

            // 点击冒泡到 Controller.OnCellItemClicked
            if (m_button != null)
            {
                m_button.onClick.AddListener(NotifyItemClicked);
            }
        }

        protected override void BindData(Example06_LoopTabBarItemData data)
        {
            _displayText.Value = data.DisplayText;
        }

        public override void UpdatePosition(float normalizedPosition)
        {
            m_currentPosition = normalizedPosition;

            if (m_animator != null && m_animator.isActiveAndEnabled)
            {
                m_animator.Play(AnimatorHash.Scroll, -1, normalizedPosition);
                m_animator.speed = 0f;
            }
        }

        protected override void OnCellVisible()
        {
            if (m_animator != null && m_animator.isActiveAndEnabled)
            {
                m_animator.Play(AnimatorHash.Scroll, -1, m_currentPosition);
                m_animator.speed = 0f;
            }
        }
    }
}
