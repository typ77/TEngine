/*
 * FancyScrollView (https://github.com/setchi/FancyScrollView)
 * Copyright (c) 2020 setchi
 * Licensed under MIT (https://github.com/setchi/FancyScrollView/blob/master/LICENSE)
 */

using UnityEngine;

namespace GameLogic.UI.FancyScrollView
{
    /// <summary>
    /// FancyScrollView Cell 抽象基类。
    /// </summary>
    public abstract class FancyCell<TItemData, TContext> : MonoBehaviour, IFancyCell<TItemData, TContext>
        where TContext : class, new()
    {
        public int Index { get; set; } = -1;
        public virtual bool IsVisible => gameObject.activeSelf;
        protected TContext Context { get; private set; }
        public virtual void SetContext(TContext context) => Context = context;
        public virtual void Initialize() { }
        public virtual void SetVisible(bool visible) => gameObject.SetActive(visible);
        public abstract void UpdateContent(TItemData itemData);
        public abstract void UpdatePosition(float position);

        // ── IFancyCell 非泛型基接口的显式实现 ─────────────────

        /// <summary>显式接口实现：将非泛型 object 数据转为泛型类型。</summary>
        void IFancyCell.UpdateContent(object itemData) => UpdateContent((TItemData)itemData);

        /// <summary>显式接口实现：将非泛型 object 上下文转为泛型类型。</summary>
        void IFancyCell.SetContext(object context) => SetContext((TContext)context);
    }

    /// <summary>
    /// 无 Context 版本的 Cell 抽象基类。
    /// </summary>
    public abstract class FancyCell<TItemData> : FancyCell<TItemData, NullContext>
    {
        public sealed override void SetContext(NullContext context) => base.SetContext(context);
    }
}
