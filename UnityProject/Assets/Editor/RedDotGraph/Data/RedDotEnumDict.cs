using System;
using System.Collections.Generic;
using System.Linq;
using TEngine;

namespace RedDot.Editor.Data
{
    [Serializable]
    public class RedDotEnumInfo
    {
        public int id;
        public string key;
        public string desc;
    }
    [Serializable]
    public class RedDotEnumDict
    {
        private Dictionary<int, string> _enumDict;
        private Dictionary<int, string> _descDict;
        public List<RedDotEnumInfo> enumInfos = new List<RedDotEnumInfo>();

        //K批(2026-09-17):移除 RedDotKeyEnum.cs 枚举导出死工作流(keyValueReg/exportFileName/ExportTo/
        //模板属性)——产物 GameLogic.RedDotKeyEnum 全库零消费者,业务消费的是导出链生成的 RedDotKeys 常量表。
        //序列化字段直接删除安全:JsonUtility 加载忽略未知 JSON 键。
        private int _maxId = 0;
        [NonSerialized]
        private RedDotGraphData _graphData;
        private bool _isChanged = false;
        public bool IsChanged => _isChanged;
        private bool _isCached = true;
        public bool IsCached => _isCached;
        public void ClearChangedTag()
        {
            _isChanged = false;
        }

        public List<int> GetIds()
        {
            //K批(2026-09-17):按 id 升序——字典序不定,205 条目下列表/搜索顺序稳定可扫
            return _enumDict.Keys.OrderBy(id => id).ToList();
        }

        public string GetNodeKey(int id)
        {
            //K批(2026-09-17):-1 纯聚合与未设置(0/悬空)此前都显示 "null" 无法区分;func 字典无 -1 条目,
            //聚合态在搜索窗口选不到——显示语义化文案后,-1 也作为固定项进搜索窗口(provider 侧)
            if (id == -1)
            {
                return "-1:纯聚合(无checker)";
            }
            string key;
            if (_enumDict.TryGetValue(id, out key))
            {
                key = id + ":" + key;
            }
            else
            {
                key = id + ":未设置";
            }

            return key;
        }

        public int GetIdByNodeKey(string key)
        {
            //GetNodeKey 产出形如 "id:key"(含 "-1:纯聚合(无checker)"),取首段即 id;"null" 字面量已随
            //GetNodeKey 旧文案废止(K批6 死分支清理)
            return Convert.ToInt32(key.Split(':')[0]);
        }

        public void PrepareExport()
        {
            enumInfos.Clear();
            _isCached = true;
            if (_enumDict == null)
            {
                _enumDict = new Dictionary<int, string>();
            }
            if (_descDict == null)
            {
                _descDict = new Dictionary<int, string>();
            }
            foreach (var info in _enumDict)
            {
                enumInfos.Add(new RedDotEnumInfo(){desc = GetDesc(info.Key), id = info.Key, key = info.Value});
            }
        }

        public void ValidateData(RedDotGraphData graphData)
        {
            _graphData = graphData;
            _enumDict = new Dictionary<int, string>();
            _descDict = new Dictionary<int, string>();
            _maxId = 0;
            foreach (var info in enumInfos)
            {
                var id = info.id;
                _enumDict[id] = info.key;
                _descDict[id] = info.desc;
                if (id > _maxId)
                {
                    _maxId = id;
                }
            }
        }

        public int Add()
        {
            var id = ++_maxId;
            _enumDict[id] = "Default";
            _descDict[id] = "加一点描述吧";
            _graphData.Dirty();
            _isChanged = true;
            _isCached = false;
            return id;
        }

        /// <summary>删除条目。K批(2026-09-17)前置引用守卫:仍被节点引用的条目拒绝删除(防悬空 keyId/funcId),
        /// 拒绝时 Log.Error 指明引用节点;返回 false=被拒。UI 侧(RedDotEnumSettingItem)另有弹窗预检。</summary>
        public bool Remove(int id)
        {
            if (_graphData != null)
            {
                var refs = _graphData.GetDictEntryReferences(_graphData.externalIdEnumDict == this, id);
                if (refs.Count > 0)
                {
                    Log.Error("删除字典条目 {0} 被拒:仍被 {1} 个节点引用(contextId:{2}),先改配这些节点再删",
                        id, refs.Count, string.Join(",", refs));
                    return false;
                }
            }
            _enumDict.Remove(id);
            _descDict.Remove(id);
            _isChanged = true;
            _isCached = false;
            _graphData.Dirty();
            return true;
        }

        /// <summary>key 字符串是否已存在(K批:新建 key 唯一性校验;字典按 int id 存储须按值扫描)。</summary>
        public bool ContainsKeyStr(string key)
        {
            foreach (var kv in _enumDict)
            {
                if (kv.Value == key)
                {
                    return true;
                }
            }
            return false;
        }

        public void ChangeKey(int id, string info)
        {
            if (_enumDict.ContainsKey(id))
            {
                _enumDict[id] = info;
                _graphData.Dirty();
                _isCached = false;
            }
        }

        public string GetKey(int id)
        {
            //P7(E16):miss 返回空串并由调用侧报错——"null" 字面量会作为合法 key 写进导出 JSON
            if (!_enumDict.TryGetValue(id, out var key))
            {
                key = string.Empty;
            }

            return key;
        }

        public void ChangeDesc(int id, string info)
        {
            if (_descDict.ContainsKey(id))
            {
                _descDict[id] = info;
                _graphData.Dirty();
                _isCached = false;
            }
        }

        public string GetDesc(int id)
        {
            //K批6:miss 返回空串(原 "null" 字面量会漏进搜索列表文案/迁移工具 desc 键)
            if (!_descDict.TryGetValue(id, out var desc))
            {
                desc = string.Empty;
            }

            return desc;
        }
    }
}