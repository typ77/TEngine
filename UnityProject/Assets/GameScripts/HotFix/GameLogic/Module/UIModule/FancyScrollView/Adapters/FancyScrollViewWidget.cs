using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;
using YooAsset;

namespace GameLogic.UI.FancyScrollView
{
    /// <summary>
    /// FancyScrollView 容器 Widget 基类。
    /// <para>
    /// 负责 Cell Prefab 的异步加载、Cell 实例的创建与事件冒泡。
    /// 子类（FancyScrollRectWidget / FancyGridViewWidget）在此基础上桥接具体的滚动引擎。
    /// </para>
    /// <para>
    /// 注意：本基类服务于 ScrollRect/GridView 引擎路径。
    /// Scroller 引擎路径使用独立的 <see cref="FancyScrollerWidget"/> + <see cref="FancyScrollerController"/>。
    /// </para>
    /// </summary>
    /// <typeparam name="TItemData">列表项数据类型。</typeparam>
    /// <typeparam name="TContext">上下文类型。</typeparam>
    public abstract class FancyScrollViewWidget<TItemData, TContext> : UIWidget
        where TContext : class, new()
    {
        private AssetHandle _cellPrefabHandle;
        private GameObject _cellPrefab;

        /// <summary>Cell 被点击时触发，参数为 Cell 索引。</summary>
        public event Action<int> OnCellItemClicked;

        /// <summary>Cell 内按钮被点击时触发，参数为 Cell 索引。</summary>
        public event Action<int> OnCellButtonClicked;

        /// <summary>触发 OnCellItemClicked 事件。</summary>
        protected void RaiseCellItemClicked(int index)
        {
            OnCellItemClicked?.Invoke(index);
        }

        /// <summary>触发 OnCellButtonClicked 事件。</summary>
        protected void RaiseCellButtonClicked(int index)
        {
            OnCellButtonClicked?.Invoke(index);
        }

        /// <summary>已加载的 Cell Prefab；仅在 InitializeAsync 成功后有效。</summary>
        protected GameObject CellPrefab => _cellPrefab;

        /// <summary>
        /// 异步加载 Cell Prefab 并缓存。
        /// 多次调用会释放前一次的 AssetHandle。
        /// </summary>
        /// <param name="cellAssetLocation">YooAsset 资源定位路径。</param>
        /// <param name="progress">加载进度回调（可选）。</param>
        /// <param name="ct">取消令牌。</param>
        public async UniTask InitializeAsync(
            string cellAssetLocation,
            IProgress<float> progress = null,
            CancellationToken ct = default)
        {
            ReleaseCellPrefabHandle();

            _cellPrefabHandle = GameModule.Resource.LoadAssetAsyncHandle<GameObject>(cellAssetLocation);

            while (!_cellPrefabHandle.IsDone)
            {
                progress?.Report(_cellPrefabHandle.Progress);
                await UniTask.Yield(ct);
            }

            ct.ThrowIfCancellationRequested();

            if (_cellPrefabHandle.Status == EOperationStatus.Failed)
            {
                throw new InvalidOperationException(
                    $"Cell prefab load failed: {cellAssetLocation}, error: {_cellPrefabHandle.LastError}");
            }

            _cellPrefab = _cellPrefabHandle.AssetObject as GameObject;
            if (_cellPrefab == null)
            {
                throw new InvalidOperationException(
                    $"Cell prefab load failed (asset is null): {cellAssetLocation}");
            }
        }

        /// <summary>
        /// 子类重写以创建具体类型的 Cell Widget 实例。
        /// <para>只需 <c>return new ExampleXX_XXXCell();</c>，基类负责 Instantiate + InitializeWidget + 事件订阅。</para>
        /// </summary>
        protected virtual FancyCellWidget<TItemData, TContext> CreateCellWidget() => null;

        /// <summary>
        /// 创建一个 FancyCellWidget 实例，订阅其事件并返回 IFancyCell 接口。
        /// <para>优先使用 <see cref="CreateCellWidget"/> 创建 Cell；返回 null 时回退到 GetComponent。</para>
        /// </summary>
        protected virtual IFancyCell<TItemData, TContext> CreateCell(GameObject prefab, Transform container)
        {
            var go = UnityEngine.Object.Instantiate(prefab, container);
            FancyCellWidget<TItemData, TContext> cell = CreateCellWidget();

            if (cell == null)
            {
                // 回退：尝试 GetComponent（仅 MonoBehaviour Cell 可用）
                try { cell = go.GetComponent<FancyCellWidget<TItemData, TContext>>(); }
                catch (ArgumentException) { }
            }

            if (cell == null)
            {
                UnityEngine.Object.DestroyImmediate(go);
                throw new MissingComponentException(
                    $"{GetType().Name}: CreateCellWidget() returned null and GetComponent failed. " +
                    $"Override CreateCellWidget() to return a new cell instance.");
            }

            cell.InitializeWidget(this, go);
            cell.OnItemClicked += HandleCellClicked;
            cell.OnButtonClicked += HandleCellButtonClicked;
            return cell;
        }

        /// <summary>
        /// 供 InternalScrollRect / InternalGridView 的 CreateCell 回调使用。
        /// 与 CreateCell 相同，但不订阅事件（事件由 CellGroupWrapper 层管理）。
        /// </summary>
        internal virtual IFancyCell<TItemData, TContext> CreateCellForInternal(GameObject prefab, Transform container)
        {
            return CreateCell(prefab, container);
        }

        private void HandleCellClicked(int index)
        {
            RaiseCellItemClicked(index);
            OnCellClicked(index);
        }

        private void HandleCellButtonClicked(int index)
        {
            RaiseCellButtonClicked(index);
            OnCellButtonItemClicked(index);
        }

        /// <summary>Cell 被点击时的虚方法，子类可重写。</summary>
        protected virtual void OnCellClicked(int index) { }

        /// <summary>Cell 内按钮被点击时的虚方法，子类可重写。</summary>
        protected virtual void OnCellButtonItemClicked(int index) { }

        private void ReleaseCellPrefabHandle()
        {
            _cellPrefab = null;
            _cellPrefabHandle?.Dispose();
            _cellPrefabHandle = null;
        }

        protected override void OnDestroy()
        {
            ReleaseCellPrefabHandle();
            OnCellItemClicked = null;
            OnCellButtonClicked = null;
            base.OnDestroy();
        }

#if UNITY_EDITOR
        internal void SetCellPrefabForTest(GameObject prefab) => _cellPrefab = prefab;
        internal IFancyCell<TItemData, TContext> CreateCellForTest(GameObject prefab, Transform container) =>
            CreateCell(prefab, container);
#endif
    }
}
