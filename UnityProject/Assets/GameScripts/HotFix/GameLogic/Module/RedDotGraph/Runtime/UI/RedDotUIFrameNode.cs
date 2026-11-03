using System;
using System.Collections;
using System.Collections.Generic;
using TEngine;

namespace RedDot.Runtime.RedDot
{
    /// <summary>
    /// 红点UI节点
    /// </summary>
    public class RedDotUIInfo
    {
        private readonly Action _cb;
        public uint LastChangeTime = 0;
        public string Key { private set; get; }
        public uint Id { private set; get; }

        public RedDotUIInfo(string key, uint id, Action cb)
        {
            _cb = cb;
            Key = key;
            Id = id;
        }

        public void DoCallBack()
        {
            //P2-6:单点隔离——回调抛异常不外泄、不中断同批其余回调(DataChanged 循环依赖本保护)
            try
            {
                _cb.Invoke();
            }
            catch (Exception e)
            {
                Log.Error("红点UI回调抛出异常，key:{0}, id:{1}, ex:{2}", Key, Id, e);
            }
        }

        public bool IsSame(uint id, Action cb)
        {
            return Id == id && _cb == cb;
        }

    }
    /// <summary>
    /// 帧节点，一个全屏面板对应一个帧，刷新红点的时候，只刷新当前帧的红点即可
    /// </summary>
    public class FrameNode : IEnumerable
    {
        private readonly Dictionary<string, List<RedDotUIInfo>> _redDotUIInfosDict = new Dictionary<string, List<RedDotUIInfo>>();

        /// <summary>
        /// 注册回调。返回新建的 UIInfo(供调用方同步 LastChangeTime 等);重复注册返回 null。
        /// </summary>
        public RedDotUIInfo TryAddRedDotUIInfo(string redDotKey, Action cb, uint id = 0)
        {
            if (!_redDotUIInfosDict.TryGetValue(redDotKey, out var infos))
            {
                infos = new List<RedDotUIInfo>();
                _redDotUIInfosDict[redDotKey] = infos;
            }

            foreach (var info in infos)
            {
                if (info.IsSame(id, cb))
                {
                    Log.Warning("add same UI, redDotKey:{0}, id:{1}", redDotKey, id);
                    return null;
                }
            }
            var newInfo = new RedDotUIInfo(redDotKey, id, cb);
            infos.Add(newInfo);
            return newInfo;
        }

        public RedDotUIInfo RemoveRedDotUIInfo(string redDotKey, Action cb, uint id = 0)
        {
            if (!_redDotUIInfosDict.TryGetValue(redDotKey, out var infos))
            {
                return null;
            }

            for (int i = infos.Count - 1; i >= 0; i--)
            {
                var info = infos[i];
                if (info.IsSame(id, cb))
                {
                    infos.RemoveAt(i);
                    return info;
                }
            }

            return null;
        }

        public void Dispose()
        {
            //P2-3:清空 UI 信息,释放各 RedDotUIInfo 持有的回调委托引用,防帧销毁后闭包悬挂
            foreach (var kv in _redDotUIInfosDict)
            {
                kv.Value.Clear();
            }
            _redDotUIInfosDict.Clear();
        }

        public IEnumerator GetEnumerator()
        {
            foreach (var infos in _redDotUIInfosDict)
            {
                foreach (var info in infos.Value)
                {
                    yield return info;
                }
            }
        }

        /// <summary>
        /// R3(审查 2026-09-12):巡检用一次性快照。回调内 RemoveUI/AddUI 改绑定会使 GetEnumerator
        /// 的惰性枚举失效(List/字典版本检查抛 InvalidOperationException),全量巡检须先物化再遍历,
        /// 与 DataChanged 派发的 per-key 快照同口径。巡检低频,分配可接受。
        /// </summary>
        public List<RedDotUIInfo> SnapshotInfos()
        {
            var result = new List<RedDotUIInfo>();
            foreach (var infos in _redDotUIInfosDict)
            {
                result.AddRange(infos.Value);
            }
            return result;
        }

        /// <summary>
        /// per-key 回调快照小池（A6-3：消除每次派发每 key 的 ToArray 分配）。
        /// 池只复用已归还实例；回调内重入 DataChanged 租的是另一实例，天然重入安全；上限 4 防极端递归膨胀。
        /// </summary>
        private readonly Stack<List<RedDotUIInfo>> _snapshotPool = new Stack<List<RedDotUIInfo>>();

        public void DataChanged(List<string> keys, uint id)
        {
            foreach (var key in keys)
            {
                if (_redDotUIInfosDict.TryGetValue(key, out var infos))
                {
                    //P6-2(审核登记):回调内可能 RemoveUI 修改同一列表,快照后派发防迭代中修改
                    var snapshot = _snapshotPool.Count > 0 ? _snapshotPool.Pop() : new List<RedDotUIInfo>();
                    snapshot.AddRange(infos);
                    try
                    {
                        foreach (var info in snapshot)
                        {
                            if (info.Id == id)
                            {
                                info.DoCallBack();
                            }
                        }
                    }
                    finally
                    {
                        snapshot.Clear();
                        if (_snapshotPool.Count < 4)
                        {
                            _snapshotPool.Push(snapshot);
                        }
                    }
                }
            }
        }
    }
}