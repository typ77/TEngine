using System;
using UnityEngine;

namespace GameLogic.UI.FancyScrollView
{
    /// <summary>
    /// FancyCellGroup 的 Widget 化包装器。
    /// <para>
    /// 替代原始 FancyCellGroup.InstantiateCells() 的默认实现（通过 GetComponent&lt;FancyCell&gt;），
    /// 改用 FancyCellWidget 生命周期管理（InitializeWidget），并订阅子 Cell 的事件向上冒泡。
    /// </para>
    /// </summary>
    /// <typeparam name="TItemData">列表项数据类型。</typeparam>
    /// <typeparam name="TContext">上下文类型，须实现 IFancyGridViewContext。</typeparam>
    public class CellGroupWrapper<TItemData, TContext> : FancyCellGroup<TItemData, TContext>
        where TContext : class, IFancyGridViewContext, new()
    {
        private UIBase _parentUI;

        /// <summary>子 Cell 的 ItemClicked 事件冒泡。</summary>
        public event Action<int> CellItemClicked;

        /// <summary>子 Cell 的 ButtonClicked 事件冒泡。</summary>
        public event Action<int> CellButtonClicked;

        /// <summary>设置父 UI 基类引用，用于 FancyCellWidget.InitializeWidget。</summary>
        public void SetParent(UIBase parentUI)
        {
            _parentUI = parentUI;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// 使用 Context.CellTemplate 实例化子 GameObject，
        /// 通过 FancyCellWidget.InitializeWidget 管理 UIWidget 生命周期，
        /// 并订阅子 Cell 的 OnItemClicked/OnButtonClicked 事件。
        /// </remarks>
        protected override FancyCell<TItemData, TContext>[] InstantiateCells()
        {
            var cells = new FancyCell<TItemData, TContext>[Context.GetGroupCount()];
            for (var i = 0; i < cells.Length; i++)
            {
                var go = Instantiate(Context.CellTemplate, transform);
                var widget = go.GetComponent<FancyCellWidget<TItemData, TContext>>();
                if (widget == null)
                {
                    Debug.LogError(
                        $"[CellGroupWrapper] FancyCellWidget<{typeof(TItemData).Name}, {typeof(TContext).Name}> " +
                        $"component not found in {Context.CellTemplate.name}.");
                    Destroy(go);
                    continue;
                }

                widget.InitializeWidget(_parentUI ?? this, go);
                widget.OnItemClicked += OnChildItemClicked;
                widget.OnButtonClicked += OnChildButtonClicked;
                cells[i] = widget;
            }

            return cells;
        }

        private void OnChildItemClicked(int index)
        {
            CellItemClicked?.Invoke(index);
        }

        private void OnChildButtonClicked(int index)
        {
            CellButtonClicked?.Invoke(index);
        }

        protected virtual void OnDestroy()
        {
            CellItemClicked = null;
            CellButtonClicked = null;
        }

#if UNITY_EDITOR
        internal int CellCountForTest => Cells?.Length ?? 0;
        internal FancyCellWidget<TItemData, TContext> GetCellForTest(int index) =>
            Cells[index] as FancyCellWidget<TItemData, TContext>;
#endif
    }
}
