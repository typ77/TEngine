using System;
using System.Collections.Generic;
using RedDot.Editor.Data;
using RedDot.Editor.Util;
using RedDot.Editor.Views;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RedDot.Editor
{
    public class RedDotEnumDictWindow : EditorWindow
    {
        private RedDotEnumDict _enumDict;
        private RedDotGraphData _graphData;
        private bool _isFuncDict;
        private List<int> _ids;
        private ListView _listView;

        //K批3(2026-09-17 用户反馈):下方分组折叠区多余且不美观,整块删除——组归属降为列表一列 + 筛选下拉。
        //筛选目标编码:-2=全部 -1=未分组(无节点引用) 0=主图 >0=组id;选择存窗口字段,
        //RepaintGraph 每次重初始化本窗口时选项重建但选中项保持(按 target 编码对齐,不随组增删漂移)。
        private readonly List<int> _filterTargets = new List<int>();
        private readonly List<string> _filterNames = new List<string>();
        private int _filterIndex;

        //2026-09-22:顶部搜索框——与「按组筛选」同道理的文本筛选,按 条目ID/key字符串/描述 子串过滤列表
        private string _searchText = string.Empty;

        public void Initialize(RedDotEnumDict enumDict, string titleStr, RedDotGraphData graphData = null)
        {
            _enumDict = enumDict;
            _graphData = graphData;
            _isFuncDict = graphData != null && enumDict == graphData.externalIdEnumDict;
            rootVisualElement.Clear();
            titleContent = new GUIContent(titleStr);
            BuildFilterOptions();
            rootVisualElement.Add(BuildToolbar());
            InitListView();
            RefreshData();
        }

        /// <summary>筛选选项:全部/未分组/主图/各具体组。组增删后重初始化时按 target 对齐,越界回落"全部"。</summary>
        private void BuildFilterOptions()
        {
            _filterTargets.Clear();
            _filterNames.Clear();
            _filterTargets.Add(-2);
            _filterNames.Add("全部");
            _filterTargets.Add(-1);
            _filterNames.Add("未分组");
            _filterTargets.Add(0);
            _filterNames.Add("主图");
            if (_graphData != null)
            {
                foreach (var g in _graphData.groups)
                {
                    _filterTargets.Add(g.id);
                    _filterNames.Add(g.name);
                }
            }
            if (_filterIndex >= _filterNames.Count || (_filterIndex > 0 && _graphData == null))
            {
                _filterIndex = 0;
            }
        }

        private IMGUIContainer BuildToolbar()
        {
            return new IMGUIContainer(() =>
            {
                GUILayout.BeginHorizontal(EditorStyles.toolbar);
                if (GUILayout.Button("增加Key & Value条目", EditorStyles.toolbarButton))
                {
                    _enumDict.Add();
                }
                GUILayout.Space(12);
                GUILayout.Label("按组筛选:", EditorStyles.miniLabel);
                var sel = EditorGUILayout.Popup(_filterIndex, _filterNames.ToArray(), EditorStyles.toolbarPopup, GUILayout.Width(200));
                if (sel != _filterIndex)
                {
                    _filterIndex = sel;
                    RefreshData();
                }
                GUILayout.Space(12);
                GUILayout.Label("搜索:", EditorStyles.miniLabel);
                var searchText = GUILayout.TextField(_searchText, EditorStyles.toolbarSearchField, GUILayout.Width(240));
                if (searchText != _searchText)
                {
                    _searchText = searchText;
                    RefreshData();
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            });
        }

        /// <summary>行组归属文案:引用该条目的节点所在组集合(去重)。key 正常态 1 key=1 节点=1 组,
        /// 多组仅出现在 K6 报错态(顺带可视化问题);funcId 合法共用,多组常态显示。</summary>
        private string BuildGroupText(int id)
        {
            if (_graphData == null)
            {
                return "";
            }
            var gids = _graphData.GetDictEntryGroups(_isFuncDict, id);
            if (gids.Count == 0)
            {
                return "未分组";
            }
            var names = new List<string>();
            foreach (var gid in gids)
            {
                if (gid == 0)
                {
                    names.Add("主图");
                }
                else
                {
                    var g = _graphData.GetGroup(gid);
                    names.Add(g != null ? g.name : "<组" + gid + ">");
                }
            }
            return string.Join("、", names.ToArray());
        }

        private bool MatchFilter(int id)
        {
            var target = _filterTargets[_filterIndex];
            if (target == -2 || _graphData == null)
            {
                return true;
            }
            var gids = _graphData.GetDictEntryGroups(_isFuncDict, id);
            if (target == -1)
            {
                return gids.Count == 0;
            }
            return gids.Contains(target);
        }

        public void RefreshData()
        {
            if (_enumDict == null) return;
            _ids = _enumDict.GetIds();
            if (_filterTargets.Count > 0 && _filterIndex > 0)
            {
                var filtered = new List<int>();
                foreach (var id in _ids)
                {
                    if (MatchFilter(id))
                    {
                        filtered.Add(id);
                    }
                }
                _ids = filtered;
            }
            //文本筛选:匹配文本 = 条目ID + key字符串(GetNodeKey 产出 "id:key" 形态) + 描述,子串不区分大小写
            if (!string.IsNullOrEmpty(_searchText))
            {
                var searched = new List<int>();
                foreach (var id in _ids)
                {
                    if ((_enumDict.GetNodeKey(id) + " " + _enumDict.GetDesc(id)).IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        searched.Add(id);
                    }
                }
                _ids = searched;
            }
            _listView.itemsSource = _ids;
            if (_ids.Count > 0)
            {
                //搜索态落首个结果;无搜索保持原行为滚到底(承接新增条目在表尾)
                _listView.ScrollToItem(!string.IsNullOrEmpty(_searchText) ? 0 : _ids.Count - 1);
            }
        }

        private void InitListView()
        {
            Func<VisualElement> makeItem = () => new EnumListRow(_enumDict, _graphData);
            Action<VisualElement, int> bindItem = (e, i) =>
            {
                var row = (EnumListRow)e;
                var id = _ids[i];
                row.Item.SetId(id);
                row.GroupLabel.text = BuildGroupText(id);
            };

            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(PathUtil.GetEditorFilePath("Resources/Styles/ListView.uxml"));
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(PathUtil.GetEditorFilePath("Resources/Styles/ListView.uss"));
            VisualElement container = visualTree.Instantiate();
            rootVisualElement.Add(container);
            container.styleSheets.Add(styleSheet);
            //容器随窗口拉伸(配合 ListView.uss 的 flex-grow:1,替换原固定500px列表高,消除窗口下方死空白)
            container.style.flexGrow = 1;
            _listView = container.Q<ListView>();
            _listView.makeItem = makeItem;
            _listView.bindItem = bindItem;
            _listView.selectionType = SelectionType.None;
            _listView.showBorder = true;
        }

        /// <summary>列表行 = 组归属列(灰斜体) + 可编辑条目(改 key/desc/删除)。编辑能力只此一处。</summary>
        private class EnumListRow : VisualElement
        {
            public readonly Label GroupLabel = new Label();
            public readonly RedDotEnumSettingItem Item;

            public EnumListRow(RedDotEnumDict dict, RedDotGraphData graphData)
            {
                style.flexDirection = FlexDirection.Row;
                style.alignItems = Align.Center;
                style.borderBottomWidth = 0.5f;
                style.borderBottomColor = new Color(0.16f, 0.16f, 0.16f);
                GroupLabel.style.width = 150;
                GroupLabel.style.marginLeft = 4;
                GroupLabel.style.marginRight = 4;
                GroupLabel.style.color = new Color(0.62f, 0.62f, 0.62f);
                GroupLabel.style.whiteSpace = WhiteSpace.Normal;
                GroupLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
                GroupLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                Add(GroupLabel);
                Item = new RedDotEnumSettingItem(dict, graphData);
                Item.style.flexGrow = 1;
                Add(Item);
            }
        }

        private void Update()
        {
            //K批6:未 Initialize(字典未注入)时 Update 也会 tick,判空防 NRE
            if (_enumDict != null && _enumDict.IsChanged)
            {
                _enumDict.ClearChangedTag();
                RefreshData();
            }
        }
    }
}
