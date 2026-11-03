#if UNITY_EDITOR
using System.Collections.Generic;
using RedDot.Editor.Data;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace RedDot.Editor.Views
{
    /// <summary>
    /// key/逻辑ID 分组搜索选择器(D6):分组树(派生投影)+ 模糊搜索(SearchWindow 原生)。
    /// userData="id:key"字符串,选择侧经 GetIdByNodeKey 解析。
    /// K批(2026-09-17):①func 字典置顶固定项"-1:纯聚合(无checker)"——func 字典无 -1 条目,
    /// 此前聚合态在编辑器 UI 上无法设置(迁移脚本直写);②key 字典置顶"新建key…"内联创建并选中,
    /// 免跳出画布开 Key设置窗四步流程(创建走 RedDotKeyCreateWizard)。
    /// </summary>
    public class RedDotKeySearchWindowProvider : ScriptableObject, ISearchWindowProvider
    {
        public delegate void OnSelectDelegate(int id);
        public OnSelectDelegate OnSelect;
        private RedDotGraphData _data;
        private RedDotEnumDict _dict;
        private bool _isFuncDict;

        /// <summary>新建key条目的 userData 标记(前缀控制字符防与 "id:key" 形态冲突)。</summary>
        private const string CreateNewTag = "\u0001create-new-key";
        /// <summary>func 字典固定项 userData:"-1:纯聚合(无checker)" 经 GetIdByNodeKey 解析回 -1。</summary>
        private const string AggregationEntry = "-1:纯聚合(无checker)";

        public void Init(RedDotGraphData data, RedDotEnumDict dict, bool isFuncDict)
        {
            _data = data;
            _dict = dict;
            _isFuncDict = isFuncDict;
        }

        /// <summary>
        /// 自适应宽度上下文:SearchWindow 默认窗宽固定,长条目(新建key…/key+中文描述)会被截断;
        /// 实测 CreateSearchTree 最长行文本宽 + 每级缩进 + 边距,经 requestedWidth 请求窗宽
        /// (requestedHeight 传 0 走默认高度)。
        /// </summary>
        public SearchWindowContext CreateAdaptiveContext(Vector2 screenMousePosition)
        {
            var entries = CreateSearchTree(default(SearchWindowContext));
            float maxWidth = 0f;
            foreach (var entry in entries)
            {
                if (entry.content == null)
                {
                    continue;
                }
                float w = EditorStyles.label.CalcSize(entry.content).x + entry.level * 16f; // 16=SearchWindow 每级缩进
                if (w > maxWidth)
                {
                    maxWidth = w;
                }
            }
            // 44=左右内边距+组行右侧箭头+竖滚动条余量;320 下限兜底空列表,1000 防超宽爆窗
            float width = Mathf.Clamp(maxWidth + 44f, 320f, 1000f);
            return new SearchWindowContext(screenMousePosition, width, 0f);
        }

        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            var entries = new List<SearchTreeEntry>();
            entries.Add(new SearchTreeGroupEntry(new GUIContent(_isFuncDict ? "逻辑ID" : "红点key")));
            if (_isFuncDict)
            {
                entries.Add(new SearchTreeEntry(new GUIContent(AggregationEntry + "  —— 仅有子节点聚合的中间节点(不可注册checker)"))
                {
                    level = 1,
                    userData = AggregationEntry
                });
                //K批5:与 key 侧对齐——func 侧内联新建(id 自增,代理键无需人工挑数字)
                entries.Add(new SearchTreeEntry(new GUIContent("＋ 新建逻辑ID…(输入名称与描述,创建并选中)"))
                {
                    level = 1,
                    userData = CreateNewTag
                });
            }
            else
            {
                entries.Add(new SearchTreeEntry(new GUIContent("＋ 新建key…(输入名称与描述,创建并选中)"))
                {
                    level = 1,
                    userData = CreateNewTag
                });
            }
            foreach (var section in _data.DeriveKeyGroups(_dict, _isFuncDict))
            {
                entries.Add(new SearchTreeGroupEntry(new GUIContent(section.Name)) { level = 1 });
                foreach (var id in section.Ids)
                {
                    entries.Add(new SearchTreeEntry(new GUIContent(_dict.GetNodeKey(id) + "  " + _dict.GetDesc(id)))
                    {
                        level = 2,
                        userData = _dict.GetNodeKey(id)
                    });
                }
            }
            return entries;
        }

        public bool OnSelectEntry(SearchTreeEntry entry, SearchWindowContext context)
        {
            if (OnSelect == null || !(entry.userData is string))
            {
                return false;
            }
            var userData = (string)entry.userData;
            if (userData == CreateNewTag)
            {
                RedDotKeyCreateWizard.Open(_data, _dict, OnSelect, _isFuncDict);
                return true;
            }
            OnSelect(_dict.GetIdByNodeKey(userData));
            return true;
        }
    }
}
#endif
