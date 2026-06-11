using GameLogic.UI.FancyScrollView;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// Example01 基础 Cell。
    /// BindData 显示消息文本，UpdatePosition 使用缩放动画模拟滚动效果。
    /// </summary>
    public class Example01_BasicCell : FancyCellWidget<Example01_BasicItemData, NullContext>
    {
        private Text m_textMessage;

        protected override void ScriptGenerator()
        {
            m_textMessage = FindChildComponent<Text>("m_textMessage");
        }

        protected override void BindData(Example01_BasicItemData data)
        {
            if (m_textMessage != null)
            {
                m_textMessage.text = data.Message;
            }
        }

        public override void UpdatePosition(float normalizedPosition)
        {
            // 基于归一化位置的缩放动画：中心最大(1.0)，边缘缩小(0.8)
            var scale = Mathf.LerpUnclamped(0.8f, 1f, 1f - Mathf.Abs(normalizedPosition - 0.5f) * 2f);
            rectTransform.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
