using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// 红点语义 key 的序列化载体(MonoBehaviour)。UIWidget 为纯 C# 类无法序列化字段,
    /// prefab 侧固定绑定 key 时把本组件挂在 RedDotWidget 节点上,widget 创建时自动读取并 AddUI。
    /// 动态绑定场景(列表复用/Tab 数据驱动)不用本组件,直接调 RedDotWidget.SetKey。
    /// </summary>
    public class RedDotKeyBinder : MonoBehaviour
    {
        [Header("红点语义key(RedDotKeys 下拉,空=不绑定)")]
        [RedDotKeyDropdown]
        [SerializeField]
        private string m_redDotKey = string.Empty;

        [Header("per-id 绑定 id(列表 cell 场景用,常驻 UI 保持 0)")]
        [SerializeField]
        private uint m_bindId = 0;

        [Header("数字封顶显示(>=cap 显示 cap+)")]
        [SerializeField]
        private int m_countCap = 99;

        /// <summary>红点语义 key(与红点图配置/RedDotKeys 常量表一致;空=不绑定)</summary>
        public string RedDotKey => m_redDotKey;

        /// <summary>per-id 绑定 id</summary>
        public uint BindId => m_bindId;

        /// <summary>数字封顶</summary>
        public int CountCap => m_countCap;
    }

    /// <summary>
    /// 标记红点语义 key 字符串字段,编辑器 Inspector 下拉选择 RedDotKeys.All(编辑器侧 PropertyDrawer)。
    /// </summary>
    public class RedDotKeyDropdownAttribute : PropertyAttribute
    {
    }
}
