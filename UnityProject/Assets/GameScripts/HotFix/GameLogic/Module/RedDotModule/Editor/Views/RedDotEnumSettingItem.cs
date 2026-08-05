using GameLogic.RedDot.Editor.Data;
using UnityEngine.UIElements;

namespace GameLogic.RedDot.Editor.Views
{
    /// <summary>
    /// Key/逻辑ID 设置列表中的单条目（key 文本框 + ID 标签 + 描述 + 删除按钮）。
    /// 纯代码构建（无 UXML 依赖）。
    /// </summary>
    public class RedDotEnumSettingItem : VisualElement
    {
        private readonly RedDotEnumDict _enumDict;
        private int _id;

        private readonly Label _valueLabel;
        private readonly TextField _keyTextField;
        private readonly TextField _descTextField;

        public RedDotEnumSettingItem(RedDotEnumDict enumDict)
        {
            _enumDict = enumDict;
            style.flexDirection = FlexDirection.Row;
            style.marginBottom = 2;

            _valueLabel = new Label { style = { width = 60, marginLeft = 4 } };
            Add(_valueLabel);

            _keyTextField = new TextField { label = "key" };
            _keyTextField.style.width = 140;
            _keyTextField.style.marginLeft = 4;
            _keyTextField.RegisterValueChangedCallback(evt => _enumDict.ChangeKey(_id, _keyTextField.value));
            Add(_keyTextField);

            _descTextField = new TextField { label = "desc" };
            _descTextField.style.width = 180;
            _descTextField.style.marginLeft = 4;
            _descTextField.RegisterValueChangedCallback(evt => _enumDict.ChangeDesc(_id, _descTextField.value));
            Add(_descTextField);

            var deleteBtn = new Button(() => _enumDict.Remove(_id)) { text = "删除" };
            deleteBtn.style.marginLeft = 4;
            Add(deleteBtn);
        }

        public void SetId(int id)
        {
            _id = id;
            _valueLabel.text = $"ID: {id}";
            _keyTextField.SetValueWithoutNotify(_enumDict.GetKey(_id));
            _descTextField.SetValueWithoutNotify(_enumDict.GetDesc(_id));
        }
    }
}
