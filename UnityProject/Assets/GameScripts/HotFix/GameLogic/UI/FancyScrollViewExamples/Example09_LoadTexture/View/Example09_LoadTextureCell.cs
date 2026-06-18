using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameLogic.DataBinding;
using GameLogic.UI.FancyScrollView;
using GameLogic.UI.FancyScrollView.EasingCore;
using TEngine;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// Example09 Cell（原版结构：根 Image(背景)+CanvasGroup，子节点 Title/Description Text + RawImage）。
    /// <para>子节点组件由 _Gen.g.cs 通过 UIBindComponent 绑定（m_textTitle/m_textDescription/m_rimgImage）；
    /// 根节点 Image/CanvasGroup 在 BindMemberProperty 手动获取（生成器不遍历根）。</para>
    /// <para>实现 pop/slide 旋转动画；异步加载网络贴图，Cell 复用时取消旧请求。</para>
    /// </summary>
    public partial class Example09_LoadTextureCell : FancyCellWidget<Example09_LoadTextureItemData, NullContext>
    {
        private Image m_imgBackground;
        private CanvasGroup m_canvasGroup;

        private BindableProperty<string> _displayTitle;
        private BindableProperty<string> _displayDescription;

        // 贴图加载取消令牌（Cell 复用 BindData 时取消前一次）
        private CancellationTokenSource _loadCts;

        // 当前绑定的 URL，回调校验防错位
        private string _currentUrl;

        // pop/slide 动画参数（移植自原版）
        private const float PopAngle = -15f;
        private const float SlideAngle = 25f;
        private const float PopSpan = 0.75f;
        private const float SlideSpan = 0.25f;

        private static readonly EasingFunction AlphaEasing = Easing.Get(TEngine.Ease.OutQuint);

        // 根节点 Image/CanvasGroup 手动获取（生成器不遍历根）+ 子节点 m_textTitle/m_textDescription/m_rimgImage 来自 _Gen.g.cs。
        protected override void BindMemberProperty()
        {
            // 根节点组件（生成器不遍历根）：Image 背景 + CanvasGroup
            m_imgBackground = gameObject.GetComponent<Image>();
            m_canvasGroup = gameObject.GetComponent<CanvasGroup>();

            // 子节点组件（m_textTitle/m_textDescription）来自 _Gen.g.cs
            _displayTitle = CreateProperty<string>("");
            _displayDescription = CreateProperty<string>("");
            BindText(m_textTitle, _displayTitle);
            BindText(m_textDescription, _displayDescription);
        }

        protected override void BindData(Example09_LoadTextureItemData data)
        {
            _displayTitle.Value = data.Title;
            _displayDescription.Value = data.Description;

            // 取消前一次贴图加载，清空 RawImage
            CancelLoad();
            if (m_rimgImage != null) m_rimgImage.texture = null;

            _currentUrl = data.Url;
            if (m_rimgImage != null && !string.IsNullOrEmpty(data.Url))
            {
                LoadTextureAsync(data.Url).Forget();
            }

            UpdateSiblingOrder();
        }

        private async UniTaskVoid LoadTextureAsync(string url)
        {
            _loadCts = new CancellationTokenSource();
            var ct = _loadCts.Token;
            try
            {
                var texture = await Example09_TextureLoader.Instance.LoadAsync(url, ct);
                // 校验：Cell 复用后 URL 可能已变，且回调时 RawImage 可能已销毁
                if (texture == null || m_rimgImage == null || url != _currentUrl) return;
                m_rimgImage.texture = texture;
            }
            catch (System.OperationCanceledException)
            {
                // 正常取消，忽略
            }
        }

        private void CancelLoad()
        {
            if (_loadCts == null) return;
            _loadCts.Cancel();
            _loadCts.Dispose();
            _loadCts = null;
        }

        /// <summary>调整兄弟节点顺序：首项置顶、末项置底，保证边缘 Cell 层级正确。</summary>
        private void UpdateSiblingOrder()
        {
            // Cell（UIWidget）不是 MonoBehaviour，无法用 GetComponent 反查兄弟 Cell 实例。
            // 改为：基于自身 Index 和兄弟节点数量推断首尾。
            // 首项（Index==0）置顶（最后渲染，覆盖其他），末项置底（最先渲染，被覆盖）。
            var siblings = transform.parent.Cast<Transform>();
            Transform firstActive = null;
            Transform lastActive = null;
            foreach (var t in siblings)
            {
                if (!t.gameObject.activeSelf) continue;
                if (firstActive == null) firstActive = t;
                lastActive = t;
            }

            if (ReferenceEquals(transform, firstActive))
            {
                transform.SetAsLastSibling();
            }
            else if (ReferenceEquals(transform, lastActive))
            {
                transform.SetAsFirstSibling();
            }
        }

        public override void UpdatePosition(float t)
        {
            t = 1f - t;

            var pop = Mathf.Min(PopSpan, t) / PopSpan;
            var slide = Mathf.Max(0, t - PopSpan) / SlideSpan;

            if (m_canvasGroup != null)
            {
                transform.localRotation = t < PopSpan
                    ? Quaternion.Euler(0, 0, PopAngle * (1f - pop))
                    : Quaternion.Euler(0, 0, SlideAngle * slide);

                transform.localPosition = Vector3.left * 500f * slide;
                m_canvasGroup.alpha = AlphaEasing(1f - slide);
            }

            if (m_imgBackground != null)
            {
                m_imgBackground.color = Color.Lerp(Color.gray, Color.white, pop);
            }
        }

        protected override void OnDestroy()
        {
            CancelLoad();
            base.OnDestroy();
        }
    }
}
