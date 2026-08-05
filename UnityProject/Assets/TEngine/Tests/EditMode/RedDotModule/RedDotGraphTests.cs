using GameLogic.RedDot.Core;
using NUnit.Framework;

namespace TEngine.Tests.RedDot
{
    /// <summary>
    /// RedDotGraph 核心算法单元测试（阶段1验证）。
    /// 覆盖：建图/加边/环检测/状态聚合/脏传播。
    /// </summary>
    [TestFixture]
    public class RedDotGraphTests
    {
        private RedDotGraph _graph;

        [SetUp]
        public void SetUp()
        {
            _graph = new RedDotGraph();
        }

        // ─── 建图与查询 ───

        [Test]
        public void InsertVertex_ReturnsIncrementingRank()
        {
            int r0 = _graph.Insert(new RedDotVertex("A", 0, -1));
            int r1 = _graph.Insert(new RedDotVertex("B", 0, -1));

            Assert.AreEqual(0, r0);
            Assert.AreEqual(1, r1);
            Assert.AreEqual(0, _graph.GetRedDotByKey("A").RedDotType);
            Assert.IsNotNull(_graph.GetRedDotByKey("A"));
            Assert.IsNotNull(_graph.GetRedDotByKey("B"));
        }

        [Test]
        public void InsertVertex_DuplicateKey_ReturnsNegativeOne()
        {
            _graph.Insert(new RedDotVertex("A", 0, -1));
            int second = _graph.Insert(new RedDotVertex("A", 1, -1));

            Assert.AreEqual(-1, second);
            Assert.AreEqual(0, _graph.GetRedDotByKey("A").RedDotType); // 保留首次
        }

        [Test]
        public void InsertEdge_BuildsParentChildRelation()
        {
            _graph.Insert(new RedDotVertex("Parent", 0, -1));
            _graph.Insert(new RedDotVertex("Child", 0, -1));

            bool ok = _graph.InsertEdge("Parent", "Child");

            Assert.IsTrue(ok);
            // 父→子边存在：Parent 的首个邻居应是 Child 的 rank
            int parentRank, childRank;
            _graph.TryGetRank("Parent", out parentRank);
            _graph.TryGetRank("Child", out childRank);
            Assert.AreEqual(childRank, _graph.FirstNbr(parentRank));
        }

        // ─── 环检测 ───

        [Test]
        public void InsertEdge_Cycle_ReturnsFalse()
        {
            _graph.Insert(new RedDotVertex("A", 0, -1));
            _graph.Insert(new RedDotVertex("B", 0, -1));
            _graph.Insert(new RedDotVertex("C", 0, -1));
            _graph.InsertEdge("A", "B");
            _graph.InsertEdge("B", "C");

            // C → A 会构成环
            bool ok = _graph.InsertEdge("C", "A");

            Assert.IsFalse(ok, "构成环的边应被拒绝");
        }

        [Test]
        public void InsertEdge_DiamondDependency_Allowed()
        {
            // A → B, A → C, B → D, C → D （菱形依赖，非环）
            _graph.Insert(new RedDotVertex("A", 0, -1));
            _graph.Insert(new RedDotVertex("B", 0, -1));
            _graph.Insert(new RedDotVertex("C", 0, -1));
            _graph.Insert(new RedDotVertex("D", 0, -1));

            Assert.IsTrue(_graph.InsertEdge("A", "B"));
            Assert.IsTrue(_graph.InsertEdge("A", "C"));
            Assert.IsTrue(_graph.InsertEdge("B", "D"));
            Assert.IsTrue(_graph.InsertEdge("C", "D"), "菱形依赖（DAG）应允许，不构成环");
        }

        [Test]
        public void InsertEdge_Duplicate_ReturnsFalse()
        {
            _graph.Insert(new RedDotVertex("A", 0, -1));
            _graph.Insert(new RedDotVertex("B", 0, -1));

            Assert.IsTrue(_graph.InsertEdge("A", "B"));
            Assert.IsFalse(_graph.InsertEdge("A", "B"), "重复边应拒绝");
        }

        // ─── 脏传播 ───

        [Test]
        public void DataChange_MarksSelfDirty()
        {
            _graph.Insert(new RedDotVertex("A", 0, -1));
            var changed = _graph.RedDotDataChange("A");

            CollectionAssert.Contains(changed, "A");
            Assert.IsTrue(_graph.GetRedDotByKey("A").GetRuntimeInfo(0).IsDataChange);
        }

        [Test]
        public void DataChange_PropagatesToAncestors_BFS()
        {
            // Root → Mid → Leaf
            _graph.Insert(new RedDotVertex("Root", 0, -1));
            _graph.Insert(new RedDotVertex("Mid", 0, -1));
            _graph.Insert(new RedDotVertex("Leaf", 0, -1));
            _graph.InsertEdge("Root", "Mid");
            _graph.InsertEdge("Mid", "Leaf");

            var changed = _graph.RedDotDataChange("Leaf");

            // 三者都应被标脏
            CollectionAssert.Contains(changed, "Leaf");
            CollectionAssert.Contains(changed, "Mid");
            CollectionAssert.Contains(changed, "Root");
        }

        [Test]
        public void DataChange_Idempotent_SecondCallReturnsEmpty()
        {
            _graph.Insert(new RedDotVertex("A", 0, -1));
            _graph.RedDotDataChange("A");

            var second = _graph.RedDotDataChange("A");

            Assert.IsEmpty(second, "已脏节点再次标脏应幂等返回空");
        }

        // ─── 状态聚合：叶子（外部逻辑） ───

        [Test]
        public void RefreshRedDotStatus_LeafCallsVisitor()
        {
            _graph.Insert(new RedDotVertex("Leaf", (int)RedDotStatus.Number, 100));
            _graph.RedDotDataChange("Leaf");

            int visitedExternalId = -1;
            _graph.RefreshRedDotStatus("Leaf", 0, (v, id) =>
            {
                visitedExternalId = v.ExternalId;
                var info = v.GetRuntimeInfo(id);
                info.RedDotStatus = RedDotStatus.Number;
                info.Value = 5;
            });

            Assert.AreEqual(100, visitedExternalId, "叶子节点应调用 visitor 取状态");
            var info = _graph.GetRedDotByKey("Leaf").GetRuntimeInfo(0);
            Assert.AreEqual(RedDotStatus.Number, info.RedDotStatus);
            Assert.AreEqual(5, info.Value);
            Assert.IsFalse(info.IsDataChange, "刷新后应清除脏标记");
        }

        // ─── 状态聚合：数量型父节点求和 ───

        [Test]
        public void RefreshRedDotStatus_NumberParent_AggregatesChildValues()
        {
            // NumberParent(Number) → Leaf1(Number), Leaf2(Number)
            _graph.Insert(new RedDotVertex("NumberParent", (int)RedDotStatus.Number, -1));
            _graph.Insert(new RedDotVertex("Leaf1", (int)RedDotStatus.Number, 1));
            _graph.Insert(new RedDotVertex("Leaf2", (int)RedDotStatus.Number, 2));
            _graph.InsertEdge("NumberParent", "Leaf1");
            _graph.InsertEdge("NumberParent", "Leaf2");

            // DataChange 语义：标记"数据源变更"的节点。叶子是数据源，故标脏叶子；
            // BFS 会沿入边向上传播，把父也标脏。
            _graph.RedDotDataChange("Leaf1");
            _graph.RedDotDataChange("Leaf2");

            _graph.RefreshRedDotStatus("NumberParent", 0, (v, id) =>
            {
                var info = v.GetRuntimeInfo(id);
                info.RedDotStatus = RedDotStatus.Number;
                // ExternalId 1 → 值 3；ExternalId 2 → 值 7
                info.Value = v.ExternalId == 1 ? 3 : 7;
            });

            var parentInfo = _graph.GetRedDotByKey("NumberParent").GetRuntimeInfo(0);
            Assert.AreEqual(RedDotStatus.Number, parentInfo.RedDotStatus);
            Assert.AreEqual(10, parentInfo.Value, "数量型父节点应累加子节点值：3 + 7 = 10");
        }

        // ─── 状态聚合：状态型父节点取最高优先级 ───

        [Test]
        public void RefreshRedDotStatus_StateParent_TakesHighestStatus()
        {
            // StateParent(允许 Normal|New|Full=1|4|8=13) → LeafNormal(Normal), LeafFull(Full)
            _graph.Insert(new RedDotVertex("StateParent", (int)(RedDotStatus.Normal | RedDotStatus.New | RedDotStatus.Full), -1));
            _graph.Insert(new RedDotVertex("LeafNormal", (int)RedDotStatus.Normal, 1));
            _graph.Insert(new RedDotVertex("LeafFull", (int)RedDotStatus.Full, 2));
            _graph.InsertEdge("StateParent", "LeafNormal");
            _graph.InsertEdge("StateParent", "LeafFull");
            // 标脏叶子（数据源），BFS 传播到父
            _graph.RedDotDataChange("LeafNormal");
            _graph.RedDotDataChange("LeafFull");

            _graph.RefreshRedDotStatus("StateParent", 0, (v, id) =>
            {
                var info = v.GetRuntimeInfo(id);
                info.RedDotStatus = v.ExternalId == 1 ? RedDotStatus.Normal : RedDotStatus.Full;
            });

            var parentInfo = _graph.GetRedDotByKey("StateParent").GetRuntimeInfo(0);
            // Full(8) > Normal(1)，父应取 Full
            Assert.AreEqual(RedDotStatus.Full, parentInfo.RedDotStatus, "状态型父节点应取最高优先级状态");
        }

        // ─── 状态聚合：三层级联 ───

        [Test]
        public void RefreshRedDotStatus_ThreeLevelCascade()
        {
            // Root(Number) → Mail(Number) → Item(Number, 叶子)
            _graph.Insert(new RedDotVertex("Root", (int)RedDotStatus.Number, -1));
            _graph.Insert(new RedDotVertex("Mail", (int)RedDotStatus.Number, -1));
            _graph.Insert(new RedDotVertex("Item", (int)RedDotStatus.Number, 1));
            _graph.InsertEdge("Root", "Mail");
            _graph.InsertEdge("Mail", "Item");
            // 标脏叶子 Item，BFS 传播到 Mail、Root
            _graph.RedDotDataChange("Item");

            _graph.RefreshRedDotStatus("Root", 0, (v, id) =>
            {
                var info = v.GetRuntimeInfo(id);
                info.RedDotStatus = RedDotStatus.Number;
                info.Value = 42;
            });

            var rootInfo = _graph.GetRedDotByKey("Root").GetRuntimeInfo(0);
            var mailInfo = _graph.GetRedDotByKey("Mail").GetRuntimeInfo(0);
            Assert.AreEqual(42, rootInfo.Value, "三层级联数量应正确向上传播");
            Assert.AreEqual(42, mailInfo.Value);
        }

        // ─── OnVertexRefreshed 推送钩子（MVE 接入点） ───

        [Test]
        public void OnVertexRefreshed_InvokedWithFinalState()
        {
            _graph.Insert(new RedDotVertex("NumberParent", (int)RedDotStatus.Number, -1));
            _graph.Insert(new RedDotVertex("Leaf", (int)RedDotStatus.Number, 1));
            _graph.InsertEdge("NumberParent", "Leaf");
            _graph.RedDotDataChange("Leaf");

            RedDotStatus capturedStatus = RedDotStatus.Null;
            int capturedValue = -1;
            string capturedKey = null;
            _graph.OnVertexRefreshed = (v, status, value) =>
            {
                capturedKey = v.Key;
                capturedStatus = status;
                capturedValue = value;
            };

            _graph.RefreshRedDotStatus("NumberParent", 0, (v, id) =>
            {
                var info = v.GetRuntimeInfo(id);
                info.RedDotStatus = RedDotStatus.Number;
                info.Value = 9;
            });

            Assert.AreEqual("NumberParent", capturedKey);
            Assert.AreEqual(RedDotStatus.Number, capturedStatus);
            Assert.AreEqual(9, capturedValue);
        }

        [Test]
        public void OnVertexRefreshed_NotInvokedForIdNonZero()
        {
            _graph.Insert(new RedDotVertex("Leaf", (int)RedDotStatus.Number, 1));
            _graph.RedDotDataChange("Leaf", id: 123);

            bool invoked = false;
            _graph.OnVertexRefreshed = (v, s, val) => invoked = true;

            _graph.RefreshRedDotStatus("Leaf", 123, (v, id) =>
            {
                var info = v.GetRuntimeInfo(id);
                info.RedDotStatus = RedDotStatus.Number;
                info.Value = 1;
            });

            Assert.IsFalse(invoked, "带 id 的列表项红点不应走全局 MVE 推送通道");
        }
    }
}
