using System;
using System.Collections.Generic;
using System.IO;
using GameLogic.RedDot.Editor.Data;
using GameLogic.RedDot.Editor.Util;
using GameLogic.RedDot.Editor.Views;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameLogic.RedDot.Editor
{
    /// <summary>
    /// Key/逻辑ID 字典管理窗口。支持增删条目、编辑模板、生成枚举代码。
    /// </summary>
    public class RedDotEnumDictWindow : EditorWindow
    {
        private RedDotEnumDict _enumDict;
        private List<int> _ids;
        private ListView _listView;

        public void Initialize(RedDotEnumDict enumDict, string titleStr)
        {
            _enumDict = enumDict;
            rootVisualElement.Clear();
            titleContent = new GUIContent(titleStr);

            var toolbar = new IMGUIContainer(() =>
            {
                GUILayout.BeginHorizontal(EditorStyles.toolbar);
                if (GUILayout.Button("增加Key & Value条目", EditorStyles.toolbarButton))
                {
                    enumDict.Add();
                }
                GUILayout.Space(6);
                if (GUILayout.Button("生成代码", EditorStyles.toolbarButton))
                {
                    ExportTo();
                }
                GUILayout.EndHorizontal();
            });
            rootVisualElement.Add(toolbar);
            InitListView();

            var exportFileNameField = new TextField("生成文件名") { value = _enumDict.exportFileName };
            exportFileNameField.RegisterValueChangedCallback(evt => _enumDict.ChangeExportFileName(exportFileNameField.value));
            rootVisualElement.Add(exportFileNameField);

            var keyValueRegField = new TextField("Key & Value生成规则") { multiline = true, value = _enumDict.keyValueReg };
            keyValueRegField.RegisterValueChangedCallback(evt => _enumDict.ChangeKeyAndValueReg(keyValueRegField.value));
            rootVisualElement.Add(keyValueRegField);

            var templateField = new TextField("代码模板") { multiline = true, value = _enumDict.classTemplate };
            templateField.RegisterValueChangedCallback(evt => _enumDict.ChangeClassTemplate(templateField.value));
            rootVisualElement.Add(templateField);

            RefreshData();
        }

        public void RefreshData()
        {
            if (_enumDict == null) return;
            _ids = _enumDict.GetIds();
            _listView.itemsSource = _ids;
            _listView.ScrollToItem(_ids.Count - 1);
        }

        private void InitListView()
        {
            Func<VisualElement> makeItem = () => new RedDotEnumSettingItem(_enumDict);
            Action<VisualElement, int> bindItem = (e, i) => (e as RedDotEnumSettingItem)?.SetId(_ids[i]);

            _listView = new ListView { makeItem = makeItem, bindItem = bindItem, selectionType = SelectionType.None, showBorder = true };
            _listView.style.height = 200;
            rootVisualElement.Add(_listView);
        }

        private void Update()
        {
            if (_enumDict != null && _enumDict.IsChanged)
            {
                _enumDict.ClearChangedTag();
                RefreshData();
            }
        }

        public void ExportTo()
        {
            if (_enumDict == null) return;
            var extension = Path.GetExtension(_enumDict.exportFileName).Replace(".", "");
            var newFilePath = EditorUtility.SaveFilePanelInProject(
                "Export Red Dot Enum",
                Path.GetFileNameWithoutExtension(_enumDict.exportFileName),
                string.IsNullOrEmpty(extension) ? "cs" : extension,
                "", Application.dataPath);
            if (!string.IsNullOrEmpty(newFilePath))
            {
                _enumDict.ExportTo(newFilePath, Util.FileUtilities.WriteToDisk);
                AssetDatabase.Refresh();
            }
        }
    }
}
