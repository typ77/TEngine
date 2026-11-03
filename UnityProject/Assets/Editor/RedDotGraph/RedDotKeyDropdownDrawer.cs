#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using RedDot.Editor.Data;
using RedDot.Runtime.RedDot;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace RedDot.Editor
{
    /// <summary>
    /// 红点语义 key 字段下拉([RedDotKeyDropdown]):默认树形(编辑器组树同源,AdvancedDropdown+搜索,数据=RedDotKeyTreeBuilder);
    /// 无图/加载失败回落平铺 Popup(选项=RedDotKeys.All)。序列化值不在可选项内时(图更新前/版本错配)追加为尾部选项保值显示,不改值防丢。
    /// </summary>
    [CustomPropertyDrawer(typeof(GameLogic.RedDotKeyDropdownAttribute))]
    public class RedDotKeyDropdownDrawer : PropertyDrawer
    {
        // 展开态全字段共享(编辑器工具,可接受);static 随 domain reload 重置
        private static readonly AdvancedDropdownState s_dropdownState = new AdvancedDropdownState();

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var cur = property.stringValue ?? string.Empty;
            RedDotKeyTreeBuildResult tree;
            if (RedDotKeyTreeBuilder.TryGetTree(out tree))
            {
                DrawTree(position, property, label, cur, tree);
            }
            else
            {
                DrawFlat(position, property, label, cur);
            }
        }

        private static void DrawTree(Rect position, SerializedProperty property, GUIContent label, string cur, RedDotKeyTreeBuildResult tree)
        {
            var fieldRect = EditorGUI.PrefixLabel(position, label);
            var content = new GUIContent(BuildButtonLabel(cur, tree));
            if (EditorGUI.DropdownButton(fieldRect, content, FocusType.Keyboard, EditorStyles.popup))
            {
                // AdvancedDropdown.ItemSelected 晚于 Inspector 的 Apply 窗口:闭包里直写 SerializedProperty
                // 不会提交(write-without-apply 目标对象读回为空)。捕获 targetObjects+path,回调内
                // 重开 SerializedObject 写入并 Apply(顺带记录 Undo)。
                var targets = property.serializedObject.targetObjects;
                var propertyPath = property.propertyPath;
                var dropdown = new RedDotKeyAdvancedDropdown(s_dropdownState, tree, cur, key =>
                {
                    foreach (var target in targets)
                    {
                        var so = new SerializedObject(target);
                        var prop = so.FindProperty(propertyPath);
                        if (prop != null)
                        {
                            prop.stringValue = key;
                            so.ApplyModifiedProperties();
                        }
                    }
                });
                dropdown.Show(fieldRect);
            }
        }

        private static string BuildButtonLabel(string cur, RedDotKeyTreeBuildResult tree)
        {
            if (string.IsNullOrEmpty(cur))
            {
                return "(不绑定)";
            }
            if (tree.IsKnown(cur))
            {
                return string.Concat(cur, "  (", tree.ExternalIdByKey[cur].ToString(), ")");
            }
            return string.Concat(cur, "  (未知key)");
        }

        /// <summary>平铺兜底(无图/构建失败时的原行为):选项=RedDotKeys.All(key + externalId) + "(不绑定)"。</summary>
        private static void DrawFlat(Rect position, SerializedProperty property, GUIContent label, string cur)
        {
            var keys = RedDotKeys.All;
            bool unknown = !string.IsNullOrEmpty(cur);
            if (keys != null)
            {
                for (int i = 0; i < keys.Length && unknown; i++)
                {
                    if (keys[i].Key == cur)
                    {
                        unknown = false;
                    }
                }
            }
            int baseCount = (keys?.Length ?? 0) + 1;          // +1 = (不绑定)
            int count = unknown ? baseCount + 1 : baseCount;
            var labels = new string[count];
            var values = new string[count];
            labels[0] = "(不绑定)";
            values[0] = string.Empty;
            if (keys != null)
            {
                for (int i = 0; i < keys.Length; i++)
                {
                    labels[i + 1] = string.Concat(keys[i].Key, "  (", keys[i].ExternalId.ToString(), ")");
                    values[i + 1] = keys[i].Key;
                }
            }
            int idx = Array.IndexOf(values, cur);
            if (unknown)
            {
                labels[count - 1] = string.Concat(cur, "  (未知key)");
                values[count - 1] = cur;
                idx = count - 1;
            }
            int next = EditorGUI.Popup(position, label.text, idx, labels);
            if (values[next] != cur)
            {
                property.stringValue = values[next];
            }
        }

        /// <summary>叶子项:携带可选 key(空串=不绑定);普通项=文件夹(不可选)。</summary>
        private class KeyDropdownItem : AdvancedDropdownItem
        {
            public readonly string Key;

            public KeyDropdownItem(string title, string key) : base(title)
            {
                Key = key;
            }
        }

        private class RedDotKeyAdvancedDropdown : AdvancedDropdown
        {
            private readonly RedDotKeyTreeBuildResult _tree;
            private readonly string _currentKey;
            private readonly Action<string> _onSelected;

            public RedDotKeyAdvancedDropdown(AdvancedDropdownState state, RedDotKeyTreeBuildResult tree, string currentKey, Action<string> onSelected) : base(state)
            {
                _tree = tree;
                _currentKey = currentKey;
                _onSelected = onSelected;
                minimumSize = new Vector2(440f, 320f);
            }

            protected override AdvancedDropdownItem BuildRoot()
            {
                var root = new AdvancedDropdownItem("红点key");
                root.AddChild(new KeyDropdownItem("(不绑定)", string.Empty));
                AddChildren(root, _tree.Root.Children);
                if (!string.IsNullOrEmpty(_currentKey) && !_tree.IsKnown(_currentKey))
                {
                    root.AddChild(new KeyDropdownItem(string.Concat(_currentKey, "  (未知key)"), _currentKey));
                }
                return root;
            }

            private void AddChildren(AdvancedDropdownItem parent, List<RedDotKeyTreeEntry> children)
            {
                if (children == null)
                {
                    return;
                }
                var used = new HashSet<string>(); // 兄弟同名去重(AdvancedDropdown 搜索按名索引)
                foreach (var child in children)
                {
                    if (child.Children != null)
                    {
                        var folder = new AdvancedDropdownItem(DedupTitle(child.Label, used));
                        parent.AddChild(folder);
                        AddChildren(folder, child.Children);
                    }
                    else
                    {
                        parent.AddChild(new KeyDropdownItem(DedupTitle(BuildLeafTitle(child), used), child.Key));
                    }
                }
            }

            private static string BuildLeafTitle(RedDotKeyTreeEntry leaf)
            {
                var title = leaf.Label != leaf.Key
                    ? string.Concat(leaf.Label, " — ", leaf.Key)
                    : leaf.Key;
                if (leaf.Unexported)
                {
                    return string.Concat(title, "  (未导出)");
                }
                return string.Concat(title, "  (", leaf.ExternalId.ToString(), ")");
            }

            private static string DedupTitle(string title, HashSet<string> used)
            {
                if (string.IsNullOrEmpty(title))
                {
                    title = "(未命名)";
                }
                if (used.Add(title))
                {
                    return title;
                }
                var n = 2;
                while (!used.Add(string.Concat(title, " (", n.ToString(), ")")))
                {
                    n++;
                }
                return string.Concat(title, " (", n.ToString(), ")");
            }

            protected override void ItemSelected(AdvancedDropdownItem item)
            {
                var keyItem = item as KeyDropdownItem;
                if (keyItem != null && keyItem.Key != null)
                {
                    _onSelected?.Invoke(keyItem.Key);
                }
            }
        }
    }
}
#endif
