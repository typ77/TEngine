using System;
using GameLogic.UI.FancyScrollView;

namespace GameLogic
{
    /// <summary>
    /// Example07 共享上下文：继承 <see cref="FancySelectionContext"/>（复用选中态），
    /// 实现 <see cref="IFancyScrollRectContext"/>（公共属性，便于 Cell 直接调用），
    /// 并携带点击回调。
    /// </summary>
    /// <remarks>
    /// 接口成员（ScrollDirection/CalculateScrollSize）由引擎 <see cref="FancyScrollRect{TItemData, TContext}.Initialize"/> 填充，
    /// 业务字段（OnCellClicked）由 UIWindow 通过 Widget.EngineContext 设置。
    /// </remarks>
    public class Example07_ScrollRectContext : FancySelectionContext, IFancyScrollRectContext
    {
        /// <inheritdoc />
        public ScrollDirection ScrollDirection { get; set; }

        /// <inheritdoc />
        public Func<(float ScrollSize, float ReuseMargin)> CalculateScrollSize { get; set; }

        /// <summary>Cell 点击回调（Cell → UI）。</summary>
        public Action<int> OnCellClicked;
    }
}
