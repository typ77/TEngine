using System.Collections.Generic;
using GameLogic.RedDot.Editor.Views;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace GameLogic.RedDot.Editor
{
    /// <summary>
    /// GraphView 右键创建节点的搜索窗口提供器。
    /// </summary>
    public class RedDotSearchWindowProvider : ScriptableObject, ISearchWindowProvider
    {
        public delegate bool OnSelectEntryDelegate(SearchTreeEntry searchTreeEntry, SearchWindowContext context);

        public OnSelectEntryDelegate OnSelectEntryHandler;

        public bool OnSelectEntry(SearchTreeEntry searchTreeEntry, SearchWindowContext context)
        {
            return OnSelectEntryHandler?.Invoke(searchTreeEntry, context) ?? false;
        }

        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            var entries = new List<SearchTreeEntry>
            {
                new SearchTreeGroupEntry(new GUIContent("Create RedDot Node")),
                new SearchTreeEntry(new GUIContent("RedDot Node")) { level = 1, userData = typeof(RedDotNodeView) }
            };
            return entries;
        }
    }
}
