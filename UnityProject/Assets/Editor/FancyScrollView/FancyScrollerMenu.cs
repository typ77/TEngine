using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using GameLogic.UI.FancyScrollView;

namespace TEngine.Editor.UI
{
    /// <summary>
    /// 右键菜单：创建标准 FancyScroller 结构（与 <see cref="FancyScrollerController"/> 配套）。
    /// <para>
    /// 生成结构：
    /// <code>
    /// m_scroller  [RectTransform, CanvasRenderer, Image, Mask, FancyScrollerView]  ← viewport 指向自身
    ///   └─ CellContainer  [RectTransform]  ← cellContainer 字段自动赋值
    /// </code>
    /// CellContainer 无需 m_ 前缀：它不作为 ScriptGenerator 的独立绑定字段，而是由 Scroller 组件
    /// （<see cref="FancyScrollerView"/>）持有，Controller 运行时从该组件读取。
    /// </para>
    /// </summary>
    public static class FancyScrollerMenu
    {
        [MenuItem("GameObject/UI/Scroller", false, 10)]
        public static void CreateScroller()
        {
            // 根节点 m_scroller
            var root = new GameObject("m_scroller");
            var rect = root.AddComponent<RectTransform>();

            // 若选中了节点，则挂到其下
            var parent = Selection.activeTransform;
            if (parent != null)
            {
                rect.SetParent(parent, false);
            }

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            // Image + Mask：裁剪越界 Cell（showMaskGraphic=false 不显示遮罩图）
            var image = root.AddComponent<Image>();
            image.color = Color.white;
            root.AddComponent<Mask>().showMaskGraphic = false;

            // 子节点 CellContainer
            var cellGo = new GameObject("CellContainer");
            var cellRect = cellGo.AddComponent<RectTransform>();
            cellRect.SetParent(rect, false);
            cellRect.anchorMin = Vector2.zero;
            cellRect.anchorMax = Vector2.one;
            cellRect.offsetMin = cellRect.offsetMax = Vector2.zero;

            // 挂 FancyScrollerView，配置 viewport（指向自身）与 cellContainer
            var view = root.AddComponent<FancyScrollerView>();
            var so = new SerializedObject(view);
            so.FindProperty("viewport").objectReferenceValue = rect;
            so.FindProperty("cellContainer").objectReferenceValue = cellRect;
            so.ApplyModifiedProperties();

            // 整树注册 Undo，便于撤销
            Undo.RegisterCreatedObjectUndo(root, "Create Scroller");

            Selection.activeGameObject = root;
        }

        [MenuItem("GameObject/UI/Scroller", true)]
        public static bool ValidateCreateScroller() => true;
    }
}
