using System.Diagnostics;
using RedDot.Editor.Data;
using RedDot.Editor.Util;
using TEngine;
using UnityEditor;
using UnityEngine.UIElements;

namespace RedDot.Editor.Views
{
    public class RedDotEnumSettingItem : VisualElement
    {
        private RedDotEnumDict _enumDict;
        private RedDotGraphData _graphData;
        private int _id;

        private VisualElement _otherContainer;
        private TextField _keyTextField;
        private Label _valueLabel;
        private TextField _descTextField;
        private Label _keyLabel;

        /// <summary>graphData 可空:传入时删除按钮前置引用预检(弹窗拒删被引用条目),数据层 Remove 另有兜底守卫。</summary>
        public RedDotEnumSettingItem(RedDotEnumDict enumDict, RedDotGraphData graphData = null)
        {
            _enumDict = enumDict;
            _graphData = graphData;
            var visualTree =
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                    PathUtil.GetEditorFilePath("Resources/Styles/SettingItem.uxml"));
            VisualElement container = visualTree.Instantiate();
            Add(container);
            var deleteBtn = container.Q<Button>("deleteBtn");
            deleteBtn.RegisterCallback<MouseUpEvent>((evt) =>
            {
                TryRemove();
            });

            _otherContainer = container.Q<VisualElement>("other");
            //K批5:字段标签按字典类型切换——func 字典的"key"字符串是展示别名(导出/运行时零消费),
            //沿用"Key"文案会误导为参与导出;key 字典的 key 才是导出身份
            _keyLabel = container.Q<Label>("key");
            var isFunc = graphData != null && enumDict == graphData.externalIdEnumDict;
            if (isFunc)
            {
                _keyLabel.text = "名称";
                _keyLabel.tooltip = "仅展示用,不参与导出与运行时;运行时身份是本条目 ID(externalId),业务经 RedDotKeys.XXX 引用";
            }
            else
            {
                _keyLabel.text = "Key";
                _keyLabel.tooltip = "导出身份,业务经 RedDotKeys.XXX 常量引用";
            }
            _keyTextField = container.Q<TextField>("keyTextField");
            _keyTextField.RegisterValueChangedCallback(evt =>
            {
                _enumDict.ChangeKey(_id, _keyTextField.value);
            });
            _valueLabel = container.Q<Label>("value");
            _descTextField = container.Q<TextField>("descTextField");
            _descTextField.RegisterValueChangedCallback(evt =>
            {
                _enumDict.ChangeDesc(_id, _descTextField.value);
            });
        }

        /// <summary>删除预检(K批 2026-09-17):仍被节点引用的条目弹窗拒删,列明引用节点便于改配。</summary>
        private void TryRemove()
        {
            if (_graphData != null)
            {
                var refs = _graphData.GetDictEntryReferences(_graphData.externalIdEnumDict == _enumDict, _id);
                if (refs.Count > 0)
                {
                    EditorUtility.DisplayDialog("删除被拒",
                        Utility.Text.Format("该条目仍被 {0} 个节点引用(contextId:{1}),先在这些节点上改配 key/逻辑ID 再删。",
                            refs.Count, string.Join(",", refs)), "好");
                    return;
                }
            }
            _enumDict.Remove(_id);
        }

        public void SetId(int id)
        {
            _id = id;
            _otherContainer.visible = true;
            _valueLabel.text = "ID: " + id;
            _keyTextField.value = _enumDict.GetKey(_id);
            _descTextField.value = _enumDict.GetDesc(_id);
        }
    }
}
