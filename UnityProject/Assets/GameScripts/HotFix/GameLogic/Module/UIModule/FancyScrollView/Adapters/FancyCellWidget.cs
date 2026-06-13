using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameLogic.DataBinding;
using GameLogic.UI.FancyScrollView.EasingCore;
using TEngine;
using UnityEngine;

namespace GameLogic.UI.FancyScrollView
{
    /// <summary>
    /// 基于 UIWidget 的 FancyScrollView Cell 基类。
    /// <para>
    /// 与原始 FancyCell（基于 MonoBehaviour）不同，FancyCellWidget 通过
    /// UIWidget.Create() 管理生命周期，支持 MVE 数据绑定、
    /// 异步内容加载以及进出场动画。
    /// </para>
    /// </summary>
    /// <typeparam name="TItemData">列表项数据类型。</typeparam>
    /// <typeparam name="TContext">上下文类型，须有无参构造函数。</typeparam>
    public abstract class FancyCellWidget<TItemData, TContext> : UIWidget, IFancyCell<TItemData, TContext>
        where TContext : class, new()
    {
        private readonly List<IDisposable> _cellProperties = new();
        private CancellationTokenSource _loadCts;
        private ContentLoadState _loadState = ContentLoadState.Skeleton;

        /// <inheritdoc/>
        public int Index { get; set; } = -1;

        /// <inheritdoc/>
        public virtual bool IsVisible => gameObject != null && gameObject.activeSelf;

        /// <summary>当前内容加载状态。</summary>
        public ContentLoadState LoadState => _loadState;

        /// <summary>当前 Cell 的上下文对象。</summary>
        protected TContext Context { get; private set; }

        /// <summary>
        /// 内容资源的资源定位路径，为 null 或空则跳过异步加载。
        /// 子类可重写以提供具体路径。
        /// </summary>
        protected virtual string ContentAssetLocation => null;

        /// <summary>Cell 被点击时触发，参数为 Cell 索引。</summary>
        public event Action<int> OnItemClicked;

        /// <summary>Cell 内按钮被点击时触发，参数为 Cell 索引。</summary>
        public event Action<int> OnButtonClicked;

        /// <inheritdoc/>
        public virtual void SetContext(TContext context)
        {
            Context = context;
        }

        // ── IFancyCell 非泛型接口的显式实现 ─────────────────

        /// <summary>
        /// 显式接口实现：将非泛型 object 数据转为泛型类型后调用 BindData。
        /// </summary>
        void IFancyCell.UpdateContent(object itemData)
        {
            BindData((TItemData)itemData);
        }

        /// <summary>
        /// 显式接口实现：将非泛型 object 上下文转为泛型类型后调用 SetContext。
        /// </summary>
        void IFancyCell.SetContext(object context)
        {
            SetContext((TContext)context);
        }

        /// <inheritdoc/>
        public virtual void Initialize()
        {
        }

        /// <inheritdoc/>
        public virtual void SetVisible(bool visible)
        {
            if (gameObject == null || gameObject.activeSelf == visible)
            {
                return;
            }

            gameObject.SetActive(visible);
            if (visible)
            {
                OnCellVisible();
            }
            else
            {
                OnCellInvisible();
            }
        }

        /// <inheritdoc/>
        public void UpdateContent(TItemData itemData)
        {
            BindData(itemData);
        }

        /// <inheritdoc/>
        public abstract void UpdatePosition(float normalizedPosition);

        #region MVE BindableProperty Helpers

        /// <summary>
        /// 创建一个与 Cell 生命周期绑定的 BindableProperty。
        /// Cell 销毁时自动 Dispose。
        /// </summary>
        protected BindableProperty<T> CreateProperty<T>(T initial = default)
        {
            var property = new BindableProperty<T>(initial);
            _cellProperties.Add(property);
            return property;
        }

        #endregion

        #region Widget Initialization

        /// <summary>
        /// 内部初始化方法，由 FancyCellWidgetFactory 调用。
        /// 封装 UIWidget.Create() 以避免外部直接访问。
        /// </summary>
        internal void InitializeWidget(UIBase parentUI, GameObject widgetRoot, bool visible = true)
        {
            Create(parentUI, widgetRoot, visible);
        }

        #endregion

        #region Data Binding

        /// <summary>绑定数据到 Cell UI，子类必须实现。</summary>
        protected abstract void BindData(TItemData data);

        #endregion

        #region Visibility Lifecycle

        /// <summary>Cell 变为可见时调用。</summary>
        protected virtual void OnCellVisible() { }

        /// <summary>Cell 变为不可见时调用。</summary>
        protected virtual void OnCellInvisible() { }

        #endregion

        #region Content Loading

        /// <summary>内容加载进度回调。</summary>
        protected virtual void OnContentLoading(float progress) { }

        /// <summary>内容加载完成回调。</summary>
        protected virtual void OnContentLoaded() { }

        /// <summary>内容加载失败回调。</summary>
        protected virtual void OnContentError(string error) { }

        /// <summary>内容卸载完成回调。</summary>
        protected virtual void OnContentUnloaded() { }

        #endregion

        #region Event Helpers

        /// <summary>通知 Item 被点击，子类在点击事件中调用。</summary>
        protected void NotifyItemClicked()
        {
            OnItemClicked?.Invoke(Index);
        }

        /// <summary>通知 Cell 内按钮被点击，子类在按钮事件中调用。</summary>
        protected void NotifyButtonClicked()
        {
            OnButtonClicked?.Invoke(Index);
        }

        #endregion

        #region Async Content Loading

        /// <summary>
        /// 异步加载 Cell 内容。若 ContentAssetLocation 为空则直接返回。
        /// 已加载或正在加载时会取消前一次加载。
        /// </summary>
        public async UniTask LoadContentAsync(IProgress<float> progress = null, CancellationToken ct = default)
        {
            if (_loadState == ContentLoadState.Loaded || string.IsNullOrEmpty(ContentAssetLocation))
            {
                return;
            }

            CancelLoad();
            _loadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var linkedToken = _loadCts.Token;
            _loadState = ContentLoadState.Loading;
            OnContentLoading(0f);

            try
            {
                await LoadContentCoreAsync(progress, linkedToken);
                linkedToken.ThrowIfCancellationRequested();
                _loadState = ContentLoadState.Loaded;
                OnContentLoaded();
            }
            catch (OperationCanceledException)
            {
                _loadState = ContentLoadState.Skeleton;
            }
            catch (Exception e)
            {
                _loadState = ContentLoadState.Error;
                OnContentError(e.Message);
                Log.Error(e);
            }
        }

        /// <summary>子类重写以实现具体的异步内容加载逻辑。</summary>
        protected virtual UniTask LoadContentCoreAsync(IProgress<float> progress, CancellationToken ct)
        {
            progress?.Report(1f);
            OnContentLoading(1f);
            return UniTask.CompletedTask;
        }

        /// <summary>取消正在进行的异步加载。</summary>
        public void CancelLoad()
        {
            if (_loadCts == null)
            {
                return;
            }

            _loadCts.Cancel();
            _loadCts.Dispose();
            _loadCts = null;
        }

        /// <summary>卸载内容并重置为 Skeleton 状态。</summary>
        public void UnloadContent()
        {
            CancelLoad();
            _loadState = ContentLoadState.Skeleton;
            OnContentUnloaded();
        }

        #endregion

        #region Animations

        /// <summary>
        /// 播放淡入 + 上移动画。
        /// 需要运行在 PlayMode 环境（依赖 Time.deltaTime）。
        /// </summary>
        public async UniTask PlayFadeInAsync(float duration = 0.3f, CancellationToken ct = default)
        {
            var canvasGroup = gameObject.GetComponent<CanvasGroup>()
                              ?? gameObject.AddComponent<CanvasGroup>();
            var rect = rectTransform;
            var startY = rect.anchoredPosition.y + 30f;
            var targetY = rect.anchoredPosition.y;
            canvasGroup.alpha = 0f;

            var easingFunc = Easing.Get(EasingCore.Ease.OutCubic);

            var elapsed = 0f;
            while (elapsed < duration)
            {
                await UniTask.Yield(ct);
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                var eased = easingFunc(t);
                canvasGroup.alpha = eased;
                rect.anchoredPosition = new Vector2(
                    rect.anchoredPosition.x,
                    Mathf.LerpUnclamped(startY, targetY, eased));
            }

            canvasGroup.alpha = 1f;
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, targetY);
        }

        /// <summary>
        /// 播放缩放弹入动画。
        /// 需要运行在 PlayMode 环境（依赖 Time.deltaTime）。
        /// </summary>
        public async UniTask PlayScaleInAsync(float duration = 0.25f, CancellationToken ct = default)
        {
            var rect = rectTransform;
            const float startScale = 0.8f;

            var easingFunc = Easing.Get(EasingCore.Ease.OutBack);

            var elapsed = 0f;
            while (elapsed < duration)
            {
                await UniTask.Yield(ct);
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                var eased = easingFunc(t);
                var scale = Mathf.LerpUnclamped(startScale, 1f, eased);
                rect.localScale = new Vector3(scale, scale, 1f);
            }

            rect.localScale = Vector3.one;
        }

        #endregion

        #region Cleanup

        protected override void OnDestroy()
        {
            CancelLoad();
            foreach (var property in _cellProperties)
            {
                property.Dispose();
            }
            _cellProperties.Clear();
            OnItemClicked = null;
            OnButtonClicked = null;
            base.OnDestroy();
        }

        #endregion
    }
}
