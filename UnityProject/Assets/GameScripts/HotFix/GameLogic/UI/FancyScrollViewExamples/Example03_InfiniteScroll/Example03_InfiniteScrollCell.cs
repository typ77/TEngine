using GameLogic.UI.FancyScrollView;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// Example03 无限循环 Cell。
    /// 循环滚动下位置连续，缩放动画使滚动更自然。
    /// </summary>
    public class Example03_InfiniteScrollCell : FancyCellWidget<Example03_InfiniteScrollItemData, NullContext>
    {
        private Text m_textMessage;

        protected override void ScriptGenerator()
        {
            m_textMessage = FindChildComponent<Text>("m_textMessage");
        }

        protected override void BindData(Example03_InfiniteScrollItemData data)
        {
            if (m_textMessage != null)
            {
                m_textMessage.text = data.Message;
            }
        }

        public override void UpdatePosition(float normalizedPosition)
        {
            var scale = Mathf.LerpUnclamped(0.8f, 1f, 1f - Mathf.Abs(normalizedPosition - 0.5f) * 2f);
            rectTransform.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
