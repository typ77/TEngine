using System;
using UnityEditor;
using UnityEngine;

namespace TEngine.Editor.UI
{
    public enum UIFieldCodeStyle
    {
        /// <summary>
        /// Field names start with underscore (e.g., _variable)
        /// </summary>
        [InspectorName("Field names start with underscore (e.g., _variable)")]
        UnderscorePrefix,

        /// <summary>
        /// Field names start with m_ prefix (e.g., m_variable)
        /// </summary>
        [InspectorName("Field names start with m_ prefix (e.g., m_variable)")]
        MPrefix,

        [InspectorName("占位符(选择无效)")]
        Max,
    }

    [Serializable]
    public class ScriptGenerateRuler
    {
        [SerializeField] public string uiElementRegex;
        [SerializeField] public UIComponentName componentName;
        [SerializeField] public bool isUIWidget = false;

        /// <summary>
        /// 是否需要自定义初始化。
        /// <para>标记为 true 的规则不走标准 GetComponent 绑定，而走特殊路径生成
        /// （如 Scroller 生成 <c>FancyScrollerController</c> + <c>new</c> + <c>Initialize</c>）。</para>
        /// </summary>
        [SerializeField] public bool isCustomInit = false;

        public ScriptGenerateRuler(string uiElementRegex, UIComponentName componentName, bool isUIWidget = false, bool isCustomInit = false)
        {
            this.uiElementRegex = uiElementRegex;
            this.componentName = componentName;
            this.isUIWidget = isUIWidget;
            this.isCustomInit = isCustomInit;
        }
    }

    [Serializable]
    public class UIGenType
    {
        public UIGenType(string uiTypeName, bool isGeneric, bool noBaseClass = false)
        {
            this.uiTypeName = uiTypeName;
            this.isGeneric = isGeneric;
            this.noBaseClass = noBaseClass;
        }

        public string uiTypeName;
        public bool isGeneric;

        /// <summary>
        /// 是否生成无基类 partial（如 FancyScrollView 的 Cell）。
        /// <para>标记为 true 时，<c>_Gen.g.cs</c> 只输出 <c>public partial class X</c>，
        /// 不附加 <c>: 基类</c> 与 <c>[Window]</c>；泛型基类由业务文件 partial 自行声明
        /// （例如 <c>public partial class X : FancyCellWidget&lt;TData,TContext&gt;</c>）。</para>
        /// </summary>
        public bool noBaseClass;
    }

    [CustomPropertyDrawer(typeof(ScriptGenerateRuler))]
    public class ScriptGenerateRulerDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            position = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);
            var indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            var uiElementRegexRect = new Rect(position.x, position.y, 120, position.height);
            var componentNameRect = new Rect(position.x + 125, position.y, 150, position.height);
            var isUIWidgetRect = new Rect(position.x + 325, position.y, 150, position.height);
            var isCustomInitRect = new Rect(position.x + 480, position.y, 100, position.height);
            EditorGUI.PropertyField(uiElementRegexRect, property.FindPropertyRelative("uiElementRegex"), GUIContent.none);
            EditorGUI.PropertyField(componentNameRect, property.FindPropertyRelative("componentName"), GUIContent.none);
            EditorGUI.PropertyField(isUIWidgetRect, property.FindPropertyRelative("isUIWidget"), GUIContent.none);
            EditorGUI.PropertyField(isCustomInitRect, property.FindPropertyRelative("isCustomInit"), GUIContent.none);
            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }
    }

    public enum UIComponentName
    {
        GameObject,
        Transform,
        RectTransform,
        Text,
        RichTextItem,
        Button,
        Image,
        RawImage,
        FancyScrollRectProxy,
        FancyScroller,
        ScrollRect,
        Scrollbar,
        InputField,
        GridLayoutGroup,
        HorizontalLayoutGroup,
        VerticalLayoutGroup,
        Slider,
        Toggle,
        ToggleGroup,
        AnimationCurve,
        CanvasGroup,
        TextMeshProUGUI,
        Canvas,
        Dropdown,
        Animator,
        TMP_InputField,
        TMP_Dropdown,
    }
}