using UnityEngine;
using UnityEngine.UI;
using GameLogic.UI.FancyScrollView;

namespace GameLogic
{
    /// <summary>
    /// Example06 内容面板滑动过渡组件（移植自原版 SlideScreenTransition）。
    /// <para>挂在内容面板节点上，由 UIWindow 在 Tab 切换时调用 <see cref="PlayIn"/>/<see cref="PlayOut"/>。</para>
    /// <para>动画期间禁用 <see cref="GraphicRaycaster"/> 防止误触；仅支持水平方向（与 Tab 横向滚动一致）。</para>
    /// </summary>
    public class Example06_SlideScreenTransition : MonoBehaviour
    {
        [SerializeField] private RectTransform m_targetTransform = null;
        [SerializeField] private GraphicRaycaster m_graphicRaycaster = null;

        private const float Duration = 0.3f; // 与 ScrollView 滚动时长保持一致

        private bool _shouldAnimate;
        private bool _isOutAnimation;
        private float _timer;
        private float _startX;
        private float _endX;

        /// <summary>面板滑入（从侧边进入）。</summary>
        public void PlayIn(MovementDirection direction) => Animate(direction, false);

        /// <summary>面板滑出（向侧边退出）。</summary>
        public void PlayOut(MovementDirection direction) => Animate(direction, true);

        private void Animate(MovementDirection direction, bool isOut)
        {
            if (_shouldAnimate || m_targetTransform == null)
            {
                return;
            }

            _timer = Duration;
            _isOutAnimation = isOut;
            _shouldAnimate = true;
            if (m_graphicRaycaster != null) m_graphicRaycaster.enabled = false;

            if (!_isOutAnimation)
            {
                gameObject.SetActive(true);
            }

            switch (direction)
            {
                case MovementDirection.Left:
                    _endX = -m_targetTransform.rect.width;
                    break;

                case MovementDirection.Right:
                    _endX = m_targetTransform.rect.width;
                    break;

                default:
                    Debug.LogWarning("[Example06_SlideScreenTransition] Only horizontal direction is supported.");
                    break;
            }

            _startX = _isOutAnimation ? 0 : -_endX;
            _endX = _isOutAnimation ? _endX : 0;

            UpdatePosition(0f);
        }

        private void Update()
        {
            if (!_shouldAnimate)
            {
                return;
            }

            _timer -= Time.deltaTime;

            if (_timer > 0)
            {
                UpdatePosition(1f - _timer / Duration);
                return;
            }

            _shouldAnimate = false;
            if (m_graphicRaycaster != null) m_graphicRaycaster.enabled = true;

            if (_isOutAnimation)
            {
                gameObject.SetActive(false);
            }

            UpdatePosition(1f);
        }

        private void UpdatePosition(float position)
        {
            if (m_targetTransform == null) return;
            var x = Mathf.Lerp(_startX, _endX, position);
            m_targetTransform.anchoredPosition = new Vector2(x, m_targetTransform.anchoredPosition.y);
        }
    }
}
