using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GameLogic.RedDot.Editor.Data
{
    /// <summary>
    /// 枚举条目信息（id + key + 描述）。
    /// </summary>
    [Serializable]
    public class RedDotEnumInfo
    {
        public int id;
        public string key;
        public string desc;
    }

    /// <summary>
    /// 红点 Key / 逻辑 ID 的字典管理（编辑器侧）。
    /// 支持增删改、序列化、代码生成导出。
    /// </summary>
    [Serializable]
    public class RedDotEnumDict
    {
        private Dictionary<int, string> _enumDict;
        private Dictionary<int, string> _descDict;
        public List<RedDotEnumInfo> enumInfos = new List<RedDotEnumInfo>();

        /// <summary>代码生成模板（含 [[key&values]] 占位符）。</summary>
        public string classTemplate =
            "namespace RedDotDemo\n{\n\tpublic enum RedDotKeyEnum\n\t{\n[[key&values]]\n\t}\n}";

        /// <summary>每条 Key=Value 生成规则（含 [[key]]/[[value]]/[[desc]] 占位符）。</summary>
        public string keyValueReg = "\t\t[[key]] = [[value]],//[[desc]]";

        public string exportFileName = "RedDotKeyEnum.cs";

        private int _maxId = 0;
        [NonSerialized] private RedDotGraphData _graphData;
        [NonSerialized] private bool _isChanged = false;
        public bool IsChanged => _isChanged;
        private bool _isCached = true;
        public bool IsCached => _isCached;

        public void ClearChangedTag() => _isChanged = false;

        public List<int> GetIds() => _enumDict.Keys.ToList();

        public List<string> GetNodeKeys()
        {
            var keys = new List<string> { "null" };
            foreach (var info in _enumDict)
            {
                keys.Add($"{info.Key}:{info.Value}");
            }
            return keys;
        }

        public string GetNodeKey(int id)
        {
            if (_enumDict.TryGetValue(id, out string key))
            {
                return $"{id}:{key}";
            }
            return "null";
        }

        public int GetIdByNodeKey(string key)
        {
            if (key == "null") return 0;
            return Convert.ToInt32(key.Split(':')[0]);
        }

        public void PrepareExport()
        {
            enumInfos.Clear();
            _isCached = true;
            if (_enumDict == null) _enumDict = new Dictionary<int, string>();
            if (_descDict == null) _descDict = new Dictionary<int, string>();
            foreach (var info in _enumDict)
            {
                enumInfos.Add(new RedDotEnumInfo { desc = GetDesc(info.Key), id = info.Key, key = info.Value });
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
                _enumDict[info.id] = info.key;
                _descDict[info.id] = info.desc;
                if (info.id > _maxId) _maxId = info.id;
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

        public void Remove(int id)
        {
            _enumDict.Remove(id);
            _descDict.Remove(id);
            _isChanged = true;
            _isCached = false;
            _graphData.Dirty();
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

        public string GetKey(int id) => _enumDict.TryGetValue(id, out string key) ? key : "null";

        public void ChangeDesc(int id, string info)
        {
            if (_descDict.ContainsKey(id))
            {
                _descDict[id] = info;
                _graphData.Dirty();
                _isCached = false;
            }
        }

        public string GetDesc(int id) => _descDict.TryGetValue(id, out string key) ? key : "null";

        public void ChangeKeyAndValueReg(string newReg)
        {
            keyValueReg = newReg;
            _isChanged = true;
            _graphData.Dirty();
        }

        public void ChangeClassTemplate(string newTemplate)
        {
            classTemplate = newTemplate;
            _isChanged = true;
            _graphData.Dirty();
        }

        public void ChangeExportFileName(string fileName)
        {
            exportFileName = fileName;
            _isChanged = true;
            _graphData.Dirty();
        }

        public void ExportTo(string filePath, System.Func<string, string, bool> writer)
        {
            var keyAndValuesStr = "";
            var endingSymble = "";
            foreach (var info in _enumDict)
            {
                var id = info.Key;
                var desc = GetDesc(id);
                keyAndValuesStr += endingSymble + keyValueReg
                    .Replace("[[key]]", info.Value)
                    .Replace("[[value]]", id.ToString())
                    .Replace("[[desc]]", desc);
                endingSymble = "\n";
            }
            var code = classTemplate.Replace("[[key&values]]", keyAndValuesStr);
            writer?.Invoke(filePath, code);
        }
    }
}
