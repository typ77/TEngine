using GameLogic.DataBinding;
using GameLogic.UI.FancyScrollView;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>Example01 基础 Cell：节点绑定由 _Gen.g.cs 自动生成，本文件只保留业务逻辑。</summary>
    public partial class Example01_BasicCell : FancyCellWidget<Example01_BasicItemData, FancySelectionContext>
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

        // 根节点 Animator/Button（生成器不遍历根节点）+ MVE 绑定，在 ScriptGenerator 之后调用
        protected override void BindMemberProperty()
        {
            m_animator = gameObject.GetComponent<Animator>();
            _displayText = CreateProperty<string>("");
            BindText(m_textMessage, _displayText);

            // 根节点 Button：点击冒泡到 Controller.OnCellItemClicked
            var btn = gameObject.GetComponent<Button>();
            if (btn != null) btn.onClick.AddListener(NotifyItemClicked);
        }

        protected override void BindData(Example01_BasicItemData data)
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
            if (m_imgBackground == null || Context == null) return;
            m_imgBackground.color = Index == Context.SelectedIndex ? SelectedColor : NormalColor;
        }
    }
}
