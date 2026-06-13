using System;
using System.Collections.Generic;
using GameLogic.UI.FancyScrollView.EasingCore;
using TEngine;
using UnityEngine;

namespace GameLogic.UI.FancyScrollView
{
    /// <summary>
    /// 基于 FancyScrollViewWidget 的 ScrollRect 容器 Widget。
    /// <para>
    /// 通过 <see cref="FancyScrollRectProxy"/> 读取 Inspector 配置，
    /// 内部创建 <see cref="InternalScrollRect"/> 实例桥接 FancyScrollRect 引擎。
    /// </para>
    /// </summary>
    /// <typeparam name="TItemData">列表项数据类型。</typeparam>
    /// <typeparam name="TContext">上下文类型，须实现 IFancyScrollRectContext。</typeparam>
    public abstract class FancyScrollRectWidget<TItemData, TContext> : FancyScrollViewWidget<TItemData, TContext>
        where TContext : class, IFancyScrollRectContext, new()
    {
        private FancyScrollRectProxy _proxy;
        private InternalScrollRect _scrollRect;

        /// <summary>内部的 FancyScrollRect 引擎实例（GameObject 组件）。</summary>
        protected InternalScrollRect ScrollRectEngine => _scrollRect;

        /// <summary>
        /// ScriptGenerator 中应查找 m_fscroll_List 节点并缓存 Proxy。
        /// 子类重写时必须调用 base.ScriptGenerator()。
        /// </summary>
        protected override void ScriptGenerator()
        {
            _proxy = FindChildComponent<FancyScrollRectProxy>("m_fscroll_List");
            base.ScriptGenerator();
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            CreateInternalScrollRect();
        }

        /// <summary>
        /// 创建 InternalScrollRect 引擎。virtual 以允许 FancyGridViewWidget 跳过此步骤。
        /// </summary>
        protected virtual void CreateInternalScrollRect()
        {
            if (_proxy == null)
            {
                Debug.LogError("[FancyScrollRectWidget] FancyScrollRectProxy not found on m_fscroll_List.");
                return;
            }

            // Create InternalScrollRect as a sibling of the proxy's GameObject
            var scrollRectGo = _proxy.gameObject;
            _scrollRect = scrollRectGo.AddComponent<InternalScrollRect>();
            _scrollRect.Initialize(this, _proxy);
        }

        /// <summary>更新列表内容，代理到 InternalScrollRect。</summary>
        public void UpdateContents(IList<TItemData> items)
        {
            _scrollRect?.PublicUpdateContents(items);
        }

        /// <summary>跳转到指定索引位置。</summary>
        public void JumpTo(int itemIndex, float alignment = 0.5f)
        {
            _scrollRect?.PublicJumpTo(itemIndex, alignment);
        }

        /// <summary>平滑滚动到指定索引位置。</summary>
        public void ScrollTo(int index, float duration, float alignment = 0.5f, Action onComplete = null)
        {
            _scrollRect?.PublicScrollTo(index, duration, alignment, onComplete);
        }

        /// <summary>平滑滚动到指定索引位置（带缓动）。</summary>
        public void ScrollTo(int index, float duration, Ease easing, float alignment = 0.5f, Action onComplete = null)
        {
            _scrollRect?.PublicScrollTo(index, duration, easing, alignment, onComplete);
        }

        protected override void OnDestroy()
        {
            // InternalScrollRect is on the proxy GO; don't destroy it here,
            // the UIWidget lifecycle will handle the hierarchy.
            _scrollRect = null;
            _proxy = null;
            base.OnDestroy();
        }

        /// <summary>
        /// 内部桥接类：继承 FancyScrollRect，将 CreateCell/CellPrefab 委托给 Owner Widget。
        /// </summary>
        protected class InternalScrollRect : FancyScrollRect<TItemData, TContext>
        {
            private FancyScrollRectWidget<TItemData, TContext> _owner;

            /// <summary>用 Proxy 参数初始化滚动引擎。</summary>
            public void Initialize(FancyScrollRectWidget<TItemData, TContext> owner, FancyScrollRectProxy proxy)
            {
                _owner = owner;
                ApplyProxy(proxy);
            }

            /// <summary>将 Proxy Inspector 参数映射到 FancyScrollRect 的 protected 字段。</summary>
            private void ApplyProxy(FancyScrollRectProxy proxy)
            {
                cellSize = proxy.CellSize;
                spacing = proxy.Spacing;
                paddingHead = proxy.PaddingHead;
                paddingTail = proxy.PaddingTail;
                reuseCellMarginCount = proxy.ReuseCellMarginCount;

                // cellContainer must be the proxy's transform (or a child)
                cellContainer = proxy.transform;

                // Set scroll direction via context (set during Initialize)
                // The Scroller component must already be on the same GameObject
            }

            /// <inheritdoc/>
            /// <remarks>返回 Owner 的 CellPrefab，由 Widget 异步加载。</remarks>
            protected override GameObject CellPrefab => _owner?.CellPrefab;

            /// <inheritdoc/>
            /// <remarks>委托到 Owner 的 CreateCellForInternal，确保 UIWidget 生命周期管理。</remarks>
            protected override IFancyCell<TItemData, TContext> CreateCell(GameObject prefab, Transform container)
            {
                return _owner.CreateCellForInternal(prefab, container);
            }

            /// <summary>公开桥接：更新列表内容。</summary>
            public void PublicUpdateContents(IList<TItemData> items) => UpdateContents(items);

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
