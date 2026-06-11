using GameLogic.UI.FancyScrollView;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// Example02 聚焦 Cell。
    /// 支持选中态高亮和点击回调。
    /// </summary>
    public class Example02_FocusOnCell : FancyCellWidget<Example02_FocusOnItemData, Example02_FocusOnContext>
    {
        private Text m_textMessage;
        private Image m_imgBackground;

        protected override void ScriptGenerator()
        {
            m_textMessage = FindChildComponent<Text>("m_textMessage");
            // 根节点自带 Image 和 Button，通过 gameObject 获取
            m_imgBackground = gameObject.GetComponent<Image>();
            var btn = gameObject.GetComponent<Button>();
            if (btn != null) btn.onClick.AddListener(() => NotifyItemClicked());
        }

        public override void SetContext(Example02_FocusOnContext context)
        {
            base.SetContext(context);
        }

        protected override void BindData(Example02_FocusOnItemData data)
        {
            if (m_textMessage != null)
            {
                m_textMessage.text = data.Message;
            }
            UpdateSelection();
        }

        public override void UpdatePosition(float normalizedPosition)
        {
            var scale = Mathf.LerpUnclamped(0.8f, 1f, 1f - Mathf.Abs(normalizedPosition - 0.5f) * 2f);
            rectTransform.localScale = new Vector3(scale, scale, 1f);
        }

        private void UpdateSelection()
        {
            if (m_imgBackground == null || Context == null) return;
            var isSelected = Index == Context.SelectedIndex;
            m_imgBackground.color = isSelected
                ? new Color(0.0f, 0.8f, 0.8f, 0.8f)  // 青色半透明
                : new Color(1f, 1f, 1f, 0.8f);         // 白色半透明
        }

        protected override void OnCellVisible()
        {
            UpdateSelection();
        }
    }
}
