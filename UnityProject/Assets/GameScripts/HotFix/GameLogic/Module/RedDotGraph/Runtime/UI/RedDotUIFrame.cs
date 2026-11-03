using System;
using System.Collections.Generic;
using TEngine;

namespace RedDot.Runtime.RedDot
{
    /// <summary>
    /// 1、RedDotUIMaps是基于帧+脏标记的方法实现的，不支持没有显示（或者已经隐藏的，不在当前帧下）的页面添加红点，
    /// 因为添加红点的话，一定是在当前最上层帧里添加的。
    /// 只要帧一还原，之前的帧数据会全部删除。
    /// 2、如果想要解决这个问题，可以吧脏标记功能禁用，is_use_dirty_state = false即可，禁用后，每次删除UI，都会修改cbDict，所以看项目需求
    ///
    /// </summary>
    public class RedDotUIFrames
    {
        private Dictionary<string, FrameNode> _frameNodeDict = new Dictionary<string, FrameNode>();
        private FrameNode _curFrameNode;

        /// <summary>
        /// 注册回调到当前帧。返回新建的 UIInfo(供回放同步 LastChangeTime);重复注册/无帧返回 null。
        /// </summary>
        public RedDotUIInfo AddUI(string redDotKey, Action cb, uint id = 0)
        {
            var frameNode = _curFrameNode;
            if (frameNode == null)
            {
                Log.Error("AddUI 时当前帧为空，请先 AddFrame。redDotKey:{0}", redDotKey);
                return null;
            }
            return frameNode.TryAddRedDotUIInfo(redDotKey, cb, id);
        }

        /// <summary>
        /// 某个UI删除了
        /// </summary>
        /// <param name="redDotKey"></param>
        /// <param name="cb"></param>
        /// <param name="id"></param>
        public void RemoveUI(string redDotKey, Action cb, uint id = 0)
        {
            var frameNode = _curFrameNode;
            if (frameNode == null)
            {
                Log.Error("RemoveUI 时当前帧为空，请先 AddFrame。redDotKey:{0}", redDotKey);
                return;
            }
            frameNode.RemoveRedDotUIInfo(redDotKey, cb, id);
        }

        public void AddFrame(string name)
        {
            if (!_frameNodeDict.ContainsKey(name))
            {
                var node = new FrameNode();
                _curFrameNode = node;
                _frameNodeDict[name] = node;
            }
            else
            {
                //P2-1:重名 AddFrame 语义=切回该帧(窗口重开序列),否则重开后当前帧停在旧帧上
                Log.Warning("已经存在了此frame:{0}，切换当前帧到它", name);
                _curFrameNode = _frameNodeDict[name];
            }
        }

        public void RemoveFrame(string name)
        {
            if (_frameNodeDict.TryGetValue(name, out FrameNode node))
            {
                _frameNodeDict.Remove(name);
                node.Dispose();
                if (ReferenceEquals(_curFrameNode, node))
                {
                    //P2-2:移除的若是当前帧,回退到字典内任意存活帧(可能为 null,由 AddUI/刷新守卫兜底)
                    _curFrameNode = null;
                    foreach (var kv in _frameNodeDict)
                    {
                        _curFrameNode = kv.Value;
                        break;
                    }
                }
            }
            else
            {
                Log.Warning("不存在此frame:{0}", name);
            }
        }

        public void ChangeToFrame(string name)
        {
            if (_frameNodeDict.TryGetValue(name, out FrameNode node))
            {
                _curFrameNode = node;
            }
            else
            {
                Log.Warning("不存在此frame:{0}", name);
            }
        }

        public FrameNode GetCurrentFrameNode()
        {
            return _curFrameNode;
        }

        /// <summary>
        /// P4-1(A6):清空全部帧(逐帧 Dispose 释放委托引用),供 Reset 重建
        /// </summary>
        public void Clear()
        {
            foreach (var kv in _frameNodeDict)
            {
                kv.Value.Dispose();
            }
            _frameNodeDict.Clear();
            _curFrameNode = null;
        }
    }
}