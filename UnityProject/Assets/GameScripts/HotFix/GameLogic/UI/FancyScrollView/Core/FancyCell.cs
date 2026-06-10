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
    }

    /// <summary>
    /// 无 Context 版本的 Cell 抽象基类。
    /// </summary>
    public abstract class FancyCell<TItemData> : FancyCell<TItemData, NullContext>
    {
        public sealed override void SetContext(NullContext context) => base.SetContext(context);
    }
}
