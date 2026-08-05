using GameLogic.DataBinding;
using GameLogic.RedDot;
using GameLogic.RedDot.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace TEngine.Tests.RedDot
{
    /// <summary>
    /// 动态叶子节点增删测试（阶段3验证）。
    /// 覆盖：AddLeaf/RemoveLeaf/ClearLeavesUnder、聚合正确性、池化复用、静态节点保护。
    /// </summary>
    [TestFixture]
    public class RedDotDynamicLeafTests
    {
        private RedDotSystem _system;
        private int _tid;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            LogAssert.Expect(LogType.Error, "请必须通过Instance方法来实例化GameLogic.RedDot.RedDotSystem类");
            _system = RedDotSystem.CreateForTest();
        }

        [SetUp]
        public void SetUp()
        {
            _tid = TestContext.CurrentContext.Test.Name.GetHashCode();
        }

        private string K(string s) => $"T{_tid}_{s}";

        private void FlushAll()
        {
            _system.Flush();
            if (SingletonHasInstance<BatchScheduler>())
            {
                BatchScheduler.Instance.OnLateUpdate();
            }
        }

        /// <summary>构造一个 Root(Number, 聚合) 静态节点作为动态叶子的父。</summary>
        private string SetupRoot()
        {
            string root = K("Root");
            string json = @"{""vertexs"":[{""key"":""" + root + @""",""redDotType"":2,""externalId"":-1}],""edges"":[]}";
            _system.LoadJsonText(json, checkVisitor: false);
            return root;
        }

        // ─── AddLeaf 基础 ───

        [Test]
        public void AddLeaf_CreatesNodeAndEdge()
        {
            string root = SetupRoot();
            _system.RegLogic(1, (out RedDotStatus s, out int n) => { s = RedDotStatus.Number; n = 5; });

            var leaf = _system.AddLeaf(K("Item"), root, 1);

            Assert.IsNotNull(leaf);
            Assert.IsTrue(_system.HasNode(K("Item")));
        }

        [Test]
        public void AddLeaf_ParentNotExists_ReturnsNull()
        {
            _system.RegLogic(1, (out RedDotStatus s, out int n) => { s = RedDotStatus.Number; n = 1; });

            var leaf = _system.AddLeaf(K("Item"), K("NotExist"), 1);

            Assert.IsNull(leaf);
        }

        [Test]
        public void AddLeaf_DuplicateKey_ReturnsNull()
        {
            string root = SetupRoot();
            _system.RegLogic(1, (out RedDotStatus s, out int n) => { s = RedDotStatus.Number; n = 1; });
            _system.AddLeaf(K("Item"), root, 1);

            var dup = _system.AddLeaf(K("Item"), root, 1);

            Assert.IsNull(dup);
        }

        // ─── 聚合正确性 ───

        [Test]
        public void AddLeaf_ParentAggregatesLeafValue()
        {
            string root = SetupRoot();
            int leafValue = 7;
            _system.RegLogic(1, (out RedDotStatus s, out int n) => { s = RedDotStatus.Number; n = leafValue; });

            var rootNode = _system.GetNode(root);
            RedDotNodeState rootCaptured = RedDotNodeState.Empty;
            rootNode.State.OnValueChanged += (o, c) => rootCaptured = c;

            _system.AddLeaf(K("Item"), root, 1);
            FlushAll();

            Assert.AreEqual(7, rootCaptured.Count, "添加叶子后父节点应聚合叶子值");
            Assert.AreEqual(RedDotStatus.Number, rootCaptured.Status);
        }

        [Test]
        public void AddLeaf_MultipleLeaves_SumsValues()
        {
            string root = SetupRoot();
            _system.RegLogic(1, (out RedDotStatus s, out int n) => { s = RedDotStatus.Number; n = 3; });
            _system.RegLogic(2, (out RedDotStatus s, out int n) => { s = RedDotStatus.Number; n = 4; });

            var rootNode = _system.GetNode(root);
            RedDotNodeState rootCaptured = RedDotNodeState.Empty;
            rootNode.State.OnValueChanged += (o, c) => rootCaptured = c;

            _system.AddLeaf(K("Item1"), root, 1);
            _system.AddLeaf(K("Item2"), root, 2);
            FlushAll();

            Assert.AreEqual(7, rootCaptured.Count, "两个叶子值应求和：3+4=7");
        }

        // ─── RemoveLeaf ───

        [Test]
        public void RemoveLeaf_RemovesNodeAndRecomputesParent()
        {
            string root = SetupRoot();
            _system.RegLogic(1, (out RedDotStatus s, out int n) => { s = RedDotStatus.Number; n = 5; });
            _system.RegLogic(2, (out RedDotStatus s, out int n) => { s = RedDotStatus.Number; n = 8; });

            _system.AddLeaf(K("Item1"), root, 1);
            _system.AddLeaf(K("Item2"), root, 2);
            FlushAll();

            var rootNode = _system.GetNode(root);
            RedDotNodeState rootCaptured = RedDotNodeState.Empty;
            rootNode.State.OnValueChanged += (o, c) => rootCaptured = c;

            bool ok = _system.RemoveLeaf(K("Item1"));
            FlushAll();

            Assert.IsTrue(ok);
            Assert.IsFalse(_system.HasNode(K("Item1")), "删除后节点应不可查询");
            Assert.AreEqual(8, rootCaptured.Count, "删除 Item1(5) 后父应只剩 Item2(8)");
        }

        [Test]
        public void RemoveLeaf_NotExists_ReturnsFalse()
        {
            SetupRoot();
            bool ok = _system.RemoveLeaf(K("NotExist"));
            Assert.IsFalse(ok);
        }

        [Test]
        public void RemoveLeaf_StaticNode_ReturnsFalse()
        {
            string root = SetupRoot();

            // Root 是静态配置的聚合节点，不可删
            bool ok = _system.RemoveLeaf(root);

            Assert.IsFalse(ok, "静态聚合节点不应可删");
            Assert.IsTrue(_system.HasNode(root), "静态节点应仍存在");
        }

        [Test]
        public void RemoveLeaf_LastLeaf_ParentBecomesEmpty()
        {
            string root = SetupRoot();
            _system.RegLogic(1, (out RedDotStatus s, out int n) => { s = RedDotStatus.Number; n = 5; });

            _system.AddLeaf(K("Item"), root, 1);
            FlushAll();

            var rootNode = _system.GetNode(root);
            RedDotNodeState rootCaptured = rootNode.State.Value;
            rootNode.State.OnValueChanged += (o, c) => rootCaptured = c;

            _system.RemoveLeaf(K("Item"));
            FlushAll();

            Assert.IsFalse(rootCaptured.IsActive, "删除唯一叶子后父应变为无红点");
        }

        // ─── ClearLeavesUnder ───

        [Test]
        public void ClearLeavesUnder_RemovesAllDynamicLeaves()
        {
            string root = SetupRoot();
            _system.RegLogic(1, (out RedDotStatus s, out int n) => { s = RedDotStatus.Number; n = 1; });

            _system.AddLeaf(K("Item1"), root, 1);
            _system.AddLeaf(K("Item2"), root, 1);
            _system.AddLeaf(K("Item3"), root, 1);

            int removed = _system.ClearLeavesUnder(root);

            Assert.AreEqual(3, removed);
            Assert.IsFalse(_system.HasNode(K("Item1")));
            Assert.IsFalse(_system.HasNode(K("Item2")));
            Assert.IsFalse(_system.HasNode(K("Item3")));
        }

        [Test]
        public void ClearLeavesUnder_OnlyDynamicLeaves_StaticNodeUntouched()
        {
            string root = SetupRoot();
            string child = K("Child");
            // child 作为静态聚合节点
            string json2 = @"{""vertexs"":[{""key"":""" + child + @""",""redDotType"":2,""externalId"":-1}],""edges"":[{""key"":""" + root + @""",""outKeys"":[""" + child + @"""]}]}";
            _system.LoadJsonText(json2, checkVisitor: false);

            _system.RegLogic(1, (out RedDotStatus s, out int n) => { s = RedDotStatus.Number; n = 1; });
            _system.AddLeaf(K("Leaf1"), root, 1);
            _system.AddLeaf(K("Leaf2"), root, 1);

            int removed = _system.ClearLeavesUnder(root);

            Assert.AreEqual(2, removed, "只应清理动态叶子");
            Assert.IsTrue(_system.HasNode(child), "静态子节点不应被清理");
        }

        // ─── 池化复用 ───

        [Test]
        public void RemoveAndAdd_PoolReusesVertex()
        {
            string root = SetupRoot();
            _system.RegLogic(1, (out RedDotStatus s, out int n) => { s = RedDotStatus.Number; n = 1; });

            // 第一次：添加后删除，顶点归还池
            _system.AddLeaf(K("Item"), root, 1);
            _system.RemoveLeaf(K("Item"));

            // 第二次：从池中复用顶点
            var leaf2 = _system.AddLeaf(K("Item2"), root, 1);

            Assert.IsNotNull(leaf2, "删除后应能再次添加（池化复用）");
            Assert.AreEqual(K("Item2"), leaf2.Key);
            Assert.IsTrue(_system.HasNode(K("Item2")));
        }

        [Test]
        public void RemovedLeaf_KeyReusable()
        {
            string root = SetupRoot();
            _system.RegLogic(1, (out RedDotStatus s, out int n) => { s = RedDotStatus.Number; n = 1; });

            _system.AddLeaf(K("Item"), root, 1);
            _system.RemoveLeaf(K("Item"));

            // 相同 key 应能再次添加（旧 key 已从映射移除）
            var leaf = _system.AddLeaf(K("Item"), root, 1);

            Assert.IsNotNull(leaf, "删除后相同 key 应可复用");
        }

        private static bool SingletonHasInstance<T>() where T : class
        {
            var field = typeof(T).GetField("_instance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            return field?.GetValue(null) != null;
        }
    }
}
