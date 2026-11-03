using System.Text.RegularExpressions;
using NUnit.Framework;
using RedDot.Runtime.RedDot;
using UnityEngine;
using UnityEngine.TestTools;

namespace TEngine.Tests.RedDot
{
    /// <summary>
    /// 动态节点测试(P6-4/P0-2)。
    /// 覆盖:RegisterDynamicNode 幂等、SetDynamicParent(含配置未就绪重放)/BindDynamicChecker(重复覆盖)/
    /// RemoveDynamicNode(父链补发重算)、RemoveDynamicParent。
    /// 只测公共 API;每个用例经 RedDotManager.Ins().Reset() 复位(Reset 置空单例)。
    /// 注:动态挂父重放表 s_dynParents 为 static 层(Reset 不清,重登重放契约),跨用例残留只会
    /// 在重新 RegisterDynamicNode 时自动补挂已有父边,对断言无影响。
    /// </summary>
    [TestFixture]
    public class RedDotDynamicNodeTests
    {
        private const string GraphJson = @"
{
  ""vertexs"": [
    {""key"":""Root"",""redDotType"":63,""externalId"":-1},
    {""key"":""Mail"",""redDotType"":63,""externalId"":-1}
  ],
  ""edges"": [
    {""key"":""Root"",""outKeys"":[""Mail""]}
  ]
}";

        private RedDotManager _mgr;

        [SetUp]
        public void SetUp()
        {
            RedDotManager.Ins().Reset();
            _mgr = RedDotManager.Ins();
            Assert.IsTrue(_mgr.AddJsonStr(GraphJson, checkVisitor: false));
        }

        [TearDown]
        public void TearDown()
        {
            RedDotManager.Ins().Reset();
            _mgr = null;
        }

        private void BindRedDotChecker(string dynKey)
        {
            _mgr.BindDynamicChecker(dynKey, (out RedDotStatus s, out int n) =>
            {
                s = RedDotStatus.RedDotStatusRedDot;
                n = 0;
            });
        }

        [Test]
        public void RegisterDynamicNode_IsIdempotentPerOwner()
        {
            _mgr.RegisterDynamicNode("act:1", out var dynKey1);
            _mgr.RegisterDynamicNode("act:1", out var dynKey1Again);
            _mgr.RegisterDynamicNode("act:2", out var dynKey2);

            // 同 ownerKey 复用同一 dynKey;不同 owner 不同 key(dyn: 前缀隔离)
            Assert.AreEqual("dyn:act:1", dynKey1);
            Assert.AreEqual(dynKey1, dynKey1Again);
            Assert.AreEqual("dyn:act:2", dynKey2);
            Assert.AreNotEqual(dynKey1, dynKey2);

            // 动态节点已入图,可读(初态 Null)
            _mgr.GetRedDotStatus(dynKey1, 0, out var status, out var num);
            Assert.AreEqual(RedDotStatus.RedDotStatusNull, status);
        }

        [Test]
        public void RegisterDynamicNode_EmptyOwnerKey_Rejected()
        {
            LogAssert.Expect(LogType.Error, new Regex("RegisterDynamicNode ownerKey 为空"));
            _mgr.RegisterDynamicNode(string.Empty, out var dynKey);
            Assert.AreEqual(string.Empty, dynKey);
        }

        [Test]
        public void DynamicNode_BindChecker_DataChange_ParentAggregates()
        {
            _mgr.RegisterDynamicNode("act:1", out var dynKey);
            _mgr.SetDynamicParent(dynKey, "Mail");
            BindRedDotChecker(dynKey);
            _mgr.DataChange(dynKey);

            _mgr.GetRedDotStatus(dynKey, 0, out var status, out _);
            Assert.AreEqual(RedDotStatus.RedDotStatusRedDot, status);
            _mgr.GetRedDotStatus("Mail", 0, out status, out _);
            Assert.AreEqual(RedDotStatus.RedDotStatusRedDot, status);
            _mgr.GetRedDotStatus("Root", 0, out status, out _);
            Assert.AreEqual(RedDotStatus.RedDotStatusRedDot, status);
        }

        [Test]
        public void DynamicNode_BindChecker_LastRegistrationWins()
        {
            _mgr.RegisterDynamicNode("act:1", out var dynKey);
            BindRedDotChecker(dynKey);
            _mgr.BindDynamicChecker(dynKey, (out RedDotStatus s, out int n) =>
            {
                s = RedDotStatus.RedDotStatusBlueNumber;
                n = 4;
            });
            _mgr.DataChange(dynKey);

            _mgr.GetRedDotStatus(dynKey, 0, out var status, out var num);
            Assert.AreEqual(RedDotStatus.RedDotStatusBlueNumber, status);
            Assert.AreEqual(4, num);
        }

        [Test]
        public void DynamicNode_SetDynamicParent_BeforeConfigLoad_Replayed()
        {
            // P0-2:父顶点未就绪(配置未加载)登记重放表,不丢边;
            // 配置加载完成后模块调 ReplayDynamicParents 补挂(此处手动调用模拟该时序)
            RedDotManager.Ins().Reset();
            _mgr = RedDotManager.Ins();
            _mgr.RegisterDynamicNode("act:1", out var dynKey);
            _mgr.SetDynamicParent(dynKey, "Mail");
            BindRedDotChecker(dynKey);

            // 此时 Mail 尚未入图,边留在重放表;配置加载后重放补挂
            Assert.IsTrue(_mgr.AddJsonStr(GraphJson, checkVisitor: false));
            _mgr.ReplayDynamicParents();

            _mgr.DataChange(dynKey);
            _mgr.GetRedDotStatus("Mail", 0, out var status, out _);
            Assert.AreEqual(RedDotStatus.RedDotStatusRedDot, status);
            _mgr.GetRedDotStatus("Root", 0, out status, out _);
            Assert.AreEqual(RedDotStatus.RedDotStatusRedDot, status);
        }

        [Test]
        public void DynamicNode_RemoveDynamicNode_ParentRecomputed()
        {
            _mgr.RegisterDynamicNode("act:1", out var dynKey);
            _mgr.SetDynamicParent(dynKey, "Mail");
            BindRedDotChecker(dynKey);
            _mgr.DataChange(dynKey);
            _mgr.GetRedDotStatus("Mail", 0, out var status, out _);
            Assert.AreEqual(RedDotStatus.RedDotStatusRedDot, status);

            // 删除动态节点:摘边/摘 checker,父链补发重算熄灭
            _mgr.RemoveDynamicNode("act:1");
            _mgr.GetRedDotStatus("Mail", 0, out status, out _);
            Assert.AreEqual(RedDotStatus.RedDotStatusNull, status);
            _mgr.GetRedDotStatus("Root", 0, out status, out _);
            Assert.AreEqual(RedDotStatus.RedDotStatusNull, status);

            // 同 ownerKey 重新注册=全新节点(分配表已清,重放表条目已摘)
            _mgr.RegisterDynamicNode("act:1", out dynKey);
            BindRedDotChecker(dynKey);
            _mgr.DataChange(dynKey);
            _mgr.GetRedDotStatus(dynKey, 0, out status, out _);
            Assert.AreEqual(RedDotStatus.RedDotStatusRedDot, status);
        }

        [Test]
        public void DynamicNode_RemoveDynamicParent_ParentRecomputed()
        {
            _mgr.RegisterDynamicNode("act:1", out var dynKey);
            _mgr.SetDynamicParent(dynKey, "Mail");
            BindRedDotChecker(dynKey);
            _mgr.DataChange(dynKey);
            _mgr.GetRedDotStatus("Mail", 0, out var status, out _);
            Assert.AreEqual(RedDotStatus.RedDotStatusRedDot, status);

            // 摘父边:父链重算熄灭,动态节点自身保留
            _mgr.RemoveDynamicParent(dynKey, "Mail");
            _mgr.GetRedDotStatus("Mail", 0, out status, out _);
            Assert.AreEqual(RedDotStatus.RedDotStatusNull, status);
            _mgr.GetRedDotStatus(dynKey, 0, out status, out _);
            Assert.AreEqual(RedDotStatus.RedDotStatusRedDot, status);
        }

        [Test]
        public void DynamicNode_Disabled_SwallowsDataChange()
        {
            _mgr.RegisterDynamicNode("act:1", out var dynKey);
            _mgr.SetDynamicParent(dynKey, "Mail");
            BindRedDotChecker(dynKey);
            _mgr.SetNodeEnabled(dynKey, false);

            _mgr.DataChange(dynKey);
            _mgr.GetRedDotStatus(dynKey, 0, out var status, out _);
            Assert.AreEqual(RedDotStatus.RedDotStatusNull, status);
            _mgr.GetRedDotStatus("Mail", 0, out status, out _);
            Assert.AreEqual(RedDotStatus.RedDotStatusNull, status);
        }
    }
}
