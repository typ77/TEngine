using UnityEngine;

namespace GameLogic.UI.FancyScrollView
{
    /// <summary>
    /// 继承 <see cref="Scroller"/>，额外持有 CellContainer 引用。
    /// <para>
    /// 配合 <see cref="FancyScrollerController"/> 使用：Controller 从本组件读取 <see cref="CellContainer"/>，
    /// 不再依赖子节点命名约定查找。右键菜单「GameObject/UI/Scroller」会自动创建本组件并完成字段赋值。
    /// </para>
    /// </summary>
    public class FancyScrollerView : Scroller
    {
        [SerializeField] private RectTransform cellContainer;

        /// <summary>Cell 实例的父容器（由本组件持有，Inspector 可设置）。</summary>
        public RectTransform CellContainer => cellContainer;
    }
}
