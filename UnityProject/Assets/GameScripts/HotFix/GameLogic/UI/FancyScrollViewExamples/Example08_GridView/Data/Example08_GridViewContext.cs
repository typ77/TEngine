using System;
using GameLogic.UI.FancyScrollView;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// Example08 共享上下文：继承 <see cref="FancySelectionContext"/>（复用选中态），
    /// 实现 <see cref="IFancyGridViewContext"/>（公共属性）。
    /// </summary>
    /// <remarks>
    /// 6 个接口成员由引擎 <see cref="FancyGridView{TItemData, TContext}.Initialize"/> 与 Setup 填充，
    /// 业务字段（OnCellClicked）由 UIWindow 通过 Widget.EngineContext 设置。
    /// </remarks>
    public class Example08_GridViewContext : FancySelectionContext, IFancyGridViewContext
    {
        // ── IFancyScrollRectContext ──
        /// <inheritdoc />
        public ScrollDirection ScrollDirection { get; set; }
        /// <inheritdoc />
        public Func<(float ScrollSize, float ReuseMargin)> CalculateScrollSize { get; set; }

        // ── IFancyCellGroupContext ──
        /// <inheritdoc />
        public GameObject CellTemplate { get; set; }
        /// <inheritdoc />
        public Func<int> GetGroupCount { get; set; }

        // ── IFancyGridViewContext ──
        /// <inheritdoc />
        public Func<float> GetStartAxisSpacing { get; set; }
        /// <inheritdoc />
        public Func<float> GetCellSize { get; set; }

        // ── 业务字段 ──
        /// <summary>Cell 点击回调。</summary>
        public Action<int> OnCellClicked;
    }
}
