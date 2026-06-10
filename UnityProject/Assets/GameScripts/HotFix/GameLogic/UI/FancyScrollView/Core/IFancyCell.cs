/*
 * FancyScrollView (https://github.com/setchi/FancyScrollView)
 * Copyright (c) 2020 setchi
 * Licensed under MIT (https://github.com/setchi/FancyScrollView/blob/master/LICENSE)
 */

namespace GameLogic.UI.FancyScrollView
{
    /// <summary>
    /// FancyScrollView Cell 的统一接口。
    /// 原始 FancyCell 与 TEngine FancyCellWidget 都实现此接口，使 Cell 创建管线可被 UIWidget 接管。
    /// </summary>
    public interface IFancyCell<TItemData, TContext> where TContext : class, new()
    {
        int Index { get; set; }
        bool IsVisible { get; }
        void SetVisible(bool visible);
        void UpdateContent(TItemData itemData);
        void UpdatePosition(float position);
        void SetContext(TContext context);
        void Initialize();
    }
}
