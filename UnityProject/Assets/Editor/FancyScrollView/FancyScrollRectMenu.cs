using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using GameLogic.UI.FancyScrollView;

namespace TEngine.Editor.UI
{
    /// <summary>
    /// 右键菜单：创建标准 ScrollRect 风格节点（与 <see cref="FancyScrollRectWidget{TItemData, TContext}"/> 配套）。
    /// <para>
    /// 生成结构：
    /// <code>
    /// m_fscroll_List  [RectTransform, Image, Mask, Scroller, FancyScrollRectProxy]  ← viewport 指向自身
    ///   ├─ Scrollbar  [Scrollbar]  ← 水平/垂直滚动条（可选）
    ///   └─ (Cell 容器 = m_fscroll_List 自身 transform，由引擎运行时填充)
    /// </code>
    /// </para>
    /// <para>
    /// 注意：与 <see cref="FancyScrollerMenu"/> 不同，本节点不挂 <see cref="FancyScrollerView"/>，
    /// 而是挂基础 <see cref="Scroller"/> + <see cref="FancyScrollRectProxy"/>。
    /// Cell 容器是 proxy 自身 transform（见 <c>FancyScrollRectWidget.InternalScrollRect.ApplyProxy</c>）。
    /// </para>
    /// </summary>
    public static class FancyScrollRectMenu
    {
        [MenuItem("GameObject/UI/ScrollRect List", false, 11)]
        public static void CreateScrollRectList()
        {
            var root = new GameObject("m_fscroll_List");
            var rect = root.AddComponent<RectTransform>();

            var parent = Selection.activeTransform;
            if (parent != null)
            {
                rect.SetParent(parent, false);
            }

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            // Image + Mask：裁剪越界 Cell
            var image = root.AddComponent<Image>();
            image.color = Color.white;
            root.AddComponent<Mask>().showMaskGraphic = false;

            // Scroller：viewport 指向自身
            var scroller = root.AddComponent<Scroller>();
            var soScroller = new SerializedObject(scroller);
            soScroller.FindProperty("viewport").objectReferenceValue = rect;
            soScroller.ApplyModifiedProperties();

            // FancyScrollRectProxy：承载 cellSize/spacing/padding 等布局参数
            var proxy = root.AddComponent<FancyScrollRectProxy>();

            // Scrollbar 子节点（垂直方向，默认配置；可按需调整方向）
            var scrollbarGo = new GameObject("Scrollbar");
            var scrollbarRect = scrollbarGo.AddComponent<RectTransform>();
            scrollbarRect.SetParent(rect, false);
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 1f);
            scrollbarRect.pivot = new Vector2(1f, 0.5f);
            scrollbarRect.sizeDelta = new Vector2(20f, 0f);
            scrollbarRect.anchoredPosition = Vector2.zero;

            var scrollbarImg = scrollbarGo.AddComponent<Image>();
            scrollbarImg.color = new Color(0.3f, 0.3f, 0.3f, 0.5f);
            var scrollbar = scrollbarGo.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            // 滑块子节点
            var slidingAreaGo = new GameObject("Sliding Area");
            var slidingAreaRect = slidingAreaGo.AddComponent<RectTransform>();
            slidingAreaRect.SetParent(scrollbarRect, false);
            slidingAreaRect.anchorMin = Vector2.zero;
            slidingAreaRect.anchorMax = Vector2.one;
            slidingAreaRect.offsetMin = new Vector2(10f, 10f);
            slidingAreaRect.offsetMax = new Vector2(-10f, -10f);

            var handleGo = new GameObject("Handle");
            var handleRect = handleGo.AddComponent<RectTransform>();
            handleRect.SetParent(slidingAreaRect, false);
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = handleRect.offsetMax = Vector2.zero;
            var handleImg = handleGo.AddComponent<Image>();
            handleImg.color = new Color(0.8f, 0.8f, 0.8f, 0.8f);

            var soScrollbar = new SerializedObject(scrollbar);
            soScrollbar.FindProperty("m_Handle").objectReferenceValue = handleRect;
            soScrollbar.FindProperty("m_TargetGraphic").objectReferenceValue = handleImg;
            soScrollbar.ApplyModifiedProperties();

            // 关联 Scroller 的 scrollbar 字段
            var soScroller2 = new SerializedObject(scroller);
            soScroller2.FindProperty("scrollbar").objectReferenceValue = scrollbar;
            soScroller2.ApplyModifiedProperties();

            Undo.RegisterCreatedObjectUndo(root, "Create ScrollRect List");
            Selection.activeGameObject = root;
        }

        [MenuItem("GameObject/UI/ScrollRect List", true)]
        public static bool ValidateCreateScrollRectList() => true;
    }
}
