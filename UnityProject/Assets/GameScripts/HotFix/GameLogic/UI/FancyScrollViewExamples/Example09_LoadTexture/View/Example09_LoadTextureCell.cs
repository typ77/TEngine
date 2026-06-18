using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameLogic.DataBinding;
using GameLogic.UI.FancyScrollView;
using GameLogic.UI.FancyScrollView.EasingCore;
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

        // Cell transform → 当前数据 Index 的静态映射。
        // 用途：UpdateSiblingOrder 遍历兄弟时反查各 Cell 的 Index。移植版 Cell 是 UIWidget（非 MonoBehaviour），
        // 无法像 setchi 原版那样 GetComponent<Cell>().Index 反查，故用此映射替代。
        // 生命周期：BindData 写入；OnDestroy 移除，避免销毁后残留引用。以 Transform 为 key 天然隔离不同 UI 实例。
        private static readonly Dictionary<Transform, int> s_visibleIndices = new();

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

            // 记录当前 Index 供兄弟 Cell 反查（UpdateSiblingOrder 依赖）
            s_visibleIndices[transform] = Index;

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

        /// <summary>
        /// 调整兄弟节点顺序，还原 setchi 原版 carousel 层级：可见范围内 Index 最小的 Cell 渲染在最上层
        ///（<see cref="Transform.SetAsLastSibling"/>），Index 最大的渲染在最下层
        ///（<see cref="Transform.SetAsFirstSibling"/>），营造"前面的卡片压住后面"的纵深感。
        /// <para>注意：默认对象池创建顺序下，sibling 与 Index 同向（pool[i] 绑定 firstIndex+i），
        /// 即"Index 大的在上层"——与 carousel 需求相反，故必须按 Index 显式反转。</para>
        /// <para>原版用 GetComponent&lt;Cell&gt;().Index 反查兄弟；移植版 Cell 是 UIWidget（非 MonoBehaviour），
        /// 改用 <see cref="s_visibleIndices"/> 静态映射（transform→Index）反查。</para>
        /// </summary>
        private void UpdateSiblingOrder()
        {
            var parent = transform.parent;
            if (parent == null) return;

            // 统计当前可见 Cell 的 Index 范围（不可见的跳过）
            int minIndex = int.MaxValue;
            int maxIndex = int.MinValue;
            foreach (Transform sibling in parent)
            {
                if (!sibling.gameObject.activeSelf) continue;
                if (!s_visibleIndices.TryGetValue(sibling, out var idx)) continue;
                if (idx < minIndex) minIndex = idx;
                if (idx > maxIndex) maxIndex = idx;
            }

            // Index 最小（最靠前）→ 最上层；Index 最大（最靠后）→ 最下层
            if (Index == minIndex) transform.SetAsLastSibling();
            if (Index == maxIndex) transform.SetAsFirstSibling();
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

                // 标量先乘再乘向量（4 次乘法），优于 Vector3.left * 500f * slide（向量×标量×标量，6 次乘法）
                transform.localPosition = 500f * slide * Vector3.left;
                m_canvasGroup.alpha = AlphaEasing(1f - slide);
            }

            if (m_imgBackground != null)
            {
                m_imgBackground.color = Color.Lerp(Color.gray, Color.white, pop);
            }
        }

        protected override void OnDestroy()
        {
            s_visibleIndices.Remove(transform);
            CancelLoad();
            base.OnDestroy();
        }
    }
}
