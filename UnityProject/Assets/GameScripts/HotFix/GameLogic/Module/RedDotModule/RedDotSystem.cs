using System;
using System.Collections.Generic;
using GameLogic.RedDot.Core;
using UnityEngine;
using TEngine;

namespace GameLogic.RedDot
{
    /// <summary>
    /// 红点系统对门外壳。整合红点图、外部逻辑 visitor、节点门面与 MVE 推送。
    /// <para>继承 <see cref="Singleton{T}"/> 接入 TEngine 单例生命周期；
    /// 实现 <see cref="ILateUpdate"/> 在帧末统一刷新（与 BatchScheduler 协同）。</para>
    /// <para>典型用法：
    /// <code>
    /// RedDotSystem.Instance.LoadJsonText(jsonStr);
    /// RedDotSystem.Instance.RegLogic((int)RedDotLogic.MailUnread, (out var s, out var n) => { s = MailModule.Unread &gt; 0 ? Number : Null; n = MailModule.Unread; });
    /// RedDotNode node = RedDotSystem.Instance.GetNode("Mail");
    /// // UIWidget 内：Bind(node.State, s =&gt; RefreshView(s));
    /// RedDotSystem.Instance.MarkDirty("Mail");
    /// </code></para>
    /// </summary>
    public sealed class RedDotSystem : Singleton<RedDotSystem>, ILateUpdate
    {
        private readonly RedDotGraph _graph = new RedDotGraph();
        private readonly RedDotExternalLogicVisitor _visitor = new RedDotExternalLogicVisitor();
        private readonly Dictionary<string, RedDotNode> _nodes = new Dictionary<string, RedDotNode>();

        // 本帧需要刷新的 key 集合（幂等去重）
        private readonly HashSet<string> _dirtyKeys = new HashSet<string>();
        private readonly List<string> _dirtyKeyBuffer = new List<string>();

        // 带 id 的列表项红点订阅（id != 0），不走 BindableProperty，走 Action 回调
        private readonly Dictionary<(string key, uint id), Action<RedDotStatus, int>> _idCallbacks = new Dictionary<(string, uint), Action<RedDotStatus, int>>();

        // 动态叶子节点池（软删除后归还，新增时复用，避免 GC）
        private readonly Stack<RedDotVertex> _leafPool = new Stack<RedDotVertex>();
        // 动态叶子 → 其父 key 的映射（用于 ClearLeavesUnder 批量清理与 RemoveLeaf 断边）
        private readonly Dictionary<string, string> _dynamicLeafParents = new Dictionary<string, string>();

        // ─── 生命周期 ───

        protected override void OnInit()
        {
            // 图状态刷新完成后：推送到对应 RedDotNode 的 BindableProperty
            _graph.OnVertexRefreshed = OnVertexRefreshed;
        }

        /// <summary>
        /// 仅供单元测试：创建一个不注册到 SingletonSystem 的实例，规避 EditMode 下
        /// DontDestroyOnLoad 限制。运行时请使用 <see cref="Singleton{T}.Instance"/>。
        /// </summary>
        public static RedDotSystem CreateForTest()
        {
            var instance = new RedDotSystem();
            // 绕过 Singleton.Instance 的自动 Retain（避免触发 SingletonSystem.CheckInit），
            // 手动执行初始化逻辑
            instance.OnInit();
            return instance;
        }

        /// <summary>
        /// 清理所有运行时状态（保留图结构与已注册逻辑）。用于测试隔离。
        /// <para>注意：此方法不重建图结构，仅重置脏标记与回调订阅。
        /// 如需完全重置，请调用 <see cref="Release"/> 后重新访问 <see cref="Singleton{T}.Instance"/>。</para>
        /// </summary>
        internal void ResetRuntimeState()
        {
            _dirtyKeys.Clear();
            _dirtyKeyBuffer.Clear();
            _idCallbacks.Clear();
            // 注：不清理 _nodes 和图结构，避免破坏已加载配置；测试应通过全新配置隔离
        }

        protected override void OnRelease()
        {
            foreach (var node in _nodes.Values)
            {
                node.Dispose();
            }
            _nodes.Clear();
            _visitor.Clear();
            _dirtyKeys.Clear();
            _idCallbacks.Clear();
            _dynamicLeafParents.Clear();
            _leafPool.Clear();
            base.OnRelease();
        }

        // ─── 外部逻辑注册（叶子节点） ───

        /// <summary>注册不带 id 的叶子判断逻辑。</summary>
        public void RegLogic(int externalId, RedDotExternalLogic logic) => _visitor.RegExternalLogic(externalId, logic);

        /// <summary>注册带 id 的叶子判断逻辑（列表项红点）。</summary>
        public void RegLogic(int externalId, RedDotExternalLogicWithId logic) => _visitor.RegExternalLogic(externalId, logic);

        /// <summary>注销指定 externalId 的逻辑。</summary>
        public void UnRegLogic(int externalId) => _visitor.UnRegExternalLogic(externalId);

        // ─── 配置加载 ───

        /// <summary>
        /// 从 JSON 文本加载红点树结构。
        /// <para>JSON 结构见 <see cref="RedDotExportJson"/>（字段 vertexs/edges/outKeys）。</para>
        /// </summary>
        public void LoadJsonText(string jsonText, bool checkVisitor = true)
        {
            var cfg = JsonUtility.FromJson<RedDotExportJson>(jsonText);
            LoadExportJson(cfg, checkVisitor);
        }

        /// <summary>
        /// 从资源系统加载红点树 JSON 配置（同步）。
        /// <para>资源需是 TextAsset，location 为资源路径（如 "Assets/AssetRaw/Config/reddot"）。</para>
        /// </summary>
        public void LoadJsonAsset(string location, bool checkVisitor = true)
        {
            var asset = GameModule.Resource.LoadAsset<TextAsset>(location);
            if (asset == null)
            {
                Log.Error($"[RedDotSystem] 加载红点配置资源失败：{location}");
                return;
            }
            LoadJsonText(asset.text, checkVisitor);
        }

        /// <summary>
        /// 从资源系统异步加载红点树 JSON 配置。
        /// </summary>
        public async void LoadJsonAssetAsync(string location, bool checkVisitor = true)
        {
            var asset = await GameModule.Resource.LoadAssetAsync<TextAsset>(location);
            if (asset == null)
            {
                Log.Error($"[RedDotSystem] 异步加载红点配置资源失败：{location}");
                return;
            }
            LoadJsonText(asset.text, checkVisitor);
        }

        /// <summary>
        /// 从已解析的配置对象加载红点树。
        /// </summary>
        public void LoadExportJson(RedDotExportJson config, bool checkVisitor = true)
        {
            // 第一遍：创建顶点 + 节点门面
            foreach (var vertexJson in config.vertexs)
            {
                if (checkVisitor && vertexJson.externalId != -1 && !_visitor.CheckHasExternalId(vertexJson.externalId))
                {
                    Log.Error($"红点key:{vertexJson.key}，对应的externalId:{vertexJson.externalId} func 未注册");
                }
                CreateVertexNode(vertexJson.key, vertexJson.redDotType, vertexJson.externalId, isDynamicLeaf: false);
            }
            // 第二遍：建立父子边
            foreach (var edgeJson in config.edges)
            {
                foreach (var outKey in edgeJson.outKeys)
                {
                    _graph.InsertEdge(edgeJson.key, outKey);
                }
            }
        }

        /// <summary>
        /// 创建顶点并关联节点门面。配置加载与动态添加共用此方法。
        /// </summary>
        private RedDotNode CreateVertexNode(string key, int redDotType, int externalId, bool isDynamicLeaf)
        {
            if (_graph.TryGetRank(key, out _))
            {
                Log.Warning($"红点 key 已存在：{key}");
                return null;
            }
            int rank = _graph.Insert(new RedDotVertex(key, redDotType, externalId));
            if (rank < 0) return null;
            var vertex = _graph.GetVertex(rank);
            vertex.IsDynamicLeaf = isDynamicLeaf;
            var node = new RedDotNode(vertex);
            vertex.UserData = node;
            _nodes[key] = node;
            return node;
        }

        // ─── 查询 ───

        /// <summary>获取红点节点门面（用于 MVE 订阅）。不存在返回 null。</summary>
        public RedDotNode GetNode(string key) => _nodes.TryGetValue(key, out var node) ? node : null;

        /// <summary>是否包含某 key 的红点。</summary>
        public bool HasNode(string key) => _nodes.ContainsKey(key);

        // ─── 动态叶子节点增删 ───
        //
        // 设计要点（软删除方案，见移植方案文档"决策 2"）：
        // - 聚合节点（系统入口、页签）静态配置，运行时不删
        // - 动态叶子（列表项：邮件/任务/商品）支持增删
        // - 不调用 DiGraphList.Remove（会触发 O(V+E) 全图 rank 重映射），
        //   而是：断边（O(度数)）+ 从 key 映射移除 + 顶点归还池
        // - List<Vertex> 里的顶点对象保留（成为孤儿），避免 rank 漂移

        /// <summary>
        /// 运行时动态添加一个叶子节点，挂到指定父节点下。
        /// <para>典型场景：邮件列表新增一项、任务列表新增一个任务。</para>
        /// </summary>
        /// <param name="key">叶子节点全局唯一 key（如 "Mail.Item.123"）</param>
        /// <param name="parentKey">父节点 key（必须是已存在的聚合节点）</param>
        /// <param name="logicId">叶子外部逻辑 ID（对应 RegLogic 注册的逻辑）</param>
        /// <param name="allowed">该叶子允许的红点状态位掩码</param>
        /// <returns>新建的节点门面；失败（key 重复/父不存在）返回 null</returns>
        public RedDotNode AddLeaf(string key, string parentKey, int logicId, RedDotStatus allowed = RedDotStatus.Number)
        {
            // 父节点必须存在
            if (!_graph.TryGetRank(parentKey, out int parentRank))
            {
                Log.Warning($"AddLeaf 失败：父节点 {parentKey} 不存在");
                return null;
            }
            // 创建叶子顶点 + 门面
            var node = CreateVertexNode(key, (int)allowed, logicId, isDynamicLeaf: true);
            if (node == null) return null;

            // 建立父→子边
            if (!_graph.TryGetRank(key, out int leafRank))
            {
                // 理论不会发生（刚创建）
                return null;
            }
            _graph.InsertEdge(parentKey, key);
            _dynamicLeafParents[key] = parentKey;

            // 标脏新叶子（数据源），BFS 传播到父，让聚合重算时叶子参与求和/取最高。
            // 注意：必须标脏叶子而非父——DataChange(父) 不会让子节点重算（陷阱）。
            MarkDirty(key);
            return node;
        }

        /// <summary>
        /// 软删除一个动态叶子节点。
        /// <para>断开与父节点的边、从 key 映射移除、顶点归还池；
        /// 标脏父节点触发重算（叶子不再参与聚合）。</para>
        /// <para>注意：仅可删除通过 <see cref="AddLeaf"/> 添加的动态叶子，
        /// 静态配置的聚合节点不可删。</para>
        /// </summary>
        public bool RemoveLeaf(string key)
        {
            if (!_nodes.TryGetValue(key, out var node))
            {
                Log.Warning($"RemoveLeaf 失败：节点 {key} 不存在");
                return false;
            }
            var vertex = node.Vertex;
            if (!vertex.IsDynamicLeaf)
            {
                Log.Warning($"RemoveLeaf 失败：节点 {key} 不是动态叶子（静态聚合节点不可删）");
                return false;
            }
            if (!_dynamicLeafParents.TryGetValue(key, out string parentKey))
            {
                Log.Warning($"RemoveLeaf 失败：节点 {key} 缺少父节点记录（数据不一致）");
                return false;
            }

            // 断开父→子边（O(度数)，叶子通常只有 1 个父）
            _graph.TryGetRank(parentKey, out int parentRank);
            int leafRank = -1;
            // 通过 vertex 找到 rank（vertex 仍在 List 中）
            leafRank = FindRankByVertex(vertex);
            if (leafRank >= 0 && parentRank >= 0)
            {
                _graph.DetachEdgeInternal(parentRank, leafRank);
            }

            // 清理：key 映射、节点门面、动态父记录、id 订阅
            _graph.RemoveFromKeyDictInternal(key);
            _nodes.Remove(key);
            _dynamicLeafParents.Remove(key);
            // 清理该 key 的所有 id 订阅
            CleanupIdCallbacksForKey(key);

            // 释放门面的 BindableProperty，顶点归还池
            node.Dispose();
            vertex.Reset();
            _leafPool.Push(vertex);

            // 标脏父节点触发重算（叶子已不参与聚合）
            MarkDirty(parentKey);
            return true;
        }

        /// <summary>
        /// 批量清理某父节点下的所有动态叶子（如关闭邮件面板时清理所有邮件项红点）。
        /// </summary>
        public int ClearLeavesUnder(string parentKey)
        {
            // 收集该父下所有动态叶子 key（避免迭代中修改字典）
            var keysToRemove = new List<string>();
            foreach (var kv in _dynamicLeafParents)
            {
                if (kv.Value == parentKey) keysToRemove.Add(kv.Key);
            }
            foreach (var k in keysToRemove)
            {
                RemoveLeaf(k);
            }
            return keysToRemove.Count;
        }

        // ─── 内部辅助（暴露给 RedDotSystem 的图操作） ───

        private int FindRankByVertex(RedDotVertex vertex)
        {
            // 朴素遍历查找（动态叶子量级一般不大；若需优化可在 vertex 上缓存 rank）
            return _graph.FindRankByVertexInternal(vertex);
        }

        private void CleanupIdCallbacksForKey(string key)
        {
            var keysToRemove = new List<(string, uint)>();
            foreach (var kv in _idCallbacks)
            {
                if (kv.Key.key == key) keysToRemove.Add(kv.Key);
            }
            foreach (var k in keysToRemove) _idCallbacks.Remove(k);
        }

        // ─── 触发刷新 ───

        /// <summary>
        /// 标记某红点数据已变更（脏传播 + 帧末批量刷新）。
        /// <para>叶子节点会调用注册的逻辑重新计算；聚合节点由子节点状态聚合。</para>
        /// </summary>
        public void MarkDirty(string key)
        {
            var changed = _graph.RedDotDataChange(key);
            foreach (var k in changed)
            {
                _dirtyKeys.Add(k);
            }
        }

        /// <summary>
        /// 标记某红点（带业务 id）数据已变更。
        /// </summary>
        public void MarkDirty(string key, uint id)
        {
            var changed = _graph.RedDotDataChange(key, id);
            foreach (var k in changed)
            {
                _dirtyKeys.Add(k);
            }
            if (id != 0)
            {
                // 同时标脏无 id 维度（因 DataChange(key,id) 内部会再触发 DataChange(key)）
                // 这里幂等加入即可
            }
        }

        /// <summary>
        /// 全量标脏并刷新所有节点。用于配置加载后或需要强制全图重算的场景。
        /// <para>会标脏所有叶子节点（触发 visitor 重算），然后立即 Flush。
        /// 聚合节点由叶子状态聚合得到。</para>
        /// </summary>
        public void RefreshAll()
        {
            // 标脏所有叶子节点（ExternalId != -1），BFS 会传播到祖先
            foreach (var kv in _nodes)
            {
                if (kv.Value.Vertex.ExternalId != -1)
                {
                    MarkDirty(kv.Key);
                }
            }
            // 也标脏所有无子的聚合节点（叶子外的根/孤立节点），确保它们被刷新
            Flush();
        }

        /// <summary>
        /// 立即刷新所有脏节点（通常由 <see cref="OnLateUpdate"/> 自动调用，也可手动强制刷新）。
        /// </summary>
        public void Flush()
        {
            if (_dirtyKeys.Count == 0) return;

            _dirtyKeyBuffer.Clear();
            foreach (var key in _dirtyKeys)
            {
                _dirtyKeyBuffer.Add(key);
            }
            _dirtyKeys.Clear();

            foreach (var key in _dirtyKeyBuffer)
            {
                // id==0：走全局 MVE 推送（RefreshRedDotStatus 内部触发 OnVertexRefreshed → ApplyRefreshedState）
                _graph.RefreshRedDotStatus(key, 0, VisitLeafVertex);
                // 带 id 的列表项：遍历订阅者，逐 id 刷新并回调
                FlushIdCallbacks(key);
            }
        }

        /// <summary>
        /// 帧末自动刷新（由 SingletonSystem 的 LateUpdate 驱动）。
        /// </summary>
        public void OnLateUpdate()
        {
            Flush();
        }

        // ─── 带 id 列表项红点订阅 ───

        /// <summary>
        /// 订阅带业务 id 的红点状态变化（列表项红点）。
        /// <para>回调签名：<c>(status, value) =&gt; ...</c>。订阅时立即推送一次当前状态。</para>
        /// </summary>
        public void AddIdListener(string key, uint id, Action<RedDotStatus, int> callback)
        {
            _idCallbacks[(key, id)] = callback;
            // 立即推送一次当前状态：先标脏确保 visitor 被调用（首次订阅时节点可能未脏）
            _graph.RedDotDataChange(key, id);
            FlushIdCallback(key, id, callback);
        }

        /// <summary>取消订阅带业务 id 的红点。</summary>
        public void RemoveIdListener(string key, uint id)
        {
            _idCallbacks.Remove((key, id));
        }

        private void FlushIdCallbacks(string key)
        {
            // 遍历所有匹配 key 的 id 订阅（朴素实现；量大时可建 key→ids 反向索引）
            foreach (var kv in _idCallbacks)
            {
                if (kv.Key.key != key) continue;
                _graph.RefreshRedDotStatus(key, kv.Key.id, VisitLeafVertex);
                var info = _graph.GetRedDotByKey(key)?.GetRuntimeInfo(kv.Key.id);
                if (info != null)
                {
                    kv.Value?.Invoke(info.RedDotStatus, info.Value);
                }
            }
        }

        private void FlushIdCallback(string key, uint id, Action<RedDotStatus, int> callback)
        {
            _graph.RefreshRedDotStatus(key, id, VisitLeafVertex);
            var info = _graph.GetRedDotByKey(key)?.GetRuntimeInfo(id);
            if (info != null)
            {
                callback?.Invoke(info.RedDotStatus, info.Value);
            }
        }

        // ─── 图回调 ───

        private void OnVertexRefreshed(RedDotVertex vertex, RedDotStatus status, int value)
        {
            if (vertex.UserData is RedDotNode node)
            {
                node.ApplyRefreshedState(status, value);
            }
        }

        private void VisitLeafVertex(RedDotVertex v, uint id)
        {
            var info = v.GetRuntimeInfo(id);
            _visitor.Visit(v.ExternalId, id, out info.RedDotStatus, out info.Value);
        }
    }
}
