using System.Collections.Generic;
using TEngine;
using UnityEngine;

namespace GameLogic.UI.FancyScrollView
{
    /// <summary>
    /// 桥接 Scroller + FancyScrollView 的 Widget 适配器。
    /// <para>
    /// 子类只需实现 ScriptGenerator（绑定 m_scroller 和 m_cellContainer），
    /// 即可获得完整的虚拟化列表能力。Cell 通过 FancyCellWidget 创建。
    /// </para>
    /// <para>
    /// 使用流程：
    /// 1. ScriptGenerator 中绑定 Scroller 和 CellContainer
    /// 2. OnCreate 中调用 InitializeAsync 加载 Cell Prefab
    /// 3. 调用 UpdateData 设置数据
    /// </para>
    /// </summary>
    /// <typeparam name="TItemData">列表项数据类型。</typeparam>
    /// <typeparam name="TContext">上下文类型。</typeparam>
    public abstract class FancyScrollerWidget<TItemData, TContext>
        : FancyScrollViewWidget<TItemData, TContext>
        where TContext : class, new()
    {
        // ── 子类在 ScriptGenerator 中必须绑定的字段 ──────────
        private Scroller m_scroller;
        private Transform m_cellContainer;

        // ── 内部桥接 ──────────────────────────────────────
        private InternalScrollView _internalScrollView;

        /// <summary>绑定的 Scroller 组件。</summary>
        protected Scroller ScrollerComponent => m_scroller;

        /// <summary>Cell 父容器。</summary>
        protected Transform CellContainerTransform => m_cellContainer;

        /// <summary>
        /// 获取与所有 Cell 共享的 Context 对象。
        /// <para>仅限 <typeparamref name="TContext"/> 非 <see cref="NullContext"/> 时有意义。</para>
        /// </summary>
        public TContext SharedContext => _internalScrollView != null
            ? _internalScrollView.GetSharedContext()
            : null;

        /// <summary>
        /// 设置 Scroller 和 CellContainer 引用。
        /// 子类在 ScriptGenerator 中调用此方法。
        /// </summary>
        protected void BindScroller(Scroller scroller, Transform cellContainer)
        {
            m_scroller = scroller;
            m_cellContainer = cellContainer;
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            if (m_scroller == null || m_cellContainer == null)
            {
                Log.Error("[FancyScrollerWidget] Scroller or CellContainer not bound. " +
                          "Call BindScroller in ScriptGenerator.");
                return;
            }

            // 创建 InternalScrollView 作为桥接 MonoBehaviour
            var bridgeGo = new GameObject("InternalScrollView");
            bridgeGo.transform.SetParent(m_cellContainer.parent);
            _internalScrollView = bridgeGo.AddComponent<InternalScrollView>();
            _internalScrollView.Initialize(this, m_cellContainer, CellPrefab);

            // 桥接 Scroller → InternalScrollView
            m_scroller.OnValueChanged(position => _internalScrollView.TriggerUpdatePosition(position));
        }

        // ── 公开 API ──────────────────────────────────────

        /// <summary>
        /// 更新列表数据并刷新显示。
        /// </summary>
        /// <param name="items">数据列表。</param>
        public void UpdateData(IList<TItemData> items)
        {
            if (_internalScrollView == null) return;
            m_scroller.SetTotalCount(items.Count);
            _internalScrollView.TriggerUpdateContents(items);
        }

        /// <summary>
        /// 滚动到指定索引位置。
        /// </summary>
        /// <param name="index">目标索引。</param>
        /// <param name="duration">动画时长（秒）。</param>
        public void ScrollTo(int index, float duration = 0.3f)
        {
            m_scroller.ScrollTo(index, duration);
        }

        /// <summary>
        /// 立即跳转到指定索引位置（无动画）。
        /// </summary>
        /// <param name="index">目标索引。</param>
        public void JumpTo(int index)
        {
            m_scroller.JumpTo(index);
        }

        /// <summary>
        /// 强制刷新所有可见 Cell 的内容。
        /// </summary>
        public void Refresh()
        {
            _internalScrollView?.TriggerRefresh();
        }

        /// <summary>
        /// 重新布局（修改 padding/spacing 后调用）。
        /// </summary>
        public void Relayout()
        {
            _internalScrollView?.TriggerRelayout();
        }

        // ── 虚方法 ────────────────────────────────────────

        /// <summary>
        /// Cell 间隔（0.01 ~ 1），默认 0.2。子类可重写。
        /// </summary>
        protected virtual float CellInterval => 0.2f;

        /// <summary>
        /// 滚动偏移（0 ~ 1），默认 0.5。
        /// </summary>
        protected virtual float ScrollOffset => 0.5f;

        /// <summary>
        /// 是否启用循环滚动，默认 false。
        /// </summary>
        protected virtual bool Loop => false;

        // ── 内部桥接类 ────────────────────────────────────

        /// <summary>
        /// 内部 MonoBehaviour 桥接类，将 FancyScrollView 的虚拟化逻辑
        /// 与 Widget 生命周期连接。挂载为隐藏子对象。
        /// 通过公共 Trigger* 方法暴露 protected 基类方法给外部 Widget。
        /// </summary>
        private class InternalScrollView : FancyScrollView<TItemData, TContext>
        {
            private FancyScrollerWidget<TItemData, TContext> _owner;

            public void Initialize(
                FancyScrollerWidget<TItemData, TContext> owner,
                Transform container,
                GameObject prefab)
            {
                _owner = owner;
                cellContainer = container;
                cellPrefab = prefab;
                cellInterval = owner.CellInterval;
                scrollOffset = owner.ScrollOffset;
                loop = owner.Loop;
            }

            protected override GameObject CellPrefab => cellPrefab;

            protected override IFancyCell<TItemData, TContext> CreateCell(
                GameObject prefab, Transform container)
            {
                return _owner.CreateCell(prefab, container);
            }

            // ── 公共包装方法（暴露 protected 基类方法）──────────

            public void TriggerUpdatePosition(float position) => UpdatePosition(position);
            public void TriggerUpdateContents(IList<TItemData> items) => UpdateContents(items);
            public void TriggerRefresh() => Refresh();
            public void TriggerRelayout() => Relayout();
            public TContext GetSharedContext() => Context;
        }

        // ── 清理 ──────────────────────────────────────────

        protected override void OnDestroy()
        {
            if (_internalScrollView != null)
            {
                var go = _internalScrollView.gameObject;
                _internalScrollView = null;
                if (go != null) UnityEngine.Object.Destroy(go);
            }
            base.OnDestroy();
        }
    }
}
