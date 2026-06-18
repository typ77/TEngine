using System;
using System.Collections.Generic;
using GameLogic.UI.FancyScrollView.EasingCore;
using TEngine;
using UnityEngine;

namespace GameLogic.UI.FancyScrollView
{
    /// <summary>
    /// 基于 FancyScrollViewWidget 的 GridView 容器 Widget。
    /// <para>
    /// 内部创建 <see cref="InternalGridView"/> 实例桥接 FancyGridView 引擎，
    /// 使用 <see cref="CellGroupWrapper{TItemData,TContext}"/> 作为 Cell Group 以管理子 Cell Widget。
    /// </para>
    /// <para>
    /// 注意：FancyGridView 继承 FancyScrollRect&lt;TItemData[], TContext&gt;，
    /// 因此 CreateCell 的类型参数是 TItemData[]（一组 item）。
    /// InternalGridView 直接重写 CreateCell 来创建 CellGroupWrapper。
    /// </para>
    /// </summary>
    /// <typeparam name="TItemData">列表项数据类型。</typeparam>
    /// <typeparam name="TContext">上下文类型，须实现 IFancyGridViewContext。</typeparam>
    public abstract class FancyGridViewWidget<TItemData, TContext> : FancyScrollViewWidget<TItemData, TContext>
        where TContext : class, IFancyGridViewContext, new()
    {
        private FancyScrollRectProxy _proxy;
        private InternalGridView _gridView;

        /// <summary>内部的 GridView 引擎实例。</summary>
        protected InternalGridView GridViewEngine => _gridView;

        /// <summary>
        /// 引擎持有的共享上下文（可读写业务字段，如 SelectedIndex）。
        /// <para>引擎在 <see cref="FancyGridView.Initialize"/> 时填充接口成员，业务字段由 UIWindow 通过此属性设置。</para>
        /// </summary>
        protected TContext EngineContext => _gridView != null ? _gridView.GetContextInternal() : null;

        /// <summary>每行/列的 Cell 数量（startAxisCellCount）。</summary>
        protected abstract int StartAxisCellCount { get; }

        /// <summary>交叉轴方向的 Cell 间距。</summary>
        protected virtual float StartAxisSpacing => 0f;

        /// <summary>交叉轴方向的 Cell 尺寸。</summary>
        protected virtual Vector2 CellSize2D => new Vector2(100f, 100f);

        protected override void ScriptGenerator()
        {
            _proxy = FindChildComponent<FancyScrollRectProxy>("m_fscroll_List");
            base.ScriptGenerator();
        }

        /// <summary>
        /// 显式注入 Proxy 并立即创建 GridView 引擎（供作为子组件挂载的子类调用）。
        /// <para>语义同 <see cref="FancyScrollRectWidget{TItemData, TContext}.InitWithProxy"/>：
        /// 子类重写 ScriptGenerator 时用 <c>GetComponent&lt;FancyScrollRectProxy&gt;()</c> 取根节点 Proxy 后调用。</para>
        /// </summary>
        protected void InitWithProxy(FancyScrollRectProxy proxy)
        {
            _proxy = proxy;
            if (_proxy == null)
            {
                Debug.LogError($"[FancyGridViewWidget] {gameObject.name} InitWithProxy: proxy 为 null。");
                return;
            }

            CreateInternalGridView();
        }

        protected override void OnCreate()
        {
            // 若 InitWithProxy 已创建引擎则跳过（子组件挂载路径）
            if (_gridView == null)
            {
                CreateInternalGridView();
            }
        }

        protected virtual void CreateInternalGridView()
        {
            if (_proxy == null)
            {
                Debug.LogError("[FancyGridViewWidget] FancyScrollRectProxy not found on m_fscroll_List.");
                return;
            }

            var gridViewGo = _proxy.gameObject;
            _gridView = gridViewGo.AddComponent<InternalGridView>();
            _gridView.Initialize(this, _proxy, StartAxisCellCount, StartAxisSpacing, CellSize2D);
        }

        /// <summary>更新列表内容。</summary>
        public void UpdateContents(IList<TItemData> items)
        {
            _gridView?.UpdateContents(items);
        }

        /// <summary>跳转到指定索引位置。</summary>
        public void JumpTo(int itemIndex, float alignment = 0.5f)
        {
            _gridView?.PublicJumpTo(itemIndex, alignment);
        }

        /// <summary>平滑滚动到指定索引位置。</summary>
        public void ScrollTo(int index, float duration, float alignment = 0.5f, Action onComplete = null)
        {
            _gridView?.PublicScrollTo(index, duration, alignment, onComplete);
        }

        /// <summary>平滑滚动到指定索引位置（带缓动）。</summary>
        public void ScrollTo(int index, float duration, Ease easing, float alignment = 0.5f, Action onComplete = null)
        {
            _gridView?.PublicScrollTo(index, duration, easing, alignment, onComplete);
        }

        /// <summary>
        /// GridView 不使用基类的 CreateCell（因为类型参数不同：TItemData vs TItemData[]），
        /// InternalGridView 直接重写 FancyScrollRect.CreateCell 来创建 CellGroupWrapper。
        /// 此方法保留以避免编译警告，但不应被 InternalGridView 调用。
        /// </summary>
        protected override IFancyCell<TItemData, TContext> CreateCell(GameObject prefab, Transform container)
        {
            // For GridView, this is not the path used by the engine.
            // The InternalGridView overrides FancyScrollRect<TItemData[], TContext>.CreateCell directly.
            // This path would only be hit if someone manually calls CreateCell on the Widget.
            throw new InvalidOperationException(
                "FancyGridViewWidget.CreateCell should not be called directly. " +
                "The InternalGridView handles cell group creation internally.");
        }

        /// <summary>
        /// 供 InternalGridView 使用的回调：当子 Cell 触发 ItemClicked 事件时调用。
        /// </summary>
        private void HandleGroupCellItemClicked(int index)
        {
            RaiseCellItemClicked(index);
            OnCellClicked(index);
        }

        /// <summary>
        /// 供 InternalGridView 使用的回调：当子 Cell 触发 ButtonClicked 事件时调用。
        /// </summary>
        private void HandleGroupCellButtonClicked(int index)
        {
            RaiseCellButtonClicked(index);
            OnCellButtonItemClicked(index);
        }

        protected override void OnDestroy()
        {
            _gridView = null;
            _proxy = null;
            base.OnDestroy();
        }

        /// <summary>
        /// 内部桥接类：继承 FancyGridView，通过 Setup 使用 CellGroupWrapper，
        /// 并将参数从 Widget/Proxy 注入。
        /// </summary>
        /// <remarks>
        /// FancyGridView 继承 FancyScrollRect&lt;TItemData[], TContext&gt;，
        /// 因此 CreateCell 返回 IFancyCell&lt;TItemData[], TContext&gt;（一组 item）。
        /// </remarks>
        protected class InternalGridView : FancyGridView<TItemData, TContext>
        {
            private FancyGridViewWidget<TItemData, TContext> _owner;

            /// <summary>初始化 GridView 引擎。</summary>
            public void Initialize(
                FancyGridViewWidget<TItemData, TContext> owner,
                FancyScrollRectProxy proxy,
                int axisCellCount,
                float axisSpacing,
                Vector2 cellSize2D)
            {
                _owner = owner;

                // FancyGridView hides FancyScrollRect.cellSize (float) with cellSize (Vector2).
                // Set the Vector2 field which the sealed CellSize property reads from.
                this.cellSize = cellSize2D;
                this.spacing = proxy.Spacing;
                this.paddingHead = proxy.PaddingHead;
                this.paddingTail = proxy.PaddingTail;
                this.reuseCellMarginCount = proxy.ReuseCellMarginCount;

                // GridView-specific settings
                this.startAxisCellCount = axisCellCount;
                this.startAxisSpacing = axisSpacing;

                cellContainer = proxy.transform;
            }

            /// <summary>暴露引擎持有的 Context（protected → internal 桥接，供 Widget 层访问）。</summary>
            internal TContext GetContextInternal() => Context;

            /// <summary>设置顶部内边距（GridView 继承 ScrollRect 字段）。</summary>
            internal void SetPaddingHead(float value) { paddingHead = value; }
            /// <summary>设置底部内边距。</summary>
            internal void SetPaddingTail(float value) { paddingTail = value; }
            /// <summary>设置主轴 Cell 间距。</summary>
            internal void SetSpacing(float value) { spacing = value; }
            /// <summary>获取顶部内边距。</summary>
            internal float GetPaddingHead() => paddingHead;
            /// <summary>获取底部内边距。</summary>
            internal float GetPaddingTail() => paddingTail;
            /// <summary>获取主轴 Cell 间距。</summary>
            internal float GetSpacing() => spacing;

            /// <summary>刷新所有可见 Cell。</summary>
            internal void PublicRefresh() => Refresh();
            /// <summary>重新布局。</summary>
            internal void PublicRelayout() => Relayout();

            /// <summary>
            /// 重写 FancyScrollRect&lt;TItemData[], TContext&gt;.CreateCell。
            /// 创建 CellGroupWrapper 实例，订阅子 Cell 事件。
            /// </summary>
            protected override IFancyCell<TItemData[], TContext> CreateCell(
                GameObject prefab, Transform container)
            {
                var go = UnityEngine.Object.Instantiate(prefab, container);
                var group = go.GetComponent<CellGroupWrapper<TItemData, TContext>>();
                if (group == null)
                {
                    UnityEngine.Object.Destroy(go);
                    throw new MissingComponentException(
                        $"CellGroupWrapper<{typeof(TItemData).Name}, {typeof(TContext).Name}> " +
                        $"component not found in {prefab.name}.");
                }

                group.SetParent(_owner);
                group.CellItemClicked += _owner.HandleGroupCellItemClicked;
                group.CellButtonClicked += _owner.HandleGroupCellButtonClicked;
                return group;
            }

            /// <inheritdoc/>
            protected override void SetupCellTemplate()
            {
                var cellGo = _owner.CellPrefab;
                if (cellGo == null)
                {
                    Debug.LogError("[InternalGridView] Owner CellPrefab is null. Call InitializeAsync first.");
                    return;
                }

                // Setup needs a FancyCell<TItemData, TContext> as template reference.
                // The CellGroupWrapper.InstantiateCells will use Context.CellTemplate (the gameObject)
                // to create actual child cells. Here we just need a reference instance.
                var tempGo = Instantiate(cellGo);
                var tempCell = tempGo.GetComponent<FancyCell<TItemData, TContext>>();
                if (tempCell == null)
                {
                    Debug.LogError(
                        "[InternalGridView] Cell prefab does not have FancyCell component. " +
                        "Add FancyCellWidget<TItemData, TContext> to the prefab.");
                    Destroy(tempGo);
                    return;
                }

                Setup<CellGroupWrapper<TItemData, TContext>>(tempCell);
            }

            /// <summary>公开桥接：跳转到指定索引。</summary>
            public void PublicJumpTo(int index, float alignment) => JumpTo(index, alignment);

            /// <summary>公开桥接：平滑滚动（无缓动）。</summary>
            public void PublicScrollTo(int index, float duration, float alignment, Action onComplete) =>
                ScrollTo(index, duration, alignment, onComplete);

            /// <summary>公开桥接：平滑滚动（带缓动）。</summary>
            public void PublicScrollTo(int index, float duration, Ease easing, float alignment, Action onComplete) =>
                ScrollTo(index, duration, easing, alignment, onComplete);
        }
    }
}
