using System.Threading;
using Cysharp.Threading.Tasks;
using GameLogic.UI.FancyScrollView;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// Example06 内容面板滑动过渡组件（UIWidget）。
    /// <para>作为 Window prefab 的根 Widget，由 <see cref="Example06_LoopTabBarUI"/> 通过
    /// <c>CreateWidgetByPathAsync</c> 异步加载。Tab 切换时 <see cref="PlayIn"/>/<see cref="PlayOut"/>
    /// 用 UniTask 协程驱动 <c>m_target_Transform</c> 水平滑动；动画期间禁用 <see cref="GraphicRaycaster"/> 防误触。</para>
    /// <para>仅支持水平方向（与 Tab 横向滚动一致）。</para>
    /// </summary>
    public class Example06_SlideScreenTransition : UIWidget
    {
        private RectTransform m_targetTransform;
        private GraphicRaycaster m_graphicRaycaster;
        private Text m_textLabel;

        /// <summary>滑动时长（与 ScrollView 滚动时长保持一致）。</summary>
        private const float Duration = 0.3f;

        /// <summary>当前动画的取消令牌；新的 <see cref="PlayIn"/>/<see cref="PlayOut"/> 会取消上一次未完成的动画。</summary>
        private CancellationTokenSource _cts;

        // 根节点组件：GraphicRaycaster 在根节点，ScriptGenerator 阶段绑定。
        protected override void ScriptGenerator()
        {
            m_graphicRaycaster = gameObject.GetComponent<GraphicRaycaster>();
        }

        // 子节点组件在 ScriptGenerator 之后绑定。
        protected override void BindMemberProperty()
        {
            m_targetTransform = FindChildComponent<RectTransform>("m_target_Transform");
            m_textLabel = FindChildComponent<Text>("m_target_Transform/m_text_Label");
        }

        /// <summary>设置面板标签文字（如 "Window 0"）。</summary>
        public void SetLabel(string text)
        {
            if (m_textLabel != null) m_textLabel.text = text;
        }

        /// <summary>面板滑入（从侧边进入）。</summary>
        public void PlayIn(MovementDirection direction) => RunAnimate(direction, false).Forget();

        /// <summary>面板滑出（向侧边退出）。</summary>
        public void PlayOut(MovementDirection direction) => RunAnimate(direction, true).Forget();

        /// <summary>执行一次滑动过渡；新调用会取消上一次未完成的动画。</summary>
        private async UniTask RunAnimate(MovementDirection direction, bool isOut)
        {
            if (m_targetTransform == null)
            {
                return;
            }

            // 仅支持水平方向（Left/Right），其它方向忽略
            if (direction != MovementDirection.Left && direction != MovementDirection.Right)
            {
                return;
            }

            // 取消上一次动画
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            float sign = direction == MovementDirection.Left ? -1f : 1f;
            float width = m_targetTransform.rect.width;
            float startX = isOut ? 0f : -sign * width;
            float endX = isOut ? sign * width : 0f;

            if (!isOut)
            {
                Visible = true;
            }

            if (m_graphicRaycaster != null)
            {
                m_graphicRaycaster.enabled = false;
            }

            try
            {
                await AnimatePosition(startX, endX, ct);
            }
            catch (System.OperationCanceledException)
            {
                // 被新动画或销毁取消，保持当前状态退出
                return;
            }

            if (m_graphicRaycaster != null)
            {
                m_graphicRaycaster.enabled = true;
            }

            if (isOut)
            {
                Visible = false;
            }
        }

        /// <summary>线性插值移动 m_targetTransform 的 anchoredPosition.x。</summary>
        private async UniTask AnimatePosition(float startX, float endX, CancellationToken ct)
        {
            float elapsed = 0f;
            while (elapsed < Duration)
            {
                await UniTask.Yield(ct);
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / Duration);
                SetX(Mathf.Lerp(startX, endX, t));
            }

            SetX(endX);
        }

        private void SetX(float x)
        {
            if (m_targetTransform == null)
            {
                return;
            }

            var pos = m_targetTransform.anchoredPosition;
            m_targetTransform.anchoredPosition = new Vector2(x, pos.y);
        }

        protected override void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            base.OnDestroy();
        }
    }
}
