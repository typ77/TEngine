/*
 * FancyScrollView (https://github.com/setchi/FancyScrollView)
 * Copyright (c) 2020 setchi
 * Licensed under MIT (https://github.com/setchi/FancyScrollView/blob/master/LICENSE)
 */

namespace GameLogic.UI.FancyScrollView
{
    /// <summary>
    /// FancyScrollView Cell 的非泛型统一接口。
    /// Controller/Widget 层只依赖此接口，不感知具体数据类型。
    /// </summary>
    public interface IFancyCell
    {
        int Index { get; set; }
        bool IsVisible { get; }
        void SetVisible(bool visible);
        void UpdateContent(object itemData);
        void UpdatePosition(float position);
        void SetContext(object context);
        void Initialize();
    }

    /// <summary>
    /// FancyScrollView Cell 的泛型接口（类型安全层）。
    /// 继承自 <see cref="IFancyCell"/>，Controller 通过非泛型接口调用，
    /// Cell 端通过显式接口实现将 object 转型为具体类型。
    /// </summary>
    public interface IFancyCell<TItemData, TContext> : IFancyCell
        where TContext : class, new()
    {
    }
}
