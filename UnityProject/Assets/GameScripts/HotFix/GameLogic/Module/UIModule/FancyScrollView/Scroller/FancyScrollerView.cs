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

        // ── ScrollView 布局参数（Inspector 可调，prefab 上配置；FancyScrollerController 初始化时读取）──
        [SerializeField, Range(1e-2f, 1f)] private float cellInterval = 0.2f;
        [SerializeField, Range(0f, 1f)] private float scrollOffset = 0.5f;
        [SerializeField] private bool loop = false;

        /// <summary>Cell 实例的父容器（由本组件持有，Inspector 可设置）。</summary>
        public RectTransform CellContainer => cellContainer;

        /// <summary>Cell 间隔（归一化 0.01~1），Inspector 可调。Controller 初始化时读取。</summary>
        public float CellInterval => cellInterval;

        /// <summary>滚动偏移（归一化 0~1），Inspector 可调。Controller 初始化时读取。</summary>
        public float ScrollOffset => scrollOffset;

        /// <summary>是否循环滚动，Inspector 可调。Controller 初始化时读取。</summary>
        public bool Loop => loop;

        /// <summary>关联的 Controller（由 Controller.Initialize 注入，供 Editor 运行时热调参数）。</summary>
        internal FancyScrollerController Controller { get; set; }

#if UNITY_EDITOR
        // Editor（含 PlayMode）下检测 Inspector 参数变化，实时同步到 Controller 并刷新，实现可视化调试。
        private float _cachedCellInterval;
        private float _cachedScrollOffset;
        private bool _cachedLoop;
        private bool _cacheInited;

        private void LateUpdate()
        {
            if (Controller == null)
            {
                return;
            }

            bool changed = false;

            if (!_cacheInited || !Mathf.Approximately(_cachedCellInterval, cellInterval))
            {
                Controller.CellInterval = cellInterval;
                _cachedCellInterval = cellInterval;
                changed = true;
            }

            if (!_cacheInited || !Mathf.Approximately(_cachedScrollOffset, scrollOffset))
            {
                Controller.ScrollOffset = scrollOffset;
                _cachedScrollOffset = scrollOffset;
                changed = true;
            }

            if (!_cacheInited || _cachedLoop != loop)
            {
                Controller.Loop = loop;
                _cachedLoop = loop;
                changed = true;
            }

            _cacheInited = true;

            if (changed)
            {
                Controller.Refresh();
            }
        }
#endif
    }
}
