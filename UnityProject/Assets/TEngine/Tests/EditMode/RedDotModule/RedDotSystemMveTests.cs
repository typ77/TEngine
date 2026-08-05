using System.Reflection;
using GameLogic.DataBinding;
using GameLogic.RedDot;
using GameLogic.RedDot.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace TEngine.Tests.RedDot
{
    /// <summary>
    /// RedDotSystem MVE 接入层端到端测试（阶段2验证）。
    /// 验证完整链路：MarkDirty → 图聚合 → OnVertexRefreshed → BindableProperty → UI 回调。
    /// <para>EditMode 约束：RedDotSystem 是 ILateUpdate，首次 Instance 初始化会触发
    /// SingletonSystem.CheckInit → UpdateDriver → DontDestroyOnLoad。
    /// 故采用 OneTimeSetUp 创建一次实例并复用；每个测试用独立 key 避免状态污染。</para>
    /// </summary>
    [TestFixture]
    public class RedDotSystemMveTests
    {
        private static RedDotSystem _system;
        private int _testCounter;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // EditMode 下 SingletonSystem 初始化会触发 DontDestroyOnLoad（仅 PlayMode 可用），
            // 故使用 CreateForTest 绕过 SingletonSystem 注册，直接创建引擎实例。
            // Singleton 构造函数对非 Instance 调用会打一条 Error 日志，需预期以避免测试失败。
            LogAssert.Expect(LogType.Error, "请必须通过Instance方法来实例化GameLogic.RedDot.RedDotSystem类");
            _system = RedDotSystem.CreateForTest();
        }

        [SetUp]
        public void SetUp()
        {
            _testCounter = TestContext.CurrentContext.Test.Name.GetHashCode(); // 用作 key 后缀隔离
        }

        private string K(string suffix) => $"Test{_testCounter}_{suffix}";

        private void FlushAll()
        {
            // EditMode 下 BatchScheduler 单例可能不存在，SafeMarkDirty 会退化为同步触发
            _system.Flush();
            if (SingletonHasInstance<BatchScheduler>())
            {
                BatchScheduler.Instance.OnLateUpdate();
            }
        }

        // ─── 基础：配置加载 + 节点门面 ───

        [Test]
        public void LoadJsonText_CreatesNodesAndEdges()
        {
            string root = K("Root");
            string mail = K("Mail");
            string json = @"{""vertexs"":[{""key"":""" + root + @""",""redDotType"":2,""externalId"":-1},{""key"":""" + mail + @""",""redDotType"":2,""externalId"":1}],""edges"":[{""key"":""" + root + @""",""outKeys"":[""" + mail + @"""]}]}";
            _system.LoadJsonText(json, checkVisitor: false);

            Assert.IsTrue(_system.HasNode(root));
            Assert.IsTrue(_system.HasNode(mail));
        }

        // ─── 核心：MVE 推送链路 ───

        [Test]
        public void MarkDirty_PropagatesToBindableProperty()
        {
            string root = K("Root");
            string mail = K("Mail");
            string json = @"{""vertexs"":[{""key"":""" + root + @""",""redDotType"":2,""externalId"":-1},{""key"":""" + mail + @""",""redDotType"":2,""externalId"":1}],""edges"":[{""key"":""" + root + @""",""outKeys"":[""" + mail + @"""]}]}";
            _system.LoadJsonText(json, checkVisitor: false);

            int unreadCount = 5;
            _system.RegLogic(1, (out RedDotStatus s, out int n) =>
            {
                s = unreadCount > 0 ? RedDotStatus.Number : RedDotStatus.Null;
                n = unreadCount;
            });

            var rootNode = _system.GetNode(root);
            RedDotNodeState captured = RedDotNodeState.Empty;
            rootNode.State.OnValueChanged += (old, cur) => captured = cur;

            _system.MarkDirty(mail);
            FlushAll();

            Assert.AreEqual(RedDotStatus.Number, captured.Status);
            Assert.AreEqual(5, captured.Count);
        }

        [Test]
        public void MarkDirty_ValueChanged_FiresAgain()
        {
            string leaf = K("Leaf");
            string json = @"{""vertexs"":[{""key"":""" + leaf + @""",""redDotType"":2,""externalId"":1}],""edges"":[]}";
            _system.LoadJsonText(json, checkVisitor: false);

            int value = 3;
            _system.RegLogic(1, (out RedDotStatus s, out int n) =>
            {
                s = value > 0 ? RedDotStatus.Number : RedDotStatus.Null;
                n = value;
            });

            var node = _system.GetNode(leaf);
            int callbackCount = 0;
            node.State.OnValueChanged += (o, c) => callbackCount++;

            _system.MarkDirty(leaf);
            FlushAll();
            int firstCount = callbackCount;

            value = 7;
            _system.MarkDirty(leaf);
            FlushAll();

            Assert.Greater(callbackCount, firstCount, "值变化应再次触发");
            Assert.AreEqual(7, node.State.Value.Count);
        }

        [Test]
        public void MarkDirty_CascadeFromLeafToRoot()
        {
            string root = K("Root");
            string mail = K("Mail");
            string item = K("Item");
            string json = @"{""vertexs"":[{""key"":""" + root + @""",""redDotType"":2,""externalId"":-1},{""key"":""" + mail + @""",""redDotType"":2,""externalId"":-1},{""key"":""" + item + @""",""redDotType"":2,""externalId"":1}],""edges"":[{""key"":""" + root + @""",""outKeys"":[""" + mail + @"""]},{""key"":""" + mail + @""",""outKeys"":[""" + item + @"""]}]}";
            _system.LoadJsonText(json, checkVisitor: false);

            int count = 42;
            _system.RegLogic(1, (out RedDotStatus s, out int n) =>
            {
                s = RedDotStatus.Number; n = count;
            });

            var rootNode = _system.GetNode(root);
            RedDotNodeState rootCaptured = RedDotNodeState.Empty;
            rootNode.State.OnValueChanged += (o, c) => rootCaptured = c;

            _system.MarkDirty(item);
            FlushAll();

            Assert.AreEqual(42, rootCaptured.Count, "叶子变更应级联传播到根");
        }

        [Test]
        public void RefreshAll_ComputesAllNodesAfterLoad()
        {
            // 验证配置加载后调用 RefreshAll，所有节点状态被正确计算
            string root = K("Root");
            string leaf = K("Leaf");
            string json = @"{""vertexs"":[{""key"":""" + root + @""",""redDotType"":2,""externalId"":-1},{""key"":""" + leaf + @""",""redDotType"":2,""externalId"":1}],""edges"":[{""key"":""" + root + @""",""outKeys"":[""" + leaf + @"""]}]}";
            _system.LoadJsonText(json, checkVisitor: false);

            _system.RegLogic(1, (out RedDotStatus s, out int n) =>
            {
                s = RedDotStatus.Number; n = 9;
            });

            // 加载后未刷新前，节点状态应为默认 Empty
            var rootNode = _system.GetNode(root);
            Assert.IsFalse(rootNode.State.Value.IsActive, "加载后未刷新前应为无红点");

            // 全量刷新
            _system.RefreshAll();
            FlushAll();

            Assert.IsTrue(rootNode.State.Value.IsActive, "RefreshAll 后应有红点");
            Assert.AreEqual(9, rootNode.State.Value.Count);
        }

        // ─── 带 id 列表项红点 ───

        [Test]
        public void AddIdListener_ReceivesState()
        {
            string node = K("MailItem");
            string json = @"{""vertexs"":[{""key"":""" + node + @""",""redDotType"":2,""externalId"":2}],""edges"":[]}";
            _system.LoadJsonText(json, checkVisitor: false);

            _system.RegLogic(2, (uint id, out RedDotStatus s, out int n) =>
            {
                s = id % 2 == 0 ? RedDotStatus.Number : RedDotStatus.Null;
                n = (int)id;
            });

            RedDotStatus capturedStatus = RedDotStatus.Null;
            int capturedValue = 0;
            _system.AddIdListener(node, 4, (s, v) =>
            {
                capturedStatus = s;
                capturedValue = v;
            });

            Assert.AreEqual(RedDotStatus.Number, capturedStatus);
            Assert.AreEqual(4, capturedValue);
        }

        [Test]
        public void MarkDirty_WithId_TriggersIdCallback()
        {
            string node = K("MailItem");
            string json = @"{""vertexs"":[{""key"":""" + node + @""",""redDotType"":2,""externalId"":2}],""edges"":[]}";
            _system.LoadJsonText(json, checkVisitor: false);

            int value = 10;
            _system.RegLogic(2, (uint id, out RedDotStatus s, out int n) =>
            {
                s = RedDotStatus.Number; n = value;
            });

            int callbackCount = 0;
            _system.AddIdListener(node, 1, (s, v) => callbackCount++);
            int initialCount = callbackCount;

            value = 20;
            _system.MarkDirty(node, id: 1);
            _system.Flush();

            Assert.Greater(callbackCount, initialCount, "带 id 的 MarkDirty 应触发 id 回调");
        }

        // ─── 辅助 ───

        private static bool SingletonHasInstance<T>() where T : class
        {
            var field = typeof(T).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            return field?.GetValue(null) != null;
        }
    }
}
